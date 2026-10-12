using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using DRN.Framework.Testing.DataAttributes;
using Xunit;

namespace ApplicationSecretBootstrap.Tests;

public class GraylogBootstrapTests
{
    private const string Credentials = "graylog-credentials";
    private const string Database = "mongo-graylog-password";
    private const string Admin = "graylog-admin-password";
    private const string Connection = "mongodb-graylog-graylog";
    private const string Uri = "mongodb://synthetic:password@mongodb.graylog:27017/graylog?ssl=true";

    [Fact]
    public async Task Fresh_Install_Should_Create_Compatible_Secrets_And_Preserve_Them_On_Rerun()
    {
        using var api = new Api { PublishConnectionAfterDelay = true };
        await api.Run();

        api.Writes.Should().Equal(Database, Admin, Credentials);
        Read(api.Secrets[Database], "password").Should().MatchRegex("^[A-Za-z0-9_-]{64}$");
        var password = Read(api.Secrets[Admin], "password");
        password.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        var credentials = api.Secrets[Credentials];
        Read(credentials, "GRAYLOG_ROOT_USERNAME").Should().Be("admin");
        Read(credentials, "GRAYLOG_ROOT_PASSWORD_SHA2").Should().Be(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant());
        Read(credentials, "GRAYLOG_PASSWORD_SECRET").Should().MatchRegex("^[A-Za-z0-9_-]{64}$");
        Read(credentials, "GRAYLOG_MONGODB_URI").Should().Be(Uri);
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
        api.Secrets[survivor] = Secret(survivor, "password", "synthetic");
        var before = Snapshot(api);
        var act = () => api.Run();

        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().Contain("Refusing encryption secret regeneration");
        api.Writes.Should().BeEmpty();
        Snapshot(api).Should().Equal(before);
    }

    [Fact]
    public async Task Missing_Database_Password_Should_Fail_Without_Writes()
    {
        using var api = Existing();
        api.Secrets.Remove(Database);
        var act = () => api.Run();
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
        foreach (var key in new[] { "password", "GRAYLOG_ROOT_USERNAME", "GRAYLOG_ROOT_PASSWORD_SHA2", "GRAYLOG_PASSWORD_SECRET", "GRAYLOG_MONGODB_URI" })
        foreach (var encoded in new string?[] { null, "", "%%%", "YQ==\n", "/w==" })
            yield return [key == "password" ? Database : Credentials, key, encoded];
    }

