using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using DRN.Framework.Testing.DataAttributes;
using Xunit;

namespace ChartValidation.Tests;

public class ChartTests
{
    private const string Revision = "0123456789abcdef0123456789abcdef01234567";
    private static readonly string[] Charts = ["deployment", "deployment/logging", "deployment/monitoring", "deployment/workloads"];
    private static readonly string Root = FindRoot();

    [Fact]
    public void ValidationCopiesMatchCanonicalRoot()
    {
        var canonical = Path.Combine(Root, "deployment/templates/validate.yaml");
        var expected = File.ReadAllBytes(canonical);
        foreach (var chart in Charts.Skip(1))
        {
            var copy = Path.Combine(Root, chart, "templates/validate.yaml");
            Assert.True(expected.SequenceEqual(File.ReadAllBytes(copy)),
                $"Validation drift: {copy} must match {canonical}.\n" +
                $"Expected:\n{File.ReadAllText(canonical)}\nActual:\n{File.ReadAllText(copy)}");
        }
    }

    [Theory]
    [DataMemberUnit(nameof(ConfigurationCases))]
    public async Task ConfigurationContract(string chart, string values, string? expectedError)
    {
        var result = await Render(chart, values);
        if (expectedError is null)
            Assert.True(result.ExitCode == 0, result.Error);
        else
        {
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(expectedError, result.Error);
        }
    }

    public static IEnumerable<object?[]> ConfigurationCases()
    {
        foreach (var chart in Charts)
        {
            yield return [chart, "{}", "environment must be staging or production"];
            foreach (var (values, error) in Cases())
                yield return [chart, JsonSerializer.Serialize(values), error];
        }
    }

    private static IEnumerable<(Dictionary<string, object> Values, string? Error)> Cases()
    {
        yield return (Values(), null);
        yield return (Values(("releaseTag", "refs/tags/2026.1.1-preview999")), null);
        yield return (Values(("releaseTag", "refs/tags/2026.12.12")), null);
        yield return (Values(("environment", "production"), ("enableProduction", true),
            ("releaseTag", "refs/tags/2026.10.1")), null);
        foreach (var stage in new[] { "prepare", "data", "workloads" })
            yield return (Retained(stage), null);
        foreach (var name in new[] { new string('a', 253), "storage.example" })
            yield return (Retained("data", name), null);
        foreach (var environment in new[] { "development", "qa" })
            yield return (Values(("environment", environment)), "environment must be");
        foreach (var revision in new[] { "", "develop", new string('a', 39), new string('a', 41), new string('A', 40), new string('g', 40) })
            yield return (Values(("gitRevision", revision)), "gitRevision must be");
        foreach (var tag in new[] { "", "2026.10.1", "refs/heads/develop", "refs/tags/2026.0.1",
                     "refs/tags/2026.13.1", "refs/tags/2026.01.1", "refs/tags/2026.10.0",
                     "refs/tags/2026.10.1-preview000", "refs/tags/2026.10.1-preview1000", "refs/tags/2026.10.1-preview01" })
            yield return (Values(("releaseTag", tag)), "select an exact refs/tags/");
        yield return (Values(("enableProduction", "true")), "enableProduction must be a boolean");
        yield return (Values(("environment", "production"), ("releaseTag", "refs/tags/2026.10.1")), "production is disabled");
        yield return (Values(("environment", "production"), ("enableProduction", true)), "production requires a final release tag");
        yield return (Values(("infrastructureProfile", "unknown")), "infrastructureProfile must be");
        var productionRetained = Retained("data");
        productionRetained["environment"] = "production";
        productionRetained["enableProduction"] = true;
        productionRetained["releaseTag"] = "refs/tags/2026.10.1";
        yield return (productionRetained, "retained is supported only in staging");
        yield return (Values(("retainedStorageClass", 123)), "retainedStorageClass must be a string");
        foreach (var name in new[] { "", "UPPER", "bad_name", "-leading", "trailing-", "a..b", new string('a', 254) })
            yield return (Retained("data", name), "retained requires a valid, nonempty retainedStorageClass name");
        foreach (var stage in new[] { "", "stop" })
            yield return (Retained(stage), "retained requires activationStage");
        yield return (Values(("activationStage", "data")), "supported only with retained");
        yield return (Values(("retainedStorageClass", "drn-retained")), "supported only with retained");
    }

