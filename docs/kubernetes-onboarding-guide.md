# Kubernetes Onboarding Guide

This guide explains how to add Consul Change Logger to an existing Kubernetes environment where Consul, Elasticsearch, Kibana, and LDAP are already running.

The intended production shape is a single Consul Change Logger gateway Deployment in front of the existing browser-facing Consul endpoint. Consul itself is not modified or owned by this product.

## 1. Target Traffic Model

The product is designed for this routing model:

```text
Browser users -> existing Consul hostname -> Consul Change Logger -> existing Consul Service
Applications  -> existing Consul hostname -> Consul Change Logger -> existing Consul Service
```

Path behavior:

- `/` and `/ui/*` require an authenticated browser session.
- authenticated browser `/v1/kv/*` traffic can be audited.
- unauthenticated `/v1/*` traffic uses fast pass-through and is not audited.

This keeps existing application Consul API calls working while adding login and change logging for browser-based Consul UI usage.

## 2. Collect Existing System Information

Prepare these values before changing anything:

| Value | Example | Why it is needed |
| --- | --- | --- |
| Install namespace | `consul` | Namespace where the gateway Deployment, Service, and PVC will be created. |
| Existing Consul HTTP URL reachable from the gateway pod | `http://consul-server.consul.svc.cluster.local:8500` | Used as `CONSUL_UPSTREAM_URL`. |
| Existing public Consul hostname | `https://consul.company.local` | This hostname should route to Consul Change Logger after rollout. |
| Elasticsearch URL reachable from the gateway pod | `https://elasticsearch.logging.svc.cluster.local:9200` | Change records are indexed here. |
| Kibana URL | `https://kibana.company.local` | Used by operators to inspect change records. The application does not call Kibana. |
| LDAP domain | `ldap.company.local` | Hostname used for login authentication. |
| LDAP port / secure port | `389` / `636` | Selected by `LdapConfiguration.UseSSL`. |

Discovery commands:

```powershell
kubectl get svc -A | findstr consul
kubectl get ingress -A | findstr consul
kubectl get svc -A | findstr elastic
kubectl get svc -A | findstr ldap
```

## 3. Install the Helm Chart

Install the chart from GHCR OCI:

```powershell
helm install consul-change-logger oci://ghcr.io/sinanakyazici/charts/consul-change-logger --version <chart-version> -n <install-namespace> --set bootstrap.consulUpstreamUrl="http://consul-server.consul.svc.cluster.local:8500"
```

Upgrade:

```powershell
helm upgrade consul-change-logger oci://ghcr.io/sinanakyazici/charts/consul-change-logger --version <chart-version> -n <install-namespace> --set bootstrap.consulUpstreamUrl="http://consul-server.consul.svc.cluster.local:8500"
```

The chart creates:

- `Deployment/consul-change-logger`
- `Service/consul-change-logger`
- `PersistentVolumeClaim/consul-change-logger-outbox` when enabled

The default `replicaCount` is `1`. Keep it at one unless you also introduce shared session state and a multi-writer outbox storage model.

It does not create or modify Consul, Elasticsearch, Kibana, or LDAP.

## 4. Seed Runtime Configuration in Consul KV

Consul Change Logger reads runtime configuration from this Consul KV key:

```text
consul-change-logger/appsettings.json
```

Review [`k8s/appsettings.consul.example.json`](../k8s/appsettings.consul.example.json), replace endpoint and credential values, then store the full JSON document as the value of that key.

The runtime JSON contains:

- Elasticsearch URL, username, password, and index
- outbox path and retention settings
- LDAP host, ports, and SSL mode

Protect this key with Consul ACLs because it can contain plaintext credentials.

## 5. Route the Existing Consul Hostname to the Gateway

Point the existing browser-facing Consul hostname or load-balancer route to:

```text
Service: consul-change-logger
Port:    80
```

The upstream Consul service remains unchanged. Consul Change Logger forwards traffic to the URL configured by:

```text
CONSUL_UPSTREAM_URL
```

Do not change application configuration if applications already call the same hostname. Cookies-less `/v1/*` calls are fast pass-through and are not audited.

## 6. Verify Health

Port-forward the gateway Service:

```powershell
kubectl port-forward svc/consul-change-logger -n <install-namespace> 8080:80
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
- LDAP URL/port when authentication is enabled
- Consul KV runtime configuration

## 7. Verify Application Pass-Through

Send an application-style request without browser cookies:

```powershell
curl http://localhost:8080/v1/status/leader
```

Expected:

- request reaches Consul
- no login redirect
- no audit document
- no outbox file

This verifies the low-cost pass-through path used by non-browser Consul clients.

## 8. Verify Login

Open the existing Consul web UI URL:

```text
https://consul.company.local/ui/
```

Expected flow:

1. Consul Change Logger shows the login page.
2. User enters LDAP username/password.
3. LDAP authentication succeeds.
4. User is redirected to the Consul web UI.

If login fails, check gateway logs:

```powershell
kubectl logs deploy/consul-change-logger -n <install-namespace> --tail=200
```

## 9. Verify Change Logging

In the Consul web UI:

1. Open a KV key.
2. Read/view its current value.
3. Modify the value.
4. Save the change.

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
is_folder
old_value
old_value_observed_at
new_value
new_value_json_error
is_create
is_update
is_delete
is_success
response_status_code
client_ip
user_email
user_agent
request_id
source_path
source
```

## 10. View Records in Kibana

Create a data view:

| Field | Value |
| --- | --- |
| Data view name | `consul-change-logger` |
| Index pattern | `consul-change-logger` |
| Time field | `@timestamp` |

Useful KQL filters:

```text
action: "kv_write"
```

```text
user_email: "user@example.com"
```

```text
kv_key: "app/config"
```

## 11. Verify Outbox Behavior

When Elasticsearch is healthy, outbox files are written and then deleted after successful delivery. An empty outbox is normal.

To test retry behavior in a non-production environment:

1. Temporarily point `Elasticsearch.Url` to an unreachable endpoint in `consul-change-logger/appsettings.json`.
2. Modify a KV key from the browser UI.
3. Confirm a JSON file appears under:

```text
/var/lib/consul-change-logger/outbox/yyyy-MM-dd/
```

4. Restore `Elasticsearch.Url`.
5. Restart the gateway or wait for retry.
6. Confirm the file is delivered and deleted.

## 12. Production Checklist

- Existing Consul hostname routes through Consul Change Logger.
- Existing Consul Service remains unchanged.
- Application `/v1/*` traffic is pass-through and not audited.
- Browser `/ui/*` traffic requires login.
- Consul ACLs protect the runtime configuration key.
- Elasticsearch TLS/auth works.
- LDAP direct bind works.
- Outbox PVC is mounted and writable.
- Container runs as non-root.
- Root filesystem is read-only.
- `/health/ready` returns ready.
- A browser UI KV change appears in Elasticsearch.
- Kibana data view exists.

## 13. Rollback Plan

Rollback is simple because Consul Change Logger is only in front of the Consul endpoint.

To rollback:

1. Change the existing hostname/load-balancer/Ingress route back to the original Consul Service.
2. Keep Elasticsearch data for investigation, or delete the `consul-change-logger` index if it was only a test.
3. Uninstall the chart when no longer needed:

```powershell
helm uninstall consul-change-logger -n <install-namespace>
```
