# DRN Project Argo CD GitOps



[![master](https://github.com/duranserkan/DRN-Project-Argo-CD-Gitops/actions/workflows/master.yml/badge.svg?branch=master)](https://github.com/duranserkan/DRN-Project-Argo-CD-Gitops/actions/workflows/master.yml)
[![develop](https://github.com/duranserkan/DRN-Project-Argo-CD-Gitops/actions/workflows/develop.yml/badge.svg?branch=develop)](https://github.com/duranserkan/DRN-Project-Argo-CD-Gitops/actions/workflows/develop.yml)
[![Docker Hub](https://img.shields.io/badge/images-2496ED?logo=docker&label=dockerhub
)](https://hub.docker.com/u/duranserkan)
[![Nuget](https://img.shields.io/badge/packages-004880?logo=nuget&label=nuget
)](https://www.nuget.org/profiles/duranserkan)
[![wiki](https://img.shields.io/badge/Doc-Awesome_Kubernetes-326CE5)](https://github.com/tomhuang12/awesome-k8s-resources)
[![wiki](https://img.shields.io/badge/Doc-Awesome_Argo-orange)](https://github.com/akuity/awesome-argo)


[![Security Rating](https://sonarcloud.io/api/project_badges/measure?project=duranserkan_DRN-Project-Argo-CD-Gitops&metric=security_rating)](https://sonarcloud.io/summary/new_code?id=duranserkan_DRN-Project-Argo-CD-Gitops)
[![Maintainability Rating](https://sonarcloud.io/api/project_badges/measure?project=duranserkan_DRN-Project-Argo-CD-Gitops&metric=sqale_rating)](https://sonarcloud.io/summary/new_code?id=duranserkan_DRN-Project-Argo-CD-Gitops)
[![Reliability Rating](https://sonarcloud.io/api/project_badges/measure?project=duranserkan_DRN-Project-Argo-CD-Gitops&metric=reliability_rating)](https://sonarcloud.io/summary/new_code?id=duranserkan_DRN-Project-Argo-CD-Gitops)
[![Vulnerabilities](https://sonarcloud.io/api/project_badges/measure?project=duranserkan_DRN-Project-Argo-CD-Gitops&metric=vulnerabilities)](https://sonarcloud.io/summary/new_code?id=duranserkan_DRN-Project-Argo-CD-Gitops)
[![Bugs](https://sonarcloud.io/api/project_badges/measure?project=duranserkan_DRN-Project-Argo-CD-Gitops&metric=bugs)](https://sonarcloud.io/summary/new_code?id=duranserkan_DRN-Project-Argo-CD-Gitops)
[![Lines of Code](https://sonarcloud.io/api/project_badges/measure?project=duranserkan_DRN-Project-Argo-CD-Gitops&metric=ncloc)](https://sonarcloud.io/summary/new_code?id=duranserkan_DRN-Project-Argo-CD-Gitops)

TL;DR: You can
* use [DRN.Framework](https://www.nuget.org/packages/DRN.Framework.Testing/#readme-body-tab) nuget packages to easily develop and test distributed reliable dotnet applications.
* use Argo CD GitOps to easily deploy your apps to a kubernetes cluster with Linkerd service mesh.
* use [Nexus App](https://hub.docker.com/r/duranserkan/drn-project-nexus) (not functional yet)
  * for service discovery
  * to get remote settings
  * to get unified microservices topology and their self-documentation
* Sample app is used for demonstration, replace it with actual services.
* Use this repository structure as boilerplate template for your actual projects
* Use DRN Project Pulumi GitOps to create an oracle cloud kubernetes cluster (not created yet)

[About Project, Kubernetes and Argo CD](#about-project-kubernetes-and-argo-cd) | [GitOps Roadmap](#gitops-roadmap) | [Tools](#tools) | [Deployment](#deployment)

## About Project, Kubernetes and Argo CD

The [Distributed Reliable .Net](https://github.com/duranserkan/DRN-Project?tab=readme-ov-file#drn-project) project is committed to following best practices, ensuring that the end result is always satisfying and worth the effort. While not adhering blindly, the project emphasizes the importance of implementing best practices, particularly evident in its Continuous Integration part of the DevSecOps pipeline.

It should do the same with Continuous Deployment and Delivery. However, deciding a good delivery and deployment practice for many cases and being reasonable upfront is challenging task. The solution must be flexible and complexity should be manageable. Therefore, Kubernetes is the obvious answer :)

To be honest, this project initially avoided and resisted Kubernetes due to its complexity and maintenance issues, despite admiring its flexibility and sense of control. However, despite these reservations, Kubernetes cannot be ignored. As a de-facto standard for cloud-native development, embracing Kubernetes is crucial to avoid vendor lock-in.

It was puzzling, like trying to find a generalized solution for [the Navier–Stokes equations.](https://en.wikipedia.org/wiki/Navier–Stokes_equations) Therefore, the solution can also be simplified if certain reasonable assumptions can be made, similar to how all true-hearted, humble engineers solve the Navier–Stokes equations under specific conditions.

* Use managed Kubernetes
* Use managed databases or services outside of cluster for stateful data.
* Don't apply manifests and configurations by yourself and version control your changes

[Argo CD](https://argo-cd.readthedocs.io/en/stable/) handles the final aspect through GitOps, while the first two assumptions aim to simplify the management of complexity and maintenance for small teams or projects.

## Continuous Integration

The workflows follow DRN-Project's branch separation and use composite actions under `.github/actions`.
This repository contains GitOps configuration, so application build, test, packaging and image publishing actions are not included.

| Workflow | Trigger | Checks |
|---|---|---|
| `pull-request` | PRs into `develop` or `master` | Trivy and CodeQL |
| `develop` | Push to `develop` | Trivy, SonarCloud and CodeQL |
| `master` | Push to `master`, plus Sunday at 13:29 UTC | Trivy, SonarCloud and CodeQL |

Trivy scans filesystem vulnerabilities, secrets and infrastructure misconfigurations. HIGH or CRITICAL findings fail the job. SARIF reports include those severities and are uploaded even when findings fail the scan, provided a report exists and the job has not been cancelled.
CodeQL analyzes GitHub Actions workflows using the `actions` language, without an application build.
SonarCloud uses `sonar-project.properties` and requires the `SONAR_TOKEN` repository secret.
PR jobs do not receive that secret and invoke pinned upstream scanner actions directly.
Branch workflows reuse local composite actions. Keep their scanner settings aligned with the PR workflow.
Checkouts disable persisted credentials, and each scanner has a separate job with scoped permissions and a timeout.
Dependabot checks GitHub Actions weekly and targets `develop`.

Configure branch rulesets to require the PR `trivy` and `codeql` checks and code-scanning results at the desired severity thresholds.
SonarCloud branch scans wait up to 600 seconds for the quality gate and fail if it fails or the wait times out.
Scheduled workflows run on the repository's default branch.

## Tools
High quality output is not a coincidence. It is natural result of good people's labour that works with right processes and tools.

This project recommends following tools for anyone who doesn't have strong Kubernetes experience as a starter pack. 

* [kube-ps1](https://github.com/jonmosco/kube-ps1)  - kube-ps1: A script that lets you add the current Kubernetes context and namespace configured on kubectl to your Bash/Zsh prompt strings (i.e. the $PS1).
* [kubectx + kubens](https://github.com/ahmetb/kubectx)  - `kubectx` helps you switch between clusters back and forth, and `kubens` helps you switch between Kubernetes namespaces smoothly.
* [Headlamp](https://headlamp.dev) is an open-source Kubernetes UI for inspecting resources, viewing logs, and troubleshooting clusters. Use the desktop app with your existing kubeconfig, and manage persistent configuration changes through Git and Argo CD.

## GitOps Roadmap
- [X] Minimal working pipeline
- [X] Linkerd support
- [X] Development environment support
  - [X] PostgreSQL with CloudNativePG
  - [X] Graylog
  - [ ] RabbitMQ
- [ ] Pulumi support to create an oracle cloud kubernetes cluster
- [ ] Kubernetes hardening practices
- [ ] [OpenTelemetry Collector and distributed tracing](todo.md#opentelemetry-collector-and-distributed-tracing)

## Deployment
The following deployment instructions are adapted from official documentation for DRN Project Argo CD GitOps. All instructions assume a clean installation.

**Install helm, argocd, kubeseal and linkerd CLIs**
```sh
brew install helm
brew install argocd
brew install kubeseal
brew install linkerd
```

### Deploy [Argo CD](https://argo-cd.readthedocs.io/en/stable/getting_started/)

Both examples pin [Helm chart `10.9.6`](https://artifacthub.io/packages/helm/argo/argo-cd), which installs Argo CD `v3.5.3`. Upstream lists Kubernetes `1.33`–`1.36` as [tested with Argo CD `3.5`](https://argo-cd.readthedocs.io/en/stable/operator-manual/installation/#tested-versions); this includes the repository's `1.36.2` baseline, but does not establish compatibility for the other deployed components.

Run from the repository root with `kubectl` configured for the intended cluster:

```sh
#https://artifacthub.io/packages/helm/argo/argo-cd
helm repo add argo https://argoproj.github.io/argo-helm
helm repo update
helm install argocd argo/argo-cd --version 10.9.6 -f infrastructure/argocd/custom-values.yaml --create-namespace -n argocd --wait --timeout 10m

# Alternative: use HA values instead of the command above (at least 3 worker nodes).
# helm install argocd argo/argo-cd --version 10.9.6 -f infrastructure/argocd/custom-values-ha.yaml --create-namespace -n argocd --wait --timeout 10m
```

Restrict the built-in `default` AppProject after installation. On an existing cluster, first move any Applications using `default` to an appropriate scoped project. All Applications supplied here use named projects.

```sh
kubectl apply -f infrastructure/argocd/default-project.yaml
```

**Login**

Keep port forwarding running in a separate terminal. Open the [browser UI](https://localhost:8080).

```sh
kubectl port-forward svc/argocd-server -n argocd 8080:443
```
**Initial password**

```sh
#change this after first login
argoPassword=`kubectl -n argocd get secret argocd-initial-admin-secret -o jsonpath="{.data.password}" | base64 -d`
```
**Argo CD CLI login**

Homebrew installs the current CLI; use the [matching `v3.5.3` release](https://github.com/argoproj/argo-cd/releases/tag/v3.5.3) when an exact client/server version match is required.

```sh
argocd login 127.0.0.1:8080 \
  --username=admin \
  --password="${argoPassword}" \
  --insecure
```

### Git repository sources

All Git-backed Applications and AppProject allowlists use `https://github.com/duranserkan/DRN-Project-Argo-CD-Gitops.git`. Workload Applications follow `develop`; infrastructure Git sources follow `preview`. Publish the intended manifests to the corresponding ref before syncing.

When using a fork or this repository as a template, complete these steps before applying any AppProject or Application manifests:

1. Replace the original Git repository URL in every Application `spec.source.repoURL` and AppProject `spec.sourceRepos` entry under `apps/` and `infrastructure/` with your repository URL. Include child Applications. Keep Helm chart repository URLs unchanged.
2. Publish the updated manifests to your repository's `develop` branch and `preview` ref. If you use different refs, update the Git-backed Applications' `targetRevision` values and publish to those refs instead.
3. For a private repository, configure Argo CD repository credentials before syncing.

Changing only your local Git remote does not change where Argo CD reads manifests. Unchanged Application URLs continue to deploy from the original repository.

### Deploy [Linkerd](https://linkerd.io/2-edge/tasks/gitops/)
> This page contains best-effort instructions by the open source community. Production users with mission-critical applications should familiarize themselves with [Linkerd production resources](https://docs.buoyant.io/runbook/getting-started/).

The manifests pin [Linkerd `edge-26.7.2`](https://github.com/linkerd/linkerd2/releases/tag/edge-26.7.2) through the upstream `https://helm.linkerd.io/edge` repository referenced by the [official Helm guide](https://linkerd.io/2-edge/tasks/install-helm/):

| Component | Chart version | App version |
|---|---|---|
| [Linkerd CRDs](https://artifacthub.io/packages/helm/linkerd2-edge/linkerd-crds/2026.7.2) | `2026.7.2` | Not specified by the chart |
| [Linkerd control plane](https://artifacthub.io/packages/helm/linkerd2-edge/linkerd-control-plane/2026.7.2) | `2026.7.2` | `edge-26.7.2` |
| [Linkerd Viz](https://artifacthub.io/packages/helm/linkerd2-edge/linkerd-viz/2026.7.2) | `2026.7.2` | `edge-26.7.2` |

These charts require Kubernetes `1.31` or later. This minimum does not establish tested compatibility with the repository's `1.36.2` baseline. Use the CLI from the matching release above and check `linkerd version --client`. Homebrew can install a different version.

**Sync cert-manager and trust-manager**

The parent `cert-manager` Application manages cert-manager and trust-manager from `https://charts.jetstack.io`, plus the repository's certificate bootstrap resources. Sealed Secrets is a separate Application. The chart pins are:

| Component | Pinned chart / controller | Purpose |
|---|---|---|
| [cert-manager](https://artifacthub.io/packages/helm/cert-manager/cert-manager) | `v1.21.2` / `v1.21.2` | Issues and renews certificates; CRDs are enabled with `crds.enabled=true`. |
| [trust-manager](https://artifacthub.io/packages/helm/cert-manager/trust-manager) | `v0.24.0` / `v0.24.0` | Distributes CA trust bundles from the `cert-manager` trust namespace. |
| [Sealed Secrets](https://artifacthub.io/packages/helm/bitnami-labs/sealed-secrets) | `2.20.0` / `v0.40.0` | Decrypts committed `SealedSecret` resources into Kubernetes Secrets. |

cert-manager `1.21` [supports Kubernetes `1.33`–`1.36`](https://cert-manager.io/docs/releases/), including this repository's `1.36.2` baseline. Kubernetes `1.36` compatibility for trust-manager and Sealed Secrets remains unverified; confirm it before deployment.

cert-manager creates the Linkerd root CA from [the bootstrap resources](infrastructure/cert-manager/resources/linkerd-trust-anchor.yaml). The Linkerd `Bundle` then uses trust-manager to distribute the public CA as `linkerd-identity-trust-roots` ConfigMaps under `ca-bundle.crt`. Root key rotation is disabled by default with `privateKey.rotationPolicy: Never`. The root certificate still renews using the same key. [Coordinated root key rotation](todo.md#linkerd-root-key-rotation) is planned work.

Promote the intended commit to the `preview` tag before applying the parent Application. Its Git sources follow that tag in this repository, so changes on `develop` are not deployed until promoted. Sync wave `0` installs cert-manager; wave `1` creates trust-manager and the certificate bootstrap Application after cert-manager is healthy. This ordering uses the Application health customization in the Argo CD values above.

```sh
kubectl apply -f infrastructure/cert-manager/cert-manager-project.yaml
kubectl apply -f infrastructure/cert-manager/cert-manager.yaml
argocd app sync cert-manager
argocd app wait cert-manager --sync --health --timeout 300
argocd app wait cert-manager-chart trust-manager cert-manager-bootstrap --sync --health --timeout 300
kubectl -n cert-manager wait --for=condition=Ready certificate/linkerd-trust-anchor --timeout=120s
```

**Sync Sealed Secrets (optional)**

The chart uses `https://bitnami.github.io/sealed-secrets` and installs in `kube-system`. Sealed Secrets is optional for this certificate bootstrap because cert-manager generates its certificate Secrets. Use it to store encrypted application Secrets in Git; never commit plaintext Secret values.

```sh
kubectl apply -f infrastructure/sealed-secrets/sealed-secrets-project.yaml
kubectl apply -f infrastructure/sealed-secrets/sealed-secrets.yaml
argocd app sync sealed-secrets
argocd app wait sealed-secrets --sync --health --timeout 300
```

The Helm release uses controller name `sealed-secrets`, while `kubeseal` defaults to `sealed-secrets-controller`. Specify the controller explicitly when fetching its public certificate or sealing Secrets:

```sh
kubeseal --controller-name sealed-secrets --controller-namespace kube-system --fetch-cert
```

**Sync shared Gateway API CRDs**

[Gateway API Standard `v1.5.1`](https://github.com/kubernetes-sigs/gateway-api/releases/tag/v1.5.1) is managed by a separate Application and is required before Linkerd or Traefik. This pin stays within [Linkerd's documented Gateway API range](https://linkerd.io/2-edge/features/gateway-api/). The Application follows `preview`, uses server-side apply and disables automatic pruning of these shared resources. Promote the intended commit to that ref in the configured Git repository before syncing.

Gateway API and Traefik use the [networking AppProject](infrastructure/networking/networking-project.yaml). It permits the cluster-scoped CRDs, admission policies, RBAC and GatewayClass required by those components. The development AppProject manages workloads and optional PostgreSQL, with only Namespace resources allowed at cluster scope. HTTPRoutes remain owned by their workload Applications.

```sh
kubectl apply -f infrastructure/networking/networking-project.yaml
kubectl apply -f infrastructure/gateway-api/gateway-api.yaml
argocd app sync gateway-api
argocd app wait gateway-api --sync --health --timeout 300
```

**Sync Linkerd**

The parent and certificate bootstrap Applications follow `preview`. Linkerd uses the cert-manager CA, issuer Secret and webhook certificates. Sync waves install the bootstrap resources, Linkerd CRDs and control plane in that order.

cert-manager injects the CA bundles into the proxy injector, service profile validator and policy validator webhook configurations. The control-plane Application excludes those fields from drift comparison and uses [`RespectIgnoreDifferences=true`](https://argo-cd.readthedocs.io/en/stable/user-guide/sync-options/#respect-ignore-differences-configs) to preserve their live values during subsequent syncs. On initial creation, cert-manager populates the CA bundles after the webhook configurations exist.

```sh
kubectl apply -f infrastructure/linkerd/linkerd-project.yaml
kubectl apply -f infrastructure/linkerd/linkerd.yaml
argocd app sync linkerd
argocd app wait linkerd --sync --health --timeout 300
argocd app wait linkerd-bootstrap linkerd-crds linkerd-control-plane --sync --health --timeout 300
linkerd check
linkerd version
```

**Sync Linkerd Viz (Optional)**
```sh
kubectl apply -f infrastructure/linkerd-viz/linkerd-viz-project.yaml
kubectl apply -f infrastructure/linkerd-viz/linkerd-viz.yaml
argocd app sync linkerd-viz
argocd app wait linkerd-viz --sync --health --timeout 300
linkerd viz check
linkerd viz dashboard &
```

Linkerd and Viz use `ghcr.io/linkerd` directly to avoid the `cr.l5d.io` image-pull failures observed with Docker Desktop's kind registry mirror. The image digests match across both registries. This is configured through Helm values and requires no node configuration changes.

**Sync Grafana (Optional)**

Grafana is installed separately from Viz. The [Grafana Application](infrastructure/grafana/grafana.yaml) provisions Linkerd dashboards and authorizes its meshed service account to query Viz's Prometheus.

```sh
kubectl apply -f infrastructure/grafana/grafana-project.yaml
kubectl apply -f infrastructure/grafana/grafana.yaml
kubectl apply -f infrastructure/linkerd-viz/linkerd-viz.yaml
argocd app wait grafana linkerd-viz --sync --health --timeout 300
linkerd viz dashboard
```

Open `/grafana/` on the dashboard's local URL. Access is anonymous and read-only, with no public ingress or admin account. Dashboards are provisioned from pinned revisions at startup and local changes are not persisted. Configure authentication before exposing Grafana outside the development cluster.

### Deploy [Traefik Gateway API](https://github.com/traefik/traefik/blob/v3.7.13/docs/content/reference/install-configuration/providers/kubernetes/kubernetes-gateway.md)

The official [Traefik chart `41.6.1`](https://artifacthub.io/packages/helm/traefik/traefik/41.6.1) installs Traefik `v3.7.13` from `https://traefik.github.io/charts`. The Traefik image is pinned by digest for reproducible deployments. `versionOverride: v3.7.13` lets the chart check version compatibility when using that digest. It creates the `traefik` GatewayClass and `drn-project` HTTP Gateway in `drn-project-develop`. Only the Gateway API provider is enabled. Traefik-specific CRDs and Ingress resources are disabled.

Install it after Gateway API and Linkerd are healthy. Its pods use normal Linkerd injection and `nativeLBByDefault` routes through Service IPs, following Linkerd's [Service-based ingress integration](https://linkerd.io/docs/tasks/using-ingress/#ingress-details). The pinned Traefik `v3.7.13` documentation targets Gateway API `1.6.1`. Its watched resource versions exist in the pinned `1.5.1` bundle, but this exact combination has not been validated on a cluster. Check Gateway and HTTPRoute status and traffic after installation.

```sh
kubectl apply -f infrastructure/networking/networking-project.yaml
kubectl apply -f infrastructure/traefik/traefik.yaml
argocd app sync traefik
argocd app wait traefik --sync --health --timeout 300
kubectl -n drn-project-develop wait --for=condition=Programmed gateway/drn-project --timeout=120s
```

The chart exposes HTTP on LoadBalancer port `80`, forwarding to its `web` listener on port `8000`. Configure HTTPS and certificates before exposing production traffic. The [sample HTTPRoute](services/sample/base/httproute.yaml) is managed by the sample Application alongside its Service and Deployment. The shared Gateway and GatewayClass remain managed by the Traefik infrastructure Application. The route sends `/api` and `/api/` to `/`, and `/api/example` to `/example` on `sample:80`. Prefix matching is case-sensitive and excludes `/apix`. It preserves `X-Forwarded-Prefix: /api`. Incoming `l5d-dst-override`, `X-Forwarded-Ssl`, `X-Original-URI` and `X-Forwarded-Path` headers are stripped. Applications should use standard forwarded headers for request metadata.

### Deploy Dev Environment Dependencies

**Sync PostgreSQL with CloudNativePG (Optional)**

The supplied Sample and Nexus overlays use this database. To use an external PostgreSQL service instead, configure their connection settings and password Secret before deploying the apps. Managed database services remain the recommended production choice.

The [CloudNativePG operator Application](infrastructure/cloudnative-pg/cloudnative-pg.yaml) pins [chart `0.29.1` on Artifact Hub](https://artifacthub.io/packages/helm/cloudnative-pg/cloudnative-pg/0.29.1) and operator `1.30.1`. The chart is also available from the [official GitHub release](https://github.com/cloudnative-pg/charts/releases/tag/cloudnative-pg-v0.29.1). Its dedicated [AppProject](infrastructure/cloudnative-pg/cloudnative-pg-project.yaml) permits the CRDs, RBAC and admission webhooks required by the operator. The chart installs in `cnpg-system` and manages database clusters across namespaces. CloudNativePG `1.30` [supports Kubernetes `1.34` through `1.36`](https://cloudnative-pg.io/docs/1.30/supported_releases/), including this repository's `1.36.2` baseline.

The [development database Application](apps/postgresql-develop.yaml) belongs to `drn-project-develop` and reads [its namespaced Cluster configuration](services/postgresql/develop/cluster.yaml). It is installed separately from the workload parent so the dependency stays optional. It follows `develop`; publish the intended manifests to that ref before syncing.

The database uses PostgreSQL `16.15`, one instance, and an `8Gi` volume from the default StorageClass. A default storage provisioner is required. Set `spec.storage.storageClass` if your cluster needs an explicit class. The operator and PostgreSQL images are pinned by multi-platform digest for `linux/amd64` and `linux/arm64`. Their digests were verified against GHCR manifests, and the chart checksum was verified against the [official chart index](https://cloudnative-pg.github.io/charts/index.yaml).

Install the operator and wait for its CRD and webhook server before creating the database:

```sh
kubectl apply -f infrastructure/cloudnative-pg/cloudnative-pg-project.yaml
kubectl apply -f infrastructure/cloudnative-pg/cloudnative-pg.yaml
argocd app sync cloudnative-pg
argocd app wait cloudnative-pg --sync --health --timeout 300
kubectl wait --for=condition=Established crd/clusters.postgresql.cnpg.io --timeout=120s
kubectl -n cnpg-system rollout status deployment/cloudnative-pg --timeout=300s

kubectl apply -f apps/develop-project.yaml
kubectl apply -f apps/postgresql-develop.yaml
argocd app sync postgresql-develop
argocd app wait postgresql-develop --sync --timeout 300
kubectl -n drn-project-develop wait --for=condition=Ready cluster.postgresql.cnpg.io/postgresql --timeout=600s
```

Bootstrap creates the `drnDb` database owned by the non-superuser role `drn`. CloudNativePG generates the `postgresql-app` Secret with the application's `password` key and exposes the primary through `postgresql-rw:5432`. Both apps mount that key at `/appconfig/key-per-file-settings/postgres-password` and set the framework's `DrnContext_DevHost`, `DrnContext_DevPort`, `DrnContext_DevUsername` and `DrnContext_DevDatabase` settings explicitly. See [CloudNativePG application connections](https://cloudnative-pg.io/docs/1.30/applications/). No database passwords are stored in Git. Restart the app Deployments after rotating credentials because their `subPath` mounts retain the mounted value.

This development configuration has no replicas or scheduled backups. It disables the database PodDisruptionBudget so node drains can proceed, with database downtime expected. Linkerd injection is disabled for the operator and database resources. Superuser network access is disabled. Both Applications enable self-healing and disable automatic pruning; the database Cluster also has `Prune=false,Delete=false` to retain it during Argo CD pruning or Application deletion. Configure and verify backups, recovery and availability before using self-managed PostgreSQL for production.

**Sync Graylog (Optional)**

For **fresh development installations only**, using official charts and Graylog Data Node. Existing databases and log indices require a separate migration.

| Component | Chart | Deployed version | Official repository |
|---|---|---|---|
| [Graylog Open and Data Node](https://artifacthub.io/packages/helm/graylog2/graylog/2.1.0) | `2.1.0` | Both `7.1.9` | `https://graylog2.github.io/graylog-helm` |
| [MongoDB Controllers for Kubernetes](https://artifacthub.io/packages/helm/mongodb-helm-charts/mongodb-kubernetes/1.13.0) | `1.13.0` | Operator `1.13.0` | `https://mongodb.github.io/helm-charts` |
| MongoDB Community Server | Separate `MongoDBCommunity` resource | `8.2.12` | `quay.io/mongodb/mongodb-community-server` |

MongoDB stays within Graylog's [supported `8.2` series](https://go2docs.graylog.org/current/downloading_and_installing_graylog/compatibility_matrix.htm). Images are digest-pinned for amd64 and arm64. Linkerd injection is disabled, and Data Node manages OpenSearch internally.

The database's [readiness role](infrastructure/graylog/mongodb/readiness-rbac.yaml) lets its probe read `mongodb-config` and update the version annotation on `mongodb-0`. Extend its pod-name list when adding members.

**Storage and host prerequisites**

- Kubernetes `1.32+` and hosts meeting MongoDB's [platform requirements](https://www.mongodb.com/docs/manual/administration/production-notes/).
- A default StorageClass supporting ReadWriteOnce: MongoDB `8Gi` data + `2Gi` logs, Graylog `8Gi`, Data Node `8Gi`.
- Host `vm.max_map_count >= 262144`. The chart does not configure it.
- Memory for Graylog `1Gi`, Data Node `3.5Gi`, MongoDB `1Gi`, plus agent/operator overhead. These are requests, not usage limits.

**Install the operator and database**

Publish the intended manifests to `preview` before syncing the Git-backed database Application. Install the operator and wait for its CRD before creating MongoDB:

```sh
kubectl apply -f infrastructure/graylog/graylog-project.yaml
kubectl apply -f infrastructure/graylog/mongodb-operator.yaml
argocd app sync mongodb-operator
argocd app wait mongodb-operator --sync --health --timeout 300
kubectl wait --for=condition=Established crd/mongodbcommunity.mongodbcommunity.mongodb.com --timeout=120s
kubectl -n graylog rollout status deployment/mongodb-kubernetes-operator --timeout=300s
```

Sync MongoDB with a **full sync**. Its bootstrap Job generates credentials, waits for the operator's connection URI, and creates the chart's [external Secret](https://github.com/Graylog2/graylog-helm/blob/graylog-2.1.0/docs/graylog-secrets.md). No manual Secret creation is needed. [Selective sync skips hooks](https://argo-cd.readthedocs.io/en/stable/user-guide/resource_hooks/#selective-sync).

```sh
kubectl apply -f infrastructure/graylog/mongodb.yaml
argocd app sync mongodb
argocd app wait mongodb --sync --timeout 1200
kubectl -n graylog wait --for=jsonpath='{.status.phase}'=Running mongodbcommunity/mongodb --timeout=600s
```

Later full syncs preserve existing Secrets. Back them up with the database. The Job does not rotate credentials or recover deleted Secrets for existing data.

**Sync Graylog**

Retrieve the generated admin password in a private terminal and save it securely. Never paste it into Git or shared output. If you supplied existing Graylog credentials, use their original password.

```sh
kubectl -n graylog get secret graylog-admin-password \
  -o jsonpath='{.data.password}' | base64 -d
```

```sh
kubectl apply -f infrastructure/graylog/graylog.yaml
argocd app sync graylog
argocd app wait graylog --sync --timeout 300
kubectl -n graylog port-forward svc/drn-graylog 9000:9000
```

Keep the port-forward running and open [Graylog](http://localhost:9000). Complete Data Node provisioning using the setup credentials in pod logs if prompted, then log in as `admin` with your saved password. Update `graylog.config.network.externalUri` if exposing another URL.

Graylog installs the **DRN GELF HTTP** global input on `0.0.0.0:12201` at startup from the bundled content pack. Bulk receiving supports Sample.Hosted and DRN.Nexus.Hosted batches. Its JSON extractor preserves `Logs` and exposes scoped properties as `scope_TraceId`, `scope_EventName`, and other `scope_` fields. The same pack revision is installed only once. Remove any manually created HTTP input on this port before enabling the pack. Set index replicas to `0` for the single Data Node.

Sync Graylog and confirm the input is running under System / Inputs before syncing Sample and Nexus. Both send logs to `http://drn-graylog.graylog:12201/gelf` with LF separators and compression disabled for bulk decoding. After verifying HTTP delivery, remove any previous UDP or Forwarder inputs in Graylog. Graylog is not meshed, so this connection does not use Linkerd mTLS.

Existing installations using `drn-gelf` need a StatefulSet recreation with pods and PVCs preserved, because its `serviceName` is immutable. Keep the old Service until log clients use `drn-graylog`.

```sh
argocd app wait graylog --sync --health --timeout 600
kubectl -n graylog get pods,pvc
```

Verify a received Sample or Nexus message in the UI. This single-instance stack has no configured backups and incurs maintenance downtime. Automatic pruning is disabled, and MongoDB is protected from Argo CD deletion. Restart Graylog and Data Node after credential rotation.

### Deploy Sample and Nexus Apps

Sample and Nexus use digest-pinned `0.10.1-preview001` images for amd64 and arm64. Containers run as non-root with read-only root filesystems. Writable data and logs use `emptyDir` volumes and are lost when pods are removed.

Development settings use the shared PostgreSQL service with automatic migrations enabled and prototype mode disabled. App IDs are explicit: Sample `0`, Nexus `126`, and instance ID `0` for both. Logs go to console, file, and Graylog HTTP, with category filters inherited from the images.

Argo CD Application declarations live under `apps/`. App-specific workload manifests and HTTPRoutes live together under `services/`. Service bases do not declare a namespace. Each environment overlay owns that choice, so deploy through an overlay rather than applying a base directly. The [sample Application](apps/develop/services/sample/application.yaml) reads `services/sample/develop`, whose `drn-project-develop` namespace applies to its HTTPRoute as well as its workloads. Nexus uses the same namespace through its own development overlay. These Git sources follow `develop`, so publish the changes to that ref before syncing.

The parent applies Nexus in sync wave `-1` and Sample in wave `0`, using `argocd.argoproj.io/sync-wave` annotations on the child Applications. The Application health customization above makes the parent wait for Nexus to become healthy before advancing to Sample. These waves order a parent sync; they do not serialize independent child Application auto-syncs. Linkerd uses bootstrap, CRD and control-plane waves `0`, `1` and `2`.

Ensure PostgreSQL is ready, then create the parent Application:

```sh
kubectl apply -f apps/develop-project.yaml
kubectl apply -f apps/develop.yaml
```

Sync the Applications:

```sh
argocd app sync drn-project-develop
argocd app sync sample
```

After the sample Service and Gateway are ready, verify that the route reports `Accepted=True` and `ResolvedRefs=True` for the `drn-project` parent:

```sh
argocd app wait sample --sync --health --timeout 300
kubectl -n drn-project-develop get httproute sample -o yaml
kubectl -n drn-project-develop get gateway drn-project
linkerd check --proxy
```

Exercise `/api`, `/api/` and a known sample endpoint through the gateway address. Check backend paths and forwarded headers.
