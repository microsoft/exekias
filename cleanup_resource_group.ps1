################################################################
# WARNING: the script deletes ALL resources in a resource group
################################################################
Param(
    [Parameter(Mandatory, HelpMessage="Azure subscription name.")][string]
    $subscription,
    [Parameter(Mandatory, HelpMessage="Azure resource group name.")][string]
    $resourceGroup
)

Write-Host "[$(Get-Date)] Deleting ALL resources in resource group $resourceGroup in $subscription..."


Set-AzContext -Subscription $subscription | Out-Null

$inProgress = Get-AzResourceGroupDeployment -ResourceGroupName $resourceGroup | Where-Object ProvisioningState -eq Running
if ($inProgress.Count -gt 0) {
    Write-Host "[$(Get-Date)] Wait while running deployments complete."
    $timeout = (Get-Date) + (New-TimeSpan -Minutes 60)
    while (((Get-Date) -lt $timeout) -and ($inProgress.Count -gt 0)) {
        Start-Sleep -Seconds 10
        $inProgress = Get-AzResourceGroupDeployment -ResourceGroupName $resourceGroup | Where-Object ProvisioningState -eq Running
    }
}

$emptyPath = Join-Path -Path ([IO.Path]::GetTempPath()) -ChildPath "empty.json"
$deploymentName = "cleanup_$((Get-Date).ToString("yyyyMMdd_HHmmss"))"
@'
{
    "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#",
    "contentVersion": "1.0.0.0",
    "resources": []
  }
'@ | Set-Content -Path $emptyPath -Encoding UTF8
Write-Host "[$(Get-Date)] Start deployment to set resource group in a state with no resources."
New-AzResourceGroupDeployment -Name $deploymentName  -ResourceGroupName $resourceGroup -TemplateFile $emptyPath -Mode Complete -Force
Remove-Item -Path $emptyPath
$resources = Get-AzResource -ResourceGroupName $resourceGroup
if ($resources) {
    $resources | Select-Object -Property Name,ResourceType | Format-Table
    Write-Error "Resource group is not empty"
    exit 1
}

Write-Host "[$(Get-Date)] Resource group $resourceGroup is now empty."
