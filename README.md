# ConsulChangeLogger

ConsulChangeLogger records Consul KV changes with user identity, timestamp, key, old value, and new value. It is designed to sit in front of Consul UI/API traffic and forward requests to Consul while writing change records to a local JSON Lines log and Elasticsearch.

It does not produce generic access logs and it does not enforce authorization rules. It logs KV changes.

```text
Browser -> ConsulChangeLogger -> Consul UI/API
              |
              +-> audit.log
              +-> durable outbox
              +-> Elasticsearch -> Kibana
```

## Features

- LDAP or mock login before Consul UI/API access.
- Consul KV write/delete change records.
- `user_email`, `client_ip`, `user_agent`, `request_id`, and `event_id` fields.
- Best-effort `old_value` capture from prior UI reads.
- Raw `new_value` capture from write requests.
- Local JSON Lines audit log.
- Audit log writing through Serilog.
- Durable local outbox with Elasticsearch retry.
- Non-secret runtime configuration from Consul KV.
- Docker and Kubernetes examples.

## What It Logs

ConsulChangeLogger emits records for Consul KV write/delete operations only. Audit records are written through a dedicated Serilog file sink as JSON Lines.

Example:

```json
{
  "@timestamp": "2026-05-23T10:01:41Z",
  "event_id": "0473452922e74526b243cfb3c31ee3ee",
  "action": "kv_write",
  "kv_key": "test/test1",
  "old_value": "{ \"a\" : 1, \"b\": 2 }",
  "old_value_seen_at": "2026-05-23T10:01:33Z",
  "old_value_read_request_id": "2a3839ca1792427aa7d02483d5cc3ded",
  "new_value": "{ \"a\" : 1, \"b\": 2, \"c\": 1 }",
  "delete_confirmed": false,
  "success": true,
  "response_code": 200,
  "client_ip": "172.21.0.1",
  "user_email": "user@example.com",
  "user_agent": "Mozilla/5.0",
  "request_id": "0473452922e74526b243cfb3c31ee3ee",
  "source_path": "/v1/kv/test/test1?dc=dc1&flags=0",
  "source": "consul-change-logger"
}
```

## Important Security Model

- ConsulChangeLogger is not an authorization system. Run Consul ACLs in production.
- It records raw KV values by design. Do not store sensitive values in Consul KV if they must not appear in logs or Elasticsearch.
- `LDAP_BIND_PASSWORD` is never read from Consul KV. Provide it through a secret-backed environment variable.
- Use HTTPS at ingress/load-balancer level and set `AUTH_COOKIE_SECURE=true` in production.

## Quick Start

The local environment starts Consul, Elasticsearch, and Kibana separately, then starts only ConsulChangeLogger with Docker Compose.

```powershell
docker network create consul-audit-net

docker run -d --name elasticsearch --network consul-audit-net -p 9200:9200 `
  -e "discovery.type=single-node" `
  -e "xpack.security.enabled=false" `
  -e "ES_JAVA_OPTS=-Xms512m -Xmx512m" `
  docker.elastic.co/elasticsearch/elasticsearch:8.15.0

docker run -d --name kibana --network consul-audit-net -p 5601:5601 `
  -e "ELASTICSEARCH_HOSTS=http://elasticsearch:9200" `
  docker.elastic.co/kibana/kibana:8.15.0

docker run -d --name consul --network consul-audit-net `
  hashicorp/consul:1.19 agent -dev "-client=0.0.0.0" -ui
```

Seed non-secret runtime config into Consul KV:

```powershell
docker exec consul consul kv put consul-change-logger/config/LISTEN_PORT 8080
docker exec consul consul kv put consul-change-logger/config/ELASTICSEARCH_URL http://elasticsearch:9200
docker exec consul consul kv put consul-change-logger/config/AUDIT_INDEX consul-change-logger
docker exec consul consul kv put consul-change-logger/config/AUDIT_LOG_PATH /var/log/audit/audit.log
docker exec consul consul kv put consul-change-logger/config/AUDIT_OUTBOX_PATH /var/log/audit/outbox
docker exec consul consul kv put consul-change-logger/config/DATA_PROTECTION_PATH /var/lib/consul-change-logger/dp-keys
docker exec consul consul kv put consul-change-logger/config/READ_MATCH_WINDOW_SECONDS 1800
docker exec consul consul kv put consul-change-logger/config/MAX_BODY_BYTES 8192
docker exec consul consul kv put consul-change-logger/config/AUDIT_QUEUE_CAPACITY 1000
docker exec consul consul kv put consul-change-logger/config/ELASTICSEARCH_RETRY_DELAY_SECONDS 2
docker exec consul consul kv put consul-change-logger/config/AUTH_COOKIE_SECURE false
docker exec consul consul kv put consul-change-logger/config/AUTH_MODE mock
docker exec consul consul kv put consul-change-logger/config/AUTH_MOCK_PASSWORD Passw0rd!
docker exec consul consul kv put consul-change-logger/config/LDAP_URL ldap://ldap.example.com:389
docker exec consul consul kv put consul-change-logger/config/LDAP_BASE_DN dc=example,dc=com
docker exec consul consul kv put consul-change-logger/config/LDAP_USER_FILTER "(mail={0})"
docker exec consul consul kv delete consul-change-logger/config/LDAP_BIND_PASSWORD
```

Start ConsulChangeLogger:

```powershell
docker compose up -d --build
```

Open Consul UI through ConsulChangeLogger:

```text
http://localhost:8080/ui/
```

For local mock auth, use any email with this password:

```text
Passw0rd!
```

## Generate a Change Record

```powershell
curl.exe -c .\logs\cookies.txt -d "email=user@example.com&password=Passw0rd!" http://localhost:8080/login
curl.exe -b .\logs\cookies.txt -X PUT --data "{ \"a\" : 1, \"b\": 2 }" http://localhost:8080/v1/kv/demo/key
curl.exe -b .\logs\cookies.txt http://localhost:8080/v1/kv/demo/key?raw
curl.exe -b .\logs\cookies.txt -X PUT --data "{ \"a\" : 1, \"b\": 2, \"c\": 1 }" http://localhost:8080/v1/kv/demo/key
```

Inspect Elasticsearch:

```powershell
curl.exe "http://localhost:9200/consul-change-logger/_search?pretty&size=10&sort=@timestamp:desc"
```

Inspect local JSON Lines:

```powershell
Get-Content .\logs\audit.log -Tail 10
```

## Configuration

ConsulChangeLogger needs two bootstrap environment variables:

| Name | Required | Default | Description |
| --- | --- | --- | --- |
| `CONSUL_UPSTREAM_URL` | no | `http://consul:8500` | Consul HTTP API endpoint. |
| `CONSUL_CONFIG_PREFIX` | no | `consul-change-logger/config` | Consul KV prefix for runtime configuration. |

