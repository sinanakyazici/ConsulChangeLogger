# Architecture

Consul Change Logger is an authenticated reverse proxy for Consul UI/API traffic. It records Consul KV write/delete events with user identity and best-effort old/new values.

```mermaid
flowchart LR
    browser["fa:fa-user User browser"]
    proxy["fa:fa-shield Consul Change Logger<br/>ASP.NET Core reverse proxy"]
    auth["fa:fa-lock Login gate<br/>LDAP or mock auth"]
    policy["fa:fa-filter Request policy<br/>allowed path prefixes"]
    consul["fa:fa-server Consul UI / HTTP API"]
    readcache["fa:fa-clock In-memory read cache<br/>user + client + key"]
    outbox["fa:fa-folder-open Daily outbox<br/>yyyy-MM-dd/*.json"]
    dispatcher["fa:fa-rotate Background dispatcher<br/>retry + retention cleanup"]
    elastic["fa:fa-database Elasticsearch index<br/>consul-change-logger"]
    kibana["fa:fa-chart-line Kibana Discover / dashboards"]

    browser --> proxy
    proxy --> auth
    proxy --> policy
    policy --> consul
    proxy --> readcache
    proxy --> outbox
    outbox --> dispatcher
    dispatcher --> elastic
    elastic --> kibana

    classDef userNode fill:#E8F3FF,stroke:#3B82F6,color:#0F172A,stroke-width:1.5px
    classDef proxyNode fill:#FFF7ED,stroke:#F97316,color:#111827,stroke-width:2px
    classDef securityNode fill:#ECFDF5,stroke:#10B981,color:#064E3B,stroke-width:1.5px
    classDef consulNode fill:#EEF2FF,stroke:#6366F1,color:#1E1B4B,stroke-width:1.5px
    classDef storageNode fill:#FEFCE8,stroke:#CA8A04,color:#422006,stroke-width:1.5px
    classDef observeNode fill:#FDF2F8,stroke:#DB2777,color:#500724,stroke-width:1.5px

    class browser userNode
    class proxy proxyNode
    class auth,policy securityNode
    class consul consulNode
    class readcache,outbox,dispatcher storageNode
    class elastic,kibana observeNode
```

## Projects

- `src/ConsulChangeLogger.Proxy`: ASP.NET Core app, login flow, Consul forwarding, change record delivery.
- `src/ConsulChangeLogger.Core`: shared models and KV parsing helpers.
- `tests/ConsulChangeLogger.Tests`: lightweight executable test runner for parsing edge cases.

## Old Value Capture

Consul Change Logger does not read Consul state before every write. It captures `old_value` from the latest successful KV read by the same user/client/key identity while the process is running.

This matches the Consul UI workflow where users read a KV value before changing it. If writes happen without a prior read through Consul Change Logger, `old_value` can be `null`.

```mermaid
sequenceDiagram
    autonumber
    participant User as User browser
    participant Proxy as Consul Change Logger
    participant Consul as Consul
    participant Cache as Read cache
    participant Outbox as Daily outbox
    participant ES as Elasticsearch

    User->>Proxy: GET /v1/kv/app/key?raw
    Proxy->>Consul: Forward read request
    Consul-->>Proxy: Current KV value
    Proxy->>Cache: Store old_value by user/client/key
    Proxy-->>User: Return current value

    User->>Proxy: PUT /v1/kv/app/key
    Proxy->>Consul: Forward write request
    Consul-->>Proxy: Write result
    Proxy->>Cache: Lookup old_value
    Proxy->>Outbox: Write change record JSON
    Outbox->>ES: Deliver with retry
    ES-->>Outbox: Accepted
    Outbox->>Outbox: Delete delivered file
    Proxy-->>User: Return write result
```

## Delivery Guarantees

For each KV write/delete event:

1. write JSON to `CHANGE_LOG_OUTBOX_PATH`
2. enqueue the outbox file for Elasticsearch delivery
3. delete the outbox file only after Elasticsearch accepts it

Outbox files are stored under daily directories named `yyyy-MM-dd`. If Elasticsearch is unavailable, the background worker retries from the outbox. Expired daily directories are deleted according to `CHANGE_LOG_RETENTION_DAYS`, which defaults to 30 days. For production, mount `CHANGE_LOG_OUTBOX_PATH` on persistent storage.

```mermaid
flowchart TD
    change["fa:fa-pen KV write/delete observed"]
    json["fa:fa-file-code Create change record JSON"]
    daily["fa:fa-folder-open Write to daily outbox folder"]
    send["fa:fa-paper-plane Send to Elasticsearch"]
    accepted{"Accepted?"}
    delete["fa:fa-check Delete delivered file"]
    keep["fa:fa-box-archive Keep file in outbox"]
    retry["fa:fa-rotate Retry after delay"]
    retention["fa:fa-calendar-days Delete expired daily folders<br/>default: 30 days"]

    change --> json --> daily --> send --> accepted
    accepted -->|yes| delete
    accepted -->|no| keep --> retry --> send
    daily --> retention

    classDef eventNode fill:#FFF7ED,stroke:#F97316,color:#111827,stroke-width:2px
    classDef storageNode fill:#FEFCE8,stroke:#CA8A04,color:#422006,stroke-width:1.5px
    classDef decisionNode fill:#E0F2FE,stroke:#0284C7,color:#0C4A6E,stroke-width:1.5px
    classDef successNode fill:#ECFDF5,stroke:#10B981,color:#064E3B,stroke-width:1.5px
    classDef retryNode fill:#FDF2F8,stroke:#DB2777,color:#500724,stroke-width:1.5px

    class change,json eventNode
    class daily,keep,retention storageNode
    class accepted decisionNode
    class send,delete successNode
    class retry retryNode
```

## Configuration

Only bootstrap values are expected from environment variables:

- `CONSUL_UPSTREAM_URL`
- `CONSUL_CONFIG_PREFIX`

Non-secret runtime configuration is read from Consul KV under `CONSUL_CONFIG_PREFIX`.

Secret values must not be stored in Consul KV. `LDAP_BIND_PASSWORD`, `ELASTICSEARCH_USERNAME`, `ELASTICSEARCH_PASSWORD`, and `ELASTICSEARCH_API_KEY` are read only from the environment.

## Request Scope

After authentication, Consul Change Logger forwards only paths matching `CONSUL_ALLOWED_PATH_PREFIXES`. `GET` and `HEAD` are allowed for those prefixes. Mutating methods are allowed only for Consul KV paths, so non-KV Consul API mutations are blocked at the proxy layer. Consul ACLs are still required in production.
