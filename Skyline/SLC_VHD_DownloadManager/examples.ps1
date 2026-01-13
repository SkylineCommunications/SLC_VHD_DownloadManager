function Initialize-Module {
    # Resolve script directory for both VSCode and external PowerShell execution
    $ScriptDirectory = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    if (-not $ScriptDirectory) { $ScriptDirectory = Get-Location }
    
    # Import ThreadJob first (required dependency)
    Write-Host "Loading ThreadJob module..." -ForegroundColor Gray
    try {
        if (-not (Get-Module ThreadJob -ErrorAction SilentlyContinue)) {
            Import-Module ThreadJob -ErrorAction Stop
        }
        Write-Host "ThreadJob module loaded successfully" -ForegroundColor Green
    } catch {
        Write-Host "ERROR: ThreadJob module not found. Installing..." -ForegroundColor Yellow
        try {
            Install-Module -Name ThreadJob -Force -Scope CurrentUser -ErrorAction Stop
            Import-Module ThreadJob -ErrorAction Stop
            Write-Host "ThreadJob module installed and loaded successfully" -ForegroundColor Green
        } catch {
            Write-Host "ERROR: Failed to install/import ThreadJob - $_" -ForegroundColor Red
            exit 1
        }
    }
    
    # Import the main module
    $ModulePath = Join-Path $ScriptDirectory 'VHDDownloadManager.psm1'
    Write-Host "Loading module from: $ModulePath" -ForegroundColor Gray
    
    if (-not (Test-Path $ModulePath)) {
        Write-Host "ERROR: Module file not found at $ModulePath" -ForegroundColor Red
        exit 1
    }
    
    try {
        Import-Module -Force $ModulePath -ErrorAction Stop
        Write-Host "Module loaded successfully`n" -ForegroundColor Green
    } catch {
        Write-Host "ERROR: Failed to import module - $_" -ForegroundColor Red
        exit 1
    }
    
    return $ScriptDirectory
}
$ScriptDirectory = Initialize-Module
Clear-Host

# Examples for SLC VHD Download Manager Module

# Example 1: Download default VHD with verification and real-time progress
$defaultUrl = 'https://stgpublicdownloads.blob.core.windows.net/cnt-selfhosteddma-vmimages/10.5/mgdsk-selfhosted-dma-Images-1005000900.vhdx'
Start-VHDDownload -Url $defaultUrl -Threads 64 -Verify -ShowReport

# Example 2: Download with lower thread count if experiencing network issues
# Uncomment to try with fewer parallel threads
#$defaultUrl = 'https://stgpublicdownloads.blob.core.windows.net/cnt-selfhosteddma-vmimages/10.5/mgdsk-selfhosted-dma-Images-1005000900.vhdx'
#Start-VHDDownload -Url $defaultUrl -Threads 8 -Verify -ShowReport

# Cleanup Examples (uncomment to clean up after download completes)
#Stop-VHDDownloadJobs
#Remove-VHDSegmentFiles -Directory $ScriptDirectory
#Remove-VHDXFile -Path $Example1.OutputPath  # Remove the downloaded file