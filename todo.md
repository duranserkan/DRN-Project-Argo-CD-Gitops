# TODO

## Linkerd root key rotation

Root key rotation is disabled by default. Introduce a coordinated procedure before enabling it. Certificate renewal continues using the existing root key.

- [ ] Retain the previous public root and distribute overlapping trust containing both roots. Keep private keys out of the retained public bundle.
- [ ] Coordinate root rotation with control-plane and meshed workload rollouts. Check replicas, readiness, rollout strategies and capacity before claiming uninterrupted availability.
- [ ] Renew the Linkerd identity issuer, webhook certificates and any other dependent certificates after every consumer trusts both roots. Prevent automatic renewals from racing ahead of trust propagation.
- [ ] Verify certificate adoption, webhook admission, mTLS and application traffic before removing the old root. Include external and multicluster consumers when present.
- [ ] Document bootstrap, rotation order, recovery and root expiry monitoring in the README and a runbook. Preserve overlapping trust if any verification fails.
- [ ] Validate manifests statically, then, with explicit execution approval, verify rotation and recovery in a non-production cluster before enabling the procedure.

## OpenTelemetry Collector and distributed tracing

Introduce tracing through an OpenTelemetry Collector. This is planned work, not an installed capability. The Collector receives, processes, and exports traces; a separate backend must provide storage and a trace UI.

- [ ] Check the instrumentation and OTLP configuration supported by the deployed Nexus and Sample images before defining the deployment contract.
- [ ] Add any missing OpenTelemetry instrumentation in the application repository for ASP.NET Core requests, outgoing HTTP requests, and database calls. Propagate trace context between services and assign distinct service names.
- [ ] Deploy the OpenTelemetry Collector through Argo CD with a scoped AppProject, explicit resource requests and limits, health checks, and OTLP receivers. Document endpoint, protocol, and Secret requirements for producers and exporters.
- [ ] Configure Nexus and Sample to export traces to the Collector through their supported application settings.
- [ ] Configure Linkerd proxy tracing to export spans to the Collector. Verify the installed Linkerd release's requirements for collector meshing and identity, and document the required workload rollout.
- [ ] Select and deploy an independently managed tracing backend for development. Evaluate Jaeger or another OTLP-compatible backend. Define retention, storage, and UI access.
- [ ] Set sampling and sensitive-data handling policies before enabling tracing. Keep Graylog for logs; metrics collection is outside this task.
- [ ] Validate manifests statically, then, with explicit execution approval, verify that a request through Sample and Nexus produces a connected trace containing application, database, and Linkerd proxy spans in the selected backend.
- [ ] Document deployment order, configuration, troubleshooting, and cleanup in the README once implemented.
