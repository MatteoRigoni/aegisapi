targetScope = 'resourceGroup'

@description('Name prefix for all resources')
param namePrefix string

@description('Location for all resources')
param location string = resourceGroup().location

@description('Gateway container image (e.g., <acr>.azurecr.io/gateway:latest)')
param gatewayImage string

@description('Summarizer container image (e.g., <acr>.azurecr.io/summarizer:latest)')
param summarizerImage string

@description('Dashboard container image (e.g., <acr>.azurecr.io/dashboard:latest)')
param dashboardImage string

// Suffix deterministico per nomi global-unique (ACR/KV)
var suffix = toLower(uniqueString(subscription().id, resourceGroup().id))

// ACR: 5-50, alfanumerico; limitiamo lunghezza
var acrName = substring('${namePrefix}${suffix}acr', 0, 50)

// Log Analytics: nome locale al RG
var logAnalyticsName = '${namePrefix}-la'

// Key Vault: 3-24, minuscolo, numeri e '-', non finire con '-'
var keyVaultBase = substring('${namePrefix}-kv-${suffix}', 0, 24)
var keyVaultName = endsWith(keyVaultBase, '-') ? substring(keyVaultBase, 0, length(keyVaultBase) - 1) : keyVaultBase

var envName = '${namePrefix}-env'

// ---------- Resources ----------

resource acr 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' = {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
  }
}

resource la 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    retentionInDays: 30
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// listKeys via funzione ARM (ok anche se il linter suggerisce la variante resource-function)
var laKeys = listKeys(la.id, la.apiVersion)

// ⚠️ API version aggiornata e supportata per EastUS
resource env 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: envName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: la.properties.customerId
        sharedKey: laKeys.primarySharedKey
      }
    }
  }
}

resource kv 'Microsoft.KeyVault/vaults@2023-02-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      name: 'standard'
      family: 'A'
    }
    enableRbacAuthorization: true
    publicNetworkAccess: 'Enabled'
  }
}

// ---------- Modules (Container Apps) ----------

module gateway './modules/containerApp.bicep' = {
  name: 'gatewayApp'
  params: {
    name: '${namePrefix}-gateway'
    image: gatewayImage
    environmentId: env.id
    external: true
    acrLoginServer: acr.properties.loginServer
    keyVaultName: keyVaultName
  }
}

module summarizer './modules/containerApp.bicep' = {
  name: 'summarizerApp'
  params: {
    name: '${namePrefix}-summarizer'
    image: summarizerImage
    environmentId: env.id
    external: false
    acrLoginServer: acr.properties.loginServer
    keyVaultName: keyVaultName
  }
}

module dashboard './modules/containerApp.bicep' = {
  name: 'dashboardApp'
  params: {
    name: '${namePrefix}-dashboard'
    image: dashboardImage
    environmentId: env.id
    external: true
    acrLoginServer: acr.properties.loginServer
    keyVaultName: keyVaultName
  }
}

// ---------- Role Assignments (extension resources con scope tipizzato) ----------
// NB: usare il simbolo di risorsa nello scope evita l'errore BCP420.

resource gatewayAcr 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, 'gateway-acrpull')
  scope: acr
  properties: {
    // AcrPull
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: gateway.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource gatewayKv 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, 'gateway-kv')
  scope: kv
  properties: {
    // Key Vault Secrets User
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
    principalId: gateway.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource summarizerAcr 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, 'summarizer-acrpull')
  scope: acr
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: summarizer.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource summarizerKv 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, 'summarizer-kv')
  scope: kv
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
    principalId: summarizer.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource dashboardAcr 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, 'dashboard-acrpull')
  scope: acr
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: dashboard.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource dashboardKv 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, 'dashboard-kv')
  scope: kv
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
    principalId: dashboard.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------- Outputs ----------
output gatewayFqdn string = gateway.outputs.fqdn
output summarizerFqdn string = summarizer.outputs.fqdn
output dashboardFqdn string = dashboard.outputs.fqdn
