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

Publish preview GitOps tags `YYYY.M.sequence-previewNNN` from develop, with three-digit preview numbers from `001` through `999`. Publish final tags `YYYY.M.sequence` from master. Select immutable tags through `refs/tags/...`. Staging accepts both. Production accepts final tags only and remains disabled initially. Each environment has its own cluster and Argo CD, using the drn-project workload namespace.

Root Helm parameters pass Argo CD's resolved SHA to all Git children. Upstream Helm chart pins and image digests are independent. Final promotion retains tested image digests. If promotion to master creates a new commit, run its CI and validate that snapshot in staging before production. GitOps tags do not publish application images. Staging uses AutoMigrateStaging with prototype disabled. Production forbids startup migrations and requires a separately provisioned database.

Keep bootstrap administration separate from the restricted workload project. Preserve namespace ingress isolation, Traefik's deny default and explicit public listener authorization. Credentials, seed/key material and trusted database CAs are Secret inputs, provisioned by the documented staging bootstraps or externally. Do not add real secret data to this repository.

Staging workloads project only `postgresql-app/pgpass` and `postgresql-ca/ca.crt` into a read-only volume with mode `0440` and the existing pod group. ConfigMaps own password-free context connection strings using Npgsql `Passfile` and `SSL Mode=VerifyFull`. Match the generated pgpass host, port, database and username. Never mount the CA private key. Production requires external application Secrets, connection strings and CA inputs. Roll out applications after credential or CA rotation and verify connections. Mounted Secret updates do not prove pool refresh.

Staging's dedicated `application-secrets` project and Job run before workloads. Validate and preserve existing Secrets, including legacy independent seeds. Create missing Secrets from Nexus's seed/key settings with application-specific hosts. Recover missing Nexus from the first configured survivor. If none survive, require restoration or a one-run `ALLOW_CREDENTIAL_GENERATION=true` override to generate shared credentials. Keep the override out of GitOps. Do not gate creation on Deployments. Operators restore lost legacy seeds and custom hosts. Extend the Applications list and named Secret-read RBAC together. Keep Secret creation outside the workload project. Generated hosts cover local access and service DNS. Operators configure custom ingress hosts.

Both credential bootstraps run single C# files through the same official digest-pinned .NET SDK image. Kustomize generates source ConfigMaps at child wave -1. Keep source and root filesystems read-only and SDK output in bounded temporary storage. Preserve resource limits and `NuGetAudit=false`. Startup restores DRN.Framework.Utils 0.10.1-preview003 and its dependencies from nuget.org. Verify restore timing and resources in staging.

