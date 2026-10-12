using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using DRN.Framework.Testing.DataAttributes;
using Xunit;

namespace ApplicationSecretBootstrap.Tests;

public class BootstrapTests
{
    private const string Seed = "DrnAppFeatures__SeedKey";
    private const string Material = "NexusAppSettings__Keys__0__KeyMaterial";
    private const string Format = "NexusAppSettings__Keys__0__Format";
    private const string Default = "NexusAppSettings__Keys__0__Default";

    [Fact]
    public async Task Fresh_Installation_Should_Create_Compatible_Secrets_And_Preserve_Them_On_Rerun()
    {
        using var api = new KubernetesApi();
        using var client = api.CreateClient();

        await Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        api.Writes.Select(request => request.Body!["metadata"]!["name"]!.GetValue<string>())
            .Should().Equal("nexus-settings", "sample-settings");
        foreach (var app in new[] { "nexus", "sample" })
        {
            var secret = api.Secrets[$"{app}-settings"];
            secret["type"]!.GetValue<string>().Should().Be("Opaque");
            secret["metadata"]!["namespace"]!.GetValue<string>().Should().Be("drn-project");
            secret["metadata"]!["ownerReferences"].Should().BeNull();
            secret["metadata"]!["annotations"].Should().BeNull();
            Value(secret, Seed).Should().MatchRegex("^Aa0!(?:[0-9a-f]{2}-){31}[0-9a-f]{2}$");
            Convert.FromBase64String(Value(secret, Material)).Should().HaveCount(32);
            Value(secret, Format).Should().Be("Base64");
            Value(secret, Default).Should().Be("true");
            Value(secret, "AllowedHosts").Should().Be(
                $"{app};{app}.drn-project;{app}.drn-project.svc;{app}.drn-project.svc.cluster.local;localhost;127.0.0.1");
        }
        Value(api.Secrets["nexus-settings"], Material).Should().Be(Value(api.Secrets["sample-settings"], Material));
        Value(api.Secrets["nexus-settings"], Seed).Should().NotBe(Value(api.Secrets["sample-settings"], Seed));
        var before = Snapshot(api);
        api.Requests.Clear();

        await Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        api.Writes.Should().BeEmpty();
        Snapshot(api).Should().Equal(before);
    }

    [Fact]
    public async Task Existing_Custom_Settings_Should_Be_Preserved_Without_Deployment_Reads()
    {
        using var api = ExistingPair();
        using var client = api.CreateClient();
        foreach (var secret in api.Secrets.Values)
        {
            SetValue(secret, "AllowedHosts", "custom.example.test");
            SetValue(secret, Format, "Utf8");
            SetValue(secret, Material, "0123456789abcdef0123456789abcdef");
            secret["data"]!["ExtraSetting"] = Encode("keep-me");
        }
        var before = Snapshot(api);

        await Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        Snapshot(api).Should().Equal(before);
        api.Requests.Should().HaveCount(2);
        api.Writes.Should().BeEmpty();
    }

    [Theory]
    [DataInlineUnit("nexus-settings")]
    [DataInlineUnit("sample-settings")]
    public async Task Partial_Pair_Should_Fail_Without_Writes(string existing)
    {
        using var api = new KubernetesApi();
        api.Secrets[existing] = Secret(existing);
        using var client = api.CreateClient();

        Func<Task> act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        (await act.Should().ThrowAsync<BootstrapException>()).Which.Message.Should().Contain("Only one settings Secret exists");
        api.Writes.Should().BeEmpty();
    }

    [Theory]
    [DataInlineUnit("nexus")]
    [DataInlineUnit("sample")]
    public async Task Missing_Secrets_With_Existing_Deployment_Should_Fail(string deployment)
    {
        using var api = new KubernetesApi();
        api.Deployments.Add(deployment);
        using var client = api.CreateClient();
        Func<Task> act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        (await act.Should().ThrowAsync<BootstrapException>()).Which.Message.Should().Contain("workloads already exist");
        api.Writes.Should().BeEmpty();
    }

    [Theory]
    [DataMemberUnit(nameof(InvalidFields))]
    public async Task Invalid_Required_Field_Should_Fail_Without_Overwriting(
        string name, string field, string? encoded)
    {
        using var api = ExistingPair();
        api.Secrets[name]["data"]![field] = encoded;
        var before = Snapshot(api);
        using var client = api.CreateClient();
        Func<Task> act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        var error = (await act.Should().ThrowAsync<BootstrapException>()).Which;

        error.Message.Should().Contain(name).And.Contain(field);
        api.Writes.Should().BeEmpty();
        Snapshot(api).Should().Equal(before);
    }

