# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project follows Semantic Versioning once public releases start.

## Unreleased

- Initial Consul KV change logging proxy.
- LDAP/mock login gate in front of Consul UI/API.
- Raw old/new KV value capture for UI read-before-write flows.
- Durable outbox for local change-record persistence and Elasticsearch delivery.
- Consul KV based non-secret runtime configuration.
- Docker Compose and Kubernetes examples.
- LDAP group allowlist support.
- Elasticsearch basic auth/API key support through secret-backed environment variables.
- Non-root container and Kubernetes PVC/securityContext examples.
- Login CSRF validation, browser hardening headers, and Consul path/method restrictions.
