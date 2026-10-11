# Platform operations

The environment root owns the Applications in [deployment/templates](../deployment/templates). Change their configuration in a reviewed snapshot, publish a new immutable tag and promote that snapshot. Do not apply these Helm templates directly. Upstream chart versions and image digests are recorded in the owning template, independently of the GitOps tag.

Integrate changes on `develop` and publish preview tags from that branch. Promote reviewed candidates through `develop → master` and publish final tags from `master`. Both branches run CI. A new merge commit requires its own CI and staging validation before production promotion, while retaining the tested image digests. Follow the [release procedure](../README.md#promote-and-roll-back). The `develop` branch does not create a development deployment environment.

## Dependency review, 2026-10-10

The project uses Kubernetes `1.36.4` for testing. When changing component pins, check their Kubernetes support ranges and validate the selected combination on that baseline. A chart's minimum Kubernetes version alone does not establish compatibility for the complete deployment.

Use the latest compatible releases within the selected database lines. PostgreSQL now targets 18 following the requested upgrade on 2026-10-11; MongoDB remains on 8.2. Keep operator helpers aligned with their chart and avoid independently upgrading bundled components across major versions.

| Component | Reviewed selection |
|---|---|
| Linkerd CRDs, control plane and Viz | Charts 2026.10.1, images edge-26.10.1, proxy v2.370.0 |
| Traefik | Chart 41.7.1, image v3.7.14 |
| Grafana | Chart 13.5.0, existing image 13.2.3-distroless and curl 8.22.0 remain current |
| Argo CD bootstrap | Chart 10.10.2, Argo CD v3.5.4, chart-selected Dex and Redis dependencies |
| trust-manager | Chart and image v0.25.0, chart-selected Trixie trust package pinned by digest |
| Graylog and Data Node | Chart 2.1.0 remains current, both images updated to 7.1.10 |
| Sample and Nexus | 0.10.1-preview003, image source revision matches the release tag |
| PostgreSQL | Updated 2026-10-11 to the current 18.6 standard-trixie image, pinned by digest |
| MongoDB credential bootstrap | Node 26.11.1-alpine3.24 |
| cert-manager | v1.21.2 remains current |
| CloudNativePG operator | Chart 0.29.1 and image 1.30.1 remain current |
| MongoDB operator and server | Chart 1.13.0 and its helper versions remain current; 8.2.12-ubi8 remains the latest published image in the selected server line |
| Sealed Secrets | Chart 2.20.0, controller 0.40.0 remain current |
| Viz Prometheus and extension-init | Retain chart-selected 2.55.1 and 0.1.12; Prometheus 3 is a separate compatibility upgrade |

Chart archives were checked against publisher index checksums. Changed explicit image pins were verified against registry manifests. Argo CD chart archive SHA-256 is `1f2bdb0e23b87fdac5222d99d1acf6964e670db9bf63912c58ab4677e505638b`. Its image digest is `sha256:49dff79439bb38b1b942b19a113fe1fec7e6b6c671ddf9fe6a4a7c46bde0b77b`. Argo CD's ECR dependency mirror was rate-limited during review; Redis and HAProxy versions were inspected through their upstream Docker Hub manifests instead. This is static verification, not a deployment or runtime compatibility test. Separate release notes are not required for this configuration update.

## Resource organization

Keep standalone Kubernetes resources in separate files within their owning component directory. Use descriptive filenames, adding the resource kind when needed to distinguish related resources. Preserve existing Application source paths, resource identities and sync waves when splitting files. Keep chart-owned `extraObjects` in their Helm values to preserve chart rendering and ownership.

## Installation dependencies

Argo CD root sync waves order projects, operators, certificate bootstrap, Linkerd, namespace isolation and application dependencies. The Argo CD values files supply child Application health assessment. On a fresh install, inspect a blocked child before retrying the root. On updates, waves do not make independently auto-syncing children transactional.

Both Argo CD installation values files use the same completion gate. A child must have no queued operation, a successful finished full sync, a current source/destination comparison, matching synced source configuration and resolved revisions, and healthy resources before it reports Healthy to the root. Failed operations report Degraded. Pending operations, stale comparisons, dry runs, selective resource syncs, apply-strategy syncs and nonempty local-manifest overrides cannot release later waves. Selective resource syncs and the apply strategy skip hooks. Local-manifest overrides can omit repository hooks while retaining the requested revision. Run a full sync from the configured source to satisfy the gate. This includes waiting for the MongoDB credential hook, which aggregate resource health alone does not assess.

The gate normalizes omitted optional `ignoreDifferences` fields so that an empty core API group in the spec matches its omitted representation in comparison status. Actual rule changes still block the gate until compared. Error conditions use explicit Argo CD condition names without requiring Lua standard libraries. Recheck these names against the pinned Argo CD API when upgrading.

If a new source revision or configuration renders identical resources, Argo CD may leave the child Synced without starting another operation. The gate deliberately waits. Run a full sync of that child at its selected revision, then retry the root if its operation has stopped. Do not use selective resource sync to bypass this requirement. A failed operation also needs a successful retry even when its resources appear healthy. Changes to this gate require updating the Argo CD installation with the chosen values file; syncing the DRN root does not update Argo CD itself.

Linkerd Viz syncs at wave `90`, before Grafana at wave `100`. This creates the `linkerd-viz` namespace before Grafana applies its Prometheus authorization policy there.

Group root resource templates in `deployment/templates/NNN-description/` using positive waves. Directory prefixes are zero-padded for lexical sorting. Each resource's explicit `argocd.argoproj.io/sync-wave` annotation must equal its directory prefix numerically, for example `020-operators` uses `"20"`. Argo CD reads the annotation, not the path. Helm renders the nested templates together. Keep `validate.yaml` at the templates root because it validates values without emitting a resource.

| Directory | Root wave | Resources |
|---|---|---|
| `010-projects` | `10` | AppProjects |
| `020-operators` | `20` | cert-manager controller, CloudNativePG, Gateway API CRDs, MongoDB operator, Sealed Secrets |
| `030-trust-manager` | `30` | trust-manager |
| `040-cert-manager-bootstrap` | `40` | Certificate bootstrap |
| `050-linkerd-crds` | `50` | Linkerd CRDs |
| `060-linkerd-bootstrap` | `60` | Linkerd certificates and trust |
| `070-linkerd-control-plane` | `70` | Linkerd control plane |
| `080-namespace-isolation` | `80` | Namespace isolation |
| `090-linkerd-viz` | `90` | Linkerd Viz |
| `100-platform-services` | `100` | Grafana, MongoDB, Traefik |
| `110-data-services` | `110` | Graylog, staging PostgreSQL |
| `200-nexus` | `200` | Nexus |
| `300-sample` | `300` | Sample |

Independent applications can share a wave. Allocate a later wave only for a real deployment dependency. Use gaps for intermediate dependencies and normally space new application tiers by `100`. When adding or moving templates, verify every resource has a positive annotation matching its directory. An omitted annotation defaults to wave `0`, before projects. Keep directory names at a consistent width if the allocation grows beyond three digits. These are root Application waves; waves inside each child remain local to that child's sync.

The numbered layout replaces the former negative infrastructure waves and application waves `100`/`200`. It preserves dependency order and same-wave groups. Resource identities and child source paths are unchanged.

Linkerd uses the edge chart repository and externally managed certificates. cert-manager issues the root, identity issuer and webhook certificates. trust-manager distributes the public trust bundle from `cert-manager`. Keep root private keys out of public bundles. Root key rotation is disabled; renewal retains the existing key. Follow the [planned coordinated rotation procedure](../todo.md#linkerd-root-key-rotation) before changing that behavior. Use a Linkerd CLI matching the deployed control plane.

Gateway API CRDs precede Traefik. Traefik watches `drn-project` and creates the `drn-project` Gateway there. Sample's HTTPRoute attaches in the same namespace, routes `/app`, and rewrites the prefix. Confirm route `Accepted=True` and `ResolvedRefs=True` before evaluating application behavior. Public HTTP/1.1 reaches Traefik; backend Sample and Nexus listeners use h2c on 5998 and 5988, with Service port 80 and `appProtocol: kubernetes.io/h2c`. Those service listeners do not accept HTTP/1.1. A separate HTTP/1 listener on port 8081 supports kubelet probes. Linkerd supplies mesh mTLS.

The [Sample HTTPRoute](../services/sample/base/httproute.yaml) maps `/app` and `/app/` to `/`, and `/app/example` to `/example`. Prefix matching is case-sensitive and excludes `/appx`. The route sets `X-Forwarded-Prefix: /app` and removes client-supplied `Forwarded`, `X-Forwarded-Ssl`, `X-Original-URI`, `X-Forwarded-Path` and `l5d-dst-override` headers. Configure application proxy trust for Traefik's forwarded request metadata.

The route also removes Linkerd retry and timeout override headers before forwarding requests to workloads. Traefik's inbound Linkerd proxy receives public requests before these filters run, so keep per-request retry and timeout policies disabled on that unfiltered ingress path. See [Linkerd's per-request policy guidance](https://linkerd.io/docs/features/retries-and-timeouts/#per-request-policies).

Use a CNI that enforces the [documented ingress policies](../README.md#service-protection). Fresh Traefik pods start with a deny default before serving traffic. When changing policies later, deploy restrictive mesh authorization before widening transport access. Do not assume manifest acceptance proves isolation, probe health or webhook reachability.

## Workload health probes

Both workload bases define startup, readiness and liveness probes. All environment overlays inherit them. Each probe requests the local anonymous GET inherited from DRN's `WeatherForecastControllerBase`:

| Workload | Pod path | Host header | Named port |
|---|---|---|---|
| Sample | `/Api/Sample/WeatherForecast` | `sample` | `health` (`8081`) |
| Nexus | `/Api/WeatherForecast` | `nexus` | `health` (`8081`) |

Probe requests go directly to the Pod IP. Sample's external `/app` prefix is not used. The Sample `/Api/Sample/WeatherForecast/Nexus` action calls Nexus and is deliberately excluded from probes. These local checks verify that the HTTP application pipeline responds. They do not query databases or establish dependency readiness.

Kestrel's additional `Health` endpoint uses `Http1` because standard kubelet HTTP probes use HTTP/1. Native h2c probes require the alpha `H2CContainerProbe` feature gate. The existing h2c service listeners remain unchanged. Port 8081 is not published by a Service or Gateway, and no ingress allowance is added. This Kestrel listener serves the same application routes, not only WeatherForecast, and retains application authentication, host filtering, mesh policy and namespace isolation.

Startup permits 60 failures at 5-second intervals, a nominal 300-second startup budget. Readiness checks every 5 seconds and marks the container unready after 3 consecutive failures. Liveness checks every 10 seconds and restarts the container after 6 consecutive failures. Each request has a 2-second timeout. Kubernetes suppresses readiness and liveness probes until startup succeeds. These are initial defaults; evaluate them against observed startup and migration durations.

Before rolling out stage/prod, include `sample` in Sample's `AllowedHosts` Secret value and `nexus` in Nexus's value, alongside the existing accepted hosts. Otherwise host filtering returns HTTP 400 and the probes fail. Roll out pods after changing Secrets. Linkerd automatically authorizes declared HTTP probe routes while the port uses its default inbound policy. If an explicit Server or route policy is later introduced for this port, preserve probe authorization deliberately.

The routes and anonymous action were checked against sibling DRN-Project tag `release/v0.10.1-preview003`. Confirm actual probes succeed with the pinned images and Linkerd before declaring the rollout healthy. See [Kubernetes probe behavior](https://kubernetes.io/docs/concepts/workloads/pods/probes/) and [Linkerd probe authorization](https://linkerd.io/docs/features/server-policy/).

## PostgreSQL

The image is `ghcr.io/cloudnative-pg/postgresql:18.6-standard-trixie@sha256:79be0d10e78b7ac4520cf6c5db199a8bb7f6de53b3229f47e90cab5865528a0d`. The unsuffixed `18.6-standard` tag is not published. The current tag's multi-architecture index digest was verified directly against GHCR on 2026-10-11 with `docker buildx imagetools inspect`; it contains Linux amd64 and arm64 images. The [publisher's image listing](https://github.com/cloudnative-pg/postgres-containers/pkgs/container/postgresql) also associates it with the October 5 build. This is manifest verification, not a runtime compatibility test.

Fresh clusters initialize PostgreSQL 18.6 on Debian Trixie. The former PostgreSQL 16 pin used Bookworm. Do not directly sync this pin to that existing cluster: CloudNativePG 1.30 in-place major upgrades require the same OS distribution. Plan and rehearse a migration to a new cluster, such as logical dump/restore, with a verified backup, application and extension compatibility checks, locale/collation validation and scheduled downtime. Take a new backup after success. Reverting an image tag after a successful major upgrade is not a database downgrade. See [CloudNativePG 1.30 major upgrades](https://cloudnative-pg.io/docs/1.30/postgres_upgrades/). No cluster upgrade was executed as part of this pin change.

The standard image includes extensions, locales and LLVM JIT, but omits the Barman Cloud binaries bundled in system images. Use a backup plugin when adding Barman Cloud backups. This cluster currently configures no backups. See the [publisher's image types](https://github.com/cloudnative-pg/postgres-containers#image-types).

Staging gets a CloudNativePG Cluster named `postgresql` in `drn-project`. It uses a single instance, an `8Gi` volume from the default StorageClass, database `drnDb`, owner `drn`, and primary service `postgresql-rw:5432`. Set a storage class explicitly if the cluster lacks a suitable default.

CloudNativePG generates the `postgresql-app` credential Secret and `postgresql-ca` CA Secret. Stage mounts only their `pgpass` and public `ca.crt` keys directly. Its ConfigMaps provide explicit password-free connection strings using Npgsql `Passfile` and verified TLS. No database credential or CA copies are needed in the [application Secrets](staging-configuration.md#staging-database-credentials). Roll out affected pods after credential or CA rotation and verify new connections; projected-file updates alone do not establish connection-pool refresh. Production database inputs remain externally provisioned.

The database and operator are unmeshed. PostgreSQL rejects non-TLS network connections and disables superuser network access. Server-enforced TLS alone does not establish server certificate verification. Stage/prod connections must explicitly verify the endpoint and trust its CA.

The single instance disables its PodDisruptionBudget, permits maintenance downtime and has no configured backups. PostgreSQL automatic pruning is disabled, and its Cluster has `Prune=false,Delete=false`. Retention is not a backup. Configure and verify recovery before relying on stored data. Prod has no bundled database Cluster and requires separate provisioning and migration.

## Graylog

See the README's [connection encryption and certificates](../README.md#connection-encryption-and-certificates) tables for the transport and certificate lifecycle of each connection. Data Node's automatic self-signed startup uses certificates valid for `99 × 365` days in the pinned version. No automatic Data Node rotation policy is configured. MongoDB's cert-manager certificate instead has a 90-day lifetime and renews 30 days before expiry with a new private key.

Graylog Open and Data Node use the upstream Graylog chart. MongoDB Community is managed by MongoDB Controllers for Kubernetes through a separate Git-backed Application. Only Graylog server pods are meshed. MongoDB, its operator and Data Node remain unmeshed; Data Node manages OpenSearch internally.

MongoDB requires native TLS and retains SCRAM authentication. cert-manager issues its server certificate through the existing `linkerd-trust-anchor` ClusterIssuer. A trust-manager Bundle distributes that CA and public roots to the `graylog` namespace as PEM and a Java PKCS12 truststore. Graylog and Data Node mount that store and verify MongoDB's certificate. Its `changeit` password protects no private material; the store contains public CA certificates only. No certificate-generation script or additional controller is needed.

For fresh installs, the MongoDB operator's generated connection URI includes `ssl=true`; the existing bootstrap Job copies it into `graylog-credentials` for both clients. This configuration is not an automatic migration of an existing plaintext deployment: the Job preserves existing Secrets. Such a migration would require updating the saved URI and restarting both clients. After CA bundle changes, restart Graylog and Data Node so their JVMs reload trust. Coordinate root CA rotation with Linkerd because the issuer is shared.

The pinned chart defaults `graylog.config.selfSignedStartup` to `"true"`, enabling automatic startup with self-signed certificates. This deployment inherits that setting. Manual CA provisioning in the preflight UI is not required for this startup mode. Data Node uses its certificate configuration for both OpenSearch and its management API. Do not add a second certificate mechanism or infer API encryption from the standalone `http_enable_tls` default. Verify HTTPS on ports 8999 and 9200 after startup. Automatic self-signed startup is distinct from configuring a managed CA and renewal policy, which remain outside this beta setup. See the [pinned chart configuration](https://github.com/Graylog2/graylog-helm/blob/graylog-2.1.0/charts/graylog/templates/config/graylog.yaml), [encrypted startup test](https://github.com/Graylog2/graylog2-server/blob/7.1.10/full-backend-tests/src/test/java/org/graylog/searchbackend/datanode/DatanodeSelfsignedStartupIT.java), and [Data Node API implementation](https://github.com/Graylog2/graylog2-server/blob/7.1.10/data-node/src/main/java/org/graylog/datanode/initializers/JerseyService.java).

Provide a default ReadWriteOnce StorageClass. Baseline volumes are MongoDB `8Gi` data plus `2Gi` logs, Graylog `8Gi`, and Data Node `8Gi`. Hosts need `vm.max_map_count >= 262144` and must meet MongoDB's platform requirements. Memory requests include Graylog `1Gi`, Data Node `3.5Gi` and MongoDB `1Gi`, plus agent/operator overhead. They are not usage limits. The stack is single-instance and has no configured backups.

The MongoDB Application must receive a full sync. Its bootstrap Job generates credentials, waits for the operator connection URI, and creates Graylog's external Secret. Selective resource sync skips hooks. Later full syncs preserve existing Secrets; the Job does not rotate credentials or recover deleted Secrets for existing data. Back up those Secrets with the database. MongoDB's readiness RBAC names `mongodb-0`; extend it deliberately when adding members.

The initial root sync can wait while Graylog setup needs human input. Forward its private UI:

```sh
kubectl -n graylog port-forward svc/drn-graylog 9000:9000
```

Open [Graylog](http://localhost:9000). Retrieve the generated password from `graylog-admin-password` privately. Complete Data Node provisioning using the setup credentials from pod logs if prompted, then log in as `admin`. Keep credentials out of Git and shared output. Update `graylog.config.network.externalUri` in the template if changing the UI address.

Graylog loads separate `DRN input` and `DRN streams` content packs at startup. The input creates **DRN GELF HTTP** on `0.0.0.0:12201`, accepts bulk messages, preserves `full_message`, and extracts scoped JSON properties as `scope_...`. Set index replicas to `0` for this single Data Node.

The DRN stream matches `scope_ScopedLog=true`, uses the default index set and removes matching messages from the Default Stream. Graylog creates content-pack streams paused. Start **DRN** after installation and confirm exactly one GELF input and one DRN stream. Verify an actual scoped message before treating logging as ready.

Both applications send LF-delimited, uncompressed GELF HTTP to `http://drn-graylog.graylog:12201/gelf`. Keep this internal URL as HTTP because Linkerd proxies provide mesh mTLS. Application authorization is still required for Graylog's API.

To change a content pack, increment its own revision in the template and publish a new GitOps snapshot. After syncing the changed files, restart Graylog. The loader installs a revision once; a new revision creates a separate installation. Preserve customizations and uninstall the prior installation before loading a replacement to avoid duplicate inputs/streams. Input and stream packs do not reference each other's entities.

Automatic pruning is disabled for Graylog/MongoDB; MongoDB also has explicit deletion protection. Restart Graylog and Data Node after credential rotation. Data recovery and availability need separate operational procedures.

## Grafana and secrets

Grafana is an ephemeral, anonymous Viewer behind Linkerd Viz. It has no initial admin account, no login form, immutable dashboard definitions and a Prometheus datasource in `linkerd-viz`. Do not expose it publicly without reviewing access and persistence needs. Its service account is authorized for the Viz Prometheus admin Server.

Start the local Viz dashboard with the matching Linkerd CLI:

```sh
linkerd viz dashboard
```

Keep the command running and open `/grafana/` on the local dashboard URL. Dashboards are provisioned from pinned revisions at startup. Local changes are not persisted.

Sealed Secrets is installed separately from cert-manager. It provides a controller, not an application secret provisioning policy. The required stage/prod application Secrets must be supplied through an administrator-controlled process. The restricted workload AppProject cannot create Secret or SealedSecret resources. Back up sealing keys before depending on sealed data for recovery.

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

Confirm app service accounts cannot read Secrets or ConfigMaps through the Kubernetes API. Verify non-TLS database connections fail and stage/prod clients authenticate the server certificate. Restore and credential-rotation exercises are required before relying on persistent beta data. These checks require an actual environment and are not established by static review.

For MongoDB, verify both Graylog and Data Node connect with certificate verification, plaintext connections fail, and an untrusted CA fails the TLS handshake. Confirm Data Node's management and OpenSearch endpoints use HTTPS after certificate provisioning. Exercise MongoDB certificate renewal before treating the deployment as operationally validated.
