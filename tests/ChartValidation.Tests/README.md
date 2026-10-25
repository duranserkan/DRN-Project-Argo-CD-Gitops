# Deployment chart contracts

The root, logging, monitoring and workloads charts render independently. Each needs its own validation. `deployment/templates/validate.yaml` is the canonical copy. Update the three branch copies in the same change. The equality test reports the differing file and expected/actual content when they diverge.

`ChartValidation.Tests.csproj` is a non-packable .NET 10 executable test project using DRN.Framework.Testing, xUnit v3 and Microsoft.Testing.Platform, matching the bootstrap tests. `ChartTests.cs` invokes the real Helm CLI with temporary JSON values files. It does not contact Kubernetes, fetch upstream charts or execute workload containers. The supplied Git SHA is synthetic because these tests check rendering, not repository resolution.

The project is separate from `Bootstrap.slnx` so bootstrap validation does not require Helm. CI restores and audits dependencies, builds Release, then runs tests with the GitHub test summary reporter. Package pins reuse the versions and provenance recorded in the [bootstrap test documentation](../ApplicationSecretBootstrap.Tests/README.md). No Python runtime is required.

The suite covers:

- Unconfigured defaults fail closed. A usable default-profile deployment still requires environment, revision and release inputs.
- Valid staging, opted-in production and all three retained stages render in all four charts.
- Invalid environments, SHA formats, release-tag boundaries, production opt-ins, retained profiles, storage names and stage combinations fail with the expected diagnostic.
- Root rendering preserves the supplied revision and retained storage class, omits monitoring in `prepare`, and keeps logging/monitoring at `data` during workload activation.
- Workload rendering omits Sample/Nexus in `prepare`, sets replica patches to zero in `data` and one in `workloads`, selects retained storage and enables read-only credential validation.

Manifest assertions inspect anchored Application documents and specific chart-owned fields. They are not a general YAML parser and do not validate Kubernetes schemas or render the upstream charts referenced by Applications.

## Rule audit

The audit on 2026-10-25 found the existing rules consistent with the repository profile, root Application inputs and retained-storage contract. No validation behavior was changed.

| Rule | Owning requirement and boundary |
|---|---|
| Staging/production only | Repository deployment environments; local development is outside GitOps. |
| Forty lowercase hexadecimal SHA characters | Immutable revision input from the root. Does not prove the commit exists or matches the release tag. |
| Calendar release and preview001 through preview999 | README release convention. Does not verify branch ancestry or publication. |
| Boolean production opt-in and final tag | Production root starts disabled. Opt-in does not prove operational readiness. |
| Staging-only retained profile | Platform operations recovery contract. |
| Retained class name and activation stages | Valid configuration combinations. Does not prove the class exists, is non-provisioning, or has reserved volumes and restored identities. |

## CI and local execution

The suite also includes `DependencyInventoryTests.cs`, described below.

The `charts` job runs on PRs, develop pushes, master pushes and master's weekly schedule. All three gatekeepers require it. Release tags retain Trivy-only behavior. The [composite action](../../.github/actions/chart-validate/action.yml) installs Helm 4.2.1, matching [Argo CD v3.5.6's tool manifest](https://github.com/argoproj/argo-cd/blob/v3.5.6/hack/tool-versions.sh).

The Linux amd64 archive checksum was checked against [Helm's official checksum](https://get.helm.sh/helm-v4.2.1-linux-amd64.tar.gz.sha256sum) on 2026-10-25: `479dca836e5b45e8bd222400c5591b0e3a647378f03ff96597180db97c17fdae`. CI verifies the downloaded archive before extraction. Recheck the tool version and checksum when upgrading Argo CD.

When local test and rendering execution is authorized, with Helm 4.2.1 on PATH, run from the repository root:

```sh
dotnet run --project tests/ChartValidation.Tests/ChartValidation.Tests.csproj --configuration Release
```

Implementation and static inspection do not establish a passing test run. CI execution and live recovery acceptance remain separate evidence.

## Dependency inventory

`DependencyInventoryTests.cs` checks [dependencies.json](../../dependencies.json) in the same required CI job. It uses only .NET libraries and does not contact registries. Components group compatible pins and list their manifest consumers. `{pinName}` placeholders refer to the owning component's `pins`, allowing one approved SDK image or release version to cover several consumers. Schema version 2 adds contextual selectors for the [.NET updater](../../tools/DependencyPins/README.md). CI runs its read-only check mode before the suite. The tests reference the updater project and verify that the real repository mappings are unambiguous and current.

`PinUpdaterTests.cs` exercises temporary fixtures for formatting preservation, idempotence, named targets, missing/ambiguous matches, overlapping captures, path escapes, external references and refusal to overwrite intervening edits. These tests never update repository files.

Discovery scans YAML under `deployment`, `infrastructure` and `services` for explicit scalar image/version/digest fields, literal `targetRevision` fields, image-version Helm parameters and GitHub release download resources. Local `Chart.yaml` versions and the exact root-propagated Git revision expression are excluded. The test compares the complete discovered file set and each file's ordered pin lines, preserving duplicate uses such as init and main container images. It ignores indentation, scalar quoting and comments. Failures identify the file and expected/actual pins. Focused tests cover discovery of new unpinned references and duplicate preservation.

Argo CD's installation chart version and CI's Helm version/checksum are checked through explicit references outside the manifest scan. Each reference must match exactly once. Component names and manifest ownership must be unique, every pin must be used, and local provenance files must exist.

This is a scanner for the repository's current scalar forms, not a general YAML parser. New representations such as flow mappings, YAML aliases or inline JSON require extending discovery and its tests in the same change. It does not inspect chart-internal default images, verify registries or attest compatibility. NuGet packages, GitHub Action commits, dashboard revisions and release-selection tags remain managed by their existing manifests and provenance rules. Inventory entries cannot substitute for publisher verification on an upgrade.
