# Consul Change Logger

[![CI](https://github.com/sinanakyazici/ConsulChangeLogger/actions/workflows/ci.yml/badge.svg)](https://github.com/sinanakyazici/ConsulChangeLogger/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Docker](https://img.shields.io/badge/Docker-ready-2496ED.svg)](src/ConsulChangeLogger.Proxy/Dockerfile)

Consul Change Logger is an ASP.NET Core reverse proxy that sits in front of Consul UI and the Consul HTTP API, authenticates users with LDAP, forwards allowed traffic to Consul, and records Consul KV write and delete activity into Elasticsearch.

The product is intentionally narrow:

- it focuses on KV change logging, not generic access logging
- it does not replace Consul ACLs
- it does not implement approval workflows
- it does not modify KV payloads before they reach Consul

## Product Model

Consul Change Logger is packaged as a Kubernetes sidecar product for environments where:

- `Consul` already exists
- `Elasticsearch`, `Kibana`, and `LDAP` already exist
- the existing browser-facing `consul-ui` Service can be patched to point at the sidecar

Ownership boundary:

- this product does not install or own Consul
- this product does not install Elasticsearch, Kibana, or LDAP
- this product owns only its own sidecar container spec, outbox PVC, and release artifacts

Official distribution artifacts:

- container image: `ghcr.io/sinanakyazici/consul-change-logger`
- Helm chart OCI package: `oci://ghcr.io/sinanakyazici/charts/consul-change-logger`
- GitHub Release asset: `consul-change-logger-X.Y.Z.tgz`

## Release Model

Releases are tag-driven.

When a semantic version tag such as `v1.2.3` is pushed, the release workflow publishes:

- container image:
  - `ghcr.io/sinanakyazici/consul-change-logger:v1.2.3`
  - `ghcr.io/sinanakyazici/consul-change-logger:latest`
- Helm OCI chart:
  - `oci://ghcr.io/sinanakyazici/charts/consul-change-logger --version 1.2.3`
- GitHub Release:
  - release notes
  - packaged chart asset `consul-change-logger-1.2.3.tgz`

Relevant workflows:

- [CI](.github/workflows/ci.yml)
- [Release](.github/workflows/release.yml)

## What It Does

- Authenticates browser users with LDAP before they can access Consul UI or the Consul API through the proxy.
- Proxies Consul UI assets and API requests to the upstream Consul endpoint.
- Captures KV reads and can prefetch single-key mutation targets to build a best-effort `old_value`.
- Captures KV writes and deletes as audit records.
- Writes each audit record to a local outbox file before Elasticsearch delivery.
- Retries Elasticsearch delivery until the record is accepted.
- Adds JSON validation metadata for `old_value` and `new_value`.
- Shows a browser warning before saving a KV value that looks like JSON but is invalid JSON.

## Install Model

The intended Kubernetes install flow is:

1. install the Helm chart
2. seed runtime config into Consul KV
3. patch the existing Consul workload to add the sidecar
4. patch the existing `consul-ui` Service so browser traffic goes to the sidecar
5. verify health, login, and audit delivery

The Helm chart intentionally creates only the product-owned PVC and prints patch guidance in `NOTES.txt`. It does not install or mutate the existing Consul workload for you.

Install example:

```powershell
helm install consul-change-logger oci://ghcr.io/sinanakyazici/charts/consul-change-logger --version 1.0.1 -n consul
```

Upgrade example:

```powershell
helm upgrade consul-change-logger oci://ghcr.io/sinanakyazici/charts/consul-change-logger --version 1.0.1 -n consul
```

Dry-run example against the published OCI chart:

```powershell
helm install consul-change-logger oci://ghcr.io/sinanakyazici/charts/consul-change-logger --version 1.0.1 -n consul --create-namespace --dry-run --debug
```

The sidecar bootstrap contract is environment-variable based:

- `CONSUL_UPSTREAM_URL`
- `CONSUL_CONFIG_KEY`
- optional `CONSUL_HTTP_TOKEN`

The application also accepts ASP.NET configuration-style keys as a fallback:

- `ConsulConfiguration__UpstreamUrl`
- `ConsulConfiguration__ConfigKey`
- `ConsulConfiguration__HttpToken`

All remaining runtime settings are read from Consul KV.

### Current Rollout Steps

For the current `consul` / `StatefulSet/consul-server` / `Service/consul-ui` shape used in this repository, the rollout looks like this:

1. install the product-owned PVC:

```powershell
helm install consul-change-logger oci://ghcr.io/sinanakyazici/charts/consul-change-logger --version 1.0.1 -n consul
```

2. seed runtime config into:

```text
consul-change-logger/appsettings.json
```

3. patch the existing Consul workload:

```powershell
kubectl patch statefulset consul-server -n consul --patch-file .\k8s\consul-server-sidecar-patch.yaml
```

4. patch the existing browser-facing Service:

```powershell
kubectl patch service consul-ui -n consul --type=json --patch-file .\k8s\consul-ui-service-targetport-patch.json
```

5. verify rollout:

```powershell
kubectl rollout status statefulset/consul-server -n consul
kubectl port-forward svc/consul-ui -n consul 8080:80
curl http://localhost:8080/health/live
curl http://localhost:8080/health/ready
```

## End-to-End Flow

```mermaid
flowchart LR
    browser["Browser"]
    ui["Consul UI HTML + JS<br/>running in browser"]
    proxy["Consul Change Logger<br/>reverse proxy"]
    ldap["LDAP / Active Directory"]
    consulUi["Consul UI shell<br/>served by Consul"]
    consulApi["Consul KV API<br/>/v1/kv/..."]
    audit["Audit capture inside proxy"]
    cache["Read Cache"]
    outbox["Daily Outbox Files"]
    worker["Dispatch Worker"]
    elastic["Elasticsearch"]
    kibana["Kibana"]

    browser -->|GET /login, POST /login| proxy
    proxy -->|direct bind| ldap
    browser -->|GET /ui/*| proxy
    proxy -->|forward UI request| consulUi
    consulUi -->|HTML + JS| proxy
    proxy --> browser
    browser --> ui
    ui -->|GET/PUT /v1/kv/...| proxy
    proxy -->|forward API request| consulApi
    consulApi -->|API response| proxy
    proxy -->|observe KV read/write/delete| audit
    audit -->|store successful KV reads| cache
    audit -->|persist write/delete records| outbox
    outbox -->|enqueue| worker
    worker -->|PUT document| elastic
    elastic --> kibana
```

The important point is that audit logging happens inside the proxy while it is relaying Consul UI API traffic to Consul. The browser talks only to Consul Change Logger. Consul UI then triggers `/v1/kv/...` requests through that proxy path, and those requests are where reads are cached and writes/deletes are turned into audit records.

The architecture document contains a more detailed breakdown: [docs/architecture.md](docs/architecture.md)

## Authentication Model

Login uses direct LDAP bind. The value entered in the login form is sent directly as the LDAP bind identity.

Typical examples:

- `test.user@examplecorp.com`
- `service.account@company.com`

This matches environments where applications authenticate directly against Active Directory or another LDAP server using username and password, without first looking up the user DN through a search.

## Audit Record

Each KV write or delete can produce a document like this:

```json
{
  "@timestamp": "2026-06-16T10:03:13Z",
  "event_id": "2d0d2f599db54e01bfab9f5209250e6a",
  "action": "kv_write",
  "kv_key": "test/test1",
  "old_value": "{ \"a\" : 1 }",
  "old_value_looks_like_json": true,
  "old_value_json_validation_status": "valid_json",
  "old_value_is_valid_json": true,
  "old_value_json_error": null,
  "old_value_seen_at": "2026-06-16T10:02:00Z",
  "old_value_read_request_id": "ed27d99ba89e49ed9440a7638caabea2",
  "new_value": "{ \"a\" : 1 }",
  "new_value_looks_like_json": true,
  "new_value_json_validation_status": "valid_json",
  "new_value_is_valid_json": true,
  "new_value_json_error": null,
  "create_detected": false,
  "update_detected": true,
  "delete_detected": false,
  "success": true,
  "response_code": 200,
  "client_ip": "::1",
  "user_email": "test.user@examplecorp.com",
  "user_agent": "Mozilla/5.0",
  "request_id": "2d0d2f599db54e01bfab9f5209250e6a",
  "source_path": "/v1/kv/test/test1?dc=dc1&flags=0",
  "source": "consul-change-logger"
}
```

Current behavior:

- `old_value` is best-effort
- the proxy caches the most recent successful KV read per user/client/key identity
- if no matching cached read exists, the proxy can prefetch the current value before a single-key write or delete
- if a matching read is not found, `old_value` can still be `null`

## JSON Validation

Consul Change Logger does not block invalid JSON on the server side. It records validation metadata and lets the operator decide what to do with that information.

Validation statuses:

- `not_json`
- `valid_json`
- `invalid_json`

Current heuristic:

- if the value starts with `{` or `[` after leading whitespace is removed, it is treated as JSON-like
- JSON-like values are parsed
- valid parse -> `valid_json`
- invalid parse -> `invalid_json`
- everything else -> `not_json`

Browser-side warning:

- when a user clicks save in Consul UI
- if the outgoing KV body looks like JSON but cannot be parsed
- the browser asks whether the user still wants to continue

The proxy still allows the write if the user confirms.

## Logging

Console logging is intentionally verbose in the current build so the whole request path can be followed during testing.

The proxy logs:

- login page requests
- login attempts
- LDAP bind success and failure
- proxied Consul requests
- upstream response status and byte counts
- KV read cache behavior
- audit record creation
- outbox writes
- queue and dispatch activity
- Elasticsearch index setup and document delivery
- invalid JSON detection

## Configuration

Bootstrap configuration defaults exist in `src/ConsulChangeLogger.Proxy/appsettings.json`, but the intended production path is environment variables on the sidecar container:

```json
{
  "ConsulConfiguration": {
    "UpstreamUrl": "http://localhost:8500",
    "ConfigKey": "consul-change-logger/appsettings.local.json"
  }
}
```

Production bootstrap contract:

```text
CONSUL_UPSTREAM_URL
CONSUL_CONFIG_KEY
CONSUL_HTTP_TOKEN (optional)
```

Fallback binding keys:

```text
ConsulConfiguration__UpstreamUrl
ConsulConfiguration__ConfigKey
ConsulConfiguration__HttpToken
```

Runtime configuration is read from the Consul KV key referenced by `CONSUL_CONFIG_KEY`.

Example runtime configuration:

```json
{
  "Elasticsearch": {
    "Url": "https://localhost:9200",
    "Username": "elastic",
    "Password": "your-password",
    "ApiKey": "",
    "Index": "consul-change-logger",
    "RetryDelaySeconds": 2,
    "SkipCertificateValidation": true
  },
  "ChangeLog": {
    "OutboxPath": ".local-data/outbox",
    "DataProtectionPath": ".local-data/data-protection",
    "ReadMatchWindowSeconds": 1800,
    "MaxBodyBytes": 8192,
    "QueueCapacity": 1000,
    "RetentionDays": 30
  },
  "LdapConfiguration": {
    "Domain": "localhost",
    "Port": 1389,
    "SecurePort": 1636,
    "BindDn": "svc-ldap-bind@examplecorp.com",
    "BindCredentials": "Passw0rd!123",
    "SearchBase": "OU=Accounts,OU=Region,OU=Organization,DC=examplecorp,DC=com",
    "SearchFilter": "(&(objectClass=user)(objectCategory=person)",
    "UseSSL": false
  }
}
```

Notes:

- `SearchFilter` is currently not used during direct bind login. It remains in the runtime contract for compatibility and future lookup scenarios.
- `BindDn` and `BindCredentials` are currently not used during direct bind login.

## Runtime Verification

After sidecar rollout, the minimum useful checks are:

1. open the existing Consul URL and confirm the login screen appears
2. sign in with LDAP
3. read a KV key
4. modify and save that KV key
5. confirm the change appears in Elasticsearch and Kibana

Expected behavior:

- `/health/live` returns live
- `/health/ready` returns ready
- a KV read followed by a KV write can populate best-effort `old_value`
- invalid JSON-like values trigger a browser warning before save
- successful audit events are written to the `consul-change-logger` index

## Local AD Test Environment

This repository now includes a Samba-based local Active Directory test environment for Kubernetes:

- [k8s/samba-ad.yaml](k8s/samba-ad.yaml)
- [k8s/samba-ad-ui.yaml](k8s/samba-ad-ui.yaml)

This environment provides:

- a local Samba AD-compatible LDAP endpoint
- a phpLDAPadmin UI
- a seeded OU structure
- a seeded service account
- a seeded test user

Test user:

```text
test.user@examplecorp.com
UserPass123!
```

Service account:

```text
svc-ldap-bind@examplecorp.com
Passw0rd!123
```

If you are using the local port-forward setup from this workspace, the endpoints are:

- LDAP: `localhost:1389`
- LDAPS: `localhost:1636`
- LDAP UI: `http://localhost:9081/`

## Elasticsearch and Kibana

The current default Elasticsearch index name is:

```text
consul-change-logger
```

Useful Kibana filters:

```text
action : "kv_write"
kv_key : "test/test1"
user_email : "test.user@examplecorp.com"
new_value_json_validation_status : "invalid_json"
```

If Discover is empty even though documents exist, create a data view for:

```text
consul-change-logger
```

with:

```text
@timestamp
```

as the time field.

## Health Endpoints

- `/health/live`
- `/health/ready`

`/health/ready` verifies Consul and Elasticsearch connectivity.

## Kubernetes

Primary install artifact:

- `chart/consul-change-logger`

Supporting examples and notes:

- [k8s/sidecar-snippet.yaml](k8s/sidecar-snippet.yaml)
- [k8s/service-example.yaml](k8s/service-example.yaml)
- [k8s/pvc-example.yaml](k8s/pvc-example.yaml)
- [k8s/consul-server-sidecar-patch.yaml](k8s/consul-server-sidecar-patch.yaml)
- [k8s/consul-ui-service-patch.yaml](k8s/consul-ui-service-patch.yaml)
- [k8s/consul-ui-service-targetport-patch.json](k8s/consul-ui-service-targetport-patch.json)
- [k8s/appsettings.consul.example.json](k8s/appsettings.consul.example.json)
- [k8s/consul-config-seed.example.sh](k8s/consul-config-seed.example.sh)
- [docs/kubernetes-onboarding-guide.md](docs/kubernetes-onboarding-guide.md)

## Security Notes

- Keep Consul ACLs enabled in production.
- Treat Consul KV runtime configuration as sensitive because it can contain plaintext credentials.
- Persist `ChangeLog.OutboxPath` if you need crash recovery across restarts.
- The product stores raw KV values in Elasticsearch by design.
- The login cookie contains only an opaque session id. No LDAP password is stored in the browser.

## Build

```powershell
dotnet build ConsulChangeLogger.slnx --configuration Release
```

## Repository Layout

```text
src/ConsulChangeLogger.Proxy     Reverse proxy, auth, shared models, audit pipeline
tests/ConsulChangeLogger.Tests   xUnit tests for core helpers and audit flow support code
k8s                              Kubernetes examples, rollout patches, and local AD lab manifests
docs                             Architecture and onboarding docs
chart/consul-change-logger       Helm chart for product-owned PVC and rollout instructions
```

## License

MIT. See [LICENSE](LICENSE).
