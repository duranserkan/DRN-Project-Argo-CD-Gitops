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
using System.Text.Json.Nodes;
using System.Threading.Tasks;

#if !BOOTSTRAP_TESTS
try
{
    await Bootstrap.RunAsync();
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
#endif

static class Bootstrap
{
    private const string ServiceAccount = "/var/run/secrets/kubernetes.io/serviceaccount";
    private const string SeedKey = "DrnAppFeatures__SeedKey";
    private const string KeyMaterial = "NexusAppSettings__Keys__0__KeyMaterial";
    private const string KeyFormat = "NexusAppSettings__Keys__0__Format";
    private const string KeyDefault = "NexusAppSettings__Keys__0__Default";

    public static async Task RunAsync()
    {
        var roots = new X509Certificate2Collection();
        try
        {
            roots.ImportFromPem(File.ReadAllText($"{ServiceAccount}/ca.crt"));
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

    private static async Task RunWithTrustAsync(X509Certificate2Collection roots)
    {
        var namespaceName = File.ReadAllText($"{ServiceAccount}/namespace").Trim();
        var chainPolicy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true,
            ApplicationPolicy = { new Oid("1.3.6.1.5.5.7.3.1") }
        };
        chainPolicy.CustomTrustStore.AddRange(roots);
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            SslOptions = new SslClientAuthenticationOptions
            {
                // Keep normal hostname verification. Trust only the mounted cluster CA.
                CertificateChainPolicy = chainPolicy
            }
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://kubernetes.default.svc"),
            Timeout = TimeSpan.FromSeconds(30)
        };

        await RunAsync(client, namespaceName, () => File.ReadAllText($"{ServiceAccount}/token").Trim());
    }

    internal static async Task RunAsync(HttpClient client, string namespaceName, Func<string> readToken)
    {
        var secrets = $"/api/v1/namespaces/{namespaceName}/secrets";
        var deployments = $"/apis/apps/v1/namespaces/{namespaceName}/deployments";
        var nexus = await RequestAsync(client, readToken, HttpMethod.Get, $"{secrets}/nexus-settings");
        var sample = await RequestAsync(client, readToken, HttpMethod.Get, $"{secrets}/sample-settings");
        if ((nexus is null) != (sample is null))
            throw new BootstrapException("Only one settings Secret exists. Restore or deliberately complete the pair. Refusing seed/key regeneration.");

        if (nexus is null)
        {
            foreach (var name in new[] { "nexus", "sample" })
                if (await RequestAsync(client, readToken, HttpMethod.Get, $"{deployments}/{name}") is not null)
                    throw new BootstrapException("Application workloads already exist. Restore their settings Secrets from backup.");

            nexus = await CreateAsync(client, readToken, secrets, namespaceName, "nexus", new Dictionary<string, string>
            {
                [KeyMaterial] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                [KeyFormat] = "Base64",
                [KeyDefault] = "true"
            });
            // Use the winning Nexus Secret if another bootstrap created it first.
            sample = await CreateAsync(client, readToken, secrets, namespaceName, "sample", Settings(nexus, "nexus-settings"));
        }

        var nexusSettings = Settings(nexus, "nexus-settings");
        var sampleSettings = Settings(sample, "sample-settings");
        foreach (var key in new[] { KeyMaterial, KeyFormat, KeyDefault })
            if (nexusSettings[key] != sampleSettings[key])
                throw new BootstrapException("Nexus key settings differ between applications. Reconcile them deliberately. Bootstrap will not overwrite them.");
    }

    private static async Task<JsonObject?> RequestAsync(
        HttpClient client, Func<string> readToken, HttpMethod method, string path, JsonObject? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        var token = readToken();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);
        if (method == HttpMethod.Get && response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (method == HttpMethod.Post && response.StatusCode == HttpStatusCode.Conflict)
        {
            var name = body!["metadata"]!["name"]!.GetValue<string>();
            return await RequestAsync(client, readToken, HttpMethod.Get, $"{path}/{name}");
        }
        if (!response.IsSuccessStatusCode)
            throw new BootstrapException($"Kubernetes API {method} failed with HTTP {(int)response.StatusCode}.");

        try
        {
            return JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonObject
                ?? throw new BootstrapException("Kubernetes API returned an invalid object.");
        }
        catch (JsonException)
        {
            throw new BootstrapException("Kubernetes API returned invalid JSON.");
        }
    }

    private static Dictionary<string, string> Settings(JsonObject? secret, string name)
    {
        if (secret is null)
            throw new BootstrapException("Secret disappeared during bootstrap. Restore the settings pair.");

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var data = secret["data"] as JsonObject;
        foreach (var key in new[] { "AllowedHosts", SeedKey, KeyMaterial, KeyFormat, KeyDefault })
        {
            if (data?[key] is not JsonValue value || !value.TryGetValue<string>(out var encoded) || string.IsNullOrEmpty(encoded))
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

            try
            {
                values[key] = new UTF8Encoding(false, true).GetString(decoded);
            }
            catch (DecoderFallbackException)
            {
                throw new BootstrapException($"Secret {name} has invalid UTF-8 for {key}.");
            }
            if (string.IsNullOrWhiteSpace(values[key]))
                throw new BootstrapException($"Secret {name} has empty {key}.");
        }
        return values;
    }

    private static Task<JsonObject?> CreateAsync(
        HttpClient client, Func<string> readToken, string secrets, string namespaceName, string name, Dictionary<string, string> key)
    {
        // DRN SecureKey: 99 safe characters. Separators retain all 256 random bits.
        var seed = "Aa0!" + string.Join("-", RandomNumberGenerator.GetBytes(32).Select(value => value.ToString("x2")));
        return RequestAsync(client, readToken, HttpMethod.Post, secrets, new JsonObject
        {
            ["apiVersion"] = "v1",
            ["kind"] = "Secret",
            ["type"] = "Opaque",
            ["metadata"] = new JsonObject { ["name"] = $"{name}-settings", ["namespace"] = namespaceName },
            ["stringData"] = new JsonObject
            {
                ["AllowedHosts"] = $"{name};{name}.{namespaceName};{name}.{namespaceName}.svc;{name}.{namespaceName}.svc.cluster.local;localhost;127.0.0.1",
                [SeedKey] = seed,
                [KeyMaterial] = key[KeyMaterial],
                [KeyFormat] = key[KeyFormat],
                [KeyDefault] = key[KeyDefault]
            }
        });
    }
}

sealed class BootstrapException(string message) : Exception(message);
