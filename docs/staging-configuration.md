# DRN environment configuration

Use separate staging and production clusters. Both use the `drn-project` workload namespace and the same service names. Staging is the rolling beta environment. Production overlays are inactive scaffolding. Development is local only and has no GitOps root or overlay here.

The `develop` Git branch remains the integration branch. Preview releases come from `develop`; normal final releases come from `master` after promotion through `develop → master`. Staging can validate either tag type. Production accepts final tags only. Branch names do not add deployment environments.

| Overlay | DRN environment | Startup migration | Database |
|---|---|---|---|
| `services/*/staging` | `Staging` | `AutoMigrateStaging=true` | Single-instance CloudNativePG |
| `services/*/production` | `Production` | Disabled | Provisioned separately before activation |

Prototype recreation and external dependency launch are disabled in every overlay. Staging applies existing migrations. It does not generate migrations for model changes. Changes must remain compatible with older pods during a rolling update. Reverting a deployment tag does not revert database migrations.

Staging retains the former development manifests' resource limits, pod security settings and single-instance PostgreSQL topology. Its application environment is `Staging`, with `AutoMigrateDevelopment=false` and `AutoMigrateStaging=true`. It uses explicit password-free connection strings and mounts the operator-generated password file and database CA directly. This does not use DRN's development-only connection-string helper.

## Configuration ownership

Each service base supplies `common-settings.json`. The environment overlay adds `mounted-settings.json` to the same generated ConfigMap. Their configuration keys are disjoint because DRN does not specify mounted JSON file enumeration order. Common settings retain service ports, logging, app IDs, and existing resource limits. Image tags and digests remain pinned in the base Deployments.

Both environments require `sample-settings` and `nexus-settings` Secrets in `drn-project`. Staging generates them on first installation through the `application-secrets` bootstrap described below. Production requires external provisioning. Secret values are never checked into Git. Required projected keys make missing configuration prevent pod startup.

The application reads these settings at startup. Roll out affected pods after changing Secrets. Mounted JSON is loaded after key-per-file settings, so do not add secret-owned keys to either JSON file. Staging's password-free connection strings belong to its mounted JSON; production connection strings remain Secret-owned. Environment names are DRN settings, not image tag channels.

## Required staging and production Secrets

For production or custom staging values, provision both Secrets through the cluster's trusted secret-management process. Double underscores in key names map to configuration separators. The following keys are required in **both** `sample-settings` and `nexus-settings`:

| Secret key | Required value |
|---|---|
| `AllowedHosts` | Explicit semicolon-separated hostnames accepted by that service, without schemes or ports. Include `sample` for Sample and `nexus` for Nexus because kubelet probes send those Host headers. Retain the actual ingress hostname for Sample and other internal request hostnames where applicable. Configure these values before rolling out the probes. Do not use `*` for public staging or production. |
| `DrnAppFeatures__SeedKey` | A secret seed of 58 to 256 characters, distinct from DRN's built-in default and test sample. Include uppercase, lowercase, a digit and a permitted special character (`! * ( ) - _` or space). Use only letters, digits and those permitted characters. Avoid runs longer than four sequential characters or three identical characters. Plain Base64 can contain forbidden characters. Keep the seed stable for the application's stored data and security configuration. |
| `NexusAppSettings__Keys__0__KeyMaterial` | Secret key material decoding to exactly 32 bytes. Never use the checked-in DRN example. |
| `NexusAppSettings__Keys__0__Format` | `Base64` when using Base64-encoded key material. Other DRN formats require matching encoded material. |
| `NexusAppSettings__Keys__0__Default` | The string `true`. |

## Automatic staging Secret bootstrap

The [bootstrap tests](../tests/ApplicationSecretBootstrap.Tests/README.md) use DRN.Framework.Testing and a fake Kubernetes HTTP handler. Both develop and master workflows build the standalone source and run these tests with coverage and SonarCloud analysis. Live TLS behavior and container resources still need staging verification.

The Job runs [bootstrap.cs](../infrastructure/application-secrets/staging/bootstrap.cs) with an official .NET SDK image. [Kustomize](../infrastructure/application-secrets/staging/kustomization.yaml) puts the complete file in a ConfigMap. It mounts the source read-only at `/bootstrap`. No custom image is built. The file references DRN.Framework.Utils 0.10.1-preview003 and uses its transitive SharedKernel JSON conventions with null omission. Kubernetes requests still use built-in .NET HTTP. The mounted NuGet configuration enables nuget.org, which must be reachable for startup restore.

