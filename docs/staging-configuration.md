# DRN environment configuration

Use separate staging and production clusters. Both use the `drn-project` workload namespace and the same service names. Staging is the rolling beta environment. Production overlays are inactive scaffolding. Development is local only and has no GitOps root or overlay here.

Preview releases come from `develop`. Final releases come from `master` after promotion through `develop → master`. Staging accepts either tag type. Production accepts final tags only.

| Overlay | DRN environment | Startup migration | Database |
|---|---|---|---|
| `services/*/staging` | `Staging` | `AutoMigrateStaging=true` | Single-instance CloudNativePG |
| `services/*/production` | `Production` | Disabled | Provisioned separately before activation |

Prototype recreation and external dependency launch are disabled in every overlay. Staging applies existing migrations. It does not generate migrations for model changes. Migrations must remain compatible with other applications sharing the database. Reverting a deployment tag does not revert database migrations.

Staging disables `AutoMigrateDevelopment`. It uses explicit password-free connection strings and mounts the operator-generated password file and database CA directly.

## Configuration ownership

Each service base supplies `common-settings.json`. The environment overlay adds `mounted-settings.json` to the same generated ConfigMap. Their configuration keys are disjoint because DRN does not specify mounted JSON file enumeration order. Common settings retain service ports, logging, app IDs, and existing resource limits. Image tags and digests remain pinned in the base Deployments.

