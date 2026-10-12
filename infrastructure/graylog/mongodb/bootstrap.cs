#:package DRN.Framework.Utils@0.10.1-preview003
#:property PublishAot=false

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DRN.Framework.SharedKernel.Json;
using DRN.Framework.Utils.Data.Encodings;

namespace DRN.GitOps.Bootstraps.Graylog;

/// <summary>Bootstraps Graylog credentials and preserves existing database and encryption secrets.</summary>
static class GraylogBootstrap
{
#if !BOOTSTRAP_TESTS
    public static async Task<int> Main()
    {
        try
        {
            await RunAsync();
            Console.WriteLine("Credentials are ready. Existing Secrets were preserved.");
            return 0;
        }
        catch (GraylogBootstrapException error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("Graylog bootstrap failed. Check API access, TLS trust and mounted files.");
            return 1;
        }
    }
#endif

    // Clone DRN conventions so Kubernetes null omission does not change global framework defaults.
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonConventions.DefaultOptions)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string ServiceAccount = "/var/run/secrets/kubernetes.io/serviceaccount";
    private const string KubernetesApiAddress = "https://kubernetes.default.svc";
    private const string CredentialsSecret = "graylog-credentials";
    private const string DatabasePasswordSecret = "mongo-graylog-password";
    private const string AdminPasswordSecret = "graylog-admin-password";
    // Public Kubernetes resource name. The operator supplies the credential-bearing URI in its data.
    private const string OperatorConnectionResourceName = "mongodb-graylog-graylog";
    private const string PasswordKey = "password";
    private const string ConnectionStringKey = "connectionString.standard";
    private const string RootUsernameKey = "GRAYLOG_ROOT_USERNAME";
    private const string RootPasswordHashKey = "GRAYLOG_ROOT_PASSWORD_SHA2";
    private const string PasswordSecretKey = "GRAYLOG_PASSWORD_SECRET";
    private const string MongoDbUriKey = "GRAYLOG_MONGODB_URI";

    /// <summary>Connects with the mounted service account token, cluster CA bundle and hostname verification.</summary>
    public static async Task RunAsync()
    {
        var roots = new X509Certificate2Collection();
        try
        {
            roots.ImportFromPem(await File.ReadAllTextAsync($"{ServiceAccount}/ca.crt"));
            if (roots.Count == 0)
                throw new GraylogBootstrapException("The mounted Kubernetes CA contains no certificates.");
            var policy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                RevocationMode = X509RevocationMode.NoCheck,
                DisableCertificateDownloads = true,
                ApplicationPolicy = { new Oid("1.3.6.1.5.5.7.3.1") }
            };
            policy.CustomTrustStore.AddRange(roots);
            using var handler = new SocketsHttpHandler();
            handler.AllowAutoRedirect = false;
            handler.UseProxy = false; // Default hostname verification remains enabled.
            handler.SslOptions = new SslClientAuthenticationOptions { CertificateChainPolicy = policy };
            using var client = new HttpClient(handler);
            client.BaseAddress = new Uri(KubernetesApiAddress);
            client.Timeout = TimeSpan.FromSeconds(30);
            await RunAsync(client, (await File.ReadAllTextAsync($"{ServiceAccount}/namespace")).Trim(),
                () => File.ReadAllText($"{ServiceAccount}/token").Trim(), TimeProvider.System,
                delay => Task.Delay(delay));
        }
        finally
        {
            foreach (var root in roots)
                root.Dispose();
        }
    }

    /// <summary>Creates first-install passwords, waits for MongoDB's URI and provisions Graylog's final Secret.</summary>
    /// <remarks>Existing credentials are authoritative. Incomplete sets require operator recovery rather than regeneration.</remarks>
    internal static async Task RunAsync(HttpClient client, string namespaceName, Func<string> readToken,
        TimeProvider clock, Func<TimeSpan, Task> delay)
    {
        var path = $"/api/v1/namespaces/{namespaceName}/secrets";
        Task<GraylogSecret?> Get(string name) => RequestAsync(client, readToken, HttpMethod.Get, $"{path}/{name}");
        async Task<GraylogSecret> Ensure(string name, Dictionary<string, string?> values)
        {
            // Preserve credentials created by a prior attempt or another bootstrap instance.
            var existing = await Get(name);
            if (existing is not null)
                return existing;
            return await RequestAsync(client, readToken, HttpMethod.Post, path, new GraylogSecret
            {
                ApiVersion = "v1", Kind = "Secret", Type = "Opaque",
                Metadata = new GraylogMetadata { Name = name, Namespace = namespaceName },
                StringData = values
            }) ?? throw new GraylogBootstrapException("Secret disappeared during bootstrap. Restore credentials before retrying.");
        }

        var credentials = await Get(CredentialsSecret);
        var database = await Get(DatabasePasswordSecret);
        if (credentials is null)
        {
            var admin = await Get(AdminPasswordSecret);
            var connection = await Get(OperatorConnectionResourceName);
            // An interrupted first bootstrap and lost credentials are indistinguishable.
            if (database is not null || admin is not null || connection is not null)
                throw new GraylogBootstrapException($"Existing bootstrap Secrets found without {CredentialsSecret}. Restore {CredentialsSecret} from backup. Refusing encryption secret regeneration, including after an interrupted first bootstrap.");
        }
        if (credentials is not null && database is null)
            throw new GraylogBootstrapException($"Restore {DatabasePasswordSecret} from backup. Refusing password regeneration.");

        database ??= await Ensure(DatabasePasswordSecret, new Dictionary<string, string?> { [PasswordKey] = RandomPassword(48) });
        Value(database, DatabasePasswordSecret, PasswordKey);
        if (credentials is null)
        {
            var admin = await Ensure(AdminPasswordSecret, new Dictionary<string, string?> { [PasswordKey] = RandomPassword(32) });
            var password = Value(admin, AdminPasswordSecret, PasswordKey);
            var uri = await WaitForConnectionUriAsync(Get, clock, delay);
            credentials = await Ensure(CredentialsSecret, new Dictionary<string, string?>
            {
                [RootUsernameKey] = "admin",
                [RootPasswordHashKey] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant(),
                [PasswordSecretKey] = RandomPassword(48),
                [MongoDbUriKey] = uri
            });
        }
        foreach (var key in new[] { RootUsernameKey, RootPasswordHashKey, PasswordSecretKey, MongoDbUriKey })
            Value(credentials, CredentialsSecret, key);
    }

    /// <summary>Waits for the operator URI, retrying transient reads within one monotonic deadline.</summary>
    private static async Task<string> WaitForConnectionUriAsync(Func<string, Task<GraylogSecret?>> get,
        TimeProvider clock, Func<TimeSpan, Task> delay)
    {
        // A monotonic deadline keeps wall-clock adjustments from extending the operator wait.
        var start = clock.GetTimestamp();
        Console.WriteLine("Waiting for the MongoDB operator connection Secret.");
        while (clock.GetElapsedTime(start) < TimeSpan.FromMinutes(15))
        {
            GraylogSecret? connection = null;
            try
            {
                connection = await get(OperatorConnectionResourceName);
            }
            catch (Exception error) when (
                error is OperationCanceledException or TimeoutException ||
                error is GraylogBootstrapException apiError && (int?)apiError.StatusCode is >= 500 and <= 599)
            {
                // Retry only this read within the original deadline. Authorization and validation failures stay fatal.
            }
            // The operator may publish the Secret before populating its URI.
            if (connection?.Data is not null &&
                connection.Data.TryGetValue(ConnectionStringKey, out var encodedUri) &&
                !string.IsNullOrEmpty(encodedUri))
                return Value(connection, OperatorConnectionResourceName, ConnectionStringKey);

            await delay(TimeSpan.FromSeconds(5));
        }
        throw new GraylogBootstrapException("Timed out waiting for the MongoDB connection URI.");
    }

    /// <summary>Encodes cryptographically random bytes using unpadded URL-safe Base64.</summary>
    private static string RandomPassword(int bytes) =>
        RandomNumberGenerator.GetBytes(bytes).Encode();

    /// <summary>Reads a required Secret field and rejects noncanonical Base64 or invalid UTF-8.</summary>
    private static string Value(GraylogSecret secret, string name, string key)
    {
        if (secret.Data is not { } data || !data.TryGetValue(key, out var encoded) || string.IsNullOrEmpty(encoded))
            throw new GraylogBootstrapException($"Secret {name} is missing {key}.");
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(encoded);
            if (Convert.ToBase64String(decoded) != encoded)
                throw new FormatException();
        }
        catch (FormatException)
        {
            throw new GraylogBootstrapException($"Secret {name} has invalid base64 for {key}.");
        }
        try
        {
            return new UTF8Encoding(false, true).GetString(decoded);
        }
        catch (DecoderFallbackException)
        {
            throw new GraylogBootstrapException($"Secret {name} has invalid UTF-8 for {key}.");
        }
    }

    /// <summary>Sends an authenticated request and reuses the stored Secret when another creator wins.</summary>
    private static async Task<GraylogSecret?> RequestAsync(HttpClient client, Func<string> readToken,
        HttpMethod method, string path, GraylogSecret? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        // The operator wait can outlast a projected token rotation. Read the token for every request.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", readToken());
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        if (method == HttpMethod.Get && response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (method == HttpMethod.Post && response.StatusCode == HttpStatusCode.Conflict)
            return await RequestAsync(client, readToken, HttpMethod.Get, $"{path}/{body!.Metadata!.Name}");
        if (!response.IsSuccessStatusCode)
            throw new GraylogBootstrapException($"Secret API {method} failed with HTTP {(int)response.StatusCode}.", response.StatusCode);
        try
        {
            return JsonSerializer.Deserialize<GraylogSecret>(await response.Content.ReadAsStringAsync(), JsonOptions)
                ?? throw new GraylogBootstrapException("Secret API returned an invalid object.");
        }
        catch (JsonException)
        {
            // Serializer diagnostics can reveal payload details. Return only a fixed safe message.
            throw new GraylogBootstrapException("Secret API returned invalid JSON.");
        }
    }
}

/// <summary>Kubernetes Secret fields used by this standalone bootstrap. Data keys remain application-defined.</summary>
sealed class GraylogSecret
{
    public string? ApiVersion { get; set; }
    public string? Kind { get; set; }
    public string? Type { get; set; }
    public GraylogMetadata? Metadata { get; set; }
    public Dictionary<string, string?>? Data { get; set; }
    public Dictionary<string, string?>? StringData { get; set; }
}

/// <summary>Secret identity without ownership or tracking metadata that could cause credential pruning.</summary>
sealed class GraylogMetadata
{
    public string? Name { get; set; }
    public string? Namespace { get; set; }
}

/// <summary>A bootstrap failure whose message is safe to write to Job logs.</summary>
public sealed class GraylogBootstrapException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    /// <summary>The API status, when available, for retry classification without inspecting log text.</summary>
    public HttpStatusCode? StatusCode { get; } = statusCode;
}
