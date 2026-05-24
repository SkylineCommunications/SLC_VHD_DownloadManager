# Publish SLC Download Manager as self-contained executable
# This creates a single .exe file that users can run without installing .NET

Write-Host "Publishing SLC Download Manager..." -ForegroundColor Cyan

# Clean previous builds
if (Test-Path ".\bin\Release") {
    Remove-Item ".\bin\Release" -Recurse -Force
}

# Publish only the app project for Windows x64 (self-contained, single file)
dotnet publish ".\SLC_DownloadManager.csproj" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

if ($LASTEXITCODE -eq 0) {
    $exePath = ".\bin\Release\net8.0\win-x64\publish\SLC_DownloadManager.exe"
    $size = (Get-Item $exePath).Length / 1MB
    
    Write-Host "`nPublish successful!" -ForegroundColor Green
    Write-Host "Executable: $exePath" -ForegroundColor Yellow
    Write-Host "Size: $([math]::Round($size, 2)) MB" -ForegroundColor Yellow

    while ($true) {
        $runChoice = (Read-Host "`nRun the published executable now? (y/n)").Trim().ToLowerInvariant()
        if ($runChoice -eq "y") {
            Write-Host "Launching executable..." -ForegroundColor Cyan
            & $exePath
            break
        }

        if ($runChoice -eq "n") {
            Write-Host "Skipping launch." -ForegroundColor DarkGray
            break
        }

        Write-Host "Please enter 'y' or 'n'." -ForegroundColor Yellow
    }
} else {
    Write-Host "`nPublish failed!" -ForegroundColor Red
    exit 1
}
