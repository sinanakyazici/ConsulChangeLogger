# Kubernetes Onboarding Guide

This guide explains how to add Consul Change Logger to an existing Kubernetes environment where Consul, Elasticsearch, Kibana, and LDAP are already running.

The intended production shape is a single Consul Change Logger sidecar in the same pod as Consul. All browser traffic that previously reached Consul should be routed to Consul Change Logger instead. Consul Change Logger authenticates the user, forwards allowed Consul HTTP UI/API requests to Consul, and writes Consul KV change records to Elasticsearch.

## 1. Collect Existing System Information

Prepare these values before changing anything:

| Value | Example | Why it is needed |
| --- | --- | --- |
| Consul namespace | `consul` | Secret, PVC, Service, and sidecar changes must be applied in the same namespace as the Consul pod. |
| Consul workload type | `Deployment` or `StatefulSet` | The sidecar patch method depends on the Kubernetes workload type. |
| Consul workload name | `consul-server` | This is the workload that will receive the Consul Change Logger sidecar container. It may be different from the Service name. |
| Consul container port | `8500` | Consul Change Logger forwards Consul HTTP UI/API traffic to this port inside the same pod. |
| Consul UI Service name | `consul-ui` | This browser-facing Service must be updated so user traffic targets the sidecar port instead of the original Consul port. Service names can differ from workload names. |
| Consul UI Service port | `80` | Ingress usually points to this Service port, so it must remain stable during the rollout. |
| Public Consul hostname | `https://consul.company.local` | Used to verify browser login and final user access. |
| Internal Consul URL from the same pod | `http://127.0.0.1:8500` | This is configured in the application's `appsettings.json` as `ConsulConfiguration.UpstreamUrl`. |
| Elasticsearch URL reachable from the pod | `https://elasticsearch.logging.svc.cluster.local:9200` | Change records are indexed here, and readiness checks depend on this endpoint. |
| Kibana URL | `https://kibana.company.local` | Used by operators to create the data view and inspect change records. The application does not call Kibana. |
| LDAP domain | `ldap.company.local` | Hostname used for login authentication. |
| LDAP port / secure port | `389` / `636` | Selected by `LdapConfiguration.UseSSL`. |
| LDAP search base | `dc=company,dc=local` | Defines where user and group searches start in the LDAP tree. |
| LDAP bind DN | `cn=readonly,ou=service-users,dc=company,dc=local` | Optional readonly account used for LDAP searches when anonymous search is not allowed. |
| LDAP search filter | `(mail={0})` | Defines how a login value maps to an LDAP user. |

If you do not know these values, start with these discovery commands:

```powershell
kubectl get pods -A | findstr consul
kubectl get svc -A | findstr consul
kubectl get ingress -A | findstr consul
```

Then inspect the workload:

```powershell
kubectl describe deployment <consul-workload-name> -n <consul-namespace>
```

or:

```powershell
kubectl describe statefulset <consul-workload-name> -n <consul-namespace>
```

In many Consul installations, the names are split like this:

| Kubernetes object | Example from the cluster |
| --- | --- |
| Pod | `consul-server-0` |
| Workload | `StatefulSet/consul-server` |
| Service used by browser traffic | `Service/consul-ui` |
| Namespace | `consul` |

This is expected. Add the sidecar to the Consul workload, for example `StatefulSet/consul-server`, but route user traffic by updating the Service that users already hit, for example `Service/consul-ui`.

### Which Namespace?

Use the namespace where the existing Consul pod/workload runs. This is the namespace that contains the Deployment or StatefulSet serving the current Consul HTTP UI/API.

Consul Change Logger is added as a sidecar to that same Consul pod, so these resources should be created or updated in the Consul namespace:

- Consul Deployment/StatefulSet
- Consul UI Service that receives browser traffic, for example `consul-ui`
- Consul Change Logger sidecar container
- `consul-change-logger-outbox` PVC

Find it with:

```powershell
kubectl get pods -A | findstr consul
```

Example output:

