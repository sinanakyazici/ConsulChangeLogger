# Security Policy

## Supported Versions

Only the `main` branch is currently supported.

## Reporting a Vulnerability

Do not open a public issue for a suspected vulnerability.

Use GitHub private vulnerability reporting if it is enabled for the repository. If private reporting is not enabled yet, contact the repository maintainers through a private channel and include:

- affected version or commit
- deployment model
- steps to reproduce
- expected impact
- relevant logs with secrets removed

## Security Notes

- `LDAP_BIND_PASSWORD` is intentionally not read from Consul KV. Provide it through a secret-backed environment variable.
- ConsulChangeLogger records raw KV values by design. Do not store sensitive data in Consul KV if those values must not appear in logs or Elasticsearch.
- Use HTTPS at the ingress/load-balancer layer and set `AUTH_COOKIE_SECURE=true` for production.
- Run Consul with ACLs enabled. ConsulChangeLogger is not an authorization system.
