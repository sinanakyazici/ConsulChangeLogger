# Consul Change Logger

[![CI](https://github.com/sinanakyazici/ConsulChangeLogger/actions/workflows/ci.yml/badge.svg)](https://github.com/sinanakyazici/ConsulChangeLogger/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Docker](https://img.shields.io/badge/Docker-ready-2496ED.svg)](src/ConsulChangeLogger.Proxy/Dockerfile)
[![Docker Hub](https://img.shields.io/badge/Docker%20Hub-sinanakyazici%2Fconsul--change--logger-2496ED.svg)](https://hub.docker.com/r/sinanakyazici/consul-change-logger)

Consul Change Logger is an ASP.NET Core reverse proxy that sits in front of Consul UI and the Consul HTTP API, authenticates browser users with LDAP, forwards allowed traffic to Consul, and records Consul KV write and delete activity into Elasticsearch.

The product is intentionally narrow:

- it focuses on KV change logging, not generic access logging
- it does not replace Consul ACLs
- it does not implement approval workflows
- it does not modify KV payloads before they reach Consul

## Product Model

Consul Change Logger is packaged as a Kubernetes gateway product for environments where:

- `Consul` already exists
- `Elasticsearch`, `Kibana`, and `LDAP` already exist
- the existing Consul hostname can be routed through Consul Change Logger before reaching Consul

Ownership boundary:

- this product does not install or own Consul
- this product does not install Elasticsearch, Kibana, or LDAP
- this product owns only its own Deployment, Service, outbox PVC, and release artifacts

Official distribution artifacts:

- container image on GHCR: `ghcr.io/sinanakyazici/consul-change-logger`
- container image on Docker Hub: `docker.io/sinanakyazici/consul-change-logger`
- Helm chart OCI package: `oci://ghcr.io/sinanakyazici/charts/consul-change-logger`
- GitHub Release asset: `consul-change-logger-X.Y.Z.tgz`

## Release Model

Releases are tag-driven.

When a semantic version tag such as `v1.2.3` is pushed, the release workflow publishes:

- container image:
  - `ghcr.io/sinanakyazici/consul-change-logger:v1.2.3`
  - `ghcr.io/sinanakyazici/consul-change-logger:latest`
  - `docker.io/sinanakyazici/consul-change-logger:v1.2.3`
  - `docker.io/sinanakyazici/consul-change-logger:latest`
- Helm OCI chart:
  - `oci://ghcr.io/sinanakyazici/charts/consul-change-logger --version 1.2.3`
- GitHub Release:
  - release notes
  - packaged chart asset `consul-change-logger-1.2.3.tgz`

Relevant workflows:

- [CI-main](.github/workflows/ci.yml)
- [CI-release](.github/workflows/release-ci.yml)
- [Release-publish](.github/workflows/release.yml)

Docker Hub publishing requires these repository secrets:

- `DOCKERHUB_USERNAME`
- `DOCKERHUB_TOKEN`

If those secrets are not configured, the release still publishes GHCR and the Helm chart, but skips Docker Hub.

## What It Does

- Authenticates browser users with LDAP before they can access Consul UI through the proxy.
- Proxies Consul UI assets and Consul API requests to the upstream Consul endpoint.
- Captures KV reads and can prefetch single-key mutation targets to build a best-effort `old_value`.
- Captures KV writes and deletes as audit records.
- Writes each audit record to a local outbox file before Elasticsearch delivery.
- Retries Elasticsearch delivery until the record is accepted.
- Adds `new_value_json_error` when a new KV value looks like JSON but is invalid.
- Shows a browser warning before saving a KV value that looks like JSON but is invalid JSON.

## Install Model

The intended Kubernetes install flow is:

1. install the Helm chart
2. seed runtime config into Consul KV
3. route the existing Consul hostname/load balancer/Ingress to the Consul Change Logger Service
4. keep the existing Consul Service as the upstream target
5. verify health, login, and audit delivery

The Helm chart creates the product-owned Deployment, Service, and optional outbox PVC. It does not install or mutate the existing Consul workload.
The default gateway replica count is `1`; keep it that way unless shared session state and multi-writer outbox storage are introduced.

Install example:

```powershell
helm install consul-change-logger oci://ghcr.io/sinanakyazici/charts/consul-change-logger --version <chart-version> -n consul --set bootstrap.consulUpstreamUrl="http://consul-server.consul.svc.cluster.local:8500"
```

Install example using the Docker Hub image:

```powershell
helm install consul-change-logger oci://ghcr.io/sinanakyazici/charts/consul-change-logger --version <chart-version> -n consul --set image.repository=docker.io/sinanakyazici/consul-change-logger --set bootstrap.consulUpstreamUrl="http://consul-server.consul.svc.cluster.local:8500"
```

Upgrade example:

```powershell
helm upgrade consul-change-logger oci://ghcr.io/sinanakyazici/charts/consul-change-logger --version <chart-version> -n consul --set bootstrap.consulUpstreamUrl="http://consul-server.consul.svc.cluster.local:8500"
```

Dry-run example against the published OCI chart:

```powershell
helm install consul-change-logger oci://ghcr.io/sinanakyazici/charts/consul-change-logger --version <chart-version> -n consul --create-namespace --dry-run --debug
```

The gateway bootstrap contract is environment-variable based:

- `CONSUL_UPSTREAM_URL`
- `CONSUL_CONFIG_KEY`
- `AUTHENTICATION` (`true` or `false`)

The application also accepts ASP.NET configuration-style keys as a fallback:

- `ConsulConfiguration__UpstreamUrl`
- `ConsulConfiguration__ConfigKey`
- `Authentication`

All remaining runtime settings are read from Consul KV.

### Current Rollout Steps

For a typical existing Consul Service, the rollout looks like this:

1. install the gateway:

```powershell
helm install consul-change-logger oci://ghcr.io/sinanakyazici/charts/consul-change-logger --version <chart-version> -n consul --set bootstrap.consulUpstreamUrl="http://consul-server.consul.svc.cluster.local:8500"
```

Docker Hub image variant:

```powershell
helm install consul-change-logger oci://ghcr.io/sinanakyazici/charts/consul-change-logger --version <chart-version> -n consul --set image.repository=docker.io/sinanakyazici/consul-change-logger --set bootstrap.consulUpstreamUrl="http://consul-server.consul.svc.cluster.local:8500"
```

2. seed runtime config into:

```text
consul-change-logger/appsettings.json
```

3. route the existing Consul hostname/load balancer/Ingress to:

```text
Service: consul-change-logger
Port:    80
```

4. verify rollout:

```powershell
kubectl rollout status deployment/consul-change-logger -n consul
kubectl port-forward svc/consul-change-logger -n consul 8080:80
curl http://localhost:8080/health/live
curl http://localhost:8080/health/ready
```

## End-to-End Flow

![Consul Change Logger request and audit flow](docs/consul-change-logger-flow.svg)

The important point is that audit logging happens inside the proxy while it is relaying Consul UI API traffic to Consul. The browser talks only to Consul Change Logger. Consul UI then triggers `/v1/kv/...` requests through that proxy path, and those requests are where reads are cached and writes/deletes are turned into audit records.

Flow summary:

1. The browser opens the existing Consul UI address, but the hostname now routes to the Consul Change Logger gateway.
2. `Consul Change Logger Login UI` serves `/login`.
3. LDAP / AD validates the submitted username and password with direct bind.
4. A successful login creates an in-memory session and redirects the browser to `/ui/`.
5. Consul UI JavaScript sends `/ui/*` and `/v1/kv/...` traffic through `Consul Change Logger Proxy`.
6. `Consul Change Logger Proxy` forwards those requests to the existing Consul UI and Consul KV API.
7. Consul responses return through the proxy back to the browser.
8. While forwarding KV traffic, the proxy creates audit records and writes them to outbox.
9. Elasticsearch indexes the audit records and Kibana visualizes them.

The architecture document contains a more detailed breakdown: [docs/architecture.md](docs/architecture.md)

## Authentication Model

Login uses direct LDAP bind. The value entered in the login form is sent directly as the LDAP bind identity.

Typical examples:

- `test.user@examplecorp.com`
- `service.account@company.com`

This matches environments where applications authenticate directly against Active Directory or another LDAP server using username and password, without first looking up the user DN through a search.

`AUTHENTICATION=false` disables the login screen entirely. In that mode:

- `/login` redirects to `/ui/`
- `/logout` redirects to `/ui/`
- `/` and `/ui/*` are proxied without requiring a session
- `/v1/*` remains unauthenticated fast pass-through
- audit capture is disabled because no authenticated browser identity is created

When `AUTHENTICATION=true`, the current request boundary is:

- `/` and `/ui/*` require an authenticated browser session
- unauthenticated `/v1/*` requests use a fast pass-through path so non-browser Consul clients are not forced through the login screen
- unauthenticated `/v1/*` requests are not audited and do not use the read cache, prefetch, outbox, or Elasticsearch dispatch path
- authenticated browser `/v1/kv/*` requests can be audited because they carry the UI session

This boundary is intentional. The product currently protects the browser UI path, not every possible Consul API caller that can reach the same endpoint.

Current LDAP runtime behavior:

- login uses direct bind only
- `LdapConfiguration.Domain`, `Port`, `SecurePort`, and `UseSSL` are actively used

## Audit Record

Each KV write or delete can produce a document like this:

```json
{
  "@timestamp": "2026-06-16T10:03:13Z",
  "event_id": "2d0d2f599db54e01bfab9f5209250e6a",
  "action": "kv_write",
  "kv_key": "test/test1",
  "is_folder": false,
  "old_value": "{ \"a\" : 1 }",
  "old_value_observed_at": "2026-06-16T10:02:00Z",
  "new_value": "{ \"a\" : 1 }",
  "new_value_json_error": null,
  "is_create": false,
  "is_update": true,
  "is_delete": false,
  "is_success": true,
  "response_status_code": 200,
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
- `is_folder=true` when the Consul key ends with `/`
- `new_value_json_error` is populated only when the submitted new value looks like JSON but cannot be parsed
- the proxy caches the most recent successful KV read per user/client/key identity
- if no matching cached read exists, the proxy can prefetch the current value before a single-key write or delete
- if a matching read is not found, `old_value` can still be `null`
- the audit record is written to outbox before Elasticsearch delivery is attempted
- the outbox file is deleted only after Elasticsearch accepts the document

## JSON Validation

Consul Change Logger does not block invalid JSON on the server side. It only records `new_value_json_error` when the submitted new value looks like JSON but cannot be parsed.

Current heuristic:

- if the new value starts with `{` or `[` after leading whitespace is removed, it is treated as JSON-like
- JSON-like values are parsed
- invalid parse -> `new_value_json_error` contains the parser message
- valid parse or non-JSON payload -> `new_value_json_error` is `null`

Browser-side warning:

- when a user clicks save in Consul UI
- if the outgoing KV body looks like JSON but cannot be parsed
- the browser asks whether the user still wants to continue

The warning script is served from `/ui/_ccl/json-validation.js`, so the browser-facing route must send `/ui/*` traffic to Consul Change Logger.
The same script injects a fixed `Logout` button into the Consul UI and posts to `/logout`.

The proxy still allows the write if the user confirms.

If an authenticated browser session expires while Consul UI is making background `fetch` or `XMLHttpRequest` calls, the injected client script now redirects the full page back to `/login`.

This is a UI safeguard only. The server does not reject KV writes just because the payload is invalid JSON.

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
- Consul startup availability waits
- LDAP startup availability waits when authentication is enabled
- clean upstream timeout / bad gateway warnings for Consul proxy requests

## Configuration

Bootstrap configuration defaults exist in `src/ConsulChangeLogger.Proxy/appsettings.json`, but the intended production path is environment variables on the gateway container:

```json
{
  "ConsulConfiguration": {
    "UpstreamUrl": "http://localhost:8500",
    "ConfigKey": "consul-change-logger/appsettings.local.json"
  },
  "Authentication": true
}
```

Production bootstrap contract:

```text
CONSUL_UPSTREAM_URL
CONSUL_CONFIG_KEY
AUTHENTICATION (true or false)
```

Fallback binding keys:

```text
ConsulConfiguration__UpstreamUrl
ConsulConfiguration__ConfigKey
Authentication
```

Runtime configuration is read from the Consul KV key referenced by `CONSUL_CONFIG_KEY`.

Example runtime configuration:

```json
{
  "Elasticsearch": {
    "Url": "https://localhost:9200",
    "Username": "elastic",
    "Password": "your-password",
    "Index": "consul-change-logger",
  },
  "ChangeLog": {
    "OutboxPath": "/var/lib/consul-change-logger/outbox",
    "ReadMatchWindowSeconds": 1800,
    "QueueCapacity": 1000,
    "RetentionDays": 30
  },
  "LdapConfiguration": {
    "Domain": "127.0.0.1",
    "Port": 1389,
    "SecurePort": 1636,
    "UseSSL": false
  }
}
```

Notes:

- The Helm chart mounts the outbox PVC at `/var/lib/consul-change-logger/outbox` by default.
- At startup the proxy waits for Consul first, then waits for LDAP when `AUTHENTICATION=true`, then waits for Elasticsearch.
- In the current implementation, startup is blocked until Elasticsearch becomes reachable and the target index mapping is ensured.
- When `LdapConfiguration.UseSSL=true`, the current implementation accepts the server certificate via a permissive validation callback. Traffic is encrypted, but strict certificate trust validation is not yet enforced.

## Runtime Verification

After gateway rollout, the minimum useful checks are:

1. call a cookies-less `/v1/*` Consul API path and confirm it passes through without login or audit
2. open the existing Consul URL and confirm the login screen appears
3. sign in with LDAP
4. read a KV key
5. modify and save that KV key
6. confirm the change appears in Elasticsearch and Kibana

Expected behavior:

- `/health/live` returns live
- `/health/ready` returns ready
- cookies-less `/v1/*` requests pass through without audit work
- a KV read followed by a KV write can populate best-effort `old_value`
- invalid JSON-like values trigger a browser warning before save
- successful audit events are written to the `consul-change-logger` index

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
new_value_json_error : *
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

`/health/ready` verifies both Consul and Elasticsearch connectivity.

Operational consequence:

- if Consul is unavailable, the proxy is not ready
- if Elasticsearch is unavailable, the proxy is also not ready
- this matches the current code, even though audit data is still written to the outbox before Elasticsearch delivery is attempted

## Kubernetes

Primary install artifact:

- `chart/consul-change-logger`

Supporting examples and notes:

- [k8s/appsettings.consul.example.json](k8s/appsettings.consul.example.json)
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
k8s                              Kubernetes runtime configuration and storage examples
docs                             Architecture and onboarding docs
chart/consul-change-logger       Helm chart for the gateway Deployment, Service, and outbox PVC
```

## License

MIT. See [LICENSE](LICENSE).