    [Theory]
    [DataInlineUnit("")]
    [DataInlineUnit("prepare")]
    [DataInlineUnit("data")]
    [DataInlineUnit("workloads")]
    public async Task RootPropagatesRevisionAndRetainedStage(string stage)
    {
        var retained = stage != "";
        var values = retained ? Retained(stage) : Values();
        values["repository"] = "https://example.invalid/gitops.git";
        var output = await Accepted("deployment", values);
        if (stage == "prepare")
            Assert.DoesNotMatch(@"(?m)^  name: drn-monitoring$", output);
        foreach (var branch in stage == "prepare" ? new[] { "logging", "workloads" } : new[] { "logging", "monitoring", "workloads" })
        {
            var document = Application(output, "drn-" + branch);
            var expectedStage = stage == "workloads" && branch != "workloads" ? "data" : stage;
            Assert.Contains($"targetRevision: \"{Revision}\"", document);
            Assert.Contains($"gitRevision: \"{Revision}\"", document);
            Assert.Contains("repository: \"https://example.invalid/gitops.git\"", document);
            Assert.Contains("repoURL: \"https://example.invalid/gitops.git\"", document);
            Assert.Contains("environment: \"staging\"", document);
            Assert.Contains("releaseTag: \"refs/tags/2026.10.1-preview001\"", document);
            Assert.Contains("enableProduction: false", document);
            Assert.Contains($"infrastructureProfile: \"{(retained ? "retained" : "")}\"", document);
            Assert.Contains($"retainedStorageClass: \"{(retained ? "drn-retained" : "")}\"", document);
            Assert.Contains($"activationStage: \"{expectedStage}\"", document);
            Assert.Contains("prune: false", document);
            Assert.Contains($"selfHeal: {retained.ToString().ToLowerInvariant()}", document);
        }
        var trust = Application(output, "cert-manager-bootstrap");
        var sealedSecrets = Application(output, "sealed-secrets");
        var traefik = Application(output, "traefik");
        if (retained)
        {
            Assert.Contains("include: linkerd-trust-anchor-issuer.yaml", trust);
            Assert.Contains("secretName: sealed-secrets-key", sealedSecrets);
            Assert.Contains("keyrenewperiod: \"0\"", sealedSecrets);
            Assert.Contains("type: ClusterIP", traefik);
            Application(output, "graylog-isolation");
        }
        else
        {
            Assert.DoesNotContain("include:", trust);
            Assert.DoesNotContain("keyrenewperiod:", sealedSecrets);
            Assert.DoesNotContain("type: ClusterIP", traefik);
            Assert.DoesNotMatch(@"(?m)^  name: graylog-isolation$", output);
        }
    }

    [Fact]
    public async Task ProductionOptInAndFinalReleaseReachEveryBranch()
    {
        var output = await Accepted("deployment", Values(("environment", "production"),
            ("enableProduction", true), ("releaseTag", "refs/tags/2026.10.1")));
        foreach (var branch in new[] { "logging", "monitoring", "workloads" })
        {
            var document = Application(output, "drn-" + branch);
            Assert.Contains("environment: \"production\"", document);
            Assert.Contains("enableProduction: true", document);
            Assert.Contains("releaseTag: \"refs/tags/2026.10.1\"", document);
        }
    }

    [Theory]
    [DataInlineUnit("prepare")]
    [DataInlineUnit("data")]
    [DataInlineUnit("workloads")]
    public async Task RetainedWorkloadActivation(string stage)
    {
        var output = await Accepted("deployment/workloads", Retained(stage));
        foreach (var name in new[] { "sample", "nexus" })
        {
            if (stage == "prepare")
                Assert.DoesNotMatch($@"(?m)^  name: {name}$", output);
            else
            {
                var document = Application(output, name);
                var replicas = stage == "workloads" ? 1 : 0;
                Assert.Matches($@"path: /spec/replicas\n\s+value: {replicas}(?:\s|$)", Patch(document, "Deployment", name));
                Assert.Matches("path: /spec/storageClassName\\n\\s+value: \"drn-retained\"",
                    Patch(document, "PersistentVolumeClaim", name + "-data"));
                Assert.Contains("selfHeal: true", document);
            }
        }
        if (stage != "prepare")
        {
            AssertExternalCredentials(Application(output, "application-secrets"), "application-secret-bootstrap");
            var database = Patch(Application(output, "postgresql"), "Cluster", "postgresql");
            Assert.Matches("path: /spec/storage/storageClass\\n\\s+value: \"drn-retained\"", database);
            Assert.Matches(@"path: /spec/bootstrap/initdb/secret\n\s+value:\n\s+name: postgresql-app", database);
            Assert.Matches(@"path: /spec/certificates\n\s+value:\n\s+serverCASecret: postgresql-ca\n\s+clientCASecret: postgresql-ca", database);
        }
        else
            foreach (var name in new[] { "postgresql", "application-secrets" })
                Assert.DoesNotMatch($@"(?m)^  name: {name}$", output);
    }

