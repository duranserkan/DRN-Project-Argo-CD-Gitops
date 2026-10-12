using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;

namespace ApplicationSecretBootstrap.Tests;

/// <summary>Checks outgoing Secret JSON independently of the deployed models and DRN serializer conventions.</summary>
internal sealed class SecretWireContract
{
    // Reject extra fields, including data and ownership metadata, even when their values are null.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    [JsonPropertyName("apiVersion")]
    public required string ApiVersion { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("metadata")]
    public required SecretWireMetadata Metadata { get; init; }

    [JsonPropertyName("stringData")]
    public required Dictionary<string, string?> StringData { get; init; }

    internal static void Verify(string json, string? name, string namespaceName)
    {
        var secret = JsonSerializer.Deserialize<SecretWireContract>(json, Options);
        secret.Should().NotBeNull();
        secret.ApiVersion.Should().Be("v1");
        secret.Kind.Should().Be("Secret");
        secret.Type.Should().Be("Opaque");
        secret.StringData.Should().NotBeNull();
        secret.Metadata.Should().NotBeNull();
        secret.Metadata.Name.Should().Be(name);
        secret.Metadata.Namespace.Should().Be(namespaceName);
    }
}

/// <summary>The exact metadata fields permitted on a bootstrap Secret creation request.</summary>
internal sealed class SecretWireMetadata
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("namespace")]
    public required string Namespace { get; init; }
}
