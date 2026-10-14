# Repository profile

## Scope and source map

This repository contains Argo CD GitOps configuration for DRN Framework Sample and Nexus. Application source and image publication live in the sibling DRN-Project repository and are outside this repository's write scope.

- `apps/bootstrap-project.yaml`: administrator-owned root project.
- `apps/{staging,production}/application.yaml`: one manually synced root per separate cluster.
- `deployment/`: native Helm chart rendering scoped projects and leaf Applications.
- `infrastructure/`: platform resource payloads and Argo CD installation values.
- Keep standalone Kubernetes resources in separate files in their existing component directory, with descriptive names and kind suffixes where needed. Keep chart-owned `extraObjects` in Helm values. File organization must preserve Application source paths, resource identities, ownership and sync waves.
- `services/{sample,nexus}/{base,staging,production}`: Kustomize workloads and DRN configuration.
- `services/postgresql/{base,staging}`: staging database.
- `tests/Bootstrap.slnx`: IDE and solution-build entry point for the bootstrap source library and test project. Run tests through `tests/ApplicationSecretBootstrap.Tests/ApplicationSecretBootstrap.Tests.csproj`.
- `README.md`, `docs/platform-operations.md`, `docs/staging-configuration.md`: source-owned deployment documentation.

## Repository overview

- Type: Kubernetes GitOps configuration with two standalone C# credential bootstraps.
- Architecture: Argo CD roots, a Helm application chart, Kustomize payloads and workload overlays. `tests/Bootstrap.Sources` links the deployed bootstrap sources into a non-packable library.
- Frontend: none in this repository. Razor, React and DRN buildwww application sources belong to the sibling DRN-Project repository.
- Testing: `tests/ApplicationSecretBootstrap.Tests` is a non-packable .NET 10 executable test project using DRN.Framework.Testing, xUnit v3 and Microsoft.Testing.Platform. It references `Bootstrap.Sources` and uses fake Kubernetes HTTP handlers. No local execution is authorized by this inventory.

## Framework Skills

This repository consumes DRN Framework packages for its bootstraps and tests. Select only the skills relevant to the task, in the order below. Package implementation and broader application examples are external context, not local source paths.

| Skill | Local trigger and evidence |
|---|---|
| `overview-drn-framework` | Orient bootstrap dependency work. Both project files reference DRN.Framework.Utils. |
| `overview-drn-testing` | Choose bootstrap test scope. The test project references DRN.Framework.Testing. |
| `drn-sharedkernel` | Review `JsonConventions.DefaultOptions` used by both deployed bootstrap files through Utils. |
| `drn-utils` | Review bootstrap encoding helpers and the direct Utils package references. |
| `drn-testing` | Modify bootstrap tests using `DataInlineUnit` and `DataMemberUnit`. |

Keep the other DRN and frontend skills as a conditional reference catalog. Do not infer local hosting, domain, Entity Framework, Razor or buildwww projects from their presence. Use those skills only when the task explicitly needs their external framework or deployed application context. There are no repository-specific custom workflow routes or custom skill groups.

## Deployment rules

Format deployment templates with two-space indentation and indented sequence items. Order Application fields as project, source, destination, optional ignoreDifferences, then syncPolicy. Within source, put chart or path before repoURL and targetRevision. Within destination, put namespace before server and use the plain cluster URL. Use a space after comment markers. Preserve component-specific settings and sequence ordering when formatting.

Only staging and production are supported deployment environments. Use these full names in environment values, paths and documentation. Local application development happens outside this GitOps repository. The develop Git branch remains the integration branch and does not represent a deployed development environment. Keep the DRN configuration keys that disable development-only behavior in their overlays.

Merge reviewed changes into develop and promote them through develop → master. Publish preview GitOps tags `YYYY.M.sequence-previewNNN` from develop, with three-digit preview numbers from `001` through `999`, and final tags `YYYY.M.sequence` from master. Select immutable exact tags through `refs/tags/...`. Staging accepts both; production accepts final tags only and remains disabled initially. Each deployment environment owns its own cluster and Argo CD, using the same drn-project workload namespace.

Root Helm parameters pass Argo CD's resolved SHA to all Git children. Upstream Helm chart pins and image digests are independent. Final promotion retains the tested image digests. If promotion to master creates a new commit, run its CI and validate that exact snapshot in staging before production promotion. GitOps tags do not publish application images. Staging uses AutoMigrateStaging with prototype disabled; production startup migrations are forbidden and its database is provisioned separately.

Keep bootstrap administration separate from the restricted workload project. Preserve namespace ingress isolation, Traefik's deny default and explicit public listener authorization. Credentials, seed/key material and trusted database CAs are Secret inputs, provisioned by the documented staging bootstraps or externally. Do not add real secret data to this repository.

