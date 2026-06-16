{{- define "consul-change-logger.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "consul-change-logger.fullname" -}}
{{- if .Values.fullnameOverride -}}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- include "consul-change-logger.name" . -}}
{{- end -}}
{{- end -}}

{{- define "consul-change-logger.namespace" -}}
{{- default .Release.Namespace .Values.namespaceOverride -}}
{{- end -}}
