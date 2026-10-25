using System.Text.Json;
using DependencyPins;
using Xunit;

namespace ChartValidation.Tests;

public class PinUpdaterTests
{
    [Fact]
    public void UpdatePreservesFormattingAndIsIdempotent()
    {
        using var fixture = new Fixture();
        fixture.Write("manifest.yaml", "# keep\r\nimage: 'registry/app:old' # keep too\r\nreplicas: 1\r\n");
        fixture.Inventory(new Manifest("manifest.yaml", ["image: {image}"]));
        var plan = PinUpdater.Plan(fixture.Root);
        Assert.Single(plan);
        Assert.Contains("registry/app:old", fixture.Read("manifest.yaml"));
        PinUpdater.Apply(plan);
        Assert.Equal("# keep\r\nimage: 'registry/app:new' # keep too\r\nreplicas: 1\r\n", fixture.Read("manifest.yaml"));
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
    [DRN.Framework.Testing.DataAttributes.DataInlineUnit("image: first\nimage: second\n")]
    [DRN.Framework.Testing.DataAttributes.DataInlineUnit("replicas: 1\n")]
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