Staging workloads project only `postgresql-app/pgpass` and `postgresql-ca/ca.crt` from CloudNativePG into a read-only volume with mode `0440` and the existing pod group. Staging ConfigMaps own explicit password-free context connection strings using Npgsql `Passfile` and `SSL Mode=VerifyFull`. Match the generated pgpass host, port, database and username. Never mount the CA private key. Staging's dedicated `application-secrets` project and Job generate a shared seed and Nexus key before workloads. Validate and preserve existing Secrets, including legacy independent seeds. Create only missing application Secrets using Nexus's seed/key settings and application-specific hosts. Recover missing Nexus from the first configured survivor, generating credentials only when none survive. Do not gate creation on Deployments. Operators own total credential loss and restoration of legacy independent seeds or custom hosts. Extend the Applications list and named Secret-read RBAC together for new consumers. Keep Secret creation outside the workload project and workload deployment ordered after bootstrap. Generated hosts cover local access and service DNS; custom ingress hosts require operator configuration. Production retains external application Secrets, connection strings and CA inputs. Roll out applications after database credential or CA rotation and verify connections; mounted Secret updates do not prove pool refresh.

The staging application Secret bootstrap runs one C# file through an official digest-pinned .NET SDK image. Kustomize generates the source ConfigMap at child wave -1. Keep the source and root filesystem read-only and SDK output in bounded temporary storage. No custom image is required. Both bootstraps reference DRN.Framework.Utils 0.10.1-preview003 and restore it with transitive dependencies from nuget.org at startup. Each clones JsonConventions.DefaultOptions and omits null properties without model attributes. Kubernetes HTTP remains built-in .NET. Preserve cluster CA bundle and hostname verification, per-request token reads, safe logs and the existing Secret lifecycle checks. Keep the bootstrap AppProject's ConfigMap permission separate from workload permissions. Automatic pruning remains disabled. Old hashed source ConfigMaps require deliberate cleanup. See docs/staging-configuration.md for runtime limits and verification needs.

Both bootstrap files declare `#:property PublishAot=false`, matching the Job and CI commands. Preserve this source-level setting for direct IDE and file-based builds because the serializers use reflection. Native AOT and trimming are not supported.

Bootstrap types use the `DRN.GitOps.Bootstraps.Application` and `DRN.GitOps.Bootstraps.Graylog` namespaces. Explicit `Main` methods remain guarded by `!BOOTSTRAP_TESTS` so the same files compile as standalone programs and as the test source library.

Bootstrap Linkerd Viz before Grafana. Grafana's Prometheus authorization policy requires the Viz namespace to exist.

Graylog's MongoDB credential bootstrap uses the same official SDK pin and single-file C# pattern. Its Kustomization includes all existing MongoDB resources and a source ConfigMap at wave -1. Preserve named Secret reads and create-only RBAC. Keep existing credentials authoritative and reject partial sets. Preserve the 15-minute operator URI wait and 20-minute Job deadline. The Bootstrap.Sources library links both deployed files without their entry points and grants internals access to the DRN.Framework.Testing test project. Both projects explicitly reference Utils, and tests reuse the deployed models and serializer options. Both branch workflows build each standalone file before project analysis and unit coverage. Existing manifests retain resource limits and NuGetAudit=false. CI audits test dependencies, including Utils and its transitive dependencies. Startup restore requires nuget.org access and resource verification. See docs/platform-operations.md for SDK resource limits and source ConfigMap cleanup.

The project uses Kubernetes 1.36.4 for testing. The pinned Linkerd charts require Kubernetes 1.31 or newer. This component minimum does not establish compatibility for the whole stack. Update CRDs, control plane and Viz together, verify image digests, and recheck proxy cryptography defaults and Grafana dashboard revisions on upgrades.

Graylog's operator URI polling retries HTTP 5xx and request timeouts on its existing five-second cadence without resetting the 15-minute deadline. Keep HTTP 401/403, malformed values and failures outside that polling read fatal. Classify API failures by status code, never error-message text.

Dependency updates target compatible releases. Keep PostgreSQL on major 18 with standard-trixie images and MongoDB on the 8.2 line unless a database upgrade is explicitly requested. PostgreSQL major upgrades on existing clusters require a tested migration, verified backup and planned downtime before syncing. Do not directly apply the Trixie pin to an existing Bookworm cluster: CloudNativePG in-place major upgrades require the same OS distribution. Standard images require a backup plugin for Barman Cloud integration. Keep chart-managed helper versions aligned with their owning chart. The dated dependency review in docs/platform-operations.md records compatibility exceptions and verification limits.