Runtime configuration is read from Consul KV under `CONSUL_CONFIG_PREFIX`:

| Key | Default | Description |
| --- | --- | --- |
| `LISTEN_PORT` | `8080` | HTTP port exposed by ConsulChangeLogger. |
| `ELASTICSEARCH_URL` | `http://elasticsearch:9200` | Elasticsearch endpoint. |
| `AUDIT_INDEX` | `consul-change-logger` | Elasticsearch index name. |
| `AUDIT_LOG_PATH` | `/var/log/audit/audit.log` | Local JSON Lines audit log path. |
| `AUDIT_OUTBOX_PATH` | `/var/log/audit/outbox` | Durable outbox directory. |
| `DATA_PROTECTION_PATH` | `/var/lib/consul-change-logger/dp-keys` | ASP.NET cookie key storage path. |
| `READ_MATCH_WINDOW_SECONDS` | `1800` | Time window for matching prior reads to writes. |
| `MAX_BODY_BYTES` | `8192` | Max captured request/response body length. |
| `AUDIT_QUEUE_CAPACITY` | `1000` | In-memory dispatch queue size. |
| `ELASTICSEARCH_RETRY_DELAY_SECONDS` | `2` | Retry delay after Elasticsearch delivery failure. |
| `AUTH_COOKIE_SECURE` | `false` | Set `true` when served over HTTPS. |
| `AUTH_MODE` | `disabled` | `disabled`, `mock`, or `ldap`. |
| `AUTH_MOCK_PASSWORD` | `Passw0rd!` | Local mock password. |
| `LDAP_URL` | `ldap://localhost:389` | LDAP/LDAPS endpoint. |
| `LDAP_BIND_DN` | empty | Optional LDAP search bind DN. |
| `LDAP_BASE_DN` | empty | LDAP search base DN. |
| `LDAP_USER_FILTER` | `(mail={0})` | LDAP user search filter. |

Secret environment variables:

| Name | Description |
| --- | --- |
| `LDAP_BIND_PASSWORD` | LDAP search bind password. Read from environment only. |

## Health Checks

```text
/health/live
/health/ready
```

`/health/ready` checks Consul and Elasticsearch connectivity.

## Kubernetes

Example manifests are under `k8s/`:

- `k8s/sidecar-snippet.yaml`
- `k8s/service-example.yaml`
- `k8s/consul-config-seed.example.sh`

For production:

- mount `AUDIT_OUTBOX_PATH` on persistent storage
- persist `DATA_PROTECTION_PATH`
- provide `LDAP_BIND_PASSWORD` from a Kubernetes Secret
- set `AUTH_COOKIE_SECURE=true`
- keep Consul ACLs enabled

## Development

Build:

```powershell
dotnet build ConsulChangeLogger.slnx
```

Run tests:

```powershell
dotnet run --project tests\ConsulChangeLogger.Tests\ConsulChangeLogger.Tests.csproj
```

Build Docker image:

```powershell
docker build -f src\ConsulChangeLogger.Proxy\Dockerfile -t consul-change-logger:local .
```

## Architecture

See [docs/architecture.md](docs/architecture.md).

## Current Limits

- `old_value` depends on a previous read from the same user/client/key while ConsulChangeLogger is running.
- If read and write requests are routed to different replicas, `old_value` can be `null` unless sticky routing or shared state is added.
- If the read/write happens across process restarts, `old_value` can be `null`.
- Large request/response bodies are truncated at `MAX_BODY_BYTES`.
- The current test project is a lightweight executable runner, not a standard xUnit/NUnit/MSTest project.

## License

MIT. See [LICENSE](LICENSE).
