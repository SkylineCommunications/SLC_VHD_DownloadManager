# SLC VHD Download Manager

A PowerShell-based tool for robust, segmented downloading of large VHD files, often used in virtualization or cloud deployment scenarios. This utility leverages PowerShell jobs to speed up downloads by splitting the target file into segments and downloading them in parallel. It also provides a cleanup facility to stop running jobs and remove temporary files.

## Features

- **Segmented Download:** The main script (`Get-OnlineFile.ps1`) downloads large files in parallel chunks for reliability and speed, checks integrity with SHA256, and supports retry logic for failed downloads.
- **Cleanup Utility:** The `Clear-Processes.ps1` script terminates all running PowerShell jobs and removes temporary files generated during segmented downloads.

## Usage

### 1. Download a VHD File

Use `Get-OnlineFile.ps1` to download a VHD file with enhanced performance:

```powershell
# Example usage (customize parameters inside the script as needed)
.\Get-OnlineFile.ps1
```

- The script fetches a hardcoded URL for the VHD file and its SHA256 hash. You can modify the `$Url` variable at the top of the script to point to a different file.
- It splits the file into segments (default 16 threads), downloads each segment in a background job, then verifies the file hash.

### 2. Clean Up Temporary Files and Jobs

If an interrupted or failed download leaves behind temporary files or lingering jobs, run:

```powershell
.\Clear-Processes.ps1
```

- This script stops all running PowerShell jobs in the current session.
- It deletes all temporary segment files (files prefixed with `segment_`) from the script’s directory.

## File Overview

- [`Get-OnlineFile.ps1`](https://github.com/SkylineCommunications/SLC_VHD_DownloadManager/blob/main/Get-OnlineFile.ps1) – Handles segmented download of large files, integrity check, and multi-thread job management.
- [`Clear-Processes.ps1`](https://github.com/SkylineCommunications/SLC_VHD_DownloadManager/blob/main/Clear-Processes.ps1) – Stops jobs and deletes leftover segment files after a download.

## Requirements

- PowerShell 5.1 or later (recommended for job management and web requests)
- Network access to target file URLs

## Customization

Edit the top of `Get-OnlineFile.ps1` to change:
- The target `$Url` of the VHD file
- The output file name ($OutputFile)
- The thread count (parameter `$Threads` in `Start-Download`)

## License

Distributed under the relevant license specified by Skyline Communications. See the repository for more information.

---

For more details, see the code:
- [Get-OnlineFile.ps1](https://github.com/SkylineCommunications/SLC_VHD_DownloadManager/blob/main/Get-OnlineFile.ps1)
- [Clear-Processes.ps1](https://github.com/SkylineCommunications/SLC_VHD_DownloadManager/blob/main/Clear-Processes.ps1)
