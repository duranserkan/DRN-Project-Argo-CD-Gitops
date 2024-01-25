# DRN Project ArgoCD GitOps

[![wiki](https://img.shields.io/badge/Doc-Awesome_Kubernetes-blue)](https://github.com/tomhuang12/awesome-k8s-resources)
[![wiki](https://img.shields.io/badge/Doc-Awesome_Argo-orange)](https://github.com/akuity/awesome-argo)

[Distributed Reliable .Net](https://github.com/duranserkan/DRN-Project) project aims to provide somewhat opinionated design and out of the box solutions to enterprise application development.
Expected result is spending less time on wiring while getting better maintainability and observability.
The project benefits from the best practices, open source solutions and personal production experience.
This project is about managing software complexity, architecting good solutions and a promise to deliver a good software.

## About Kubernetes, ArgoCD and GitOps
The DRN Project is committed to following best practices, ensuring that the end result is always satisfying and worth the effort. While not adhering blindly, the project emphasizes the importance of implementing best practices, particularly evident in its Continuous Integration part of the DevSecOps pipeline.

It should do the same with Continuous Deployment and Delivery. However, deciding a good delivery and deployment practice for many cases and being reasonable upfront is challenging task. The solution must be flexible and complexity should be manageable. Therefore, Kubernetes is the obvious answer :)

To be honest, this project initially avoided and resisted Kubernetes due to its complexity and maintenance issues, despite admiring its flexibility and sense of control. However, despite these reservations, Kubernetes cannot be ignored. As a de-facto standard for cloud-native development, embracing Kubernetes is crucial to avoid vendor lock-in.

It was puzzling, like trying to find a generalized solution for [the Navier–Stokes equations.](https://en.wikipedia.org/wiki/Navier–Stokes_equations) Therefore, the solution can also be simplified if certain reasonable assumptions can be made, similar to how all true-hearted, humble engineers solve the Navier–Stokes equations under specific conditions.

* Use managed Kubernetes
* Use managed databases or services outside of cluster for stateful data.
* Don't apply manifests and configurations by yourself and version control your changes

ArgoCD elegantly handles the final aspect through GitOps, while the first two assumptions aim to simplify the management of complexity and maintenance for small teams or projects.

## About Tools
High quality output is not a coincidence. It is natural result of good people's labour that works with right processes and tools.

This project recommends following tools for anyone who doesn't have strong Kubernetes experience as a starter pack. 

* [kube-ps1](https://github.com/jonmosco/kube-ps1)  - kube-ps1: A script that lets you add the current Kubernetes context and namespace configured on kubectl to your Bash/Zsh prompt strings (i.e. the $PS1).
* [kubectx + kubens](https://github.com/ahmetb/kubectx)  - `kubectx` helps you switch between clusters back and forth, and `kubens` helps you switch between Kubernetes namespaces smoothly.
* [OpenLens](https://github.com/MuhammedKalkan/OpenLens) - Lens it's an useful, attractive, open source user interface (UI) for working with Kubernetes clusters.


## About Deployment Roadmap
- [X] Minimal working pipeline
- [ ] Linkerd support
- [ ] Postgres and Rabbit Operators for development environment
- [ ] Kubernetes hardening practices

## About Deployment

To be continued...