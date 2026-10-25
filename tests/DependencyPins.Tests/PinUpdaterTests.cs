using System.Text.Json;
using System.Text;
using DependencyPins;
using DRN.Framework.Testing.DataAttributes;
using Xunit;

namespace DependencyPins.Tests;

public class PinUpdaterTests
{
    [Theory]
    [DataInlineUnit("\n", "", false)]
    [DataInlineUnit("\r\n", "'", false)]
    [DataInlineUnit("\n", "\"", true)]
    public void UpdatePreservesFormattingAndIsIdempotent(string newline, string quote, bool bom)
    {
        using var fixture = new Fixture();
        var before = $"# keep{newline}image: {quote}registry/app:old{quote} # keep too{newline}replicas: 1{newline}";
        File.WriteAllText(Path.Combine(fixture.Root, "manifest.yaml"), before, new UTF8Encoding(bom));
        fixture.Inventory(new Manifest("manifest.yaml", ["image: {image}"]));
        var plan = PinUpdater.Plan(fixture.Root);
        Assert.Single(plan);
        Assert.Contains("registry/app:old", fixture.Read("manifest.yaml"));
        PinUpdater.Apply(plan);
        Assert.Equal(before.Replace("registry/app:old", "registry/app:new"), fixture.Read("manifest.yaml"));
        Assert.Equal(bom, File.ReadAllBytes(Path.Combine(fixture.Root, "manifest.yaml")).AsSpan()
            .StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        Assert.Empty(PinUpdater.Plan(fixture.Root));
    }

    [Theory]
    [DataInlineUnit("\n")]
    [DataInlineUnit("\r\n")]
    public void ReleaseUrlUpdatesPreserveLineEndings(string newline)
    {
        using var fixture = new Fixture();
        var before = $"resources:{newline}  - https://example.invalid/releases/download/v1/resources.yaml{newline}";
        fixture.Write("manifest.yaml", before);
        fixture.Write("dependencies.json", JsonSerializer.Serialize(new Inventory(2,
            [new Component("fixture", "provenance.md",
                new Dictionary<string, string> { ["version"] = "v2" },
                [new Manifest("manifest.yaml", ["- https://example.invalid/releases/download/{version}/resources.yaml"])])])));

        var plan = PinUpdater.Plan(fixture.Root);

        Assert.Single(plan);
        Assert.Equal(before, fixture.Read("manifest.yaml"));
        PinUpdater.Apply(plan);
        Assert.Equal(before.Replace("/v1/", "/v2/"), fixture.Read("manifest.yaml"));
        Assert.Empty(PinUpdater.Plan(fixture.Root));
    }

    [Fact]
    public void NamedSelectorsUpdateRepeatedKeysWithoutChangingOtherImages()
    {
        using var fixture = new Fixture();
        fixture.Write("manifest.yaml", "- name: bootstrap\n  image: old\n- name: other\n  image: unrelated\n");
        fixture.Inventory(new Manifest("manifest.yaml", ["image: {image}"])
        {
            Selectors = [@"- name: bootstrap\n  image: ([^\s]+)"]
        });
        PinUpdater.Apply(PinUpdater.Plan(fixture.Root));
        Assert.Equal("- name: bootstrap\n  image: registry/app:new\n- name: other\n  image: unrelated\n", fixture.Read("manifest.yaml"));
    }

    [Theory]
    [DataInlineUnit("image: first\nimage: second\n")]
    [DataInlineUnit("replicas: 1\n")]
    public void MissingOrAmbiguousTargetsFailBeforeAnyWrites(string second)
    {
        using var fixture = new Fixture();
        fixture.Write("first.yaml", "image: original\n");
        fixture.Write("second.yaml", second);
        fixture.Inventory(new Manifest("first.yaml", ["image: {image}"]), new Manifest("second.yaml", ["image: {image}"]));
        Assert.Throws<InvalidDataException>(() => PinUpdater.Plan(fixture.Root));
        Assert.Equal("image: original\n", fixture.Read("first.yaml"));
        Assert.Equal(second, fixture.Read("second.yaml"));
    }

    [Fact]
    public void InterveningEditPreventsAllWrites()
    {
        using var fixture = new Fixture();
        fixture.Write("first.yaml", "image: original\n");
        fixture.Write("second.yaml", "image: original\n");
        fixture.Inventory(new Manifest("first.yaml", ["image: {image}"]), new Manifest("second.yaml", ["image: {image}"]));
        var plan = PinUpdater.Plan(fixture.Root);
        fixture.Write("second.yaml", "image: user-edit\n");
        Assert.Throws<InvalidDataException>(() => PinUpdater.Apply(plan));
        Assert.Equal("image: original\n", fixture.Read("first.yaml"));
        Assert.Equal("image: user-edit\n", fixture.Read("second.yaml"));
    }

    [Fact]
    public void OverlappingTargetsAreRejected()
    {
        using var fixture = new Fixture();
        fixture.Write("manifest.yaml", "image: original\n");
        fixture.Inventory(new Manifest("manifest.yaml", ["image: {image}", "image: {image}"]));
        Assert.Throws<InvalidDataException>(() => PinUpdater.Plan(fixture.Root));
    }

    [Fact]
    public void PathsOutsideTheRepositoryAreRejected()
    {
        using var fixture = new Fixture();
        fixture.Inventory(new Manifest("../outside.yaml", ["image: {image}"]));
        Assert.Throws<InvalidDataException>(() => PinUpdater.Plan(fixture.Root));
    }

    [Fact]
    public void ReferenceCaptureUpdatesOnlyTheVersion()
    {
        using var fixture = new Fixture();
        fixture.Write("instructions.md", "install --image registry/app:old --wait\n");
        fixture.InventoryWithReferences(new Reference("instructions.md", @"--image ([^\s]+)", "image"));
        PinUpdater.Apply(PinUpdater.Plan(fixture.Root));
        Assert.Equal("install --image registry/app:new --wait\n", fixture.Read("instructions.md"));
    }

    [Theory]
    [DataInlineUnit("image: original", "exactly one capture")]
    [DataInlineUnit("(image): (original)", "exactly one capture")]
    [DataInlineUnit("image: (missing)", "Expected one target")]
    [DataInlineUnit("image: ()", "Expected one target")]
    public void InvalidSelectorsRejectTheWholePlan(string selector, string diagnostic)
    {
        using var fixture = new Fixture();
        fixture.Write("first.yaml", "image: original\n");
        fixture.Write("second.yaml", "image: original\n");
        fixture.Inventory(new Manifest("first.yaml", ["image: {image}"]),
            new Manifest("second.yaml", ["image: {image}"]) { Selectors = [selector] });

        var error = Assert.Throws<InvalidDataException>(() => PinUpdater.Plan(fixture.Root));

        Assert.Contains(diagnostic, error.Message);
        Assert.Equal("image: original\n", fixture.Read("first.yaml"));
        Assert.Equal("image: original\n", fixture.Read("second.yaml"));
    }

    [Theory]
    [DataInlineUnit("duplicate-owner", "Duplicate manifest ownership")]
    [DataInlineUnit("unknown-pin", "Unknown pin")]
    [DataInlineUnit("unused-pin", "Unused pins")]
    [DataInlineUnit("selector-count", "Selector count")]
    public void InvalidMappingsFailWithAnActionableDiagnostic(string failure, string diagnostic)
    {
        using var fixture = new Fixture();
        fixture.Write("manifest.yaml", "image: original\n");
        var manifest = new Manifest("manifest.yaml", ["image: {image}"]);
        switch (failure)
        {
            case "duplicate-owner": fixture.Inventory(manifest, manifest); break;
            case "unknown-pin": fixture.Inventory(manifest with { Lines = ["image: {missing}"] }); break;
            case "unused-pin": fixture.Inventory(manifest with { Lines = ["image: literal"] }); break;
            case "selector-count": fixture.Inventory(manifest with { Selectors = [] }); break;
            default: throw new ArgumentOutOfRangeException(nameof(failure));
        }

        var error = Assert.Throws<InvalidDataException>(() => PinUpdater.Plan(fixture.Root));

        Assert.Contains(diagnostic, error.Message);
        Assert.Equal("image: original\n", fixture.Read("manifest.yaml"));
    }

    [Fact]
    public void SymbolicLinkTargetsAreRejectedWithoutChangingTheDestination()
    {
        using var fixture = new Fixture();
        fixture.Write("destination.yaml", "image: original\n");
        File.CreateSymbolicLink(Path.Combine(fixture.Root, "manifest.yaml"),
            Path.Combine(fixture.Root, "destination.yaml"));
        fixture.Inventory(new Manifest("manifest.yaml", ["image: {image}"]));

        var error = Assert.Throws<InvalidDataException>(() => PinUpdater.Plan(fixture.Root));

        Assert.Contains("Symlink targets are not supported", error.Message);
        Assert.Equal("image: original\n", fixture.Read("destination.yaml"));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("dependency-pins-tests-");
        public string Root => _directory.FullName;
        public Fixture() => Write("provenance.md", "Synthetic test provenance.");
        public void Write(string name, string content) => File.WriteAllText(Path.Combine(Root, name), content);
        public string Read(string name) => File.ReadAllText(Path.Combine(Root, name));
        public void Inventory(params Manifest[] manifests) => Save(manifests, []);
        public void InventoryWithReferences(params Reference[] references) => Save([], references);
        private void Save(Manifest[] manifests, Reference[] references) => Write("dependencies.json",
            JsonSerializer.Serialize(new Inventory(2, [new Component("fixture", "provenance.md",
                new Dictionary<string, string> { ["image"] = "registry/app:new" }, manifests) { References = references }])));
        public void Dispose() => _directory.Delete(recursive: true);
    }
}
