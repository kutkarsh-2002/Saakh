#!/usr/bin/env bash
#
# Provisions the Saakh demo on Azure: an Azure SQL free-offer database and two
# Container Apps (the nginx/Angular web app, and the API behind internal ingress).
#
# This is written to be run once, by hand, by someone logged in with `az login`.
# It is idempotent enough to re-run: every create is guarded by an existence
# check, so a failed run can be repeated without tearing anything down first.
#
# What it deliberately does NOT do:
#   - build or push images (see push-images.sh; Container Apps pulls from a public
#     registry so the free tier is not spent on a private one)
#   - run migrations (the API applies them on boot here; see MIGRATE_ON_STARTUP)
#
# Read deploy/azure/README.md before running this. The free-tier limits are not
# generous enough to ignore, and two of the choices below exist only because of
# them.

set -euo pipefail

# ---- settings ----------------------------------------------------------------
# Override any of these in the environment: LOCATION=westeurope ./provision.sh

RESOURCE_GROUP="${RESOURCE_GROUP:-saakh-demo}"
LOCATION="${LOCATION:-centralindia}"
ENVIRONMENT="${ENVIRONMENT:-saakh-env}"

SQL_SERVER="${SQL_SERVER:-saakh-sql-$RANDOM}"
SQL_DB="${SQL_DB:-Saakh}"
SQL_ADMIN="${SQL_ADMIN:-saakhadmin}"

# The images Container Apps will pull. Set these to wherever push-images.sh put
# them — they must be publicly readable, or Container Apps needs registry
# credentials that the free tier is not the place to manage.
API_IMAGE="${API_IMAGE:?set API_IMAGE, e.g. ghcr.io/youruser/saakh-api:latest}"
WEB_IMAGE="${WEB_IMAGE:?set WEB_IMAGE, e.g. ghcr.io/youruser/saakh-web:latest}"

# Secrets. Generated if not supplied, and printed once at the end.
SQL_PASSWORD="${SQL_PASSWORD:-$(openssl rand -base64 24 | tr -d '/+=' | cut -c1-20)Aa1!}"
JWT_KEY="${JWT_KEY:-$(openssl rand -base64 48 | tr -d '\n')}"

say() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }

# ---- preflight ---------------------------------------------------------------

command -v az >/dev/null || { echo "az CLI not found. Install it first." >&2; exit 1; }
az account show >/dev/null 2>&1 || { echo "Not logged in. Run: az login" >&2; exit 1; }

say "Subscription"
az account show --query '{name:name, id:id}' -o tsv

az extension add --name containerapp --upgrade --only-show-errors >/dev/null
az provider register --namespace Microsoft.App --wait >/dev/null
az provider register --namespace Microsoft.OperationalInsights --wait >/dev/null

# ---- resource group ----------------------------------------------------------

say "Resource group: $RESOURCE_GROUP"
az group create -n "$RESOURCE_GROUP" -l "$LOCATION" -o none

# ---- SQL -----------------------------------------------------------------------
# The free offer is a serverless General Purpose database with auto-pause. It
# comes with 100,000 vCore-seconds a month; at the 0.5-vCore floor that is about
# 55 hours of being *online*, not of being busy. Hence auto-pause at the minimum
# delay, and hence Jobs__Enabled=false on the API below: Hangfire's 15-second
# poll would hold the database open and spend the month's allowance in ~2 days.

say "SQL server: $SQL_SERVER"
if ! az sql server show -g "$RESOURCE_GROUP" -n "$SQL_SERVER" -o none 2>/dev/null; then
  az sql server create \
    -g "$RESOURCE_GROUP" -n "$SQL_SERVER" -l "$LOCATION" \
    -u "$SQL_ADMIN" -p "$SQL_PASSWORD" -o none
fi

# Container Apps egress IPs are not fixed on the consumption plan, so the
# database is opened to Azure services rather than to a pinned address. This is
# the demo-grade choice: the database still requires the password, but it is
# reachable from inside Azure. A real deployment uses a private endpoint or VNet
# integration instead.
say "SQL firewall: allow Azure services"
az sql server firewall-rule create \
  -g "$RESOURCE_GROUP" -s "$SQL_SERVER" -n AllowAzureServices \
  --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 -o none 2>/dev/null || true

