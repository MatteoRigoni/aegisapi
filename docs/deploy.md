# Deployment Guide

This guide walks through configuring GitHub OIDC authentication, deploying the Bicep templates, and validating the Azure Container Apps endpoints.

## Prerequisites

- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) v2.45 or later
- Logged in with an account that can create Azure resources and Azure AD applications
- The GitHub repository with workflows that will deploy the infrastructure

## 1. Set variables
```bash
RG=aegisapi-rg
LOCATION=eastus
PREFIX=aegis
GITHUB_ORG=<your-org>
GITHUB_REPO=<your-repo>
```

## 2. Create resource group
```bash
az group create --name $RG --location $LOCATION
```

## 3. Configure GitHub OIDC federated identity
Create an Azure AD application and federated credential so GitHub Actions can request tokens without secrets.

```bash
APP_ID=$(az ad app create --display-name "$GITHUB_REPO-ci" --query appId -o tsv)
az ad sp create --id $APP_ID
az ad app federated-credential create --id $APP_ID --parameters "{\"name\":\"github\",\"issuer\":\"https://token.actions.githubusercontent.com\",\"subject\":\"repo:$GITHUB_ORG/$GITHUB_REPO:ref:refs/heads/main\",\"audiences\":[\"api://AzureADTokenExchange\"]}"
SUB_ID=$(az account show --query id -o tsv)
az role assignment create --assignee $APP_ID --role contributor --scope "/subscriptions/$SUB_ID/resourceGroups/$RG"
```

Configure your GitHub workflow to use the OIDC token:
```yaml
# .github/workflows/deploy.yml
permissions:
  id-token: write
  contents: read
steps:
  - uses: actions/checkout@v4
  - uses: azure/login@v1
    with:
      client-id: ${{ env.AZURE_CLIENT_ID }}
      tenant-id: ${{ env.AZURE_TENANT_ID }}
      subscription-id: ${{ env.AZURE_SUBSCRIPTION_ID }}
```
Set `AZURE_CLIENT_ID` to `$APP_ID`, `AZURE_TENANT_ID` to your tenant and `AZURE_SUBSCRIPTION_ID` to `$SUB_ID` as repository secrets.

## 4. Build and push container images
After the infrastructure deploys an Azure Container Registry, build your images and push them. Example:
```bash
ACR_NAME=${PREFIX}acr
az acr build -r $ACR_NAME -t gateway:latest path/to/gateway
az acr build -r $ACR_NAME -t summarizer:latest path/to/summarizer
az acr build -r $ACR_NAME -t dashboard:latest path/to/dashboard
```

## 5. Deploy Bicep templates
Provide the fully qualified image names referencing the ACR login server.
```bash
LOGIN_SERVER=$(az acr show -n ${PREFIX}acr --query loginServer -o tsv)
az deployment group create \
  --resource-group $RG \
  --template-file infra/main.bicep \
  --parameters namePrefix=$PREFIX \
               gatewayImage=$LOGIN_SERVER/gateway:latest \
               summarizerImage=$LOGIN_SERVER/summarizer:latest \
               dashboardImage=$LOGIN_SERVER/dashboard:latest
```
Deployment outputs will include the container app FQDNs.

## 6. Verify endpoints
```bash
GATEWAY_FQDN=$(az containerapp show -n ${PREFIX}-gateway -g $RG --query properties.configuration.ingress.fqdn -o tsv)
DASHBOARD_FQDN=$(az containerapp show -n ${PREFIX}-dashboard -g $RG --query properties.configuration.ingress.fqdn -o tsv)

echo "Gateway: https://$GATEWAY_FQDN"
echo "Dashboard: https://$DASHBOARD_FQDN"

curl -I https://$GATEWAY_FQDN
curl -I https://$DASHBOARD_FQDN
```

The summarizer app uses internal ingress and is reachable only within the environment.

## Cleanup
```bash
az group delete --name $RG --yes --no-wait
```