```text
consul     consul-6d7f8c9d9b-abc12     2/2     Running
```

In this example, the namespace is:

```text
consul
```

Recommended assumption:

```text
Only one Consul Change Logger pod/sidecar is active.
```

Single-pod operation keeps `old_value` matching deterministic because the read cache is local memory.

## 2. Install the Helm Chart

Install the chart from GHCR OCI:

```powershell
helm install consul-change-logger oci://ghcr.io/sinanakyazici/charts/consul-change-logger --version 1.0.0 -n <consul-namespace>
```

This chart intentionally creates only the product-owned PVC and prints the patch steps in `NOTES.txt`.

Verify the release:

```powershell
helm status consul-change-logger -n <consul-namespace>
```

## 3. Protect the Configuration Prefix

All runtime settings, including LDAP and Elasticsearch credentials, are stored in one Consul KV JSON document. Enable Consul ACLs and restrict read access to `consul-change-logger/appsettings.json` before using real credentials.

Use either `Elasticsearch.ApiKey` or `Elasticsearch.Username` + `Elasticsearch.Password`. If API key is set, it takes precedence.

## 4. Create Persistent Volumes
Consul Change Logger needs persistent writable storage for:

| Path | Purpose |
| --- | --- |
| `/var/lib/consul-change-logger/outbox` | Durable retry buffer for Elasticsearch delivery |

If you install through Helm, the PVC is created by the chart. Confirm it exists:

```powershell
kubectl get pvc -n <consul-namespace>
```

Adjust size and storage class through Helm values before install if needed.

## 5. Seed Runtime Configuration in Consul KV

Consul Change Logger reads all runtime configuration from this Consul KV key:

```text
consul-change-logger/appsettings.json
```

Run the seed script from an environment where the `consul` CLI can reach your Consul cluster:

```sh
sh k8s/consul-config-seed.example.sh
```

Review [`k8s/appsettings.consul.example.json`](../k8s/appsettings.consul.example.json), replace its endpoint and credential values, then store the entire JSON document as the value of this key.

## 6. Add the Sidecar to the Consul Pod

Patch the existing Consul Deployment/StatefulSet using the sidecar snippet produced by the chart `NOTES.txt`.

Key points:

- `CONSUL_UPSTREAM_URL` should point to the in-pod Consul HTTP endpoint, usually `http://127.0.0.1:8500`
- `CONSUL_CONFIG_KEY` should point to the Consul KV JSON document
- `CONSUL_HTTP_TOKEN` is optional and only needed if Consul KV bootstrap access requires it
- The sidecar listens on port `8080`.
- The pod-level `fsGroup` should allow the non-root container user to write mounted PVCs.
- The outbox path must be mounted as a writable volume.

Apply your patched workload manifest:

```powershell
kubectl apply -f your-consul-workload.yaml
```

Watch rollout:

```powershell
kubectl rollout status deployment/consul -n consul
```

Use the correct workload kind/name for your environment. The chart does not patch the workload automatically.

## 7. Route Consul Traffic Through the Sidecar

Patch the existing Consul UI Service so it targets the Consul Change Logger sidecar port instead of the original Consul port.

Before:

```yaml
targetPort: 8500
```

After:

```yaml
targetPort: logger-http
```

Example patch target:

```yaml
apiVersion: v1
kind: Service
metadata:
  name: consul-ui
  namespace: consul
spec:
  ports:
    - name: http
      port: 80
      targetPort: logger-http
```

Your Ingress hostname can stay the same. The user should continue opening the existing Consul web UI URL, but traffic now lands on Consul Change Logger first.

## 8. Verify Health

Port-forward the existing browser-facing Service:

```powershell
kubectl port-forward svc/<consul-ui-service-name> -n <consul-namespace> 8080:<consul-ui-service-port>
```

Check liveness:

```powershell
curl http://localhost:8080/health/live
```

Expected:

```json
{"status":"live"}
```

Check readiness:

```powershell
curl http://localhost:8080/health/ready
```

