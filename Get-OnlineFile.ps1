$Url = "https://stgpublicdownloads.blob.core.windows.net/cnt-selfhosteddma-vmimages/10.5/mgdsk-selfhosted-dma-Images-1005000900.vhdx"
$OutputFile = Join-Path -Path $ScriptDirectory -ChildPath "disk-selfhosteddma.vhdx"
# Download the SHA256 file
$shaResponse = Invoke-WebRequest -Uri ($Url + ".sha256")

# Extract the hash (first token before the filename)
$urlSHA256 = $shaResponse.Content.Split(" ")[0]


function Get-Segment {
    param (
        [string]$Url,
        [int64]$Start,
        [int64]$End,
        [string]$OutputFile
    )

    $Headers = @{
        Range = "bytes=$Start-$End"
    }
    $SegmentSize = $End - $Start + 1
    $MaxRetries = 3  # Maximum number of retries
    $RetryCount = 0

    do {
        try {
            # Attempt to download the segment
            Invoke-WebRequest -Uri $Url -Headers $Headers -OutFile $OutputFile -ErrorAction Stop
            Write-Output "Segment downloaded to $OutputFile"
            $RetryCount = $MaxRetries  # Exit the retry loop on success
        } catch {
            $RetryCount++
            Write-Warning "Failed to download segment to $OutputFile. Retry $RetryCount of $MaxRetries."
            Start-Sleep -Seconds 2  # Wait before retrying
        }
    } while ($RetryCount -lt $MaxRetries)

    if ($RetryCount -eq $MaxRetries) {
        Write-Error "Failed to download segment to $OutputFile after $MaxRetries attempts."
    }
}

function Start-Download {
    param (
        [string]$Url,
        [string]$OutputFile,
        [int]$Threads = 16
    )

    # Get the script's directory
    $ScriptDirectory = $PSScriptRoot
    $OutputFile = Join-Path -Path $ScriptDirectory -ChildPath $OutputFile

    $Response = Invoke-WebRequest -Uri $Url -Method Head
    $FileSize = [int64]($Response.Headers['Content-Length'] -join "")
    $SegmentSize = [math]::Ceiling($FileSize / $Threads)
    $Jobs = @()
    $SegmentFiles = @()

    for ($i = 0; $i -lt $Threads; $i++) {
        $Start = $i * $SegmentSize
        $End = if ($i -eq $Threads - 1) { $FileSize - 1 } else { ($i + 1) * $SegmentSize - 1 }
        $SegmentFile = Join-Path -Path $ScriptDirectory -ChildPath "segment_$i"
        $SegmentFiles += $SegmentFile
        $Jobs += Start-Job -ScriptBlock {
            param ($Url, $Start, $End, $SegmentFile)
            Invoke-WebRequest -Uri $Url -Headers @{ Range = "bytes=$Start-$End" } -OutFile $SegmentFile
        } -ArgumentList $Url, $Start, $End, $SegmentFile
    }

    # Track progress of segment downloads
    while ($Jobs.State -contains 'Running') {
        $totalDownloaded = 0
        foreach ($SegmentFile in $SegmentFiles) {
            if (Test-Path $SegmentFile) {
                $totalDownloaded += (Get-Item $SegmentFile).Length
            }
        }
        $percentComplete = [math]::Round(($totalDownloaded / $FileSize) * 100)
        Write-Progress -Activity "Downloading segments" -Status "$percentComplete% complete" -PercentComplete $percentComplete
        Start-Sleep -Milliseconds 500
    }

    # Receive and clean up completed jobs
    $Jobs | ForEach-Object {
        $_ | Receive-Job
        Remove-Job -Job $_
    }

    # Return the list of segment files
    return $SegmentFiles
}

function Merge-Segments {
    param (
        [string[]]$SegmentFiles,
        [string]$OutputFile
    )

    $OutputStream = [System.IO.File]::Create($OutputFile)
    $totalSegments = $SegmentFiles.Count
    $completedSegments = 0
    $bufferSize = 4MB

    foreach ($Segment in $SegmentFiles) {
        if (Test-Path $Segment) {
            $InputStream = [System.IO.File]::OpenRead($Segment)
            $buffer = New-Object byte[] $bufferSize
            while (($bytesRead = $InputStream.Read($buffer, 0, $bufferSize)) -gt 0) {
                $OutputStream.Write($buffer, 0, $bytesRead)
            }
            $InputStream.Close()
            Remove-Item $Segment
            $completedSegments++
            $percentComplete = [math]::Round(($completedSegments / $totalSegments) * 100)
            Write-Progress -Activity "Merging segments" -Status "$percentComplete% complete" -PercentComplete $percentComplete
        } else {
            Write-Error "Segment file '$Segment' not found."
        }
    }
    $OutputStream.Close()
}

# Run the downloader and merge the segments
Clear-Host

# Log the start time
$startTime = Get-Date
Write-Host "Download started at: $startTime"

# Start the download
$SegmentFiles = Start-Download -Url $Url -OutputFile $OutputFile -Threads 64

# Merge the segments
Write-Host "Starting merge process..." -ForegroundColor Yellow
Merge-Segments -SegmentFiles $SegmentFiles -OutputFile $OutputFile
Write-Host "Merge process completed." -ForegroundColor Yellow

# Log the end time
$endTime = Get-Date
Write-Host "Download completed at: $endTime"

# Calculate and display the duration
$duration = $endTime - $startTime
Write-Host "Total duration: $($duration.Hours) hours, $($duration.Minutes) minutes, $($duration.Seconds) seconds"

# Calculate the checksum of the downloaded file
Write-Host "Starting checksum calculation..." -ForegroundColor Yellow
$Hash = Get-FileHash -Path $OutputFile -Algorithm SHA256
Write-Host "Checksum calculation completed." -ForegroundColor Yellow

# Compare the checksums
if ($Hash.Hash -eq $urlSHA256) {
    Write-Host "Checksum validation passed. The file is valid." -ForegroundColor Green
} else {
    Write-Host "Checksum validation failed. The file may be corrupted." -ForegroundColor Red
    Write-Host "Expected: $urlSHA256"
    Write-Host "Actual:   $($Hash.Hash)"
}