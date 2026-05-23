# Architecture

ConsulChangeLogger is an authenticated reverse proxy for Consul UI/API traffic. It records Consul KV write/delete events with user identity and best-effort old/new values.

```text
Browser
  |
  v
ConsulChangeLogger.Proxy
  |-- login/authentication
  |-- Consul request forwarding
  |-- KV read cache
  |-- durable outbox
  |
  +--> Consul HTTP API
  +--> Elasticsearch
```

## Projects

- `src/ConsulChangeLogger.Proxy`: ASP.NET Core app, login flow, Consul forwarding, audit delivery.
- `src/ConsulChangeLogger.Core`: shared models and KV parsing helpers.
- `tests/ConsulChangeLogger.Tests`: lightweight executable test runner for parsing edge cases.

## Old Value Capture

ConsulChangeLogger does not read Consul state before every write. It captures `old_value` from the latest successful KV read by the same user/client/key identity while the process is running.

This matches the Consul UI workflow where users read a KV value before changing it. If writes happen without a prior read through ConsulChangeLogger, `old_value` can be `null`.

## Delivery Guarantees

For each KV write/delete event:

1. write JSON to `AUDIT_OUTBOX_PATH`
2. enqueue the outbox file for Elasticsearch delivery
3. delete the outbox file only after Elasticsearch accepts it

If Elasticsearch is unavailable, the background worker retries from the outbox. For production, mount `AUDIT_OUTBOX_PATH` on persistent storage.

## Configuration

Only bootstrap values are expected from environment variables:

- `CONSUL_UPSTREAM_URL`
- `CONSUL_CONFIG_PREFIX`

Non-secret runtime configuration is read from Consul KV under `CONSUL_CONFIG_PREFIX`.

Secret values must not be stored in Consul KV. `LDAP_BIND_PASSWORD` is read only from the environment.