Expected:

```json
{"status":"ready"}
```

If readiness fails, check:

- `CONSUL_UPSTREAM_URL`
- `CONSUL_CONFIG_KEY`
- Elasticsearch URL/auth/TLS
- `CONSUL_HTTP_TOKEN` if used
- Consul KV config prefix

## 9. Verify Login

Open the existing Consul web UI URL:

```text
https://consul.company.local/ui/
```

Expected flow:

1. Consul Change Logger shows the login page.
2. User enters email/password.
3. LDAP authentication succeeds.
5. User is redirected to the Consul web UI.

If login fails:

- Confirm `LdapConfiguration.Domain`, ports, `SearchBase`, `SearchFilter`, and `UseSSL`.
- Confirm `LdapConfiguration.BindCredentials` is correct when `BindDn` is configured.
- Check sidecar logs.

```powershell
kubectl logs deployment/consul -n consul -c consul-change-logger --tail=200
```

## 10. Verify Change Logging

In the Consul web UI:

1. Open a KV key.
2. Read/view its current value.
3. Modify the value.
4. Save the change.

This order matters because `old_value` is captured from the previous read through Consul Change Logger.

Then query Elasticsearch:

```powershell
curl -k "https://elasticsearch.logging.svc.cluster.local:9200/consul-change-logger/_search?pretty&size=10&sort=@timestamp:desc"
```

Expected fields:

```text
@timestamp
event_id
action
kv_key
old_value
new_value
success
response_code
client_ip
user_email
user_agent
request_id
source_path
source
```

## 11. View Records in Kibana

Open Kibana and create a data view:

| Field | Value |
| --- | --- |
| Data view name | `consul-change-logger` |
| Index pattern | `consul-change-logger` |
| Time field | `@timestamp` |

Open Discover and filter with KQL:

```text
action: "kv_write"
```

```text
user_email: "user@example.com"
```

```text
kv_key: "app/config"
```

Useful dashboard fields:

- `user_email`
- `kv_key`
- `action`
- `success`
- `response_code`
- `@timestamp`

## 12. Verify Outbox Behavior

When Elasticsearch is healthy, outbox files are written and then deleted after successful delivery. An empty outbox is normal.

To test retry behavior safely in a non-production environment:

1. Temporarily point `Elasticsearch.Url` to an unreachable endpoint in `consul-change-logger/appsettings.json`.
2. Modify a KV key.
3. Confirm a JSON file appears under:

```text
/var/lib/consul-change-logger/outbox/yyyy-MM-dd/
```

4. Restore `Elasticsearch.Url`.
5. Restart the sidecar or wait for retry.
6. Confirm the file is delivered and deleted.

## 13. Production Checklist

Before enabling this for all users:

- Consul traffic routes through Consul Change Logger.
- Consul ACLs are enabled.
- Elasticsearch TLS/auth works.
- Consul ACLs restrict read access to the configuration prefix containing credentials.
- Outbox PVC is mounted and writable.
- Sidecar runs as non-root.
- Root filesystem is read-only.
- `/health/ready` returns ready.
- A test KV change appears in Elasticsearch.
- Kibana data view exists.

## 14. Rollback Plan

Rollback is simple because Consul Change Logger sits in front of Consul.

To rollback:

1. Change the Consul UI Service `targetPort` back to the original Consul port, for example `8500`.
2. Roll back the Consul Deployment/StatefulSet to remove the sidecar if needed.
3. Keep Elasticsearch data for investigation, or delete the `consul-change-logger` index if it was only a test.

Rollback service example:

```yaml
ports:
  - name: http
    port: 80
    targetPort: 8500
```

## 15. Recommended First Pilot

Start with a limited pilot:

1. Use one namespace.
2. Use one Consul instance.
3. Allow only a small LDAP group.
4. Ask one user to read and update one non-sensitive KV key.
5. Verify the record in Kibana.
6. Keep the previous Service targetPort ready for rollback.

After the pilot succeeds, keep the same pattern for production rollout.
