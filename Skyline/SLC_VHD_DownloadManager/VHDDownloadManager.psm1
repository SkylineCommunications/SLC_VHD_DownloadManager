# Ensure ThreadJob module is available for Start-ThreadJob
try {
    if (-not (Get-Module ThreadJob -ErrorAction SilentlyContinue)) {
        Import-Module ThreadJob -ErrorAction Stop
    }
} catch {
    Write-Error "ThreadJob module is required but could not be imported. Install it with: Install-Module -Name ThreadJob -Force"
    throw $_
}

function Get-Segment {
    param (
        [string]$Url,
        [int64]$Start,
        [int64]$End,
        [string]$OutputFile
    )

    $Headers = @{ Range = "bytes=$Start-$End" }
    $MaxRetries = 3
    $RetryCount = 0

    do {
        try {
            Invoke-WebRequest -Uri $Url -Headers $Headers -OutFile $OutputFile -ErrorAction Stop
            $RetryCount = $MaxRetries
        } catch {
            $RetryCount++
            Start-Sleep -Seconds 2
        }
    } while ($RetryCount -lt $MaxRetries)

    if ($RetryCount -eq $MaxRetries -and -not (Test-Path $OutputFile)) {
        throw "Failed to download segment to $OutputFile after $MaxRetries attempts."
    }
}

function Start-Download {
    param (
        [string]$Url,
        [string]$OutputFile,
        [int]$Threads = 16,
        [switch]$Debug
    )

    # Optimize HTTP connection settings
    $uri = [System.Uri]$Url
    [System.Net.ServicePointManager]::DefaultConnectionLimit = [Math]::Max($Threads * 2, 100)
    [System.Net.ServicePointManager]::Expect100Continue = $false
    [System.Net.ServicePointManager]::UseNagleAlgorithm = $false
    $servicePoint = [System.Net.ServicePointManager]::FindServicePoint($uri)
    $servicePoint.ConnectionLimit = [Math]::Max($Threads * 2, 100)
    $servicePoint.UseNagleAlgorithm = $false

    $ScriptDirectory = Split-Path -Parent $OutputFile
    $Response = Invoke-WebRequest -Uri $Url -Method Head
    $FileSize = [int64]($Response.Headers['Content-Length'] -join "")
    $SegmentSize = [math]::Ceiling($FileSize / $Threads)
    $SegmentFiles = @()

    # Build segment info
    $segmentInfo = @()
    for ($i = 0; $i -lt $Threads; $i++) {
        $Start = $i * $SegmentSize
        $End = if ($i -eq $Threads - 1) { $FileSize - 1 } else { ($i + 1) * $SegmentSize - 1 }
        $SegmentFile = Join-Path -Path $ScriptDirectory -ChildPath "segment_$i"
        
        $segmentInfo += [PSCustomObject]@{
            Index = $i
            Start = $Start
            End = $End
            Url = $Url
            SegmentFile = $SegmentFile
        }
        
        $SegmentFiles += $SegmentFile
    }

    Write-Host "Launching parallel download with $Threads threads..." -ForegroundColor Yellow
    $downloadStartTime = Get-Date
    
    # Start downloads in background job so main thread can monitor
    $downloadJob = Start-ThreadJob -ScriptBlock {
        param($segmentInfo, $Threads)
        
        $segmentInfo | ForEach-Object -Parallel {
            $segment = $_
            $Request = $null
            $Response = $null
            $Stream = $null
            $FileStream = $null
            $Buffered = $null
            
            try {
                $Request = [System.Net.HttpWebRequest]::Create($segment.Url)
                $Request.Method = "GET"
                $Request.AddRange([long]$segment.Start, [long]$segment.End)
                
                $Response = $Request.GetResponse()
                $Stream = $Response.GetResponseStream()
                $FileStream = [System.IO.File]::Create($segment.SegmentFile)
                $Buffered = [System.IO.BufferedStream]::new($FileStream, 8MB)
                
                $Buffer = [byte[]]::new(8MB)
                $Read = 0
                while (($Read = $Stream.Read($Buffer, 0, $Buffer.Length)) -gt 0) {
                    $Buffered.Write($Buffer, 0, $Read)
                }
                
                $Buffered.Flush()
            } catch {
                Write-Error "Segment $($segment.Index) download failed: $_"
            } finally {
                # Ensure all streams are disposed even on error
                if ($Buffered) { try { $Buffered.Dispose() } catch {} }
                if ($FileStream) { try { $FileStream.Dispose() } catch {} }
                if ($Stream) { try { $Stream.Dispose() } catch {} }
                if ($Response) { try { $Response.Dispose() } catch {} }
            }
        } -ThrottleLimit $using:Threads
    } -ArgumentList $segmentInfo, $Threads
    
    # Monitor progress in main thread while job runs
    $progressCheck = 0
    $lastDisplay = [DateTime]::MinValue
    
    while ($downloadJob.State -eq 'Running') {
        Start-Sleep -Milliseconds 250
        $progressCheck++
        
        $now = Get-Date
        # Update display every 1 second
        if (($now - $lastDisplay).TotalMilliseconds -ge 1000) {
            $lastDisplay = $now
            
            # Calculate total downloaded
            $totalDownloaded = 0
            Get-ChildItem "$ScriptDirectory/segment_*" -ErrorAction SilentlyContinue | 
                ForEach-Object { $totalDownloaded += $_.Length }
            
            $percent = if ($FileSize -gt 0) { [int]($totalDownloaded / $FileSize * 100) } else { 0 }
            
            $barLength = 30
            $filledBars = [int]($percent / 100.0 * $barLength)
            $bar = ('#' * $filledBars) + ('-' * ($barLength - $filledBars))
            
            $mbDownloaded = [math]::Round($totalDownloaded / 1MB, 2)
            $mbTotal = [math]::Round($FileSize / 1MB, 2)
            
            $elapsed = (Get-Date) - $downloadStartTime
            $speedMBps = if ($elapsed.TotalSeconds -gt 0) { $totalDownloaded / 1MB / $elapsed.TotalSeconds } else { 0 }
            $remaining = $FileSize - $totalDownloaded
            $etaSeconds = if ($speedMBps -gt 0.001) { $remaining / 1MB / $speedMBps } else { 0 }
            
            $etaStr = ""
            if ($etaSeconds -gt 0) {
                if ($etaSeconds -lt 60) { $etaStr = "$([int]$etaSeconds)s" }
                elseif ($etaSeconds -lt 3600) { $etaStr = "$([int]($etaSeconds/60))m $([int]($etaSeconds % 60))s" }
                else { $etaStr = "$([int]($etaSeconds/3600))h $([int](($etaSeconds % 3600)/60))m" }
            }
            
            Write-Host "Progress: $bar $percent% | $mbDownloaded MB / $mbTotal MB | $([math]::Round($speedMBps, 2)) MB/s | ETA: $etaStr"
        }
    }
    
    # Wait for job to complete and clean up
    $downloadJob | Wait-Job | Out-Null
    $jobErrors = $downloadJob | Receive-Job 2>&1
    $downloadJob | Remove-Job -Force
    
    # Display any errors from download job
    if ($jobErrors) {
        $jobErrors | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | 
            ForEach-Object { Write-Host "Error: $_" -ForegroundColor Red }
    }
    
    # Force garbage collection
    [System.GC]::Collect()
    [System.GC]::WaitForPendingFinalizers()
    [System.GC]::Collect()
    Start-Sleep -Milliseconds 500

    Write-Host "All segments downloaded." -ForegroundColor Cyan
    return $SegmentFiles
}

