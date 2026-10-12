using System.Text.Json;

namespace ApplicationSecretBootstrap.Tests;

/// <summary>Serializes fixtures with the owning bootstrap's options, separately from wire-contract validation.</summary>
internal sealed class FixtureJson<T>(JsonSerializerOptions options) where T : class
{
    internal string Serialize(T value) => JsonSerializer.Serialize(value, options);

    internal T Deserialize(string json) => JsonSerializer.Deserialize<T>(json, options)
        ?? throw new InvalidOperationException("Expected a non-null fixture object.");

    internal T Clone(T value) => Deserialize(Serialize(value));

    internal string[] Snapshot(IReadOnlyDictionary<string, T> values) => values
        .OrderBy(pair => pair.Key).Select(pair => Serialize(pair.Value)).ToArray();
}
