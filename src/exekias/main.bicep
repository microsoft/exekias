// Deployment template for sync services connected to a blob container.

param location string = resourceGroup().location

param runStoreName string
param storeContainer string
param metadataFilePattern string = '^(?<runId>(?<timestamp>(?<date>[\\d]+)-(?<time>[\\d]+))-(?<title>[^/]*))/params.json$'
param batchVmSize string = 'Standard_E2_v3'

var syncName = '${take(runStoreName, 19)}8sync'
var syncFunctionName = '${syncName}-${storeContainer}'

// RunStore storage
resource runStore 'Microsoft.Storage/storageAccounts@2023-04-01' existing = {
  name: runStoreName
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-04-01' existing = {
  name: 'default'
  parent: runStore
}

resource blobContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-04-01' = {
  name: storeContainer
  parent: blobService
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
resource syncMeta 'Microsoft.DocumentDB/databaseAccounts@2023-11-15' = {
  name: syncName
  location: location
  properties: {
    databaseAccountOfferType: 'Standard'
    locations: [
      {
        locationName: location
      }
    ]
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
    minimalTlsVersion: 'Tls12'
  }

  resource dataReader 'sqlRoleDefinitions' existing = {
    name: '00000000-0000-0000-0000-000000000001'  // Cosmos DB Built-in Data Reader
  }
  resource dataContributor 'sqlRoleDefinitions' existing = {
    name: '00000000-0000-0000-0000-000000000002'  // Cosmos DB Built-in Data Contributor
  }

  resource syncContributorAssignment 'sqlRoleAssignments' = {
    name: guid(syncApp.id, syncMeta.id, dataContributor.id)
    properties: {
        principalId: syncApp.identity.principalId
        roleDefinitionId: dataContributor.id
        scope: syncMeta.id
    }
  }

  resource poolContributorAssignment 'sqlRoleAssignments' = {
    name: guid(poolIdentity.id, syncMeta.id, dataContributor.id)
    properties: {
        principalId: poolIdentity.properties.principalId
        roleDefinitionId: dataContributor.id
        scope: syncMeta.id
    }
  }

}

// Storage account for sync services
resource syncStore 'Microsoft.Storage/storageAccounts@2023-04-01' = {
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
resource batchAccount 'Microsoft.Batch/batchAccounts@2024-02-01' = {
  name: syncName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    autoStorage: {
      storageAccountId: syncStore.id
      authenticationMode: 'BatchAccountManagedIdentity'
      nodeIdentityReference: {
        resourceId: poolIdentity.id
      }
    }
  }
}

resource poolIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  location: location
  name: '${runStoreName}-${storeContainer}-job-identity'
}

resource batchPool 'Microsoft.Batch/batchAccounts/pools@2024-02-01' = {
  parent: batchAccount
  name: 'exekias'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${poolIdentity.id}': {}
    }
  }
  properties: {
    deploymentConfiguration: {
      virtualMachineConfiguration: {
        imageReference: {
          publisher: 'MicrosoftWindowsServer'
          offer: 'WindowsServer'
          sku: '2022-datacenter-core-smalldisk'
          version: 'latest'
        }
        nodeAgentSkuId: 'batch.node.windows amd64'
      }
    }
    scaleSettings: {
      autoScale: {
        formula: '''
maxConcurrency = 10;
dormantTimeInterval = 2 * TimeInterval_Hour;
isNotDormant = $PendingTasks.GetSamplePercent(dormantTimeInterval) < 50 ? 1 : max($PendingTasks.GetSample(dormantTimeInterval));
observationTimeInterval = 1 * TimeInterval_Hour;
observedConcurrency = min(
    $PendingTasks.GetSamplePercent(observationTimeInterval) < 50 ? 1 : max(1, $PendingTasks.GetSample(observationTimeInterval)), 
    maxConcurrency);
$TargetDedicatedNodes = isNotDormant ? 1 : 0;
$TargetLowPriorityNodes = observedConcurrency - 1;
$NodeDeallocationOption = taskcompletion;'''
      }
    }
    vmSize: batchVmSize
  }
}
// Sync function