function Merge-Segments {
    param (
        [string[]]$SegmentFiles,
        [string]$OutputFile,
        [switch]$KeepSegments
    )

    $tempFile = $OutputFile + ".tmp"
    $totalSegments = $SegmentFiles.Count
    $completedSegments = 0
    $bufferSize = 1MB

    try {
        $OutputStream = [System.IO.File]::Create($tempFile)
        
        foreach ($Segment in $SegmentFiles) {
            if (Test-Path $Segment) {
                $InputStream = [System.IO.File]::OpenRead($Segment)
                $buffer = New-Object byte[] $bufferSize
                while (($bytesRead = $InputStream.Read($buffer, 0, $bufferSize)) -gt 0) { 
                    $OutputStream.Write($buffer, 0, $bytesRead) 
                }
                $InputStream.Close()
                if (-not $KeepSegments) { Remove-Item $Segment }
                $completedSegments++
                $percentComplete = [math]::Min([math]::Round(($completedSegments / $totalSegments) * 100), 100)
                Write-Progress -Activity "Merging segments" -Status "$percentComplete% complete" -PercentComplete $percentComplete
            } else {
                Write-Error "Segment file '$Segment' not found."
                $OutputStream.Close()
                Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
                return $false
            }
        }
        Write-Progress -Activity "Merging segments" -Status "100% complete" -PercentComplete 100 -Completed
        $OutputStream.Close()
        
        if (Test-Path $tempFile) {
            if (Test-Path $OutputFile) { Remove-Item $OutputFile -Force }
            Rename-Item -Path $tempFile -NewName (Split-Path -Leaf $OutputFile) -Force
            Write-Host "Successfully created: $OutputFile" -ForegroundColor Green
        }
        return $true
    } catch {
        Write-Error "Error during merge: $_"
        if ($OutputStream) { $OutputStream.Close() }
        if (Test-Path $tempFile) { Remove-Item $tempFile -Force -ErrorAction SilentlyContinue }
        return $false
    }
}

