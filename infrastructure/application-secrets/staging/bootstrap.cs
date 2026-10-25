#:package DRN.Framework.Utils@0.10.1-preview005
#:property PublishAot=false

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

namespace DRN.GitOps.Bootstraps.Application;

/// <summary>Creates staging application settings once and validates existing Secrets without rotating credentials.</summary>
static class Bootstrap
{
#if !BOOTSTRAP_TESTS
    public static async Task<int> Main()
    {
        try
        {
            await RunAsync();
            Console.WriteLine("Application settings are ready. Existing Secrets were preserved.");
            return 0;
        }
        catch (BootstrapException error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
        catch (Exception)
        {
            // Framework exceptions can contain request or response details.
            Console.Error.WriteLine("Application Secret bootstrap failed. Check API access, TLS trust and mounted files.");
            return 1;
        }
    }
#endif

    // Clone DRN conventions so Kubernetes null omission does not change global framework defaults.
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonConventions.DefaultOptions)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Kubernetes projects these authentication files into the Pod, independently of /bootstrap.
    private const string ServiceAccount = "/var/run/secrets/kubernetes.io/serviceaccount";
    private const string KubernetesApiAddress = "https://kubernetes.default.svc";
    private const string NexusApplication = "nexus";
    private const string SampleApplication = "sample";
    private const string SettingsSuffix = "-settings";
    private const string InitializationMarker = "application-secret-bootstrap-initialized";
    // Add future applications here and grant named Secret reads in the bootstrap Role.
    private static readonly string[] Applications = [NexusApplication, SampleApplication];
    private const string AllowedHosts = "AllowedHosts";
    private const string SeedKey = "DrnAppFeatures__SeedKey";
    private const string KeyMaterial = "NexusAppSettings__Keys__0__KeyMaterial";
    private const string KeyFormat = "NexusAppSettings__Keys__0__Format";
    private const string KeyDefault = "NexusAppSettings__Keys__0__Default";

    /// <summary>Loads the mounted cluster CA and runs with the pod's service account identity.</summary>
    public static async Task RunAsync()
    {
        var roots = new X509Certificate2Collection();
        try
        {
            roots.ImportFromPem(await File.ReadAllTextAsync($"{ServiceAccount}/ca.crt"));
            if (roots.Count == 0)
                throw new BootstrapException("The mounted Kubernetes CA contains no certificates.");
            await RunWithTrustAsync(roots);
        }
        finally
        {
            foreach (var root in roots)
                root.Dispose();
        }
    }

    /// <summary>Connects to the Kubernetes API using only cluster trust and normal hostname verification.</summary>
    private static async Task RunWithTrustAsync(X509Certificate2Collection roots)
    {
        var namespaceName = (await File.ReadAllTextAsync($"{ServiceAccount}/namespace")).Trim();
        var chainPolicy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true,
            ApplicationPolicy = { new Oid("1.3.6.1.5.5.7.3.1") }
        };
        chainPolicy.CustomTrustStore.AddRange(roots);
        using var handler = new SocketsHttpHandler();
        handler.AllowAutoRedirect = false;
        handler.UseProxy = false;
        handler.SslOptions = new SslClientAuthenticationOptions
        {
            // Keep normal hostname verification. Trust only the mounted cluster CA.
            CertificateChainPolicy = chainPolicy
        };
        using var client = new HttpClient(handler);
        client.BaseAddress = new Uri(KubernetesApiAddress);
        client.Timeout = TimeSpan.FromSeconds(30);

