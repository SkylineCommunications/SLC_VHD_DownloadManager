# SLC Download Manager (C#)

Production-grade parallel VHD/file downloader with real-time heatmap progress visualization, automatic retry logic, and configurable concurrency.

## Features

- **Parallel segment downloads** with configurable thread count (1-256+)
- **Real-time heatmap** showing per-segment status:
  - **Green (0)**: Segment completed successfully
  - **Yellow (1-3)**: Segment retrying after failure (shows retry count)
  - **Red (3+)**: Segment failed after exhausting max retries
  - **Gray (0)**: Segment in progress or queued
- **File integrity verification** with SHA256 hash comparison
- **Automatic hash lookup** (`--hash=auto` or `--hash-url=...`)
- **Dynamic image discovery** with list/select flows from Azure Blob container listing
- **Automatic retry logic** with 3 attempts per segment and 2-second backoff
- **HTTP range request support** for efficient parallel downloads
- **Progress tracking** with percentage and bytes downloaded
- **Segment merging** with progress display
- **Automatic cleanup** of temporary segment files
- **Chaos mode** for testing failure scenarios
- **Async/await** patterns with proper cancellation support

## Quick Start

### For Developers

#### Build
```bash
dotnet build
```

#### Usage
```bash
dotnet run -- [URL] [THREADS] [OUTPUT_PATH] [--chaos] [--retries=N] [--hash=HASH|auto] [--hash-url=URL] [--list-images] [--select-image]
```

**Examples:**
```bash
dotnet run -- "https://github.com/szalony9szymek/large/releases/download/free/large" 64 "test.bin"
dotnet run -- "https://github.com/szalony9szymek/large/releases/download/free/large" 8 "test.bin" --chaos
dotnet run -- --list-images --catalog-url="https://softwaredownloads.dataminer.services/dataminer-virtual-disk/"
dotnet run -- --select-image 64 --hash=auto
dotnet run -- "https://softwaredownloads.dataminer.services/dataminer-virtual-disk/10.6/mgdsk-selfhosted-dma-Images-Standard-1006.00.0200.vhdx" 256 "SLC_DMA.vhdx" --hash=auto
```

### For End Users

#### Publish Self-Contained Executable (Windows)
```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

The executable will be in: `bin\Release\net8.0\win-x64\publish\SLC_DownloadManager.exe`

#### Usage
```bash
SLC_DownloadManager.exe [URL] [THREADS] [OUTPUT_PATH] [--chaos] [--retries=N] [--hash=HASH|auto] [--hash-url=URL] [--list-images] [--select-image]
```

**Examples:**
```bash
SLC_DownloadManager.exe "https://github.com/szalony9szymek/large/releases/download/free/large" 64 "test.bin"
SLC_DownloadManager.exe "https://github.com/szalony9szymek/large/releases/download/free/large" 8 "test.bin" --chaos
SLC_DownloadManager.exe --list-images --catalog-url="https://softwaredownloads.dataminer.services/dataminer-virtual-disk/"
SLC_DownloadManager.exe --select-image 64 --hash=auto
SLC_DownloadManager.exe "https://softwaredownloads.dataminer.services/dataminer-virtual-disk/10.5/mgdsk-selfhosted-dma-Images-Standard-1005.00.1400.vhdx" 256 "SLC_DMA.vhdx" --hash=auto
```

## Command-Line Arguments

| Argument | Type | Default | Description |
|----------|------|---------|-------------|
| URL | string | required | Download URL (must support HTTP range requests) |
| THREADS | int | `8` | Number of parallel segments (recommended: 8-64) |
| OUTPUT_PATH | string | `downloaded_file.bin` | Local file path to save |
| --chaos | flag | disabled | Enable chaos mode (injects failures for testing) |
| --retries=N | int | `3` | Maximum retry attempts per segment (minimum: 1) |
| --hash=HASH | string | none | Explicit SHA256 hash for file integrity verification |
| --hash=auto | string | disabled | Derive hash URL candidates from the VHDX URL and resolve SHA256 automatically |
| --hash-url=URL | string | none | Download hash text from a specific URL and parse SHA256 |
| --list-images | flag | disabled | List available VHDX images from the catalog endpoint |
| --select-image | flag | disabled | Prompt user to select a VHDX image from catalog before downloading |
| --catalog-url=URL | string | `https://softwaredownloads.dataminer.services/dataminer-virtual-disk/` | Blob container endpoint used by list/select |
| --catalog-prefix=PREFIX | string | `10.` | Prefix filter passed to blob listing API |
| --threads=N | int | none | Alternative to positional `THREADS` argument |
| --no-hash-verify | flag | disabled | Skip hash verification even when auto resolution is enabled |

## Project Structure

