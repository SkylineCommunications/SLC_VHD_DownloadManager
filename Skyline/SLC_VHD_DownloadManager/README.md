# SLC VHD Download Manager

A robust PowerShell module for downloading large VHD files with multi-threaded parallel segment downloads. Designed for virtualization and cloud deployment scenarios, this utility ensures reliability through integrity verification and automatic retry logic.

## Features

- **Segmented Parallel Downloads:** Split large files into segments and download them concurrently using ThreadJob for maximum performance
- **Real-time Progress Monitoring:** Live progress bar showing download speed, ETA, and completion percentage
- **Integrity Verification:** Optional SHA256 checksum validation to ensure file integrity
- **Automatic Retry Logic:** Failed segments are retried up to 3 times automatically
- **Job Management:** Built-in functions to monitor, stop, and clean up download jobs
- **Segment File Management:** Utilities to remove temporary segment files and merge downloaded chunks

## Requirements

- **PowerShell 7+** (Windows, macOS, or Linux)
- **.NET 6+** (runtime requirement)
- **ThreadJob Module** (required for parallel job execution - auto-installed if missing)
- Network access to target file URLs

**Dependency Details:**
- ThreadJob module is required and will be automatically installed on first run if not present
- Requires internet access during initial setup for ThreadJob installation
- CurrentUser module scope installation does not require administrator privileges

## Installation

1. Clone or download this repository
2. Navigate to the module directory:
   ```powershell
   cd '.\Skyline\SLC_VHD_DownloadManager'
   ```
3. Run the examples script (it will auto-import the module and ThreadJob):
   ```powershell
   .\examples.ps1
   ```

## Usage

### Basic Download with Progress Monitoring

```powershell
# Load the module
Import-Module '.\VHDDownloadManager.psm1' -Force

# Download with default settings (16 threads)
$url = 'https://stgpublicdownloads.blob.core.windows.net/cnt-selfhosteddma-vmimages/10.5/mgdsk-selfhosted-dma-Images-1005000900.vhdx'
Start-VHDDownload -Url $url -ShowReport

# Download with custom thread count and verification
Start-VHDDownload -Url $url -Threads 64 -Verify -ShowReport

# Download to a specific location
Start-VHDDownload -Url $url -OutputFile "C:\VHD\custom-image.vhd" -Threads 32
```

### Available Functions

- **`Start-VHDDownload`** – Main download function with progress monitoring
  - `-Url` – Download URL (required)
  - `-OutputFile` – Output file path (optional, auto-named from URL)
  - `-Threads` – Number of parallel segments (default: 16)
  - `-Verify` – Enable SHA256 checksum verification
  - `-ShowReport` – Display a final download report

- **`Stop-VHDDownloadJobs`** – Stop all active download jobs
- **`Remove-VHDSegmentFiles`** – Clean up temporary segment files from a directory
- **`Remove-VHDXFile`** – Delete a downloaded VHD file

### Clean Up Temporary Files and Jobs

If a download is interrupted or fails, clean up with:

```powershell
# Stop all running download jobs
Stop-VHDDownloadJobs

# Remove temporary segment files
Remove-VHDSegmentFiles -Directory '.'

# Remove the downloaded file (if needed)
Remove-VHDXFile -Path '.\downloaded-file.vhdx'
```

## File Overview

- **`VHDDownloadManager.psm1`** – Main module containing all download, verification, and cleanup functions
- **`examples.ps1`** – Quick-start script demonstrating module usage with auto-setup
- **`README.md`** – This file
- **`RELEASE_NOTES.md`** – Version history and changelog

## Troubleshooting

### "Start-ThreadJob is not recognized"

The ThreadJob module is required but not installed. Run:

```powershell
Install-Module -Name ThreadJob -Force -Scope CurrentUser
```

The `examples.ps1` script will do this automatically.

### Download Stops or Fails

- **Network Issues:** Try reducing the thread count: `-Threads 8` or `-Threads 4`
- **Timeout Issues:** Large files may timeout; reduce threads or increase segment size
- **Cleanup:** Stop jobs and remove partial files: `Stop-VHDDownloadJobs; Remove-VHDSegmentFiles -Directory '.'`

### Verification Failed

If SHA256 verification fails, the file may be corrupted. Try downloading again or use `-Verify:$false` to skip verification (not recommended).

## Performance Tips

- **Thread Count:** More threads = faster but higher resource usage
  - Default: 16 threads (balanced)
  - Fast networks: 32-64 threads
  - Slow/unstable networks: 4-8 threads
- **File Size:** For files > 5GB, use 32+ threads for optimal speed
- **Network:** Parallel downloads work best on stable, high-bandwidth connections

## Release Notes

See [RELEASE_NOTES.md](RELEASE_NOTES.md) for version history and recent changes.

## License

Distributed under the relevant license specified by Skyline Communications.