        await RunAsync(client, namespaceName, () => File.ReadAllText($"{ServiceAccount}/token").Trim(),
            allowCredentialGeneration: Environment.GetEnvironmentVariable("ALLOW_CREDENTIAL_GENERATION") == "true");
    }

    /// <summary>Preserves existing settings and copies Nexus credentials into missing application Secrets.</summary>
    internal static async Task RunAsync(HttpClient client, string namespaceName, Func<string> readToken,
        IReadOnlyList<string>? applications = null, bool allowCredentialGeneration = false)
    {
        var secrets = $"/api/v1/namespaces/{namespaceName}/secrets";
        var names = new[] { NexusApplication }.Concat(applications ?? Applications).Distinct(StringComparer.Ordinal).ToArray();
        var existing = new Dictionary<string, BootstrapSecret?>();
        Dictionary<string, string>? shared = null;
        // Read and validate every survivor before writing. Nexus is always the preferred source.
        foreach (var name in names)
        {
            var secret = await RequestAsync(client, readToken, HttpMethod.Get, $"{secrets}/{name}{SettingsSuffix}");
            existing.Add(name, secret);
            if (secret is null) continue;
            var settings = Settings(secret, name + SettingsSuffix);
            shared ??= settings;
            ValidateSharedKey(shared, settings);
        }

        // Persist the claim before any credential writes. Only its creator may initialize automatically.
        var claimedInitialization = await ClaimInitializationAsync(client, readToken, namespaceName);
        if (shared is null && !claimedInitialization && !allowCredentialGeneration)
            throw new BootstrapException("Initialization was already claimed but no settings Secret survives. Restore the lost settings Secrets. " +
                "To create new credentials deliberately, set ALLOW_CREDENTIAL_GENERATION=true for this run only.");

        shared ??= new Dictionary<string, string>
        {
            // DRN SecureKey: 99 safe characters retaining all 256 random bits.
            [SeedKey] = "Aa0!" + string.Join("-", RandomNumberGenerator.GetBytes(32).Select(value => value.ToString("x2"))),
            [KeyMaterial] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            [KeyFormat] = "Base64",
            [KeyDefault] = "true"
        };
        foreach (var name in names)
        {
            if (existing[name] is not null) continue;
            var created = await CreateAsync(client, readToken, secrets, namespaceName, name, shared);
            var settings = Settings(created, name + SettingsSuffix);
            if (name == NexusApplication)
            {
                // A competing Nexus creator wins. Check survivors before copying its stored credentials.
                foreach (var survivor in existing.Where(pair => pair.Value is not null))
                    ValidateSharedKey(settings, Settings(survivor.Value, survivor.Key + SettingsSuffix));
                shared = settings;
            }
            ValidateSharedKey(shared, settings);
        }
    }

    private static async Task<bool> ClaimInitializationAsync(HttpClient client, Func<string> readToken, string namespaceName)
    {
        var path = $"/api/v1/namespaces/{namespaceName}/configmaps";
        if (await RequestMarkerAsync(HttpMethod.Get, $"{path}/{InitializationMarker}") is not null)
            return false;

        var marker = new BootstrapInitializationMarker
        {
            ApiVersion = "v1",
            Kind = "ConfigMap",
            Metadata = new BootstrapMetadata { Name = InitializationMarker, Namespace = namespaceName },
            Immutable = true,
            Data = new Dictionary<string, string?> { ["version"] = "1" }
        };
        // A conflict is not ownership. Never turn an uncertain POST result into generation permission.
        if (await RequestMarkerAsync(HttpMethod.Post, path, marker) is not null)
            return true;
        if (await RequestMarkerAsync(HttpMethod.Get, $"{path}/{InitializationMarker}") is null)
            throw new BootstrapException("Initialization marker disappeared after a create conflict. Restore bootstrap state before retrying.");
        return false;

        async Task<BootstrapInitializationMarker?> RequestMarkerAsync(
            HttpMethod method, string requestPath, BootstrapInitializationMarker? body = null)
        {
            using var request = new HttpRequestMessage(method, requestPath);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", readToken());
            if (body is not null)
                request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request);
            if (method == HttpMethod.Get && response.StatusCode == HttpStatusCode.NotFound ||
                method == HttpMethod.Post && response.StatusCode == HttpStatusCode.Conflict)
                return null;
            var expectedStatus = method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK;
            if (response.StatusCode != expectedStatus)
                throw new BootstrapException($"Initialization marker API {method} failed with HTTP {(int)response.StatusCode}.");

            BootstrapInitializationMarker? stored;
            try
            {
                stored = JsonSerializer.Deserialize<BootstrapInitializationMarker>(await response.Content.ReadAsStringAsync(), JsonOptions);
            }
            catch (JsonException)
            {
                throw new BootstrapException("Initialization marker API returned invalid JSON.");
            }
            if (stored?.ApiVersion != "v1" || stored.Kind != "ConfigMap" ||
                stored.Metadata?.Name != InitializationMarker || stored.Metadata.Namespace != namespaceName ||
                stored.Immutable != true || stored.Data is not { Count: 1 } ||
                !stored.Data.TryGetValue("version", out var version) || version != "1")
                throw new BootstrapException("Initialization marker is invalid. Restore bootstrap state before retrying.");
            return stored;
        }
    }

    private static void ValidateSharedKey(Dictionary<string, string> expected, Dictionary<string, string> actual)
    {
        // Older installations used independent seeds. Preserve them rather than rotating stored credentials.
        if (new[] { KeyMaterial, KeyFormat, KeyDefault }.Any(key => expected[key] != actual[key]))
            throw new BootstrapException("Nexus key settings differ between applications. Reconcile them deliberately. Bootstrap will not overwrite them.");
    }

    /// <summary>Sends an authenticated API request, handling absent resources and competing Secret creation safely.</summary>
    private static async Task<BootstrapSecret?> RequestAsync(
        HttpClient client, Func<string> readToken, HttpMethod method, string path, BootstrapSecret? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        // Projected service account tokens can rotate during the Job.
        var token = readToken();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);
        if (method == HttpMethod.Get && response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (method == HttpMethod.Post && response.StatusCode == HttpStatusCode.Conflict)
        {
            // The stored winner is authoritative, not the credentials generated for the rejected POST.
            var name = body!.Metadata!.Name;
            return await RequestAsync(client, readToken, HttpMethod.Get, $"{path}/{name}");
        }
        if (!response.IsSuccessStatusCode)
            throw new BootstrapException($"Kubernetes API {method} failed with HTTP {(int)response.StatusCode}.");

        try
        {
            return JsonSerializer.Deserialize<BootstrapSecret>(await response.Content.ReadAsStringAsync(), JsonOptions)
                ?? throw new BootstrapException("Kubernetes API returned an invalid object.");
        }
        catch (JsonException)
        {
            // Serializer diagnostics can reveal payload details. Return only a fixed safe message.
            throw new BootstrapException("Kubernetes API returned invalid JSON.");
        }
    }

    /// <summary>Reads the required application settings from a surviving Secret.</summary>
    private static Dictionary<string, string> Settings(BootstrapSecret? secret, string name)
    {
        if (secret is null)
            throw new BootstrapException("Secret disappeared during bootstrap. Restore the settings pair.");

        return new[] { AllowedHosts, SeedKey, KeyMaterial, KeyFormat, KeyDefault }
            .ToDictionary(key => key, key => Value(secret, name, key), StringComparer.Ordinal);
    }

    /// <summary>Decodes a required field as canonical Base64 and strict UTF-8, rejecting empty settings.</summary>
    private static string Value(BootstrapSecret secret, string name, string key)
    {
        if (secret.Data is not { } data || !data.TryGetValue(key, out var encoded) || string.IsNullOrEmpty(encoded))
            throw new BootstrapException($"Secret {name} is missing {key}.");

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(encoded);
            if (Convert.ToBase64String(decoded) != encoded)
                throw new FormatException();
        }
        catch (FormatException)
        {
            throw new BootstrapException($"Secret {name} has invalid base64 for {key}.");
        }

        string value;
        try
        {
            value = new UTF8Encoding(false, true).GetString(decoded);
        }
        catch (DecoderFallbackException)
        {
            throw new BootstrapException($"Secret {name} has invalid UTF-8 for {key}.");
        }

        return string.IsNullOrWhiteSpace(value)
            ? throw new BootstrapException($"Secret {name} has empty {key}.")
            : value;
    }

    /// <summary>Creates an unowned settings Secret with shared credentials and application-specific hosts.</summary>
    private static Task<BootstrapSecret?> CreateAsync(
        HttpClient client, Func<string> readToken, string secrets, string namespaceName, string name, Dictionary<string, string> key)
    {
        // POST JSON over HTTPS to create the API resource. No local Secret files or kubectl are involved.
        return RequestAsync(client, readToken, HttpMethod.Post, secrets, new BootstrapSecret
        {
            ApiVersion = "v1",
            Kind = "Secret",
            Type = "Opaque",
            Metadata = new BootstrapMetadata { Name = name + SettingsSuffix, Namespace = namespaceName },
            // Send text values as stringData. API responses expose them as base64-encoded data.
            StringData = new Dictionary<string, string?>
            {
                [AllowedHosts] = $"{name};{name}.{namespaceName};{name}.{namespaceName}.svc;{name}.{namespaceName}.svc.cluster.local;localhost;127.0.0.1",
                [SeedKey] = key[SeedKey],
                [KeyMaterial] = key[KeyMaterial],
                [KeyFormat] = key[KeyFormat],
                [KeyDefault] = key[KeyDefault]
            }
        });
    }
}

/// <summary>Kubernetes Secret fields used by this standalone bootstrap. Data keys remain application-defined.</summary>
sealed class BootstrapSecret
{
    public string? ApiVersion { get; set; }
    public string? Kind { get; set; }
    public string? Type { get; set; }
    public BootstrapMetadata? Metadata { get; set; }
    public Dictionary<string, string?>? Data { get; set; }
    public Dictionary<string, string?>? StringData { get; set; }
}

/// <summary>Resource identity only. Generated Secrets and the marker have no owner or Argo CD tracking metadata.</summary>
sealed class BootstrapMetadata
{
    public string? Name { get; set; }
    public string? Namespace { get; set; }
}

/// <summary>Non-secret, immutable evidence that initialization has been claimed, retained independently of Jobs.</summary>
sealed class BootstrapInitializationMarker
{
    public string? ApiVersion { get; set; }
    public string? Kind { get; set; }
    public BootstrapMetadata? Metadata { get; set; }
    public bool? Immutable { get; set; }
    public Dictionary<string, string?>? Data { get; set; }
}

/// <summary>A bootstrap failure whose message is safe to write to Job logs.</summary>
public sealed class BootstrapException(string message) : Exception(message);