Keep the Application health Lua identical in both Argo CD installation values files. Root waves require a finished successful sync for the current source and resolved revision, a current Synced comparison, and healthy child resources. Accept a selective automatic self-heal only when it has a positive autoHealAttemptsCount and successful automated deployment history matching every current source and revision. In pinned Argo CD 3.5.4, automated history proves an unfiltered repository sync using hooks. Manual history does not preserve enough information to prove hooks ran. Reject queued/running/failed operations, dry runs, manual selective syncs, apply-strategy syncs and nonempty local-manifest overrides. Missing or evicted matching automated history requires a full child sync. Normalize omitted optional ignoreDifferences fields before comparing spec and status. Keep the script compatible with Argo CD's default Lua sandbox. No-op source updates can require explicit full child syncs. The gate is installed through Argo CD Helm values, outside the DRN root. Reverify history and automated sync semantics when upgrading Argo CD.

Use explicit positive root sync waves and group resource templates under `deployment/templates/NNN-description/`. Zero-pad directory numbers to at least three digits and keep each sync-wave annotation numerically equal to its directory prefix. Projects and infrastructure use waves 10 through 120, Nexus uses 200, and Sample uses 300. Independent applications share a wave. Space new application tiers by 100 where possible and use gaps for intermediate dependencies. Keep `validate.yaml` at the templates root because it emits no resource. Verify directory and annotation alignment when moving or adding templates; missing annotations default to wave 0 and run before projects. See docs/platform-operations.md for the allocation convention. Child resource waves are local to each Application.

Sample and Nexus bases probe their local anonymous WeatherForecast routes for startup, readiness and liveness through an internal Http1 listener on port 8081. Keep service traffic on its existing h2c listeners. Staging and production AllowedHosts Secrets must include each service's probe Host (`sample` or `nexus`). These probes check HTTP responsiveness, not database or downstream health. See docs/platform-operations.md for timings and Linkerd authorization constraints.

MongoDB requires native TLS with SCRAM and remains unmeshed. Its certificate uses the existing linkerd-trust-anchor issuer. The graylog-database-trust Bundle supplies PEM and PKCS12 trust to Graylog and Data Node without helper scripts. Preserve certificate verification and the operator-generated TLS URI. The pinned Graylog chart enables automatic self-signed startup for Data Node's API and OpenSearch TLS; manual CA provisioning is not required for this mode. Managed Data Node CA/renewal configuration is outside the current beta scope. MongoDB CA changes require client restarts and coordinated rotation with the shared issuer. See docs/platform-operations.md.

The pinned standard Linkerd proxy already prefers hybrid X25519MLKEM768 key exchange. Preserve its upstream defaults without redundant PQ flags. Certificate authentication remains classical. Native database/Graylog TLS has no verified configuration-only PQ enablement with current pins. Reassess runtime and peer compatibility on version changes; never infer PQ certificate support from hybrid TLS support. See README's post-quantum support section for pinned-source evidence and runtime verification limits.

## Conventions

Git: integration branch `develop`; release branch `master`. Topic PRs and Dependabot target develop. Release PRs promote develop to master. Preview releases come from develop; final releases come from master. Deployment roots always select immutable tags, never branch heads.

## Commands and verification

Default execution scope is static inspection and git diff --check. Do not restore, build, render charts/overlays, run apps, tests, benchmarks, clusters or ad-hoc helper scripts without explicit authorization. JSON parsing and read-only source/diff inspection are static checks. No staging, commit, tag, push or deployment without explicit user authorization.

CI: pull-request scans PRs into develop and master with secretless Trivy/CodeQL. Develop and master call `.github/workflows/ci.yml`. Its backend action `.github/actions/dotnet-sonar-scan` builds both standalone bootstraps, audits test dependencies, and runs SonarScanner for .NET around the source-library build and unit coverage. The scanner version belongs to `.config/dotnet-tools.json`. The test project directly pins GitHubActionsReport 2.5.1 and the coverage collector. Coverage includes only Bootstrap.Sources, excluding test helpers and dependencies. GitHub summaries and the 14-day coverage artifact accompany Sonar's Microsoft XML import. Sonar scans supported repository configuration files as well as C#. The shared gatekeeper requires backend (including the Sonar quality gate), Trivy and CodeQL. Both branch pushes run this pipeline. Master also runs weekly. Configure SONAR_TOKEN and CI-based analysis with both branches in SonarCloud. Update required checks for the reusable workflow's ci / gatekeeper name after confirming the first run. The test project, commands and provenance are documented in `tests/ApplicationSecretBootstrap.Tests/README.md`. Local execution still requires explicit authorization. Calendar-shaped preview tag pushes run Trivy in develop. Non-preview calendar tag pushes run Trivy in master. Tag filters route scans but do not enforce branch ancestry. Verify the release commit belongs to its required branch before tagging. Dependabot targets develop. Preserve full SHA-pinned third-party actions and verify upstream provenance before version updates. There are no release-publishing jobs here.

## Documentation and release notes

Update owning deployment docs and this profile when shared deployment behavior changes. This repository has no package-specific release-note file. Record operational changes in deployment documentation; separate release notes are not required for configuration-only changes. CAD workflow artifacts belong in ignored `.agent/temp/` and remain local.