say "SQL database: $SQL_DB (free offer)"
if ! az sql db show -g "$RESOURCE_GROUP" -s "$SQL_SERVER" -n "$SQL_DB" -o none 2>/dev/null; then
  # --use-free-limit is what selects the free grant. "AutoPause" tells Azure to
  # stop the database for the rest of the month once the grant is used up, rather
  # than billing the overage.
  az sql db create \
    -g "$RESOURCE_GROUP" -s "$SQL_SERVER" -n "$SQL_DB" \
    --edition GeneralPurpose --compute-model Serverless \
    --family Gen5 --capacity 2 --min-capacity 0.5 \
    --auto-pause-delay 60 \
    --use-free-limit --free-limit-exhaustion-behavior AutoPause \
    --backup-storage-redundancy Local \
    -o none
fi

SQL_FQDN="$(az sql server show -g "$RESOURCE_GROUP" -n "$SQL_SERVER" --query fullyQualifiedDomainName -o tsv)"
CONNECTION_STRING="Server=tcp:${SQL_FQDN},1433;Initial Catalog=${SQL_DB};User ID=${SQL_ADMIN};Password=${SQL_PASSWORD};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;"

# ---- Container Apps environment ----------------------------------------------

say "Container Apps environment: $ENVIRONMENT"
if ! az containerapp env show -g "$RESOURCE_GROUP" -n "$ENVIRONMENT" -o none 2>/dev/null; then
  az containerapp env create \
    -g "$RESOURCE_GROUP" -n "$ENVIRONMENT" -l "$LOCATION" -o none
fi

# ---- API ---------------------------------------------------------------------
# Internal ingress: only the web app talks to it, so it gets no public hostname.
# Note the port asymmetry — the container listens on 5000, but Container Apps
# publishes every internal app on 80 and routes to the target port. That is why
# the web app's API_UPSTREAM below is "api" and not "api:5000".
#
# min-replicas 0 lets the whole thing cost nothing while idle, at the price of a
# cold start on the first request. The free monthly grant (180,000 vCPU-seconds)
# does not cover even one always-on replica, so this is not really optional.

say "Container app: api"
az containerapp create \
  -g "$RESOURCE_GROUP" -n api --environment "$ENVIRONMENT" \
  --image "$API_IMAGE" \
  --ingress internal --target-port 5000 --transport auto \
  --min-replicas 0 --max-replicas 1 \
  --cpu 0.5 --memory 1.0Gi \
  --secrets "conn=$CONNECTION_STRING" "jwt=$JWT_KEY" \
  --env-vars \
    "ConnectionStrings__Default=secretref:conn" \
    "Jwt__Key=secretref:jwt" \
    "ASPNETCORE_ENVIRONMENT=Production" \
    "Database__MigrateOnStartup=true" \
    "Jobs__Enabled=false" \
    "Gstin__Provider=Mock" \
    "Email__Provider=Log" \
    "Sms__Provider=Log" \
    "Otp__RevealCodeInResponse=true" \
  -o none

# ---- web ---------------------------------------------------------------------

say "Container app: web"
az containerapp create \
  -g "$RESOURCE_GROUP" -n web --environment "$ENVIRONMENT" \
  --image "$WEB_IMAGE" \
  --ingress external --target-port 80 --transport auto \
  --min-replicas 0 --max-replicas 1 \
  --cpu 0.25 --memory 0.5Gi \
  --env-vars "API_UPSTREAM=api" \
  -o none

WEB_FQDN="$(az containerapp show -g "$RESOURCE_GROUP" -n web --query properties.configuration.ingress.fqdn -o tsv)"

# The API only ever sees the browser through the web app's nginx, on that app's
# own origin, so CORS matters only if the API is ever given public ingress. Set
# anyway so that change does not become a silent 401-shaped mystery.
az containerapp update -g "$RESOURCE_GROUP" -n api \
  --set-env-vars "Cors__Origins__0=https://${WEB_FQDN}" -o none

# ---- done --------------------------------------------------------------------

cat <<EOF

$(say "Deployed")

  URL                https://${WEB_FQDN}
  SQL server         ${SQL_FQDN}
  SQL admin          ${SQL_ADMIN}
  SQL password       ${SQL_PASSWORD}
  JWT key            ${JWT_KEY}

Save that password and key now — the password is not recoverable from Azure.

The database is empty. Seed the demo accounts with:

  az containerapp exec -g ${RESOURCE_GROUP} -n api --command "dotnet Saakh.Api.dll --seed"

Then sign in at https://${WEB_FQDN} as lender1@saakh.demo / Saakh@2026

First request after an idle period is slow: both apps scale to zero and the
database auto-pauses, so expect 30-60s on a cold hit before anything renders.

Background jobs are OFF (Jobs__Enabled=false). The overdue-settlement closure,
suspension expiry and GSTIN retry will not run. README.md explains why and what
to do if you want them.
EOF
