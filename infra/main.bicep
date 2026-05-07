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

@description('JWT issuer for App.Web.')
param jwtIssuer string

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

var normalizedEnv = toLower(replace(environmentName, '_', '-'))
var webAppName = 'app-habitinator-${normalizedEnv}-${uniqueString(resourceGroup().id)}'
var appServicePlanName = 'asp-habitinator-${normalizedEnv}'

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
  tags: {
    'azd-service-name': 'web'
    'azd-env-name': environmentName
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      minTlsVersion: '1.2'
      alwaysOn: true
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'ConnectionStrings__DefaultConnection'
          value: postgresConnectionString
        }
        {
          name: 'Jwt__Issuer'
          value: jwtIssuer
        }
        {
          name: 'Jwt__Audience'
          value: jwtAudience
        }
        {
          name: 'Jwt__SigningKey'
          value: jwtSigningKey
        }
        {
          name: 'DemoUser__Email'
          value: demoUserEmail
        }
        {
          name: 'DemoUser__Password'
          value: demoUserPassword
        }
      ]
    }
  }
}

output AZURE_WEBAPP_NAME string = web.name
output AZURE_WEBAPP_URL string = 'https://${web.properties.defaultHostName}'
output PRODUCTION_API_BASE_URL string = 'https://${web.properties.defaultHostName}'
output PRODUCTION_WEB_URL string = 'https://${web.properties.defaultHostName}'
