# Credential bootstrap tests

Open [Bootstrap.slnx](../Bootstrap.slnx) in your IDE to manage the bootstrap source library and test project together. The source library links the deployed bootstrap files, so edits apply directly to the code used by the Jobs.

This project tests the real [application bootstrap](../../infrastructure/application-secrets/stage/bootstrap.cs) and [Graylog bootstrap](../../infrastructure/graylog/mongodb/bootstrap.cs). [Bootstrap.Sources](../Bootstrap.Sources/Bootstrap.Sources.csproj) compiles those files as a non-test library and exposes its internals only to this test assembly. This separates bootstrap code from tests for Sonar and coverage. Tests use DRN.Framework.Testing, xUnit v3 and AwesomeAssertions. Row tests use `DataInlineUnit` and `DataMemberUnit`. Fake HTTP handlers model Kubernetes responses. Tests never contact a cluster or read service account credentials.

Each bootstrap declares its own typed Secret and metadata models in its deployed file. Tests use those same models for valid fixtures, captured requests and fake API storage. Secret data remains a dictionary because key names vary. Malformed-response tests retain raw JSON to exercise incompatible field types and sanitized errors. Each bootstrap clones `JsonConventions.DefaultOptions` with `WhenWritingNull`; tests reuse those options. Camel-case property naming preserves Kubernetes field names without model attributes. Both Jobs retain single-file deployment with unchanged manifests and reference DRN.Framework.Utils 0.10.1-preview003, which supplies SharedKernel transitively.

Outgoing JSON is also checked against a separate typed `SecretWireContract`. Its required properties use explicit Kubernetes JSON names and independent, case-sensitive serializer options. Unmapped fields are rejected at both the Secret and metadata levels, including `data`, `ownerReferences` and `annotations`, even when null. This preserves independent wire-format checks without `JsonDocument` or production model attributes.

`FixtureJson<T>` centralizes fixture serialization, cloning and snapshots with each bootstrap's serializer options. `BootstrapFixtures` shares fake API responses and Base64 fixture encoding through DRN Utils. Each fake API keeps its own lifecycle behavior. These helpers do not participate in independent wire-contract validation. Graylog uses DRN Utils for unpadded URL-safe password encoding. Both bootstraps retain explicit canonical Base64 and strict UTF-8 validation for stored Secret fields.

Coverage includes fresh creation, preserving custom settings, incomplete pairs and missing Secrets with existing Deployments. It also covers malformed fields, shared-key mismatches, competing creators, interrupted creation, API errors and token refresh. These tests do not establish live TLS behavior or SDK container resource requirements.

Graylog tests also cover password formats, the administrator hash, operator URI preservation and interrupted bootstrap recovery guards. A fake monotonic clock exercises the 15-minute timeout without sleeping. An empty operator URI keeps polling. Create conflicts reuse the winning Secret. Existing custom credentials remain authoritative.

URI polling tests cover recovery from HTTP 5xx and request timeouts, exhaustion of the original deadline and fatal authorization failures. The CI coverage check parses XML and requires one module with both the exact `Bootstrap.Sources.dll` name and a positive `lines_covered` value, independent of attribute order.

The source library's `BOOTSTRAP_TESTS` compilation symbol excludes only the production entry points. Its `FileBasedProgram` feature accepts the source files' package directives, while an explicit Utils package reference supplies the dependency for project compilation. The test project also references Utils explicitly. Tests call the same lifecycle and HTTP request code as the Jobs. CI builds each standalone entry point before starting Sonar analysis. The deployed Jobs restore packages and compile the original files without a project or custom image.

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

Package provenance checked on 2026-10-12:

| Package | Version | Canonical NuGet SHA-512 |
|---|---|---|
| DRN.Framework.Testing | 0.10.1-preview003 | `bxGZIiI/X86hO4FH3Poq/6QGbf0MBO5atNkpdVDw72mVTwmv4Xd4VGo6hjVQM8qNuKh4wIeOHjWcGyUySi9wLA==` |
| DRN.Framework.Utils | 0.10.1-preview003 | `pJSmIyJdDmMEwEUJztsDK9UMxuFqFvztTMCD7q5DOxMZfks1TPEV95mNbFPVUTMKHBDt4oZVc5+Kv/szOKcbAQ==` |
| xunit.v3.mtp-v2 | 4.0.2 | `OesFiVVe38QnAO3W7WhSvq1l7rNFS/Kf9ioQXmQ4zkV6fRAsI+RhWz1apRw6s70K2BLk4lq6ql10FKNeAkOyUQ==` |
| Microsoft.Testing.Extensions.GitHubActionsReport | 2.5.1 | `9zM6qx1T0FWBConQwtgRJ3hsDQRH17g3nTW3rpg8ba+sBTkG2moeYwYbYxRfB44lSbyYzoEOxyYVYygwCztdew==` |
| Microsoft.Testing.Extensions.CodeCoverage | 18.12.0 | `bMR8B/80Nx8AyaOyuOTuSjaZtyuTCFeB3Q0Vej8eFBI3PQWOyk5lV8dEdm9TwQLNgxMQUxuF2uauwofS5LsHww==` |
| dotnet-sonarscanner | 11.2.1 | `GGLhuNOiN4XdQkrFCd0AXLW4qoc3AjYcxGZxsqmn5Lvys/Bso18y64m0tIFGSb4uAEvVSMAqjz3P6vXBGI2tFg==` |

The DRN.Framework.Testing package download matched its catalog hash. The cached Utils package hash matched its [canonical NuGet catalog](https://api.nuget.org/v3/catalog0/data/2026.10.04.17.33.37/drn.framework.utils.0.10.1-preview003.json). The xUnit hash was checked in NuGet catalog metadata. The setup-dotnet v5.4.0 tag resolves to its pinned action commit.

The downloaded reporter, coverage collector and scanner packages matched the SHA-512 values in their canonical NuGet catalogs. The upstream `actions/upload-artifact` v7.0.1 tag resolves directly to `043fb46d1a93c77aae656e7c1c64a875d1fc6a0a`.
