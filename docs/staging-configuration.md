# DRN environment configuration

Application settings live in `drn-project` in each environment's separate cluster. See the [deployment model](../README.md#deployment-model) for roots, release tags and production activation.

[Ownership](#configuration-ownership) | [Required Secrets](#required-staging-and-production-secrets) | [Bootstrap and recovery](#automatic-staging-secret-bootstrap) | [Staging database](#staging-database-credentials) | [Production database](#production-database-secrets) | [Authentication](#authentication-and-image-readiness)

| Overlay | DRN environment | Startup migration | Database |
|---|---|---|---|
| `services/*/staging` | `Staging` | `AutoMigrateStaging=true` | Single-instance CloudNativePG |
| `services/*/production` | `Production` | Disabled | Provisioned separately before activation |

All overlays disable prototype recreation and external dependency launch. Staging also disables `AutoMigrateDevelopment`. It applies existing migrations without generating new ones. Keep migrations compatible with applications sharing the database. Tag rollback does not reverse them. See [DRN migration settings](https://github.com/duranserkan/DRN-Project/blob/master/DRN.Framework.EntityFramework/README.md#migration-and-prototype-settings).

## Configuration ownership

| Source | Owns |
|---|---|
| Base `common-settings.json` | Ports, logging and application IDs |
| Overlay `mounted-settings.json` | Environment flags and staging's password-free connection strings |
| `sample-settings` and `nexus-settings` Secrets | Hosts, seeds, keys and production database inputs |
| Base Deployments | Image pins and resource limits |

Keep the two JSON files' keys disjoint because their enumeration order is unspecified. Mounted JSON overrides key-per-file settings, so exclude Secret-owned keys from both files. See [DRN configuration layering](https://github.com/duranserkan/DRN-Project/blob/master/DRN.Framework.Hosting/README.md#layering).

Staging bootstraps application Secrets. Production requires external provisioning. Never commit Secret values. Missing projected keys block pod startup. Applications read settings at startup, so roll out affected pods after changes.

Both overlays inherit persistent `/appdata` and single-replica `Recreate` deployments. Updates interrupt service. Follow the [storage requirements](platform-operations.md#application-persistent-storage) before rollout.

## Required staging and production Secrets

Provision production or custom staging values through the trusted secret-management process. Both application Secrets require these keys. Double underscores separate configuration sections.

| Secret key | Required value |
|---|---|
| `AllowedHosts` | Semicolon-separated hostnames, no schemes, ports or `*`. Include the ingress/internal hosts and probe host: `sample` or `nexus`. |
| `DrnAppFeatures__SeedKey` | Stable private seed, 58 to 256 characters. Never use DRN's default or test sample. See constraints below. |
| `NexusAppSettings__Keys__0__KeyMaterial` | Private key decoding to exactly 32 bytes. Never use the DRN example. |
| `NexusAppSettings__Keys__0__Format` | `Base64`, or another DRN format matching the encoded material |
| `NexusAppSettings__Keys__0__Default` | String `true` |

Seeds must contain uppercase, lowercase, a digit and a special character from `! * ( ) - _` or space. Only letters, digits and those characters are allowed. Limit sequential runs to four characters and identical runs to three. Plain Base64 can contain forbidden characters. See [seed behavior](https://github.com/duranserkan/DRN-Project/blob/master/DRN.Framework.Utils/README.md#seed-keys-and-derived-settings) and [Nexus key formats](https://github.com/duranserkan/DRN-Project/blob/master/DRN.Framework.Utils/README.md#nexusappsettings-and-nexus-keys).

## Automatic staging Secret bootstrap

The following automatic initialization applies to the default infrastructure profile. The [retained-storage profile](platform-operations.md#retained-storage) requires externally provisioned Secrets and existing initialization markers. Its Jobs validate those inputs without creating or copying credentials, even when `ALLOW_CREDENTIAL_GENERATION=true` is present.

The staging-only `application-secrets` Sync hook runs alongside PostgreSQL before Nexus and Sample. It creates a shared 99-character seed with 256 bits of randomness and a random 32-byte Base64 Nexus key on fresh installs. See [bootstrap runtime](platform-operations.md#credential-bootstrap-runtime) and [tests](../tests/ApplicationSecretBootstrap.Tests/README.md) for shared implementation details and verification limits.

Generated hosts cover each application's Kubernetes DNS names, `localhost` and `127.0.0.1`. Add the ingress hostname and roll out the application before public access.

| Existing settings | Behavior |
|---|---|
| All Secrets present | Validate five nonempty fields and matching Nexus keys. Preserve values, including independent legacy seeds and custom hosts. |
| An application Secret missing | Copy Nexus's seed and key, with application-specific hosts. |
| Nexus missing | Recover from the first configured survivor. |
| Competing Secret creator | Validate and use the stored winner. A Nexus winner supplies subsequent creations. |
| Invalid UTF-8, Base64 or mismatched keys | Stop before writing. Applications perform full DRN value validation. |

The Job has named Secret and marker reads plus namespace-scoped create permission. Kubernetes cannot restrict creation by name. It cannot read Deployments or update/delete Secrets or ConfigMaps. Keep this RBAC outside the workload project. API requests time out after 30 seconds, with redirects and proxies disabled.

### Initialization marker

Before credential writes, the Job atomically creates the immutable `application-secret-bootstrap-initialized` ConfigMap in `drn-project`. It contains only `data.version: "1"`. This records a claim, not completion. It has no Job owner or Argo CD tracking metadata. Retain and back it up with the settings Secrets.

| Initialization marker | Settings Secrets | Action |
|---|---|---|
| Absent | None | Atomically create the marker. Only the successful creator generates fresh credentials automatically. |
| Absent | Valid survivors | Create the marker to enroll the existing installation, then preserve or recover settings. |
| Valid and present | Valid survivors | Preserve survivors and recover missing settings using their credentials. |
| Valid and present | None | Stop and require restoration or deliberate one-run recovery. |
| Invalid or unreadable | Any | Stop without writing credentials, even with the generation override. |

Conflicts, timeouts, unexpected responses and API errors never grant automatic generation. Retry after a competing creator finishes. A crash before saving the first Secret requires recovery. Once a valid Secret survives, normal retries can recover the rest.

Protection requires the marker and namespace to survive. It cannot distinguish a fresh install from restored data with no marker or credentials. Never delete the marker to clear an error.

### Enrollment and recovery

1. Enroll existing installations with a full bootstrap sync while valid Secrets survive. Confirm with `kubectl -n drn-project get configmap application-secret-bootstrap-initialized`.
2. If credentials were lost before enrollment, restore them first. Restore independent legacy seeds and custom hosts from backup when those exact values are needed.
3. For total loss, restore Secrets with the application data they protect, then full-sync `application-secrets` and roll out affected applications.

For a deliberate reset or interrupted fresh initialization, use `ALLOW_CREDENTIAL_GENERATION=true` for **one run only**. It still requires a valid marker or successful marker creation and never overwrites survivors. Keep it out of GitOps. Remove the one-off Job or override afterward, then full-sync normally. New credentials cannot recover data protected by lost keys.

Generated Secrets have no workload owners or Argo CD tracking, so bootstrap pruning leaves them intact. Recovery requires a full sync because this Job is not a background controller.

To add an application, update `Applications` in [bootstrap.cs](../infrastructure/application-secrets/staging/bootstrap.cs), the Role's `<name>-settings` read permission and the workload's Secret projection. Duplicate names are processed once. Existing Secrets stay unchanged.

## Staging database credentials

CloudNativePG generates both Secrets in `drn-project`. Staging mounts their public CA and password file directly, without copying credentials into application Secrets:

| Secret key | Mount path |
|---|---|
| `postgresql-app/pgpass` | `/appcerts/postgresql/pgpass` |
| `postgresql-ca/ca.crt` | `/appcerts/postgresql/root.crt` |

The volume is read-only, mode `0440`, with pod group `1654`. Missing keys block startup. Never mount the CA private key.

The staging ConfigMaps supply all five named EF context connection strings with this value:

```text
Host=postgresql-rw;Port=5432;Database=drnDb;Username=drn;Passfile=/appcerts/postgresql/pgpass;SSL Mode=VerifyFull;Root Certificate=/appcerts/postgresql/root.crt
```

DRN passes these strings to Npgsql, which reads `Passfile`. Match its host, port, database and username to the generated `pgpass` entry. The service hostname is covered by the database certificate.

For existing installs, sync ConfigMaps and Deployments and verify all contexts. Then remove unused database connection and CA copies from application Secrets through the trusted secret-management process. Retain hosts, seeds and keys.

After credential or CA rotation, roll out both applications and verify connections and certificates. Do not assume updated files refresh existing connection pools. No automatic rollout controller is configured.

Source contracts: [CloudNativePG 1.30.1 Secret generation](https://github.com/cloudnative-pg/cloudnative-pg/blob/v1.30.1/pkg/specs/secrets.go), [certificate service names](https://github.com/cloudnative-pg/cloudnative-pg/blob/v1.30.1/api/v1/cluster_funcs.go), and [Npgsql password files](https://www.npgsql.org/doc/security.html#password-management).

## Production database Secrets

Provision the database externally. Both application Secrets need a PEM CA bundle at key `postgresql-root-ca.crt`, mounted at `/appcerts/postgresql/root.crt`.

| Secret | Required connection keys |
|---|---|
| `sample-settings` | `ConnectionStrings__QAContext`, `ConnectionStrings__QBContext`, `ConnectionStrings__SampleIdentityContext`, `ConnectionStrings__DrnDataProtectionContext` |
| `nexus-settings` | `ConnectionStrings__NexusIdentityContext` |

Each value needs host, port, database, username, password and `SSL Mode=VerifyFull;Root Certificate=/appcerts/postgresql/root.crt`. Use a hostname covered by the certificate and update the CA bundle on rotation. `SSL Mode=Require` alone does not authenticate the server.

Staging migrations need DDL privileges. Production runtime credentials should have only runtime permissions, with a separate privileged migration process. No production Cluster or migration Job is supplied.

Keep credentials, seeds and keys stable and separate across environments. Changing Sample's seed can make stored data-protection material unreadable. The projection exposes one default Nexus key. Extend it before rotation if old parsing keys must remain available.

## Authentication and image readiness

| Application | Account, email and phone confirmation defaults |
|---|---|
| Sample staging | All disabled |
| Sample production and Nexus | All enabled |

Establish account provisioning and confirmation flows before beta access. Change nonsecret environment JSON deliberately when a different policy is needed. Validate startup, connectivity and migrations with the pinned images. Source inspection does not prove container behavior.

Production inherits preview images. Keep it disabled until the [production prerequisites](../README.md#production-activation-prerequisites) are met. A final GitOps tag does not certify its images.

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
