# DRN Project ArgoCD GitOps



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

[About Project, Kubernetes and ArgoCD](#about-project) | [GitOps Roadmap](#gitops-roadmap) | [Tools](#tools) | [Deployment](#deployment)

## About Project, Kubernetes and ArgoCD

The [Distributed Reliable .Net](https://github.com/duranserkan/DRN-Project?tab=readme-ov-file#drn-project) project is committed to following best practices, ensuring that the end result is always satisfying and worth the effort. While not adhering blindly, the project emphasizes the importance of implementing best practices, particularly evident in its Continuous Integration part of the DevSecOps pipeline.

It should do the same with Continuous Deployment and Delivery. However, deciding a good delivery and deployment practice for many cases and being reasonable upfront is challenging task. The solution must be flexible and complexity should be manageable. Therefore, Kubernetes is the obvious answer :)

To be honest, this project initially avoided and resisted Kubernetes due to its complexity and maintenance issues, despite admiring its flexibility and sense of control. However, despite these reservations, Kubernetes cannot be ignored. As a de-facto standard for cloud-native development, embracing Kubernetes is crucial to avoid vendor lock-in.

It was puzzling, like trying to find a generalized solution for [the Navier–Stokes equations.](https://en.wikipedia.org/wiki/Navier–Stokes_equations) Therefore, the solution can also be simplified if certain reasonable assumptions can be made, similar to how all true-hearted, humble engineers solve the Navier–Stokes equations under specific conditions.

* Use managed Kubernetes
* Use managed databases or services outside of cluster for stateful data.
* Don't apply manifests and configurations by yourself and version control your changes

ArgoCD elegantly handles the final aspect through GitOps, while the first two assumptions aim to simplify the management of complexity and maintenance for small teams or projects.

## Tools
High quality output is not a coincidence. It is natural result of good people's labour that works with right processes and tools.

This project recommends following tools for anyone who doesn't have strong Kubernetes experience as a starter pack. 

* [kube-ps1](https://github.com/jonmosco/kube-ps1)  - kube-ps1: A script that lets you add the current Kubernetes context and namespace configured on kubectl to your Bash/Zsh prompt strings (i.e. the $PS1).
* [kubectx + kubens](https://github.com/ahmetb/kubectx)  - `kubectx` helps you switch between clusters back and forth, and `kubens` helps you switch between Kubernetes namespaces smoothly.
* [OpenLens](https://github.com/MuhammedKalkan/OpenLens) - Lens it's an useful, attractive, open source user interface (UI) for working with Kubernetes clusters.

## GitOps Roadmap
- [X] Minimal working pipeline
- [X] Linkerd support
- [X] Development environment support
  - [X] Postgres
  - [X] Graylog
  - [ ] RabbitMQ
- [ ] Pulumi support to create an oracle cloud kubernetes cluster
- [ ] Kubernetes hardening practices

## Deployment
Following deployment instructions gathered together from official documentations and refactored for DRN Project ArgoCD GitOps.

**Install helm, step, kubeseal and linkerd CLIs**
```
brew install helm
brew install step
brew install kubeseal
brew install linkerd
```

### Deploy [Argo CD](https://argo-cd.readthedocs.io/en/stable/getting_started/)
**Install**
```
#https://artifacthub.io/packages/helm/argo/argo-cd
helm repo add argo https://argoproj.github.io/argo-helm
helm repo update
helm install argocd argo/argo-cd --version 7.6.5 -f infrastructure/argocd/custom-values.yaml --create-namespace -n argocd

#At least 3 worker nodes for High Availability is needed
#helm install argocd argo/argo-cd --version 7.6.5 -f infrastructure/argocd/custom-values-ha.yaml --create-namespace -n argocd
```
**Login**

**Browser UI - https://localhost:8080**
```
kubectl port-forward svc/argocd-server -n argocd 8080:443
```
**Initial password**
```
#change this after first login
argoPassword=`kubectl -n argocd get secret argocd-initial-admin-secret -o jsonpath="{.data.password}" | base64 -d`
```
**ArgoCD cli login**
```
argocd login 127.0.0.1:8080 \
  --username=admin \
  --password="${argoPassword}" \
  --insecure 
```

### Deploy [Linkerd](https://linkerd.io/2.14/tasks/gitops/)
> This page contains best-effort instructions by the open source community. Production users with mission-critical applications should familiarize themselves with [Linkerd production resources](https://docs.buoyant.io/runbook/getting-started/).

**Sync cert-manager**
> cert-manger configuration refactored to create root-ca automatically. However, [Official guide](https://linkerd.io/2.14/tasks/gitops/) uses the [step cli](https://smallstep.com/docs/step-cli/installation/) to create root-ca.
```
kubectl apply -f infrastructure/cert-manager/cert-manager-project.yaml
kubectl apply -f infrastructure/cert-manager/cert-manager.yaml
argocd app sync cert-manager
```

**Sync sealed-secrets**
> [Sealed Secrets](https://github.com/bitnami-labs/sealed-secrets) used for certificate encryption by [Official guide](https://linkerd.io/2.14/tasks/gitops/). This guide doesn't need this. However, it can be used to encrypt other secrets.
```
kubectl apply -f infrastructure/sealed-secrets/sealed-secrets-project.yaml
kubectl apply -f infrastructure/sealed-secrets/sealed-secrets.yaml
argocd app sync sealed-secrets
```

**Sync Linkerd**
```
kubectl apply -f infrastructure/linkerd/linkerd-project.yaml
kubectl apply -f infrastructure/linkerd/linkerd.yaml
argocd app sync linkerd
linkerd check
```

**Sync Linkerd Jaeger (Optional)**
```
kubectl apply -f infrastructure/linkerd-jaeger/linkerd-jaeger-project.yaml
kubectl apply -f infrastructure/linkerd-jaeger/linkerd-jaeger.yaml
argocd app sync linkerd-jaeger
linkerd jaeger check
linkerd jaeger dashboard &
```

**Sync Linkerd Viz (Optional)**
```
kubectl apply -f infrastructure/linkerd-viz/linkerd-viz-project.yaml
kubectl apply -f infrastructure/linkerd-viz/linkerd-viz.yaml
argocd app sync linkerd-viz
linkerd viz check
linkerd viz dashboard &
```

### Install [Ingress NGINX Controller](https://github.com/kubernetes/ingress-nginx) for Docker Desktop
```
kubectl apply -f apps/develop-project.yaml
kubectl apply -f infrastructure/ingress-nginx-controller/ingress-nginx-controller.yaml
argocd app sync ingress-nginx-controller
```

### Deploy Dev Environment Dependencies

**Sync Postgresql (Optional)**
> **Don't use this approach in production.** Instead, use managed database services for production. For this, PV provisioner support in the underlying infrastructure is required.
```
kubectl apply -f apps/develop-project.yaml
kubectl apply -f infrastructure/postgresql/postgresql.yaml
argocd app sync postgresql
```

**Sync Graylog (Optional)**
> **Don't use this approach in production.** 
> Instead, use managed database services for production. 
> For this, PV provisioner support in the underlying infrastructure is required.
> This configuration uses multi-platform Mongodb images that support **Arm 64-bit architecture**.
```
kubectl apply -f infrastructure/graylog/graylog-project.yaml
kubectl create namespace graylog
kubectl create secret generic mongo-admin-password \
--from-literal=password=yourpassword \
--namespace graylog

kubectl apply -f infrastructure/graylog/mongodb-operator.yaml
kubectl apply -f infrastructure/graylog/mongodb.yaml
argocd app sync mongodb
```

```
kubectl apply -f infrastructure/graylog/graylog.yaml
argocd app sync graylog
```
### Deploy Sample and Nexus Apps

```
kubectl apply -f apps/develop-project.yaml
kubectl apply -f apps/develop.yaml
argocd app sync drn-project-develop
```