    [Theory]
    [DataInlineUnit("prepare")]
    [DataInlineUnit("data")]
    [DataInlineUnit("workloads")]
    public async Task RetainedLoggingProtectsStorageAndCredentials(string stage)
    {
        var output = await Accepted("deployment/logging", Retained(stage));
        if (stage == "prepare")
        {
            foreach (var name in new[] { "mongodb", "graylog" })
                Assert.DoesNotMatch($@"(?m)^  name: {name}$", output);
            return;
        }
        var mongodb = Application(output, "mongodb");
        AssertExternalCredentials(mongodb, "graylog-credential-bootstrap");
        var storage = Patch(mongodb, "MongoDBCommunity", "mongodb");
        foreach (var (index, name) in new[] { (0, "data-volume"), (1, "logs-volume") })
        {
            Assert.Matches($@"path: /spec/statefulSet/spec/volumeClaimTemplates/{index}/metadata/name\n\s+value: {name}", storage);
            Assert.Matches($"path: /spec/statefulSet/spec/volumeClaimTemplates/{index}/spec/storageClassName\\n\\s+value: \"drn-retained\"", storage);
        }
        var graylog = Application(output, "graylog");
        Assert.Matches("global:\\n(?:[ \\t]*#[^\\n]*\\n)*\\s+existingSecretName: graylog-credentials\\n\\s+storageClass: \"drn-retained\"", graylog);
        Assert.DoesNotContain("kind: NetworkPolicy", graylog);
        Assert.Contains("roles: [cluster_manager, data, ingest, remote_cluster_client]", graylog);
        Assert.Contains("GRAYLOG_DATANODE_PATH_REPO: /var/lib/graylog-datanode/drn-backups", graylog);
        Assert.Contains("command: [mkdir, -p, /var/lib/graylog-datanode/drn-backups]", graylog);
    }

    private static void AssertExternalCredentials(string document, string job)
    {
        var patch = Patch(document, "Job", job);
        Assert.Matches("- name: bootstrap\\n\\s+env:\\n\\s+- name: CREDENTIALS_EXTERNALLY_MANAGED\\n\\s+value: \"true\"", patch);
    }

    private static string Patch(string document, string kind, string name)
    {
        // Restrict assertions to one target so a valid patch on the wrong resource cannot pass.
        var blocks = Regex.Split(document, @"(?m)^        - target:\n").Skip(1);
        var block = Assert.Single(blocks.Where(block =>
        {
            var target = block.Split("          patch:", StringSplitOptions.None)[0];
            return Regex.IsMatch(target, @"(?m)^            kind: " + Regex.Escape(kind) + "$") &&
                   Regex.IsMatch(target, @"(?m)^            name: " + Regex.Escape(name) + "$");
        }));
        return block.Split("          patch:", StringSplitOptions.None)[1].Split("\n  destination:", StringSplitOptions.None)[0];
    }

    private static Dictionary<string, object> Values(params (string Key, object Value)[] overrides)
    {
        var values = new Dictionary<string, object>
        {
            ["environment"] = "staging", ["gitRevision"] = Revision,
            ["releaseTag"] = "refs/tags/2026.10.1-preview001"
        };
        foreach (var (key, value) in overrides) values[key] = value;
        return values;
    }

    private static Dictionary<string, object> Retained(string stage, string storageClass = "drn-retained") =>
        Values(("infrastructureProfile", "retained"), ("activationStage", stage), ("retainedStorageClass", storageClass));

    private static string Application(string output, string name)
    {
        // Anchor chart-owned Application documents, excluding embedded resource patches.
        return Assert.Single(Regex.Split(output, @"(?m)^---\s*$").Where(document =>
            Regex.IsMatch(document, @"(?m)^kind: Application$") &&
            Regex.IsMatch(document, @"(?m)^metadata:\n  name: " + Regex.Escape(name) + "$")));
    }

    private static async Task<string> Accepted(string chart, Dictionary<string, object> values)
    {
        var result = await Render(chart, JsonSerializer.Serialize(values));
        Assert.True(result.ExitCode == 0, result.Error);
        return result.Output;
    }

    private static async Task<(int ExitCode, string Output, string Error)> Render(string chart, string values)
    {
        var directory = Directory.CreateTempSubdirectory("chart-validation-");
        try
        {
            var path = Path.Combine(directory.FullName, "values.json");
            await File.WriteAllTextAsync(path, values);
            var start = new ProcessStartInfo("helm")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            };
            foreach (var argument in new[] { "template", "contract-test", Path.Combine(Root, chart), "--values", path })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Helm.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                await Task.WhenAll(output, error);
                throw new TimeoutException($"Helm rendering exceeded 30 seconds for {chart}.");
            }
            return (process.ExitCode, (await output).Replace("\r\n", "\n"), await error);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "deployment/Chart.yaml")) &&
                File.Exists(Path.Combine(directory.FullName, "global.json")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not find the repository root above the test output directory.");
    }
}
