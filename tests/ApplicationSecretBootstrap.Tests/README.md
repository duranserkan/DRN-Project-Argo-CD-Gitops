# Credential bootstrap tests

Open [Bootstrap.slnx](../Bootstrap.slnx) in your IDE to manage the bootstrap source library and test project together. The source library links the deployed bootstrap files, so edits apply directly to the code used by the Jobs.

This project tests the real [application bootstrap](../../infrastructure/application-secrets/staging/bootstrap.cs) and [Graylog bootstrap](../../infrastructure/graylog/mongodb/bootstrap.cs). [Bootstrap.Sources](../Bootstrap.Sources/Bootstrap.Sources.csproj) compiles those files as a non-test library and exposes its internals only to this test assembly. This separates bootstrap code from tests for Sonar and coverage. Tests use DRN.Framework.Testing, xUnit v3 and AwesomeAssertions. Row tests use `DataInlineUnit` and `DataMemberUnit`. Fake HTTP handlers model Kubernetes responses. Tests never contact a cluster or read service account credentials.

Each bootstrap declares its own typed Secret and metadata models in its deployed file. Tests use those same models for valid fixtures, captured requests and fake API storage. Secret data remains a dictionary because key names vary. Malformed-response tests retain raw JSON to exercise incompatible field types and sanitized errors. Each bootstrap clones `JsonConventions.DefaultOptions` with `WhenWritingNull`; tests reuse those options. Camel-case property naming preserves Kubernetes field names without model attributes. Both Jobs retain single-file deployment with unchanged manifests and reference DRN.Framework.Utils 0.10.1-preview004, which supplies SharedKernel transitively.

Outgoing Secret JSON is also checked against a separate typed `SecretWireContract`. Its required properties use explicit Kubernetes JSON names and independent, case-sensitive serializer options. Unmapped fields are rejected at both the Secret and metadata levels, including `data`, `ownerReferences` and `annotations`, even when null. Initialization marker JSON is checked independently with `JsonDocument` for exact Kubernetes field names, immutable version-only data and metadata without owners or tracking annotations. Production models do not need serialization attributes.

`FixtureJson<T>` centralizes fixture serialization, cloning and snapshots with each bootstrap's serializer options. `BootstrapFixtures` shares fake API responses and Base64 fixture encoding through DRN Utils. Each fake API keeps its own lifecycle behavior. These helpers do not participate in independent wire-contract validation. Graylog uses DRN Utils for unpadded URL-safe password encoding. Both bootstraps retain explicit canonical Base64 and strict UTF-8 validation for stored Secret fields.

Coverage includes automatic fresh staging initialization after an atomic ConfigMap claim, preserving existing custom settings and legacy seeds, partial recovery, and adding an application without recreating existing Secrets. Marker tests cover enrollment of existing installs, total Secret loss, concurrent initializers, create conflicts, interrupted initialization, ambiguous create responses, invalid markers and API errors. Only the confirmed marker creator can generate automatically. A retained marker with no surviving settings requires explicit one-run recovery. Tests verify that the override does not bypass marker validation and does not persist into subsequent runs.

Tests also cover duplicate application names, invalid survivors, shared-key mismatches, competing Secret creators, interrupted retries, safe API errors and token refresh for Secret and marker requests. The fake API rejects Deployment reads. These tests do not establish live RBAC, TLS behavior, ConfigMap retention or SDK container resource requirements. Test implementation does not imply an executed or successful test run.

Graylog tests also cover password formats, the administrator hash, operator URI preservation and interrupted bootstrap recovery guards. A fake monotonic clock exercises the 15-minute timeout without sleeping. An empty operator URI keeps polling. Create conflicts reuse the winning Secret. Existing custom credentials remain authoritative.

Graylog marker tests cover automatic fresh initialization, enrollment without rotation, total Secret loss, restoration, competing claims, interrupted and ambiguous creation, malformed markers and API failures. Every Secret write requires a prior marker in the fake API. Existing partial-loss tests remain in force. Graylog has no generation override. Marker retention and live RBAC still require runtime validation.

