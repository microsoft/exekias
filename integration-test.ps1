Param(
    [Parameter(Mandatory, HelpMessage = "Azure resource group name.")][string]
    $resourceGroup,
    [Parameter(Mandatory, HelpMessage = "Azure storage account name.")][string]
    $storageAccount,
    [Parameter(Mandatory, HelpMessage = "A path to exekias executable.")][string]
    $exekias
)

if (-not (Test-Path $exekias)) {
    Write-Error "Executable $exekias not found."
    exit 1
}

$batch = Get-AzBatchAccountKey -ResourceGroupName $resourceGroup -Name "${storageAccount}8sync"  -WarningAction SilentlyContinue

Write-Host "[$(Get-Date)] Creating and uploading a sample Run..."
$sample_folder = "./sample"
$runid = "$(Get-Date -UFormat "%Y%m%d-%H%M%S")-integration-test"
$upload_relative_path = "$sample_folder/upload/$runid"
$download_relative_path = "$sample_folder/download"
if (Test-Path $sample_folder) {
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
    $ls = & $exekias data ls $runid
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
    $downloaded = Get-ChildItem -File -Path "$sample_folder/download/$runid" -Recurse
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
    $attempts = 5
    while ($attempts -gt -0) {
        $timeout = (Get-Date) + (New-TimeSpan -Seconds 120)
        while (((Get-Date) -lt $timeout) -and -not $batchtask) {
            Start-Sleep -Seconds 5
            $batchjob = Get-AzBatchTask -JobId exekias -BatchContext $batch -ErrorAction SilentlyContinue
            if ($batchjob) {
                $batchtask = Get-AzBatchTask -JobId exekias -BatchContext $batch | Where-Object { $_.Id -Like "$runid*" }
            }
        }
        if ($batchtask -or $attempts -eq 1) {
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
$runs = & $exekias runs query "run.id = '$' and run.run = '$runid'" --json | ConvertFrom-Json
if (($runs.Count -ne 1) -or ($runs[0].run -ne $runid) -or ($runs.params.TestKey -ne "test value")) {
    Write-Error "Query failed."
    exit 1
}

Write-Host "[$(Get-Date)] Check hiding/unhiding runs..."
& $exekias runs hide $runid
if (((& $exekias runs query) -contains $runid) -or -not ((& $exekias runs query --hidden) -contains $runid)) {
    Write-Error "Hiding failed."
    exit 1
}
& $exekias runs hide $runid --unhide
if (-not ((& $exekias runs query) -contains $runid) -or ((& $exekias runs query --hidden) -contains $runid)) {
    Write-Error "Unhiding failed."
    exit 1
}

Write-Host "[$(Get-Date)] Waiting for a job task to be completed..."
$timeout = (Get-Date) + (New-TimeSpan -Minutes 20)
while (((Get-Date) -lt $timeout) -and ($batchtaskstate -ne "Completed")) {
    Start-Sleep -Seconds 5
    $batchtaskstate = (Get-AzBatchTask -JobId exekias -BatchContext $batch | Where-Object { $_.Id -Like "$runid*"}).State
}
if ($batchtaskstate -ne "Completed") {
    Write-Error "Batch task didn't complete within 20 mins."
    exit 1
}
Write-Host "[$(Get-Date)] Checking batch task execution result:"
(Get-AzBatchTask -JobId exekias -BatchContext $batch | Where-Object { $_.Id -Like "$runid*"}).ExecutionInformation | Select-Object Result, FailureInformation
$runs = & $exekias runs show $runid | ConvertFrom-Json
if (-not ($runs.Variables.CSV -and ($runs.Variables.CSV[0] -eq "columnTitle"))) {
    Write-Error "Run metadata doesn't have updated information about data columns."
    exit 1
}
Write-Host "[$(Get-Date)] All done."