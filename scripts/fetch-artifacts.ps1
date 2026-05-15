# Fetch the latest successful CI build artifacts and merge them into unity_package/.
#
# Requires: gh CLI authenticated to the repo  (gh auth login)
#
# Usage:
#   pwsh scripts/fetch-artifacts.ps1                    # all platforms
#   pwsh scripts/fetch-artifacts.ps1 windows linux      # specific platforms
#   pwsh scripts/fetch-artifacts.ps1 -Run 12345678      # specific run ID

param(
    [Parameter(Position = 0, ValueFromRemainingArguments)]
    [string[]] $Platforms = @("windows", "macos", "linux", "android", "ios"),

    [string] $Run = ""
)

$ErrorActionPreference = "Stop"

$artifactNames = @{
    "windows" = "unity_dlp-windows-x64"
    "macos"   = "unity_dlp-macos-universal"
    "linux"   = "unity_dlp-linux-x64"
    "android" = "unity_dlp-android-arm64"
    "ios"     = "unity_dlp-ios-arm64"
}

$repoRoot = Split-Path $PSScriptRoot -Parent
$tmpDir   = Join-Path ([System.IO.Path]::GetTempPath()) "dlp-artifacts-$(Get-Random)"

try {
    New-Item -ItemType Directory -Force $tmpDir | Out-Null

    if (-not $Run) {
        Write-Host "==> Finding latest successful run on main..."
        $Run = (gh run list `
            --workflow build.yml `
            --branch main `
            --status success `
            --limit 1 `
            --json databaseId `
            --jq '.[0].databaseId').Trim()
        if (-not $Run) {
            Write-Error "No successful runs found on main branch. Check: gh run list --workflow build.yml --branch main"
            exit 1
        }
        Write-Host "    Run ID: $Run"
    }

    foreach ($plat in $Platforms) {
        $name = $artifactNames[$plat]
        if (-not $name) {
            Write-Warning "Unknown platform '$plat'. Valid: $($artifactNames.Keys -join ', ')"
            continue
        }

        Write-Host "==> Downloading $name..."
        gh run download $Run --name $name --dir $tmpDir
        if ($LASTEXITCODE -ne 0) {
            Write-Error "gh run download failed for $name"
            continue
        }

        # Artifacts preserve the repo-relative path, so files land at:
        #   <tmpDir>/<artifact-name>/unity_package/Plugins/...
        $srcPkg = Join-Path $tmpDir $name "unity_package"
        if (-not (Test-Path $srcPkg)) {
            Write-Warning "Expected path not found: $srcPkg — skipping $name"
            continue
        }

        $dstPkg = Join-Path $repoRoot "unity_package"
        Get-ChildItem $srcPkg | ForEach-Object {
            Copy-Item $_.FullName (Join-Path $dstPkg $_.Name) -Recurse -Force
        }
        Write-Host "    Merged into unity_package/"
    }
}
finally {
    Remove-Item $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Done."
