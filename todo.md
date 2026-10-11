# TODO

Tasks are grouped by owner. Repository changes define desired configuration and documentation. Cluster prerequisites and runtime checks require access to the actual environment. Listing execution work here does not authorize running it.

## Cluster-level prerequisites

These belong to cluster provisioning, provider settings or the existing secret-management process. This repository does not manage the Kubernetes API-server configuration.

- [ ] Verify Secret encryption at rest, including coverage of existing Secrets and backups. If protection is missing, address it through the cluster provisioning configuration or provider controls. Ordinary Argo CD application manifests and Sealed Secrets installation do not enable API-server encryption at rest. See [Kubernetes encryption configuration](https://kubernetes.io/docs/tasks/administer-cluster/encrypt-data/).
- [ ] Verify credential rotation ownership and recovery in the cluster or secret backend. Identify copied credentials and CA bundles that need coordinated updates, and consumers that require restarts. Record the application-facing procedure in this repository's operations documentation.
- [ ] Confirm that the installed network plugin enforces NetworkPolicy. Obtain reliable API-server source CIDRs and required API-server, DNS and webhook traffic details before tightening policies.
- [ ] Audit effective Kubernetes and Argo CD permissions, including Secret access, pod execution, namespace labels and service-account selection. Keep bootstrap and infrastructure administration restricted. Repository role definitions alone do not establish effective cluster access.
- [ ] Before enforcing Pod Security policies, confirm admission support and resolve Linkerd proxy-init capabilities through the cluster networking setup, for example Linkerd CNI. Baseline and Restricted policies would currently reject injected pods.

## External repository and identity prerequisites

- [ ] Verify protected branches and required CODEOWNERS approval for infrastructure and bootstrap manifests in the Git hosting settings. The checked-in CODEOWNERS file alone does not enforce approval.
- [ ] When shared Argo CD access requires SSO, obtain the identity-provider configuration and actual user/group mappings before adding repository configuration.
- [ ] If tracing discovery identifies missing instrumentation, add it in the application repository for ASP.NET Core requests, outgoing HTTP requests and database calls. Propagate trace context between services and assign distinct service names. Publish suitable images before configuring their exporters here.

## Work in this GitOps repository

### Argo CD health gate and bootstrap verification

The Application health scripts in [standard values](infrastructure/argocd/custom-values.yaml) and [HA values](infrastructure/argocd/custom-values-ha.yaml) have been reviewed statically. The repository has no automated behavior tests for these scripts or the [MongoDB credential bootstrap](infrastructure/graylog/mongodb/credential-bootstrap.yaml). Existing CI scans do not execute their behavior. Test implementation and execution remain pending.

- [ ] Add regression tests for the complete health script extracted from each values file. Use the pinned Argo CD Lua runtime with `UseOpenLibs=false`, and assert that both embedded scripts remain identical. Do not test a separately maintained copy.
- [ ] Cover successful full sync and healthy resources, queued/running/failed operations, missing status, unfinished operations, stale comparisons, dry runs, selective resource syncs and apply-strategy syncs. Verify that every non-success case blocks Healthy and that all supported error conditions report Degraded.
- [ ] Cover single-source and multi-source Applications, changed source configuration, destination and resolved revisions, missing revisions, and no-op source updates. Test omitted versus empty optional `ignoreDifferences` fields and real rule changes. Preserve child resource health propagation after the completion checks pass.
- [ ] Add a regression case for `ApplyOutOfSyncOnly=true` with hooks. The pinned [sync engine](https://github.com/argoproj/argo-cd/blob/v3.5.4/gitops-engine/pkg/sync/sync_context.go) retains hook tasks in `filterOutOfSyncTasks`; do not treat this option as equivalent to selective resource sync.
- [ ] Add regression tests proving that nonempty local-manifest overrides block Healthy even when source, revision and resource health match. Verify that omitted and empty manifest lists still allow an otherwise valid full sync. Cover recovery through a subsequent full sync from the configured source.
- [ ] Add behavior tests for the credential-bootstrap JavaScript using synthetic Secrets and a controlled API substitute. Cover fresh bootstrap, preservation of existing credentials, refusal to regenerate after partial bootstrap or credential loss, concurrent-create conflicts, missing/invalid Secret fields, API failures and timeouts. Assert that failures do not disclose Secret values.
- [ ] Connect the approved regression suites to pull-request CI.

### Namespace isolation

The implemented baseline allows traffic within each protected application namespace and explicitly whitelists cross-namespace ingress. It covers `drn-project`, Graylog and Grafana in each environment's separate cluster, not the whole cluster.

Here, ingress and egress mean traffic entering and leaving pods under NetworkPolicy. They do not refer to the Kubernetes Ingress resource. This repository uses Gateway API HTTPRoutes and Services for routing. The current policies restrict incoming pod traffic; outgoing pod traffic remains unrestricted.

These items become implementation work when their stated conditions apply and the cluster prerequisites are satisfied.

- [ ] Extend ingress isolation to platform namespaces when protecting a specific platform endpoint or requiring cluster-wide namespace isolation. First map API-server, webhook, controller and monitoring traffic. Managed API-server traffic may not originate from namespace-selected pods.
- [ ] Restrict the MongoDB operator's TCP1993 webhook exception when reliable API-server source CIDRs are known. Verify admission after restricting it. Until then, retain the documented all-source exception on that one operator port.
- [ ] Add egress restrictions when exfiltration resistance, outbound dependency control or stricter tenancy is required. First identify API-server destinations, DNS topology, required external endpoints and network-plugin behavior. Do not substitute broad HTTPS access for a verified API-server rule.
- [ ] Move Traefik to a dedicated infrastructure namespace before delegating workload pod creation to less-trusted users or using this layout as a production isolation boundary. Update Gateway attachment and controller scope with the move.
- [ ] Separate PostgreSQL only when database administration, credentials or tenancy need a different trust boundary. Define credential delivery and data placement first. A namespace move alone does not separate shared database privileges.
- [ ] After the cluster admission and Linkerd prerequisites are satisfied, configure namespace Pod Security labels before allowing untrusted workloads or relying on this boundary in production. Use built-in audit/warn modes for compatibility assessment before enforcement.
- [ ] Add service-specific Linkerd authorization when namespace trust is insufficient. For example, distinguish Graylog GELF from its admin API behind mesh port 4143 if workload callers must not reach the API.
- [ ] Configure project-scoped Argo CD user or group permissions before delegating access. Add SSO settings when shared access requires them and the external identity configuration is available. Argo CD installation values require a separate administrator-managed installation update; the DRN root does not apply them.

### Linkerd root key rotation

Root key rotation is disabled by default. The root is shared by Linkerd and MongoDB. Graylog and Data Node also trust it for their MongoDB connections. Prepare manifests and a coordinated procedure here before enabling rotation. Certificate renewal continues using the existing root key. Actual rotation and recovery belong to the authorized runtime checks below.

- [ ] Prepare trust-bundle configuration that retains the previous public root and distributes overlapping trust containing both roots through `linkerd-identity-trust-roots` and `graylog-database-trust`. Include the latter's PEM and PKCS12 formats used by MongoDB, Graylog and Data Node. Keep private keys out of the retained public bundle.
- [ ] Document bootstrap, rotation order, recovery and root expiry monitoring in the README and a runbook. Include control-plane and workload rollout order, capacity/readiness checks, and external or multicluster consumers when present. Renew dependent certificates only after all consumers trust both roots. Prevent automatic renewals from racing ahead of trust propagation, and preserve overlapping trust if verification fails.
- [ ] Include MongoDB server certificate renewal and adoption in the shared-root procedure. Restart Graylog and Data Node after distributing overlapping trust so their JVMs reload it before MongoDB adopts a certificate signed by the new root. Document trust reloads after old-root removal and recovery if either client's MongoDB TLS connection fails. Keep Data Node's separately managed self-signed API and OpenSearch certificates outside this shared-root rotation.
- [ ] Validate the proposed manifests statically before runtime verification.

### OpenTelemetry Collector and distributed tracing

Introduce tracing through an OpenTelemetry Collector. This is planned work, not an installed capability. The Collector receives, processes, and exports traces; a separate backend must provide storage and a trace UI.

- [ ] Check the instrumentation and OTLP configuration supported by the deployed Nexus and Sample images before defining the deployment contract.
- [ ] Define the OpenTelemetry Collector deployment for Argo CD with a scoped AppProject, explicit resource requests and limits, health checks, and OTLP receivers. Document endpoint, protocol, and Secret requirements for producers and exporters.
- [ ] Configure Nexus and Sample to export traces to the Collector through their supported application settings.
- [ ] Configure Linkerd proxy tracing to export spans to the Collector. Verify the installed Linkerd release's requirements for collector meshing and identity, and document the required workload rollout.
- [ ] Select and define an independently managed tracing backend deployment for staging. Evaluate Jaeger or another OTLP-compatible backend. Define retention, storage, and UI access.
- [ ] Set sampling and sensitive-data handling policies before enabling tracing. Keep Graylog for logs; metrics collection is outside this task.
- [ ] Validate tracing manifests statically before runtime verification.
- [ ] Document deployment order, configuration, troubleshooting, and cleanup in the README once implemented.

## Runtime verification and operations

These checks require explicit execution approval and an actual non-production cluster. Static review and manifest acceptance do not establish runtime behavior.

- [ ] Verify that a pending or failed MongoDB credential hook blocks the later Graylog wave and that a successful full retry releases it.
- [ ] Before relying on isolation, check allowed service flows, operator reconciliation and webhooks, and denied calls from an unlisted namespace, including meshed callers. Recheck admission after restricting webhook sources.
- [ ] Exercise the proposed root rotation and recovery procedure before enabling it for normal operations. Verify overlapping trust before renewing the Linkerd identity issuer, webhook certificates and other dependent certificates. Coordinate control-plane and meshed workload rollouts, checking replicas, readiness, rollout strategies and capacity. Verify certificate adoption, webhook admission, mTLS and application traffic before removing the old root. Include external and multicluster consumers when present, and retain overlapping trust if any check fails.
- [ ] Verify shared-root rotation and recovery for MongoDB, Graylog and Data Node. Confirm both roots reach the MongoDB PEM bundle and the Graylog/Data Node PKCS12 truststores. Restart both clients and verify MongoDB connectivity before renewing its server certificate. Confirm MongoDB serves the new certificate and both clients establish new TLS connections with certificate verification enabled. Verify Graylog ingestion and Data Node operation before removing the old root. Reload the final trust bundles and repeat connectivity checks after removal. Retain overlapping trust if any check fails.
- [ ] After approved tracing deployment and workload rollouts, verify that a request through Sample and Nexus produces a connected trace containing application, database and Linkerd proxy spans in the selected backend.
