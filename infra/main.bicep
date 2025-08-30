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

@description('ACR name to use (e.g., aegisacr)')
param acrName string = '${namePrefix}acr'

@description('Create the ACR if it does not exist')
param createAcr bool = true

var logAnalyticsName = '${namePrefix}-la'
var keyVaultName = '${namePrefix}-kv'
var envName = '${namePrefix}-env'

//
// ACR: crea solo se richiesto, altrimenti riferisci l’esistente
//
resource acr 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' = if (createAcr) {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
  }
}

resource acrExisting 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' existing = if (!createAcr) {
  name: acrName
}

var acrLoginServer = createAcr
  ? acr.properties.loginServer
  : reference(acrExisting.id, '2023-01-01-preview').loginServer

//
// Log Analytics
//
resource la 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    retentionInDays: 30
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

var laRef  = reference(la.id, la.apiVersion)
var laKeys = listKeys(la.id, la.apiVersion)

//
// Managed Environment (API versione supportata)
//
resource env 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: envName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: laRef.customerId
        sharedKey: laKeys.primarySharedKey
      }
    }
  }
  dependsOn: [
    la
  ]
}

//
// Key Vault (RBAC-enabled)
//
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

//
// Container Apps (modulo) – passa acrLoginServer unificato
//
module gateway './modules/containerApp.bicep' = {
  name: 'gatewayApp'
  params: {
    name: '${namePrefix}-gateway'
    image: gatewayImage
    environmentId: env.id
    external: true
    acrLoginServer: acrLoginServer
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
    acrLoginServer: acrLoginServer
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
    acrLoginServer: acrLoginServer
    keyVaultName: keyVaultName
  }
}

//
// Role assignments (nomi deterministici)
// NB: richiedono che chi esegue il deploy abbia almeno "User Access Administrator" o "Owner".
//
resource gatewayAcr 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, 'gateway-acrpull')
  scope: (createAcr ? acr : acrExisting)
  properties: {
    // AcrPull
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: gateway.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource gatewayKv 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, 'gateway-kv')
  scope: kv
  properties: {
    // Key Vault Secrets User
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
    principalId: gateway.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource summarizerAcr 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, 'summarizer-acrpull')
  scope: (createAcr ? acr : acrExisting)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: summarizer.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource summarizerKv 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, 'summarizer-kv')
  scope: kv
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
    principalId: summarizer.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource dashboardAcr 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, 'dashboard-acrpull')
  scope: (createAcr ? acr : acrExisting)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: dashboard.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource dashboardKv 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, 'dashboard-kv')
  scope: kv
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
    principalId: dashboard.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

output gatewayFqdn string = gateway.outputs.fqdn
output summarizerFqdn string = summarizer.outputs.fqdn
output dashboardFqdn string = dashboard.outputs.fqdn
