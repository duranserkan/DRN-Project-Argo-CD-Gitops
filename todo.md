# TODO

Tasks are grouped by owner. Repository changes define desired configuration and documentation. Cluster prerequisites and runtime checks require access to the actual environment. Listing execution work here does not authorize running it.

Reviewed against local source, test cases and workflow definitions on 2026-10-18. Checked implementation items mean the code or CI wiring exists. This review did not execute tests, inspect CI run results or verify the live cluster.

## Cluster-level prerequisites

These belong to cluster provisioning, provider settings or the existing secret-management process. This repository does not manage the Kubernetes API-server configuration.

- [ ] Verify Secret encryption at rest, including coverage of existing Secrets and backups. If protection is missing, address it through the cluster provisioning configuration or provider controls. Ordinary Argo CD application manifests and Sealed Secrets installation do not enable API-server encryption at rest. See [Kubernetes encryption configuration](https://kubernetes.io/docs/tasks/administer-cluster/encrypt-data/).
- [ ] Verify credential rotation ownership and recovery in the cluster or secret backend. Identify copied credentials and CA bundles that need coordinated updates, and consumers that require restarts. Record the application-facing procedure in this repository's operations documentation.
- [ ] Confirm that the installed network plugin enforces NetworkPolicy. Obtain reliable API-server source CIDRs and required API-server, DNS and webhook traffic details before tightening policies.
- [ ] Audit effective Kubernetes and Argo CD permissions, including Secret access, pod execution, namespace labels and service-account selection. Keep bootstrap and infrastructure administration restricted. Repository role definitions alone do not establish effective cluster access.
- [ ] Before enforcing Pod Security policies, confirm admission support and resolve Linkerd proxy-init capabilities through the cluster networking setup, for example Linkerd CNI. Baseline and Restricted policies would currently reject injected pods.

## External repository and identity prerequisites

- [ ] Verify protected branches and required CODEOWNERS approval for infrastructure and bootstrap manifests in the Git hosting settings. The checked-in CODEOWNERS file alone does not enforce approval.
- [ ] Verify SonarCloud CI analysis, `SONAR_TOKEN`, and required checks against actual workflow runs. Branch CI defines `ci / gatekeeper`; pull-request CI currently runs Trivy and CodeQL without the bootstrap test job. Confirm checks are emitted for the intended event before requiring them in branch protection. See [CI configuration](tests/ApplicationSecretBootstrap.Tests/README.md).
- [ ] When shared Argo CD access requires SSO, obtain the identity-provider configuration and actual user/group mappings before adding repository configuration.
- [ ] If tracing discovery identifies missing instrumentation, add it in the application repository for ASP.NET Core requests, outgoing HTTP requests and database calls. Propagate trace context between services and assign distinct service names. Publish suitable images before configuring their exporters here.

## Work in this GitOps repository

### Argo CD health gate and bootstrap verification

The Application health scripts in [standard values](infrastructure/argocd/custom-values.yaml) and [HA values](infrastructure/argocd/custom-values-ha.yaml) still lack automated behavior tests. Both credential bootstraps now use C#. The [test source library](tests/Bootstrap.Sources/Bootstrap.Sources.csproj) compiles the deployed files with their standalone entry points excluded. Tests exercise lifecycle and HTTP behavior using synthetic Secrets, an HTTP substitute and a simulated polling clock. They do not establish SDK container startup, real service-account authentication, TLS or Argo CD hook behavior.

- [ ] Add regression tests for the complete health script extracted from each values file. Use the pinned Argo CD Lua runtime with `UseOpenLibs=false`, and assert that both embedded scripts remain identical. Do not test a separately maintained copy.
- [ ] Cover successful full sync and healthy resources, queued/running/failed operations, missing status, unfinished operations, stale comparisons, dry runs, manual selective resource syncs and apply-strategy syncs. Verify that every rejected case blocks Healthy and that all supported error conditions report Degraded. Cover the permitted automatic self-heal case separately below.
- [ ] Cover single-source and multi-source Applications, changed source configuration, destination and resolved revisions, missing revisions, and no-op source updates. Test omitted versus empty optional `ignoreDifferences` fields and real rule changes. Preserve child resource health propagation after the completion checks pass.
- [ ] Add a regression case for `ApplyOutOfSyncOnly=true` with hooks. Follow the pinned-version evidence in [installation dependencies](docs/platform-operations.md#installation-dependencies); do not treat this option as equivalent to selective resource sync.
- [ ] Add regression tests proving that nonempty local-manifest overrides block Healthy even when source, revision and resource health match. Verify that omitted and empty manifest lists still allow an otherwise valid full sync. Cover recovery through a subsequent full sync from the configured source.
- [ ] Cover automatic selective self-heal acceptance only with positive `autoHealAttemptsCount` and matching automated full-sync history for all current sources and revisions. Verify missing, evicted, manual or mismatched history blocks Healthy and that a subsequent full sync recovers it.
- [x] Implement [Graylog C# bootstrap behavior tests](tests/ApplicationSecretBootstrap.Tests/GraylogBootstrapTests.cs) using synthetic Secrets and a controlled API substitute. Cases cover fresh bootstrap, credential preservation, refusal after partial bootstrap or credential loss, create conflicts, invalid fields, API failures and timeouts. Error assertions check that private response bodies and timeout details are not exposed. Operator polling tests cover transient retries within the original deadline and fatal authorization failures.
- [x] Implement [application Secret bootstrap behavior tests](tests/ApplicationSecretBootstrap.Tests/BootstrapTests.cs) for automatic fresh initialization with a ConfigMap claim, enrollment of existing installs, total Secret loss, concurrent claimants and explicit recovery. Cover preservation of custom settings, recovery from surviving credentials, interrupted creation, create conflicts, invalid fields and API failures. Include assertions against response-body disclosure and verify token refresh on each request. Test implementation is complete; execution is not established by this checklist.
- [x] Wire bootstrap builds, tests and coverage into [shared branch CI](.github/workflows/ci.yml) through the [backend action](.github/actions/dotnet-sonar-scan/action.yml). Both [develop](.github/workflows/develop.yml) and [master](.github/workflows/master.yml) invoke it on branch pushes. This records workflow implementation, not a verified successful run.
- [ ] Connect bootstrap tests and the future Lua regression suite to pull-request CI. The current [PR workflow](.github/workflows/pull-request.yml) runs Trivy and CodeQL only. Preserve its secretless execution boundary when adding tests.

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

- [ ] Test OpenObserve with the OpenTelemetry Collector in staging. Evaluate NLog JSON logs collected from container stdout, structured-field preservation, and searchability. Compare setup effort, resource usage, ingestion delay, and query latency with Graylog using the same workload. Record findings before deciding whether to replace Graylog.
- [ ] Test traces exported through the OpenTelemetry Collector to OpenObserve in staging. Verify connected traces across Sample and Nexus, span attributes, error details, and trace search. Check log-to-trace correlation where trace identifiers are available.
- [ ] Test metrics exported through the OpenTelemetry Collector to OpenObserve in staging. Verify application and Kubernetes metric ingestion, labels, timestamps, and dashboard queries. Record resource usage and ingestion delay for both metrics and traces. This evaluation is separate from the tracing-only implementation scope above.
- [ ] Verify both credential bootstrap Jobs start and complete using the pinned Ubuntu 24.04 SDK image as UID/GID `1654`. Confirm the init container creates private runtime and application-data directories, restore and compilation use the writable scratch volume, and execution succeeds with a read-only root filesystem. Check nuget.org access, resource limits, deadlines and preservation of existing Secrets. Unit tests exclude the production entry points and do not validate this container environment.
- [ ] Verify automatic application Secret initialization in a disposable fresh staging namespace. Confirm the immutable initialization ConfigMap is retained after hook cleanup, partial loss recovers existing credentials, and total settings Secret loss blocks regeneration while the marker survives. Check marker RBAC and existing-install enrollment before promotion. Verify backup/restoration and deliberate one-run recovery separately. Do not delete credentials in an environment containing data that must remain readable.
- [ ] Verify that a pending or failed MongoDB credential hook blocks the later Graylog wave and that a successful full retry releases it.
- [ ] In an authorized disposable namespace, verify Graylog marker enrollment, retention after hook cleanup, refusal after total Secret loss, unchanged partial-loss safeguards and restoration from backup. Verify the new ConfigMap RBAC. Unit test additions do not establish these runtime outcomes.
- [ ] Before relying on isolation, check allowed service flows, operator reconciliation and webhooks, and denied calls from an unlisted namespace, including meshed callers. Recheck admission after restricting webhook sources.
- [ ] Exercise the proposed root rotation and recovery procedure before enabling it for normal operations. Verify overlapping trust before renewing the Linkerd identity issuer, webhook certificates and other dependent certificates. Coordinate control-plane and meshed workload rollouts, checking replicas, readiness, rollout strategies and capacity. Verify certificate adoption, webhook admission, mTLS and application traffic before removing the old root. Include external and multicluster consumers when present, and retain overlapping trust if any check fails.
- [ ] Verify shared-root rotation and recovery for MongoDB, Graylog and Data Node. Confirm both roots reach the MongoDB PEM bundle and the Graylog/Data Node PKCS12 truststores. Restart both clients and verify MongoDB connectivity before renewing its server certificate. Confirm MongoDB serves the new certificate and both clients establish new TLS connections with certificate verification enabled. Verify Graylog ingestion and Data Node operation before removing the old root. Reload the final trust bundles and repeat connectivity checks after removal. Retain overlapping trust if any check fails.
- [ ] After approved tracing deployment and workload rollouts, verify that a request through Sample and Nexus produces a connected trace containing application, database and Linkerd proxy spans in the selected backend.
