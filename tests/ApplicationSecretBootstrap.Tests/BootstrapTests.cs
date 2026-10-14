using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using AwesomeAssertions;
using DRN.Framework.Testing.DataAttributes;
using DRN.GitOps.Bootstraps.Application;
using Xunit;
using static ApplicationSecretBootstrap.Tests.BootstrapFixtures;

namespace ApplicationSecretBootstrap.Tests;

/// <summary>Verifies the deployed application bootstrap against an in-memory Kubernetes API.</summary>
[SuppressMessage("ReSharper", "AccessToDisposedClosure")]
public class BootstrapTests
{
    private static readonly FixtureJson<BootstrapSecret> Json = new(Bootstrap.JsonOptions);

    private const string Seed = "DrnAppFeatures__SeedKey";
    private const string Material = "NexusAppSettings__Keys__0__KeyMaterial";
    private const string Format = "NexusAppSettings__Keys__0__Format";
    private const string Default = "NexusAppSettings__Keys__0__Default";
    private const string AllowedHosts = "AllowedHosts";
    private const string NexusApplication = "nexus";
    private const string SampleApplication = "sample";
    private const string NexusSettingsSecret = "nexus-settings";
    private const string SampleSettingsSecret = "sample-settings";

    [Fact]
    public async Task Fresh_Installation_Should_Create_Compatible_Secrets_And_Preserve_Them_On_Rerun()
    {
        using var api = new KubernetesApi();
        using var client = api.CreateClient();

        await Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        api.Writes.Select(request => request.Body!.Metadata!.Name)
            .Should().Equal(NexusSettingsSecret, SampleSettingsSecret);
        foreach (var app in new[] { NexusApplication, SampleApplication })
        {
            var secret = api.Secrets[$"{app}-settings"];
            secret.ApiVersion.Should().Be("v1");
            secret.Kind.Should().Be("Secret");
            secret.Type.Should().Be("Opaque");
            secret.Metadata!.Namespace.Should().Be("drn-project");
            Value(secret, Seed).Should().MatchRegex("^Aa0!(?:[0-9a-f]{2}-){31}[0-9a-f]{2}$");
            Convert.FromBase64String(Value(secret, Material)).Should().HaveCount(32);
            Value(secret, Format).Should().Be("Base64");
            Value(secret, Default).Should().Be("true");
            Value(secret, AllowedHosts).Should().Be(
                $"{app};{app}.drn-project;{app}.drn-project.svc;{app}.drn-project.svc.cluster.local;localhost;127.0.0.1");
        }
        Value(api.Secrets[NexusSettingsSecret], Material).Should().Be(Value(api.Secrets[SampleSettingsSecret], Material));
        Value(api.Secrets[NexusSettingsSecret], Seed).Should().Be(Value(api.Secrets[SampleSettingsSecret], Seed));
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
            SetValue(secret, AllowedHosts, "custom.example.test");
            SetValue(secret, Format, "Utf8");
            SetValue(secret, Material, "0123456789abcdef0123456789abcdef");
            secret.Data!["ExtraSetting"] = Encode("keep-me");
        }
        var before = Snapshot(api);