```
src/
  Program.cs           - Entry point, command-line parsing
  DownloadManager.cs   - Core download logic, heatmap rendering, retry handling
   ImageCatalogService.cs   - Catalog listing and VHDX discovery
   HashResolutionService.cs - Hash source resolution and SHA256 parsing
tests/
   SLC_DownloadManager.Tests/
      HashResolutionServiceTests.cs
      ImageCatalogServiceTests.cs
SLC_DownloadManager.csproj
.vscode/tasks.json    - VS Code build/run tasks
```

## Dynamic Discovery and Hash Lookup

### Image Discovery

- `--list-images` calls Azure Blob listing (`restype=container&comp=list`) and renders all `.vhdx` blobs.
- `--select-image` opens an interactive picker using Spectre.Console and uses the selected URL for the download.
- `--catalog-prefix=...` can narrow results (for example `10.5/`).

### Hash Lookup

- `--hash=<64-hex>` uses the exact user-provided hash.
- `--hash-url=...` fetches a hash file and extracts the first 64-hex SHA256 token.
- `--hash=auto` tries derived URLs in this order:
   - `<vhdx>.sha256`
   - `<vhdx>.sha256sum`
   - `<vhdx>.hash`
   - `<vhdx-without-extension>.sha256`
   - `<vhdx-without-extension>.sha256sum`

If no hash can be resolved, download proceeds without verification unless explicit hash/hash-url was supplied.

## Progress Display

During download, the heatmap updates every 500ms to show per-segment state and overall throughput:
```
Segment Status:
0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0
0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0
Progress: 25% | 250.00 MB / 1000.00 MB
```

Legend: **green** = success, **yellow** = retrying (number shows retry count), **red** = failed after max retries, **gray** = in progress/queued.

After download completes, the final segment heatmap is displayed as a summary, followed by a single-line merge progress indicator that updates in-place (0% → 100%) without scrolling:
```
Download Complete - Final Segment Status:
0 0 0 0 0 0 0 0

100% | 1996.20 MB / 1996.20 MB

Merging segments into final file...
Merging:  75%
Successfully created: test.bin
```

## Chaos Mode

Chaos mode (`--chaos`) purposely injects failures to validate retry logic and resilience:
- **Segment 0**: Simulated immediate failure on first attempt, forces retry path
- **Segment 1**: Simulated timeout (5-second limit), exercises cancellation handling

Use chaos mode to verify:
- Heatmap color transitions (gray → yellow [1] → green or red)
- Retry counter increments in yellow segments
- Merge executes successfully after all retries complete
- Single-line merge progress updates cleanly without scrolling

Example output during chaos (retries in progress):
```
Segment Status:
1 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0
0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0
Progress: 12% | 120.00 MB / 1000.00 MB
```

After segment retries complete (all segments green):
```
Download Complete - Final Segment Status:
0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0

100% | 1000.00 MB / 1000.00 MB
```

Tip: Increase `--retries` if segments exceed max attempts (e.g., `--retries=6`) for more aggressive retry behavior.

## Error Handling

- **HTTP errors (403, 404, 500)**: Retried 3 times with 2-second delays
- **Timeout errors**: 5-second per-attempt timeout
- **File I/O errors**: Caught and reported
- **Failed segments**: Download aborts if exhausted all retries
- **Merge failures**: Temporary file cleanup on error

## Performance Notes

- **Optimal thread count**: 8-64 threads
- **Segment size**: Calculated as `fileSize / threadCount`
- **Memory usage**: Minimal—streams written directly to disk

## Testing

### Test Scenarios

1. **Successful download**:
   ```bash
   dotnet run -- "https://github.com/szalony9szymek/large/releases/download/free/large" 8
   ```

2. **Chaos mode** (test retry/fail):
   ```bash
   dotnet run -- "https://github.com/szalony9szymek/large/releases/download/free/large" 8 "test.bin" --chaos
   ```

3. **High concurrency** (128 threads):
   ```bash
   dotnet run -- "https://github.com/szalony9szymek/large/releases/download/free/large" 128
   ```

## Requirements

- **.NET 8.0** or later
- **Spectre.Console** 0.49.1+ (NuGet auto-restore)

## Troubleshooting

| Issue | Solution |
|-------|----------|
| "Server did not return Content-Length" | URL doesn't support HTTP range requests |
| "No VHDX images found" | Check `--catalog-url`, `--catalog-prefix`, or container listing permissions |
| Hash auto-resolution failed | Provide `--hash-url=...` or explicit `--hash=...` |
| "HTTP 403 Forbidden" | Server is restricting access or requires authentication |
| "Segment files missing" | Disk space exhausted or permission issue |
| Slow download | Reduce thread count or check network bandwidth |

## License

Internal use. Built for Skyline Communications.
