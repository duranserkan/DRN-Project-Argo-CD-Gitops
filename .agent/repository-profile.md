# Repository profile

## Scope and source map

This repository contains Argo CD GitOps configuration for DRN Framework Sample and Nexus. Application source and image publication live in the sibling DRN-Project repository and are outside this repository's write scope.

Cloud provisioning belongs to infrastructure tooling such as [DRN-Project-Pulumi-Ops](https://github.com/duranserkan/DRN-Project-Pulumi-Ops), which implements OCI recovery. This repository owns provider-neutral GitOps workload configuration. The staging-only `retained` profile accepts an externally provisioned storage class and requires checkpointed identities, staged activation and native backups. Runtime acceptance remains pending.

- `apps/bootstrap-project.yaml`: administrator-owned root project.
- `apps/{staging,production}/application.yaml`: one manually synced root per separate cluster.
- `deployment/`: root Helm chart rendering scoped projects, shared prerequisites and parallel branch Applications. `deployment/{logging,monitoring,workloads}/` are independent Helm charts owning their leaf Applications.
- `infrastructure/`: platform resource payloads and Argo CD installation values.
- Keep standalone Kubernetes resources in separate files in their existing component directory, with descriptive names and kind suffixes where needed. Keep chart-owned `extraObjects` in Helm values. File organization must preserve Application source paths, resource identities, ownership and sync waves.
- `services/{sample,nexus}/{base,staging,production}`: Kustomize workloads and DRN configuration.
- `services/postgresql/{base,staging}`: staging database.
- `GitOps.slnx`: repository-wide IDE and build entry point. The `/Src/` solution folder contains Bootstrap.Sources and DependencyPins. `/Test/` contains ApplicationSecretBootstrap.Tests, ChartValidation.Tests and DependencyPins.Tests. These logical folders preserve existing filesystem paths, following DRN-Project. Building does not run tests or require Helm. Run each test project explicitly. Bootstrap validation targets `tests/ApplicationSecretBootstrap.Tests/ApplicationSecretBootstrap.Tests.csproj`; CodeQL builds the full solution and both standalone entry points.
- `README.md`, `docs/platform-operations.md`, `docs/staging-configuration.md`: source-owned deployment documentation.
- `docs/local-access.md`: workstation port-forwarding, login and database TLS guidance for platform UIs, staging PostgreSQL and Graylog MongoDB.

## Repository overview

- Type: Kubernetes GitOps configuration with two standalone C# credential bootstraps.
- Architecture: Argo CD roots, a Helm application chart, Kustomize payloads and workload overlays. `tests/Bootstrap.Sources` links the deployed bootstrap sources into a non-packable library.
- Frontend: none in this repository. Razor, React and DRN buildwww application sources belong to the sibling DRN-Project repository.
- Testing: `tests/ApplicationSecretBootstrap.Tests` is a non-packable .NET 10 executable test project using DRN.Framework.Testing, xUnit v3 and Microsoft.Testing.Platform. It references `Bootstrap.Sources` and uses fake Kubernetes HTTP handlers. No local execution is authorized by this inventory.
- Non-bootstrap tests: `tests/ChartValidation.Tests` owns Helm chart contracts. `tests/DependencyPins.Tests` owns dependency inventory and updater tests, references `tools/DependencyPins` and does not require Helm. The required `charts` CI job builds and runs both projects independently and checks generated pins.

## Framework Skills

This repository consumes DRN Framework packages for its bootstraps and tests. Select only the skills relevant to the task, in the order below. Package implementation and broader application examples are external context, not local source paths.

| Skill | Local trigger and evidence |
|---|---|
| `overview-drn-framework` | Orient bootstrap dependency work. Both project files reference DRN.Framework.Utils. |
| `overview-drn-testing` | Choose bootstrap test scope. The test project references DRN.Framework.Testing. |
| `drn-sharedkernel` | Review `JsonConventions.DefaultOptions` used by both deployed bootstrap files through Utils. |
| `drn-utils` | Review bootstrap encoding helpers and the direct Utils package references. |
| `drn-testing` | Modify bootstrap tests using `DataInlineUnit` and `DataMemberUnit`. |

Keep the other DRN and frontend skills as a conditional reference catalog. Do not infer local hosting, domain, Entity Framework, Razor or buildwww projects from their presence. Use those skills only when the task explicitly needs their external framework or deployed application context. There are no repository-specific custom workflow routes or custom skill groups.

## Deployment rules

`dependencies.json` is the authoritative input for mapped external chart versions, explicit deployment image pins, Gateway API resources and Argo CD's related Helm tooling. `tools/DependencyPins` provides .NET `update` and read-only `check` modes. Change pins in the inventory and regenerate mapped scalar fields, preserving the manually owned surrounding manifests. Commit inventory and consumers together only when authorized. Update source-owned provenance separately. CI requires generator check mode and independent discovery of supported scalar pin forms across deployment/infrastructure/services. Repeated keys require contextual selectors with one capture; missing, ambiguous and overlapping targets fail before writes. Extend discovery tests when introducing another representation. Never treat inventory agreement as registry verification. NuGet and GitHub Action pins retain their existing owners. See [dependency updates](../docs/platform-operations.md#updating-dependency-pins). Local .NET execution still requires explicit authorization.

Format deployment templates with two-space indentation and indented sequence items. Order Application fields as project, source, destination, optional ignoreDifferences, then syncPolicy. Within source, put chart or path before repoURL and targetRevision. Within destination, put namespace before server and use the plain cluster URL. Use a space after comment markers. Preserve component-specific settings and sequence ordering when formatting.

Only staging and production are supported deployment environments. Use these full names in environment values, paths and documentation. Local application development happens outside this GitOps repository. The develop Git branch remains the integration branch and does not represent a deployed development environment. Keep the DRN configuration keys that disable development-only behavior in their overlays.

Publish preview GitOps tags `YYYY.M.sequence-previewNNN` from develop, with three-digit preview numbers from `001` through `999`. Publish final tags `YYYY.M.sequence` from master. Select immutable tags through `refs/tags/...`. Staging accepts both. Production accepts final tags only and remains disabled initially. Each environment has its own cluster and Argo CD, using the drn-project workload namespace.

Root Helm parameters pass Argo CD's resolved SHA to all Git children. Upstream Helm chart pins and image digests are independent. Final promotion retains tested image digests. If promotion to master creates a new commit, run its CI and validate that snapshot in staging before production. GitOps tags do not publish application images. Staging uses AutoMigrateStaging with prototype disabled. Production forbids startup migrations and requires a separately provisioned database.

Keep bootstrap administration separate from the restricted workload project. Preserve namespace ingress isolation, Traefik's deny default and explicit public listener authorization. Credentials, seed/key material and trusted database CAs are Secret inputs, provisioned by the documented staging bootstraps or externally. Do not add real secret data to this repository.

Staging workloads project only `postgresql-app/pgpass` and `postgresql-ca/ca.crt` into a read-only volume with mode `0440` and the existing pod group. ConfigMaps own password-free context connection strings using Npgsql `Passfile` and `SSL Mode=VerifyFull`. Match the generated pgpass host, port, database and username. Never mount the CA private key. Production requires external application Secrets, connection strings and CA inputs. Roll out applications after credential or CA rotation and verify connections. Mounted Secret updates do not prove pool refresh.

Staging's dedicated `application-secrets` project and Job run before workloads. Validate and preserve existing Secrets, including legacy independent seeds. Create missing Secrets from Nexus's seed/key settings with application-specific hosts. Recover missing Nexus from the first configured survivor. Before credential writes, atomically create or validate the immutable, non-secret `application-secret-bootstrap-initialized` ConfigMap with `data.version: "1"`. Only a confirmed successful creator may generate automatically when no settings survive. An existing marker with no survivors requires restoration or a one-run `ALLOW_CREDENTIAL_GENERATION=true` override. Conflicts, ambiguous creation, invalid markers and API failures never grant automatic generation. The override cannot bypass marker errors. Keep it out of GitOps. Retain the marker without Job owners or Argo CD tracking. Protect against total Secret loss only while the marker and namespace survive. Enroll old installs while valid Secrets survive; otherwise restore before rollout. Grant only named marker reads and namespace ConfigMap creation, without update/delete, outside workload permissions. Do not gate creation on Deployments. Operators restore lost legacy seeds and custom hosts. Extend the Applications list and named Secret-read RBAC together. Keep Secret creation outside the workload project. Generated hosts cover local access and service DNS. Operators configure custom ingress hosts.

Both credential bootstraps run single C# files through the same official digest-pinned .NET SDK image. Use the image's built-in `app` user with UID/GID and fsGroup `1654` for both init and bootstrap containers. Kustomize generates source ConfigMaps at child wave -1. Keep source and root filesystems read-only and SDK output in bounded temporary storage. Set `XDG_RUNTIME_DIR=/tmp/runtime` for named mutex storage and `XDG_DATA_HOME=/tmp/data` for local application data. A non-root init container using the same SDK pin creates both directories with mode `0700` on the Pod's scratch volume. The built-in user does not make its home writable on a read-only root filesystem. `DOTNET_CLI_HOME` does not redirect either location. Preserve resource limits and `NuGetAudit=false`. Startup restores DRN.Framework.Utils 0.10.1-preview005 and its dependencies from nuget.org. Verify directory initialization, restore timing and resources in staging.

Both bootstrap Jobs use the Ubuntu 24.04 LTS (Noble) SDK variant. Keep init and bootstrap image pins identical across both manifests. The manifests own the exact version and digest. Verify base-image changes against MCR and validate restore and execution under the existing security and resource limits before promotion.

Each bootstrap clones `JsonConventions.DefaultOptions` and omits null properties without model attributes. Kubernetes HTTP uses built-in .NET. Preserve CA bundle and hostname verification, per-request token reads, safe logs and Secret lifecycle checks. Keep bootstrap ConfigMap permission separate from workload permissions. Automatic pruning is disabled. Remove unused hashed ConfigMaps deliberately. See [staging bootstrap](../docs/staging-configuration.md#automatic-staging-secret-bootstrap) and [Graylog operations](../docs/platform-operations.md#graylog) for runtime limits.

Both bootstrap files declare `#:property PublishAot=false`, matching the Job and CI commands. Preserve this source-level setting for direct IDE and file-based builds because the serializers use reflection. Native AOT and trimming are not supported.

Bootstrap types use the `DRN.GitOps.Bootstraps.Application` and `DRN.GitOps.Bootstraps.Graylog` namespaces. Explicit `Main` methods remain guarded by `!BOOTSTRAP_TESTS` so the same files compile as standalone programs and as the test source library.

Bootstrap Linkerd Viz before Grafana. Grafana's Prometheus authorization policy requires the Viz namespace to exist.

Graylog's Kustomization must retain all MongoDB resources. Preserve named Secret reads and create-only RBAC. Existing credentials are authoritative. Reject partial sets. Keep the 15-minute operator URI wait and 20-minute Job deadline.

Bootstrap.Sources links both deployed files without entry points and grants internals access to the DRN.Framework.Testing project. Both projects reference Utils. Tests reuse deployed models and serializer options.

The project uses Kubernetes 1.36.4 for testing. The pinned Linkerd charts require Kubernetes 1.31 or newer. This component minimum does not establish compatibility for the whole stack. Update CRDs, control plane and Viz together, verify image digests, and recheck proxy cryptography defaults and Grafana dashboard revisions on upgrades.

Graylog's operator URI polling retries HTTP 5xx and request timeouts on its existing five-second cadence without resetting the 15-minute deadline. Keep HTTP 401/403, malformed values and failures outside that polling read fatal. Classify API failures by status code, never error-message text.

Dependency updates target compatible releases. Keep PostgreSQL on major 18 with standard-trixie images and MongoDB on the 8.2 line unless a database upgrade is explicitly requested. PostgreSQL major upgrades on existing clusters require a tested migration, verified backup and planned downtime before syncing. Do not directly apply the Trixie pin to an existing Bookworm cluster: CloudNativePG in-place major upgrades require the same OS distribution. Standard images require a backup plugin for Barman Cloud integration. Keep chart-managed helper versions aligned with their owning chart. The dated dependency review in docs/platform-operations.md records compatibility exceptions and verification limits.

Keep Application health Lua identical in both Argo CD values files and compatible with the default Lua sandbox. Root waves require a successful finished sync for the current source and revision, a current Synced comparison and healthy resources. Reject queued, running or failed operations, dry runs, manual selective syncs, apply-strategy syncs and nonempty local manifests. Normalize omitted optional `ignoreDifferences` fields before comparing spec and status.

Accept selective automatic self-heals only with positive `autoHealAttemptsCount` and successful automated history matching every current source and revision. In Argo CD 3.5.6, this history proves an unfiltered repository sync using hooks. The upstream patch comparison preserves the relevant controller and sync source files. Manual history cannot prove hooks ran. Missing matching history or no-op source updates can require a full child sync. Install gate changes through Argo CD Helm values, outside the DRN root. Reverify these semantics on Argo CD upgrades.

Group each chart's resource templates in descriptive directories without numeric prefixes. Sync-wave annotations define order within each chart. Start at 10 and increment by 10 for dependent groups. Independent Applications share a wave. Document dependency relationships rather than duplicating wave numbers. The root prepares shared prerequisites, including Gateway API CRDs for Linkerd and Traefik, then starts `drn-logging`, `drn-monitoring`, `drn-workloads`, Traefik and Sealed Secrets concurrently. Keep Traefik outside the workloads branch so ingress failures do not block application deployment. Preserve the [installation dependencies](../docs/platform-operations.md#installation-dependencies). Branches use the bootstrap project without expanded permissions and receive the root's exact SHA, environment, release tag and production opt-in. Keep `validate.yaml` identical at all four template roots. Preserve Application health checks and require every root child to succeed before root completion. Preserve existing Application names and payload ownership. Transfer leaf Applications with pruning disabled, including on rollback. Verify new tracking before enabling pruning.

Sample and Nexus bases probe their local anonymous WeatherForecast routes for startup, readiness and liveness through an internal Http1 listener on port 8081. Keep service traffic on its existing h2c listeners. Staging and production AllowedHosts Secrets must include each service's probe Host (`sample` or `nexus`). These probes check HTTP responsiveness, not database or downstream health. See docs/platform-operations.md for timings and Linkerd authorization constraints.

Sample and Nexus inherit separate `1Gi` ReadWriteOnce filesystem PVCs at `/appdata` from their service bases. Keep claims in the Deployment's child sync wave for delayed binding. Preserve fsGroup 1654, one replica, `Recreate` and `rollingUpdate: null`. Keep temporary volumes and their budgets unchanged. Retain claims with `Prune=false,Delete=false`. Preserve required emptyDir data before rollout. MongoDB's `4Gi` data request applies to new volumes. Existing `8Gi` volumes require a verified migration before syncing. Never force PVC shrink or deletion. See [storage operations](../docs/platform-operations.md#application-persistent-storage) for provisioning, backups and retirement.

Keep `retained` staging-only and opt-in, with empty profile, storage-class and stage defaults. Require and propagate `retainedStorageClass` without embedding provider-specific drivers or identifiers. Preserve ordered activation, immutable Git revision propagation and the [storage and identity contract](../docs/platform-operations.md#retained-storage). Stage rollback does not stop retained Applications. The external recovery coordinator owns stopping writers and reconciliation. Keep each branch Application in its own file under `deployment/templates/parallel-branches/`.

Omit the monitoring branch during retained `prepare`. Its chart is empty in that stage and cannot automatically establish the completed sync required by the root health gate. Create it during `data`.

Externally managed credential Jobs must validate Secrets and immutable markers using reads only. Generation overrides must not enable writes. Graylog must compare the operator-derived URI with the saved identity. Never regenerate checkpointed authorities for retained data. Preserve default-profile initialization behavior.

MongoDB requires native TLS with SCRAM and remains unmeshed. Its certificate uses the linkerd-trust-anchor issuer. The graylog-database-trust Bundle supplies PEM and PKCS12 trust to Graylog and Data Node. Preserve certificate verification and the operator-generated TLS URI. Grant the MongoDB graylog user readWrite and dbAdmin only on the graylog database. Graylog requires collMod on cluster_events during startup, which readWrite alone does not permit. The Graylog chart enables self-signed startup for Data Node's API and OpenSearch TLS without manual CA provisioning. Managed Data Node CA and renewal configuration is outside beta scope. MongoDB CA changes require client restarts and coordinated rotation with the shared issuer. See [Graylog operations](../docs/platform-operations.md#graylog).

The pinned Linkerd proxy prefers hybrid X25519MLKEM768 key exchange. Preserve upstream defaults without redundant PQ flags. Certificate authentication remains classical. Native database/Graylog TLS has no verified configuration-only PQ enablement with current pins. Reassess runtime and peer compatibility on upgrades. Never infer PQ certificate support from hybrid TLS support. See [post-quantum evidence and limits](../README.md#post-quantum-support-in-the-pinned-deployment).

## Conventions

Git: integration branch `develop`. Release branch `master`. Topic PRs and Dependabot target develop. Release PRs promote develop to master. Deployment roots select immutable tags, never branch heads.

## Commands and verification

Default execution scope is static inspection and git diff --check. Do not restore, build, render charts/overlays, run apps, tests, benchmarks, clusters or ad-hoc helper scripts without explicit authorization. JSON parsing and read-only source/diff inspection are static checks. No staging, commit, tag, push or deployment without explicit user authorization.

Keep separate develop, master and pull-request workflows with explicit permissions and gatekeepers. Reuse composite actions for bootstrap validation, Trivy and Actions/C# CodeQL. Keep single-step PR dependency review inline. Develop runs validation and Trivy. Master adds required Sonar analysis and CodeQL, including on its weekly schedule. PRs add CodeQL and dependency review, pass no repository secrets and skip Sonar. PR source and local actions execute from the checked-out merge revision on hosted runners without persisted checkout credentials. Preserve repository-specific paths and identities. Sonar must finish after a successful build even when tests fail, without hiding that failure. CodeQL must include both deployed entry points; coverage includes only Bootstrap.Sources. The [test README](../tests/ApplicationSecretBootstrap.Tests/README.md) owns CI setup, check-name migration, commands and provenance.

Calendar-shaped preview tags route to develop's Trivy workflow, and final tags to master's. Filters do not enforce ancestry. Verify each release commit belongs to the required branch before tagging. Preserve full SHA-pinned third-party actions and verify provenance before version updates. CI does not publish releases.

## Dependency and test maintenance

Use `overview-github-actions` for dependency/manifest changes and `test-unit` for regression maintenance. Apply `drn-testing` when changing test attributes or fixtures. The procedures below document available commands, not permission to execute them.

### Updating dependencies

1. Read the [dependency update procedure](../docs/platform-operations.md#updating-dependency-pins) and the component's compatibility constraints. Verify candidate chart archives, image digests, package hashes or Action tag-to-commit mappings with their canonical publishers.
2. For mapped deployment dependencies, edit `dependencies.json`, then run `dotnet run --project tools/DependencyPins -- update` when authorized. Review every generated consumer change. Repeated scalar keys need contextual selectors with exactly one value capture. Add new consumers to the inventory and extend discovery for new representations.
3. NuGet versions remain in project files, bootstrap `#:package` directives and the tool manifest. GitHub Action SHAs remain in workflows/composite actions. Update all affected consumers of those separately owned pins. SDK selection comes from `global.json`. Do not assume the deployment updater covers them.
4. Refresh the owning provenance and compatibility documentation. When Argo CD changes, verify its bundled Helm version and the official archive checksum, then update the inventory pins consumed by `.github/actions/chart-validate/action.yml`.
5. Check generated pins and run the affected suites within the authorized scope. Inventory and generated consumers belong in the same reviewed change. Commit only with authorization. Staging runtime acceptance remains separate from CI.

### Validating manifests and tests

Run these commands from the repository root only when execution is explicitly authorized. `dotnet run` may restore and build. Helm must be installed at the version pinned by the chart-validation action before running chart tests. For CI-equivalent audited restore/build flags, follow [.github/actions/chart-validate/action.yml](../.github/actions/chart-validate/action.yml) and [.github/actions/dotnet-validate/action.yml](../.github/actions/dotnet-validate/action.yml). Use `--no-build` only after building the current sources in the same configuration.

| Check | Command | What it proves |
|---|---|---|
| Generated pin consistency | `dotnet run --project tools/DependencyPins -- check` | Mapped consumers match the inventory without rewriting them. |
| Inventory and updater contracts | `dotnet run --project tests/DependencyPins.Tests/DependencyPins.Tests.csproj --configuration Release` | Independent discovery covers supported pin locations; updater fixtures enforce validation and preservation. No Helm required. |
| Manifest rendering contracts | `dotnet run --project tests/ChartValidation.Tests/ChartValidation.Tests.csproj --configuration Release` | All four local charts validate inputs, render expected Applications and retain identical validation files. |
| Credential bootstrap behavior | `dotnet run --project tests/ApplicationSecretBootstrap.Tests/ApplicationSecretBootstrap.Tests.csproj --configuration Release` | Deployed lifecycle logic behaves correctly against fake Kubernetes HTTP responses. |

Building `GitOps.slnx` does not run these suites. The `charts` CI job runs dependency and chart tests independently. Bootstrap validation belongs to the `dotnet` job. Both are required by branch and PR gatekeepers.

Chart contracts do not apply Kustomize patches, render referenced upstream charts, validate Kubernetes schemas or prove live storage, TLS and recovery behavior. If a change needs those checks, identify the affected Application source, upstream chart/version and overlay from the manifests. Perform additional rendering or environment acceptance only with explicit authorization and report it separately.

### Keeping tests up to date

| Changed contract | Update alongside the source |
|---|---|
| Pin location, scalar representation or inventory schema | `DependencyInventoryTests.cs` discovery/coverage cases and inventory mappings. Keep discovery independent of inventory enumeration. |
| Updater validation or replacement behavior | `PinUpdaterTests.cs` temporary fixtures. Include relevant invalid tokens, LF/CRLF, end-of-input, exact preservation, idempotence and failure-before-write cases. |
| Helm input validation | Canonical `deployment/templates/validate.yaml`, all three branch copies and `ChartTests.ConfigurationCases`. Preserve positive and negative boundary cases. |
| Application propagation, storage or activation stages | Resource-specific assertions in `ChartTests.cs`, covering default and affected retained/production combinations. Do not substitute whole-output text matches for target-specific checks. |
| Credential lifecycle or API behavior | The owning bootstrap suite, including read-only external mode, lost credentials, marker validation and sanitized failures where affected. |
| Test project, command or tool changes | `GitOps.slnx`, owning CI actions/gatekeepers, test README and this profile. |

Investigate failures before changing expected output. Preserve the deployment invariant when fixing test formatting assumptions. Add a regression that distinguishes the old failure from the intended behavior. Re-run affected suites after their source or fixtures change when authorized. Record the revision, commands, results and remaining coverage limits. A checked-in test or documentation entry is not evidence of a passing run. With static-only permission, inspect the diff and run `git diff --check`, then explicitly record execution as not run.

## Documentation and release notes

Use .NET/C# for repository test tooling and automation implementations, following the credential bootstrap conventions. Do not introduce Python test runners or helper scripts. Declarative workflow configuration and short native CLI setup steps remain in composite actions.

The `charts` job is required by all three CI gatekeepers on branch and PR runs. `tests/ChartValidation.Tests` uses .NET 10, DRN.Framework.Testing, xUnit v3 and Microsoft.Testing.Platform to invoke Helm, enforce equality with the root `validate.yaml`, reject invalid inputs and inspect retained-stage output. All .NET projects belong to `GitOps.slnx`. Keep bootstrap and chart CI validation scoped to their respective test projects so bootstrap tests do not require Helm. The chart-validation action owns the Helm version and checksum, aligned with Argo CD's tool manifest. See [chart contracts](../tests/ChartValidation.Tests/README.md) for the rule audit and verification limits. This inventory does not authorize local test or rendering execution.

MongoDB/Graylog bootstrap claims the immutable `graylog-credential-bootstrap-initialized` ConfigMap in `graylog` with `data.version: "1"` before credential writes. Only its confirmed creator may initialize a fresh installation. Retain the marker without owners or GitOps tracking. Existing-marker total loss, partial credential loss, invalid markers and API failures fail closed. Keep the existing partial-loss safeguards and no generation override. Validate the database password and final Graylog credentials before enrolling existing installations without rotation. Grant named ConfigMap get and namespace create only. Protection requires the marker and namespace to survive. Enroll while credentials survive and restore unmarked damaged installations before rollout. Document recovery and pending live checks in platform operations and TODO.

Update owning deployment docs and this profile when shared deployment behavior changes. Record operational changes in deployment documentation. There is no package-specific release-note file, and configuration-only changes need no separate release notes. Keep CAD workflow artifacts local in ignored `.agent/temp/`.
