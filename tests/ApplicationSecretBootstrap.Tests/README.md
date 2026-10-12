# Credential bootstrap tests

This project tests the real [application bootstrap](../../infrastructure/application-secrets/stage/bootstrap.cs) and [Graylog bootstrap](../../infrastructure/graylog/mongodb/bootstrap.cs). [Bootstrap.Sources](../Bootstrap.Sources/Bootstrap.Sources.csproj) compiles those files as a non-test library and exposes its internals only to this test assembly. This separates bootstrap code from tests for Sonar and coverage. Tests use DRN.Framework.Testing, xUnit v3 and AwesomeAssertions. Row tests use `DataInlineUnit` and `DataMemberUnit`. Fake HTTP handlers model Kubernetes responses. Tests never contact a cluster or read service account credentials.

Coverage includes fresh creation, preserving custom settings, incomplete pairs and missing Secrets with existing Deployments. It also covers malformed fields, shared-key mismatches, competing creators, interrupted creation, API errors and token refresh. These tests do not establish live TLS behavior or SDK container resource requirements.

Graylog tests also cover password formats, the administrator hash, operator URI preservation and interrupted bootstrap recovery guards. A fake monotonic clock exercises the 15-minute timeout without sleeping. An empty operator URI keeps polling. Create conflicts reuse the winning Secret. Existing custom credentials remain authoritative.

The source library's `BOOTSTRAP_TESTS` compilation symbol excludes only the production entry points. Tests call the same lifecycle and HTTP request code as the Jobs. CI builds each standalone entry point before starting Sonar analysis. The deployed Jobs still compile the original files without a project or external packages.

When local execution is authorized, use:

```sh
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

The test and scanner package source is `tests/NuGet.Config`. Deployed bootstraps keep their separate offline configurations. CI fails on reported test-dependency vulnerabilities or audit retrieval errors. Unlike DRN-Project, this repository does not currently use locked restore. Transitive dependencies remain subject to NuGet resolution.

Package provenance checked on 2026-10-12:

| Package | Version | Canonical NuGet SHA-512 |
|---|---|---|
| DRN.Framework.Testing | 0.10.1-preview003 | `bxGZIiI/X86hO4FH3Poq/6QGbf0MBO5atNkpdVDw72mVTwmv4Xd4VGo6hjVQM8qNuKh4wIeOHjWcGyUySi9wLA==` |
| xunit.v3.mtp-v2 | 4.0.2 | `OesFiVVe38QnAO3W7WhSvq1l7rNFS/Kf9ioQXmQ4zkV6fRAsI+RhWz1apRw6s70K2BLk4lq6ql10FKNeAkOyUQ==` |
| Microsoft.Testing.Extensions.GitHubActionsReport | 2.5.1 | `9zM6qx1T0FWBConQwtgRJ3hsDQRH17g3nTW3rpg8ba+sBTkG2moeYwYbYxRfB44lSbyYzoEOxyYVYygwCztdew==` |
| Microsoft.Testing.Extensions.CodeCoverage | 18.12.0 | `bMR8B/80Nx8AyaOyuOTuSjaZtyuTCFeB3Q0Vej8eFBI3PQWOyk5lV8dEdm9TwQLNgxMQUxuF2uauwofS5LsHww==` |
| dotnet-sonarscanner | 11.2.1 | `GGLhuNOiN4XdQkrFCd0AXLW4qoc3AjYcxGZxsqmn5Lvys/Bso18y64m0tIFGSb4uAEvVSMAqjz3P6vXBGI2tFg==` |

The DRN package download matched its catalog hash. The xUnit hash was checked in NuGet catalog metadata. The setup-dotnet v5.4.0 tag resolves to its pinned action commit.

The downloaded reporter, coverage collector and scanner packages matched the SHA-512 values in their canonical NuGet catalogs. The upstream `actions/upload-artifact` v7.0.1 tag resolves directly to `043fb46d1a93c77aae656e7c1c64a875d1fc6a0a`.
