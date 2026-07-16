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

var normalizedEnv = toLower(replace(environmentName, '_', '-'))
// App Service app names are globally unique. Delete any other site using this name before provisioning.
var webAppName = 'app-habitinator-${normalizedEnv}'
// Must match the App Service default hostname so tokens validate for this deployment.
var jwtIssuerUrl = 'https://${webAppName}.azurewebsites.net'
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
      linuxFxVersion: 'DOTNETCORE|10.0'
      appCommandLine: 'chmod +x App.Web && ./App.Web'
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
          value: jwtIssuerUrl
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

resource authSettings 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: web
  name: 'authsettingsv2'
  properties: {
    enabled: false
  }
}


output AZURE_WEBAPP_NAME string = web.name
output AZURE_WEBAPP_URL string = 'https://${web.properties.defaultHostName}'
output PRODUCTION_API_BASE_URL string = 'https://${web.properties.defaultHostName}'
output PRODUCTION_WEB_URL string = 'https://${web.properties.defaultHostName}'
