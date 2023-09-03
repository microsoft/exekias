################################################################
# WARNING: the script deletes ALL resources in a resource group
################################################################
if ($args.Length -lt 2) {
    Write-Host "Usage: ./cleanup_resource_group.ps1 <subscription> <resourceGroup>"
    exit 1
}
$subscription = $args[0]
$resourceGroup = $args[1]

Write-Host "[$(Get-Date)] Deleting ALL resources in resource group $resourceGroup in $subscription..."

Set-AzContext -Subscription $subscription | Out-Null

function Remove-ResourceUntilAbsent($resourceId){
    Remove-AzResource -ResourceId $resourceId -Force
    while (Get-AzResource -ResourceId $resourceId -ErrorAction SilentlyContinue){
        Write-Information "Retry: $resourceId"
        Start-Sleep -Seconds 10
        Remove-AzResource -ResourceId $resourceId -Force
    }
    $resourceId
}

# Delete Function Apps first to unblock App Service Plans
$apps = Get-AzWebApp -ResourceGroupName $resourceGroup
if ($apps) {
    Write-Host "[$(Get-Date)] Deleting web apps $($apps | Select-Object -ExpandProperty Name | Join-String -Separator ', ')..."
    $jobs = $apps | ForEach-Object {Start-Job {Remove-ResourceUntilAbsent $_.ResourceId}}
    Receive-Job $jobs -AutoRemoveJob -Wait
}

# Delete other resources
$resources = Get-AzResource -ResourceGroupName $resourceGroup
if ($resources) {
    Write-Host "[$(Get-Date)] Deleting $($resources.Length) other resources..."
    $jobs = $resources | ForEach-Object {Start-Job {Remove-ResourceUntilAbsent $_.ResourceId}}
    Receive-Job $jobs -AutoRemoveJob -Wait
}

# Wait until ARM reports the resource group is empty
$timeout = (Get-Date) + (New-TimeSpan -Minutes 10)
while (((Get-Date) -lt $timeout) -and (Get-AzResource -ResourceGroupName $resourceGroup)) {
    Start-Sleep -Seconds 1
}
$resources_left = Get-AzResource -ResourceGroupName $resourceGroup
if ($resources_left) {
    $resources_left
    Write-Error "[$(Get-Date)] Resource group is not empty"
    exit 1
}

Write-Host "[$(Get-Date)] Resource group $resourceGroup is now empty."