        await Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        Snapshot(api).Should().Equal(before);
        api.Requests.Should().HaveCount(2);
        api.Writes.Should().BeEmpty();
    }

    [Theory]
    [DataInlineUnit(NexusSettingsSecret)]
    [DataInlineUnit(SampleSettingsSecret)]
    public async Task Partial_Pair_Should_Preserve_Survivor_And_Copy_Credentials_Into_Missing_Secret(string existing)
    {
        using var api = new KubernetesApi();
        api.Secrets[existing] = Secret(existing);
        SetValue(api.Secrets[existing], Format, "Utf8");
        SetValue(api.Secrets[existing], Material, "0123456789abcdef0123456789abcdef");
        var saved = Json.Serialize(api.Secrets[existing]);
        using var client = api.CreateClient();

        await Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        var missing = existing == NexusSettingsSecret ? SampleSettingsSecret : NexusSettingsSecret;
        api.Writes.Should().ContainSingle().Which.Name.Should().Be(missing);
        Json.Serialize(api.Secrets[existing]).Should().Be(saved);
        foreach (var field in new[] { Seed, Material, Format, Default })
            Value(api.Secrets[missing], field).Should().Be(Value(api.Secrets[existing], field));
        Value(api.Secrets[missing], AllowedHosts).Should().NotBe(Value(api.Secrets[existing], AllowedHosts));
        var before = Snapshot(api);
        api.Requests.Clear();

        await Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        Snapshot(api).Should().Equal(before);
        api.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Adding_Application_Should_Copy_Nexus_Credentials_And_Skip_Existing_Secrets()
    {
        using var api = ExistingPair();
        var before = Snapshot(api);
        using var client = api.CreateClient();
        string[] applications = [NexusApplication, SampleApplication, "future", "future"];

        await Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token", applications);

        api.Writes.Should().ContainSingle().Which.Name.Should().Be("future-settings");
        Json.Snapshot(api.Secrets.Where(pair => pair.Key != "future-settings")
            .ToDictionary(pair => pair.Key, pair => pair.Value)).Should().Equal(before);
        foreach (var field in new[] { Seed, Material, Format, Default })
            Value(api.Secrets["future-settings"], field).Should().Be(Value(api.Secrets[NexusSettingsSecret], field));
        Value(api.Secrets["future-settings"], AllowedHosts).Should().StartWith("future;future.drn-project;");
        var completed = Snapshot(api);
        api.Requests.Clear();

        await Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token", applications);

        api.Writes.Should().BeEmpty();
        Snapshot(api).Should().Equal(completed);
    }

    [Theory]
    [DataMemberUnit(nameof(InvalidFields))]
    public async Task Invalid_Survivor_Should_Fail_Before_Creating_Missing_Secret(string name, string field, string? encoded)
    {
        using var api = new KubernetesApi();
        api.Secrets[name] = Secret(name);
        api.Secrets[name].Data![field] = encoded;
        var before = Snapshot(api);
        using var client = api.CreateClient();
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        (await act.Should().ThrowAsync<BootstrapException>()).Which.Message.Should().Contain(name).And.Contain(field);

        api.Writes.Should().BeEmpty();
        Snapshot(api).Should().Equal(before);
    }

    [Theory]
    [DataInlineUnit(NexusSettingsSecret, false)]
    [DataInlineUnit(NexusSettingsSecret, true)]
    [DataInlineUnit(SampleSettingsSecret, false)]
    [DataInlineUnit(SampleSettingsSecret, true)]
    public async Task Partial_Recovery_Create_Conflict_Should_Preserve_And_Validate_Winner(string existing, bool mismatch)
    {
        using var api = new KubernetesApi();
        api.Secrets[existing] = Secret(existing);
        var saved = Json.Serialize(api.Secrets[existing]);
        var missing = existing == NexusSettingsSecret ? SampleSettingsSecret : NexusSettingsSecret;
        var winner = Secret(missing);
        if (mismatch) SetValue(winner, Material, "different-synthetic-key");
        api.Intercept = request =>
        {
            if (request.Method != HttpMethod.Post || request.Name != missing) return null;
            api.Secrets[missing] = winner;
            return Response(HttpStatusCode.Conflict);
        };
        using var client = api.CreateClient();
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        if (mismatch)
            (await act.Should().ThrowAsync<BootstrapException>()).Which.Message.Should().Contain("key settings differ");
        else
            await act.Should().NotThrowAsync();

        api.Writes.Should().ContainSingle();
        api.Secrets[missing].Should().BeSameAs(winner);
        Json.Serialize(api.Secrets[existing]).Should().Be(saved);
    }

    [Theory]
    [DataMemberUnit(nameof(InvalidFields))]
    public async Task Invalid_Required_Field_Should_Fail_Without_Overwriting(
        string name, string field, string? encoded)
    {
        using var api = ExistingPair();
        api.Secrets[name].Data![field] = encoded;
        var before = Snapshot(api);
        using var client = api.CreateClient();
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        var error = (await act.Should().ThrowAsync<BootstrapException>()).Which;

        error.Message.Should().Contain(name).And.Contain(field);
        api.Writes.Should().BeEmpty();
        Snapshot(api).Should().Equal(before);
    }

    public static IEnumerable<object?[]> InvalidFields()
    {
        foreach (var name in new[] { NexusSettingsSecret, SampleSettingsSecret })
        foreach (var field in new[] { AllowedHosts, Seed, Material, Format, Default })
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
        SetValue(api.Secrets[SampleSettingsSecret], field, "different-synthetic-value");
        using var client = api.CreateClient();
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        (await act.Should().ThrowAsync<BootstrapException>()).Which.Message.Should().Contain("key settings differ");
        api.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Nexus_Create_Conflict_Should_Reuse_The_Winning_Key()
    {
        using var api = new KubernetesApi();
        var winner = Secret(NexusSettingsSecret);
        api.Intercept = request =>
        {
            if (request.Method != HttpMethod.Post || request.Name != NexusSettingsSecret) return null;
            api.Secrets[NexusSettingsSecret] = winner;
            return Response(HttpStatusCode.Conflict);
        };
        using var client = api.CreateClient();

        await Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        api.Secrets[NexusSettingsSecret].Should().BeSameAs(winner);
        Value(api.Secrets[SampleSettingsSecret], Seed).Should().Be(Value(winner, Seed));
        Value(api.Secrets[SampleSettingsSecret], Material).Should().Be(Value(winner, Material));
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
            if (request.Method != HttpMethod.Post || request.Name != SampleSettingsSecret) return null;
            var winner = Secret(SampleSettingsSecret);
            SetValue(winner, Material, mismatch ? "different-synthetic-key" : Value(api.Secrets[NexusSettingsSecret], Material));
            api.Secrets[SampleSettingsSecret] = winner;
            return Response(HttpStatusCode.Conflict);
        };
        using var client = api.CreateClient();
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

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
            ? Response(HttpStatusCode.Conflict) : null;
        using var client = api.CreateClient();
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        (await act.Should().ThrowAsync<BootstrapException>()).Which.Message.Should().Contain("disappeared");
        api.Writes.Should().ContainSingle();
        api.Secrets.Should().BeEmpty();
    }

    [Fact]
    public async Task Interrupted_Pair_Creation_Should_Complete_On_Retry_Without_Changing_Nexus()
    {
        using var api = new KubernetesApi();
        api.Intercept = request => request.Method == HttpMethod.Post && request.Name == SampleSettingsSecret
            ? Response(HttpStatusCode.Forbidden) : null;
        using var client = api.CreateClient();
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        await act.Should().ThrowAsync<BootstrapException>();
        var saved = Json.Serialize(api.Secrets[NexusSettingsSecret]);
        api.Requests.Clear();
        api.Intercept = null;

        await act.Should().NotThrowAsync();
        api.Writes.Should().ContainSingle().Which.Name.Should().Be(SampleSettingsSecret);
        Json.Serialize(api.Secrets[NexusSettingsSecret]).Should().Be(saved);
        Value(api.Secrets[SampleSettingsSecret], Seed).Should().Be(Value(api.Secrets[NexusSettingsSecret], Seed));
        Value(api.Secrets[SampleSettingsSecret], Material).Should().Be(Value(api.Secrets[NexusSettingsSecret], Material));
    }

    [Theory]
    [DataInlineUnit(401)]
    [DataInlineUnit(403)]
    [DataInlineUnit(500)]
    public async Task Api_Failure_Should_Not_Expose_Response_Body(int status)
    {
        using var api = new KubernetesApi();
        api.Intercept = _ => Response((HttpStatusCode)status, "synthetic-sensitive-response");
        using var client = api.CreateClient();
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        var error = (await act.Should().ThrowAsync<BootstrapException>()).Which;

        error.Message.Should().Contain(status.ToString()).And.NotContain("synthetic-sensitive-response");
        api.Writes.Should().BeEmpty();
    }

    [Theory]
    [DataInlineUnit("not-json")]
    [DataInlineUnit("null")]
    [DataInlineUnit("[]")]
    [DataInlineUnit("{\"data\":123,\"private\":\"synthetic-sensitive-response\"}")]
    [DataInlineUnit("{\"data\":[],\"private\":\"synthetic-sensitive-response\"}")]
    [DataInlineUnit("{\"metadata\":\"synthetic-sensitive-response\"}")]
    [DataInlineUnit("{\"data\":{\"AllowedHosts\":123},\"private\":\"synthetic-sensitive-response\"}")]
    public async Task Invalid_Api_Response_Should_Fail_Without_Writes(string response)
    {
        using var api = new KubernetesApi();
        api.Intercept = _ => Response(HttpStatusCode.OK, response);
        using var client = api.CreateClient();
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        (await act.Should().ThrowAsync<BootstrapException>()).Which.Message
            .Should().NotContain("synthetic-sensitive-response");
        api.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Every_Request_Should_Read_The_Current_Token()
    {
        using var api = new KubernetesApi();
        using var client = api.CreateClient();
        var tokenReads = 0;

        await Bootstrap.RunAsync(client, "drn-project", () => $"synthetic-token-{++tokenReads}");

        tokenReads.Should().Be(4);
        api.Requests.Select(request => request.Authorization)
            .Should().Equal(Enumerable.Range(1, 4).Select(index => $"Bearer synthetic-token-{index}"));
    }

    private static KubernetesApi ExistingPair()
    {
        var api = new KubernetesApi();
        api.Secrets[NexusSettingsSecret] = Secret(NexusSettingsSecret);
        api.Secrets[SampleSettingsSecret] = Secret(SampleSettingsSecret);
        return api;
    }

    private static BootstrapSecret Secret(string name) => new()
    {
        ApiVersion = "v1",
        Kind = "Secret",
        Type = "Opaque",
        Metadata = new BootstrapMetadata { Name = name, Namespace = "drn-project" },
        Data = new Dictionary<string, string?>
        {
            [AllowedHosts] = Encode("synthetic.example.test"),
            [Seed] = Encode($"synthetic-{name}-seed"),
            [Material] = Encode(Convert.ToBase64String(Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"))),
            [Format] = Encode("Base64"),
            [Default] = Encode("true")
        }
    };

    private static string Value(BootstrapSecret secret, string field) =>
        Decode(secret.Data![field]!);
    private static void SetValue(BootstrapSecret secret, string field, string value) => secret.Data![field] = Encode(value);
    private static string[] Snapshot(KubernetesApi api) =>
        Json.Snapshot(api.Secrets);

    private sealed record ApiRequest(HttpMethod Method, string Path, string? Authorization, BootstrapSecret? Body)
    {
        public string? Name => Body?.Metadata?.Name;
    }

    private sealed class KubernetesApi : HttpMessageHandler
    {
        private const string SecretsPath = "/api/v1/namespaces/drn-project/secrets";
        public Dictionary<string, BootstrapSecret> Secrets { get; } = [];
        public List<ApiRequest> Requests { get; } = [];
        public IEnumerable<ApiRequest> Writes => Requests.Where(request => request.Method != HttpMethod.Get);
        public Func<ApiRequest, HttpResponseMessage?>? Intercept { get; set; }

        public HttpClient CreateClient() => new(this, disposeHandler: false)
        {
            BaseAddress = new Uri("https://kubernetes.default.svc")
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var body = json is null ? null : Json.Deserialize(json);
            if (json is not null)
                SecretWireContract.Verify(json, body!.Metadata!.Name, "drn-project");
            var captured = new ApiRequest(request.Method, request.RequestUri!.AbsolutePath,
                request.Headers.Authorization?.ToString(), body);
            Requests.Add(captured);
            var intercepted = Intercept?.Invoke(captured);
            if (intercepted is not null) return intercepted;

            if (request.Method == HttpMethod.Get && captured.Path.StartsWith(SecretsPath + "/", StringComparison.Ordinal))
                return Secrets.TryGetValue(captured.Path[(SecretsPath.Length + 1)..], out var secret)
                    ? Response(HttpStatusCode.OK, Json.Serialize(secret)) : Response(HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Post && captured.Path == SecretsPath)
            {
                if (Secrets.ContainsKey(captured.Name!)) return Response(HttpStatusCode.Conflict);
                // Kubernetes persists stringData as base64 data and does not return the write-only input.
                var stored = new BootstrapSecret
                {
                    ApiVersion = body!.ApiVersion,
                    Kind = body.Kind,
                    Type = body.Type,
                    Metadata = body.Metadata,
                    Data = body.StringData!.ToDictionary(pair => pair.Key, pair => (string?)Encode(pair.Value!))
                };
                Secrets.Add(captured.Name!, stored);
                return Response(HttpStatusCode.Created, Json.Serialize(stored));
            }
            throw new InvalidOperationException($"Unexpected API request: {request.Method} {captured.Path}");
        }
    }
}
