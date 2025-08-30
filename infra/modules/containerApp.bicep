@description('Container App name')
param name string

@description('Full image name')
param image string

@description('Managed environment resource ID')
param environmentId string

@description('Expose public ingress')
param external bool

@description('ACR login server (e.g., contoso.azurecr.io)')
param acrLoginServer string

@description('Key Vault name')
param keyVaultName string

@description('Container target port')
param targetPort int = 8080

// Usa il suffisso KeyVault dell'ambiente cloud, no hardcoded "vault.azure.net"
var keyVaultDns = environment().suffixes.keyvaultDns
var secretUrl = 'https://${keyVaultName}.${keyVaultDns}/secrets/sample-secret'

resource app 'Microsoft.App/containerApps@2023-05-01' = {
  name: name
  location: resourceGroup().location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      ingress: {
        external: external
        targetPort: targetPort
      }
      registries: [
        {
          server: acrLoginServer
          identity: 'system'
        }
      ]
      secrets: [
        {
          name: 'sample-secret'
          keyVaultUrl: secretUrl
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: name
          image: image
          env: [
            {
              name: 'SAMPLE_SECRET'
              secretRef: 'sample-secret'
            }
          ]
        }
      ]
    }
  }
}

output principalId string = app.identity.principalId
output fqdn string = app.properties.configuration.ingress.fqdn