Both environments inherit persistent `/appdata` storage and single-replica `Recreate` deployments. Updates briefly interrupt service. Follow [application storage requirements](platform-operations.md#application-persistent-storage) before rollout, including preservation of existing files.

Both environments require `sample-settings` and `nexus-settings` Secrets in `drn-project`. Staging generates them on first installation through the `application-secrets` bootstrap described below. Production requires external provisioning. Secret values are never checked into Git. Required projected keys make missing configuration prevent pod startup.

Applications read settings at startup. Roll out affected pods after changing Secrets. Mounted JSON loads after key-per-file settings, so keep secret-owned keys out of both JSON files. Staging's password-free connection strings belong to mounted JSON. Production connection strings belong to Secrets. Environment names are DRN settings, not image tag channels.

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

The Job runs [bootstrap.cs](../infrastructure/application-secrets/staging/bootstrap.cs) with an official .NET SDK image. [Kustomize](../infrastructure/application-secrets/staging/kustomization.yaml) packages the source in a ConfigMap mounted read-only at `/bootstrap`. The file references DRN.Framework.Utils 0.10.1-preview003 for SharedKernel JSON conventions with null omission. Kubernetes requests use built-in .NET HTTP. Startup restore requires nuget.org access.

The SDK restores packages and compiles the file at startup. The root filesystem is read-only. A bounded `emptyDir` at `/tmp` holds packages, SDK files and build output. Limits are 512 MiB of memory, one CPU and 512 MiB of temporary storage, with a 300-second deadline. Verify these limits in staging. Runtime NuGet audit is disabled. CI audits test dependencies, including Utils.

The source ConfigMap uses a content hash in its name. Kustomize updates the Job's reference when the source changes. Wave `-1` creates the ConfigMap and RBAC before the Sync hook. Automatic pruning remains disabled. Old source ConfigMaps can remain after updates. Remove unused versions deliberately after confirming that no Job references them. They contain source code and NuGet configuration only, never generated credentials.

The staging-only `application-secrets` Application runs a Sync-hook Job at root wave `120`, before Nexus (`200`) and Sample (`300`). With the one-run override described below, a fresh installation generates a shared 99-character seed with 256 bits of randomness and a random 32-byte Base64 Nexus key. The seed satisfies DRN-Project's `SecureKeyAttribute` rules.

New application Secrets copy Nexus's seed and key settings. Generated `AllowedHosts` covers each application's short and qualified Kubernetes DNS names, `localhost` and `127.0.0.1`. Add your ingress hostname through your secret-management process and roll out the application before public access.

The Job validates all configured Secrets before writing. It requires five nonempty fields and matching Nexus key settings, rejecting malformed Base64 and invalid UTF-8. It never overwrites existing Secrets, including legacy independent seeds. Applications perform full DRN value validation. Syncing again does not rotate keys or reset custom hosts.

The bootstrap identity has named Secret reads and namespace-scoped Secret creation. It cannot access Deployments or update or delete Secrets.

API requests use the mounted service account token and CA bundle. Certificate chain and hostname checks remain enabled. The token is read again for each request. Requests time out after 30 seconds. Redirects and proxy use are disabled. Errors do not log credentials or API response bodies.

Missing application Secrets are recreated from Nexus, including its seed, regardless of existing Deployments. If Nexus is missing, the first surviving application in the configured order supplies its credentials. If no configured Secret survives, bootstrap stops without writing. A competing creator's stored Secret remains authoritative and must pass validation. A Nexus create conflict uses the stored winner's seed and key for subsequent creations.

For a fresh install or deliberate credential reset, set `ALLOW_CREDENTIAL_GENERATION=true` in the Job's environment for one run. Do not save this override in GitOps. New credentials cannot recover data protected by lost seeds or keys.

To add an application, add its name to `Applications` in `bootstrap.cs`, add `<name>-settings` to the Role's named reads, and configure the workload's Secret projection. Existing applications are preserved while only the new Secret is created. Duplicate application names are processed once.

Operators own recovery when all credential copies are lost. Back up Secrets with application data and restore them before syncing if the original values are required. A lost legacy Secret with an independent seed or custom hosts must also be restored from backup to retain those values. The generated Secrets have no workload owner references or Argo CD tracking metadata, so pruning the bootstrap does not delete them. This Job is not a background secret controller. After recovery, run a full `application-secrets` sync and roll out affected applications.

## Staging database credentials

CloudNativePG generates `postgresql-app` and `postgresql-ca` in `drn-project`. Both staging Deployments project only `postgresql-app`'s `pgpass` key and `postgresql-ca`'s public `ca.crt` key into `/appcerts/postgresql/pgpass` and `/appcerts/postgresql/root.crt`. The CA private key is never mounted. The volume is read-only with mode `0440`, readable through the existing pod group `1654`. Missing Secrets or keys prevent startup. No application Kubernetes API access or credential-copying Job is needed.

The staging ConfigMaps supply all five named EF context connection strings with this value:

```text
Host=postgresql-rw;Port=5432;Database=drnDb;Username=drn;Passfile=/appcerts/postgresql/pgpass;SSL Mode=VerifyFull;Root Certificate=/appcerts/postgresql/root.crt
```

DRN passes each explicit connection string to the context's Npgsql data source. Npgsql reads its password from `Passfile`. Keep the host, port, database and username aligned with the operator's generated `pgpass` entry. The short service hostname also appears in CloudNativePG's generated server certificate. No database password or CA copy is required in staging `sample-settings` or `nexus-settings`.

For an existing staging installation, sync ConfigMaps and Deployments and verify connectivity for all contexts. Old database connection-string and CA keys in application Secrets are no longer projected. Remove unused copies through the trusted secret-management process after rollout succeeds. Keep application seed/key and host settings.

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

Keep seeds and ID key material separate across environments. Do not regenerate them at each deployment. Sample persists ASP.NET data-protection keys in `DrnDataProtectionContext`. Changing its seed can prevent reading existing protected material. The projection permits one default Nexus key. Extend the projected key list before rotating keys if older parsing keys must remain available.

## Authentication and image readiness

The pinned Sample image's staging settings disable `Identity:RequireConfirmedAccount`, `RequireConfirmedEmail`, and `RequireConfirmedPhoneNumber`. Nexus has no staging settings file and defaults all three to true. Production Sample also defaults them to true. Establish the intended account provisioning and confirmation flows before exposing either service to beta users. Change the environment's nonsecret JSON deliberately if a different policy is needed.

The local DRN source tag `release/v0.10.1-preview003` includes these environment, migration, and secret-validation contracts. Source inspection alone does not prove the provenance or runtime behavior of the pinned container digests. Validate startup, connectivity, identity flows, and migrations before making staging available to users.

Production inherits preview image pins. Keep it disabled until the [production prerequisites](../README.md#production-activation-prerequisites) are verified, including approved final images and identity flows. A final GitOps tag does not certify its images for production.

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