URI polling tests cover recovery from HTTP 5xx and request timeouts, exhaustion of the original deadline and fatal authorization failures. The CI coverage check parses XML and requires one module with both the exact `Bootstrap.Sources.dll` name and a positive `lines_covered` value, independent of attribute order.

The source library's `BOOTSTRAP_TESTS` compilation symbol excludes only the production entry points. Its `FileBasedProgram` feature accepts the source files' package directives, while an explicit Utils package reference supplies the dependency for project compilation. The test project also references Utils explicitly. Tests call the same lifecycle and HTTP request code as the Jobs. CI builds each standalone entry point before starting Sonar analysis. The deployed Jobs restore packages and compile the original files without a project or custom image.

The application and Graylog types live in `DRN.GitOps.Bootstraps.Application` and `DRN.GitOps.Bootstraps.Graylog`. Each file has an explicit `Main` method guarded by `!BOOTSTRAP_TESTS`, preserving standalone execution while allowing both files in the source library.

Both source files declare `#:property PublishAot=false` because their JSON serialization uses reflection. This aligns direct IDE and file-based builds with the existing Job and CI settings. Native AOT and trimming are not supported by these bootstraps.

When local execution is authorized, use:

```sh
dotnet build tests/Bootstrap.slnx --configuration Release
dotnet run --project tests/ApplicationSecretBootstrap.Tests/ApplicationSecretBootstrap.Tests.csproj --configuration Release
```

Both `develop` and `master` call [shared CI](../../.github/workflows/ci.yml). Its [backend action](../../.github/actions/dotnet-sonar-scan/action.yml) follows DRN-Project's SonarScanner for .NET begin/build/coverage/end pattern. It builds both standalone sources, restores and audits test dependencies, restores the manifest-pinned scanner, then analyzes the library and tests. The same test run produces a GitHub test/coverage summary and Microsoft coverage XML for SonarCloud. The backend fails if tests or the Sonar quality gate fail. Trivy and CodeQL remain separate required jobs. Master retains its weekly schedule. Calendar release tags run Trivy only. PR scans remain secretless.

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

`--report-gh` writes its summary only in GitHub Actions. Local runs print coverage and write `TestResults/coverage.xml`. CI rejects reports without covered bootstrap lines, then uploads the report as `bootstrap-coverage`, retained for 14 days, including after test or Sonar failures when a report exists. Sonar imports this Microsoft XML through `sonar.cs.vscoveragexml.reportsPaths`. Its .NET scanner also scans supported configuration files under the repository root with `scanAll=true`. Generated output and agent files are excluded.

Use absolute coverage-settings and results paths because the test host can change its working directory. If a coverage run is interrupted, clean and rebuild before retrying. Static instrumentation changes output assemblies and can leave instrumentation artifacts after an interruption. An empty report is not evidence of zero coverage.

Configure `SONAR_TOKEN` and enable CI-based analysis in the existing SonarCloud project. Disable automatic analysis when using the scanner. Ensure the project supports analysis of both `master` and `develop`. Required branch checks should use the reusable workflow's `ci / gatekeeper` check, which aggregates backend (including Sonar), Trivy and CodeQL. Replace obsolete standalone `sonar` or `gatekeeper` check requirements in repository settings after confirming the first run's check names.

The test and scanner package source is `tests/NuGet.Config`. Each deployed bootstrap's mounted NuGet configuration also enables nuget.org. Runtime restore now needs network access and uses the existing bounded `/tmp` storage. Existing Job commands retain disabled runtime NuGet audit. CI fails on reported test-dependency vulnerabilities or audit retrieval errors, including Utils and its transitive dependencies. Unlike DRN-Project, this repository does not currently use locked restore. Transitive dependencies remain subject to NuGet resolution. Startup timing and resource sufficiency require staging verification.

Package provenance: DRN packages and SonarScanner checked on 2026-10-17. Unchanged xUnit, reporter and coverage hashes retain their 2026-10-12 verification.

