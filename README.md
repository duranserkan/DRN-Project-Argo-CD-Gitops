# DRN Project Argo CD GitOps

[![master](https://github.com/duranserkan/DRN-Project-Argo-CD-Gitops/actions/workflows/master.yml/badge.svg?branch=master)](https://github.com/duranserkan/DRN-Project-Argo-CD-Gitops/actions/workflows/master.yml)
[![develop](https://github.com/duranserkan/DRN-Project-Argo-CD-Gitops/actions/workflows/develop.yml/badge.svg?branch=develop)](https://github.com/duranserkan/DRN-Project-Argo-CD-Gitops/actions/workflows/develop.yml)
[![Docker Hub](https://img.shields.io/badge/images-2496ED?logo=docker&label=dockerhub)](https://hub.docker.com/u/duranserkan)
[![Nuget](https://img.shields.io/badge/packages-004880?logo=nuget&label=nuget)](https://www.nuget.org/profiles/duranserkan)
[![wiki](https://img.shields.io/badge/Doc-Awesome_Kubernetes-326CE5)](https://github.com/tomhuang12/awesome-k8s-resources)
[![wiki](https://img.shields.io/badge/Doc-Awesome_Argo-orange)](https://github.com/akuity/awesome-argo)

[![Security Rating](https://sonarcloud.io/api/project_badges/measure?project=duranserkan_DRN-Project-Argo-CD-Gitops&metric=security_rating)](https://sonarcloud.io/summary/new_code?id=duranserkan_DRN-Project-Argo-CD-Gitops)
[![Maintainability Rating](https://sonarcloud.io/api/project_badges/measure?project=duranserkan_DRN-Project-Argo-CD-Gitops&metric=sqale_rating)](https://sonarcloud.io/summary/new_code?id=duranserkan_DRN-Project-Argo-CD-Gitops)
[![Reliability Rating](https://sonarcloud.io/api/project_badges/measure?project=duranserkan_DRN-Project-Argo-CD-Gitops&metric=reliability_rating)](https://sonarcloud.io/summary/new_code?id=duranserkan_DRN-Project-Argo-CD-Gitops)
[![Vulnerabilities](https://sonarcloud.io/api/project_badges/measure?project=duranserkan_DRN-Project-Argo-CD-Gitops&metric=vulnerabilities)](https://sonarcloud.io/summary/new_code?id=duranserkan_DRN-Project-Argo-CD-Gitops)
[![Bugs](https://sonarcloud.io/api/project_badges/measure?project=duranserkan_DRN-Project-Argo-CD-Gitops&metric=bugs)](https://sonarcloud.io/summary/new_code?id=duranserkan_DRN-Project-Argo-CD-Gitops)
[![Lines of Code](https://sonarcloud.io/api/project_badges/measure?project=duranserkan_DRN-Project-Argo-CD-Gitops&metric=ncloc)](https://sonarcloud.io/summary/new_code?id=duranserkan_DRN-Project-Argo-CD-Gitops)

Deploy [DRN Project](https://github.com/duranserkan/DRN-Project) Sample and Nexus with Argo CD, Linkerd, Traefik, PostgreSQL and Graylog. This repository owns deployment configuration. Application builds and image publishing belong to DRN Project.

**TL;DR: You can**

- Develop and test distributed .NET applications with [DRN Framework](https://github.com/duranserkan/DRN-Project).
- Deploy Sample and Nexus to Kubernetes using Argo CD GitOps and Linkerd service mesh.
- Use Sample as a reference application and adapt this repository's deployment structure for your own services.
- Publish preview releases from `develop` and final releases from `master`, using immutable tags.
- **TODO:** Extend [Nexus App](https://hub.docker.com/r/duranserkan/drn-project-nexus) with service discovery, remote settings, a unified microservices topology view and service self-documentation.
- **TODO:** Create DRN Project Pulumi GitOps to provision Kubernetes clusters on Oracle Cloud, Azure, AWS and other cloud providers.

[About Project, Kubernetes and Argo CD](#about-project-kubernetes-and-argo-cd) | [GitOps Roadmap](#gitops-roadmap) | [Tools](#tools) | [Deployment](#deployment-model) | [Bootstrap](#bootstrap-a-cluster) | [Continuous Integration](#continuous-integration)

## About Project, Kubernetes and Argo CD

The [Distributed Reliable .Net](https://github.com/duranserkan/DRN-Project?tab=readme-ov-file#drn-project) project is committed to following best practices, ensuring that the end result is always satisfying and worth the effort. While not adhering blindly, the project emphasizes the importance of implementing best practices, particularly evident in its Continuous Integration part of the DevSecOps pipeline.

It should do the same with Continuous Deployment and Delivery. However, deciding a good delivery and deployment practice for many cases and being reasonable upfront is challenging task. The solution must be flexible and complexity should be manageable. Therefore, Kubernetes is the obvious answer :)

To be honest, this project initially avoided and resisted Kubernetes due to its complexity and maintenance issues, despite admiring its flexibility and sense of control. However, despite these reservations, Kubernetes cannot be ignored. As a de-facto standard for cloud-native development, embracing Kubernetes is crucial to avoid vendor lock-in.

It was puzzling, like trying to find a generalized solution for [the Navier–Stokes equations.](https://en.wikipedia.org/wiki/Navier–Stokes_equations) Therefore, the solution can also be simplified if certain reasonable assumptions can be made, similar to how all true-hearted, humble engineers solve the Navier–Stokes equations under specific conditions.

- Use managed Kubernetes
- Use managed databases or services outside of cluster for stateful data.
- Don't apply manifests and configurations by yourself and version control your changes

[Argo CD](https://argo-cd.readthedocs.io/en/stable/) handles the final aspect through GitOps, while the first two assumptions aim to simplify the management of complexity and maintenance for small teams or projects.

These remain design preferences. The current staging setup includes in-cluster PostgreSQL and Graylog's data services. Production application databases are provisioned separately. Administrators still install Argo CD and apply the reviewed bootstrap and environment roots. Argo CD manages the resources beneath those roots, as described in the deployment guidance below.

## Tools

High quality output is not a coincidence. It is natural result of good people's labour that works with right processes and tools.

This project recommends following tools for anyone who doesn't have strong Kubernetes experience as a starter pack.

- [kube-ps1](https://github.com/jonmosco/kube-ps1) - kube-ps1: A script that lets you add the current Kubernetes context and namespace configured on kubectl to your Bash/Zsh prompt strings (i.e. the $PS1).
- [kubectx + kubens](https://github.com/ahmetb/kubectx) - `kubectx` helps you switch between clusters back and forth, and `kubens` helps you switch between Kubernetes namespaces smoothly.
- [Headlamp](https://headlamp.dev) is an open-source Kubernetes UI for inspecting resources, viewing logs, and troubleshooting clusters. Use the desktop app with your existing kubeconfig, and manage persistent configuration changes through Git and Argo CD.

## GitOps Roadmap

Checked items describe repository configuration, not verified production readiness. Development is local. Staging is configured as the beta environment.

- [X] CI checks and GitOps deployment configuration
- [X] Linkerd support
- [X] Staging environment support
  - [X] PostgreSQL with CloudNativePG
  - [X] Graylog
- [x] [Namespace ingress isolation](#service-protection) for application, Graylog and Grafana namespaces
- [ ] RabbitMQ support in staging
- [ ] Explore CloudNativePG for production, including high availability, backups, recovery and upgrades
- [ ] Pulumi support to create a Kubernetes cluster on cloud providers such as Oracle Cloud, AWS or Azure
- [ ] [Additional Kubernetes hardening](todo.md#cluster-level-prerequisites) and [verification in a running cluster](todo.md#runtime-verification-and-operations)
- [ ] [OpenTelemetry Collector and distributed tracing](todo.md#opentelemetry-collector-and-distributed-tracing)

## Deployment model

This repository deploys staging and production only. Development is reserved for local application work outside this GitOps repository. Each deployment environment has a **separate Kubernetes cluster and its own Argo CD installation**. Apply exactly one environment root per cluster. Each uses the same platform names and `drn-project` workload namespace. These roots cannot coexist in one cluster.

The project uses Kubernetes `1.36.4` for testing. The pinned Linkerd charts require Kubernetes 1.31 or newer, but that component minimum is not a compatibility baseline for the whole stack.

| Environment | Root | DRN environment | Accepted GitOps tags | Database migrations |
|---|---|---|---|---|
| stage | [apps/staging/application.yaml](apps/staging/application.yaml) | Staging | Preview or final | `AutoMigrateStaging=true`, no prototype recreation |
| prod | [apps/production/application.yaml](apps/production/application.yaml) | Production | Final only | Explicit migration process, never startup migration |

Initially operate staging as a rolling beta. Production is disabled: its root sets `enableProduction=false`, and chart rendering fails until explicitly enabled. Do not apply the prod root during beta. Its overlays prepare for later activation and do not establish production readiness.

Use `develop` as the integration branch. Merge reviewed topic PRs into `develop`, then promote releases through a `develop → master` PR. Publish preview releases from `develop` and normal final releases from `master`. The integration branch remains even though there is no development deployment environment. Deploy immutable calendar tags:

- `2026.10.1-preview001`: first candidate from `develop` for October's first release.
- `2026.10.1-preview002`: a changed candidate from `develop`, with a new tag.
- `2026.10.1`: final release from `master`.

The format is `YYYY.M.sequence[-previewNNN]`, with an unpadded month, a positive release sequence and a three-digit preview number from `001` through `999`. Deployment roots select immutable tags rather than moving branch heads. Protect tags against updates and deletion. Roots use `refs/tags/...` to avoid branch/tag ambiguity. The chart validates tag shape and rejects previews for prod; it does not create tags or configure repository rulesets.

## One snapshot for every application

[deployment/](deployment/) is a native Helm chart containing AppProjects and leaf Applications. Workloads remain Kustomize overlays under `services/`. Platform resources remain under `infrastructure/`.

Keep standalone resources in their component directories and chart-owned `extraObjects` in Helm values. See [resource organization](docs/platform-operations.md#resource-organization) for file conventions.

The root selects a tag once. Argo CD supplies its resolved commit through `$ARGOCD_APP_REVISION`, and every Git-backed child uses that SHA. `$ARGOCD_APP_SOURCE_TARGET_REVISION` supplies the tag for validation. Linkerd and cert-manager children are flattened into this chart. This uses native [Argo CD Helm build variables](https://argo-cd.readthedocs.io/en/stable/user-guide/helm/#build-environment), with no custom plugin or deployment script.

Upstream Helm chart versions and container digests remain independent pins. GitOps tags do not trigger DRN Project's image publishing workflow or rename images. Sample and Nexus use `0.10.1-preview003` digest pins. A final GitOps tag alone does not certify those images for production.

Root selections under `apps/` are administrator-applied control configuration. Each root loads only `deployment/` from its selected tag. Updating a selection on a branch therefore does not alter the tagged payload. Record selection changes through the branch review flow, apply the selected root to its cluster, and explicitly sync it. Root auto-sync is absent; children reconcile their selected sources.

## Bootstrap a cluster

These instructions target fresh installations. No migration from the former local-only layout is provided. Provide a NetworkPolicy-enforcing CNI, default persistent StorageClass, and the capacity/host settings described in [platform operations](docs/platform-operations.md).

### Install local tools

On macOS with [Homebrew](https://formulae.brew.sh/), install the command-line tools:

```sh
brew install kubernetes-cli helm argocd kubeseal linkerd
```

Homebrew installs its current versions. Use the [Argo CD `v3.5.4` release](https://github.com/argoproj/argo-cd/releases/tag/v3.5.4) and [Linkerd `edge-26.10.1` release](https://github.com/linkerd/linkerd2/releases/tag/edge-26.10.1) for clients matching this deployment. On other platforms, use the corresponding upstream installation instructions. `kubeseal` is needed when preparing Sealed Secrets.

Check the tools and intended cluster before installing Argo CD:

```sh
kubectl config current-context
kubectl get nodes
kubectl get storageclass
helm version
argocd version --client
linkerd version --client
kubeseal --version
```

### Install Argo CD and log in

Use the intended cluster's kubectl context. Install Argo CD with the existing pinned chart:

```sh
helm repo add argo https://argoproj.github.io/argo-helm
helm repo update
helm install argocd argo/argo-cd --version 10.10.2 \
  -f infrastructure/argocd/custom-values.yaml \
  --create-namespace -n argocd --wait --timeout 10m
```

For Argo CD HA with at least three workers, use `custom-values-ha.yaml` instead. This chart installs Argo CD `v3.5.4`. Both values files include the child Application health customization needed by root sync waves. Authenticated users receive no default resource grants. Configure explicit administrator/project access and restrict the built-in default project:

```sh
kubectl apply -f infrastructure/argocd/default-project.yaml
kubectl apply -f apps/bootstrap-project.yaml
kubectl -n argocd port-forward svc/argocd-server 8080:443
```

Keep the port-forward running in a separate terminal. In another terminal, use the CLI matching Argo CD `v3.5.4` to retrieve the initial `admin` password from the cluster:

```sh
argocd admin initial-password -n argocd
```

This prints a credential. Keep it private. Log in through [Argo CD](https://localhost:8080) as `admin`, or enter that password at the CLI prompt:

```sh
argocd login localhost:8080 --username admin
```

The default server certificate is self-signed. For this local port-forward, verify the cluster context and accept the certificate prompt. Change the password after login:

```sh
argocd account update-password
```

The initial password does not track later password changes. If it no longer works or the initial Secret has been deleted, use the current admin password or follow the [official admin password reset procedure](https://argo-cd.readthedocs.io/en/stable/faq/#i-forgot-the-admin-password-how-do-i-reset-it). See [Argo CD's login instructions](https://argo-cd.readthedocs.io/en/stable/getting_started/#4-login-using-the-cli).

### Install platform dependencies and workloads

The environment root now installs the dependencies that the previous layout required you to apply separately. After bootstrap, one full `drn-project` sync creates their Applications in this order:

| Order | Dependencies installed by the root |
|---|---|
| 1 | Scoped AppProjects |
| 2 | Gateway API CRDs, cert-manager, CloudNativePG, MongoDB operator and Sealed Secrets |
| 3 | trust-manager and certificate bootstrap |
| 4 | Linkerd CRDs, certificate/trust bootstrap and control plane |
| 5 | Namespace isolation and Linkerd Viz |
| 6 | Traefik, Grafana and MongoDB |
| 7 | Graylog and staging PostgreSQL |
| 8 | Nexus, then Sample |

Gateway API installation is included through the `gateway-api` Application. Traefik's Gateway API provider is already enabled in its chart values. Later waves wait for earlier Applications to satisfy the deployment completion gate described below and become healthy. Use the root flow below instead of the removed per-component `kubectl apply` paths.

For a fork, update the repository URL in `apps/bootstrap-project.yaml`, each environment root and `deployment/values.yaml` before tagging. Keep Helm repository URLs unchanged. Configure Argo CD credentials for private repositories. Changing only a local Git remote is insufficient.

Publish the selected snapshot before applying its root. The initial `2026.10.1-preview001` selection is an example, not an assertion that the tag exists. Its commit must contain the chart and overlays. Wait for that commit's CI checks and the tag's Trivy scan before deployment.

For staging, use its cluster context:

```sh
kubectl apply -f apps/staging/application.yaml
argocd app sync drn-project
```

In another terminal, monitor dependency installation:

```sh
argocd app get drn-project
argocd app list
```

An Application in a later wave may not exist until earlier waves complete. Inspect the blocking child in the root's resource tree. Complete the secret and Graylog setup below before expecting the root to become healthy. After setup, run the [readiness checks](docs/platform-operations.md#readiness-checks). See [Grafana and secrets](docs/platform-operations.md#grafana-and-secrets) for dashboard access and sealing commands.

Follow the required [stage secret configuration](docs/staging-configuration.md). Isolation creates `drn-project`, and PostgreSQL creates credentials before the workload waves. Stage workloads mount the generated PostgreSQL password file and public CA directly; no database credential copying is required. During first stage bootstrap, provision `sample-settings` and `nexus-settings` with the application seed/key material and allowed hosts. Workload pods cannot start without them. If root sync times out waiting for configuration or Graylog setup, finish those prerequisites and sync again.

Graylog setup can require operator interaction. The child gate requires deployment hooks to complete for the current source and revision plus healthy resources before releasing later root waves. Successful automatic self-heals pass when matching automated deployment history proves that completion. A new revision with unchanged manifests may require an explicit full child sync. See [installation dependencies](docs/platform-operations.md#installation-dependencies) for gate rules and upgrade instructions. Waves are not transactions and do not serialize independent child auto-syncs on later updates. Check child sync revisions and health before declaring success.

If Gateway API shows **Synced / Degraded: CRD is not established**, inspect its Kubernetes conditions before continuing. Synced alone does not mean the API is available. Follow [Gateway API CRD troubleshooting](docs/platform-operations.md#gateway-api-crd-troubleshooting).

Root templates use numbered directories under `deployment/templates/` with matching sync-wave annotations. The annotations control deployment order. See [installation dependencies](docs/platform-operations.md#installation-dependencies) for wave allocation.

Sample and Nexus use their anonymous WeatherForecast endpoints for startup, readiness and liveness probes on an internal HTTP/1 listener at port `8081`. Their Services continue to use h2c. Stage/prod `AllowedHosts` Secrets must include `sample` and `nexus` for their respective probe Host headers. These checks verify application responsiveness, not database or downstream availability. See [workload health probes](docs/platform-operations.md#workload-health-probes) for paths, timings and rollout requirements.

Do not recursively apply `apps/`, apply `deployment/templates/` as raw YAML, or deploy workload bases directly. The bootstrap project manages AppProjects and Applications in `argocd`, which grants indirect administrative authority. Restrict it and its source files to administrators. Infrastructure children own namespaces. The workload project cannot create namespaces, Secrets, RBAC, NetworkPolicies or Linkerd authorization policies.

## Promote and roll back

1. Merge reviewed changes into `develop`. Wait for CI on the exact commit.
2. Publish a new preview tag at that `develop` commit. Wait for its tag scan. Update staging's `targetRevision`, record the selection in Git, apply the staging root and sync it.
3. Validate the snapshot in staging. Verify migrations, routing, authentication, database access and logs.
4. For another change, publish a new preview sequence. Never move a tag.
5. When a final release is needed, promote the reviewed candidate through `develop → master`. Preserve the tested image digests and wait for CI on the resulting `master` commit.
6. Publish the final tag from `master` and wait for its tag scan. If merging created a new commit, select that final tag in staging and validate the exact snapshot. Once the snapshot is validated and production prerequisites are complete, select the same final tag in prod, explicitly enable production in its root and sync against its separate cluster.

During rolling beta, repeat steps 1 through 4. A final tag or prod deployment is not required for each iteration. Production uses its own overlay of the validated snapshot.

Rollback selects a previous immutable tag and syncs the root. It reverts desired manifests and image pins, not database contents. Startup migrations and retained database resources mean a code rollback can require a compatible forward fix or verified database recovery. An older application may not support a newer schema.

## Production activation prerequisites

- Select approved image digests and platform settings in a candidate snapshot. Existing preview images and single-instance stateful services are not certified production defaults.
- Provision production databases separately, with verified backups, restore, availability and migrations. The chart includes the PostgreSQL Cluster only in staging. Supply explicit connections and trusted database CA certificates through required secrets.
- Configure hostnames, edge TLS and authentication. Current Traefik exposes cleartext HTTP. Linkerd mTLS protects meshed cluster traffic, not the public edge. Complete edge TLS before exposing real beta or production credentials publicly.
- Review namespace trust, admission and delegated access. Traefik and workloads share `drn-project`; workload creators are trusted with that namespace's identities and secrets.
- Exercise the production overlay, migration process, recovery and operational checks before changing `enableProduction`.

The enable flag does not perform these readiness tasks.

## Service protection

Ingress policies protect `drn-project`, `graylog` and `grafana` independently in each cluster. Same-namespace traffic is allowed. Egress and other platform namespaces are outside this baseline.

| Destination | Allowed source outside its namespace | TCP ports |
|---|---|---|
| Traefik public listener | Any source | 8000 and 4143 mesh transport |
| PostgreSQL pods | `cnpg-system` | 5432, 8000 |
| Meshed workload pods | `linkerd-viz` | 4191 metrics, 4190 tap |
| Graylog namespace | `drn-project` | 12201 GELF, 4143 mesh transport |
| Graylog namespace | `linkerd-viz` | 4191, 4190 |
| MongoDB operator webhook | Any source | 1993 |
| Grafana namespace | `linkerd-viz` | 3000, 4143, 4191, 4190 |

Traefik starts with Linkerd's default inbound policy `deny`. Its public web Server grants unauthenticated HTTP/1.1 only on the web listener. TCP4143 does not grant access to its other application ports. Automatic health-probe authorization is separate. Dashboard and public websecure exposure remain disabled.

Other meshed destinations require trusted identities. Graylog's TCP4143 allowance also exposes its authenticated API to trusted workload callers; it is not GELF-only authorization. Native database authentication/TLS remain required. Node traffic and API-authorized port-forwarding need separate controls. The MongoDB webhook exception preserves API-server admission until reliable source CIDRs are known.

Sample and Nexus retain non-root containers, read-only root filesystems, dedicated service accounts, disabled API-token mounting and no application RBAC grants. Writable `emptyDir` data/logs are lost on pod removal. Namespace workload creators can mount its secrets. See [remaining hardening work](todo.md#namespace-isolation).

## Connection encryption and certificates

Linkerd mTLS encrypts traffic between proxies and authenticates both workload identities. Applications can still use internal `http://` URLs. Native TLS is handled by the destination application itself. Namespace policies control reachability but do not provide encryption. Application login and database credentials remain required independently of transport encryption.

| Connection | Current protection |
|---|---|
| Public client → Traefik | Plain HTTP. Public HTTPS is not configured yet. |
| Traefik → Sample; Sample → Nexus | Linkerd mTLS between the meshed pods. Backend application listeners use h2c. |
| Sample/Nexus → Graylog GELF input (`12201`) | Linkerd mTLS. The internal GELF URL remains `http://`. |
| Permitted meshed caller → Graylog UI/API (`9000`) | Linkerd mTLS. Graylog login/API authorization still applies. |
| Browser → Graylog through `kubectl port-forward` | Local HTTP carried through Kubernetes' authenticated port-forward tunnel. This path is not Linkerd mTLS. |
| Sample/Nexus → PostgreSQL (`5432`) | Native TLS is required. PostgreSQL is unmeshed. Stage/prod must use certificate verification and a trusted CA. |
| Graylog/Data Node → MongoDB (`27017`) | Native TLS with server certificate verification and SCRAM authentication. MongoDB and Data Node are unmeshed. |
| Graylog → Data Node API (`8999`) and OpenSearch (`9200`) | Native TLS using Data Node's automatically generated self-signed certificates. The chart enables this startup mode by default; manual CA provisioning is not required. This is distinct from a managed CA trust and renewal policy. |
| Kubelet → Sample/Nexus health listener (`8081`) | HTTP probes to the Pod IP, not workload-to-workload mTLS. The port is not published by a Service or Gateway. |

Certificate issuance and renewal have separate owners:

| Certificates | Issuance | Renewal in this setup |
|---|---|---|
| Linkerd workload identities | Linkerd issues short-lived certificates to its proxies. | Linkerd automatically rotates them. The default lifetime is 24 hours. |
| Linkerd issuer and admission webhooks | cert-manager issues them from the repository's trust anchor. | cert-manager renews them automatically: configured lifetime `24000h`, renewal `73h` before expiry. |
| Shared trust anchor | cert-manager creates the self-signed root used by Linkerd and MongoDB. | Configured lifetime `175200h`, renewal `87600h` before expiry. Root certificate renewal retains its private key (`rotationPolicy: Never`). Replacing the root key requires a coordinated trust rollout. |
| MongoDB server | cert-manager issues it from the shared trust anchor. trust-manager distributes the CA in PEM and Java PKCS12 formats to Graylog and Data Node. | Automatic renewal: 90-day lifetime, 30 days before expiry, with a new private key on reissuance. Restart the Java clients after CA trust changes. |
| Staging PostgreSQL | CloudNativePG generates its own CA and database certificates. | CloudNativePG automatically renews its managed certificates. Production database certificates belong to the separately provisioned database. |
| Data Node API and OpenSearch | Data Node generates self-signed certificates during automatic startup. | No automatic rotation policy is configured. The pinned Graylog version gives these certificates a validity of `99 × 365` days. Managed CA issuance and renewal remain outside the current beta scope. |

trust-manager distributes public trust material; it does not issue certificates. Existing controllers handle automatic renewal where noted, without custom renewal jobs. Staging mounts the operator's public PostgreSQL CA directly. Production CA copies in application Secrets remain administrator-managed. Roll out affected applications after database credential or CA rotation and verify new connections.

These are configured behaviors, not evidence of a verified running deployment. See [platform operations](docs/platform-operations.md) for startup, trust changes and runtime checks. Upstream references: [Linkerd mTLS](https://linkerd.io/docs/features/automatic-mtls/), [CloudNativePG certificates](https://cloudnative-pg.io/docs/devel/certificates/), [pinned Graylog chart startup setting](https://github.com/Graylog2/graylog-helm/blob/graylog-2.1.0/charts/graylog/templates/config/graylog.yaml), and [Data Node self-signed certificate lifetime](https://github.com/Graylog2/graylog2-server/blob/7.1.10/data-node/src/main/java/org/graylog/datanode/configuration/DatanodeKeystore.java).

### Post-quantum support in the pinned deployment

Post-quantum key exchange and post-quantum certificate authentication are separate capabilities. Our mesh already enables the former through Linkerd's upstream defaults. The deployment does not provide post-quantum certificates or post-quantum protection for every connection.

| Component / connection | Finding and action |
|---|---|
| Linkerd `edge-26.10.1`, proxy `v2.370.0` | The standard proxy's TLS 1.3 provider prefers hybrid `X25519MLKEM768`. It is already enabled for compatible meshed peers. Preserve this default; no extra Helm value is needed. Classical fallback remains supported. |
| Linkerd certificate authentication | The pinned proxy uses ECDSA P-256 signatures. Hybrid key exchange does not make these signatures post-quantum. |
| cert-manager `v1.21.2` | Its Certificate API supports RSA, ECDSA and Ed25519 keys, not post-quantum certificate keys. Keep the supported certificate configuration. |
| Graylog `7.1.10` native connections to MongoDB and Data Node | The published Graylog image uses Java 21. Upstream JSSE hybrid TLS key exchange arrives in JDK 27, so adding a named-group flag to this runtime is not an enablement path. Leave native TLS unchanged. |
| MongoDB `8.2.12-ubi8` | No supported end-to-end PQ setting was established for this pinned server and its Java clients. Linux MongoDB relies on OpenSSL; a newer MongoDB version number alone does not establish PQ support. |
| PostgreSQL `18.6`, `standard-trixie` image | The selected image uses Debian Trixie. Its runtime TLS library and end-to-end hybrid key exchange with the application clients have not been verified. The database version and OS change alone do not establish PQ support. |
| Public ingress | Traefik currently exposes HTTP, so there is no public TLS key exchange to make post-quantum. |

No image upgrades, custom cryptographic providers, extra sidecars or certificate algorithm changes were introduced for PQ support. Runtime libraries inside the pinned database images and actual negotiated connections have not been inspected in a running cluster. After deployment, check Linkerd's reported TLS key-exchange metrics for the hybrid group before claiming that a particular connection uses it. A replacement FIPS proxy build needs a separate check: the pinned source's FIPS branch lists classical groups only.

Evidence: the [pinned Linkerd proxy version](https://github.com/linkerd/linkerd2/blob/edge-26.10.1/.proxy-version) resolves to the [proxy crypto implementation](https://github.com/linkerd/linkerd2-proxy/blob/release/v2.370.0/linkerd/rustls/src/crypto.rs). See also the [cert-manager key API](https://github.com/cert-manager/cert-manager/blob/v1.21.2/pkg/apis/certmanager/v1/types_certificate.go), [Graylog image runtime](https://github.com/Graylog2/graylog-docker), [JDK 27 TLS enhancement](https://inside.java/2026/09/16/jdk27-security-enhancements/), [MongoDB TLS libraries](https://www.mongodb.com/docs/manual/core/security-transport-encryption/), and [OpenSSL 3.5 hybrid groups](https://docs.openssl.org/3.5/man3/SSL_CONF_cmd/).

## Continuous integration

| Workflow | Trigger | Checks |
|---|---|---|
| `pull-request` | PRs into `develop` or `master` | Trivy and CodeQL, without repository secrets |
| `develop` | Push to `develop` | Trivy, SonarCloud and CodeQL |
| `develop` | Calendar-shaped preview tag push | Trivy |
| `master` | Push to `master`, Sunday 13:29 UTC | Trivy, SonarCloud and CodeQL |
| `master` | Calendar-shaped non-preview tag push | Trivy |

Tag triggers are broad GitHub globs. Strict deployment tag validation belongs to the chart. Preview tags must point to commits on `develop`; final tags must point to commits on `master`. Verify branch membership before publishing because tag filters route scans without enforcing ancestry. CI neither creates tags nor deploys clusters. SonarCloud and CodeQL remain branch checks; deployment tags must identify commits whose checks passed. Tag scans cannot cancel branch scans.

Trivy scans vulnerabilities, secrets and infrastructure misconfigurations; HIGH/CRITICAL findings fail. SARIF uploads when available, including scan failures. SonarCloud requires `SONAR_TOKEN` and waits up to 600 seconds for its quality gate. CodeQL analyzes Actions without an application build. Existing actions remain SHA-pinned, checkouts disable persisted credentials, and jobs retain scoped permissions/timeouts. Dependabot targets `develop`.

Configure required checks, code-scanning gates, CODEOWNERS enforcement and immutable tag rulesets in repository settings. Static source review does not establish those external settings are active.

See [platform operations](docs/platform-operations.md), [staging configuration](docs/staging-configuration.md), and [planned work](todo.md).
