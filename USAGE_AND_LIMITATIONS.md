# Usage and Known Limitations

## Recommended Commands

- List available VHDX images:
  - `SLC_DownloadManager.exe --list-images --catalog-url="https://softwaredownloads.dataminer.services/dataminer-virtual-disk/"`
- Pick image interactively and auto-resolve hash:
  - `SLC_DownloadManager.exe --select-image 64 --hash=auto`
- Download direct URL with auto hash lookup:
  - `SLC_DownloadManager.exe "https://softwaredownloads.dataminer.services/dataminer-virtual-disk/10.6/mgdsk-selfhosted-dma-Images-Standard-1006.00.0200.vhdx" 64 "SLC_DMA.vhdx" --hash=auto`
- Download direct URL with explicit hash source:
  - `SLC_DownloadManager.exe "<vhdx-url>" 64 "SLC_DMA.vhdx" --hash-url="<hash-url>"`

## Known Limitations

- Catalog listing requires blob/container listing permissions.
- Auto hash lookup depends on common naming conventions (`.sha256`, `.sha256sum`, `.hash`); some image URLs may require `--hash-url`.
- Hash integrity check verifies file consistency, but trust still depends on the trustworthiness of the hash source.