resource syncApp 'Microsoft.Web/sites@2023-12-01' = {
  name: syncFunctionName
  location: location
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    httpsOnly: true
    siteConfig: {
      use32BitWorkerProcess: false
      remoteDebuggingEnabled: false
      minTlsVersion: '1.2'
      ftpsState: 'FtpsOnly'
      appSettings: [
        {
          name: 'AzureWebJobsStorage__accountName'
          value: syncStore.name
        }
        {
          name: 'AzureWebJobsStorage__blobServiceUri'
          value: runStore.properties.primaryEndpoints.blob
        }
        {
          name: 'AzureWebJobsStorage__queueServiceUri'
          value: runStore.properties.primaryEndpoints.queue
        }
        {
          name: 'AzureWebJobsStorage__tableServiceUri'
          value: runStore.properties.primaryEndpoints.table
        }
        // {
        //     name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
        //     value: 'DefaultEndpointsProtocol=https;AccountName=${syncStore.name};AccountKey=${syncStore.listKeys().keys[0].value}'
        // }
        // {
        //     name: 'WEBSITE_CONTENTSHARE'
        //     value: toLower(syncFunctionName)
        // }
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
          name: 'RunStore:BlobContainerUrl'
          value: '${runStore.properties.primaryEndpoints.blob}${storeContainer}'
        }
        {
          name: 'RunStore:MetadataFilePattern'
          value: metadataFilePattern
        }
        {
          name: 'POOL_MANAGED_IDENTITY'
          value: poolIdentity.properties.clientId
        }
      ]
    }
  }
}

resource blobDataReaderRoleDefinition 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'
}

resource blobDataContributorRoleDefinition 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe' // Storage Blob Data Contributor
}

resource blobDataOwnerRoleDefinition 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
}

resource queueDataContributorRoleDefinition 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: '974c5e8b-45b9-4653-ba55-5f855dd0fb88' // Storage Queue Data Contributor
}

resource tableDataContributorRoleDefinition 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3' // Storage Table Data Contributor
}

resource functionSyncBlobOwnerRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: syncStore
  name: guid(syncApp.id, syncStore.id, blobDataOwnerRoleDefinition.id)
  properties: {
    principalId: syncApp.identity.principalId
    principalType: 'ServicePrincipal' // see https://learn.microsoft.com/en-gb/azure/role-based-access-control/role-assignments-template#new-service-principal
    roleDefinitionId: blobDataOwnerRoleDefinition.id
  }
}

resource functionSyncQueueContrinutorRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: syncStore
  name: guid(syncApp.id, syncStore.id, queueDataContributorRoleDefinition.id)
  properties: {
    principalId: syncApp.identity.principalId
    principalType: 'ServicePrincipal' // see https://learn.microsoft.com/en-gb/azure/role-based-access-control/role-assignments-template#new-service-principal
    roleDefinitionId: queueDataContributorRoleDefinition.id
  }
}

resource functionSyncTableContrinutorRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: syncStore
  name: guid(syncApp.id, syncStore.id, tableDataContributorRoleDefinition.id)
  properties: {
    principalId: syncApp.identity.principalId
    principalType: 'ServicePrincipal' // see https://learn.microsoft.com/en-gb/azure/role-based-access-control/role-assignments-template#new-service-principal
    roleDefinitionId: tableDataContributorRoleDefinition.id
  }
}

resource functionRunDataReaderRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: runStore
  name: guid(syncApp.id, runStore.id, blobDataReaderRoleDefinition.id)
  properties: {
    principalId: syncApp.identity.principalId
    principalType: 'ServicePrincipal' // see https://learn.microsoft.com/en-gb/azure/role-based-access-control/role-assignments-template#new-service-principal
    roleDefinitionId: blobDataReaderRoleDefinition.id
  }
}

resource batchSyncDataContributorRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: syncStore
  name: guid(batchAccount.id, syncStore.id, blobDataContributorRoleDefinition.id)
  properties: {
    principalId: batchAccount.identity.principalId
    principalType: 'ServicePrincipal' // see https://learn.microsoft.com/en-gb/azure/role-based-access-control/role-assignments-template#new-service-principal
    roleDefinitionId: blobDataContributorRoleDefinition.id
  }
}

resource poolRunDataReaderRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: runStore
  name: guid(poolIdentity.id, runStore.id, blobDataReaderRoleDefinition.id)
  properties: {
    principalId: poolIdentity.properties.principalId
    principalType: 'ServicePrincipal' // see https://learn.microsoft.com/en-gb/azure/role-based-access-control/role-assignments-template#new-service-principal
    roleDefinitionId: blobDataReaderRoleDefinition.id
  }
}

resource poolSyncDataContributorRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: syncStore
  name: guid(poolIdentity.id, syncStore.id, blobDataContributorRoleDefinition.id)
  properties: {
    principalId: poolIdentity.properties.principalId
    principalType: 'ServicePrincipal' // see https://learn.microsoft.com/en-gb/azure/role-based-access-control/role-assignments-template#new-service-principal
    roleDefinitionId: blobDataContributorRoleDefinition.id
  }
}

output syncFunctionId string = syncApp.id
output topicId string = topic.id
output batchAccountId string = batchAccount.id
output batchPoolId string = batchPool.id

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