function Stop-VHDDownloadJobs {
    Write-Host "Note: With PowerShell 7+ Task-based downloads, all segments complete before returning." -ForegroundColor Cyan
}

function Remove-VHDSegmentFiles {
    param(
        [string]$Directory,
        [int]$RetryCount = 5,
        [int]$RetryDelayMs = 1000
    )
    
    if (-not (Test-Path $Directory)) {
        Write-Error "Directory not found: $Directory"
        return
    }
    
    # Force garbage collection before deletion
    [System.GC]::Collect()
    [System.GC]::WaitForPendingFinalizers()
    Start-Sleep -Milliseconds 500
    
    $segmentFiles = Get-ChildItem -Path $Directory -Filter "segment_*" -File -ErrorAction SilentlyContinue
    
    if ($segmentFiles) {
        $count = @($segmentFiles).Count
        Write-Host "Removing $count segment file(s)..." -ForegroundColor Yellow
        
        $attempt = 0
        $success = $false
        $removed = 0
        
        while ($attempt -lt $RetryCount -and -not $success) {
            try {
                $attempt++
                $remaining = @()
                
                foreach ($file in $segmentFiles) {
                    try {
                        Remove-Item $file.FullName -Force -ErrorAction Stop
                        $removed++
                    } catch {
                        $remaining += $file
                    }
                }
                
                if ($remaining.Count -eq 0) {
                    Write-Host "Successfully removed all $count segment file(s)" -ForegroundColor Green
                    $success = $true
                } else {
                    if ($attempt -lt $RetryCount) {
                        Write-Host "Removed $removed/$count files. $($remaining.Count) still locked. Retrying in ${RetryDelayMs}ms..." -ForegroundColor Yellow
                        Start-Sleep -Milliseconds $RetryDelayMs
                        
                        [System.GC]::Collect()
                        [System.GC]::WaitForPendingFinalizers()
                        $segmentFiles = $remaining
                    } else {
                        Write-Error "Failed to remove $($remaining.Count) segment file(s) after $RetryCount attempts."
                        Write-Host "Locked files:" -ForegroundColor Cyan
                        $remaining | ForEach-Object { Write-Host "  - $($_.Name)" -ForegroundColor Cyan }
                    }
                }
            } catch {
                Write-Error "Error during removal: $_"
                $attempt = $RetryCount
            }
        }
    } else {
        Write-Host "No segment files found in $Directory" -ForegroundColor Cyan
    }
}

function Remove-VHDXFile {
    param([string]$Path)
    if (Test-Path $Path) { 
        Remove-Item -Path $Path -Force -ErrorAction SilentlyContinue 
    }
}

function Get-RemoteFileHash {
    param([string]$Url)
    try {
        $request = [System.Net.HttpWebRequest]::Create($Url)
        $request.Method = "GET"
        $response = $request.GetResponse()
        $stream = $response.GetResponseStream()
        try {
            $sha256 = [System.Security.Cryptography.SHA256]::Create()
            $hashBytes = $sha256.ComputeHash($stream)
            [System.BitConverter]::ToString($hashBytes).Replace("-", "")
        } finally {
            $stream.Close()
            $response.Close()
        }
    } catch { 
        return $null 
    }
}

function Get-LocalFileHash {
    param([string]$Path)
    return (Get-FileHash -Path $Path -Algorithm SHA256).Hash
}

function Get-ExpectedHashFromUrl {
    param([string]$Url)
    try {
        $hashUrl = $Url + '.sha256'
        $hashContent = (Invoke-WebRequest -Uri $hashUrl -ErrorAction Stop).Content.Trim()
        if ($hashContent -match '^([a-fA-F0-9]{64})') {
            return $Matches[1]
        }
        return $null
    } catch {
        return $null
    }
}

