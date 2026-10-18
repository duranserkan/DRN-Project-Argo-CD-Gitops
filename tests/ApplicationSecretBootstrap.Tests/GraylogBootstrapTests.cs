using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using DRN.Framework.Testing.DataAttributes;
using DRN.GitOps.Bootstraps.Graylog;
using Xunit;
using static ApplicationSecretBootstrap.Tests.BootstrapFixtures;

namespace ApplicationSecretBootstrap.Tests;

/// <summary>Verifies the deployed Graylog bootstrap with a simulated Kubernetes API and polling clock.</summary>
[SuppressMessage("ReSharper", "AccessToDisposedClosure")]
public class GraylogBootstrapTests
{
    private static readonly FixtureJson<GraylogSecret> Json = new(GraylogBootstrap.JsonOptions);
    private static readonly FixtureJson<GraylogInitializationMarker> MarkerJson = new(GraylogBootstrap.JsonOptions);
    private const string MarkerName = "graylog-credential-bootstrap-initialized";

    private const string Credentials = "graylog-credentials";
    private const string Database = "mongo-graylog-password";
    private const string Admin = "graylog-admin-password";
    private const string Connection = "mongodb-graylog-graylog";
    private const string PasswordKey = "password";
    private const string ConnectionStringKey = "connectionString.standard";
    private const string RootUsernameKey = "GRAYLOG_ROOT_USERNAME";
    private const string RootPasswordHashKey = "GRAYLOG_ROOT_PASSWORD_SHA2";
    private const string PasswordSecretKey = "GRAYLOG_PASSWORD_SECRET";
    private const string MongoDbUriKey = "GRAYLOG_MONGODB_URI";
    private const string Uri = "mongodb://synthetic:password@mongodb.graylog:27017/graylog?ssl=true";

    [Fact]
    public async Task Fresh_Install_Should_Create_Compatible_Secrets_And_Preserve_Them_On_Rerun()
    {
        using var api = new Api { PublishConnectionAfterDelay = true };
        await api.Run();

        api.Writes.Should().Equal(Database, Admin, Credentials);
        api.MarkerWrites.Should().Be(1);
        var savedMarker = MarkerJson.Serialize(api.Marker!);
        Read(api.Secrets[Database], PasswordKey).Should().MatchRegex("^[A-Za-z0-9_-]{64}$");
        var password = Read(api.Secrets[Admin], PasswordKey);
        password.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        var credentials = api.Secrets[Credentials];
        Read(credentials, RootUsernameKey).Should().Be("admin");
        Read(credentials, RootPasswordHashKey).Should().Be(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant());
        Read(credentials, PasswordSecretKey).Should().MatchRegex("^[A-Za-z0-9_-]{64}$");
        Read(credentials, MongoDbUriKey).Should().Be(Uri);
        api.Delays.Should().Equal(TimeSpan.FromSeconds(5));
        var before = Snapshot(api);
        api.Writes.Clear();

        await api.Run();

        api.Writes.Should().BeEmpty();
        Snapshot(api).Should().Equal(before);
        api.MarkerWrites.Should().Be(1);
        MarkerJson.Serialize(api.Marker!).Should().Be(savedMarker);
    }

    [Theory]
    [DataInlineUnit(Database, false)]
    [DataInlineUnit(Admin, false)]
    [DataInlineUnit(Connection, false)]
    [DataInlineUnit(Database, true)]
    [DataInlineUnit(Admin, true)]
    [DataInlineUnit(Connection, true)]
    public async Task Missing_Credentials_With_Any_Surviving_Secret_Should_Fail_Without_Writes(string survivor, bool enrolled)
    {
        using var api = new Api { Marker = enrolled ? Marker() : null };
        api.Secrets[survivor] = Secret(survivor, PasswordKey, "synthetic");
        var before = Snapshot(api);
        var act = api.Run;

        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().Contain("Refusing encryption secret regeneration");
        api.Writes.Should().BeEmpty();
        Snapshot(api).Should().Equal(before);
        api.MarkerWrites.Should().Be(0);
    }

