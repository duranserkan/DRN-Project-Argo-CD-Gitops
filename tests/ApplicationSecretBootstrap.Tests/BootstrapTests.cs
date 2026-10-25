using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Text.Json;
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
    private static readonly FixtureJson<BootstrapInitializationMarker> MarkerJson = new(Bootstrap.JsonOptions);

    private const string Seed = "DrnAppFeatures__SeedKey";
    private const string Material = "NexusAppSettings__Keys__0__KeyMaterial";
    private const string Format = "NexusAppSettings__Keys__0__Format";
    private const string Default = "NexusAppSettings__Keys__0__Default";
    private const string AllowedHosts = "AllowedHosts";
    private const string NexusApplication = "nexus";
    private const string SampleApplication = "sample";
    private const string NexusSettingsSecret = "nexus-settings";
    private const string SampleSettingsSecret = "sample-settings";
    private const string MarkerName = "application-secret-bootstrap-initialized";
    private const string MarkersPath = "/api/v1/namespaces/drn-project/configmaps";

    [Fact]
    public async Task Externally_Managed_Settings_Should_Validate_Without_Writing()
    {
        using var api = ExistingPair();
        using var client = api.CreateClient();
        var before = Snapshot(api);
        var marker = MarkerJson.Serialize(api.Marker!);

        await Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token", externallyManagedCredentials: true);

        Snapshot(api).Should().Equal(before);
        MarkerJson.Serialize(api.Marker!).Should().Be(marker);
        api.Requests.Should().OnlyContain(request => request.Method == HttpMethod.Get);
    }

    [Theory]
    [DataInlineUnit("nexus-settings")]
    [DataInlineUnit("sample-settings")]
    [DataInlineUnit("all-secrets")]
    [DataInlineUnit("missing-marker")]
    [DataInlineUnit("invalid-marker")]
    [DataInlineUnit("unavailable-marker")]
    [DataInlineUnit("invalid-secret")]
    public async Task Externally_Managed_Settings_Should_Fail_Closed_Even_With_Generation_Override(string failure)
    {
        using var api = ExistingPair();
        using var client = api.CreateClient();
        switch (failure)
        {
            case "all-secrets": api.Secrets.Clear(); break;
            case "missing-marker": api.Marker = null; break;
            case "invalid-marker": api.Marker!.Immutable = false; break;
            case "unavailable-marker":
                api.Intercept = request => request.Path.StartsWith(MarkersPath, StringComparison.Ordinal)
                    ? Response(HttpStatusCode.Forbidden, "private-marker-body") : null;
                break;
            case "invalid-secret": api.Secrets[NexusSettingsSecret].Data![AllowedHosts] = "%%%"; break;
            default: api.Secrets.Remove(failure); break;
        }
        var before = Snapshot(api);
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token",
            allowCredentialGeneration: true, externallyManagedCredentials: true);

        (await act.Should().ThrowAsync<BootstrapException>()).Which.Message.Should().NotContain("private-marker-body");

        Snapshot(api).Should().Equal(before);
        api.Requests.Should().OnlyContain(request => request.Method == HttpMethod.Get);
    }

    [Theory]
    [DataInlineUnit("TRUE")]
    [DataInlineUnit("yes")]
    [DataInlineUnit(" true ")]
    public void Invalid_External_Mode_Should_Not_Enable_Automatic_Generation(string value)
    {
        var act = () => Bootstrap.ParseExternalMode(value);
        act.Should().Throw<BootstrapException>().WithMessage("CREDENTIALS_EXTERNALLY_MANAGED must be true or false.");
    }

    [Fact]
    public async Task Fresh_Installation_Should_Create_Compatible_Secrets_And_Preserve_Them_On_Rerun()
    {
        using var api = new KubernetesApi();
        using var client = api.CreateClient();

        await Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        api.MarkerWrites.Should().ContainSingle();
        api.Requests.Where(request => request.Method == HttpMethod.Post).Select(request => request.Name)
            .Should().Equal(MarkerName, NexusSettingsSecret, SampleSettingsSecret);
        var savedMarker = MarkerJson.Serialize(api.Marker!);

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
        api.MarkerWrites.Should().BeEmpty();
        MarkerJson.Serialize(api.Marker!).Should().Be(savedMarker);
        Snapshot(api).Should().Equal(before);
    }

    [Fact]
    public async Task Total_Secret_Loss_After_Initialization_Should_Fail_Without_Generation()
    {
        using var api = new KubernetesApi();
        using var client = api.CreateClient();
        await Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");
        var savedMarker = MarkerJson.Serialize(api.Marker!);
        api.Secrets.Clear();
        api.Requests.Clear();
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        (await act.Should().ThrowAsync<BootstrapException>()).Which.Message
            .Should().Contain("Restore the lost settings Secrets").And.Contain("ALLOW_CREDENTIAL_GENERATION=true");

        api.Requests.Should().HaveCount(3);
        api.Writes.Should().BeEmpty();
        api.MarkerWrites.Should().BeEmpty();
        MarkerJson.Serialize(api.Marker!).Should().Be(savedMarker);
        api.Secrets.Should().BeEmpty();
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
        api.Requests.Should().HaveCount(3);
        api.Writes.Should().BeEmpty();
    }

    [Theory]
    [DataInlineUnit(NexusSettingsSecret, false)]
    [DataInlineUnit(SampleSettingsSecret, false)]
    [DataInlineUnit(NexusSettingsSecret, true)]
    [DataInlineUnit(SampleSettingsSecret, true)]
    public async Task Partial_Pair_Should_Preserve_Survivor_And_Copy_Credentials_Into_Missing_Secret(string existing, bool initialized)
    {
        using var api = new KubernetesApi { Marker = initialized ? Marker() : null };
        api.Secrets[existing] = Secret(existing);
        SetValue(api.Secrets[existing], Format, "Utf8");
        SetValue(api.Secrets[existing], Material, "0123456789abcdef0123456789abcdef");
        var saved = Json.Serialize(api.Secrets[existing]);
        using var client = api.CreateClient();

        await Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        var missing = existing == NexusSettingsSecret ? SampleSettingsSecret : NexusSettingsSecret;
        api.MarkerWrites.Should().HaveCount(initialized ? 0 : 1);
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
        api.MarkerWrites.Should().BeEmpty();
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
        api.Intercept = request => request.Method == HttpMethod.Post && request.Body is not null
            ? Response(HttpStatusCode.Conflict) : null;
        using var client = api.CreateClient();
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        (await act.Should().ThrowAsync<BootstrapException>()).Which.Message.Should().Contain("Secret disappeared during bootstrap");
        api.MarkerWrites.Should().ContainSingle();
        api.Writes.Should().ContainSingle().Which.Name.Should().Be(NexusSettingsSecret);
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

        await Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");
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

        tokenReads.Should().Be(6);
        api.Requests.Select(request => request.Authorization)
            .Should().Equal(Enumerable.Range(1, 6).Select(index => $"Bearer synthetic-token-{index}"));
    }

    [Fact]
    public async Task Existing_Installation_Should_Enroll_Without_Changing_Credentials()
    {
        using var api = ExistingPair();
        api.Marker = null;
        var before = Snapshot(api);
        using var client = api.CreateClient();

        await Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        api.MarkerWrites.Should().ContainSingle();
        api.Writes.Should().BeEmpty();
        Snapshot(api).Should().Equal(before);
        api.Marker.Should().NotBeNull();
        api.Secrets.Clear();
        api.Requests.Clear();
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");
        await act.Should().ThrowAsync<BootstrapException>();
        api.Writes.Should().BeEmpty();
        api.MarkerWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task Claim_Conflict_Should_Not_Authorize_Generation_And_Retry_Should_Use_Survivor()
    {
        using var api = new KubernetesApi();
        api.Intercept = request =>
        {
            if (request.Method != HttpMethod.Post || request.Path != MarkersPath) return null;
            api.Marker = Marker(); // Another initializer claimed the namespace before this POST.
            return Response(HttpStatusCode.Conflict);
        };
        using var client = api.CreateClient();
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        (await act.Should().ThrowAsync<BootstrapException>()).Which.Message.Should().Contain("already claimed");
        api.Writes.Should().BeEmpty();
        api.Secrets.Should().BeEmpty();

        // The winner subsequently stores Nexus. A retry recovers Sample using those exact keys.
        api.Secrets[NexusSettingsSecret] = Secret(NexusSettingsSecret);
        var saved = Json.Serialize(api.Secrets[NexusSettingsSecret]);
        api.Requests.Clear();
        api.Intercept = null;
        await act();
        api.MarkerWrites.Should().BeEmpty();
        api.Writes.Should().ContainSingle().Which.Name.Should().Be(SampleSettingsSecret);
        Json.Serialize(api.Secrets[NexusSettingsSecret]).Should().Be(saved);
        Value(api.Secrets[SampleSettingsSecret], Material).Should().Be(Value(api.Secrets[NexusSettingsSecret], Material));
    }

    [Fact]
    public async Task Disappearing_Marker_After_Conflict_Should_Fail_Closed_Even_With_Override()
    {
        using var api = new KubernetesApi();
        api.Intercept = request => request.Method == HttpMethod.Post && request.Path == MarkersPath
            ? Response(HttpStatusCode.Conflict) : null;
        using var client = api.CreateClient();
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token", allowCredentialGeneration: true);

        (await act.Should().ThrowAsync<BootstrapException>()).Which.Message.Should().Contain("marker disappeared");
        api.Writes.Should().BeEmpty();
        api.Secrets.Should().BeEmpty();
    }

    [Fact]
    public async Task Failure_Before_First_Secret_Should_Leave_Claim_And_Block_Automatic_Retry()
    {
        using var api = new KubernetesApi();
        api.Intercept = request => request.Method == HttpMethod.Post && request.Name == NexusSettingsSecret
            ? Response(HttpStatusCode.Forbidden, "synthetic-private-body") : null;
        using var client = api.CreateClient();
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        (await act.Should().ThrowAsync<BootstrapException>()).Which.Message.Should().NotContain("synthetic-private-body");
        api.Marker.Should().NotBeNull();
        api.Secrets.Should().BeEmpty();
        api.Intercept = null;
        api.Requests.Clear();
        (await act.Should().ThrowAsync<BootstrapException>()).Which.Message.Should().Contain("already claimed");
        api.Writes.Should().BeEmpty();
        api.MarkerWrites.Should().BeEmpty();
    }

    [Theory]
    [DataInlineUnit(false)]
    [DataInlineUnit(true)]
    public async Task Ambiguous_Claim_Response_Should_Not_Generate_And_Should_Block_Retry(bool timeout)
    {
        using var api = new KubernetesApi();
        api.Intercept = request =>
        {
            if (request.Method != HttpMethod.Post || request.Path != MarkersPath) return null;
            api.Marker = Marker(); // The API persisted the marker but the client did not receive confirmation.
            if (timeout) throw new TaskCanceledException("synthetic-timeout");
            return Response(HttpStatusCode.ServiceUnavailable, "synthetic-private-body");
        };
        using var client = api.CreateClient();
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        if (timeout)
            await act.Should().ThrowAsync<TaskCanceledException>();
        else
            (await act.Should().ThrowAsync<BootstrapException>()).Which.Message.Should().NotContain("synthetic-private-body");
        api.Writes.Should().BeEmpty();
        api.Secrets.Should().BeEmpty();
        api.Intercept = null;
        api.Requests.Clear();
        (await act.Should().ThrowAsync<BootstrapException>()).Which.Message.Should().Contain("already claimed");
        api.Writes.Should().BeEmpty();
        api.MarkerWrites.Should().BeEmpty();
    }

    [Theory]
    [DataInlineUnit("get", 401)]
    [DataInlineUnit("get", 403)]
    [DataInlineUnit("get", 500)]
    [DataInlineUnit("post", 403)]
    [DataInlineUnit("post", 500)]
    [DataInlineUnit("post", 200)]
    public async Task Marker_Api_Failure_Should_Block_Secret_Writes_Even_With_Override(string verb, int status)
    {
        using var api = new KubernetesApi();
        api.Intercept = request => request.Path.StartsWith(MarkersPath, StringComparison.Ordinal) &&
            request.Method == (verb == "get" ? HttpMethod.Get : HttpMethod.Post)
            ? Response((HttpStatusCode)status, "synthetic-private-body") : null;
        using var client = api.CreateClient();
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token", allowCredentialGeneration: true);

        (await act.Should().ThrowAsync<BootstrapException>()).Which.Message
            .Should().Contain(status.ToString()).And.NotContain("synthetic-private-body");
        api.Writes.Should().BeEmpty();
        api.Secrets.Should().BeEmpty();
    }

    [Theory]
    [DataInlineUnit("version")]
    [DataInlineUnit("immutable")]
    [DataInlineUnit("name")]
    [DataInlineUnit("namespace")]
    [DataInlineUnit("kind")]
    [DataInlineUnit("apiVersion")]
    [DataInlineUnit("data")]
    [DataInlineUnit("extra-data")]
    public async Task Invalid_Marker_Should_Block_Recovery_Without_Changing_Survivors(string field)
    {
        using var api = ExistingPair();
        api.Secrets.Remove(SampleSettingsSecret);
        var marker = api.Marker!;
        switch (field)
        {
            case "version": marker.Data!["version"] = "synthetic-private-value"; break;
            case "immutable": marker.Immutable = false; break;
            case "name": marker.Metadata!.Name = "wrong"; break;
            case "namespace": marker.Metadata!.Namespace = "wrong"; break;
            case "kind": marker.Kind = "Secret"; break;
            case "apiVersion": marker.ApiVersion = "v2"; break;
            case "data": marker.Data = null; break;
            case "extra-data": marker.Data!["extra"] = "synthetic-private-value"; break;
        }
        var before = Snapshot(api);
        using var client = api.CreateClient();
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token", allowCredentialGeneration: true);

        (await act.Should().ThrowAsync<BootstrapException>()).Which.Message
            .Should().Contain("marker is invalid").And.NotContain("synthetic-private-value");
        api.Writes.Should().BeEmpty();
        api.MarkerWrites.Should().BeEmpty();
        Snapshot(api).Should().Equal(before);
    }

    [Theory]
    [DataInlineUnit("not-json")]
    [DataInlineUnit("null")]
    [DataInlineUnit("[]")]
    [DataInlineUnit("{}")]
    [DataInlineUnit("{\"immutable\":\"synthetic-private-value\"}")]
    public async Task Invalid_Claim_Response_Should_Not_Authorize_Generation(string response)
    {
        using var api = new KubernetesApi();
        api.Intercept = request => request.Method == HttpMethod.Post && request.Path == MarkersPath
            ? Response(HttpStatusCode.Created, response) : null;
        using var client = api.CreateClient();
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");

        (await act.Should().ThrowAsync<BootstrapException>()).Which.Message.Should().NotContain("synthetic-private-value");
        api.Writes.Should().BeEmpty();
        api.Secrets.Should().BeEmpty();
    }

    [Fact]
    public async Task Explicit_Recovery_Should_Preserve_Existing_Marker_And_Not_Leave_Generation_Enabled()
    {
        using var api = new KubernetesApi { Marker = Marker() };
        var before = MarkerJson.Serialize(api.Marker);
        using var client = api.CreateClient();

        await Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token", allowCredentialGeneration: true);

        api.Writes.Should().HaveCount(2);
        api.MarkerWrites.Should().BeEmpty();
        MarkerJson.Serialize(api.Marker).Should().Be(before);
        api.Secrets.Clear();
        api.Requests.Clear();
        var act = () => Bootstrap.RunAsync(client, "drn-project", () => "synthetic-token");
        await act.Should().ThrowAsync<BootstrapException>();
        api.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Concurrent_Initializers_Should_Allow_Only_The_Claim_Winner_To_Generate()
    {
        using var api = new KubernetesApi();
        using var firstClient = api.CreateClient();
        using var secondClient = api.CreateClient();
        var firstPost = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var posts = 0;
        api.InterceptAsync = async request =>
        {
            if (request.Method != HttpMethod.Post || request.Path != MarkersPath || ++posts != 1) return null;
            firstPost.SetResult(true);
            await releaseFirst.Task;
            return Response(HttpStatusCode.Conflict);
        };
        var first = Bootstrap.RunAsync(firstClient, "drn-project", () => "first-token");
        await firstPost.Task;
        try
        {
            await Bootstrap.RunAsync(secondClient, "drn-project", () => "second-token");
        }
        finally
        {
            releaseFirst.SetResult(true);
        }
        var losingRun = () => first;
        (await losingRun.Should().ThrowAsync<BootstrapException>()).Which.Message.Should().Contain("already claimed");
        api.Writes.Should().HaveCount(2);
        api.Writes.Should().OnlyContain(request => request.Authorization == "Bearer second-token");
        var saved = Snapshot(api);
        api.InterceptAsync = null;
        await Bootstrap.RunAsync(firstClient, "drn-project", () => "first-token");
        Snapshot(api).Should().Equal(saved);
        api.Writes.Should().HaveCount(2);
    }

    private static KubernetesApi ExistingPair()
    {
        var api = new KubernetesApi { Marker = Marker() };
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

    private static BootstrapInitializationMarker Marker() => new()
    {
        ApiVersion = "v1",
        Kind = "ConfigMap",
        Metadata = new BootstrapMetadata { Name = MarkerName, Namespace = "drn-project" },
        Immutable = true,
        Data = new Dictionary<string, string?> { ["version"] = "1" }
    };

    private sealed record ApiRequest(HttpMethod Method, string Path, string? Authorization, BootstrapSecret? Body,
        BootstrapInitializationMarker? MarkerBody)
    {
        public string? Name => Body?.Metadata?.Name ?? MarkerBody?.Metadata?.Name;
    }

    private sealed class KubernetesApi : HttpMessageHandler
    {
        private const string SecretsPath = "/api/v1/namespaces/drn-project/secrets";
        private static readonly string[] MarkerPropertyNames = ["apiVersion", "kind", "metadata", "immutable", "data"];
        private static readonly string[] MarkerMetadataPropertyNames = ["name", "namespace"];
        public Dictionary<string, BootstrapSecret> Secrets { get; } = [];
        public BootstrapInitializationMarker? Marker { get; set; }
        public List<ApiRequest> Requests { get; } = [];
        public IEnumerable<ApiRequest> Writes => Requests.Where(request => request.Method != HttpMethod.Get && request.Path == SecretsPath);
        public IEnumerable<ApiRequest> MarkerWrites => Requests.Where(request => request.Method != HttpMethod.Get && request.Path == MarkersPath);
        public Func<ApiRequest, HttpResponseMessage?>? Intercept { get; set; }
        public Func<ApiRequest, Task<HttpResponseMessage?>>? InterceptAsync { get; set; }

        public HttpClient CreateClient() => new(this, disposeHandler: false)
        {
            BaseAddress = new Uri("https://kubernetes.default.svc")
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var isMarker = request.RequestUri!.AbsolutePath.StartsWith(MarkersPath, StringComparison.Ordinal);
            var body = json is null || isMarker ? null : Json.Deserialize(json);
            var marker = json is not null && isMarker ? MarkerJson.Deserialize(json) : null;
            if (json is not null && !isMarker)
                SecretWireContract.Verify(json, body!.Metadata!.Name, "drn-project");
            if (json is not null && isMarker)
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                root.EnumerateObject().Select(property => property.Name)
                    .Should().BeEquivalentTo(MarkerPropertyNames);
                root.GetProperty("apiVersion").GetString().Should().Be("v1");
                root.GetProperty("kind").GetString().Should().Be("ConfigMap");
                root.GetProperty("immutable").GetBoolean().Should().BeTrue();
                var metadata = root.GetProperty("metadata");
                metadata.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(MarkerMetadataPropertyNames);
                metadata.GetProperty("name").GetString().Should().Be(MarkerName);
                metadata.GetProperty("namespace").GetString().Should().Be("drn-project");
                root.GetProperty("data").EnumerateObject().Should().ContainSingle();
                root.GetProperty("data").GetProperty("version").GetString().Should().Be("1");
            }
            var captured = new ApiRequest(request.Method, request.RequestUri!.AbsolutePath,
                request.Headers.Authorization?.ToString(), body, marker);
            Requests.Add(captured);
            if (InterceptAsync is not null)
            {
                var asyncResponse = await InterceptAsync(captured);
                if (asyncResponse is not null) return asyncResponse;
            }
            var intercepted = Intercept?.Invoke(captured);
            if (intercepted is not null) return intercepted;

            if (request.Method == HttpMethod.Get && captured.Path == $"{MarkersPath}/{MarkerName}")
                return Marker is null ? Response(HttpStatusCode.NotFound) : Response(HttpStatusCode.OK, MarkerJson.Serialize(Marker));
            if (request.Method == HttpMethod.Post && captured.Path == MarkersPath)
            {
                if (Marker is not null) return Response(HttpStatusCode.Conflict);
                Marker = MarkerJson.Clone(marker!);
                return Response(HttpStatusCode.Created, MarkerJson.Serialize(Marker));
            }

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
