// Deployment template for sync services connected to a blob container.

param location string = resourceGroup().location

param runStoreName string
param storeContainer string
param metadataFilePattern string = '^(?<runId>(?<timestamp>(?<date>[\\d]+)-(?<time>[\\d]+))-(?<title>[^/]*))/params.json$'
param batchVmSize string = 'Standard_E2_v3'

var syncName = '${take(runStoreName, 19)}8sync'
var syncFunctionName = '${syncName}-${storeContainer}'

// RunStore storage
resource runStore 'Microsoft.Storage/storageAccounts@2022-09-01' existing = {
    name: runStoreName
}

resource blobContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2022-09-01' = {
    name: '${runStoreName}/default/${storeContainer}'
}

resource topic 'Microsoft.EventGrid/systemTopics@2022-06-15' = {
    name: runStoreName
    location: location
    properties: {
        source: runStore.id
        topicType: 'Microsoft.Storage.StorageAccounts'
    }
}

// ExekiasStore CosmosDB database
resource syncMeta 'Microsoft.DocumentDB/databaseAccounts@2021-03-15' = {
    name: syncName
    location: location
    properties: {
        databaseAccountOfferType: 'Standard'
        locations: [ {
                locationName: location
            } ]
        capabilities: [ {
                name: 'EnableServerless'
            } ]
    }
}

// Storage account for sync services
resource syncStore 'Microsoft.Storage/storageAccounts@2022-09-01' = {
    name: syncName
    location: location
    kind: 'StorageV2'
    sku: {
        name: 'Standard_LRS'
    }
    properties: {
        allowBlobPublicAccess: false
    }
}

// Batch account
resource batchAccount 'Microsoft.Batch/batchAccounts@2022-10-01' = {
    name: syncName
    location: location
    properties: {
        autoStorage: {
            storageAccountId: syncStore.id
        }
    }
}

// Sync function

resource syncApp 'Microsoft.Web/sites@2022-09-01' = {
    name: syncFunctionName
    location: location
    kind: 'functionapp'
    properties: {
        siteConfig: {
            use32BitWorkerProcess: false
            remoteDebuggingEnabled: false
            minTlsVersion: '1.2'
            ftpsState: 'FtpsOnly'
            appSettings: [
                {
                    name: 'AzureWebJobsStorage'
                    value: 'DefaultEndpointsProtocol=https;AccountName=${syncStore.name};AccountKey=${syncStore.listKeys().keys[0].value}'
                }
                {
                    name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
                    value: 'DefaultEndpointsProtocol=https;AccountName=${syncStore.name};AccountKey=${syncStore.listKeys().keys[0].value}'
                }
                {
                    name: 'WEBSITE_CONTENTSHARE'
                    value: toLower(syncFunctionName)
                }
                {
                    name: 'WEBSITE_RUN_FROM_PACKAGE'
                    value: '1'
                }
                {
                    name: 'FUNCTIONS_WORKER_RUNTIME'
                    value: 'dotnet'
                }
                {
                    name: 'FUNCTIONS_EXTENSION_VERSION'
                    value: '~4'
                }
                {
                    name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
                    value: logStore.properties.InstrumentationKey
                }
                {
                    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
                    value: logStore.properties.ConnectionString
                }
                {
                    name: 'Batch:AccessKey'
                    value: batchAccount.listKeys().primary
                }
                {
                    name: 'Batch:Endpoint'
                    value: batchAccount.properties.accountEndpoint
                }
                {
                    name: 'Batch:Name'
                    value: batchAccount.name
                }
                {
                    name: 'Batch:VmSize'
                    value: batchVmSize
                }
                {
                    name: 'ExekiasCosmos:ConnectionString'
                    value: syncMeta.listConnectionStrings().connectionStrings[0].connectionString
                }
                {
                    name: 'ExekiasCosmos:ContainerName'
                    value: storeContainer
                }
                {
                    name: 'ImportStore:ConnectionString'
                    value: 'DefaultEndpointsProtocol=https;AccountName=${syncStore.name};AccountKey=${syncStore.listKeys().keys[0].value}'
                }
                {
                    name: 'ImportStore:BlobContainerName'
                    value: 'shadow-${storeContainer}'
                }
                {
                    name: 'Pipeline:ThresholdSeconds'
                    value: '30'
                }
                {
                    name: 'RunStore:BlobContainerName'
                    value: storeContainer
                }
                {
                    name: 'RunStore:ConnectionString'
                    value: 'DefaultEndpointsProtocol=https;AccountName=${runStore.name};AccountKey=${runStore.listKeys().keys[0].value}'
                }
                {
                    name: 'RunStore:MetadataFilePattern'
                    value: metadataFilePattern
                }
            ]
        }
    }
}


output syncFunctionId string = syncApp.id
output topicId string = topic.id
output batchAccountId string = batchAccount.id

// additional resources

resource logStore 'Microsoft.Insights/components@2020-02-02' = {
    name: syncName
    location: location
    kind: 'web'
    properties: {
        Application_Type: 'general'
        WorkspaceResourceId: workspace.id
    }
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2020-10-01' = {
    name: syncName
    location: location
}
