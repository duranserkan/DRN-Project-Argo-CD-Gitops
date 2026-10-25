# Dependency pin tests

This non-packable .NET 10 executable test project owns dependency inventory and updater tests. It references [DependencyPins](../../tools/DependencyPins/DependencyPins.csproj) and uses the same DRN.Framework.Testing, xUnit v3 and Microsoft.Testing.Platform versions as the other test projects. Package provenance is recorded in the [bootstrap test documentation](../ApplicationSecretBootstrap.Tests/README.md).

The project appears under `Test` in [GitOps.slnx](../../GitOps.slnx). It is separate from bootstrap and Helm chart tests and does not require Helm, Kubernetes or registry access during test execution. Initial package restore requires the configured NuGet source.

## CI and local execution

The required `charts` CI job restores, audits, builds and runs this project separately from ChartValidation.Tests. Its project reference builds the updater before CI invokes read-only `check` mode. CodeQL builds the full solution, including these tests and the updater.

When local execution is authorized, run from the repository root:

```sh
dotnet run --project tests/DependencyPins.Tests/DependencyPins.Tests.csproj --configuration Release
```

## Dependency inventory

`DependencyInventoryTests.cs` checks [dependencies.json](../../dependencies.json) without contacting registries. Components group compatible pins and list their manifest consumers. `{pinName}` placeholders refer to the owning component's `pins`. Schema version 2 adds contextual selectors for the [.NET updater](../../tools/DependencyPins/README.md). Tests verify that the real repository mappings are unambiguous and current.

Discovery scans YAML under `deployment`, `infrastructure` and `services` for explicit scalar image/version/digest fields, literal `targetRevision` fields, image-version Helm parameters and GitHub release download resources. Local `Chart.yaml` versions and the exact root-propagated Git revision expression are excluded. It compares the complete discovered file set and each file's ordered pin lines, preserving duplicate uses. It ignores indentation, scalar quoting and comments, and normalizes scalar list items such as `- image:` and `- newTag:`. Failures identify the file and expected/actual pins. Focused cases cover release URLs, image parameters, unrelated values and duplicate preservation.

Argo CD's installation chart version and CI's Helm version/checksum are checked through explicit references outside the manifest scan. Each reference must match exactly once. Component names and manifest ownership must be unique, every pin must be used, and local provenance files must exist.

This is a scanner for the repository's supported scalar forms, not a general YAML parser. Flow mappings, YAML aliases or inline JSON require extending discovery and its tests. It does not inspect chart-internal default images, verify registries or attest compatibility. NuGet packages, GitHub Action commits, dashboard revisions and release-selection tags retain their existing owners. Inventory agreement cannot substitute for publisher verification.

## Updater behavior

`PinUpdaterTests.cs` uses temporary fixtures for LF/CRLF preservation in scalar and release-URL updates, scalar quoting, UTF-8 BOM preservation, idempotence, named targets, missing/ambiguous matches, invalid captures, duplicate ownership, unknown/unused pins, selector counts, overlapping captures, path escapes, symbolic links, external references and refusal to overwrite intervening edits. Tests never update repository files. The symbolic-link case requires link creation support, available on the Linux CI runners.

Static inspection does not establish a passing test run. These tests do not validate upstream chart rendering or live deployment behavior.