The SDK restores packages and compiles the file when the Job starts. The root filesystem stays read-only. A bounded `emptyDir` at `/tmp` holds downloaded packages, SDK files and build output. The unchanged manifest allows 512 MiB of memory, one CPU and 512 MiB of temporary storage. Its deadline is 300 seconds. Verify startup timing and resource sufficiency with the transitive dependencies in staging. Runtime NuGet audit remains disabled by the existing Job command. CI audits the test dependency graph, which includes Utils.

The source ConfigMap uses a content hash in its name. Kustomize updates the Job's reference when the source changes. Wave `-1` creates the ConfigMap and RBAC before the Sync hook. Automatic pruning remains disabled. Old source ConfigMaps can remain after updates. Remove unused versions deliberately after confirming that no Job references them. They contain source code and NuGet configuration only, never generated credentials.

The staging-only `application-secrets` Application runs a Sync-hook Job at root wave `120`, before Nexus (`200`) and Sample (`300`). A fresh installation generates one shared 99-character seed with 256 bits of randomness and one shared random 32-byte Nexus key encoded as Base64. Seed formatting satisfies DRN-Project's `SecureKeyAttribute` length, character-class and sequence rules. New application Secrets copy Nexus's seed and key settings. Only generated `AllowedHosts` differs by application, covering its short and qualified Kubernetes DNS names, `localhost` and `127.0.0.1`. Add your actual ingress hostname through your secret-management process and roll out the affected application before public access.

The Job reads and validates all configured Secrets before writing. It checks all five nonempty fields and matching Nexus key settings, rejecting malformed Base64 and invalid UTF-8. Existing Secrets are skipped for creation and never overwritten, including older installations with independent seeds. The applications remain responsible for full DRN value validation. Syncing again does not rotate keys or reset custom hosts. The bootstrap identity has named Secret reads and namespace-scoped Secret creation. It has no Deployment, Secret update or Secret delete access. Workload permissions remain unchanged.

API requests use the mounted service account token and CA bundle. Certificate chain and hostname checks remain enabled. The token is read again for each request. Requests time out after 30 seconds. Redirects and proxy use are disabled. Errors do not log credentials or API response bodies.

Missing application Secrets are recreated from Nexus, including its seed, regardless of existing Deployments. If Nexus is missing, the first surviving application in the configured order supplies its credentials. Only when no configured Secret survives does bootstrap generate new credentials. A competing creator's stored Secret remains authoritative and must pass validation. A Nexus create conflict uses the stored winner's seed and key for subsequent creations.

To add an application, add its name to `Applications` in `bootstrap.cs`, add `<name>-settings` to the Role's named reads, and configure the workload's Secret projection. Existing applications are preserved while only the new Secret is created. Duplicate application names are processed once.

Operators own recovery when all credential copies are lost. Back up Secrets with application data and restore them before syncing if the original values are required. A lost legacy Secret with an independent seed or custom hosts must also be restored from backup to retain those values. The generated Secrets have no workload owner references or Argo CD tracking metadata, so pruning the bootstrap does not delete them. This Job is not a background secret controller. After recovery, run a full `application-secrets` sync and roll out affected applications.

## Staging database credentials

CloudNativePG generates `postgresql-app` and `postgresql-ca` in `drn-project`. Both staging Deployments project only `postgresql-app`'s `pgpass` key and `postgresql-ca`'s public `ca.crt` key into `/appcerts/postgresql/pgpass` and `/appcerts/postgresql/root.crt`. The CA private key is never mounted. The volume is read-only with mode `0440`, readable through the existing pod group `1654`. Missing Secrets or keys prevent startup. No application Kubernetes API access or credential-copying Job is needed.

The staging ConfigMaps supply all five named EF context connection strings with this value:

```text
Host=postgresql-rw;Port=5432;Database=drnDb;Username=drn;Passfile=/appcerts/postgresql/pgpass;SSL Mode=VerifyFull;Root Certificate=/appcerts/postgresql/root.crt
```

DRN passes each explicit connection string to the context's Npgsql data source. Npgsql reads its password from `Passfile`. Keep the host, port, database and username aligned with the operator's generated `pgpass` entry. The short service hostname also appears in CloudNativePG's generated server certificate. No database password or CA copy is required in staging `sample-settings` or `nexus-settings`.

