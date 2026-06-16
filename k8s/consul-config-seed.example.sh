#!/usr/bin/env sh
set -eu

FILE="${1:-k8s/appsettings.consul.example.json}"
KEY="${CONSUL_CONFIG_KEY:-consul-change-logger/appsettings.json}"

consul kv put "$KEY" "@$FILE"