Each bootstrap clones `JsonConventions.DefaultOptions` and omits null properties without model attributes. Kubernetes HTTP uses built-in .NET. Preserve CA bundle and hostname verification, per-request token reads, safe logs and Secret lifecycle checks. Keep bootstrap ConfigMap permission separate from workload permissions. Automatic pruning is disabled. Remove unused hashed ConfigMaps deliberately. See [staging bootstrap](../docs/staging-configuration.md#automatic-staging-secret-bootstrap) and [Graylog operations](../docs/platform-operations.md#graylog) for runtime limits.

Both bootstrap files declare `#:property PublishAot=false`, matching the Job and CI commands. Preserve this source-level setting for direct IDE and file-based builds because the serializers use reflection. Native AOT and trimming are not supported.

Bootstrap types use the `DRN.GitOps.Bootstraps.Application` and `DRN.GitOps.Bootstraps.Graylog` namespaces. Explicit `Main` methods remain guarded by `!BOOTSTRAP_TESTS` so the same files compile as standalone programs and as the test source library.

Bootstrap Linkerd Viz before Grafana. Grafana's Prometheus authorization policy requires the Viz namespace to exist.

Graylog's Kustomization must retain all MongoDB resources. Preserve named Secret reads and create-only RBAC. Existing credentials are authoritative. Reject partial sets. Keep the 15-minute operator URI wait and 20-minute Job deadline.

Bootstrap.Sources links both deployed files without entry points and grants internals access to the DRN.Framework.Testing project. Both projects reference Utils. Tests reuse deployed models and serializer options.

The project uses Kubernetes 1.36.4 for testing. The pinned Linkerd charts require Kubernetes 1.31 or newer. This component minimum does not establish compatibility for the whole stack. Update CRDs, control plane and Viz together, verify image digests, and recheck proxy cryptography defaults and Grafana dashboard revisions on upgrades.

Graylog's operator URI polling retries HTTP 5xx and request timeouts on its existing five-second cadence without resetting the 15-minute deadline. Keep HTTP 401/403, malformed values and failures outside that polling read fatal. Classify API failures by status code, never error-message text.

Dependency updates target compatible releases. Keep PostgreSQL on major 18 with standard-trixie images and MongoDB on the 8.2 line unless a database upgrade is explicitly requested. PostgreSQL major upgrades on existing clusters require a tested migration, verified backup and planned downtime before syncing. Do not directly apply the Trixie pin to an existing Bookworm cluster: CloudNativePG in-place major upgrades require the same OS distribution. Standard images require a backup plugin for Barman Cloud integration. Keep chart-managed helper versions aligned with their owning chart. The dated dependency review in docs/platform-operations.md records compatibility exceptions and verification limits.

Keep Application health Lua identical in both Argo CD values files and compatible with the default Lua sandbox. Root waves require a successful finished sync for the current source and revision, a current Synced comparison and healthy resources. Reject queued, running or failed operations, dry runs, manual selective syncs, apply-strategy syncs and nonempty local manifests. Normalize omitted optional `ignoreDifferences` fields before comparing spec and status.

Accept selective automatic self-heals only with positive `autoHealAttemptsCount` and successful automated history matching every current source and revision. In Argo CD 3.5.4, this history proves an unfiltered repository sync using hooks. Manual history cannot prove hooks ran. Missing matching history or no-op source updates can require a full child sync. Install gate changes through Argo CD Helm values, outside the DRN root. Reverify these semantics on Argo CD upgrades.

Group root resource templates under `deployment/templates/NNN-description/`. Zero-pad directory prefixes to at least three digits. Use positive sync-wave annotations matching those prefixes numerically. Projects and infrastructure use waves 10 through 120, Nexus uses 200, and Sample uses 300. Independent applications share a wave. Space application tiers by 100 where possible and use gaps for dependencies. Keep `validate.yaml` at the templates root because it emits no resource. Verify annotations when moving or adding templates. Missing annotations default to wave 0, before projects. Child resource waves are local to each Application. See [wave allocation](../docs/platform-operations.md#installation-dependencies).

Sample and Nexus bases probe their local anonymous WeatherForecast routes for startup, readiness and liveness through an internal Http1 listener on port 8081. Keep service traffic on its existing h2c listeners. Staging and production AllowedHosts Secrets must include each service's probe Host (`sample` or `nexus`). These probes check HTTP responsiveness, not database or downstream health. See docs/platform-operations.md for timings and Linkerd authorization constraints.

Sample and Nexus inherit separate `1Gi` ReadWriteOnce filesystem PVCs at `/appdata` from their service bases. Keep claims in the Deployment's child sync wave for delayed binding. Preserve fsGroup 1654, one replica, `Recreate` and `rollingUpdate: null`. Keep temporary volumes and their budgets unchanged. Retain claims with `Prune=false,Delete=false`. Preserve required emptyDir data before rollout. MongoDB's `4Gi` data request applies to new volumes. Existing `8Gi` volumes require a verified migration before syncing. Never force PVC shrink or deletion. See [storage operations](../docs/platform-operations.md#application-persistent-storage) for provisioning, backups and retirement.

MongoDB requires native TLS with SCRAM and remains unmeshed. Its certificate uses the linkerd-trust-anchor issuer. The graylog-database-trust Bundle supplies PEM and PKCS12 trust to Graylog and Data Node. Preserve certificate verification and the operator-generated TLS URI. The Graylog chart enables self-signed startup for Data Node's API and OpenSearch TLS without manual CA provisioning. Managed Data Node CA and renewal configuration is outside beta scope. MongoDB CA changes require client restarts and coordinated rotation with the shared issuer. See [Graylog operations](../docs/platform-operations.md#graylog).

The pinned Linkerd proxy prefers hybrid X25519MLKEM768 key exchange. Preserve upstream defaults without redundant PQ flags. Certificate authentication remains classical. Native database/Graylog TLS has no verified configuration-only PQ enablement with current pins. Reassess runtime and peer compatibility on upgrades. Never infer PQ certificate support from hybrid TLS support. See [post-quantum evidence and limits](../README.md#post-quantum-support-in-the-pinned-deployment).

## Conventions

Git: integration branch `develop`. Release branch `master`. Topic PRs and Dependabot target develop. Release PRs promote develop to master. Deployment roots select immutable tags, never branch heads.

## Commands and verification

Default execution scope is static inspection and git diff --check. Do not restore, build, render charts/overlays, run apps, tests, benchmarks, clusters or ad-hoc helper scripts without explicit authorization. JSON parsing and read-only source/diff inspection are static checks. No staging, commit, tag, push or deployment without explicit user authorization.

Develop and master call `.github/workflows/ci.yml`. Master also runs weekly. The backend action `.github/actions/dotnet-sonar-scan` builds both standalone bootstraps and audits test dependencies. It runs SonarScanner around the source-library build and unit coverage. `.config/dotnet-tools.json` owns the scanner version. The test project pins GitHubActionsReport 2.5.1 and the coverage collector. Coverage includes only Bootstrap.Sources. GitHub summaries and a 14-day artifact accompany Sonar's Microsoft XML import. Sonar also scans supported configuration files.

The shared gatekeeper requires backend checks, including the Sonar quality gate, Trivy and CodeQL. Configure SONAR_TOKEN and CI-based analysis for both branches in SonarCloud. Confirm the first run before changing required checks to `ci / gatekeeper`. See [test and CI configuration](../tests/ApplicationSecretBootstrap.Tests/README.md) for commands and provenance.

PRs into develop and master run secretless Trivy/CodeQL scans. Calendar-shaped preview tags route to develop's Trivy workflow, and final tags to master's. Filters do not enforce ancestry. Verify each release commit belongs to the required branch before tagging. Preserve full SHA-pinned third-party actions and verify provenance before version updates. CI does not publish releases.

## Documentation and release notes

Update owning deployment docs and this profile when shared deployment behavior changes. Record operational changes in deployment documentation. There is no package-specific release-note file, and configuration-only changes need no separate release notes. Keep CAD workflow artifacts local in ignored `.agent/temp/`.
