# Platform operations

The environment root owns shared prerequisites and parallel branch Applications in [deployment/templates](../deployment/templates). The [logging](../deployment/logging), [monitoring](../deployment/monitoring) and [workloads](../deployment/workloads) charts own their leaf Applications. Change their configuration in a reviewed snapshot, publish a new immutable tag and promote that snapshot. Do not apply these Helm templates directly. Upstream chart versions and image digests are recorded in the owning template, independently of the GitOps tag.

Follow the [release procedure](../README.md#promote-and-roll-back). A new merge commit requires CI and staging validation before production promotion. Retain the tested image digests.

## Dependency review, 2026-10-17

The project uses Kubernetes `1.36.4` for testing. When changing component pins, check their Kubernetes support ranges and validate the selected combination on that baseline. A chart's minimum Kubernetes version alone does not establish compatibility for the complete deployment.

Keep PostgreSQL on major 18 and MongoDB on 8.2 unless planning a database upgrade. Align operator helpers with their chart. Review compatibility before upgrading bundled components across major versions.

| Component | Reviewed selection |
|---|---|
| Linkerd CRDs, control plane and Viz | Charts 2026.10.1, images edge-26.10.1, proxy v2.370.0 |
| Traefik | Chart 42.0.0, image v3.7.14 |
| Grafana | Chart 14.0.1, image 13.2.4-distroless and curl 8.23.0 |
| Argo CD bootstrap | Chart 10.10.5, Argo CD v3.5.5, chart-selected Dex and Redis dependencies |
| Gateway API | Retain v1.5.1 standard CRDs recommended by the pinned Linkerd release |
| trust-manager | Chart and image v0.25.0, chart-selected Trixie trust package pinned by digest |
| Graylog and Data Node | Chart 2.1.0, images 7.2.0 |
| Sample and Nexus | Updated 2026-10-14 to 0.10.1-preview004, image source revision matches the release tag |
| PostgreSQL | 18.6 standard-trixie, refreshed registry digest on 2026-10-17 |
| MongoDB credential bootstrap | Updated 2026-10-18 to .NET SDK 10.0.402-noble (Ubuntu 24.04), pinned by digest |
| Staging application Secret bootstrap | Updated 2026-10-18 to .NET SDK 10.0.402-noble (Ubuntu 24.04), pinned by digest |
| cert-manager | v1.21.2 |
| CloudNativePG operator | Chart 0.29.1, image 1.30.1 |
| MongoDB operator and server | Chart 1.14.0 with agent 109.0.1.9310-1, server 8.2.12-ubi8 |
| Sealed Secrets | Chart 2.20.0, controller 0.40.0 |
| Viz Prometheus and extension-init | Chart-selected 2.55.1 and 0.1.12. Prometheus 3 requires a separate compatibility review. |

Chart archives were checked against publisher index checksums and changed image pins against registry manifests. Argo CD chart archive SHA-256 is `ba6447eeed42bc41d1f0527721047b236304e97669a7b6286d02be4a1644ba7a`. Its image digest is `sha256:13db7b10db23283b1b36f855046602e18539a8a8204c9f6f039e8896c8897518`. Chart-selected Dex and Redis dependencies remain aligned with the chart. Runtime compatibility remains unverified.

The [Traefik 42 upgrade notes](https://github.com/traefik/traefik-helm-chart/releases/tag/v42.0.0) require Kubernetes 1.30 or newer and change unused Hub settings. The repository baseline satisfies that minimum. Grafana 14 adds a values schema. Current values were inspected against that schema without rendering. MongoDB's upgrade and readiness helpers retain the new chart's default versions, with refreshed registry digests for their republished tags. Bootstrap Utils and test packages now use 0.10.1-preview004. CI action commits, package hashes and SonarScanner 12.0.0 compatibility are recorded in the [test documentation](../tests/ApplicationSecretBootstrap.Tests/README.md).

All 18 Grafana dashboard revisions were checked. Retain Deployment dashboard 15475 at revision 6 from the pinned Linkerd suite. Revision 7 changes datasource strings to UID objects and a datasource variable, so it needs a separate provisioning change before use with the current string substitution. The other 17 revisions remain current. Staging already selects the latest published GitOps preview tag. Production remains disabled with its final-tag placeholder because no final release has been published.

Gateway API v1.6.3 is published and its standard bundle checksum was verified. Retain v1.5.1 because the pinned Linkerd release still recommends it. Linkerd's API-version checks alone do not establish tested support for the newer bundle. Coordinate this update with Linkerd compatibility verification.

No chart render, restore, build, test or deployment was run during this dependency update. Verify the changed combination in staging before promotion. Separate release notes are not required for this configuration repository. Operational changes and migration constraints are recorded here.

## Resource organization

Environment values and overlay directories use the full names `staging` and `production`. When adopting this rename, update the root Application's environment parameter and target revision together to a tag containing these paths. Older tags expect the previous abbreviated values. For rollback, restore the matching root manifest from the selected tag. Namespaces and Kubernetes resource names remain unchanged.

Keep standalone Kubernetes resources in separate files within their owning component directory. Use descriptive filenames, adding the resource kind when needed to distinguish related resources. Preserve existing Application source paths, resource identities and sync waves when splitting files. Keep chart-owned `extraObjects` in their Helm values to preserve chart rendering and ownership.

## Installation dependencies

Argo CD root sync waves order projects, shared CRDs, certificate bootstrap, Linkerd and namespace isolation. The final wave starts logging, monitoring, workloads, Traefik and Sealed Secrets concurrently. Each branch has its own dependency waves. Logging or Traefik failures allow workloads and monitoring to progress, but block root completion. The Argo CD values files supply child Application health assessment at both levels. On updates, waves do not make independently auto-syncing children transactional.

Both Argo CD installation values files use the same completion gate. A child reports Healthy to the root only when it has:

- No queued operation and a successful finished sync.
- A current source/destination comparison with matching source configuration and resolved revisions.
- Healthy resources.

Failed operations report Degraded. Pending operations, stale comparisons, dry runs, manual selective syncs, apply-strategy syncs and nonempty local-manifest overrides block later waves.

A successful automatic self-heal may replace the latest full-sync result with a selective operation. The gate accepts it only with a positive `autoHealAttemptsCount` and an automated deployment history entry matching all current sources and resolved revisions. Argo CD `v3.5.5` [records history only after successful unfiltered, non-dry-run syncs](https://github.com/argoproj/argo-cd/blob/v3.5.5/controller/sync.go#L408). Its [automatic operations](https://github.com/argoproj/argo-cd/blob/v3.5.5/controller/appcontroller.go#L2465) use repository manifests and the hook strategy. This retains evidence that deployment hooks, including MongoDB credential bootstrap, completed before later self-healing. `ApplyOutOfSyncOnly` [retains hook tasks](https://github.com/argoproj/argo-cd/blob/v3.5.5/gitops-engine/pkg/sync/sync_context.go#L761) in this version. These source files are unchanged from v3.5.4.

Manual deployment history omits the strategy and local-manifest inputs, so it cannot prove hooks ran. If matching automated history is absent or evicted, run a full child sync from the configured source. A successful full sync passes directly, but later self-healing still requires retained automated history. Recheck these upstream semantics when upgrading Argo CD. History records deployment completion, not continued availability of a hook's external effects.

The gate normalizes omitted optional `ignoreDifferences` fields so that an empty core API group in the spec matches its omitted representation in comparison status. Actual rule changes still block the gate until compared. Error conditions use explicit Argo CD condition names without requiring Lua standard libraries. Recheck these names against the pinned Argo CD API when upgrading.

If a source change renders identical resources, Argo CD may leave the child Synced without starting another operation. Run a full child sync at the selected revision, then retry the root if its operation has stopped. Selective sync cannot satisfy this gate. Failed operations also require a successful retry, even when resources appear healthy.

Gate changes require updating the Argo CD installation with the chosen values file. Syncing the DRN root does not update Argo CD itself.

To install the updated gate on an existing standard installation, use the intended cluster context and preserve any additional installation-specific values:

```sh
helm upgrade argocd argo/argo-cd --version 10.10.5 \
  -n argocd -f infrastructure/argocd/custom-values.yaml \
  --wait --timeout 10m
argocd app get drn-project --hard-refresh
```

For HA, use `custom-values-ha.yaml` instead. An active root sync can continue once child health is reassessed. Retry `argocd app sync drn-project` only if its previous operation has stopped.

Within `drn-monitoring`, Linkerd Viz syncs before Grafana to create the `linkerd-viz` namespace for Grafana's Prometheus authorization policy.

Group each chart's resource templates in descriptive directories without numeric prefixes. Set order through `argocd.argoproj.io/sync-wave` annotations, starting at `10` within each chart and increasing by `10` for dependent groups. Independent Applications share a wave. Missing annotations default to `0`. Keep `validate.yaml` identical at each chart's templates root. All four charts validate the environment, exact Git SHA, release tag and production opt-in.

The root prepares these groups in order:

1. AppProjects.
2. cert-manager controller and Gateway API CRDs.
3. trust-manager.
4. Certificate bootstrap.
5. Linkerd CRDs.
6. Linkerd certificates and trust.
7. Linkerd control plane.
8. Namespace isolation.
9. Logging, monitoring, workloads, Traefik and Sealed Secrets in parallel.

Branch waves are independent of the root and each other:

| Branch | Dependency order |
|---|---|
| `drn-logging` | MongoDB operator → MongoDB, TLS trust and credential bootstrap → Graylog and Data Node |
| `drn-monitoring` | Linkerd Viz → Grafana |
| `drn-workloads` | CloudNativePG → staging PostgreSQL and staging application Secret bootstrap → Nexus → Sample |

PostgreSQL and application Secret creation run independently. Nexus waits for both. Sample follows Nexus because it calls Nexus. Traefik runs alongside the branches, so ingress failures do not block workload deployment. Public access requires Traefik and Sample's route to be ready. Gateway API CRDs are shared by Linkerd and Traefik. MongoDB's credential hook must finish before Graylog starts. Verify public access and logging separately from workload readiness.

Each branch uses the administrator-only bootstrap project and receives the root's Git SHA, environment, release tag and production opt-in. The SHA pins both the branch and its Git-backed leaves. Branch automatic pruning and self-healing are disabled. [Automatic sync requires an OutOfSync comparison](https://argo-cd.readthedocs.io/en/stable/user-guide/auto_sync/#automated-sync-semantics). If a new revision leaves branch manifests unchanged, the health gate can still require a full sync. For example, run `argocd app sync drn-monitoring` when its health message requests one. Retry failed branches after recovery.

For the first migration from the flat root, sync the new root snapshot **without pruning**. Branches adopt existing leaf Applications through Argo CD resource tracking, preserving their names, destinations, source settings and finalizers. Before enabling pruning, verify that each moved leaf belongs to its new branch, is absent from the root's pruning candidates and has no shared-resource warning. If `drn-workloads` already owns Traefik, transfer it back to the root without pruning and verify its tracking. Do not delete or recreate leaf Applications, databases, PVCs or Secrets during transfer. Rollback requires the same transfer without pruning back to the old root. Restrict root and branch chart sources to administrators.

Staging's `application-secrets` Job completes before Nexus and Sample. Its dedicated project keeps Secret and initialization-marker creation RBAC outside the workload project. A fresh install creates the immutable `application-secret-bootstrap-initialized` ConfigMap before generating credentials automatically. Retain this non-secret marker independently of Jobs and source ConfigMaps. Full syncs preserve existing Secrets and create only missing ones, copying Nexus's seed and key with application-specific hosts. If Nexus is missing, a surviving application supplies its credentials. Once the marker exists, total settings Secret loss requires operator recovery. Enroll existing installations while their credentials survive. Marker or namespace deletion is outside this protection boundary. Lost legacy custom values also require operator recovery. See [automatic staging Secret bootstrap](staging-configuration.md#automatic-staging-secret-bootstrap). Production does not install this component.

Both credential bootstrap Jobs run a single C# file from a Kustomize-generated ConfigMap. Their init and bootstrap containers use the official `mcr.microsoft.com/dotnet/sdk:10.0.402-noble` image based on Ubuntu 24.04 LTS. Its index digest is `sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa`. On 2026-10-18, the MCR manifest response and its calculated SHA-256 matched this pin. The index includes Linux amd64, arm/v7 and arm64. This replaces Alpine while retaining the SDK version. The [SDK supports file-based apps](https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps), so no custom image is needed. Verify restore and execution on the new base under the existing security settings and resource limits before promotion.

Both Jobs use the SDK image's built-in `app` user with UID/GID `1654` and pod filesystem group `1654`. This identity applies to the init and bootstrap containers. Both Jobs set `XDG_RUNTIME_DIR=/tmp/runtime` on their private scratch volumes. An init container using the same SDK pin creates it with mode `0700` as UID `1654`. Since .NET 10.0.13, Unix named mutex storage uses the runtime directory or the user's home. `DOTNET_CLI_HOME` does not redirect it, so SDK initialization can fail with `NuGet-Migrations` before bootstrap code starts. See the [runtime change](https://github.com/dotnet/docs/issues/56396) and [SDK reproduction](https://github.com/dotnet/runtime/issues/135726). Preserve the non-root identity, read-only root filesystem and storage limits.

Both Jobs also set `XDG_DATA_HOME=/tmp/data`. The same init container creates this directory with mode `0700` as UID `1654` on the scratch volume. This supplies an existing writable `LocalApplicationData` directory for file-based SDK execution. The built-in user does not make its home writable on a read-only root filesystem, so retain these scratch directories. `DOTNET_CLI_HOME` and `XDG_RUNTIME_DIR` do not configure this lookup. A missing application-data directory can cause `Unable to determine a temporary directory path` before bootstrap code starts. See the [upstream folder-resolution report](https://github.com/dotnet/runtime/issues/27582). After deploying the change, verify a fresh bootstrap Job completes under the existing security settings. Static checks do not establish runtime success.

Both files reference DRN.Framework.Utils 0.10.1-preview004 for its transitive SharedKernel JSON conventions. Each clones the default options with null omission, avoiding JSON model attributes. Kubernetes HTTP still uses built-in .NET. Startup restore requires nuget.org access and downloads Utils with its transitive dependencies into writable `/tmp` storage under a non-root user. The source and root filesystem stay read-only. Existing manifests retain disabled runtime NuGet audit, telemetry and workload update notifications. CI audits the test dependency graph, including Utils. The application Secret Job retains its 300-second deadline. Static review does not establish startup timing or resource sufficiency after adding package restore. Verify these in staging before promotion. Automatic pruning remains disabled, so obsolete source ConfigMaps need deliberate cleanup.

Both bootstrap files declare `#:property PublishAot=false` to support their reflection-based JSON serialization. Direct IDE and file-based builds therefore use the same managed execution mode as the Job and CI commands. Native AOT and trimming are not supported.

Linkerd uses the edge chart repository and externally managed certificates. cert-manager issues the root, identity issuer and webhook certificates. trust-manager distributes public trust from `cert-manager`. Keep private keys out of public bundles. Renewal retains the root key. Follow the [planned coordinated rotation procedure](../todo.md#linkerd-root-key-rotation) before changing it. Match the Linkerd CLI to the deployed control plane.

Gateway API CRDs precede Traefik. Traefik watches `drn-project` and creates its Gateway there. Sample's HTTPRoute attaches in the same namespace. Confirm `Accepted=True` and `ResolvedRefs=True` before evaluating application behavior.

Traefik accepts public HTTP/1.1. Sample and Nexus use h2c on ports 5998 and 5988, exposed through Service port 80 with `appProtocol: kubernetes.io/h2c`. These listeners do not accept HTTP/1.1. Port 8081 serves HTTP/1 kubelet probes. Linkerd supplies mesh mTLS.

The [Sample HTTPRoute](../services/sample/base/httproute.yaml) maps `/app` and `/app/` to `/`, and `/app/example` to `/example`. Prefix matching is case-sensitive and excludes `/appx`. The route sets `X-Forwarded-Prefix: /app` and removes client-supplied `Forwarded`, `X-Forwarded-Ssl`, `X-Original-URI`, `X-Forwarded-Path` and `l5d-dst-override` headers. Configure application proxy trust for Traefik's forwarded request metadata.

The route also removes Linkerd retry and timeout override headers before forwarding requests to workloads. Traefik's inbound Linkerd proxy receives public requests before these filters run, so keep per-request retry and timeout policies disabled on that unfiltered ingress path. See [Linkerd's per-request policy guidance](https://linkerd.io/docs/features/retries-and-timeouts/#per-request-policies).

Use a CNI that enforces the [documented ingress policies](../README.md#service-protection). Fresh Traefik pods start with a deny default before serving traffic. When changing policies later, deploy restrictive mesh authorization before widening transport access. Do not assume manifest acceptance proves isolation, probe health or webhook reachability.

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

Both workload bases define startup, readiness and liveness probes. All environment overlays inherit them. Each probe requests the local anonymous GET inherited from DRN's `WeatherForecastControllerBase`:

| Workload | Pod path | Host header | Named port |
|---|---|---|---|
| Sample | `/Api/Sample/WeatherForecast` | `sample` | `health` (`8081`) |
| Nexus | `/Api/WeatherForecast` | `nexus` | `health` (`8081`) |

Probe requests go directly to the Pod IP. Sample's external `/app` prefix is not used. The Sample `/Api/Sample/WeatherForecast/Nexus` action calls Nexus and is deliberately excluded from probes. These local checks verify that the HTTP application pipeline responds. They do not query databases or establish dependency readiness.

Kestrel's additional `Health` endpoint uses `Http1` because standard kubelet HTTP probes use HTTP/1. Native h2c probes require the alpha `H2CContainerProbe` feature gate. The existing h2c service listeners remain unchanged. Port 8081 is not published by a Service or Gateway, and no ingress allowance is added. This Kestrel listener serves the same application routes, not only WeatherForecast, and retains application authentication, host filtering, mesh policy and namespace isolation.

Startup permits 60 failures at 5-second intervals, a nominal 300-second budget. Readiness checks every 5 seconds and marks the container unready after 3 consecutive failures. Liveness checks every 10 seconds and restarts it after 6 consecutive failures. Requests time out after 2 seconds. Readiness and liveness probes start after startup succeeds. Evaluate these defaults against observed startup and migration durations.

Before rolling out staging or production, include `sample` in Sample's `AllowedHosts` Secret value and `nexus` in Nexus's value, alongside the existing accepted hosts. Otherwise host filtering returns HTTP 400 and the probes fail. Roll out pods after changing Secrets. Linkerd automatically authorizes declared HTTP probe routes while the port uses its default inbound policy. If an explicit Server or route policy is later introduced for this port, preserve probe authorization deliberately.

The routes and anonymous action were checked against sibling DRN-Project tag `release/v0.10.1-preview003`. Confirm actual probes succeed with the pinned images and Linkerd before declaring the rollout healthy. See [Kubernetes probe behavior](https://kubernetes.io/docs/concepts/workloads/pods/probes/) and [Linkerd probe authorization](https://linkerd.io/docs/features/server-policy/).

## Application persistent storage

Sample and Nexus mount separate `1Gi` PersistentVolumeClaims (PVCs) at `/appdata`: `sample-data` and `nexus-data` in `drn-project`. Staging and production inherit these claims from the service bases. Each cluster provisions its own volumes. PostgreSQL retains its separate `8Gi` volume.

Provide a default StorageClass supporting ReadWriteOnce filesystem volumes writable by pod group `1654`. To use another class, set `storageClassName` on each claim before provisioning. Keep PVCs and Deployments in the same child sync wave so `WaitForFirstConsumer` binding can proceed. The workload AppProject permits PVCs.

Both applications use one replica and `Recreate`, stopping the old pod before starting its replacement. Updates interrupt service until the new pod is ready. `rollingUpdate: null` clears the previous strategy's settings. Review storage access and application concurrency before increasing replicas. `/apptemp`, `/applogs` and `/tmp` remain ephemeral, with a `1Gi` ephemeral-storage request and limit per application container.

New volumes start empty. Before syncing, preserve required `/appdata` files from existing `emptyDir` volumes and plan their restoration. Reverting to a pre-PVC deployment stops mounting the retained data. Verify claim binding, non-root write access and persistence across pod replacement. Runtime verification is pending.

`Prune=false,Delete=false` retains claims when removed from manifests or when their Application is deleted. Skipped pruning can leave the Application OutOfSync. Retention does not replace backups or protect against direct PVC or namespace deletion. Check the StorageClass reclaim policy and back up data before retiring a claim. See [Argo CD sync options](https://argo-cd.readthedocs.io/en/stable/user-guide/sync-options/) and [Kubernetes persistent volumes](https://kubernetes.io/docs/concepts/storage/persistent-volumes/).

## PostgreSQL

The image is `ghcr.io/cloudnative-pg/postgresql:18.6-standard-trixie@sha256:c7747d8d1049fff9cf30d9cd83d14d609c957afb5ffbcd114145f393ea1850e1`. The registry manifest was verified on 2026-10-17. This refresh preserves PostgreSQL major 18 and the Trixie distribution. Runtime compatibility remains unverified.

Fresh clusters initialize PostgreSQL 18.6 on Debian Trixie. The former PostgreSQL 16 pin used Bookworm. Do not directly sync this pin to that existing cluster: CloudNativePG 1.30 in-place major upgrades require the same OS distribution. Plan and rehearse a migration to a new cluster, such as logical dump/restore, with a verified backup, application and extension compatibility checks, locale/collation validation and scheduled downtime. Take a new backup after success. Reverting an image tag after a successful major upgrade is not a database downgrade. See [CloudNativePG 1.30 major upgrades](https://cloudnative-pg.io/docs/1.30/postgres_upgrades/). No cluster upgrade was executed as part of this pin change.

The standard image includes extensions, locales and LLVM JIT, but omits the Barman Cloud binaries bundled in system images. Use a backup plugin when adding Barman Cloud backups. This cluster currently configures no backups. See the [publisher's image types](https://github.com/cloudnative-pg/postgres-containers#image-types).

Staging gets a CloudNativePG Cluster named `postgresql` in `drn-project`. It uses a single instance, an `8Gi` volume from the default StorageClass, database `drnDb`, owner `drn`, and primary service `postgresql-rw:5432`. Set a storage class explicitly if the cluster lacks a suitable default.

Staging mounts CloudNativePG's generated password file and public CA directly. ConfigMaps supply password-free Npgsql connection strings with `Passfile` and verified TLS. See [database credentials](staging-configuration.md#staging-database-credentials) for Secret projections and connection settings. Roll out affected pods after credential or CA rotation and verify new connections. Projected-file updates do not prove connection-pool refresh. Production database inputs are externally provisioned.

The database and operator are unmeshed. PostgreSQL rejects non-TLS network connections and disables superuser network access. Server-enforced TLS alone does not establish server certificate verification. Staging and production connections must explicitly verify the endpoint and trust its CA.

The single instance disables its PodDisruptionBudget, permits maintenance downtime and has no configured backups. PostgreSQL automatic pruning is disabled, and its Cluster has `Prune=false,Delete=false`. Retention is not a backup. Configure and verify recovery before relying on stored data. Production has no bundled database Cluster and requires separate provisioning and migration.

## Graylog

See [connection encryption and certificates](../README.md#connection-encryption-and-certificates) for transport protection, certificate lifetimes and renewal ownership.

Graylog Open and Data Node use the upstream Graylog chart. MongoDB Controllers for Kubernetes manages MongoDB Community through a separate Git-backed Application. Only Graylog server pods are meshed. MongoDB, its operator and Data Node are unmeshed. Data Node manages OpenSearch internally.

MongoDB requires native TLS and SCRAM authentication. cert-manager issues its certificate through `linkerd-trust-anchor`. trust-manager distributes the CA and public roots to `graylog` as PEM and a Java PKCS12 truststore. Graylog and Data Node mount the store and verify MongoDB's certificate. The store contains only public CA certificates, so its `changeit` password protects no private material.

The MongoDB `graylog` user has `readWrite` and `dbAdmin`, both scoped only to the `graylog` database in the [MongoDB resource](../infrastructure/graylog/mongodb/mongodb-custom-resource.yaml). Graylog needs `collMod` to configure the capped `cluster_events` collection during startup. `readWrite` alone does not grant this action. The database-scoped `dbAdmin` role supplies it. See [MongoDB collMod privileges](https://www.mongodb.com/docs/v8.0/reference/command/collmod/#access-control).

For existing installations, sync the MongoDB Application and wait for the operator to reconcile the user roles. Confirm Graylog starts without MongoDB error 13 (`Unauthorized`) on `collMod` and passes its startup probe. Preserve the existing credential Secrets and database volumes. A startup probe HTTP 502 can be a downstream symptom of Graylog exiting after this permission error. Check the application logs before changing Linkerd or probe settings.

For fresh installs, the operator's connection URI includes `ssl=true`. The bootstrap copies it into `graylog-credentials` for both clients. Existing Secrets are preserved. To migrate a plaintext deployment, update the saved URI and restart both clients. Restart Graylog and Data Node after CA changes so their JVMs reload trust. Coordinate root rotation with Linkerd because the issuer is shared.

The pinned chart defaults `graylog.config.selfSignedStartup` to `"true"`, enabling automatic startup with self-signed certificates. This deployment inherits that setting. Manual CA provisioning in the preflight UI is not required for this startup mode. Data Node uses its certificate configuration for both OpenSearch and its management API. Do not add a second certificate mechanism or infer API encryption from the standalone `http_enable_tls` default. Verify HTTPS on ports 8999 and 9200 after startup. Automatic self-signed startup is distinct from configuring a managed CA and renewal policy, which remain outside this beta setup. See the [pinned chart configuration](https://github.com/Graylog2/graylog-helm/blob/graylog-2.1.0/charts/graylog/templates/config/graylog.yaml), [encrypted startup test](https://github.com/Graylog2/graylog2-server/blob/7.2.0/full-backend-tests/src/test/java/org/graylog/searchbackend/datanode/DatanodeSelfsignedStartupIT.java), and [Data Node API implementation](https://github.com/Graylog2/graylog2-server/blob/7.2.0/data-node/src/main/java/org/graylog/datanode/initializers/JerseyService.java).

Graylog and Data Node now select 7.2.0 together. Before syncing an existing 7.1 installation, verify backups of MongoDB, Data Node data and credential Secrets, then schedule a coordinated upgrade with service interruption. Follow the [7.2 upgrade procedure](https://go2docs.graylog.org/current/upgrading_graylog/upgrade_to_graylog_7.2.htm). The [compatibility matrix](https://go2docs.graylog.org/current/downloading_and_installing_graylog/compatibility_matrix.htm) includes MongoDB 8.2. Version 7.2 enables stream-aware field types by default and reports container-aware resource metrics. Check search performance and monitoring after rollout. Existing GELF inputs and the chart's self-signed startup settings are retained. A tag rollback does not replace a data recovery plan.

Provide a default ReadWriteOnce StorageClass. Baseline volumes are MongoDB `4Gi` data plus `2Gi` logs, Graylog `8Gi`, and Data Node `8Gi`. Hosts need `vm.max_map_count >= 262144` and must meet MongoDB's platform requirements. Memory requests include Graylog `1Gi`, Data Node `3.5Gi` and MongoDB `1Gi`, plus agent/operator overhead. They are not usage limits. The stack is single-instance and has no configured backups.

MongoDB's `4Gi` data request applies to new volumes. Kubernetes cannot shrink an existing `8Gi` PVC. Before syncing an existing installation, verify a backup/restore migration to a new volume. Include credentials and capacity for data and database overhead. Do not delete or recreate the database or PVC to force reconciliation. See [PVC resizing restrictions](https://kubernetes.io/docs/concepts/storage/persistent-volumes/#recovering-from-failure-when-expanding-volumes).

Full-sync the MongoDB Application to run its bootstrap hook. The Job generates credentials, waits for the operator URI and creates Graylog's external Secret. Selective sync skips hooks. Later syncs preserve existing Secrets without rotating credentials or recovering deleted Secrets for existing data. Back up Secrets with the database. Readiness RBAC names `mongodb-0`. Extend it when adding members.

Before generating credentials, the Job atomically creates the immutable ConfigMap `graylog-credential-bootstrap-initialized` in `graylog` with `data.version: "1"`. Only the run receiving a validated successful creation may initialize automatically. The marker contains no credentials and has no Job owner or Argo CD tracking metadata. Retain it during hook and source ConfigMap cleanup. It records an initialization claim, not successful completion.

An existing marker with all credential Secrets missing blocks regeneration. Partial-loss safeguards remain: missing `graylog-credentials` with any database password, admin password or operator connection Secret surviving requires restoration. A surviving `graylog-credentials` without `mongo-graylog-password` also requires restoration. A valid pair can run without the separate admin or operator connection Secret. These different credentials cannot be recovered by copying one Secret to another.

Enroll existing installations by full-syncing MongoDB while `graylog-credentials` and `mongo-graylog-password` remain intact and valid. Enrollment preserves their values. Confirm the marker with `kubectl -n graylog get configmap graylog-credential-bootstrap-initialized`. An unmarked installation that already lost every checked Secret is indistinguishable from a fresh install. Restore its credentials before deploying this change.

Marker read failures, invalid markers, create conflicts and uncertain create responses never authorize generation. A crash after claiming but before completing credentials requires operator recovery, even on an originally fresh install. Restore the original credential Secrets from backup and retry the full sync. If initialization never saved a complete set and no backup exists, stop for an explicitly reviewed recovery or fresh-environment reset that accounts for database and Graylog data. This bootstrap has no generation override. Do not delete the marker to clear the error.

Protection covers Secret deletion while the namespace and marker survive. Deleting the marker or namespace removes that history. Back up the marker along with credentials and data. The marker is not a credential backup and does not restore operator-managed Secrets, TLS keys or database contents.

The Graylog Job runs [bootstrap.cs](../infrastructure/graylog/mongodb/bootstrap.cs) from a hashed source ConfigMap. Its mounted NuGet configuration enables nuget.org for startup restore. The source and root filesystem stay read-only. Downloaded packages and SDK output use the existing bounded 512 MiB `/tmp` volume. Limits remain one CPU and 512 MiB of memory. The Job keeps its 20-minute deadline and polls for the operator URI every five seconds for up to 15 minutes. Verify startup timing and these resource limits with the transitive dependencies in staging.

The C# bootstrap preserves existing Secrets and rejects partial credential sets. Passwords use cryptographic random bytes. The administrator password is stored separately and hashed with SHA-256 for Graylog. The operator's connection URI is copied unchanged. API calls verify the mounted CA bundle and server hostname. Each request reads the current service account token. Errors do not log credentials or response bodies. RBAC permits named Secret and marker reads plus namespace-scoped Secret and ConfigMap creation. Kubernetes cannot restrict create permission by resource name. The Job has no update or delete permission.

During the operator URI wait, HTTP 5xx responses and request timeouts retry after the existing five-second delay within the original 15-minute deadline. HTTP 401/403, malformed Secret values and failures outside that polling read remain fatal. Retries never regenerate passwords or restart the deadline.

[DRN.Framework.Testing tests](../tests/ApplicationSecretBootstrap.Tests/GraylogBootstrapTests.cs) cover credential lifecycle and polling through a fake API and clock. Develop and master CI build both standalone bootstrap files and run their tests with coverage and SonarCloud analysis. These tests do not establish live TLS behavior. Automatic pruning remains disabled. Remove old source ConfigMaps only after confirming no Job references them. Source ConfigMaps contain code and NuGet configuration, never generated credentials.

The logging branch and overall root completion can wait while Graylog setup needs human input. Workloads and monitoring continue independently. Forward its private UI:

```sh
kubectl -n graylog port-forward svc/drn-graylog 9000:9000
```

Open [Graylog](http://localhost:9000). Retrieve the generated password from `graylog-admin-password` privately. Complete Data Node provisioning using the setup credentials from pod logs if prompted, then log in as `admin`. Keep credentials out of Git and shared output. Update `graylog.config.network.externalUri` in the template if changing the UI address.

Graylog loads separate `DRN input` and `DRN streams` content packs at startup. The input creates **DRN GELF HTTP** on `0.0.0.0:12201`, accepts bulk messages, preserves `full_message`, and extracts scoped JSON properties as `scope_...`. Set index replicas to `0` for this single Data Node.

The DRN stream matches `scope_ScopedLog=true`, uses the default index set and removes matching messages from the Default Stream. Graylog creates content-pack streams paused. Start **DRN** after installation and confirm exactly one GELF input and one DRN stream. Verify an actual scoped message before treating logging as ready.

Both applications send LF-delimited, uncompressed GELF HTTP to `http://drn-graylog.graylog:12201/gelf`. Keep this internal URL as HTTP because Linkerd proxies provide mesh mTLS. Application authorization is still required for Graylog's API.

To change a content pack, increment its revision and publish a new GitOps snapshot. Sync the files, then restart Graylog. Each revision creates a separate installation. Preserve customizations and uninstall the prior installation before loading its replacement to avoid duplicate inputs or streams. Input and stream packs do not reference each other's entities.

Automatic pruning is disabled for Graylog and MongoDB. MongoDB also has deletion protection. Restart Graylog and Data Node after credential rotation. Plan and verify data recovery separately.

## Grafana and secrets

Grafana is an ephemeral, anonymous Viewer behind Linkerd Viz. It has no initial admin account, no login form, immutable dashboard definitions and a Prometheus datasource in `linkerd-viz`. Do not expose it publicly without reviewing access and persistence needs. Its service account is authorized for the Viz Prometheus admin Server.

Start the local Viz dashboard with the matching Linkerd CLI:

```sh
linkerd viz dashboard
```

Keep the command running and open `/grafana/` on the local dashboard URL. Dashboards are provisioned from pinned revisions at startup. Local changes are not persisted.

Sealed Secrets is installed separately from cert-manager. It provides a controller, not an application secret provisioning policy. The required staging and production application Secrets must be supplied through an administrator-controlled process. The restricted workload AppProject cannot create Secret or SealedSecret resources. Back up sealing keys before depending on sealed data for recovery.

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

Before enabling an environment, verify actual child revisions and health, route attachment, public HTTP behavior, h2c backend traffic, Sample-to-Nexus requests, database migrations/queries and received logs. Check Linkerd metrics/tap, Grafana through Viz, operator reconciliation and admission webhooks. Confirm both meshed and unmeshed callers from an unlisted namespace cannot access protected private services, except documented exceptions. Verify Traefik's other ports remain denied through mesh transport, with authorized health probes still working.

Confirm app service accounts cannot read Secrets or ConfigMaps through the Kubernetes API. Verify non-TLS database connections fail and staging and production clients authenticate the server certificate. Restore and credential-rotation exercises are required before relying on persistent beta data. These checks require an actual environment and are not established by static review.

For MongoDB, verify both Graylog and Data Node connect with certificate verification, plaintext connections fail, and an untrusted CA fails the TLS handshake. Confirm Data Node's management and OpenSearch endpoints use HTTPS after certificate provisioning. Exercise MongoDB certificate renewal before treating the deployment as operationally validated.
