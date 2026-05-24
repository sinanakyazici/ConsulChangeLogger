# Kubernetes Onboarding Guide

This guide explains how to add Consul Change Logger to an existing Kubernetes environment where Consul, Consul UI, Elasticsearch, Kibana, and LDAP are already running.

The intended production shape is a single Consul Change Logger sidecar in the same pod as Consul UI. All browser traffic that previously reached Consul UI should be routed to Consul Change Logger instead. Consul Change Logger authenticates the user, forwards allowed Consul UI/API requests to Consul, and writes Consul KV change records to Elasticsearch.

## 1. Collect Existing System Information

Prepare these values before changing anything:

| Value | Example |
| --- | --- |
| Kubernetes namespace for Consul UI | `consul` |
| Consul UI Deployment/StatefulSet name | `consul-ui` |
| Consul UI container port | `8500` |
| Internal Consul URL from the same pod | `http://127.0.0.1:8500` |
| Elasticsearch URL reachable from the pod | `https://elasticsearch.logging.svc.cluster.local:9200` |
| Kibana URL | `https://kibana.company.local` |
| LDAP URL | `ldaps://ldap.company.local:636` |
| LDAP base DN | `dc=company,dc=local` |
| LDAP bind DN | `cn=readonly,ou=service-users,dc=company,dc=local` |
| LDAP user filter | `(mail={0})` |
| LDAP group allowlist filter | `(&(objectClass=group)(cn=consul-admins)(member={0}))` |
| Public Consul UI hostname | `https://consul.company.local` |

Recommended assumption:

```text
Only one Consul Change Logger pod/sidecar is active.
```

Single-pod operation keeps `old_value` matching deterministic because the read cache is local memory.

## 2. Build and Publish the Image

Build the image:

```powershell
docker build -f src\ConsulChangeLogger.Proxy\Dockerfile -t your-registry/consul-change-logger:1.0.0 .
```

Push it:

```powershell
docker push your-registry/consul-change-logger:1.0.0
```

Update `k8s/sidecar-snippet.yaml`:

```yaml
image: your-registry/consul-change-logger:1.0.0
```

## 3. Create Kubernetes Secrets

Create a secret for LDAP and Elasticsearch credentials. Do not store these values in Consul KV.

```yaml
apiVersion: v1
kind: Secret
metadata:
  name: consul-change-logger-secrets
  namespace: consul
type: Opaque
stringData:
  ldap-bind-password: change-me
  elasticsearch-username: elastic
  elasticsearch-password: change-me
  elasticsearch-api-key: ""
```

Apply it:

```powershell
kubectl apply -f k8s/secret-example.yaml
```

Use either `ELASTICSEARCH_API_KEY` or `ELASTICSEARCH_USERNAME` + `ELASTICSEARCH_PASSWORD`. If API key is set, it takes precedence.

## 4. Create Persistent Volumes

Consul Change Logger needs persistent writable storage for:

| Path | Purpose |
| --- | --- |
| `/var/lib/consul-change-logger/outbox` | Durable retry buffer for Elasticsearch delivery |
| `/var/lib/consul-change-logger/dp-keys` | ASP.NET cookie protection keys |

Apply the example PVCs:

```powershell
kubectl apply -f k8s/pvc-example.yaml
```

Adjust storage size and storage class according to your cluster policy.

## 5. Seed Runtime Configuration in Consul KV

Consul Change Logger reads non-secret runtime configuration from Consul KV under this prefix:

```text
consul-change-logger/config
```

Run the seed script from an environment where the `consul` CLI can reach your Consul cluster:

```sh
export CONSUL_CONFIG_PREFIX="consul-change-logger/config"
sh k8s/consul-config-seed.example.sh
```

For production, review and set these values explicitly:

```sh
consul kv put consul-change-logger/config/LISTEN_PORT "8080"
consul kv put consul-change-logger/config/CONSUL_ALLOWED_PATH_PREFIXES "/ui,/v1/kv,/v1/status,/v1/catalog,/v1/health,/v1/agent,/v1/internal"
consul kv put consul-change-logger/config/ELASTICSEARCH_URL "https://elasticsearch.logging.svc.cluster.local:9200"
consul kv put consul-change-logger/config/CHANGE_LOG_INDEX "consul-change-logger"
consul kv put consul-change-logger/config/CHANGE_LOG_OUTBOX_PATH "/var/lib/consul-change-logger/outbox"
consul kv put consul-change-logger/config/DATA_PROTECTION_PATH "/var/lib/consul-change-logger/dp-keys"
consul kv put consul-change-logger/config/CHANGE_LOG_RETENTION_DAYS "30"
consul kv put consul-change-logger/config/AUTH_COOKIE_SECURE "true"
consul kv put consul-change-logger/config/AUTH_MODE "ldap"
consul kv put consul-change-logger/config/LDAP_URL "ldaps://ldap.company.local:636"
consul kv put consul-change-logger/config/LDAP_BIND_DN "cn=readonly,ou=service-users,dc=company,dc=local"
consul kv put consul-change-logger/config/LDAP_BASE_DN "dc=company,dc=local"
consul kv put consul-change-logger/config/LDAP_USER_FILTER "(mail={0})"
consul kv put consul-change-logger/config/LDAP_GROUP_FILTER "(&(objectClass=group)(cn=consul-admins)(member={0}))"
```

Do not set these in Consul KV:

