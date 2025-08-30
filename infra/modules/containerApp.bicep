param name string
param image string
param environmentId string
param external bool
param acrLoginServer string
param keyVaultName string
param targetPort int = 8080

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
          keyVaultUrl: 'https://${keyVaultName}.vault.azure.net/secrets/sample-secret'
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
