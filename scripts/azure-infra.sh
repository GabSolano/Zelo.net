#!/usr/bin/env bash
# ==============================================================================
# Script Azure CLI para provisionamento da infraestrutura da zelo-api-dotnet
# Uso pelo time de DevOps (todas as credenciais vêm de variáveis de ambiente)
# ==============================================================================
set -euo pipefail

RESOURCE_GROUP="${AZURE_RESOURCE_GROUP:-rg-zelo-gs2026}"
LOCATION="${AZURE_LOCATION:-brazilsouth}"
SQL_SERVER_NAME="${AZURE_SQL_SERVER:-sql-zelo-gs2026}"
SQL_DB_NAME="${AZURE_SQL_DB:-zelo-cuidado-db}"
APP_SERVICE_PLAN="${AZURE_APP_PLAN:-asp-zelo-gs2026}"
WEBAPP_NAME="${AZURE_WEBAPP_NAME:-zelo-api-dotnet}"

if [ -z "${SQL_ADMIN_USER:-}" ] || [ -z "${SQL_ADMIN_PASSWORD:-}" ] || [ -z "${JWT_SECRET:-}" ]; then
  echo "Erro: Defina SQL_ADMIN_USER, SQL_ADMIN_PASSWORD e JWT_SECRET nas variáveis de ambiente."
  exit 1
fi

az group create --name "$RESOURCE_GROUP" --location "$LOCATION"

az sql server create \
  --name "$SQL_SERVER_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --admin-user "$SQL_ADMIN_USER" \
  --admin-password "$SQL_ADMIN_PASSWORD"

az sql server firewall-rule create \
  --resource-group "$RESOURCE_GROUP" \
  --server "$SQL_SERVER_NAME" \
  --name "AllowAzureServices" \
  --start-ip-address 0.0.0.0 \
  --end-ip-address 0.0.0.0

az sql db create \
  --resource-group "$RESOURCE_GROUP" \
  --server "$SQL_SERVER_NAME" \
  --name "$SQL_DB_NAME" \
  --service-objective Basic

az appservice plan create \
  --name "$APP_SERVICE_PLAN" \
  --resource-group "$RESOURCE_GROUP" \
  --sku B1 \
  --is-linux

az webapp create \
  --resource-group "$RESOURCE_GROUP" \
  --plan "$APP_SERVICE_PLAN" \
  --name "$WEBAPP_NAME" \
  --runtime "DOTNETCORE:8.0"

az webapp config appsettings set \
  --resource-group "$RESOURCE_GROUP" \
  --name "$WEBAPP_NAME" \
  --settings \
    DB_PROVIDER="SqlServer" \
    JWT_SECRET="$JWT_SECRET" \
    MODO_SIMULACAO="true"