    [Theory]
    [DataInlineUnit(false)]
    [DataInlineUnit(true)]
    public async Task Missing_Database_Password_Should_Fail_Without_Writes(bool enrolled)
    {
        using var api = Existing();
        api.Marker = enrolled ? Marker() : null;
        api.Secrets.Remove(Database);
        var act = api.Run;
        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().Contain("Refusing password regeneration");
        api.Writes.Should().BeEmpty();
        api.MarkerWrites.Should().Be(0);
    }

    [Fact]
    public async Task Existing_Credentials_Should_Preserve_Custom_Values_Without_Requiring_Admin_Or_Connection()
    {
        using var api = Existing();
        var before = Snapshot(api);
        await api.Run();
        Snapshot(api).Should().Equal(before);
        api.Writes.Should().BeEmpty();
        api.Gets.Should().Equal(Credentials, Database);
    }

    public static IEnumerable<object?[]> InvalidFields()
    {
        return new[] { PasswordKey, RootUsernameKey, RootPasswordHashKey, PasswordSecretKey, MongoDbUriKey } .SelectMany(
                _ => new[] { null, "", "%%%", "YQ==\n", "/w==" }, 
                (key, encoded) => (object?[])[key == PasswordKey ? Database : Credentials, key, encoded]);
    }

    [Theory]
    [DataMemberUnit(nameof(InvalidFields))]
    public async Task Invalid_Existing_Fields_Should_Fail_Without_Writes(string name, string key, string? encoded)
    {
        using var api = Existing();
        api.Secrets[name].Data![key] = encoded;
        var act = api.Run;
        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().Contain(name).And.Contain(key);
        api.Writes.Should().BeEmpty();
        api.MarkerWrites.Should().Be(0);
    }

    [Fact]
    public async Task Timeout_Should_Leave_Passwords_Intact_And_Reject_A_Partial_Retry()
    {
        using var api = new Api();
        var act = api.Run;
        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().Contain("Timed out");
        api.Delays.Should().HaveCount(180);
        api.Writes.Should().Equal(Database, Admin);
        var before = Snapshot(api);
        api.Writes.Clear();
        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().Contain("Refusing encryption secret regeneration");
        Snapshot(api).Should().Equal(before);
        api.Writes.Should().BeEmpty();
    }

