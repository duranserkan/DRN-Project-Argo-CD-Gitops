# Credential bootstrap tests

Open [Bootstrap.slnx](../Bootstrap.slnx) in your IDE to manage the bootstrap source library and test project together. The source library links the deployed bootstrap files, so edits apply directly to the code used by the Jobs.

This project tests the real [application bootstrap](../../infrastructure/application-secrets/staging/bootstrap.cs) and [Graylog bootstrap](../../infrastructure/graylog/mongodb/bootstrap.cs). [Bootstrap.Sources](../Bootstrap.Sources/Bootstrap.Sources.csproj) compiles those files as a non-test library and exposes its internals only to this test assembly. This separates bootstrap code from tests for Sonar and coverage. Tests use DRN.Framework.Testing, xUnit v3 and AwesomeAssertions. Row tests use `DataInlineUnit` and `DataMemberUnit`. Fake HTTP handlers model Kubernetes responses. Tests never contact a cluster or read service account credentials.

Each bootstrap declares its own typed Secret and metadata models in its deployed file. Tests use those same models for valid fixtures, captured requests and fake API storage. Secret data remains a dictionary because key names vary. Malformed-response tests retain raw JSON to exercise incompatible field types and sanitized errors. Each bootstrap clones `JsonConventions.DefaultOptions` with `WhenWritingNull`; tests reuse those options. Camel-case property naming preserves Kubernetes field names without model attributes. Both Jobs retain single-file deployment and reference DRN.Framework.Utils 0.10.1-preview005, which supplies SharedKernel transitively.

Outgoing Secret JSON is also checked against a separate typed `SecretWireContract`. Its required properties use explicit Kubernetes JSON names and independent, case-sensitive serializer options. Unmapped fields are rejected at both the Secret and metadata levels, including `data`, `ownerReferences` and `annotations`, even when null. Initialization marker JSON is checked independently with `JsonDocument` for exact Kubernetes field names, immutable version-only data and metadata without owners or tracking annotations. Production models do not need serialization attributes.

`FixtureJson<T>` centralizes fixture serialization, cloning and snapshots with each bootstrap's serializer options. `BootstrapFixtures` shares fake API responses and Base64 fixture encoding through DRN Utils. Each fake API keeps its own lifecycle behavior. These helpers do not participate in independent wire-contract validation. Graylog uses DRN Utils for unpadded URL-safe password encoding. Both bootstraps retain explicit canonical Base64 and strict UTF-8 validation for stored Secret fields.

Coverage includes automatic fresh staging initialization after an atomic ConfigMap claim, preserving existing custom settings and legacy seeds, partial recovery, and adding an application without recreating existing Secrets. Marker tests cover enrollment of existing installs, total Secret loss, concurrent initializers, create conflicts, interrupted initialization, ambiguous create responses, invalid markers and API errors. Only the confirmed marker creator can generate automatically. A retained marker with no surviving settings requires explicit one-run recovery. Tests verify that the override does not bypass marker validation and does not persist into subsequent runs.

Both suites cover `CREDENTIALS_EXTERNALLY_MANAGED=true`: read-only validation, missing or invalid Secrets and markers, denied reads and refusal of credential generation. Graylog waits for the operator's connection Secret and rejects a URI that differs from the saved identity. Invalid mode values fail instead of enabling generation.

Tests also cover duplicate application names, invalid survivors, shared-key mismatches, competing Secret creators, interrupted retries, safe API errors and token refresh for Secret and marker requests. The fake API rejects Deployment reads.

Graylog tests also cover password formats, the administrator hash, operator URI preservation and interrupted bootstrap recovery guards. A fake monotonic clock exercises the 15-minute timeout without sleeping. An empty operator URI keeps polling. Create conflicts reuse the winning Secret. Existing custom credentials remain authoritative.

