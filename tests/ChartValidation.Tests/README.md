# Deployment chart contracts

The root, logging, monitoring and workloads charts render independently. Each needs its own validation. `deployment/templates/validate.yaml` is the canonical copy. Update the three branch copies in the same change. The equality test reports the differing file and expected/actual content when they diverge.

`ChartValidation.Tests.csproj` is a non-packable .NET 10 executable test project using DRN.Framework.Testing, xUnit v3 and Microsoft.Testing.Platform, matching the bootstrap tests. `ChartTests.cs` invokes the real Helm CLI with temporary JSON values files. It does not contact Kubernetes, fetch upstream charts or execute workload containers. The supplied Git SHA is synthetic because these tests check rendering, not repository resolution.

The project belongs to the repository-wide [GitOps.slnx](../../GitOps.slnx). Building the solution does not run tests or require Helm. CI targets bootstrap and chart test projects separately, so only chart test execution requires Helm. Chart CI restores and audits dependencies, builds Release, then runs tests with the GitHub test summary reporter. Package pins reuse the versions and provenance recorded in the [bootstrap test documentation](../ApplicationSecretBootstrap.Tests/README.md). No Python runtime is required.

The suite covers:

- Unconfigured defaults fail closed. A usable default-profile deployment still requires environment, revision and release inputs.
- Valid staging, opted-in production and all three retained stages render in all four charts.
- Invalid environments, SHA formats, release-tag boundaries, production opt-ins, retained profiles, storage names and stage combinations fail with the expected diagnostic.
- Root rendering preserves repository, revision, environment, release tag, production opt-in and retained settings. The default profile shares the same propagation test. Retained rendering omits monitoring in `prepare` and keeps logging/monitoring at `data` during workload activation.
- Retained root rendering selects the saved trust issuer, disables Sealed Secrets key renewal, selects Traefik ClusterIP and creates early Graylog isolation. The default profile excludes these overrides.
- Workload rendering omits Sample/Nexus, PostgreSQL and credential validation in `prepare`. Later stages target the named Deployments, PVCs, database Cluster and bootstrap Job. Assertions check replica counts, storage class, PostgreSQL credentials and CA inputs, and read-only credential validation.
- Logging rendering omits MongoDB and Graylog in `prepare`. Later stages check both named MongoDB claims, the credential Job, Graylog storage and backup configuration, and exclusion of chart-owned isolation policies.

Manifest assertions inspect anchored Application documents and specific chart-owned fields. Patch assertions select exactly one target by kind and name before checking its content. They do not apply Kustomize patches, validate Kubernetes schemas or render the upstream charts referenced by Applications. Actual claim creation, patch application and live recovery remain separate acceptance checks.

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

Dependency inventory and updater tests have their own [DependencyPins.Tests](../DependencyPins.Tests/README.md) project. The required `charts` job builds and runs both suites independently. This project has no reference to the updater.

The `charts` job runs on PRs, develop pushes, master pushes and master's weekly schedule. All three gatekeepers require it. Release tags retain Trivy-only behavior. The [composite action](../../.github/actions/chart-validate/action.yml) installs Helm 4.2.1, matching [Argo CD v3.5.6's tool manifest](https://github.com/argoproj/argo-cd/blob/v3.5.6/hack/tool-versions.sh).

The Linux amd64 archive checksum was checked against [Helm's official checksum](https://get.helm.sh/helm-v4.2.1-linux-amd64.tar.gz.sha256sum) on 2026-10-25: `479dca836e5b45e8bd222400c5591b0e3a647378f03ff96597180db97c17fdae`. CI verifies the downloaded archive before extraction. Recheck the tool version and checksum when upgrading Argo CD.

When local test and rendering execution is authorized, with Helm 4.2.1 on PATH, run from the repository root:

```sh
dotnet run --project tests/ChartValidation.Tests/ChartValidation.Tests.csproj --configuration Release
```

Implementation and static inspection do not establish a passing test run. CI execution and live recovery acceptance remain separate evidence.

## Dependency inventory

See [dependency inventory coverage](../DependencyPins.Tests/README.md#dependency-inventory) for the separate suite's discovery rules, updater cases and verification limits.
