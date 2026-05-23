#!/usr/bin/env sh
set -eu

PREFIX="${CONSUL_CONFIG_PREFIX:-consul-change-logger/config}"

consul kv put "$PREFIX/LISTEN_PORT" "8080"
consul kv put "$PREFIX/ELASTICSEARCH_URL" "http://elasticsearch.logging.svc.cluster.local:9200"
consul kv put "$PREFIX/AUDIT_INDEX" "consul-change-logger"
consul kv put "$PREFIX/AUDIT_OUTBOX_PATH" "/var/log/audit/outbox"
consul kv put "$PREFIX/DATA_PROTECTION_PATH" "/var/lib/consul-change-logger/dp-keys"
consul kv put "$PREFIX/READ_MATCH_WINDOW_SECONDS" "1800"
consul kv put "$PREFIX/MAX_BODY_BYTES" "8192"
consul kv put "$PREFIX/AUDIT_QUEUE_CAPACITY" "1000"
consul kv put "$PREFIX/ELASTICSEARCH_RETRY_DELAY_SECONDS" "2"
consul kv put "$PREFIX/AUTH_COOKIE_SECURE" "true"
consul kv put "$PREFIX/AUTH_MODE" "ldap"
consul kv put "$PREFIX/LDAP_URL" "ldaps://ldap.company.local:636"
consul kv put "$PREFIX/LDAP_BIND_DN" "cn=readonly,ou=service-users,dc=company,dc=local"
consul kv put "$PREFIX/LDAP_BASE_DN" "dc=company,dc=local"
consul kv put "$PREFIX/LDAP_USER_FILTER" "(mail={0})"