Graylog marker tests cover automatic fresh initialization, enrollment without rotation, total Secret loss, restoration, competing claims, interrupted and ambiguous creation, malformed markers and API failures. Every Secret write requires a prior marker in the fake API. Graylog has no generation override.

URI polling tests cover recovery from HTTP 5xx and request timeouts, exhaustion of the original deadline and fatal authorization failures. The CI coverage check parses XML and requires one module with both the exact `Bootstrap.Sources.dll` name and a positive `lines_covered` value, independent of attribute order.

The source library's `BOOTSTRAP_TESTS` compilation symbol excludes only the production entry points. Its `FileBasedProgram` feature accepts the source files' package directives, while an explicit Utils package reference supplies the dependency for project compilation. The test project also references Utils explicitly. Tests call the same lifecycle and HTTP request code as the Jobs. CI builds each standalone entry point before starting Sonar analysis. The deployed Jobs restore packages and compile the original files without a project or custom image.

The application and Graylog types live in `DRN.GitOps.Bootstraps.Application` and `DRN.GitOps.Bootstraps.Graylog`. Each file has an explicit `Main` method guarded by `!BOOTSTRAP_TESTS`, preserving standalone execution while allowing both files in the source library.

Both source files declare `#:property PublishAot=false` because their JSON serialization uses reflection. This aligns direct IDE and file-based builds with the existing Job and CI settings. Native AOT and trimming are not supported by these bootstraps.

## Verification

