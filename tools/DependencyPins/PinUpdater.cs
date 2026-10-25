using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DependencyPins;

public sealed record Inventory(int SchemaVersion, Component[] Components);
public sealed record Component(string Name, string Provenance, Dictionary<string, string> Pins, Manifest[] Manifests)
{
    public Reference[] References { get; init; } = [];
}
public sealed record Manifest(string Path, string[] Lines)
{
    public string?[]? Selectors { get; init; }
}
public sealed record Reference(string Path, string Pattern, string Pin);
public sealed record FileChange(string Path, string Before, string After, bool HasBom);

public static class PinUpdater
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    public static IReadOnlyList<FileChange> Plan(string repositoryRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var inventory = JsonSerializer.Deserialize<Inventory>(File.ReadAllText(Resolve(root, "dependencies.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("Empty dependency inventory.");
        Require(inventory.SchemaVersion == 2, "Expected dependency inventory schemaVersion 2.");
        Require(inventory.Components is { Length: > 0 }, "Inventory has no components.");
        var files = new Dictionary<string, (string Text, List<Edit> Edits)>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var owners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in inventory.Components)
        {
            Require(!string.IsNullOrWhiteSpace(component.Name) && names.Add(component.Name), "Missing or duplicate component name.");
            Require(component.Pins is { Count: > 0 } && component.Manifests is not null, $"Incomplete component: {component.Name}");
            Require(!string.IsNullOrWhiteSpace(component.Provenance), $"Missing provenance for {component.Name}");
            Require(File.Exists(Resolve(root, component.Provenance.Split('#')[0])), $"Missing provenance file for {component.Name}");
            foreach (var value in component.Pins.Values)
                Require(!string.IsNullOrEmpty(value) && Regex.IsMatch(value, @"^[A-Za-z0-9][A-Za-z0-9./:@+_-]*$", RegexOptions.None, Timeout),
                    $"Invalid scalar pin in {component.Name}. Only version, image and digest tokens are supported.");
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var manifest in component.Manifests)
            {
                var path = Resolve(root, manifest.Path);
                Require(owners.Add(path), $"Duplicate manifest ownership: {manifest.Path}");
                Require(manifest.Lines is { Length: > 0 }, $"No pins mapped in {manifest.Path}");
                Require(manifest.Selectors is null || manifest.Selectors.Length == manifest.Lines.Length,
                    $"Selector count does not match lines in {manifest.Path}");
                for (var index = 0; index < manifest.Lines.Length; index++)
                {
                    var line = Regex.Replace(manifest.Lines[index], @"\{([A-Za-z0-9]+)\}", match => Pin(match.Groups[1].Value), RegexOptions.None, Timeout);
                    Require(!line.Contains('{') && !line.Contains('}'), $"Unresolved placeholder in {manifest.Path}");
                    string replacement;
                    string defaultPattern;
                    if (line.StartsWith("- https://", StringComparison.Ordinal))
                    {
                        replacement = line[2..];
                        defaultPattern = @"^[ \t]*- (https://[^\s]+)[ \t]*$";
                    }
                    else
                    {
                        var separator = line.IndexOf(": ", StringComparison.Ordinal);
                        Require(separator > 0, $"Invalid pin line: {line}");
                        replacement = line[(separator + 2)..];
                        defaultPattern = @"^[ \t]*(?:- )?" + Regex.Escape(line[..separator]) + @":[ \t]*([^\s]+)";
                    }
                    Add(path, manifest.Selectors?[index] ?? defaultPattern, replacement);
                }
            }
            foreach (var reference in component.References)
                Add(Resolve(root, reference.Path), reference.Pattern, Pin(reference.Pin));
            Require(component.Pins.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(used), $"Unused pins in {component.Name}.");

            string Pin(string key)
            {
                Require(component.Pins.ContainsKey(key), $"Unknown pin: {component.Name}.{key}");
                used.Add(key);
                return component.Pins[key];
            }
        }

        var changes = new List<FileChange>();
        foreach (var (path, file) in files)
        {
            var edits = file.Edits.OrderBy(edit => edit.Start).ToArray();
            for (var i = 1; i < edits.Length; i++)
                Require(edits[i].Start >= edits[i - 1].Start + edits[i - 1].Length, $"Overlapping pin targets in {path}");
            var updated = new StringBuilder(file.Text);
            foreach (var edit in edits.Reverse())
                updated.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Value);
            var after = updated.ToString();
            if (after != file.Text)
                changes.Add(new FileChange(path, file.Text, after, File.ReadAllBytes(path).AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })));
        }
        return changes;

        void Add(string path, string pattern, string replacement)
        {
            if (!files.TryGetValue(path, out var file))
                files.Add(path, file = (File.ReadAllText(path), []));
            var regex = new Regex(pattern, RegexOptions.Multiline, Timeout);
            Require(regex.GetGroupNumbers().Length == 2, $"Selector must have exactly one capture group: {path}: {pattern}");
            var matches = regex.Matches(file.Text);
            Require(matches.Count == 1 && matches[0].Groups[1].Success && matches[0].Groups[1].Length > 0,
                $"Expected one target in {path}, found {matches.Count}: {pattern}");
            var group = matches[0].Groups[1];
            var start = group.Index;
            var length = group.Length;
            if (length >= 2 && (group.Value[0] is '\'' or '"') && group.Value[^1] == group.Value[0])
            {
                start++;
                length -= 2;
            }
            file.Edits.Add(new Edit(start, length, replacement));
        }
    }

    public static void Apply(IReadOnlyList<FileChange> changes)
    {
        // Validate every target before the first write. Never overwrite an intervening edit.
        foreach (var change in changes)
            Require(File.ReadAllText(change.Path) == change.Before, $"File changed after planning: {change.Path}");
        foreach (var change in changes)
            File.WriteAllText(change.Path, change.After, new UTF8Encoding(change.HasBom));
    }

    private static string Resolve(string root, string relative)
    {
        Require(!string.IsNullOrWhiteSpace(relative) && !Path.IsPathRooted(relative), $"Expected repository-relative path: {relative}");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        Require(path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal), $"Path escapes repository: {relative}");
        for (var current = path; current.Length >= root.Length; current = Path.GetDirectoryName(current)!)
        {
            Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, $"Symlink targets are not supported: {current}");
            if (current == root) break;
        }
        return path;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private sealed record Edit(int Start, int Length, string Value);
}
