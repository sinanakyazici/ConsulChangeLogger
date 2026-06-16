# Architecture

Consul Change Logger is an authenticated reverse proxy with an audit pipeline behind it. Its job is to sit in front of Consul UI and the Consul HTTP API, authenticate the user, forward the request to Consul, observe KV traffic, and persist normalized change records into Elasticsearch.

This document describes the current implementation, not a generic future design.

## High-Level View

```mermaid
flowchart LR
    browser["Browser"]
    proxy["Consul Change Logger<br/>ASP.NET Core reverse proxy"]
    ldap["LDAP / Active Directory"]
    consulUi["Consul UI shell<br/>served by Consul"]
    consulApi["Consul HTTP API<br/>/v1/kv and other endpoints"]
    audit["Audit capture inside proxy"]
    cache["In-memory read cache"]
    outbox["Outbox files<br/>yyyy-MM-dd/*.json"]
    worker["Background dispatch worker"]
    elastic["Elasticsearch<br/>consul-change-logger"]
    kibana["Kibana"]

    browser -->|login and all Consul traffic| proxy
    proxy -->|direct bind| ldap
    proxy -->|forward /ui/*| consulUi
    proxy -->|forward /v1/*| consulApi
    consulUi --> proxy
    consulApi --> proxy
    proxy -->|observe KV read/write/delete<br/>while forwarding| audit
    audit -->|store successful reads| cache
    audit -->|persist write/delete audit records| outbox
    outbox -->|enqueue| worker
    worker -->|retry until success| elastic
    elastic --> kibana
```

The key detail is this:

- Consul UI itself does not write directly to Elasticsearch
- the browser loads the Consul UI through Consul Change Logger
- that UI then sends KV API requests such as `GET /v1/kv/...` and `PUT /v1/kv/...`
- those API calls pass through Consul Change Logger
- Consul Change Logger forwards them to Consul and, at the same time, creates audit data from the request and response

## Request Path

### 1. Authentication

The login form is served by the proxy itself.

Flow:

1. browser requests `/login`
2. proxy renders login page with CSRF token
3. browser posts username and password
4. proxy performs direct LDAP bind using the submitted identity
5. if bind succeeds, the proxy creates an in-memory session and sets an opaque cookie
6. browser is redirected to `/ui/`

Important properties:

- login uses direct bind
- the submitted username is used as the LDAP bind identity
- the session store is in memory
- the browser cookie stores only the session id

### 2. Consul UI and API forwarding

After authentication:

- requests to `/ui/*` are forwarded to Consul UI
- requests to `/v1/*` are forwarded to the Consul HTTP API

The proxy copies request headers, body, and method, then writes the upstream response back to the browser.

Non-mutating UI/API traffic passes through as normal. KV mutation traffic is additionally observed by the audit pipeline.

The practical browser flow is:

1. browser requests `/ui/` from Consul Change Logger
2. Consul Change Logger forwards that to the upstream Consul UI
3. browser receives Consul UI HTML and JavaScript through the proxy
4. that JavaScript later calls endpoints such as `/v1/kv/...`
5. those `/v1/kv/...` calls also go through Consul Change Logger
6. while forwarding them to Consul, the proxy inspects them for audit purposes

### 3. Client-side JSON warning

When the Consul UI HTML shell is returned, the proxy injects a small JavaScript file into the HTML response.

That script:

- intercepts `fetch` and `XMLHttpRequest`
- watches `PUT /v1/kv/...` requests
- checks whether the body looks like JSON
- if it looks like JSON but is invalid, shows a browser confirmation dialog

This is only a UI guard. The server still allows the request if the user confirms.

## Audit Pipeline

```mermaid
sequenceDiagram
    autonumber
    participant User as Browser
    participant UI as Consul UI JS in Browser
    participant Proxy as Consul Change Logger
    participant Consul as Consul API
    participant Cache as Read Cache
    participant Outbox as Outbox
    participant Worker as Dispatch Worker
    participant ES as Elasticsearch

    User->>Proxy: GET /ui/
    Proxy->>Consul: Forward /ui/ request
    Consul-->>Proxy: UI HTML + JS
    Proxy-->>User: Return UI

    User->>UI: Edit or view KV in browser
    UI->>Proxy: GET /v1/kv/app/key?raw
    Proxy->>Consul: Forward GET
    Consul-->>Proxy: Current value
    Proxy->>Cache: Store value by user + client + key
    Proxy-->>UI: Return response

    UI->>Proxy: PUT /v1/kv/app/key
    Proxy->>Consul: Forward PUT
    Consul-->>Proxy: Write result
    Proxy->>Cache: Lookup previous read
    Proxy->>Proxy: Build ChangeRecord
    Proxy->>Outbox: Write JSON file
    Proxy->>Worker: Enqueue file path
    Worker->>ES: PUT document
    ES-->>Worker: 2xx accepted
    Worker->>Outbox: Delete delivered file
    Proxy-->>UI: Return write result
```

