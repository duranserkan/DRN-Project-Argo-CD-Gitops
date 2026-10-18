# Local access to platform UIs and databases

Use these commands from your workstation after the services have deployed. Staging and production use separate clusters with the same resource names. Confirm the intended context first:

```sh
kubectl config current-context
```

Run each tunnel in its own terminal. Keep it running while using the UI and press `Ctrl+C` to close it. The commands below use local-only listeners. A tunnel grants network access, but application login still applies where enabled. These commands are operator instructions and have not been exercised against a live cluster as part of this documentation change.

## Quick reference

| Service | Command | Local access |
|---|---|---|
| Graylog | `kubectl -n graylog port-forward --address localhost svc/drn-graylog 9000:9000` | [http://localhost:9000](http://localhost:9000) |
| Argo CD | `kubectl -n argocd port-forward --address localhost svc/argocd-server 8080:443` | [https://localhost:8080](https://localhost:8080) |
| Linkerd Viz | `linkerd viz dashboard` | Use the local URL printed by the command |
| Grafana | Use the running Linkerd Viz dashboard | Append `/grafana/` to the dashboard's local base URL |
| PostgreSQL, staging only | `kubectl -n drn-project port-forward --address 127.0.0.1 svc/postgresql-rw 15432:5432` | Database client on port 15432, with TLS settings below |
| MongoDB for Graylog | `kubectl -n graylog port-forward --address 127.0.0.1 pod/mongodb-0 27018:27017` | Database client on port 27018, with TLS settings below |

Use the matching Linkerd CLI version documented in the [installation guide](../README.md#install-argo-cd-and-log-in). See the upstream [port-forward reference](https://kubernetes.io/docs/reference/kubectl/generated/kubectl_port-forward/) and [Linkerd Viz CLI reference](https://linkerd.io/2-edge/reference/cli/viz/).

## Graylog

```sh
kubectl -n graylog port-forward --address localhost svc/drn-graylog 9000:9000
```

Open [Graylog](http://localhost:9000). For generated credentials, use username `admin` and retrieve the `password` field from the `graylog-admin-password` Secret privately. Use the current credentials if they have been changed. If setup is requested, follow the [Graylog operations guide](platform-operations.md#graylog) and use the setup credentials from the pod logs. Do not paste passwords or setup credentials into shared logs or screenshots.

The configured external URI is `http://localhost:9000/`. Prefer local port 9000 so generated links match. Changing the access address requires reviewing `graylog.config.network.externalUri` in the [Graylog Application](../deployment/logging/templates/graylog/graylog.yaml).

Port-forwarding cannot repair a crashing server. If the connection fails, inspect the pod and the previous application instance:

```sh
kubectl -n graylog get pods
kubectl -n graylog logs graylog-0 -c graylog-app --previous --tail=100
```

If no previous container exists, omit `--previous`. MongoDB error 13 on `collMod` requires reconciling the database-scoped roles described in the operations guide. Recreating Graylog, its Secrets or its database is not part of that fix.

## Argo CD

```sh
kubectl -n argocd port-forward --address localhost svc/argocd-server 8080:443
```

Open [Argo CD](https://localhost:8080). Use HTTPS. The default server certificate is self-signed. Verify the intended cluster before accepting its certificate prompt for this local connection.

Use your configured account. For first-time administrator access, follow [Install Argo CD and log in](../README.md#install-argo-cd-and-log-in), including initial-password retrieval and password replacement. The initial-password Secret does not track later password changes.

## Linkerd Viz and Grafana

```sh
linkerd viz dashboard
```

Use the local URL printed by the command. Open its `/grafana/` path for Grafana. The [Grafana configuration](../deployment/monitoring/templates/grafana/grafana.yaml) uses that subpath, disables the login form and provides anonymous Viewer access. There is no initial Grafana admin account. Dashboards are provisioned from Git and local changes are not persisted.

Keep this command running in a separate terminal from the other tunnels. See [Grafana and secrets](platform-operations.md#grafana-and-secrets) for provisioning details.

## PostgreSQL

The bundled PostgreSQL cluster exists only in staging. Production uses an externally provisioned database. The [cluster configuration](../services/postgresql/base/cluster.yaml) initializes database `drnDb` with owner `drn` and disables superuser access. Use the primary service for this connection:

```sh
kubectl -n drn-project port-forward --address 127.0.0.1 svc/postgresql-rw 15432:5432
```

In another terminal, save only the public CA certificate to a new local file. Run this from a private working directory outside the repository:

```sh
kubectl -n drn-project get secret postgresql-ca -o jsonpath='{.data.ca\.crt}' | base64 --decode > postgresql-root.crt
```

Retrieve the `password` field from `postgresql-app` privately. Connect with a local `psql` client and enter that password at the prompt:

```sh
psql "host=postgresql-rw hostaddr=127.0.0.1 port=15432 dbname=drnDb user=drn sslmode=verify-full sslrootcert=postgresql-root.crt" -W
```

`hostaddr` selects the local tunnel address, while `host` retains the database hostname for certificate verification. Do not replace both with `localhost`. See [libpq connection parameters](https://www.postgresql.org/docs/18/libpq-connect.html). The cluster rejects plaintext connections.

For GUI clients, use database `drnDb`, user `drn`, local port `15432`, the public CA and full certificate verification. Configure a separate connection address and certificate hostname if supported. Otherwise, temporarily map `postgresql-rw` to `127.0.0.1` in your workstation's hosts file and connect using `postgresql-rw`. Remove only that temporary entry when finished. Do not disable hostname verification to work around the tunnel.

The generated application's `pgpass` file uses the in-cluster endpoint and port. It will not match port `15432` unchanged. The password prompt avoids that mismatch. See [staging database credentials](staging-configuration.md#staging-database-credentials) for the application-mounted configuration.

## MongoDB for Graylog

The [MongoDB resource](../infrastructure/graylog/mongodb/mongodb-custom-resource.yaml) currently has one member. Forward that pod for a direct administrative connection:

```sh
kubectl -n graylog port-forward --address 127.0.0.1 pod/mongodb-0 27018:27017
```

Save the public CA bundle from the generated ConfigMap to a new file outside the repository:

```sh
kubectl -n graylog get configmap graylog-database-trust -o jsonpath='{.data.ca\.crt}' > graylog-mongodb-ca.crt
```

The [MongoDB certificate](../infrastructure/graylog/mongodb/mongodb-server-certificate.yaml) covers `mongodb-svc`, but not `localhost`. Temporarily add this mapping to your workstation's hosts file (`/etc/hosts` on macOS and Linux):

```text
127.0.0.1 mongodb-svc
```

Retrieve the `password` field from `mongo-graylog-password` privately. Connect with a local `mongosh` client and enter it at the prompt:

```sh
mongosh "mongodb://mongodb-svc:27018/graylog?authSource=graylog&directConnection=true" \
  --tls --tlsCAFile graylog-mongodb-ca.crt --username graylog --password
```

This keeps certificate verification enabled. `directConnection=true` prevents replica-set discovery from redirecting the client to cluster-only addresses outside the tunnel. See the [mongosh connection options](https://www.mongodb.com/docs/mongodb-shell/reference/options/) and [direct connection behavior](https://www.mongodb.com/docs/manual/reference/connection-string-options/).

For MongoDB Compass, use the same URI, username, authentication database and CA file. Enter the password separately. The dedicated user has `readWrite` and `dbAdmin` only on `graylog`. Use this access carefully because it permits data changes. When finished, close the tunnel and remove only the temporary `mongodb-svc` hosts entry. Revisit the target pod and connection method if the deployment grows beyond one member.

Data Node and OpenSearch are managed by Graylog. Use Graylog's UI for normal operations. A tunnel alone does not supply their API credentials or TLS trust configuration.

## Troubleshooting

| Symptom | Check |
|---|---|
| Service or namespace not found | Confirm the Kubernetes context and whether the owning Argo CD Application has deployed. |
| Local address already in use | Stop the existing tunnel or choose another local port. For Argo CD, `8081:443` uses `https://localhost:8081`. |
| Tunnel exits after a rollout | Run the command again. A selected pod terminating ends its port-forward session. |
| Connection refused or Graylog startup 502 | Inspect application logs and pod status. An open tunnel does not establish application health. |
| Kubernetes reports `Forbidden` | Check your account's namespace access and permission to create `pods/portforward` requests. |
| Grafana login or redirect confusion | Use `/grafana/` through Linkerd Viz, as configured above. |
| Database certificate hostname mismatch | Keep the certificate-matching hostname and use the tunnel address as documented above. Do not turn off verification. |
| Database connection closes after failover | Restart the tunnel. PostgreSQL's `postgresql-rw` service selects the current primary when establishing a new session. |

Successful local access does not validate ingress routing, Linkerd mTLS or service-to-service authorization. Use the [readiness checks](platform-operations.md#readiness-checks) for deployment validation.