Unit tests exercise lifecycle and HTTP behavior with a fake Kubernetes API. Test and coverage results come from actual runs, not this inventory. Live RBAC, TLS, marker retention, SDK startup and retained-data recovery require separate environment checks. Pending acceptance is tracked in [TODO](../../todo.md#retained-data-recovery).

When local execution is authorized, use:

```sh
dotnet build tests/Bootstrap.slnx --configuration Release
dotnet run --project tests/ApplicationSecretBootstrap.Tests/ApplicationSecretBootstrap.Tests.csproj --configuration Release
```

## CI configuration

The [develop](../../.github/workflows/develop.yml), [master](../../.github/workflows/master.yml) and [pull-request](../../.github/workflows/pull-request.yml) workflows own their triggers, permissions and gatekeepers. They reuse composite actions for [bootstrap validation](../../.github/actions/dotnet-validate/action.yml), [CodeQL](../../.github/actions/codeql-scan/action.yml) and [Trivy](../../.github/actions/scan-file-system-vulnerabilities/action.yml). Dependency review stays inline because it is a single PR-only step.

| Event | Required jobs |
|---|---|
| Develop push | Chart contracts, bootstrap builds, dependency audit, unit coverage and Trivy |
| Master push or weekly schedule | The same validation, with SonarCloud and C#/Actions CodeQL |
| Pull request to develop or master | Chart contracts, bootstrap validation, Trivy, C#/Actions CodeQL and dependency review |
| Preview or final release tag | Trivy only |

Bootstrap validation builds both standalone sources and `tests/Bootstrap.slnx`. Master explicitly enables Sonar analysis and supplies `SONAR_TOKEN`; a missing token fails validation. Develop and PRs leave Sonar disabled and pass no repository secrets. PR jobs use the checked-out PR merge revision for both source and local actions, on GitHub-hosted runners with checkout credential persistence disabled. Each gatekeeper requires every listed job to succeed, including every CodeQL matrix entry where applicable.

CodeQL analyzes GitHub Actions and C# in separate matrix jobs. Its manual C# build compiles both standalone bootstrap files after scanner initialization, then builds the test solution. This includes production Main methods that Bootstrap.Sources excludes through BOOTSTRAP_TESTS. Code-scanning rules must enforce alert-based merge protection because a successful analysis does not itself reject alerts.

Coverage uses `Microsoft.Testing.Extensions.CodeCoverage` with `Microsoft.Testing.Extensions.GitHubActionsReport` pinned directly to `2.5.1`. [Coverage settings](../bootstrap-coverage.config) include only `Bootstrap.Sources.dll`, excluding test methods, fake handlers and dependencies. Unexercised TLS and service-account setup remains visible as uncovered code. The standalone entry points excluded at compilation are outside this coverage denominator.

After restoring and building Release, run coverage from the repository root:

```sh
dotnet run --project tests/ApplicationSecretBootstrap.Tests/ApplicationSecretBootstrap.Tests.csproj \
  --no-build --configuration Release -- \
  --report-gh --report-gh-step-summary-sections test-results coverage \
  --coverage --coverage-settings "$PWD/tests/bootstrap-coverage.config" \
  --coverage-output-format xml --coverage-output coverage.xml \
  --results-directory "$PWD/TestResults"
```

`--report-gh` writes its summary only in GitHub Actions. Local runs print coverage and write `TestResults/coverage.xml`. CI rejects reports without covered bootstrap lines, then uploads the report as `bootstrap-coverage`, retained for 14 days, including after test or Sonar failures when a report exists. After a successful build, branch Sonar analysis still finishes when tests fail so diagnostics remain available. Test failures continue to fail CI. Sonar imports this Microsoft XML through `sonar.cs.vscoveragexml.reportsPaths`. Its .NET scanner also scans supported configuration files under the repository root with `scanAll=true`. Generated output and agent files are excluded.

Use absolute coverage-settings and results paths because the test host can change its working directory. If a coverage run is interrupted, clean and rebuild before retrying. Static instrumentation changes output assemblies and can leave instrumentation artifacts after an interruption. An empty report is not evidence of zero coverage.

Configure `SONAR_TOKEN` and enable CI-based analysis for `master` in the existing SonarCloud project. Disable automatic analysis when using the scanner. The validation action sets project key `duranserkan_DRN-Project-Argo-CD-Gitops` and organization `duranserkan`. Develop no longer publishes Sonar analysis; its changes receive CodeQL on PRs and Sonar after promotion to master. Enable GitHub's dependency graph and dependency review. Each workflow now owns a direct `gatekeeper` job rather than the reusable `ci / gatekeeper` job. Confirm the emitted check names in actual runs, then update required checks or workflow rulesets for the intended branch and event. Remove the obsolete `ci / gatekeeper` requirement only as part of that migration. Repository settings are not changed by these files.

The test and scanner package source is `tests/NuGet.Config`. Each deployed bootstrap's mounted NuGet configuration also enables nuget.org. Runtime restore now needs network access and uses the existing bounded `/tmp` storage. Existing Job commands retain disabled runtime NuGet audit. CI fails on reported test-dependency vulnerabilities or audit retrieval errors, including Utils and its transitive dependencies. Unlike DRN-Project, this repository does not currently use locked restore. Transitive dependencies remain subject to NuGet resolution. Startup timing and resource sufficiency require staging verification.

Package provenance: DRN packages checked on 2026-10-25. SonarScanner retains its 2026-10-17 hash verification. Unchanged xUnit, reporter and coverage hashes retain their 2026-10-12 verification. All six package version indexes were rechecked on 2026-10-25.

| Package | Version | Canonical NuGet SHA-512 |
|---|---|---|
| DRN.Framework.Testing | 0.10.1-preview005 | `izz6dXDeen5yTKsWniw6BA6ckme12GUiPOlxytvayhf702gLcWm9IcgOyXAkkbbSZiPxiH8AW4zxrMBO8cDRGQ==` |
| DRN.Framework.Utils | 0.10.1-preview005 | `bDjW46wVY7ISZcFCsJGGQJx7VVMv3VaL70LYUPi3drSlU3M4wWFou1FxzmFnpqMx4cpBUpIVU2ppx37DgKqQsg==` |
| xunit.v3.mtp-v2 | 4.0.2 | `OesFiVVe38QnAO3W7WhSvq1l7rNFS/Kf9ioQXmQ4zkV6fRAsI+RhWz1apRw6s70K2BLk4lq6ql10FKNeAkOyUQ==` |
| Microsoft.Testing.Extensions.GitHubActionsReport | 2.5.1 | `9zM6qx1T0FWBConQwtgRJ3hsDQRH17g3nTW3rpg8ba+sBTkG2moeYwYbYxRfB44lSbyYzoEOxyYVYygwCztdew==` |
| Microsoft.Testing.Extensions.CodeCoverage | 18.12.0 | `bMR8B/80Nx8AyaOyuOTuSjaZtyuTCFeB3Q0Vej8eFBI3PQWOyk5lV8dEdm9TwQLNgxMQUxuF2uauwofS5LsHww==` |
| dotnet-sonarscanner | 12.0.0 | `3de/QlFpKg+CbPeWQmVOTnMh/7A0KIw44zrwIFVyPBtRevdNAi+CODye25mzdkxK1upJ0Qrzypua855bwPFwgg==` |

Downloaded [Testing](https://api.nuget.org/v3/catalog0/data/2026.10.25.14.59.30/drn.framework.testing.0.10.1-preview005.json) and [Utils](https://api.nuget.org/v3/catalog0/data/2026.10.25.15.00.10/drn.framework.utils.0.10.1-preview005.json) packages matched their canonical NuGet SHA-512 hashes. Both target .NET 10 and identify source commit `2a898d0b41747d156d4215c33f3699131ecac5bd`. Testing still depends on xUnit 4.0.2 and GitHubActionsReport 2.5.1. The unchanged [SonarScanner](https://api.nuget.org/v3/catalog0/data/2026.10.15.15.17.03/dotnet-sonarscanner.12.0.0.json) package retains its earlier canonical hash verification.

The earlier reporter and coverage collector downloads matched their canonical NuGet hashes. The xUnit hash was checked in NuGet catalog metadata. All three versions remain current in the 2026-10-25 registry check.

[SonarScanner 12.0](https://github.com/SonarSource/sonar-scanner-msbuild/releases/tag/12.0.0.12728) removes support for older SonarQube Server versions and TFS XAML builds. This repository uses SonarCloud with the standard .NET begin/build/end workflow. CI must verify the updated scanner and packages.

CI dependency provenance checked on 2026-10-25:

| Action | Release | Verified commit |
|---|---|---|
| actions/checkout | v7.0.1 | `3d3c42e5aac5ba805825da76410c181273ba90b1` |
| actions/setup-dotnet | v6.0.0 | `a98b56852c35b8e3190ac28c8c2271da59106c68` |
| actions/upload-artifact | v7.0.2 | `cf430e030ddbb5b0abf93d22962f4752f3646cd9` |
| aquasecurity/trivy-action | v0.36.0 | `ed142fd0673e97e23eac54620cfb913e5ce36c25` |
| github/codeql-action | v4.39.1 | `9b3254085494b55a0f7c4c2b3d2d52cc712ce370` |
| actions/dependency-review-action | v5.0.0 | `a1d282b36b6f3519aa1f3fc636f609c47dddb294` |

Upstream Git tag queries verified all six commits. CodeQL and Trivy action annotated tags were peeled to their commits. CodeQL's pinned defaults select CodeQL 2.27.3 through `tools: linked`. The setup-dotnet v6 release retains the existing inputs and Node 24 runtime. CI uses GitHub-hosted Ubuntu 24.04 runners.

Trivy is pinned to v0.76.1 in both PR and branch scans. Its [official release metadata](https://github.com/aquasecurity/trivy/releases/tag/v0.76.1) records Linux x64 archive SHA-256 `c7cbe3137fb8a48a17574bede3dd6beb16742d1e9772929940fc90082c39d9c0`. This patch updates Go and CA trust for the eMudhra incident. The unchanged Trivy action v0.36.0 accepts this explicit version. All six actions use the latest published releases at this review. Local builds and scans were not run.