```text
LDAP_BIND_PASSWORD
ELASTICSEARCH_USERNAME
ELASTICSEARCH_PASSWORD
ELASTICSEARCH_API_KEY
```

They must come from Kubernetes Secret-backed environment variables.

## 6. Add the Sidecar to the Consul UI Pod

Edit the existing Consul UI Deployment/StatefulSet and add the Consul Change Logger container from `k8s/sidecar-snippet.yaml`.

Key points:

- `CONSUL_UPSTREAM_URL` should usually be `http://127.0.0.1:8500` when Consul UI and Consul Change Logger run in the same pod.
- The sidecar listens on port `8080`.
- The pod-level `fsGroup` should allow the non-root container user to write mounted PVCs.
- The outbox and data-protection paths must be mounted as writable volumes.

Minimal sidecar environment:

```yaml
env:
  - name: CONSUL_UPSTREAM_URL
    value: http://127.0.0.1:8500
  - name: CONSUL_CONFIG_PREFIX
    value: consul-change-logger/config
```

Secret-backed environment:

```yaml
env:
  - name: LDAP_BIND_PASSWORD
    valueFrom:
      secretKeyRef:
        name: consul-change-logger-secrets
        key: ldap-bind-password
        optional: true
  - name: ELASTICSEARCH_USERNAME
    valueFrom:
      secretKeyRef:
        name: consul-change-logger-secrets
        key: elasticsearch-username
        optional: true
  - name: ELASTICSEARCH_PASSWORD
    valueFrom:
      secretKeyRef:
        name: consul-change-logger-secrets
        key: elasticsearch-password
        optional: true
  - name: ELASTICSEARCH_API_KEY
    valueFrom:
      secretKeyRef:
        name: consul-change-logger-secrets
        key: elasticsearch-api-key
        optional: true
```

Apply the Deployment/StatefulSet change:

```powershell
kubectl apply -f your-consul-ui-workload.yaml
```

Watch rollout:

```powershell
kubectl rollout status deployment/consul-ui -n consul
```

Use the correct workload kind/name for your environment.

## 7. Route Consul UI Traffic Through the Sidecar

Update the existing Consul UI Service so it targets the Consul Change Logger sidecar port instead of the original Consul UI port.

Before:

```yaml
targetPort: 8500
```

After:

```yaml
targetPort: logger-http
```

Example:

```yaml
apiVersion: v1
kind: Service
metadata:
  name: consul-ui
  namespace: consul
spec:
  selector:
    app: consul
  ports:
    - name: http
      port: 80
      targetPort: logger-http
```

Your Ingress hostname can stay the same. The user should continue opening the existing Consul UI URL, but traffic now lands on Consul Change Logger first.

## 8. Verify Health

Port-forward the service:

```powershell
kubectl port-forward svc/consul-ui -n consul 8080:80
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

- Consul URL from the sidecar
- Elasticsearch URL/auth/TLS
- Kubernetes Secret values
- Consul KV config prefix

## 9. Verify Login

Open the existing Consul UI URL:

```text
https://consul.company.local/ui/
```

Expected flow:

1. Consul Change Logger shows the login page.
2. User enters email/password.
3. LDAP authentication succeeds.
4. `LDAP_GROUP_FILTER` allowlist check succeeds.
5. User is redirected to Consul UI.

If login fails:

- Confirm `AUTH_MODE=ldap`.
- Confirm `LDAP_URL`, `LDAP_BASE_DN`, `LDAP_USER_FILTER`, and `LDAP_GROUP_FILTER`.
- Confirm `LDAP_BIND_PASSWORD` exists in Kubernetes Secret.
- Check sidecar logs.

```powershell
kubectl logs deployment/consul-ui -n consul -c consul-change-logger --tail=200
```

## 10. Verify Change Logging

In Consul UI:

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

1. Temporarily point `ELASTICSEARCH_URL` to an unreachable endpoint.
2. Modify a KV key.
3. Confirm a JSON file appears under:

```text
/var/lib/consul-change-logger/outbox/yyyy-MM-dd/
```

4. Restore `ELASTICSEARCH_URL`.
5. Restart the sidecar or wait for retry.
6. Confirm the file is delivered and deleted.

## 13. Production Checklist

Before enabling this for all users:

- Consul UI traffic routes through Consul Change Logger.
- `AUTH_MODE=ldap`.
- `AUTH_COOKIE_SECURE=true`.
- `LDAP_GROUP_FILTER` is set.
- Consul ACLs are enabled.
- Elasticsearch TLS/auth works.
- Secret values are in Kubernetes Secret, not Consul KV.
- Outbox PVC is mounted and writable.
- Data-protection PVC is mounted and writable.
- Sidecar runs as non-root.
- Root filesystem is read-only.
- `/health/ready` returns ready.
- A test KV change appears in Elasticsearch.
- Kibana data view exists.

## 14. Rollback Plan

Rollback is simple because Consul Change Logger sits in front of Consul UI.

To rollback:

1. Change the Consul UI Service `targetPort` back to the original Consul UI port, for example `8500`.
2. Roll back the Consul UI Deployment/StatefulSet to remove the sidecar if needed.
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
2. Use one Consul UI instance.
3. Allow only a small LDAP group.
4. Ask one user to read and update one non-sensitive KV key.
5. Verify the record in Kibana.
6. Keep the previous Service targetPort ready for rollback.

After the pilot succeeds, keep the same pattern for production rollout.
