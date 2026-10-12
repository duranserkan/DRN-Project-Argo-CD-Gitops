using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

#if !BOOTSTRAP_TESTS
try
{
    await GraylogBootstrap.RunAsync();
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
#endif

static class GraylogBootstrap
{
    private const string ServiceAccount = "/var/run/secrets/kubernetes.io/serviceaccount";

    public static async Task RunAsync()
    {
        var roots = new X509Certificate2Collection();
        try
        {
            roots.ImportFromPem(File.ReadAllText($"{ServiceAccount}/ca.crt"));
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
            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                // Default hostname verification remains enabled.
                SslOptions = new SslClientAuthenticationOptions { CertificateChainPolicy = policy }
            };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://kubernetes.default.svc"),
                Timeout = TimeSpan.FromSeconds(30)
            };
            await RunAsync(client, File.ReadAllText($"{ServiceAccount}/namespace").Trim(),
                () => File.ReadAllText($"{ServiceAccount}/token").Trim(), TimeProvider.System,
                delay => Task.Delay(delay));
        }
        finally
        {
            foreach (var root in roots)
                root.Dispose();
        }
    }

    internal static async Task RunAsync(HttpClient client, string namespaceName, Func<string> readToken,
        TimeProvider clock, Func<TimeSpan, Task> delay)
    {
        var path = $"/api/v1/namespaces/{namespaceName}/secrets";
        Task<JsonObject?> Get(string name) => RequestAsync(client, readToken, HttpMethod.Get, $"{path}/{name}");
        async Task<JsonObject> Ensure(string name, JsonObject values)
        {
            var existing = await Get(name);
            if (existing is not null)
                return existing;
            return await RequestAsync(client, readToken, HttpMethod.Post, path, new JsonObject
            {
                ["apiVersion"] = "v1", ["kind"] = "Secret", ["type"] = "Opaque",
                ["metadata"] = new JsonObject { ["name"] = name, ["namespace"] = namespaceName },
                ["stringData"] = values
            }) ?? throw new GraylogBootstrapException("Secret disappeared during bootstrap. Restore credentials before retrying.");
        }

        var credentials = await Get("graylog-credentials");
        var database = await Get("mongo-graylog-password");
        if (credentials is null)
        {
            var admin = await Get("graylog-admin-password");
            var connection = await Get("mongodb-graylog-graylog");
            // An interrupted first bootstrap and lost credentials are indistinguishable.
            if (database is not null || admin is not null || connection is not null)
                throw new GraylogBootstrapException("Existing bootstrap Secrets found without graylog-credentials. Restore graylog-credentials from backup. Refusing encryption secret regeneration, including after an interrupted first bootstrap.");
        }
        if (credentials is not null && database is null)
            throw new GraylogBootstrapException("Restore mongo-graylog-password from backup. Refusing password regeneration.");

        database ??= await Ensure("mongo-graylog-password", new JsonObject { ["password"] = RandomPassword(48) });
        Value(database, "mongo-graylog-password", "password");
        if (credentials is null)
        {
            var admin = await Ensure("graylog-admin-password", new JsonObject { ["password"] = RandomPassword(32) });
            var password = Value(admin, "graylog-admin-password", "password");
            var start = clock.GetTimestamp();
            string? uri = null;
            Console.WriteLine("Waiting for the MongoDB operator connection Secret.");
            while (clock.GetElapsedTime(start) < TimeSpan.FromMinutes(15))
            {
                var connection = await Get("mongodb-graylog-graylog");
                var encodedUri = (connection?["data"] as JsonObject)?["connectionString.standard"];
                // The operator may publish the Secret before populating its URI.
                if (encodedUri is not null && !(encodedUri is JsonValue value &&
                    value.TryGetValue<string>(out var text) && string.IsNullOrEmpty(text)))
                {
                    uri = Value(connection, "mongodb-graylog-graylog", "connectionString.standard");
                    break;
                }
                await delay(TimeSpan.FromSeconds(5));
            }
            if (uri is null)
                throw new GraylogBootstrapException("Timed out waiting for the MongoDB connection URI.");
            credentials = await Ensure("graylog-credentials", new JsonObject
            {
                ["GRAYLOG_ROOT_USERNAME"] = "admin",
                ["GRAYLOG_ROOT_PASSWORD_SHA2"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant(),
                ["GRAYLOG_PASSWORD_SECRET"] = RandomPassword(48),
                ["GRAYLOG_MONGODB_URI"] = uri
            });
        }
        foreach (var key in new[] { "GRAYLOG_ROOT_USERNAME", "GRAYLOG_ROOT_PASSWORD_SHA2", "GRAYLOG_PASSWORD_SECRET", "GRAYLOG_MONGODB_URI" })
            Value(credentials, "graylog-credentials", key);
    }

    private static string RandomPassword(int bytes) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Value(JsonObject secret, string name, string key)
    {
        if (secret["data"] is not JsonObject data || data[key] is not JsonValue value ||
            !value.TryGetValue<string>(out var encoded) || string.IsNullOrEmpty(encoded))
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

    private static async Task<JsonObject?> RequestAsync(HttpClient client, Func<string> readToken,
        HttpMethod method, string path, JsonObject? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", readToken());
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        if (method == HttpMethod.Get && response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (method == HttpMethod.Post && response.StatusCode == HttpStatusCode.Conflict)
            return await RequestAsync(client, readToken, HttpMethod.Get, $"{path}/{body!["metadata"]!["name"]!.GetValue<string>()}");
        if (!response.IsSuccessStatusCode)
            throw new GraylogBootstrapException($"Secret API {method} failed with HTTP {(int)response.StatusCode}.");
        try
        {
            return JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonObject
                ?? throw new GraylogBootstrapException("Secret API returned an invalid object.");
        }
        catch (JsonException)
        {
            throw new GraylogBootstrapException("Secret API returned invalid JSON.");
        }
    }
}

sealed class GraylogBootstrapException(string message) : Exception(message);