For an existing staging installation, sync both the updated ConfigMaps and Deployments and verify connectivity for all contexts. Old database connection-string and CA keys in the application Secrets are no longer projected; remove those unused copies through the trusted secret-management process after the rollout succeeds. Keep all application seed/key and host settings.

The projected files can receive Secret updates without `subPath` mounts. This does not establish seamless credential or CA rotation for existing connection pools. Roll out both applications after rotation and verify database connectivity and certificate validation. No automatic rollout controller is configured.

Source contracts: [CloudNativePG 1.30.1 Secret generation](https://github.com/cloudnative-pg/cloudnative-pg/blob/v1.30.1/pkg/specs/secrets.go), [certificate service names](https://github.com/cloudnative-pg/cloudnative-pg/blob/v1.30.1/api/v1/cluster_funcs.go), and [Npgsql password files](https://www.npgsql.org/doc/security.html#password-management).

## Production database Secrets

Production retains external database provisioning. Both production application Secrets additionally require `postgresql-root-ca.crt`, a PEM CA bundle projected at `/appcerts/postgresql/root.crt`, and their connection keys below.

Required connection keys in `sample-settings`:

- `ConnectionStrings__QAContext`
- `ConnectionStrings__QBContext`
- `ConnectionStrings__SampleIdentityContext`
- `ConnectionStrings__DrnDataProtectionContext`

Required connection key in `nexus-settings`:

- `ConnectionStrings__NexusIdentityContext`

Each production value is a complete Npgsql connection string with host, port, database, username, password, and verified TLS settings for its separately provisioned database. Staging and production use independent clusters, credentials, and data.

Use `SSL Mode=VerifyFull;Root Certificate=/appcerts/postgresql/root.crt` and a host matching the database certificate. Confirm the issued certificate's subject alternative names cover the selected DNS name. Provision the matching production CA bundle explicitly and update it when the database CA rotates. `SSL Mode=Require` alone does not authenticate the database endpoint.

Staging migration credentials need the DDL privileges required by the application's migrations. Production runtime credentials should have only runtime permissions, with a separate privileged migration procedure. No production database Cluster or migration Job is supplied by these scaffolds.

Keep seeds and ID key material separate across environments. Do not regenerate them at each deployment. Sample persists ASP.NET data-protection keys in `DrnDataProtectionContext`; changing its seed can prevent reading existing protected material. The current projection permits one default Nexus key. A planned key rotation that retains older parsing keys must also extend the projected key list before rollout.

## Authentication and image readiness

The pinned Sample image's staging settings disable `Identity:RequireConfirmedAccount`, `RequireConfirmedEmail`, and `RequireConfirmedPhoneNumber`. Nexus has no staging settings file and defaults all three to true. Production Sample also defaults them to true. Establish the intended account provisioning and confirmation flows before exposing either service to beta users. Change the environment's nonsecret JSON deliberately if a different policy is needed.

The local DRN source tag `release/v0.10.1-preview003` includes these environment, migration, and secret-validation contracts. Source inspection alone does not prove the provenance or runtime behavior of the pinned container digests. Validate startup, connectivity, identity flows, and migrations before making staging available to users.

The production scaffold currently inherits the existing preview image pins. Keep production disabled until approved final image digests, database provisioning and migration, secrets, identity flows, public TLS, persistence, backups, and recovery have been validated. A final GitOps snapshot tag does not independently certify its embedded images as production-ready.

## Source evidence

These paths belong to the sibling DRN-Project source at `release/v0.10.1-preview003`:

- `DRN.Framework.Utils/Models/DevelopmentStatus.cs`: migration and prototype environment gates.
- `DRN.Framework.Utils/Settings/AppSettings.cs`: explicit app IDs, default Nexus key, and seed validation.
- `DRN.Framework.Utils/Settings/NexusAppSettings.cs`: key material validation.
- `DRN.Framework.EntityFramework/Extensions/ServiceCollectionExtensions.cs`: required nondevelopment connection strings.
- `DRN.Framework.Hosting/Extensions/ConfigurationExtensions.cs`: mounted settings precedence.
- `Sample.Hosted/appsettings.Staging.json`: staging host and identity defaults.
- `Sample.Hosted/SampleModule.cs`: persistent data-protection configuration.
- `DRN.Nexus.Hosted/Settings/NexusIdentityConfig.cs`: Nexus identity defaults.