    [Theory]
    [DataInlineUnit(Database)]
    [DataInlineUnit(Admin)]
    [DataInlineUnit(Credentials)]
    public async Task Create_Conflict_Should_Reuse_The_Winner(string name)
    {
        using var api = new Api { PublishConnectionAfterDelay = true };
        var winner = name == Credentials ? ExistingCredentials() : Secret(name, PasswordKey, "winning-password");
        api.Intercept = (method, target) =>
        {
            if (method != HttpMethod.Post || target != name) return null;
            api.Secrets[name] = Json.Clone(winner);
            return Response(HttpStatusCode.Conflict, "private-conflict-body");
        };
        await api.Run();
        Json.Serialize(api.Secrets[name]).Should().Be(Json.Serialize(winner));
        if (name == Admin)
            Read(api.Secrets[Credentials], RootPasswordHashKey).Should().Be(
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("winning-password"))).ToLowerInvariant());
    }

    [Fact]
    public async Task Disappearing_Conflict_Winner_Should_Fail()
    {
        using var api = new Api();
        api.Intercept = (method, name) => method == HttpMethod.Post && name != MarkerName
            ? Response(HttpStatusCode.Conflict, "private") : null;
        var act = api.Run;
        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().Contain("Secret disappeared during bootstrap");
        api.MarkerWrites.Should().Be(1);
        api.Secrets.Should().BeEmpty();
    }

    [Theory]
    [DataInlineUnit(401)]
    [DataInlineUnit(403)]
    [DataInlineUnit(500)]
    public async Task Api_Failure_Should_Not_Expose_Response_Bodies(int status)
    {
        using var api = new Api();
        api.Intercept = (_, _) => Response((HttpStatusCode)status, "private-secret-body");
        var act = api.Run;
        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message
            .Should().Contain(status.ToString()).And.NotContain("private-secret-body");
        api.Writes.Should().BeEmpty();
    }

    [Theory]
    [DataInlineUnit("not-json")]
    [DataInlineUnit("null")]
    [DataInlineUnit("[]")]
    [DataInlineUnit("{\"data\":123,\"private\":\"private-secret-body\"}")]
    [DataInlineUnit("{\"data\":[],\"private\":\"private-secret-body\"}")]
    [DataInlineUnit("{\"metadata\":\"private-secret-body\"}")]
    [DataInlineUnit("{\"data\":{\"GRAYLOG_ROOT_USERNAME\":123},\"private\":\"private-secret-body\"}")]
    public async Task Invalid_Api_Object_Should_Fail(string body)
    {
        using var api = new Api();
        api.Intercept = (_, _) => Response(HttpStatusCode.OK, body);
        var act = api.Run;
        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().NotContain("private-secret-body");
        api.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Each_Request_Should_Read_A_Fresh_Token()
    {
        using var api = new Api { PublishConnectionAfterDelay = true };
        await api.Run();
        api.Tokens.Should().Equal(Enumerable.Range(1, api.Tokens.Count).Select(i => $"synthetic-{i}"));
    }

    [Theory]
    [DataInlineUnit(Admin, PasswordKey)]
    [DataInlineUnit(Connection, ConnectionStringKey)]
    public async Task Malformed_New_Admin_Or_Operator_Value_Should_Prevent_Credentials_Creation(string name, string key)
    {
        using var api = new Api { PublishConnectionAfterDelay = true };
        api.Intercept = (method, target) =>
        {
            if (method != HttpMethod.Get || target != name || !api.Secrets.ContainsKey(Database)) return null;
            var secret = Secret(name, key, "synthetic");
            secret.Data![key] = "/w==";
            return Response(HttpStatusCode.OK, Json.Serialize(secret));
        };
        var act = api.Run;
        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().Contain("invalid UTF-8");
        api.Secrets.Should().NotContainKey(Credentials);
    }

    [Fact]
    public async Task Empty_Operator_Uri_Should_Keep_Polling()
    {
        using var api = new Api { PublishConnectionAfterDelay = true };
        var polls = 0;
        api.Intercept = (method, target) =>
        {
            if (method != HttpMethod.Get || target != Connection || !api.Secrets.ContainsKey(Database) || polls++ > 0) return null;
            var secret = Secret(Connection, ConnectionStringKey, "unused");
            secret.Data![ConnectionStringKey] = "";
            return Response(HttpStatusCode.OK, Json.Serialize(secret));
        };
        await api.Run();
        api.Delays.Should().HaveCount(1);
        Read(api.Secrets[Credentials], MongoDbUriKey).Should().Be(Uri);
    }

    [Theory]
    [DataInlineUnit("500", false)]
    [DataInlineUnit("503", false)]
    [DataInlineUnit("599", false)]
    [DataInlineUnit("task-canceled", false)]
    [DataInlineUnit("timeout", false)]
    [DataInlineUnit("500", true)]
    [DataInlineUnit("503", true)]
    [DataInlineUnit("599", true)]
    [DataInlineUnit("task-canceled", true)]
    [DataInlineUnit("timeout", true)]
    public async Task Transient_Operator_Read_Failures_Should_Retry_Within_The_Original_Deadline(string failure, bool persistent)
    {
        using var api = new Api { PublishConnectionAfterDelay = true };
        var polls = 0;
        api.Intercept = (method, target) =>
        {
            if (method != HttpMethod.Get || target != Connection || !api.Secrets.ContainsKey(Database)) return null;
            polls++;
            // Bound a deadline regression so resetting the timer fails this test instead of looping forever.
            if (polls > 180) throw new InvalidOperationException("Operator polling exceeded its original deadline.");
            if (!persistent && polls > 1) return null;
            return failure switch
            {
                "task-canceled" => throw new TaskCanceledException("private-timeout-details"),
                "timeout" => throw new TimeoutException("private-timeout-details"),
                _ => Response((HttpStatusCode)int.Parse(failure), "private-secret-body")
            };
        };

        if (persistent)
        {
            var act = api.Run;
            (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message
                .Should().Contain("Timed out").And.NotContain("private-timeout-details").And.NotContain("private-secret-body");
            polls.Should().Be(180);
            api.Delays.Should().Equal(Enumerable.Repeat(TimeSpan.FromSeconds(5), 180));
            api.Writes.Should().Equal(Database, Admin);
            api.Secrets.Should().NotContainKey(Credentials);
        }
        else
        {
            await api.Run();
            polls.Should().Be(2);
            api.Delays.Should().Equal(TimeSpan.FromSeconds(5));
            api.Writes.Should().Equal(Database, Admin, Credentials);
            Read(api.Secrets[Credentials], MongoDbUriKey).Should().Be(Uri);
        }
    }

    [Theory]
    [DataInlineUnit(401)]
    [DataInlineUnit(403)]
    public async Task Operator_Read_Authorization_Failure_Should_Stop_Without_Retry(int status)
    {
        using var api = new Api { PublishConnectionAfterDelay = true };
        var polls = 0;
        api.Intercept = (method, target) =>
        {
            if (method != HttpMethod.Get || target != Connection || !api.Secrets.ContainsKey(Database)) return null;
            polls++;
            return Response((HttpStatusCode)status, "private-secret-body");
        };
        var act = api.Run;

        var error = (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which;
        error.StatusCode.Should().Be((HttpStatusCode)status);
        error.Message.Should().Contain(status.ToString()).And.NotContain("private-secret-body");
        polls.Should().Be(1);
        api.Delays.Should().BeEmpty();
        api.Writes.Should().Equal(Database, Admin);
        api.Secrets.Should().NotContainKey(Credentials);
    }

    [Fact]
    public async Task Failed_Final_Write_Should_Leave_Passwords_Intact_And_Reject_Retry()
    {
        using var api = new Api { PublishConnectionAfterDelay = true };
        api.Intercept = (method, name) => method == HttpMethod.Post && name == Credentials
            ? Response(HttpStatusCode.Forbidden, "private-response") : null;
        var act = api.Run;
        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().Contain("403");
        var before = Snapshot(api);
        api.Intercept = null;
        api.Writes.Clear();
        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().Contain("Refusing encryption secret regeneration");
        Snapshot(api).Should().Equal(before);
        api.Writes.Should().BeEmpty();
    }

    private static Api Existing()
    {
        var api = new Api();
        api.Secrets[Database] = Secret(Database, PasswordKey, "existing-password");
        api.Secrets[Credentials] = ExistingCredentials();
        return api;
    }

    [Fact]
    public async Task Total_Loss_Should_Not_Be_Treated_As_A_Fresh_Install()
    {
        using var api = new Api { PublishConnectionAfterDelay = true };
        await api.Run();
        var marker = MarkerJson.Serialize(api.Marker!);
        api.Secrets.Clear();
        api.Writes.Clear();
        var act = api.Run;
        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().Contain("already claimed");
        api.Writes.Should().BeEmpty();
        api.Secrets.Should().BeEmpty();
        MarkerJson.Serialize(api.Marker!).Should().Be(marker);
        api.MarkerWrites.Should().Be(1);
    }

    [Fact]
    public async Task Existing_Installation_Should_Enroll_Without_Rotating_And_Remain_Protected()
    {
        using var api = Existing();
        var before = Snapshot(api);
        await api.Run();
        api.MarkerWrites.Should().Be(1);
        Snapshot(api).Should().Equal(before);
        api.Writes.Should().BeEmpty();
        api.Secrets.Clear();
        var act = api.Run;
        await act.Should().ThrowAsync<GraylogBootstrapException>();
        api.Writes.Should().BeEmpty();
        // Restoring the authoritative pair permits retries without changing the claim.
        api.Secrets[Database] = Secret(Database, PasswordKey, "restored-password");
        api.Secrets[Credentials] = ExistingCredentials();
        await api.Run();
        api.MarkerWrites.Should().Be(1);
        api.Writes.Should().BeEmpty();
    }

    [Theory]
    [DataInlineUnit(false)]
    [DataInlineUnit(true)]
    public async Task Marker_Conflict_Should_Never_Authorize_Generation(bool winnerSurvives)
    {
        using var api = new Api();
        api.Intercept = (method, name) =>
        {
            if (method != HttpMethod.Post || name != MarkerName) return null;
            if (winnerSurvives) api.Marker = Marker();
            return Response(HttpStatusCode.Conflict);
        };
        var act = api.Run;
        await act.Should().ThrowAsync<GraylogBootstrapException>();
        api.Writes.Should().BeEmpty();
        api.Secrets.Should().BeEmpty();
    }

    [Theory]
    [DataInlineUnit(false)]
    [DataInlineUnit(true)]
    public async Task Ambiguous_Claim_Should_Block_This_Run_And_Retry(bool timeout)
    {
        using var api = new Api();
        api.Intercept = (method, name) =>
        {
            if (method != HttpMethod.Post || name != MarkerName) return null;
            api.Marker = Marker();
            if (timeout) throw new TaskCanceledException("private-timeout");
            return Response(HttpStatusCode.ServiceUnavailable, "private-body");
        };
        var act = api.Run;
        if (timeout) await act.Should().ThrowAsync<TaskCanceledException>();
        else (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().NotContain("private-body");
        api.Writes.Should().BeEmpty();
        api.Intercept = null;
        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().Contain("already claimed");
        api.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Failure_Before_First_Secret_Should_Leave_Claim_And_Block_Retry()
    {
        using var api = new Api();
        api.Intercept = (method, name) => method == HttpMethod.Post && name == Database
            ? Response(HttpStatusCode.Forbidden) : null;
        var act = api.Run;
        await act.Should().ThrowAsync<GraylogBootstrapException>();
        api.Marker.Should().NotBeNull();
        api.Secrets.Should().BeEmpty();
        api.Intercept = null;
        api.Writes.Clear();
        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().Contain("already claimed");
        api.Writes.Should().BeEmpty();
    }

    [Theory]
    [DataInlineUnit("get", 403)]
    [DataInlineUnit("get", 500)]
    [DataInlineUnit("post", 403)]
    [DataInlineUnit("post", 500)]
    [DataInlineUnit("post", 200)]
    public async Task Marker_Api_Failures_Should_Prevent_Writes(string verb, int status)
    {
        using var api = new Api();
        api.Intercept = (method, name) => name == MarkerName && method == (verb == "get" ? HttpMethod.Get : HttpMethod.Post)
            ? Response((HttpStatusCode)status, "private-body") : null;
        var act = api.Run;
        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().NotContain("private-body");
        api.Writes.Should().BeEmpty();
    }

    [Theory]
    [DataInlineUnit("get", "not-json")]
    [DataInlineUnit("post", "not-json")]
    [DataInlineUnit("get", "null")]
    [DataInlineUnit("post", "[]")]
    [DataInlineUnit("get", "{}")]
    [DataInlineUnit("post", "{\"immutable\":\"private-value\"}")]
    public async Task Malformed_Marker_Response_Should_Prevent_Writes(string verb, string body)
    {
        using var api = new Api();
        api.Intercept = (method, name) => name == MarkerName && method == (verb == "get" ? HttpMethod.Get : HttpMethod.Post)
            ? Response(method == HttpMethod.Get ? HttpStatusCode.OK : HttpStatusCode.Created, body) : null;
        var act = api.Run;
        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().NotContain("private-value");
        api.Writes.Should().BeEmpty();
    }

    [Theory]
    [DataInlineUnit("version")]
    [DataInlineUnit("immutable")]
    [DataInlineUnit("name")]
    [DataInlineUnit("namespace")]
    [DataInlineUnit("kind")]
    [DataInlineUnit("apiVersion")]
    [DataInlineUnit("data")]
    [DataInlineUnit("extra")]
    public async Task Invalid_Marker_Should_Block_Existing_Installation(string field)
    {
        using var api = Existing();
        api.Marker = Marker();
        switch (field)
        {
            case "version": api.Marker.Data!["version"] = "private-value"; break;
            case "immutable": api.Marker.Immutable = false; break;
            case "name": api.Marker.Metadata!.Name = "wrong"; break;
            case "namespace": api.Marker.Metadata!.Namespace = "wrong"; break;
            case "kind": api.Marker.Kind = "Secret"; break;
            case "apiVersion": api.Marker.ApiVersion = "v2"; break;
            case "data": api.Marker.Data = null; break;
            case "extra": api.Marker.Data!["extra"] = "private-value"; break;
        }
        var before = Snapshot(api);
        var act = api.Run;
        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message
            .Should().Contain("marker is invalid").And.NotContain("private-value");
        api.Writes.Should().BeEmpty();
        api.MarkerWrites.Should().Be(0);
        Snapshot(api).Should().Equal(before);
    }

    private static GraylogInitializationMarker Marker() => new()
    {
        ApiVersion = "v1", Kind = "ConfigMap",
        Metadata = new GraylogMetadata { Name = MarkerName, Namespace = "graylog" },
        Immutable = true, Data = new Dictionary<string, string?> { ["version"] = "1" }
    };

    private static GraylogSecret ExistingCredentials()
    {
        var secret = Secret(Credentials, RootUsernameKey, "custom-admin");
        foreach (var key in new[] { RootPasswordHashKey, PasswordSecretKey, MongoDbUriKey, "ExtraSetting" })
            secret.Data![key] = Encode($"existing-{key}");
        return secret;
    }

    private static string Read(GraylogSecret secret, string key) => Decode(secret.Data![key]!);
    private static string[] Snapshot(Api api) => Json.Snapshot(api.Secrets);
    private static GraylogSecret Secret(string name, string key, string value) => new()
    {
        ApiVersion = "v1",
        Kind = "Secret",
        Type = "Opaque",
        Metadata = new GraylogMetadata { Name = name, Namespace = "graylog" },
        Data = new Dictionary<string, string?> { [key] = Encode(value) }
    };

    private sealed class Clock : TimeProvider
    {
        public long Timestamp { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Timestamp;
    }

    private sealed class Api : HttpMessageHandler
    {
        public Dictionary<string, GraylogSecret> Secrets { get; } = new();
        public GraylogInitializationMarker? Marker { get; set; }
        public int MarkerWrites { get; private set; }
        public List<string> Writes { get; } = [];
        public List<string> Gets { get; } = [];
        public List<string> Tokens { get; } = [];
        public List<TimeSpan> Delays { get; } = [];
        public bool PublishConnectionAfterDelay { get; init; }
        public Func<HttpMethod, string, HttpResponseMessage?>? Intercept { get; set; }
        private readonly Clock _clock = new();
        private int _token;

        public async Task Run()
        {
            using var client = new HttpClient(this, disposeHandler: false);
            client.BaseAddress = new Uri("https://kubernetes.invalid");
            await GraylogBootstrap.RunAsync(client, "graylog", () => $"synthetic-{++_token}", _clock, Delay);
        }

        private Task Delay(TimeSpan duration)
        {
            Delays.Add(duration);
            _clock.Timestamp += duration.Ticks;
            if (PublishConnectionAfterDelay)
                Secrets[Connection] = Secret(Connection, ConnectionStringKey, Uri);
            return Task.CompletedTask;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri!.AbsolutePath.Should().StartWith("/api/v1/namespaces/graylog/");
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            Tokens.Add(request.Headers.Authorization.Parameter!);
            var json = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (request.RequestUri.AbsolutePath.StartsWith("/api/v1/namespaces/graylog/configmaps", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Post)
                {
                    MarkerWrites++;
                    using var document = JsonDocument.Parse(json!);
                    var root = document.RootElement;
                    root.EnumerateObject().Select(property => property.Name).Should()
                        .BeEquivalentTo(new[] { "apiVersion", "kind", "metadata", "immutable", "data" });
                    root.GetProperty("metadata").EnumerateObject().Select(property => property.Name).Should()
                        .BeEquivalentTo(new[] { "name", "namespace" });
                    root.GetProperty("apiVersion").GetString().Should().Be("v1");
                    root.GetProperty("kind").GetString().Should().Be("ConfigMap");
                    root.GetProperty("immutable").GetBoolean().Should().BeTrue();
                    root.GetProperty("metadata").GetProperty("name").GetString().Should().Be(MarkerName);
                    root.GetProperty("metadata").GetProperty("namespace").GetString().Should().Be("graylog");
                    root.GetProperty("data").EnumerateObject().Should().ContainSingle();
                    root.GetProperty("data").GetProperty("version").GetString().Should().Be("1");
                    MarkerJson.Serialize(MarkerJson.Deserialize(json!)).Should().Be(MarkerJson.Serialize(GraylogBootstrapTests.Marker()));
                }
                else request.Method.Should().Be(HttpMethod.Get);
                request.RequestUri.AbsolutePath.Should().Be(request.Method == HttpMethod.Post
                    ? "/api/v1/namespaces/graylog/configmaps" : $"/api/v1/namespaces/graylog/configmaps/{MarkerName}");
                var markerResponse = Intercept?.Invoke(request.Method, MarkerName);
                if (markerResponse is not null) return markerResponse;
                if (request.Method == HttpMethod.Get)
                    return Marker is null ? Response(HttpStatusCode.NotFound) : Response(HttpStatusCode.OK, MarkerJson.Serialize(Marker));
                if (Marker is not null) return Response(HttpStatusCode.Conflict);
                Marker = MarkerJson.Deserialize(json!);
                return Response(HttpStatusCode.Created, MarkerJson.Serialize(Marker));
            }
            request.RequestUri.AbsolutePath.Should().StartWith("/api/v1/namespaces/graylog/secrets");
            var body = json is null ? null : Json.Deserialize(json);
            var name = body?.Metadata?.Name ?? request.RequestUri.Segments.Last();
            if (request.Method == HttpMethod.Get) Gets.Add(name);
            else if (request.Method == HttpMethod.Post) Writes.Add(name);
            else throw new InvalidOperationException("Unexpected API method.");
            if (request.Method == HttpMethod.Post) Marker.Should().NotBeNull("the claim must precede every Secret write");
            var intercepted = Intercept?.Invoke(request.Method, name);
            if (intercepted is not null) return intercepted;
            if (request.Method == HttpMethod.Get)
                return Secrets.TryGetValue(name, out var secret)
                    ? Response(HttpStatusCode.OK, Json.Serialize(secret)) : Response(HttpStatusCode.NotFound);
            body!.ApiVersion.Should().Be("v1");
            body.Kind.Should().Be("Secret");
            body.Type.Should().Be("Opaque");
            body.Metadata!.Namespace.Should().Be("graylog");
            SecretWireContract.Verify(json!, name, "graylog");
            if (Secrets.ContainsKey(name)) return Response(HttpStatusCode.Conflict);
            // Kubernetes persists stringData as base64 data and does not return the write-only input.
            var saved = new GraylogSecret
            {
                ApiVersion = body.ApiVersion,
                Kind = body.Kind,
                Type = body.Type,
                Metadata = body.Metadata,
                Data = body.StringData!.ToDictionary(pair => pair.Key, pair => (string?)Encode(pair.Value!))
            };
            Secrets.Add(name, saved);
            return Response(HttpStatusCode.Created, Json.Serialize(saved));
        }
    }
}
