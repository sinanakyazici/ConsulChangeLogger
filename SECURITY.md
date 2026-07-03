# Security Policy

## Supported Versions

The following are considered supported:

- the latest tagged release
- the current `main` branch

## Reporting a Vulnerability

Do not open a public issue for a suspected vulnerability.

Use GitHub private vulnerability reporting if it is enabled for the repository. If private reporting is not enabled yet, contact the repository maintainers through a private channel and include:

- affected version or commit
- deployment model
- steps to reproduce
- expected impact
- relevant logs with secrets removed

## Security Notes

Current security-relevant behavior:

- Runtime settings are loaded from a Consul KV JSON document referenced by `CONSUL_CONFIG_KEY`.
- That runtime document can contain plaintext Elasticsearch credentials.
- Protect the configuration key with Consul ACLs and restrict who can read or update it.
- Consul Change Logger records raw KV values by design. Do not use this product if those values must not be written to logs, outbox files, or Elasticsearch.
- Browser authentication is LDAP direct bind. The submitted username and password are used only for the bind attempt and are not stored in the browser session.
- The browser session is an opaque in-memory session id. It is not persisted across process restarts.
- `AUTHENTICATION=false` disables the login screen and does not create an authenticated audit identity. In that mode, requests are proxied but KV changes are not audited.
- When `AUTHENTICATION=true`, every Consul UI and API request forwarded by the gateway requires a valid in-memory session.
- Requests without a valid session are redirected to `/login`; machine-to-machine Consul clients must use the existing internal Consul Service directly.
- Consul ACLs must remain enabled. Consul Change Logger is not an authorization system and does not replace Consul ACL enforcement.
- Run the container as non-root, keep the root filesystem read-only, and mount only the outbox path as writable storage.
- Persist the outbox path if you need audit durability across pod restarts.
- Use HTTPS or TLS at the ingress/load-balancer layer for browser traffic.
- When `LdapConfiguration.UseSSL=true`, the current implementation encrypts LDAP traffic but accepts the LDAP server certificate through a permissive validation callback. Treat this as weaker than strict certificate trust validation.
