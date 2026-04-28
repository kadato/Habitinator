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

@description('PostgreSQL Flexible Server SKU. Standard_B1ms is low-cost for demos.')
param postgresSkuName string = 'Standard_B1ms'

@description('PostgreSQL storage in GB.')
@minValue(32)
param postgresStorageGb int = 32

@description('PostgreSQL admin username.')
param postgresAdminLogin string

@secure()
@description('PostgreSQL admin password.')
param postgresAdminPassword string

@description('PostgreSQL database name for the app.')
param postgresDatabaseName string = 'habitinator'

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
var webAppName = 'app-web-${uniqueString(resourceGroup().id, normalizedEnv)}'
var appServicePlanName = 'asp-${normalizedEnv}-${uniqueString(resourceGroup().id)}'
var postgresServerName = 'pg-${normalizedEnv}-${uniqueString(resourceGroup().id)}'

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

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2023-12-01-preview' = {
  name: postgresServerName
  location: location
  sku: {
    name: postgresSkuName
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    storage: {
      storageSizeGB: postgresStorageGb
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    network: {
      publicNetworkAccess: 'Enabled'
    }
  }
}

resource postgresAllowAzureIps 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2023-12-01-preview' = {
  name: 'AllowAzureServices'
  parent: postgres
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource postgresDb 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2023-12-01-preview' = {
  name: postgresDatabaseName
  parent: postgres
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

resource web 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  tags: {
    'azd-service-name': 'web'
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
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'ConnectionStrings__DefaultConnection'
          value: 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=${postgresDatabaseName};Username=${postgresAdminLogin};Password=${postgresAdminPassword};Ssl Mode=Require;Trust Server Certificate=False'
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
output AZURE_POSTGRESQL_SERVER string = postgres.name
output AZURE_POSTGRESQL_DATABASE string = postgresDatabaseName
output PRODUCTION_API_BASE_URL string = 'https://${web.properties.defaultHostName}'
output PRODUCTION_WEB_URL string = 'https://${web.properties.defaultHostName}'
