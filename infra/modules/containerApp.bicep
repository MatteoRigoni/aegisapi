param name string
param image string
param environmentId string
param external bool
param acrLoginServer string
param keyVaultName string
@allowed([80, 8080])
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
          // evita hard-coded: usa environment().suffixes.keyvaultDns per il dominio KV
          // es.: vault.azure.net
          keyVaultUrl: 'https://${keyVaultName}${environment().suffixes.keyvaultDns}/secrets/sample-secret'
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: name
          image: image
          resources: {
            cpu: 0.5
            memory: '1Gi'
          }
          env: [
            {
              name: 'SAMPLE_SECRET'
              secretRef: 'sample-secret'
            }
            // Se l’app .NET ascolta su 8080, non serve altro.
            // Se vuoi forzare 80, decommenta:
            // {
            //   name: 'ASPNETCORE_URLS'
            //   value: 'http://+:80'
            // }
          ]
          // Probe (opzionale, utile in prod):
          // probes: [
          //   {
          //     type: 'liveness'
          //     httpGet: { path: '/', port: targetPort }
          //     initialDelaySeconds: 10
          //     periodSeconds: 15
          //   }
          // ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
}

output principalId string = app.identity.principalId
output fqdn string = app.properties.configuration.ingress?.fqdn
