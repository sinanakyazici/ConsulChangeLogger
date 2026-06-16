#!/usr/bin/env sh
set -eu

FILE="${1:-k8s/appsettings.consul.example.json}"

consul kv put "consul-change-logger/appsettings.json" "@$FILE"
