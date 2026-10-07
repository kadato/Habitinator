@description('Environment name used as a suffix for resources.')
param environmentName string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('App Service plan SKU (B1 is a low-cost baseline).')
@allowed([
  'B1'
  'S1'
])
param appServicePlanSkuName string = 'B1'

@secure()
@description('Full PostgreSQL connection string (e.g. from Neon or any Postgres provider).')
param postgresConnectionString string

@description('JWT audience for App.Web.')
param jwtAudience string = 'habitinator-clients'

@secure()
@description('JWT signing key for App.Web (minimum 32 chars recommended).')
param jwtSigningKey string

@description('Seeded demo user email.')
param demoUserEmail string = 'guest@habitinator.local'

@secure()
@description('Seeded demo user password.')
param demoUserPassword string

@description('OTLP endpoint for OpenTelemetry. Empty disables export.')
param otlpEndpoint string = ''

var normalizedEnv = toLower(replace(environmentName, '_', '-'))
// App Service app names are globally unique. Delete any other site using this name before provisioning.
var webAppName = 'app-habitinator-${normalizedEnv}'
// Must match the App Service default hostname so tokens validate for this deployment.
var jwtIssuerUrl = 'https://${webAppName}.azurewebsites.net'
var appServicePlanName = 'asp-habitinator-${normalizedEnv}'
// Vault names are globally unique with a 24 character cap.
var keyVaultName = 'kvhab${uniqueString(resourceGroup().id, environmentName)}'

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: appServicePlanSkuName
    tier: appServicePlanSkuName == 'B1' ? 'Basic' : 'Standard'
    size: appServicePlanSkuName
    capacity: 1
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource web 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  tags: {
    'azd-service-name': 'web'
    'azd-env-name': environmentName
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    clientCertEnabled: false
    siteConfig: {
      // Stay on 10.0 until Azure adds DOTNETCORE|11.0 after .NET 11 GA, expected Nov 2026.
      // The app ships self-contained, so the platform version is metadata only.
      linuxFxVersion: 'DOTNETCORE|10.0'
      appCommandLine: 'chmod +x App.Web && ./App.Web'
      minTlsVersion: '1.2'
      alwaysOn: true
      healthCheckPath: '/health/ready'
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'ConnectionStrings__DefaultConnection'
          value: '@Microsoft.KeyVault(SecretUri=${keyVault.properties.vaultUri}secrets/postgres-connection/)'
        }
        {
          name: 'Jwt__Issuer'
          value: jwtIssuerUrl
        }
        {
          name: 'Jwt__Audience'
          value: jwtAudience
        }
        {
          name: 'Jwt__SigningKey'
          value: '@Microsoft.KeyVault(SecretUri=${keyVault.properties.vaultUri}secrets/jwt-signing-key/)'
        }
        {
          name: 'DemoUser__Email'
          value: demoUserEmail
        }
        {
          name: 'DemoUser__Password'
          value: '@Microsoft.KeyVault(SecretUri=${keyVault.properties.vaultUri}secrets/demo-user-password/)'
        }
        {
          name: 'OTEL_EXPORTER_OTLP_ENDPOINT'
          value: otlpEndpoint
        }
      ]
    }
  }
}

resource authSettings 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: web
  name: 'authsettingsv2'
  properties: {
    platform: {
      enabled: false
    }
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
  }
}

resource postgresSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'postgres-connection'
  properties: {
    value: postgresConnectionString
  }
}

resource jwtSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'jwt-signing-key'
  properties: {
    value: jwtSigningKey
  }
}

resource demoPasswordSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'demo-user-password'
  properties: {
    value: demoUserPassword
  }
}

// Lets the web app read the three secrets above. Key Vault RBAC can lag a few
// minutes, so the first start after provisioning may need one restart.
resource vaultReaderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, web.id, '4633458b-17de-408a-b874-0445c86b69e6')
  scope: keyVault
  properties: {
    // Key Vault Secrets User
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: web.identity.principalId
    principalType: 'ServicePrincipal'
  }
}


output AZURE_WEBAPP_NAME string = web.name
output AZURE_WEBAPP_URL string = 'https://${web.properties.defaultHostName}'
output PRODUCTION_API_BASE_URL string = 'https://${web.properties.defaultHostName}'
output PRODUCTION_WEB_URL string = 'https://${web.properties.defaultHostName}'