| Package | Version | Canonical NuGet SHA-512 |
|---|---|---|
| DRN.Framework.Testing | 0.10.1-preview004 | `bCO8ZW1XNq2+iESC3PeKtT1GGc1PxHM+7iQMDTEtEO4Ug6upjqi935TxIs5Y+RGbHQltPlIICqcC0u5Q2FzJ4A==` |
| DRN.Framework.Utils | 0.10.1-preview004 | `C5kMVEiSx9a6m/SO7TnTprqYHZIL+MzPkRvr8gqp44m+ifgl6N4HX3kd2gjr8FPXAkL451r0CLtNIIR+TOCpTA==` |
| xunit.v3.mtp-v2 | 4.0.2 | `OesFiVVe38QnAO3W7WhSvq1l7rNFS/Kf9ioQXmQ4zkV6fRAsI+RhWz1apRw6s70K2BLk4lq6ql10FKNeAkOyUQ==` |
| Microsoft.Testing.Extensions.GitHubActionsReport | 2.5.1 | `9zM6qx1T0FWBConQwtgRJ3hsDQRH17g3nTW3rpg8ba+sBTkG2moeYwYbYxRfB44lSbyYzoEOxyYVYygwCztdew==` |
| Microsoft.Testing.Extensions.CodeCoverage | 18.12.0 | `bMR8B/80Nx8AyaOyuOTuSjaZtyuTCFeB3Q0Vej8eFBI3PQWOyk5lV8dEdm9TwQLNgxMQUxuF2uauwofS5LsHww==` |
| dotnet-sonarscanner | 12.0.0 | `3de/QlFpKg+CbPeWQmVOTnMh/7A0KIw44zrwIFVyPBtRevdNAi+CODye25mzdkxK1upJ0Qrzypua855bwPFwgg==` |

Downloaded [Testing](https://api.nuget.org/v3/catalog0/data/2026.10.14.15.56.32/drn.framework.testing.0.10.1-preview004.json), [Utils](https://api.nuget.org/v3/catalog0/data/2026.10.14.15.57.10/drn.framework.utils.0.10.1-preview004.json) and [SonarScanner](https://api.nuget.org/v3/catalog0/data/2026.10.15.15.17.03/dotnet-sonarscanner.12.0.0.json) packages matched their canonical NuGet SHA-512 hashes. Both DRN packages target .NET 10 and identify source commit `a8bc3c7f667962d66da37d7ef0df5437f61b4f00`.

The earlier reporter and coverage collector downloads matched their canonical NuGet hashes. The xUnit hash was checked in NuGet catalog metadata. All three versions remain current in the 2026-10-17 registry check.

[SonarScanner 12.0](https://github.com/SonarSource/sonar-scanner-msbuild/releases/tag/12.0.0.12728) removes support for older SonarQube Server versions and TFS XAML builds. This repository uses SonarCloud with the standard .NET begin/build/end workflow. CI must verify the updated scanner and packages.

CI dependency provenance checked on 2026-10-17:

| Action | Release | Verified commit |
|---|---|---|
| actions/setup-dotnet | v6.0.0 | `a98b56852c35b8e3190ac28c8c2271da59106c68` |
| actions/upload-artifact | v7.0.2 | `cf430e030ddbb5b0abf93d22962f4752f3646cd9` |
| github/codeql-action | v4.39.0 | `6f2ac81f5cd81fe351a197ba0b00d91d03cd2aee` |

Upstream Git tag queries verified these commits. CodeQL's annotated tag was peeled to its commit. Its pinned defaults select CodeQL 2.27.2 through `tools: linked`. The setup-dotnet v6 release retains the existing inputs and Node 24 runtime. CI uses GitHub-hosted Ubuntu 24.04 runners.

Trivy is pinned to v0.76.0 in both PR and branch scans. Its [official release metadata](https://github.com/aquasecurity/trivy/releases/tag/v0.76.0) records Linux x64 archive SHA-256 `e24b1c51293c1b15e4ba8d45bbc7891ba053f90278ec2698f3562c993e145fe7`. The unchanged Trivy action v0.36.0 accepts this explicit version. Checkout v7.0.1 and Trivy action v0.36.0 remain current at this review. Local builds and scans were not run.