    [Theory]
    [DataMemberUnit(nameof(InvalidFields))]
    public async Task Invalid_Existing_Fields_Should_Fail_Without_Writes(string name, string key, string? encoded)
    {
        using var api = Existing();
        api.Secrets[name]["data"]![key] = encoded;
        var act = () => api.Run();
        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message.Should().Contain(name).And.Contain(key);
        api.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Timeout_Should_Leave_Passwords_Intact_And_Reject_A_Partial_Retry()
    {
        using var api = new Api();
        var act = () => api.Run();
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
        var winner = name == Credentials ? ExistingCredentials() : Secret(name, "password", "winning-password");
        api.Intercept = (method, target) =>
        {
            if (method != HttpMethod.Post || target != name) return null;
            api.Secrets[name] = (JsonObject)winner.DeepClone();
            return Response(HttpStatusCode.Conflict, "private-conflict-body");
        };
        await api.Run();
        api.Secrets[name].ToJsonString().Should().Be(winner.ToJsonString());
        if (name == Admin)
            Read(api.Secrets[Credentials], "GRAYLOG_ROOT_PASSWORD_SHA2").Should().Be(
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("winning-password"))).ToLowerInvariant());
    }

    [Fact]
    public async Task Disappearing_Conflict_Winner_Should_Fail()
    {
        using var api = new Api();
        api.Intercept = (method, _) => method == HttpMethod.Post ? Response(HttpStatusCode.Conflict, "private") : null;
        var act = () => api.Run();
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
        var act = () => api.Run();
        (await act.Should().ThrowAsync<GraylogBootstrapException>()).Which.Message
            .Should().Contain(status.ToString()).And.NotContain("private-secret-body");
        api.Writes.Should().BeEmpty();
    }

    [Theory]
    [DataInlineUnit("not-json")]
    [DataInlineUnit("null")]
    [DataInlineUnit("[]")]
    public async Task Invalid_Api_Object_Should_Fail(string body)
    {
        using var api = new Api();
        api.Intercept = (_, _) => Response(HttpStatusCode.OK, body);
        var act = () => api.Run();
        await act.Should().ThrowAsync<GraylogBootstrapException>();
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
    [DataInlineUnit(Admin, "password")]
    [DataInlineUnit(Connection, "connectionString.standard")]
    public async Task Malformed_New_Admin_Or_Operator_Value_Should_Prevent_Credentials_Creation(string name, string key)
    {
        using var api = new Api { PublishConnectionAfterDelay = true };
        api.Intercept = (method, target) =>
        {
            if (method != HttpMethod.Get || target != name || !api.Secrets.ContainsKey(Database)) return null;
            var secret = Secret(name, key, "synthetic");
            secret["data"]![key] = "/w==";
            return Response(HttpStatusCode.OK, secret.ToJsonString());
        };
        var act = () => api.Run();
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
            var secret = Secret(Connection, "connectionString.standard", "unused");
            secret["data"]!["connectionString.standard"] = "";
            return Response(HttpStatusCode.OK, secret.ToJsonString());
        };
        await api.Run();
        api.Delays.Should().HaveCount(1);
        Read(api.Secrets[Credentials], "GRAYLOG_MONGODB_URI").Should().Be(Uri);
    }

    [Fact]
    public async Task Failed_Final_Write_Should_Leave_Passwords_Intact_And_Reject_Retry()
    {
        using var api = new Api { PublishConnectionAfterDelay = true };
        api.Intercept = (method, name) => method == HttpMethod.Post && name == Credentials
            ? Response(HttpStatusCode.Forbidden, "private-response") : null;
        var act = () => api.Run();
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
        api.Secrets[Database] = Secret(Database, "password", "existing-password");
        api.Secrets[Credentials] = ExistingCredentials();
        return api;
    }

    private static JsonObject ExistingCredentials()
    {
        var secret = Secret(Credentials, "GRAYLOG_ROOT_USERNAME", "custom-admin");
        foreach (var key in new[] { "GRAYLOG_ROOT_PASSWORD_SHA2", "GRAYLOG_PASSWORD_SECRET", "GRAYLOG_MONGODB_URI", "ExtraSetting" })
            secret["data"]![key] = Encode($"existing-{key}");
        return secret;
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    private static string Read(JsonObject secret, string key) => Encoding.UTF8.GetString(Convert.FromBase64String(secret["data"]![key]!.GetValue<string>()));
    private static string[] Snapshot(Api api) => api.Secrets.OrderBy(pair => pair.Key).Select(pair => pair.Value.ToJsonString()).ToArray();
    private static JsonObject Secret(string name, string key, string value) => new()
    {
        ["metadata"] = new JsonObject { ["name"] = name, ["namespace"] = "graylog" },
        ["data"] = new JsonObject { [key] = Encode(value) }
    };
    private static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };

    private sealed class Clock : TimeProvider
    {
        public long Timestamp { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Timestamp;
    }

    private sealed class Api : HttpMessageHandler
    {
        public Dictionary<string, JsonObject> Secrets { get; } = new();
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
            using var client = new HttpClient(this, disposeHandler: false) { BaseAddress = new System.Uri("https://kubernetes.invalid") };
            await GraylogBootstrap.RunAsync(client, "graylog", () => $"synthetic-{++_token}", _clock, Delay);
        }

        private Task Delay(TimeSpan duration)
        {
            Delays.Add(duration);
            _clock.Timestamp += duration.Ticks;
            if (PublishConnectionAfterDelay)
                Secrets[Connection] = Secret(Connection, "connectionString.standard", Uri);
            return Task.CompletedTask;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri!.AbsolutePath.Should().StartWith("/api/v1/namespaces/graylog/secrets");
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            Tokens.Add(request.Headers.Authorization.Parameter!);
            var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
            var name = body?["metadata"]?["name"]?.GetValue<string>() ?? request.RequestUri.Segments.Last();
            if (request.Method == HttpMethod.Get) Gets.Add(name);
            else if (request.Method == HttpMethod.Post) Writes.Add(name);
            else throw new InvalidOperationException("Unexpected API method.");
            var intercepted = Intercept?.Invoke(request.Method, name);
            if (intercepted is not null) return intercepted;
            if (request.Method == HttpMethod.Get)
                return Secrets.TryGetValue(name, out var secret)
                    ? Response(HttpStatusCode.OK, secret.ToJsonString()) : Response(HttpStatusCode.NotFound, "{}");
            body!["type"]!.GetValue<string>().Should().Be("Opaque");
            body["metadata"]!["namespace"]!.GetValue<string>().Should().Be("graylog");
            if (Secrets.ContainsKey(name)) return Response(HttpStatusCode.Conflict, "{}");
            var saved = (JsonObject)body.DeepClone();
            var data = new JsonObject();
            foreach (var pair in body["stringData"]!.AsObject()) data[pair.Key] = Encode(pair.Value!.GetValue<string>());
            saved.Remove("stringData");
            saved["data"] = data;
            Secrets.Add(name, saved);
            return Response(HttpStatusCode.Created, saved.ToJsonString());
        }
    }
}
