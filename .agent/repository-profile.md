# Repository profile

## Scope and source map

This repository contains Argo CD GitOps configuration for DRN Framework Sample and Nexus. Application source and image publication live in the sibling DRN-Project repository and are outside this repository's write scope.

- `apps/bootstrap-project.yaml`: administrator-owned root project.
- `apps/{staging,production}/application.yaml`: one manually synced root per separate cluster.
- `deployment/`: native Helm chart rendering scoped projects and leaf Applications.
- `infrastructure/`: platform resource payloads and Argo CD installation values.
- Keep standalone Kubernetes resources in separate files in their existing component directory, with descriptive names and kind suffixes where needed. Keep chart-owned `extraObjects` in Helm values. File organization must preserve Application source paths, resource identities, ownership and sync waves.
- `services/{sample,nexus}/{base,stage,prod}`: Kustomize workloads and DRN configuration.
- `services/postgresql/{base,stage}`: staging database.
- `README.md`, `docs/platform-operations.md`, `docs/staging-configuration.md`: source-owned deployment documentation.

## Deployment rules

Format deployment templates with two-space indentation and indented sequence items. Order Application fields as project, source, destination, optional ignoreDifferences, then syncPolicy. Within source, put chart or path before repoURL and targetRevision. Within destination, put namespace before server and use the plain cluster URL. Use a space after comment markers. Preserve component-specific settings and sequence ordering when formatting.

Only stage and prod are supported deployment environments. Local application development happens outside this GitOps repository. The develop Git branch remains the integration branch and does not represent a deployed development environment. Keep the DRN configuration keys that disable development-only behavior in their overlays.

Merge reviewed changes into develop and promote them through develop → master. Publish preview GitOps tags `YYYY.M.sequence-preview.N` from develop and final tags `YYYY.M.sequence` from master. Select immutable exact tags through `refs/tags/...`. Stage accepts both; prod accepts final tags only and remains disabled initially. Each deployment environment owns its own cluster and Argo CD, using the same drn-project workload namespace.

Root Helm parameters pass Argo CD's resolved SHA to all Git children. Upstream Helm chart pins and image digests are independent. Final promotion retains the tested image digests. If promotion to master creates a new commit, run its CI and validate that exact snapshot in staging before production promotion. GitOps tags do not publish application images. Stage uses AutoMigrateStaging with prototype disabled; production startup migrations are forbidden and its database is provisioned separately.

Keep bootstrap administration separate from the restricted workload project. Preserve namespace ingress isolation, Traefik's deny default and explicit public listener authorization. Credentials, seed/key material and trusted database CAs are external Secret inputs. Do not add real secret data to this repository.

Stage workloads project only `postgresql-app/pgpass` and `postgresql-ca/ca.crt` from CloudNativePG into a read-only volume with mode `0440` and the existing pod group. Stage ConfigMaps own explicit password-free context connection strings using Npgsql `Passfile` and `SSL Mode=VerifyFull`. Match the generated pgpass host, port, database and username. Never mount the CA private key. Application seed/key and allowed-host Secrets remain externally supplied. Production retains external connection strings and CA inputs. Roll out applications after database credential or CA rotation and verify connections; mounted Secret updates do not prove pool refresh.

Bootstrap Linkerd Viz before Grafana. Grafana's Prometheus authorization policy requires the Viz namespace to exist.

The project uses Kubernetes 1.36.4 for testing. The pinned Linkerd charts require Kubernetes 1.31 or newer. This component minimum does not establish compatibility for the whole stack. Update CRDs, control plane and Viz together, verify image digests, and recheck proxy cryptography defaults and Grafana dashboard revisions on upgrades.

Dependency updates target compatible releases. Keep PostgreSQL on major 18 with standard-trixie images and MongoDB on the 8.2 line unless a database upgrade is explicitly requested. PostgreSQL major upgrades on existing clusters require a tested migration, verified backup and planned downtime before syncing. Do not directly apply the Trixie pin to an existing Bookworm cluster: CloudNativePG in-place major upgrades require the same OS distribution. Standard images require a backup plugin for Barman Cloud integration. Keep chart-managed helper versions aligned with their owning chart. The dated dependency review in docs/platform-operations.md records compatibility exceptions and verification limits.

