#:package DRN.Framework.Utils@0.10.1-preview003
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

    private const string ServiceAccount = "/var/run/secrets/kubernetes.io/serviceaccount";
    private const string KubernetesApiAddress = "https://kubernetes.default.svc";
    private const string NexusApplication = "nexus";
    private const string SampleApplication = "sample";
    private const string SettingsSuffix = "-settings";
    private const string NexusSettingsSecret = NexusApplication + SettingsSuffix;
    private const string SampleSettingsSecret = SampleApplication + SettingsSuffix;
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

        await RunAsync(client, namespaceName, () => File.ReadAllText($"{ServiceAccount}/token").Trim());
    }

    /// <summary>Creates a fresh settings pair or checks that both surviving Secrets contain the same Nexus key.</summary>
    /// <remarks>Partial pairs and missing settings beside existing Deployments require deliberate recovery.</remarks>
    internal static async Task RunAsync(HttpClient client, string namespaceName, Func<string> readToken)
    {
        var secrets = $"/api/v1/namespaces/{namespaceName}/secrets";
        var deployments = $"/apis/apps/v1/namespaces/{namespaceName}/deployments";
        var nexus = await RequestAsync(client, readToken, HttpMethod.Get, $"{secrets}/{NexusSettingsSecret}");
        var sample = await RequestAsync(client, readToken, HttpMethod.Get, $"{secrets}/{SampleSettingsSecret}");
        // A surviving Secret may protect existing data. Never infer that its missing peer is disposable.
        if ((nexus is null) != (sample is null))
            throw new BootstrapException("Only one settings Secret exists. Restore or deliberately complete the pair. Refusing seed/key regeneration.");

        if (nexus is null)
        {
            // Existing workloads may depend on the lost credentials. Their settings must be restored.
            if (await RequestAsync(client, readToken, HttpMethod.Get, $"{deployments}/{NexusApplication}") is not null ||
                await RequestAsync(client, readToken, HttpMethod.Get, $"{deployments}/{SampleApplication}") is not null)
                throw new BootstrapException("Application workloads already exist. Restore their settings Secrets from backup.");

            nexus = await CreateAsync(client, readToken, secrets, namespaceName, NexusApplication, new Dictionary<string, string>
            {
                [KeyMaterial] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                [KeyFormat] = "Base64",
                [KeyDefault] = "true"
            });
            // Use the winning Nexus Secret if another bootstrap created it first.
            sample = await CreateAsync(client, readToken, secrets, namespaceName, SampleApplication, Settings(nexus, NexusSettingsSecret));
        }

        var nexusSettings = Settings(nexus, NexusSettingsSecret);
        var sampleSettings = Settings(sample, SampleSettingsSecret);
        if (new[] { KeyMaterial, KeyFormat, KeyDefault }.Any(key => nexusSettings[key] != sampleSettings[key]))
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

    /// <summary>Creates an unowned settings Secret with a new application seed and the supplied shared Nexus key.</summary>
    private static Task<BootstrapSecret?> CreateAsync(
        HttpClient client, Func<string> readToken, string secrets, string namespaceName, string name, Dictionary<string, string> key)
    {
        // DRN SecureKey: 99 safe characters. Separators retain all 256 random bits.
        var seed = "Aa0!" + string.Join("-", RandomNumberGenerator.GetBytes(32).Select(value => value.ToString("x2")));
        return RequestAsync(client, readToken, HttpMethod.Post, secrets, new BootstrapSecret
        {
            ApiVersion = "v1",
            Kind = "Secret",
            Type = "Opaque",
            Metadata = new BootstrapMetadata { Name = name + SettingsSuffix, Namespace = namespaceName },
            StringData = new Dictionary<string, string?>
            {
                [AllowedHosts] = $"{name};{name}.{namespaceName};{name}.{namespaceName}.svc;{name}.{namespaceName}.svc.cluster.local;localhost;127.0.0.1",
                [SeedKey] = seed,
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

/// <summary>Secret identity only. Generated credentials have no owner or Argo CD tracking metadata.</summary>
sealed class BootstrapMetadata
{
    public string? Name { get; set; }
    public string? Namespace { get; set; }
}

/// <summary>A bootstrap failure whose message is safe to write to Job logs.</summary>
public sealed class BootstrapException(string message) : Exception(message);
