{{/*
Expand the name of the chart.
*/}}
{{- define "directory-net.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Create a default fully qualified app name.
*/}}
{{- define "directory-net.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{/*
Create chart name and version as used by the chart label.
*/}}
{{- define "directory-net.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Common labels
*/}}
{{- define "directory-net.labels" -}}
helm.sh/chart: {{ include "directory-net.chart" . }}
{{ include "directory-net.selectorLabels" . }}
{{- if .Chart.AppVersion }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- end }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{/*
Selector labels
*/}}
{{- define "directory-net.selectorLabels" -}}
app.kubernetes.io/name: {{ include "directory-net.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{/*
Server labels
*/}}
{{- define "directory-net.server.labels" -}}
{{ include "directory-net.labels" . }}
app.kubernetes.io/component: server
{{- end }}

{{/*
Server selector labels
*/}}
{{- define "directory-net.server.selectorLabels" -}}
{{ include "directory-net.selectorLabels" . }}
app.kubernetes.io/component: server
{{- end }}

{{/*
Web labels
*/}}
{{- define "directory-net.web.labels" -}}
{{ include "directory-net.labels" . }}
app.kubernetes.io/component: web
{{- end }}

{{/*
Web selector labels
*/}}
{{- define "directory-net.web.selectorLabels" -}}
{{ include "directory-net.selectorLabels" . }}
app.kubernetes.io/component: web
{{- end }}

{{/*
Create the name of the service account to use
*/}}
{{- define "directory-net.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "directory-net.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}

{{/*
Server fullname
*/}}
{{- define "directory-net.server.fullname" -}}
{{- printf "%s-server" (include "directory-net.fullname" .) }}
{{- end }}

{{/*
Web fullname
*/}}
{{- define "directory-net.web.fullname" -}}
{{- printf "%s-web" (include "directory-net.fullname" .) }}
{{- end }}

{{/*
Secret name for Cosmos DB credentials
*/}}
{{- define "directory-net.secretName" -}}
{{- printf "%s-secrets" (include "directory-net.fullname" .) }}
{{- end }}

{{/*
ConfigMap name
*/}}
{{- define "directory-net.configMapName" -}}
{{- printf "%s-config" (include "directory-net.fullname" .) }}
{{- end }}