Keep the Application health Lua identical in both Argo CD installation values files. Root waves require a finished successful full sync for the current source and resolved revision, a current Synced comparison, and healthy child resources. Reject queued/running/failed operations, dry runs, selective resource syncs, apply-strategy syncs that skip hooks and nonempty local-manifest overrides that can omit repository hooks. Normalize omitted optional ignoreDifferences fields before comparing spec and status. Keep the script compatible with Argo CD's default Lua sandbox. No-op source updates can require explicit full child syncs. The gate is installed through Argo CD Helm values, outside the DRN root.

Use explicit positive root sync waves and group resource templates under `deployment/templates/NNN-description/`. Zero-pad directory numbers to at least three digits and keep each sync-wave annotation numerically equal to its directory prefix. Projects and infrastructure use waves 10 through 110, Nexus uses 200, and Sample uses 300. Independent applications share a wave. Space new application tiers by 100 where possible and use gaps for intermediate dependencies. Keep `validate.yaml` at the templates root because it emits no resource. Verify directory and annotation alignment when moving or adding templates; missing annotations default to wave 0 and run before projects. See docs/platform-operations.md for the allocation convention. Child resource waves are local to each Application.

Sample and Nexus bases probe their local anonymous WeatherForecast routes for startup, readiness and liveness through an internal Http1 listener on port 8081. Keep service traffic on its existing h2c listeners. Stage/prod AllowedHosts Secrets must include each service's probe Host (`sample` or `nexus`). These probes check HTTP responsiveness, not database or downstream health. See docs/platform-operations.md for timings and Linkerd authorization constraints.

MongoDB requires native TLS with SCRAM and remains unmeshed. Its certificate uses the existing linkerd-trust-anchor issuer. The graylog-database-trust Bundle supplies PEM and PKCS12 trust to Graylog and Data Node without helper scripts. Preserve certificate verification and the operator-generated TLS URI. The pinned Graylog chart enables automatic self-signed startup for Data Node's API and OpenSearch TLS; manual CA provisioning is not required for this mode. Managed Data Node CA/renewal configuration is outside the current beta scope. MongoDB CA changes require client restarts and coordinated rotation with the shared issuer. See docs/platform-operations.md.

The pinned standard Linkerd proxy already prefers hybrid X25519MLKEM768 key exchange. Preserve its upstream defaults without redundant PQ flags. Certificate authentication remains classical. Native database/Graylog TLS has no verified configuration-only PQ enablement with current pins. Reassess runtime and peer compatibility on version changes; never infer PQ certificate support from hybrid TLS support. See README's post-quantum support section for pinned-source evidence and runtime verification limits.

## Conventions

Git: integration branch `develop`; release branch `master`. Topic PRs and Dependabot target develop. Release PRs promote develop to master. Preview releases come from develop; final releases come from master. Deployment roots always select immutable tags, never branch heads.

## Commands and verification

Default execution scope is static inspection and git diff --check. Do not restore, build, render charts/overlays, run apps, tests, benchmarks, clusters or ad-hoc helper scripts without explicit authorization. JSON parsing and read-only source/diff inspection are static checks. No staging, commit, tag, push or deployment without explicit user authorization.

CI: pull-request scans PRs into develop and master with Trivy/CodeQL. Both branch workflows scan pushes with Trivy/SonarCloud/CodeQL; master also runs weekly. Calendar-shaped preview tag pushes run Trivy in develop; non-preview calendar tag pushes run Trivy in master. Tag filters route scans but do not enforce branch ancestry. Verify the release commit belongs to its required branch before tagging. Dependabot targets develop. Preserve full SHA-pinned third-party actions and verify upstream provenance before version updates. There are no local application build/test commands or release-publishing jobs here.

## Documentation and release notes

Update owning deployment docs and this profile when shared deployment behavior changes. This repository has no package-specific release-note file. Record operational changes in deployment documentation; separate release notes are not required for configuration-only changes. CAD workflow artifacts belong in ignored `.agent/temp/` and remain local.
