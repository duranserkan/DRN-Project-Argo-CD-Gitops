# DRN environment configuration

Use separate staging and production clusters. Both use the `drn-project` workload namespace and the same service names. Staging is the rolling beta environment. Prod overlays are inactive scaffolding. Development is local only and has no GitOps root or overlay here.

| Overlay | DRN environment | Startup migration | Database |
|---|---|---|---|
| `services/*/stage` | `Staging` | `AutoMigrateStaging=true` | Single-instance CloudNativePG |
| `services/*/prod` | `Production` | Disabled | Provisioned separately before activation |

Prototype recreation and external dependency launch are disabled in every overlay. Staging applies existing migrations. It does not generate migrations for model changes. Changes must remain compatible with older pods during a rolling update. Reverting a deployment tag does not revert database migrations.

Staging retains the former development manifests' resource limits, pod security settings and single-instance PostgreSQL topology. Its application environment is `Staging`, with `AutoMigrateDevelopment=false` and `AutoMigrateStaging=true`. Development-only database helper settings and the password-only mount are replaced by the explicit connection strings, application Secrets and trusted database CA described below. Renaming the environment alone is insufficient.

## Configuration ownership

Each service base supplies `common-settings.json`. The environment overlay adds `mounted-settings.json` to the same generated ConfigMap. Their configuration keys are disjoint because DRN does not specify mounted JSON file enumeration order. Common settings retain service ports, logging, app IDs, and existing resource limits. Image tags and digests remain pinned in the base Deployments.

Stage and prod require separately provisioned `sample-settings` and `nexus-settings` Secrets in `drn-project`. These Secrets are not generated or checked in here. Required projected keys make missing configuration prevent pod startup.

The application reads these settings at startup. Roll out affected pods after changing Secrets. Mounted JSON is loaded after key-per-file settings, so do not add secret-owned keys to either JSON file. Environment names are DRN settings, not image tag channels.

## Required stage and prod Secrets

Provision each Secret through the cluster's trusted secret-management process. Double underscores in key names map to configuration separators. The following keys are required in **both** `sample-settings` and `nexus-settings`:

| Secret key | Required value |
|---|---|
| `AllowedHosts` | Explicit semicolon-separated hostnames accepted by that service, without schemes or ports. Include `sample` for Sample and `nexus` for Nexus because kubelet probes send those Host headers. Retain the actual ingress hostname for Sample and other internal request hostnames where applicable. Configure these values before rolling out the probes. Do not use `*` for public staging or production. |
| `DrnAppFeatures__SeedKey` | A secret seed distinct from DRN's built-in default and test sample. Keep it stable for the application's stored data and security configuration. |
| `NexusAppSettings__Keys__0__KeyMaterial` | Secret key material decoding to exactly 32 bytes. Never use the checked-in DRN example. |
| `NexusAppSettings__Keys__0__Format` | `Base64` when using Base64-encoded key material. Other DRN formats require matching encoded material. |
| `NexusAppSettings__Keys__0__Default` | The string `true`. |
| `postgresql-root-ca.crt` | PEM CA certificate bundle trusted for the database endpoint. Projected separately at `/appcerts/postgresql/root.crt`. |

Required connection keys in `sample-settings`:

- `ConnectionStrings__QAContext`
- `ConnectionStrings__QBContext`
- `ConnectionStrings__SampleIdentityContext`
- `ConnectionStrings__DrnDataProtectionContext`

Required connection key in `nexus-settings`:

- `ConnectionStrings__NexusIdentityContext`

Each value is a complete Npgsql connection string with host, port, database, username, password, and verified TLS settings. For the stage CloudNativePG cluster, the service is `postgresql-rw`, database is `drnDb`, and the bootstrap owner is `drn`. Obtain the current password from the operator-managed credentials through the trusted provisioning process. Contexts can use this database with DRN's context-specific schemas. Staging and production use independent clusters, credentials, and data.

Use `SSL Mode=VerifyFull;Root Certificate=/appcerts/postgresql/root.crt` and a host matching the database certificate. Confirm the issued certificate's subject alternative names cover the selected DNS name, including `postgresql-rw` if using that short name. Provision the matching CA bundle explicitly. The workload manifests do not copy or synchronize the operator's CA into application Secrets. Update these copies when the database CA rotates. `SSL Mode=Require` alone does not authenticate the database endpoint.

Stage migration credentials need the DDL privileges required by the application's migrations. Production runtime credentials should have only runtime permissions, with a separate privileged migration procedure. No production database Cluster or migration Job is supplied by these scaffolds.

Keep seeds and ID key material separate across environments. Do not regenerate them at each deployment. Sample persists ASP.NET data-protection keys in `DrnDataProtectionContext`; changing its seed can prevent reading existing protected material. The current projection permits one default Nexus key. A planned key rotation that retains older parsing keys must also extend the projected key list before rollout.

## Authentication and image readiness

The pinned Sample image's staging settings disable `Identity:RequireConfirmedAccount`, `RequireConfirmedEmail`, and `RequireConfirmedPhoneNumber`. Nexus has no staging settings file and defaults all three to true. Production Sample also defaults them to true. Establish the intended account provisioning and confirmation flows before exposing either service to beta users. Change the environment's nonsecret JSON deliberately if a different policy is needed.

The local DRN source tag `release/v0.10.1-preview003` includes these environment, migration, and secret-validation contracts. Source inspection alone does not prove the provenance or runtime behavior of the pinned container digests. Validate startup, connectivity, identity flows, and migrations before making stage available to users.

The prod scaffold currently inherits the existing preview image pins. Keep prod disabled until approved final image digests, database provisioning and migration, secrets, identity flows, public TLS, persistence, backups, and recovery have been validated. A final GitOps snapshot tag does not independently certify its embedded images as production-ready.

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