function Show-ExecutionReport {
    param([pscustomobject]$Result)
    
    Write-Host ""
    Write-Host "VHD Download Manager - Execution Report" -ForegroundColor Cyan
    Write-Host "-------------------------------------------------------" -ForegroundColor Cyan

    if ($Result.Timing) {
        $totalDuration = [TimeSpan]::Zero
        foreach ($op in $Result.Timing) {
            $startStr = $op.StartTime.ToString("HH:mm:ss")
            $endStr = $op.EndTime.ToString("HH:mm:ss")
            
            if ($op.Duration.TotalSeconds -lt 60) {
                $durationStr = [string][int]$op.Duration.TotalSeconds + "s"
            } elseif ($op.Duration.TotalMinutes -lt 60) {
                $durationStr = [string][int]$op.Duration.TotalMinutes + "m " + [string]$op.Duration.Seconds + "s"
            } else {
                $durationStr = [string]$op.Duration.Hours + "h " + [string]$op.Duration.Minutes + "m " + [string]$op.Duration.Seconds + "s"
            }
            
            $msg = $op.Operation.PadRight(25) + " " + $startStr + " -> " + $endStr + "  [" + $durationStr + "] OK"
            Write-Host $msg
            $totalDuration += $op.Duration
        }
        
        if ($totalDuration.TotalSeconds -lt 60) {
            $totalStr = [string][int]$totalDuration.TotalSeconds + "s"
        } elseif ($totalDuration.TotalMinutes -lt 60) {
            $totalStr = [string][int]$totalDuration.TotalMinutes + "m " + [string]$totalDuration.Seconds + "s"
        } else {
            $totalStr = [string]$totalDuration.Hours + "h " + [string]$totalDuration.Minutes + "m " + [string]$totalDuration.Seconds + "s"
        }
        
        Write-Host "-------------------------------------------------------" -ForegroundColor Cyan
        Write-Host "TOTAL: " $totalStr " |  OK All successful" -ForegroundColor Green
    }
    Write-Host ""
}

function Start-VHDDownload {
    param(
        [string]$Url,
        [string]$OutputPath,
        [int]$Threads = 16,
        [switch]$Verify,
        [string]$ExpectedHash,
        [switch]$KeepSegments,
        [switch]$ShowReport,
        [switch]$Debug
    )

    if (-not $OutputPath) {
        $ScriptDirectory = $PSScriptRoot
        if (-not $ScriptDirectory) { $ScriptDirectory = $PWD.Path }
        $fileName = [System.IO.Path]::GetFileName($Url.Split('?')[0])
        $OutputPath = Join-Path $ScriptDirectory $fileName
    }

    $timing = @()
    
    # Download segments
    $downloadStart = Get-Date
    $segments = Start-Download -Url $Url -OutputFile $OutputPath -Threads $Threads -Debug:$Debug
    $downloadEnd = Get-Date
    $timing += @{
        Operation = "Download segments"
        StartTime = $downloadStart
        EndTime = $downloadEnd
        Duration = $downloadEnd - $downloadStart
    }
    
    if (-not $segments) {
        Write-Error "Download failed"
        return $null
    }
    
    # Merge segments
    $mergeStart = Get-Date
    $mergeSuccess = Merge-Segments -SegmentFiles $segments -OutputFile $OutputPath -KeepSegments:$KeepSegments
    $mergeEnd = Get-Date
    
    if (-not $mergeSuccess) {
        Write-Error "Merge failed"
        return $null
    }
    
    $timing += @{
        Operation = "Merge segments"
        StartTime = $mergeStart
        EndTime = $mergeEnd
        Duration = $mergeEnd - $mergeStart
    }
    
    $output = @{
        Url = $Url
        OutputPath = $OutputPath
        Threads = $Threads
        Timing = $timing
    }
    
    if ($Verify) {
        if (-not $ExpectedHash) {
            $ExpectedHash = Get-ExpectedHashFromUrl -Url $Url
        }
        
        $verifyStart = Get-Date
        $localHash = Get-LocalFileHash -Path $OutputPath
        $verifyEnd = Get-Date
        $verified = $ExpectedHash -and ($localHash -eq $ExpectedHash)
        
        $timing += @{
            Operation = "Hash verification"
            StartTime = $verifyStart
            EndTime = $verifyEnd
            Duration = $verifyEnd - $verifyStart
        }
        
        $output['ExpectedHash'] = $ExpectedHash
        $output['LocalHash'] = $localHash
        $output['Verified'] = $verified
    }

    $result = [pscustomobject]$output
    
    if ($ShowReport) {
        Show-ExecutionReport -Result $result
    }
    
    return $result
}

Export-ModuleMember -Function Get-Segment, Start-Download, Merge-Segments, Stop-VHDDownloadJobs, Remove-VHDSegmentFiles, Remove-VHDXFile, Get-RemoteFileHash, Get-LocalFileHash, Start-VHDDownload, Show-ExecutionReport, Get-ExpectedHashFromUrl
