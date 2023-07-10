#
# Canary test for Exekias.DataImport
# ----------------------------------
#
# Prerequisites:
# - Azure PowerShell, see https://learn.microsoft.com/en-gb/powershell/azure/install-azps-windows
# - Azurite is installed and running, see https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite
# - CosmosDB emulator is installed and running, see https://learn.microsoft.com/en-us/azure/cosmos-db/local-emulator
# - Exekias.DataImport project has been built in Debug configuration
#
if ($args.Length -gt 0) {
    $dataimport = $args[0]
} else {
    $dataimport = "bin\Debug\net6.0\Exekias.DataImport.exe"
}
if (-not (Test-Path $dataimport)){ throw "DataImport not found at $dataimport"}
# Check Azurite
Import-Module Az.Storage
try
{
    $Response = Invoke-WebRequest -Uri http://127.0.0.1:10000/devstoreaccount1 -TimeoutSec 1
    # This will only execute if the Invoke-WebRequest is successful.
    $StatusCode = $Response.StatusCode
} catch {
    $StatusCode = $_.Exception.Response.StatusCode.value__
}
if ($StatusCode -ne 400) {throw "Azurite is not running"}

# Create a blob to Import

$azurite = New-AzStorageContext -Local
if (-not (Get-AzStorageContainer -Prefix "ppp" -Context $azurite)) {New-AzStorageContainer "ppp" -Context $azurite | Out-Null}
if (-not (Get-AzStorageBlob -Prefix "b/a.csv" "ppp" -Context $azurite)) {
Set-Content "a.csv" @"
header
3.1415
"@
Set-AzStorageBlobContent "a.csv" "ppp" -Blob "b/a.csv" -Context $azurite  | Out-Null
Remove-Item "a.csv"
}
# well-known emulator connection string
$cosmos = [System.Text.Encoding]::ASCII.GetString([System.Convert]::FromBase64String("QWNjb3VudEVuZHBvaW50PWh0dHBzOi8vbG9jYWxob3N0OjgwODEvO0FjY291bnRLZXk9QzJ5NnlEamY1L1Irb2IwTjhBN0NndjMwVlJESklXRUhMTSs0UURVNURFMm5ROW5EdVZUcW9iRDRiOG1HR3lQTWJJWm5xeU1zRWNhR1F5NjdYSXcvSnc9PQ=="))

$env:RunStore__ConnectionString = "UseDevelopmentStorage=true"
$env:RunStore__BlobContainerName = "ppp"
$env:RunStore__MetadataFilePattern = "^(?<runId>[^/]*)$"
$env:ImportStore__ConnectionString = "UseDevelopmentStorage=true"
$env:ImportStore__BlobContainerName = "qqq"
$env:ExekiasCosmos__ConnectionString = $cosmos
$env:ExekiasCosmos__ContainerName = "rrr"

# Create CosmosDB run object

Invoke-Expression "$dataimport b -canary:write"

# Perform import

Invoke-Expression "$dataimport b b/a.csv"

# Check imported blob

if ((Get-AzStorageBlob "b/a.csv" "qqq" -Context $azurite).Length -le 0) {throw "Imported blob has no content!"}

# Check variable metadata

$out = Invoke-Expression "$dataimport b -canary:read"
if ((ConvertFrom-Json $out[-1]).variables.CSV -cne "header") {throw "Metadata do not match!"}

Write-Output "All done!"