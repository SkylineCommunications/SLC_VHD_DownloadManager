# Release Notes

## Version 2.0.0 (2026-01-12)

### Major Changes - Module Refactor

**Status:** Release

#### Added
- Complete refactor to PowerShell module architecture (`VHDDownloadManager.psm1`)
- Exported functions: `Start-VHDDownload`, `Stop-VHDDownloadJobs`, `Remove-VHDSegmentFiles`, `Remove-VHDXFile`
- ThreadJob module dependency with automatic installation on first run
- Enhanced `examples.ps1` with automatic ThreadJob setup and error handling
- ThreadJob module import in both PSM1 and calling scripts for terminal compatibility
- Comprehensive documentation with troubleshooting guides and performance tips

#### Fixed
- **Critical:** ThreadJob module not recognized when running from terminal directly
  - Now explicitly imported in `VHDDownloadManager.psm1` module header
  - `examples.ps1` pre-imports ThreadJob before module load
  - Auto-installs ThreadJob if missing with helpful error messages
- **Critical:** Module loading failed in terminal due to missing ThreadJob import
  - Module now gracefully handles ThreadJob initialization with try-catch
- Segment file path issues resolved with proper directory handling

#### Changed
- Restructured from procedural scripts to modular functions
- Improved error handling throughout module
- Enhanced progress monitoring with better formatting
- Updated README with module-based usage patterns
- Release notes now track version history with detailed changelogs

#### Improved
- Terminal compatibility (now works in both VS Code and direct terminal execution)
- Module usability with proper function exports
- Documentation clarity for new and existing users
- Error messages are more descriptive and actionable

### Migration Guide from v1.1.0

**Old approach:**
```powershell
.\Get-OnlineFile.ps1
.\Clear-Processes.ps1
```

**New approach:**
```powershell
Import-Module '.\VHDDownloadManager.psm1'
Start-VHDDownload -Url $url -Threads 64 -Verify -ShowReport
Stop-VHDDownloadJobs
Remove-VHDSegmentFiles -Directory '.'
```

Or simply use:
```powershell
.\examples.ps1
```

---

## Version 1.1.0 (2026-01-12)

### Added
- Window titles for clarity:
  - Main: "VHD Download Manager - Main Process"
  - Children: "VHD Download - Segment <N>"

### Changed
- Removed PID-based tracking/cleanup; replaced with job-based cleanup using `Get-Job`, `Stop-Job`, and `Remove-Job`.
- Fixed Unix-style stderr redirection (`2>/dev/null`) to PowerShell semantics.
- Updated README: requirements now state Windows PowerShell 5.1 or PowerShell 7+, usage examples, and cleanup behavior.

### Fixed
- Error where `Get-Process -Id $job.ChildJobs[0].Location` attempted to use `Location` (e.g., `localhost`) as a PID.

### Compatibility
- Validated on Windows PowerShell 5.1 and PowerShell 7.
- Minimum PowerShell 4.0 due to `Get-FileHash`; 5.1+ recommended. Use your default PowerShell for normal operation.

---

## Version 1.0.0

- Initial segmented download implementation with retry logic and SHA-256 verification.
- Basic cleanup script to remove segment files and stop lingering jobs.
