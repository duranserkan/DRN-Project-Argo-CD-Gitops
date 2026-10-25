# Platform operations

Use the [README](../README.md#bootstrap-a-cluster) for installation and releases, [environment configuration](staging-configuration.md) for application settings, and [local access](local-access.md) for UIs and database tunnels.

[Dependencies](#installation-dependencies) | [Bootstrap runtime](#credential-bootstrap-runtime) | [Certificates](#certificates-and-trust) | [Gateway API](#gateway-api-crd-troubleshooting) | [Probes](#workload-health-probes) | [Storage](#application-persistent-storage) | [PostgreSQL](#postgresql) | [Graylog](#graylog) | [Grafana and Secrets](#grafana-and-secrets) | [Readiness](#readiness-checks)

[dependencies.json](../dependencies.json) is the authoritative input for mapped chart versions, explicit image pins and related deployment tooling. The [.NET updater](../tools/DependencyPins/README.md) writes those fields into committed templates and manifests, which remain the executable configuration. The required `charts` CI job checks generated pins and inventory coverage. Publish changes through the [release procedure](../README.md#promote-and-roll-back), retaining tested image digests. Never apply Helm templates directly.

### Updating dependency pins

1. Review the component's compatibility constraints and verify candidate chart archives and image digests against their publishers.
2. Update the component's `pins` in `dependencies.json`. Place shared versions or images in one pin and reference them with `{pinName}` in the expected lines. Use contextual `selectors` for repeated keys and `references` for installation or tooling fields.
3. Run `dotnet run --project tools/DependencyPins -- update` from the repository root when local execution is authorized. Review the generated diff and update provenance documentation and verification comments. Commit the inventory and its generated consumers together.
4. Let the required `charts` CI job validate the change. It rejects changed values, duplicate component/file ownership, unused pins and missing or added dependency locations in the supported manifest forms. Complete staging acceptance separately before promotion.

Editing the inventory alone does not change deployment behavior. The updater must refresh the mapped fields before promotion. Its initial entries preserve existing selections and point to existing provenance records; this tooling change does not claim a new registry verification. See [dependency-check coverage](../tests/ChartValidation.Tests/README.md#dependency-inventory) for scope and limitations.

## Dependency review

Last reviewed: 2026-10-25.

The test baseline is Kubernetes `1.36.4`. Pinned Linkerd charts require 1.31 or newer. Component minimums do not establish whole-stack compatibility. Validate changed combinations in staging.

Keep PostgreSQL on major 18 and MongoDB on 8.2 unless planning a database upgrade. Align operator helpers with their chart. Review compatibility before upgrading bundled components across major versions.

| Component | Reviewed selection |
|---|---|
| Linkerd CRDs, control plane and Viz | Charts 2026.10.1, images edge-26.10.1, proxy v2.370.0 |
| Traefik | Chart 42.1.3, image v3.7.16 |
| Grafana | Chart 14.0.1, image 13.2.4-distroless and curl 8.23.0 |
| Argo CD bootstrap | Chart 10.11.1, Argo CD v3.5.6, chart-selected Dex and Redis dependencies |
| Gateway API | Retain v1.5.1 standard CRDs recommended by the pinned Linkerd release |
| trust-manager | Chart and image v0.25.0, chart-selected Trixie trust package pinned by digest |
| Graylog and Data Node | Chart 2.1.0, images 7.2.1 |
| Sample and Nexus | Updated 2026-10-25 to 0.10.1-preview005, multi-platform image digests verified against Docker Hub manifests for linux/amd64 and linux/arm64 |
| PostgreSQL | 18.6 standard-trixie, refreshed registry digest on 2026-10-25 |
| MongoDB credential bootstrap | .NET SDK 10.0.402-noble (Ubuntu 24.04), refreshed digest on 2026-10-25 |
| Staging application Secret bootstrap | .NET SDK 10.0.402-noble (Ubuntu 24.04), refreshed digest on 2026-10-25 |
| cert-manager | v1.21.3, all five component images pinned by digest |
| CloudNativePG operator | Chart 0.29.1, image 1.30.1 |
| MongoDB operator and server | Chart 1.14.0 with agent 109.0.1.9310-1, server 8.2.12-ubi8 |
| Sealed Secrets | Chart 2.20.0, controller 0.40.0 pinned by digest. Newer release images are not published. |
| Viz Prometheus and extension-init | Chart-selected 2.55.1 and 0.1.12. Prometheus 3 requires a separate compatibility review. |

Changed chart archives were checked against publisher index checksums and image pins against registry manifests. Argo CD chart archive SHA-256 is `dbb92a8c7855d4c564b87ab70e70bc36f5b17fd2bd30fa041a31e80dbea99c38`. Both installation values files pin its image to `sha256:8e5cc5bb008df8a09dba439462e58c94c55b7735888d3ebde0c25fc05e4e5335`. Chart-selected Dex and Redis dependencies remain aligned with the chart. Runtime compatibility remains unverified.

The [Traefik 42.1.3 release](https://github.com/traefik/traefik-helm-chart/releases/tag/v42.1.3) supports the selected v3.7.16 image. Its Kubernetes 1.30 minimum remains satisfied. Grafana 14.0.1 and 13.2.4-distroless remain current. The curl 8.23.0 helper digest was refreshed. MongoDB's upgrade and readiness helpers retain their chart-selected versions and verified digests. Bootstrap Utils and test packages now use 0.10.1-preview005. CI action commits, package hashes and SonarScanner 12.0.0 compatibility are recorded in the [test documentation](../tests/ApplicationSecretBootstrap.Tests/README.md).

[cert-manager v1.21.3](https://github.com/cert-manager/cert-manager/releases/tag/v1.21.3) fixes certificate renewal and private-key ownership checks and updates its trust store and Go runtime. The chart archive SHA-256 is `9ec67938b2981d8b333af61b3fcf53e26b4f6f5deb7ccda3de4ea116bb373a17`. Controller, webhook, cainjector, ACME solver and startup API check digests were verified against Quay.

Sealed Secrets [v0.41.1](https://github.com/bitnami/sealed-secrets/releases/tag/v0.41.1) is released, but the documented `docker.io/bitnami/sealed-secrets-controller:0.41.1` image returned not found on 2026-10-25. Docker Hub lists 0.40.0 as the newest available image, and the Helm index still selects chart 2.20.0. Retain that available controller with its verified digest. Do not promote an unpublished image. Linkerd's Prometheus and extension-init remain aligned with its current chart, rather than independently crossing component major versions.

All 18 Grafana dashboard revisions were checked. Retain Deployment dashboard 15475 at revision 6 from the pinned Linkerd suite. Revision 7 changes datasource strings to UID objects and a datasource variable, so it needs a separate provisioning change before use with the current string substitution. The other 17 revisions remain current. Staging already selects the latest published GitOps preview tag. Production remains disabled with its final-tag placeholder because no final release has been published.

Gateway API v1.6.3 is published and its standard bundle checksum was verified. Retain v1.5.1 because the pinned Linkerd release still recommends it. Linkerd's API-version checks alone do not establish tested support for the newer bundle. Coordinate this update with Linkerd compatibility verification.

No chart render, restore, build, test or deployment was run during this dependency update. Verify the changed combination in staging before promotion. Separate release notes are not required for this configuration repository. Operational changes and migration constraints are recorded here.

## Resource organization

Environment values and overlay directories use the full names `staging` and `production`. When adopting this rename, update the root Application's environment parameter and target revision together to a tag containing these paths. Older tags expect the previous abbreviated values. For rollback, restore the matching root manifest from the selected tag. Namespaces and Kubernetes resource names remain unchanged.

Keep standalone Kubernetes resources in separate files within their owning component directory. Use descriptive filenames, adding the resource kind when needed to distinguish related resources. Preserve existing Application source paths, resource identities and sync waves when splitting files. Keep chart-owned `extraObjects` in their Helm values to preserve chart rendering and ownership.

## Installation dependencies

The root prepares shared prerequisites in this order:

1. AppProjects.
2. cert-manager controller and Gateway API CRDs.
3. trust-manager.
4. Certificate bootstrap.
5. Linkerd CRDs.
6. Linkerd certificates and trust.
7. Linkerd control plane.
8. Namespace isolation.
9. Logging, monitoring, workloads, Traefik and Sealed Secrets in parallel.

Each branch then orders its own dependencies:

| Branch | Dependency order |
|---|---|
| `drn-logging` | MongoDB operator → MongoDB, TLS trust and credential bootstrap → Graylog and Data Node |
| `drn-monitoring` | Linkerd Viz → Grafana |
| `drn-workloads` | CloudNativePG → staging PostgreSQL and staging application Secret bootstrap → Nexus → Sample |

In staging, Nexus waits for PostgreSQL and application Secrets. Sample follows Nexus because it calls it. Viz creates the namespace needed by Grafana's Prometheus authorization. MongoDB's credential hook must finish before Graylog starts.

Branches inherit the root's SHA, environment, release tag and production opt-in. They use the administrator-only bootstrap project, with automatic pruning disabled. Self-healing is disabled by default; the retained profile enables it for branches and Sample/Nexus so staged recovery can resume reconciliation. Traefik or logging failures do not stop other branches, but every child must succeed for root completion. On updates, children can auto-sync independently. Root waves are not transactions.

Use descriptive template directories without numeric prefixes. Set sync waves from `10`, increasing by `10` for dependent groups. Independent Applications share a wave. Missing annotations default to `0`. Keep `validate.yaml` identical in all four charts.

### Child completion gate

Both Argo CD values files use the same health gate:

| Child state | Result |
|---|---|
| Successful finished sync, current Synced source/destination comparison, matching revisions and healthy resources | Healthy |
| Failed operation | Degraded |
| Queued/running operation, stale comparison, dry run, manual selective sync, apply strategy or nonempty local manifests | Blocks later waves |
| Automatic selective self-heal | Requires positive `autoHealAttemptsCount` and matching automated deployment history for every source and revision |

In Argo CD `v3.5.6`, [history records successful unfiltered syncs](https://github.com/argoproj/argo-cd/blob/v3.5.6/controller/sync.go#L408). [Automatic operations](https://github.com/argoproj/argo-cd/blob/v3.5.6/controller/appcontroller.go#L2465) use repository manifests and hooks. [`ApplyOutOfSyncOnly` retains hooks](https://github.com/argoproj/argo-cd/blob/v3.5.6/gitops-engine/pkg/sync/sync_context.go#L761). The upstream v3.5.5 to v3.5.6 comparison changes none of these three source files. Manual history cannot prove hooks ran because it omits strategy and local-manifest inputs. History proves completion, not continued availability of a hook's effects.

Run a full child sync when automated history is missing, a previous operation failed, or a new revision renders unchanged resources. For example: `argocd app sync drn-monitoring`. A successful full sync passes directly. Later self-heals still need matching automated history. Retry the root only after its previous operation has stopped.

The gate normalizes omitted optional `ignoreDifferences` fields. Real rule changes require a new comparison. Recheck history semantics, error-condition names and Lua sandbox compatibility when upgrading Argo CD.

Gate changes belong to the Argo CD installation. In the intended context, preserve installation-specific values and run:

```sh
helm upgrade argocd argo/argo-cd --version 10.11.1 \
  -n argocd -f infrastructure/argocd/custom-values.yaml \
  --wait --timeout 10m
argocd app get drn-project --hard-refresh
```

Use `custom-values-ha.yaml` for HA. Syncing the DRN root does not update Argo CD itself.

### Ownership migration

When moving from a flat root to branch charts:

1. Sync **without pruning** so branches adopt existing leaf Applications. Preserve names, destinations, source settings and finalizers.
2. Verify each leaf belongs to its new branch, has no shared-resource warning and is absent from the root's pruning candidates.
3. If `drn-workloads` owns Traefik, transfer it to the root without pruning and verify tracking.
4. Use the same process in reverse for rollback. Never delete Applications, databases, PVCs or Secrets to transfer ownership.

### Credential bootstrap runtime

The [application](../infrastructure/application-secrets/staging/credential-bootstrap.yaml) and [Graylog](../infrastructure/graylog/mongodb/credential-bootstrap.yaml) Jobs compile C# files from hashed ConfigMaps at startup. See their separate [application recovery](staging-configuration.md#automatic-staging-secret-bootstrap) and [Graylog recovery](#graylog-credentials-and-recovery) rules.

| Setting | Both Jobs |
|---|---|
| Image | Official .NET SDK `10.0.402-noble`, same digest for init and bootstrap containers |
| Identity | Built-in `app` user, UID/GID and `fsGroup` `1654` |
| Filesystems | Read-only source and root, bounded `512Mi` scratch volume at `/tmp` |
| Bootstrap limits | One CPU, `512Mi` memory |
| Restore | nuget.org access for DRN.Framework.Utils and transitive dependencies |
| Execution | `PublishAot=false`, reflection-based JSON, no native AOT or trimming |
| Audit | Runtime NuGet audit disabled, [CI dependency audit](../tests/ApplicationSecretBootstrap.Tests/README.md) enabled |
| API access | Mounted CA and hostname verification, per-request token reads, sanitized errors |

The MCR index digest `sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2` was verified on 2026-10-25. It includes Linux amd64, arm/v7 and arm64. This refresh retains the Noble distribution and SDK version.

The init container creates both directories below with mode `0700` before SDK startup. The image's home is read-only. `DOTNET_CLI_HOME` does not configure either location.

| Variable | Path | Failure prevented |
|---|---|---|
| `XDG_RUNTIME_DIR` | `/tmp/runtime` | `NuGet-Migrations` named-mutex failure since .NET 10.0.13 |
| `XDG_DATA_HOME` | `/tmp/data` | `Unable to determine a temporary directory path` during file-based execution |

See the [runtime change](https://github.com/dotnet/docs/issues/56396), [SDK reproduction](https://github.com/dotnet/runtime/issues/135726) and [folder-resolution report](https://github.com/dotnet/runtime/issues/27582). Verify fresh Job completion, restore timing and resource limits in staging. The application Job has a 300-second deadline. Graylog has 20 minutes, including a 15-minute URI wait.

Source ConfigMaps contain code and NuGet configuration, never credentials. Wave `-1` creates source and RBAC before the hook. Pruning is disabled. Remove obsolete source ConfigMaps only after confirming no Job uses them. Retain initialization markers independently of source ConfigMaps and Jobs.

### Routing

Traefik watches `drn-project`, where its Gateway and Sample's HTTPRoute live. Confirm route conditions `Accepted=True` and `ResolvedRefs=True`.

| Path | Listener |
|---|---|
| Public → Traefik | HTTP/1.1 |
| Service port `80` → Sample / Nexus | h2c only, ports `5998` / `5988`, `appProtocol: kubernetes.io/h2c` |
| Kubelet → Sample / Nexus | HTTP/1 on `8081` |

The [Sample route](../services/sample/base/httproute.yaml) strips `/app`: `/app` and `/app/` become `/`, while `/app/example` becomes `/example`. Matching is case-sensitive and excludes `/appx`.

It sets `X-Forwarded-Prefix: /app` and removes client-supplied forwarding, Linkerd destination, retry and timeout overrides. See the route for exact headers. Configure [application proxy trust](https://github.com/duranserkan/DRN-Project/blob/master/DRN.Framework.Hosting/README.md#forwarded-headers-reverse-proxy--gateway) for Traefik's metadata.

Traefik's inbound proxy sees public requests before route filters. Keep [per-request retries and timeouts](https://linkerd.io/docs/features/retries-and-timeouts/#per-request-policies) disabled on that path. Apply restrictive mesh authorization before widening network access. Verify the [ingress policy](../README.md#service-protection) with a NetworkPolicy-capable CNI.

## Certificates and trust

See the [connection map](../README.md#connection-encryption-and-certificates) for transport protection. Certificate owners handle renewal as follows:

| Certificate | Owner | Lifetime and renewal |
|---|---|---|
| Linkerd workload identities | Linkerd | Automatic rotation, default lifetime 24 hours |
| Linkerd issuer and admission webhooks | cert-manager | `24000h`, renewed `73h` before expiry |
| Shared Linkerd/MongoDB trust anchor | cert-manager | `175200h`, renewed `87600h` before expiry, existing private key retained |
| MongoDB server | cert-manager | 90 days, renewed 30 days before expiry with a new key |
| Staging PostgreSQL CA and server | CloudNativePG | Operator-managed renewal |
| Data Node API and OpenSearch | Data Node | Automatic self-signed startup, `99 × 365` days, no configured rotation policy |

trust-manager distributes public roots, including PEM and PKCS12 bundles for Graylog clients. It does not issue certificates. Keep private keys out of bundles. Replacing the shared root key requires [coordinated rotation](../todo.md#linkerd-root-key-rotation). Restart Graylog and Data Node after MongoDB CA changes.

Production database certificates and application CA copies are externally managed. After database credential or CA rotation, roll out affected applications and verify new connections. Data Node managed CA issuance and renewal remain outside beta scope.

References: [Linkerd mTLS](https://linkerd.io/docs/features/automatic-mtls/), [CloudNativePG certificates](https://cloudnative-pg.io/docs/devel/certificates/), [Graylog startup setting](https://github.com/Graylog2/graylog-helm/blob/graylog-2.1.0/charts/graylog/templates/config/graylog.yaml), [Data Node certificate lifetime](https://github.com/Graylog2/graylog2-server/blob/7.2.0/data-node/src/main/java/org/graylog/datanode/configuration/DatanodeKeystore.java).

## Gateway API CRD troubleshooting

The `gateway-api` Application installs the pinned standard CRDs with server-side apply. A CRD can be Synced in Argo CD while Kubernetes has not established its API. Inspect the failing HTTPRoute CRD and wait briefly for establishment:

```sh
kubectl describe crd httproutes.gateway.networking.k8s.io
kubectl get crd httproutes.gateway.networking.k8s.io -o jsonpath='{.status.conditions}'
kubectl wait --for=condition=Established crd/httproutes.gateway.networking.k8s.io --timeout=120s
```

If the wait fails, use the condition reasons/messages and events from `describe` to diagnose the failure. Inspect the Application's sync result as well:

```sh
argocd app get gateway-api --hard-refresh
```

Do not delete the CRD or bypass the health gate. Deleting a CRD deletes its custom resources. Diagnose the failure from Kubernetes conditions and the sync result before choosing a repair.

Once the CRD is established, refresh the Application and check its health. If no sync is running and the root still waits for a successful full child sync at the current revision, run:

```sh
argocd app sync gateway-api
argocd app wait gateway-api --sync --health --timeout 120
```

The root can then continue. If its operation has already failed or timed out, sync `drn-project` again. See [Kubernetes CRD establishment](https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definitions/) and [Gateway API CRD management](https://gateway-api.sigs.k8s.io/guides/crd-management/).

## Workload health probes

Both overlays inherit startup, readiness and liveness probes for local anonymous WeatherForecast endpoints:

| Workload | Pod path | Host header | Named port |
|---|---|---|---|
| Sample | `/Api/Sample/WeatherForecast` | `sample` | `health` (`8081`) |
| Nexus | `/Api/WeatherForecast` | `nexus` | `health` (`8081`) |

Requests go directly to the Pod IP, without Sample's external `/app` prefix. They check HTTP responsiveness, not databases or downstream services. Sample's Nexus-calling action is deliberately excluded.

| Probe | Interval | Consecutive failures | Effect |
|---|---|---|---|
| Startup | 5 seconds | 60 | Nominal 300-second startup budget |
| Readiness | 5 seconds | 3 | Remove from service traffic |
| Liveness | 10 seconds | 6 | Restart container |

All requests time out after two seconds. Readiness and liveness start after startup succeeds. Check these budgets against migration and startup times.

The HTTP/1 `Health` listener avoids requiring native h2c probe support. It serves the application's other routes too, with authentication and host filtering intact. Port `8081` has no Service, Gateway or added ingress allowance.

Include each probe Host in its [AllowedHosts Secret](staging-configuration.md#required-staging-and-production-secrets) before rollout, or probes return HTTP 400. Linkerd authorizes declared HTTP probes under its default inbound policy. Preserve this authorization when adding an explicit Server or route policy.

Routes were checked against DRN-Project `release/v0.10.1-preview003`. Verify them with the pinned images and Linkerd. References: [Kubernetes probes](https://kubernetes.io/docs/concepts/workloads/pods/probes/), [Linkerd probe authorization](https://linkerd.io/docs/features/server-policy/).

## Application persistent storage

| Storage | Configuration |
|---|---|
| Sample `/appdata` | `sample-data`, `1Gi` PVC |
| Nexus `/appdata` | `nexus-data`, `1Gi` PVC |
| Access | ReadWriteOnce filesystem, writable by pod group `1654` |
| Rollout | One replica, `Recreate`, `rollingUpdate: null` |
| Temporary files and logs | `/apptemp`, `/applogs`, `/tmp` in `emptyDir`, `1Gi` ephemeral-storage request and limit per container |

Each environment provisions its own volumes. Provide a default StorageClass or set `storageClassName` before provisioning. Keep PVCs and Deployments in the same child wave for `WaitForFirstConsumer` binding. Updates interrupt service. Review storage access and concurrency before adding replicas.

Before replacing `emptyDir` with a PVC, preserve required files and plan restoration. New volumes start empty. Reverting to a pre-PVC deployment stops mounting retained data. Verify binding, non-root writes and persistence across pod replacement.

`Prune=false,Delete=false` retains claims after manifest or Application removal, which can leave Argo CD OutOfSync. It does not protect against direct PVC or namespace deletion. Back up data and check the StorageClass reclaim policy before retirement. See [Argo CD retention](https://argo-cd.readthedocs.io/en/stable/user-guide/sync-options/) and [Kubernetes volumes](https://kubernetes.io/docs/concepts/storage/persistent-volumes/).

### Retained storage

Select `infrastructureProfile=retained`, a nonempty `retainedStorageClass` and `activationStage` on the staging root. The root passes these values and its resolved Git SHA to every branch. Empty values preserve existing deployment defaults. Production rejects this profile. The manifests contain no provider-specific storage drivers or volume identifiers.

The recovery coordinator advances three stages: `prepare` installs controllers, trust and isolation policies; `data` starts databases, Graylog and monitoring, validates credential Jobs and reserves Sample/Nexus storage with both Deployments at zero replicas; `workloads` starts Sample and Nexus. Logging and monitoring keep their `data` source parameters during workload activation to preserve sync-health evidence. These are activation stages, not shutdown commands: branches retain existing Applications when a stage omits them. The coordinator must stop reconciliation and application writers before backup, pause or restoration. Do not switch stages by hand on a running installation.

The root omits `drn-monitoring` during `prepare` because its chart has no resources in that stage. This avoids waiting for a completed sync operation that an already-Synced empty application will not start automatically. The branch is created during `data`.

The profile sets all seven persistent claims to the supplied class, including claims created by CloudNativePG, MongoDB and the Graylog chart. The infrastructure owner must provision a non-provisioning StorageClass and static volumes with `Retain` and exact claim reservations. Missing volumes must leave claims Pending instead of provisioning empty replacements. Helm validates the class name, not the live class or volumes. Existing claim names, requested sizes and Application ownership stay unchanged.

[Pulumi Ops](https://github.com/duranserkan/DRN-Project-Pulumi-Ops) implements this contract on OCI and supplies `retainedStorageClass=drn-retained`. Another infrastructure implementation must meet the same storage, identity and recovery requirements. Publish and select a matching GitOps release when updating the caller from the former `oci-retained` profile. The old profile name is rejected rather than silently selecting default behavior.

| Namespace | Claim | Data |
|---|---|---|
| `drn-project` | `sample-data` | Sample appdata |
| `drn-project` | `nexus-data` | Nexus appdata |
| `drn-project` | `postgresql-1` | PostgreSQL |
| `graylog` | `data-volume-mongodb-0` | MongoDB data |
| `graylog` | `logs-volume-mongodb-0` | MongoDB logs |
| `graylog` | `graylog-data-graylog-0` | Graylog journal and node state |
| `graylog` | `data-graylog-datanode-0` | Data Node indexes |

Graylog claim names follow the pinned [2.1.0 chart templates](https://github.com/Graylog2/graylog-helm/tree/graylog-2.1.0/charts/graylog/templates/workload/statefulsets). Grafana and Linkerd Viz remain ephemeral. The retained profile uses `ClusterIP` for Traefik. Use workstation port-forwarding until a separate ingress design is accepted. This profile does not create a cloud load balancer.

Both credential Jobs receive `CREDENTIALS_EXTERNALLY_MANAGED=true`. They validate stable Secrets and immutable version-1 initialization markers using reads only. Missing or invalid inputs fail the hook; the application generation override cannot bypass this mode. The Graylog Job waits for the MongoDB operator to derive `mongodb-graylog-graylog` under the new Cluster owner, then requires its URI to match the saved Graylog identity. Do not restore that derived Secret with a stale owner reference.

Before consumers start, the recovery coordinator must save and restore twelve stable Secrets: `nexus-settings`, `sample-settings`, `postgresql-app` and `postgresql-ca` in `drn-project`; `mongo-graylog-password`, `graylog-scram-scram-credentials`, `mongodb-agent-password`, `mongodb-keyfile`, `graylog-admin-password` and `graylog-credentials` in `graylog`; `cert-manager/linkerd-identity-trust-roots`; and `kube-system/sealed-secrets-key`. The two bootstrap initialization ConfigMaps are checkpointed with them. CNPG uses the supplied application Secret and CA, while renewing its server and replication certificates. The retained profile omits the self-generating root Certificate; renewable Linkerd and database leaf certificates use the saved shared root. Sealed Secrets automatic key renewal is disabled for this profile. Root rotation requires a coordinated new checkpoint and recovery validation.

Native backup inputs complement the offline volume set: the single PostgreSQL `drnDb` dump, the Graylog MongoDB database archive, and an OpenSearch snapshot under `/var/lib/graylog-datanode/drn-backups` on the retained Data Node volume. The pinned MongoDB ARM64 image includes Database Tools 100.18.0. Data Node explicitly uses the hot-node roles, and an unprivileged init container creates its snapshot directory. The recovery coordinator owns backup completeness, encrypted checkpoint storage, shared coordination, restoration of CNPG instance/PVC metadata and the ordered native restore. Follow [the recovery checklist](../todo.md#retained-data-recovery) for runtime acceptance. Never disable the profile to generate credentials for retained data.

The profile is for new claims or a reviewed restoration. Kubernetes does not permit changing a bound claim's StorageClass. Migrating an existing installation requires verified backups and a deliberate volume migration. Claim names and operator replica counts are part of the storage contract. Do not add replicas or rename claims without provisioning and reserving the corresponding durable volumes first.

## PostgreSQL

The image is `ghcr.io/cloudnative-pg/postgresql:18.6-standard-trixie@sha256:d72ace96f5803b12c341bb5dac48532e8c1f92f69e8a717a59d779bfc038dea9`. The registry manifest was verified on 2026-10-25. This refresh preserves PostgreSQL major 18 and the Trixie distribution. Runtime compatibility remains unverified.

Fresh clusters initialize PostgreSQL 18.6 on Debian Trixie. The former PostgreSQL 16 pin used Bookworm. Do not directly sync this pin to that existing cluster: CloudNativePG 1.30 in-place major upgrades require the same OS distribution. Plan and rehearse a migration to a new cluster, such as logical dump/restore, with a verified backup, application and extension compatibility checks, locale/collation validation and scheduled downtime. Take a new backup after success. Reverting an image tag after a successful major upgrade is not a database downgrade. See [CloudNativePG 1.30 major upgrades](https://cloudnative-pg.io/docs/1.30/postgres_upgrades/). No cluster upgrade was executed as part of this pin change.

The standard image includes extensions, locales and LLVM JIT, but omits the Barman Cloud binaries bundled in system images. Use a backup plugin when adding Barman Cloud backups. The default profile has no backup configuration; retained recovery requires externally coordinated native logical dumps and stopped volume backups. See the [publisher's image types](https://github.com/cloudnative-pg/postgres-containers#image-types).

Staging gets a CloudNativePG Cluster named `postgresql` in `drn-project`. It uses a single instance, an `8Gi` volume from the default StorageClass, database `drnDb`, owner `drn`, and primary service `postgresql-rw:5432`. Set a storage class explicitly if the cluster lacks a suitable default.

Staging mounts CloudNativePG's generated password file and public CA directly. ConfigMaps supply password-free Npgsql connection strings with `Passfile` and verified TLS. See [database credentials](staging-configuration.md#staging-database-credentials) for Secret projections and connection settings. Roll out affected pods after credential or CA rotation and verify new connections. Projected-file updates do not prove connection-pool refresh. Production database inputs are externally provisioned.

The database and operator are unmeshed. PostgreSQL rejects non-TLS network connections and disables superuser network access. Server-enforced TLS alone does not establish server certificate verification. Staging and production connections must explicitly verify the endpoint and trust its CA.

The single instance disables its PodDisruptionBudget and permits maintenance downtime. The default profile has no configured backups; the retained profile requires the external backup protocol described above. PostgreSQL automatic pruning is disabled, and its Cluster has `Prune=false,Delete=false`. Retention is not a backup. Verify recovery before relying on stored data. Production has no bundled database Cluster and requires separate provisioning and migration.

## Graylog

| Component | Deployment | Network |
|---|---|---|
| Graylog Open | Graylog Helm chart | Meshed |
| Data Node, including OpenSearch | Graylog Helm chart | Unmeshed |
| MongoDB Community | MongoDB operator and Git-backed Application | Unmeshed |

MongoDB uses native TLS and SCRAM. Graylog and Data Node verify its certificate through the shared PEM/PKCS12 trust bundle. The truststore contains public certificates only, so `changeit` protects no private material. See [certificates and trust](#certificates-and-trust).

The [MongoDB user](../infrastructure/graylog/mongodb/mongodb-custom-resource.yaml) needs `readWrite` and `dbAdmin` on `graylog` only. The latter permits [`collMod`](https://www.mongodb.com/docs/v8.0/reference/command/collmod/#access-control) on `cluster_events`. If startup fails with MongoDB error 13, sync MongoDB and wait for role reconciliation. Verify Graylog starts and passes its probe. Preserve credentials and volumes. Probe HTTP 502 can be a symptom of this failure.

Fresh installs copy the operator's TLS URI, including `ssl=true`, unchanged. For a plaintext-to-TLS migration, update the saved URI and restart both clients. Restart them after CA changes and coordinate shared-root rotation with Linkerd.

The chart inherits `graylog.config.selfSignedStartup: "true"`. Data Node automatically creates certificates for its API and OpenSearch, without manual CA provisioning. Verify HTTPS on ports `8999` and `9200`. Managed CA renewal is outside beta scope. See the [chart setting](https://github.com/Graylog2/graylog-helm/blob/graylog-2.1.0/charts/graylog/templates/config/graylog.yaml), [startup test](https://github.com/Graylog2/graylog2-server/blob/7.2.0/full-backend-tests/src/test/java/org/graylog/searchbackend/datanode/DatanodeSelfsignedStartupIT.java) and [API implementation](https://github.com/Graylog2/graylog2-server/blob/7.2.0/data-node/src/main/java/org/graylog/datanode/initializers/JerseyService.java).

### Capacity and upgrades

Graylog and Data Node now select 7.2.1 together. Before syncing an existing 7.1 installation, verify backups of MongoDB, Data Node data and credential Secrets, then schedule a coordinated upgrade with service interruption. Follow the [7.2 upgrade procedure](https://go2docs.graylog.org/current/upgrading_graylog/upgrade_to_graylog_7.2.htm). The [compatibility matrix](https://go2docs.graylog.org/current/downloading_and_installing_graylog/compatibility_matrix.htm) includes MongoDB 8.2. Version 7.2 enables stream-aware field types by default and reports container-aware resource metrics. Check search performance and monitoring after rollout. Existing GELF inputs and the chart's self-signed startup settings are retained. A tag rollback does not replace a data recovery plan.

| Component | Storage | Memory request |
|---|---|---|
| MongoDB | `4Gi` data + `2Gi` logs | `1Gi`, plus agent/operator overhead |
| Graylog | `8Gi` | `1Gi` |
| Data Node | `8Gi` | `3.5Gi` |

Provide a default ReadWriteOnce StorageClass and hosts with `vm.max_map_count >= 262144` that meet MongoDB's requirements. Memory requests are not usage limits. The stack is single-instance with no configured backups.

MongoDB's `4Gi` data request applies to new volumes. Kubernetes cannot shrink an existing `8Gi` PVC. Before syncing an existing installation, verify a backup/restore migration to a new volume. Include credentials and capacity for data and database overhead. Do not delete or recreate the database or PVC to force reconciliation. See [PVC resizing restrictions](https://kubernetes.io/docs/concepts/storage/persistent-volumes/#recovering-from-failure-when-expanding-volumes).

### Graylog credentials and recovery

Full-sync MongoDB to run its credential hook. Selective sync skips hooks. The Job creates random credentials, waits for the operator URI and creates `graylog-credentials`. It stores the admin password separately and supplies its SHA-256 hash to Graylog. Later syncs preserve credentials.

Before writing, the Job claims the immutable `graylog-credential-bootstrap-initialized` ConfigMap in `graylog`, containing only `data.version: "1"`. It has no Job owner or Argo CD tracking. Retain it during cleanup and back it up with credentials and data. It records a claim, not completion.

| State | Action |
|---|---|
| No marker or credentials | Only the confirmed marker creator may initialize. |
| Valid `graylog-credentials` and `mongo-graylog-password` | Preserve them and enroll if needed. Separate admin/operator connection Secrets may be absent. |
| Marker exists, all credentials lost | Restore from backup. No regeneration. |
| `graylog-credentials` missing, any database/admin/operator connection Secret survives | Restore the missing credentials. |
| `graylog-credentials` survives, database password missing | Restore `mongo-graylog-password`. |
| Invalid/unreadable marker, creation conflict or ambiguous response | Stop without generation. |

Enroll existing installations while the valid pair survives. Confirm with `kubectl -n graylog get configmap graylog-credential-bootstrap-initialized`. Restore damaged unmarked installations before enrollment. An empty unmarked environment looks like a fresh install.

An interrupted initialization also requires recovery. Restore original credentials and retry a full sync. If no complete set or backup exists, stop for a reviewed recovery or fresh-environment reset that accounts for stored data. There is **no generation override**. Never delete the marker to clear an error. Different credentials cannot be recovered by copying one Secret to another.

Protection requires the namespace and marker to survive. The marker does not restore credentials, TLS keys or data. See [shared runtime settings](#credential-bootstrap-runtime) and [bootstrap tests](../tests/ApplicationSecretBootstrap.Tests/README.md).

The Job can read named Secrets and the marker, and create Secrets/ConfigMaps in its namespace. It cannot update or delete them. Kubernetes cannot restrict create permission by name. Readiness RBAC names `mongodb-0`. Extend it when adding members.

Operator URI polling runs every five seconds for up to 15 minutes within the 20-minute Job deadline. HTTP 5xx and request timeouts retry without regenerating passwords or resetting the deadline. HTTP 401/403, malformed values and failures outside this polling read are fatal.

### Logging setup

Open Graylog through the [local access guide](local-access.md#graylog). Complete Data Node setup if prompted. This can delay logging and root completion while other branches continue.

| Content pack | Configuration |
|---|---|
| `DRN input` | **DRN GELF HTTP**, `0.0.0.0:12201`, bulk messages, preserved `full_message`, scoped properties as `scope_...` |
| `DRN streams` | Match `scope_ScopedLog=true`, use the default index set, remove matches from Default Stream |

Set index replicas to `0` for the single Data Node. Content-pack streams start paused. Start **DRN**, confirm exactly one input and stream, and verify a real scoped message.

Applications send LF-delimited, uncompressed GELF to `http://drn-graylog.graylog:12201/gelf`. Keep HTTP internally because Linkerd provides mTLS. Graylog API authorization still applies.

For content-pack changes, increment the revision and publish a new snapshot. Preserve customizations and uninstall the prior pack installation before loading its replacement. Sync files and restart Graylog. Each revision creates a separate installation, so leaving the old one creates duplicates. Input and stream packs are independent.

Graylog and MongoDB disable automatic pruning. MongoDB also has deletion protection. Restart Graylog and Data Node after credential rotation. Verify data recovery separately.

## Grafana and secrets

Grafana provides anonymous Viewer access through [Linkerd Viz](local-access.md#linkerd-viz-and-grafana). It has no initial admin account or login form. Pinned dashboards load at startup and local changes are not persisted. Its service account can access the Viz Prometheus admin Server. Review access and persistence before public exposure.

Sealed Secrets supplies a controller, independently of cert-manager. Application Secret ownership follows [environment configuration](staging-configuration.md#configuration-ownership). The workload AppProject cannot create Secrets or SealedSecrets. Back up sealing keys before relying on sealed data for recovery.

The chart names its controller `sealed-secrets` in `kube-system`, while `kubeseal` defaults to `sealed-secrets-controller`. Specify the controller when fetching its public certificate or sealing a Secret:

```sh
kubeseal --controller-name sealed-secrets --controller-namespace kube-system --fetch-cert
```

See the [Sealed Secrets controller naming guidance](https://github.com/bitnami/sealed-secrets#helm-chart).

## Readiness checks

Use the intended Kubernetes and Argo CD contexts. After the root sync and required setup are complete, inspect the Applications and check the mesh:

```sh
argocd app get drn-project
argocd app list
argocd app wait drn-project --sync --health --timeout 600
linkerd version
linkerd check
linkerd check --proxy
linkerd viz check
kubectl -n drn-project wait --for=condition=Programmed gateway/drn-project --timeout=120s
kubectl -n drn-project get httproute sample -o yaml
```

If the root waits, inspect the child named in its resource tree. Complete missing setup or retry a full child sync as described under [installation dependencies](#installation-dependencies). A CLI wait timeout does not cancel the controller's sync operation.

Before enabling an environment, verify these in the running cluster:

| Area | Required checks |
|---|---|
| Applications | Expected child revisions and health, route attachment, public HTTP, h2c backends, Sample-to-Nexus calls, migrations and queries |
| Observability | Received logs, Linkerd metrics/tap and Grafana through Viz |
| Controllers | Operator reconciliation and admission webhooks |
| Isolation | Deny unlisted meshed and unmeshed callers except documented allowances. Deny Traefik's private ports through mesh transport while probes remain healthy. |
| Workload permissions | Application service accounts cannot read Secrets or ConfigMaps through the API. |
| Database TLS | Reject plaintext and verify server certificates. For both Graylog clients, reject an untrusted MongoDB CA and exercise certificate renewal. |
| Data Node | HTTPS on management and OpenSearch endpoints |
| Recovery | Exercise restore and credential rotation before relying on persistent beta data. |

Static review does not establish these runtime results.
