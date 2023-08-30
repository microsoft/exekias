#
# Canary test for Exekias services and command line utility.
#
# Parameters file "~/exekias-canary-test.json" must contain:
# - Subscription name, resource group name and strorage account name to use for deployment.
# - A path to exekias command line utilty produced by "dotnet publish" command.
#
# WARNING: All existing resource will be deleted from the resource group.
#
# The canary test performs:
# - Azure deployment of an empty storage.
# - Upload of a test run with a small CSV file.
# - Download a copy of the run and compare with the original.
# - Retrieval of metadata object.
# - Wait until postprocessing of the run completes and check appropriate changes to teh run metadata.
#
$paramsFile = "~/exekias-canary-test.json"
if (Test-Path $paramsFile){

    $params = Get-Content  | ConvertFrom-Json
    
    $subscription = $params.subscription
    $resourceGroup = $params.resourceGroup
    $storageAccount = $params.storageAccount
    $exekias = $params.exekiasPath
}
else {
    $subscription = $env:EXEKIAS_SUBSCRIPTION
    $resourceGroup =  $env:EXEKIAS_RESOURCEGROUP
    $storageAccount = $env:EXEKIAS_STORAGEACCOUNT
    $exekias = $env:EXEKIAS_BIN
}

if (-not (Test-Path $exekias)) {
    Write-Error "$exekias not found."
    exit 1
}
if (Test-Path ~/.exekias.json) {
    Remove-Item ~/.exekias.json -Force
}

Write-Host "[$(Get-Date)] Starting canary test."
.\cleanup_resource_group.ps1 $subscription $resourceGroup
if (-not $?) {
    exit $LASTEXITCODE
}

Write-Host "[$(Get-Date)] Start deployment of backend..."
$context = Set-AzContext -Subscription $subscription
& $exekias backend deploy --subscription $context.Subscription --resourcegroup $resourceGroup --storageaccount $storageAccount
if ($LASTEXITCODE -ne 0) {
    Write-Error "Deployment of backend failed, exit code: $LASTEXITCODE"
    exit 1
}

Write-Host "[$(Get-Date)] Creating config..."
& $exekias config create --subscription $context.Subscription --resourcegroup $resourceGroup --storageaccount $storageAccount
if ($LASTEXITCODE -ne 0) {
    Write-Error "Create config operation failed, exit code: $LASTEXITCODE"
    exit 1
}

Write-Host "[$(Get-Date)] Creating and uploading a sample Run..."
$sample_folder = "./sample"
$runid = "12345678-123456-samples"
$upload_relative_path = "$sample_folder/upload/$runid"
$download_relative_path = "$sample_folder/download"
if (Test-Path ./sample) {
    Remove-Item -Path $sample_folder -Recurse
}
$upload_folder = New-Item -Path "$upload_relative_path/sub folder" -ItemType Directory | Select-Object -ExpandProperty Parent

Set-Content -Path "$upload_folder/params.json" -Value '{"testKey": "test value"}'
Set-Content -Path "$upload_folder/data.csv" -Value @'
columnTitle
columnValue
'@
Set-Content -Path "$upload_folder/sub folder/params.json" -Value '{"testKey": "other value"}'
$uploaded = Get-ChildItem -File -Path $upload_folder -Recurse