### Read cache

`old_value` is best-effort.

The proxy does not fetch Consul state before every write. Instead, it caches the latest successful KV read using this identity model:

- authenticated username
- client IP
- user agent
- KV key

If the same identity later performs a write or delete for the same key, the cached read becomes `old_value`.

This means:

- if there is no prior read, `old_value` can be `null`
- if the process restarts, `old_value` can be `null`
- if traffic is split across replicas without shared state, `old_value` can be `null`

### ChangeRecord structure

Current fields include:

- event metadata:
  `@timestamp`, `event_id`, `request_id`, `action`, `source`, `source_path`
- KV metadata:
  `kv_key`, `old_value`, `new_value`, `delete_confirmed`
- response metadata:
  `success`, `response_code`
- user context:
  `user_email`, `client_ip`, `user_agent`
- old value tracking:
  `old_value_seen_at`, `old_value_read_request_id`
- JSON validation metadata:
  `old_value_looks_like_json`, `old_value_is_valid_json`, `old_value_json_error`, `old_value_json_validation_status`
  `new_value_looks_like_json`, `new_value_is_valid_json`, `new_value_json_error`, `new_value_json_validation_status`

### JSON validation semantics

The proxy never rewrites or blocks the KV payload on the server side.

Validation is informational:

- `not_json`
- `valid_json`
- `invalid_json`

Current detection rule:

- if a value starts with `{` or `[` after trimming leading whitespace, it is treated as JSON-like
- JSON-like values are parsed
- successful parse -> `valid_json`
- failed parse -> `invalid_json`
- everything else -> `not_json`

This means JSON primitives such as `true`, `123`, or `"text"` are currently classified as `not_json`, not `valid_json`.

## Outbox and Delivery

```mermaid
flowchart TD
    event["KV write/delete detected"]
    record["Build ChangeRecord JSON"]
    persist["Write file to outbox"]
    queue["Queue outbox path"]
    dispatch["Send document to Elasticsearch"]
    accepted{"Accepted?"}
    retry["Wait and retry"]
    remove["Delete outbox file"]
    cleanup["Delete expired daily folders"]

    event --> record --> persist --> queue --> dispatch --> accepted
    accepted -->|yes| remove
    accepted -->|no| retry --> dispatch
    persist --> cleanup
```

Delivery rules:

1. write record JSON to outbox first
2. enqueue file path
3. worker dispatches to Elasticsearch
4. only after Elasticsearch accepts the document is the file deleted
5. if Elasticsearch is unavailable, the file stays in outbox and is retried

At startup, the worker scans the outbox and re-queues any leftover files.

## Elasticsearch Integration

Current index name:

```text
consul-change-logger
```

Startup behavior:

1. wait until Elasticsearch root endpoint is reachable
2. create the index if needed
3. update the mapping with the expected fields

Delivery model:

- one `ChangeRecord` becomes one Elasticsearch document
- document id uses `event_id` if present, otherwise `request_id`
- retries continue with the configured delay

## Logging Model

The proxy currently runs with verbose console logging enabled.

It logs:

- application startup
- LDAP bind attempts and results
- HTTP request summaries
- proxied upstream response details
- KV read cache hits
- audit record creation
- outbox persistence
- queue and dispatch lifecycle
- Elasticsearch health and index setup
- invalid JSON detection
- request cancellation and client disconnect behavior

## Runtime Configuration

The application reads only bootstrap values from local `appsettings.json`:

- `ConsulConfiguration.UpstreamUrl`
- `ConsulConfiguration.ConfigKey`

All runtime settings come from the Consul KV JSON document referenced by `ConfigKey`.

This includes:

- Elasticsearch settings
- outbox settings
- LDAP settings

Because these values can contain plaintext credentials, Consul ACL policies must restrict access to the configuration key.

## Local AD Lab

This repository includes a local Samba-based AD lab under `k8s/`:

- `k8s/samba-ad.yaml`
- `k8s/samba-ad-ui.yaml`

This lab is for development and verification:

- direct LDAP bind testing
- AD-style `SearchBase`
- seeded service account
- seeded test user
- phpLDAPadmin access

Current seeded identities:

- `svc-ldap-bind@examplecorp.com`
- `test.user@examplecorp.com`

## Limits

- `old_value` is best-effort, not a guaranteed previous-state read
- direct bind login does not validate authorization groups
- the session store is in memory only
- JSON validation is heuristic for objects and arrays, not all JSON forms
- the client-side JSON warning works only for traffic that goes through the Consul UI in the browser
- server-side audit still allows invalid JSON writes if the user or calling client sends them
