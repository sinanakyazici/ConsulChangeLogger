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

{{- define "consul-change-logger.labels" -}}
app.kubernetes.io/name: {{ include "consul-change-logger.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version | replace "+" "_" }}
{{- end -}}

{{- define "consul-change-logger.selectorLabels" -}}
app.kubernetes.io/name: {{ include "consul-change-logger.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}