try {
    & $exekias data upload $upload_relative_path
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Upload failed, exit code: $LASTEXITCODE"
        exit 1
    }

    Write-Host "[$(Get-Date)] Check data ls command output..."
    $ls = & $exekias data ls 12345678-123456-samples
    if ($LASTEXITCODE -ne 0) {
        Write-Error "ls failed, exit code: $LASTEXITCODE"
        exit 1
    }
    if ($ls.length -ne $uploaded.length) {
        Write-Error "ls failed."
        exit 1
    }

    Write-Host "[$(Get-Date)] Downloading and comparing the Run..."
    New-Item -Path $download_relative_path -ItemType Directory | Out-Null
    & $exekias data download $runid $download_relative_path
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Download failed, exit code: $LASTEXITCODE"
        exit 1
    }
    $downloaded = Get-ChildItem -File -Path ./sample/download/12345678-123456-samples -Recurse
    if ($downloaded.length -ne $uploaded.length) {
        Write-Error "Download failed"
        exit 1
    }
    foreach ($file in $uploaded) {
        $content = Get-Content $file -Raw
        $file_relative_path = [io.path]::GetRelativePath($upload_folder.FullName, $file.FullName)
        $original = Get-Content "$download_relative_path/$runid/$file_relative_path" -Raw
        if ($content -ne $original) {
            Write-Error "The content of the downloaded file $($file.Name) is not the same as the original one."
            exit 1
        }
        $uploaded_time = (Get-ItemPropertyValue $file -Name LastWriteTime)
        $downloaded_time = (Get-ItemPropertyValue "$download_relative_path/$runid/$file_relative_path" -Name LastWriteTime)
        $time_difference = [Math]::Abs(($uploaded_time - $downloaded_time).TotalMilliseconds)
        if ($time_difference -ge 1) {
            Write-Error "The date/time of the downloaded file $($file_relative_path) is not the same as the original one, $downloaded_time != $uploaded_time, the difference is $time_difference ms."
            exit 1
        }
    }
Write-Host "[$(Get-Date)] Waiting for a job to be created..."
$batch = Get-AzBatchAccountKey -ResourceGroupName $resourceGroup -Name "${storageAccount}8sync"
    $attempts = 5
    while ($attempts -gt -0) {
$timeout = (Get-Date) + (New-TimeSpan -Seconds 120)
while (((Get-Date) -lt $timeout) -and -not $batchjob) {
    Start-Sleep -Seconds 5
    $batchjob = Get-AzBatchJob -BatchContext $batch
}
        if ($batchjob -or $attempts -eq 1) {
            $attempts = 0
        }
        else {
            # first event sometimes lost due to transient processes after deployment.
            # update file times and upload them again.
            $attempts -= 1
            Write-Host "[$(Get-Date)] $attempts more attempt(s) to raise blob events..."
            $uploaded | ForEach-Object { $_.LastWriteTime = (Get-Date) }
            & $exekias data upload $upload_relative_path
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Upload failed, exit code: $LASTEXITCODE"
                exit 1
            }
        }
    }
}
finally {
    Remove-Item -Path $sample_folder -Recurse
} 

Write-Host "[$(Get-Date)] Query for runs..."
$runs = & $exekias query --json | ConvertFrom-Json
if (($runs.Count -ne 1) -or ($runs[0].run -ne "12345678-123456-samples") -or ($runs.params.TestKey -ne "test value")) {
    Write-Error "Query failed."
    exit 1
}

Write-Host "[$(Get-Date)] Waiting for a job task to be completed..."
$timeout = (Get-Date) + (New-TimeSpan -Minutes 20)
while (((Get-Date) -lt $timeout) -and ($batchtaskstate -ne "Completed")) {
    Start-Sleep -Seconds 5
    $batchtaskstate = (Get-AzBatchTask -JobId exekias -BatchContext $batch).State
}
if ($batchtaskstate -ne "Completed") {
    Write-Error "Batch task didn't complete within 20 mins."
    exit 1
}
Write-Host "[$(Get-Date)] Checking batch task execution result:"
(Get-AzBatchTask -JobId exekias -BatchContext $batch).ExecutionInformation | Select-Object Result, FailureInformation
$runs = & $exekias query --json | ConvertFrom-Json
if (-not ($runs.Variables.CSV -and ($runs.Variables.CSV[0] -eq "columnTitle"))) {
    Write-Error "Run metadata doesn't have updated information about data columns."
    exit 1
}
Write-Host "[$(Get-Date)] All done."