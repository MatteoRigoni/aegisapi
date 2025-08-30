// Gateway -> ACR pull
resource gatewayAcr 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, 'gateway-acrpull')
  scope: acr
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: gateway.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// Gateway -> Key Vault Secrets User
resource gatewayKv 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, 'gateway-kv')
  scope: kv
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
    principalId: gateway.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// Summarizer -> ACR pull
resource summarizerAcr 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, 'summarizer-acrpull')
  scope: acr
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: summarizer.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// Summarizer -> Key Vault Secrets User
resource summarizerKv 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, 'summarizer-kv')
  scope: kv
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
    principalId: summarizer.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// Dashboard -> ACR pull
resource dashboardAcr 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, 'dashboard-acrpull')
  scope: acr
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: dashboard.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// Dashboard -> Key Vault Secrets User
resource dashboardKv 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, 'dashboard-kv')
  scope: kv
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
    principalId: dashboard.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}
