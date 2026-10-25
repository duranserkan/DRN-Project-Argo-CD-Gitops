using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DRN.Framework.Testing.DataAttributes;
using Xunit;
using DependencyPins;

namespace ChartValidation.Tests;

public class DependencyInventoryTests
{
    private static readonly string Root = FindRoot();
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    [Fact]
    public void EveryManifestPinMatchesTheInventory()
    {
        var inventory = JsonSerializer.Deserialize<Inventory>(File.ReadAllText(Path.Combine(Root, "dependencies.json")),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            })!;
        Assert.Equal(2, inventory.SchemaVersion);
        Assert.NotEmpty(inventory.Components);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var component in inventory.Components)
        {
            Assert.True(names.Add(component.Name), $"Duplicate component: {component.Name}");
            Assert.NotEmpty(component.Pins);
            Assert.True(File.Exists(Resolve(component.Provenance.Split('#')[0])), $"Missing provenance: {component.Provenance}");
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var manifest in component.Manifests)
            {
                Resolve(manifest.Path);
                Assert.NotEmpty(manifest.Lines);
                var lines = manifest.Lines.Select(line => Regex.Replace(line, @"\{([A-Za-z0-9]+)\}", match =>
                {
                    var pin = match.Groups[1].Value;
                    used.Add(pin);
                    Assert.True(component.Pins.ContainsKey(pin), $"Unknown pin {component.Name}.{pin}");
                    return component.Pins[pin];
                }, RegexOptions.None, RegexTimeout)).ToArray();
                Assert.True(expected.TryAdd(manifest.Path, lines), $"Duplicate manifest ownership: {manifest.Path}");
            }
            foreach (var reference in component.References)
            {
                used.Add(reference.Pin);
                Assert.True(component.Pins.ContainsKey(reference.Pin), $"Unknown reference pin: {reference.Pin}");
                var matches = Regex.Matches(File.ReadAllText(Resolve(reference.Path)), reference.Pattern,
                    RegexOptions.Multiline, RegexTimeout);
                Assert.True(matches.Count == 1, $"Expected one pin reference in {reference.Path}: {reference.Pattern}");
                Assert.Equal(component.Pins[reference.Pin], matches[0].Groups[1].Value);
            }
            Assert.Equal(component.Pins.Keys.Order(), used.Order());
            Assert.All(component.Pins.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        }

        var actual = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var directory in new[] { "deployment", "infrastructure", "services" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, directory), "*", SearchOption.AllDirectories)
                     .Where(file => Path.GetExtension(file) is ".yaml" or ".yml"))
        {
            // Local chart metadata is not an external dependency.
            if (Path.GetFileName(file) == "Chart.yaml") continue;
            var lines = ExtractPins(File.ReadAllText(file));
            if (lines.Length > 0)
                actual.Add(Path.GetRelativePath(Root, file).Replace('\\', '/'), lines);
        }

        Assert.True(expected.Keys.Order().SequenceEqual(actual.Keys.Order()),
            $"Dependency locations drifted. Missing from inventory: {string.Join(", ", actual.Keys.Except(expected.Keys))}. " +
            $"Missing from manifests: {string.Join(", ", expected.Keys.Except(actual.Keys))}.");
        foreach (var (path, lines) in expected)
            Assert.True(lines.SequenceEqual(actual[path]),
                $"Dependency drift in {path}.\nExpected:\n{string.Join('\n', lines)}\nActual:\n{string.Join('\n', actual[path])}");
    }

    // Read only the scalar pin forms owned by this repository. No general YAML parsing
    // or template execution is attempted. Preserve order and duplicates within a file.
    internal static string[] ExtractPins(string text)
    {
        var result = new List<string>();
        var parameter = "";
        foreach (var original in text.Split('\n'))
        {
            var line = original.Trim();
            if (line.StartsWith('#')) continue;
            var comment = line.IndexOf(" #", StringComparison.Ordinal);
            if (comment >= 0) line = line[..comment].TrimEnd();
            if (line.StartsWith("- name: ", StringComparison.Ordinal)) parameter = line[8..];
            if (line.StartsWith("- https://", StringComparison.Ordinal) && line.Contains("/releases/download/", StringComparison.Ordinal))
            {
                result.Add(line);
                continue;
            }
            var separator = line.IndexOf(':');
            if (separator < 0) continue;
            var key = line[..separator];
            var value = line[(separator + 1)..].Trim().Trim('"', '\'');
            if (value.Length == 0) continue;
            if (key == "targetRevision" && value == "{{ .Values.gitRevision | quote }}") continue;
            var imageParameter = Regex.IsMatch(parameter, @"(?i)(image.*(version|tag|digest|sha)|imageVersion)$", RegexOptions.None, RegexTimeout);
            if (key is "targetRevision" or "image" or "imageName" or "tag" or "sha" or "digest" or "version" or "versionOverride" or "newTag" or "newName"
                || key == "value" && (value.Contains("sha256:", StringComparison.Ordinal) || imageParameter))
                result.Add($"{key}: {value}");
            if (key == "value") parameter = "";
        }
        return result.ToArray();
    }

    [Theory]
    [DataInlineUnit("image: registry/app:latest", "image: registry/app:latest")]
    [DataInlineUnit("  tag: \"1.2.3\" # explanation", "tag: 1.2.3")]
    [DataInlineUnit("- name: proxy.image.version\n  value: unpinned", "value: unpinned")]
    [DataInlineUnit("targetRevision: 1.2.3", "targetRevision: 1.2.3")]
    public void DiscoveryIncludesNewAndUnpinnedReferences(string source, string expected) =>
        Assert.Equal(new[] { expected }, ExtractPins(source));

    [Fact]
    public void DiscoveryIgnoresCommentsAndRootRevisionButPreservesDuplicates()
    {
        Assert.Equal(new[] { "image: app:1", "image: app:1" }, ExtractPins(
            "# image: ignored\ntargetRevision: {{ .Values.gitRevision | quote }}\nimage: app:1\nimage: app:1"));
    }

    private static string Resolve(string relative)
    {
        Assert.False(Path.IsPathRooted(relative));
        var path = Path.GetFullPath(Path.Combine(Root, relative));
        Assert.True(path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal), $"Path escapes repository: {relative}");
        return path;
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "dependencies.json")) &&
                File.Exists(Path.Combine(directory.FullName, "deployment/Chart.yaml"))) return directory.FullName;
        throw new DirectoryNotFoundException("Cannot find dependencies.json above the test output directory.");
    }

    [Fact]
    public void GeneratorMappingsAreUnambiguousAndCurrent() => Assert.Empty(PinUpdater.Plan(Root));
}
