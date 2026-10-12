using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using System.Text;
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
    }

    [Theory]
    [DataInlineUnit(Database)]
    [DataInlineUnit(Admin)]
    [DataInlineUnit(Connection)]
    public async Task Missing_Credentials_With_Any_Surviving_Secret_Should_Fail_Without_Writes(string survivor)
    {
        using var api = new Api();
        api.Secrets[survivor] = Secret(survivor, PasswordKey, "synthetic");
        var before = Snapshot(api);
        var act = api.Run;

        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().Contain("Refusing encryption secret regeneration");
        api.Writes.Should().BeEmpty();
        Snapshot(api).Should().Equal(before);
    }

    [Fact]
    public async Task Missing_Database_Password_Should_Fail_Without_Writes()
    {
        using var api = Existing();
        api.Secrets.Remove(Database);
        var act = api.Run;
        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().Contain("Refusing password regeneration");
        api.Writes.Should().BeEmpty();
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
        api.Intercept = (method, _) => method == HttpMethod.Post ? Response(HttpStatusCode.Conflict, "private") : null;
        var act = api.Run;
        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().Contain("disappeared");
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
            request.RequestUri!.AbsolutePath.Should().StartWith("/api/v1/namespaces/graylog/secrets");
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            Tokens.Add(request.Headers.Authorization.Parameter!);
            var json = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var body = json is null ? null : Json.Deserialize(json);
            var name = body?.Metadata?.Name ?? request.RequestUri.Segments.Last();
            if (request.Method == HttpMethod.Get) Gets.Add(name);
            else if (request.Method == HttpMethod.Post) Writes.Add(name);
            else throw new InvalidOperationException("Unexpected API method.");
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