    public static IEnumerable<object?[]> InvalidFields()
    {
        foreach (var name in new[] { "nexus-settings", "sample-settings" })
        foreach (var field in new[] { "AllowedHosts", Seed, Material, Format, Default })
        foreach (var encoded in new[] { null, "", "%%%", "YQ==\n", "/w==", Encode(" \t\n") })
            yield return [name, field, encoded];
    }

    [Theory]
    [DataInlineUnit(Material)]
    [DataInlineUnit(Format)]
    [DataInlineUnit(Default)]
    public async Task Different_Shared_Key_Settings_Should_Fail_Without_Writes(string field)
    {
        using var api = ExistingPair();
        SetValue(api.Secrets["sample-settings"], field, "different-synthetic-value");
        using var client = api.CreateClient();
        Func<Task> act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        (await act.Should().ThrowAsync<BootstrapException>()).Which.Message.Should().Contain("key settings differ");
        api.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Nexus_Create_Conflict_Should_Reuse_The_Winning_Key()
    {
        using var api = new KubernetesApi();
        var winner = Secret("nexus-settings");
        api.Intercept = request =>
        {
            if (request.Method != HttpMethod.Post || request.Name != "nexus-settings") return null;
            api.Secrets["nexus-settings"] = winner;
            return KubernetesApi.Response(HttpStatusCode.Conflict);
        };
        using var client = api.CreateClient();

        await Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        api.Secrets["nexus-settings"].Should().BeSameAs(winner);
        Value(api.Secrets["sample-settings"], Material).Should().Be(Value(winner, Material));
        api.Writes.Should().HaveCount(2);
    }

    [Theory]
    [DataInlineUnit(false)]
    [DataInlineUnit(true)]
    public async Task Sample_Create_Conflict_Should_Validate_The_Winner(bool mismatch)
    {
        using var api = new KubernetesApi();
        api.Intercept = request =>
        {
            if (request.Method != HttpMethod.Post || request.Name != "sample-settings") return null;
            var winner = Secret("sample-settings");
            SetValue(winner, Material, mismatch ? "different-synthetic-key" : Value(api.Secrets["nexus-settings"], Material));
            api.Secrets["sample-settings"] = winner;
            return KubernetesApi.Response(HttpStatusCode.Conflict);
        };
        using var client = api.CreateClient();
        Func<Task> act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        if (mismatch)
            (await act.Should().ThrowAsync<BootstrapException>()).Which.Message.Should().Contain("key settings differ");
        else
            await act.Should().NotThrowAsync();
        api.Writes.Should().HaveCount(2);
    }

    [Fact]
    public async Task Disappearing_Conflict_Winner_Should_Fail_Before_Creating_Sample()
    {
        using var api = new KubernetesApi();
        api.Intercept = request => request.Method == HttpMethod.Post
            ? KubernetesApi.Response(HttpStatusCode.Conflict) : null;
        using var client = api.CreateClient();
        Func<Task> act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        (await act.Should().ThrowAsync<BootstrapException>()).Which.Message.Should().Contain("disappeared");
        api.Writes.Should().ContainSingle();
        api.Secrets.Should().BeEmpty();
    }

    [Fact]
    public async Task Interrupted_Pair_Creation_Should_Require_Repair_On_Retry()
    {
        using var api = new KubernetesApi();
        api.Intercept = request => request.Method == HttpMethod.Post && request.Name == "sample-settings"
            ? KubernetesApi.Response(HttpStatusCode.Forbidden) : null;
        using var client = api.CreateClient();
        Func<Task> act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        await act.Should().ThrowAsync<BootstrapException>();
        var saved = api.Secrets["nexus-settings"].ToJsonString();
        api.Requests.Clear();
        api.Intercept = null;

        (await act.Should().ThrowAsync<BootstrapException>()).Which.Message.Should().Contain("Only one settings Secret exists");
        api.Writes.Should().BeEmpty();
        api.Secrets["nexus-settings"].ToJsonString().Should().Be(saved);
    }

    [Theory]
    [DataInlineUnit(401)]
    [DataInlineUnit(403)]
    [DataInlineUnit(500)]
    public async Task Api_Failure_Should_Not_Expose_Response_Body(int status)
    {
        using var api = new KubernetesApi();
        api.Intercept = _ => KubernetesApi.Response((HttpStatusCode)status, "synthetic-sensitive-response");
        using var client = api.CreateClient();
        Func<Task> act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        var error = (await act.Should().ThrowAsync<BootstrapException>()).Which;

        error.Message.Should().Contain(status.ToString()).And.NotContain("synthetic-sensitive-response");
        api.Writes.Should().BeEmpty();
    }

    [Theory]
    [DataInlineUnit("not-json")]
    [DataInlineUnit("null")]
    [DataInlineUnit("[]")]
    public async Task Invalid_Api_Response_Should_Fail_Without_Writes(string response)
    {
        using var api = new KubernetesApi();
        api.Intercept = _ => KubernetesApi.Response(HttpStatusCode.OK, response);
        using var client = api.CreateClient();
        Func<Task> act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        await act.Should().ThrowAsync<BootstrapException>();
        api.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Every_Request_Should_Read_The_Current_Token()
    {
        using var api = new KubernetesApi();
        using var client = api.CreateClient();
        var tokenReads = 0;

        await Bootstrap.RunAsync(client, "drn-project", () => $"synthetic-token-{++tokenReads}");

        tokenReads.Should().Be(6);
        api.Requests.Select(request => request.Authorization)
            .Should().Equal(Enumerable.Range(1, 6).Select(index => $"Bearer synthetic-token-{index}"));
    }

    private static KubernetesApi ExistingPair()
    {
        var api = new KubernetesApi();
        api.Secrets["nexus-settings"] = Secret("nexus-settings");
        api.Secrets["sample-settings"] = Secret("sample-settings");
        return api;
    }

    private static JsonObject Secret(string name) => new()
    {
        ["apiVersion"] = "v1",
        ["kind"] = "Secret",
        ["type"] = "Opaque",
        ["metadata"] = new JsonObject { ["name"] = name, ["namespace"] = "drn-project" },
        ["data"] = new JsonObject
        {
            ["AllowedHosts"] = Encode("synthetic.example.test"),
            [Seed] = Encode($"synthetic-{name}-seed"),
            [Material] = Encode(Convert.ToBase64String(Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"))),
            [Format] = Encode("Base64"),
            [Default] = Encode("true")
        }
    };

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    private static string Value(JsonObject secret, string field) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(secret["data"]![field]!.GetValue<string>()));
    private static void SetValue(JsonObject secret, string field, string value) => secret["data"]![field] = Encode(value);
    private static string[] Snapshot(KubernetesApi api) =>
        api.Secrets.OrderBy(pair => pair.Key).Select(pair => pair.Value.ToJsonString()).ToArray();

    private sealed record ApiRequest(HttpMethod Method, string Path, string? Authorization, JsonObject? Body)
    {
        public string? Name => Body?["metadata"]?["name"]?.GetValue<string>();
    }

    private sealed class KubernetesApi : HttpMessageHandler
    {
        private const string SecretsPath = "/api/v1/namespaces/drn-project/secrets";
        private const string DeploymentsPath = "/apis/apps/v1/namespaces/drn-project/deployments/";
        public Dictionary<string, JsonObject> Secrets { get; } = [];
        public HashSet<string> Deployments { get; } = [];
        public List<ApiRequest> Requests { get; } = [];
        public IEnumerable<ApiRequest> Writes => Requests.Where(request => request.Method != HttpMethod.Get);
        public Func<ApiRequest, HttpResponseMessage?>? Intercept { get; set; }

        public HttpClient CreateClient() => new(this, disposeHandler: false)
        {
            BaseAddress = new Uri("https://kubernetes.default.svc")
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null
                : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
            var captured = new ApiRequest(request.Method, request.RequestUri!.AbsolutePath,
                request.Headers.Authorization?.ToString(), body);
            Requests.Add(captured);
            var intercepted = Intercept?.Invoke(captured);
            if (intercepted is not null) return intercepted;

            if (request.Method == HttpMethod.Get && captured.Path.StartsWith(SecretsPath + "/", StringComparison.Ordinal))
                return Secrets.TryGetValue(captured.Path[(SecretsPath.Length + 1)..], out var secret)
                    ? Response(HttpStatusCode.OK, secret.ToJsonString()) : Response(HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Get && captured.Path.StartsWith(DeploymentsPath, StringComparison.Ordinal))
                return Response(Deployments.Contains(captured.Path[DeploymentsPath.Length..]) ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Post && captured.Path == SecretsPath)
            {
                if (Secrets.ContainsKey(captured.Name!)) return Response(HttpStatusCode.Conflict);
                var stored = body!.DeepClone().AsObject();
                var data = new JsonObject();
                foreach (var pair in stored["stringData"]!.AsObject())
                    data[pair.Key] = Encode(pair.Value!.GetValue<string>());
                stored.Remove("stringData");
                stored["data"] = data;
                Secrets.Add(captured.Name!, stored);
                return Response(HttpStatusCode.Created, stored.ToJsonString());
            }
            throw new InvalidOperationException($"Unexpected API request: {request.Method} {captured.Path}");
        }

        public static HttpResponseMessage Response(HttpStatusCode status, string body = "{}") => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
