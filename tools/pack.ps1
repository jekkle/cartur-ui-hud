# Builds the plugin and zips a Thunderstore-ready package into dist/.
#
#   powershell -ExecutionPolicy Bypass -File tools\pack.ps1
#
# Thunderstore wants manifest.json, icon.png and README.md at the zip root, so the
# package/ folder is zipped by its contents, not as a folder.
#
# Unlike the other Cartur mods this one ships art, and AssetLoader looks for it in an
# assets/ folder beside the DLL. So the DLL cannot sit loose in plugins/ - it goes in
# plugins/CarturUIHud/ with assets/ next to it, and that shape has to survive the zip.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$pkg = Join-Path $root "package"
$dist = Join-Path $root "dist"

$manifest = Get-Content (Join-Path $pkg "manifest.json") -Raw | ConvertFrom-Json
$version = $manifest.version_number

# The plugin version and the package version are read by different people in different
# places; if they drift, the Thunderstore listing lies about what is in the DLL.
$plugin = Get-Content (Join-Path $root "src\Plugin.cs") -Raw
if ($plugin -notmatch 'PluginVersion\s*=\s*"([^"]+)"') { throw "Could not read the plugin version from src\Plugin.cs" }
if ($Matches[1] -ne $version) { throw "manifest.json is $version but the plugin says $($Matches[1])" }

dotnet build (Join-Path $root "src\CarturUIHud.csproj") -c Release
if (-not $?) { throw "build failed" }

# Cleared, not just overwritten: a DLL or a renamed PNG left behind from an earlier build
# would otherwise ride along in the zip.
$modDir = Join-Path $pkg "plugins\CarturUIHud"
if (Test-Path (Join-Path $pkg "plugins")) { Remove-Item (Join-Path $pkg "plugins") -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $modDir "assets") | Out-Null
Copy-Item (Join-Path $root "src\bin\Release\net472\CarturUIHud.dll") $modDir -Force
# Board art the code no longer loads stays out (AssetLoader.s_boardNames is the list it loads):
# the per-size chest boards and the thin boards were replaced by GridBoard, and the single
# enchanting board by one per tab. ~22 MB.
$unused = "board_chest.png", "board_chest_[0-9]*x*.png", "board_thin*.png", "board_enchant.png"   # not board_chest_icon_*: those are used
Get-ChildItem (Join-Path $root "src\Assets") -Filter *.png -File |
    Where-Object { $n = $_.Name; -not ($unused | Where-Object { $n -like $_ }) } |
    Copy-Item -Destination (Join-Path $modDir "assets") -Force
# The same three art folders the csproj deploys beside the DLL (LoadingArt, SleepVideo,
# Inlays read them). Packing only the loose PNGs shipped a release with no loading art, no
# sleep screen and no ship inlay (release review, 2026-10-05). Retired card art stays out.
foreach ($sub in "loading", "sleep", "inlays") {
    $from = Join-Path $root "src\Assets\$sub"
    if (-not (Test-Path $from)) { throw "src\Assets\$sub is missing - LoadingArt/SleepVideo/Inlays would ship empty" }
    $to = Join-Path $modDir "assets\$sub"
    New-Item -ItemType Directory -Force -Path $to | Out-Null
    Get-ChildItem $from -File | Copy-Item -Destination $to -Force
}

$shipped = Get-ChildItem (Join-Path $modDir "assets") -File -Recurse
$mb = [math]::Round(($shipped | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "assets: $($shipped.Count) files, $mb MB"

New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zip = Join-Path $dist "$($manifest.name)-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }

# Written entry by entry rather than with Compress-Archive: that cmdlet stores Windows
# backslashes in the entry names, and an installer reading the zip then creates a single
# file literally named "plugins\CarturUIHud\CarturUIHud.dll" instead of the folders.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, "Create")
try {
    foreach ($file in Get-ChildItem $pkg -Recurse -File) {
        $name = $file.FullName.Substring($pkg.Length + 1).Replace("\", "/")
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $name) | Out-Null
    }
}
finally { $archive.Dispose() }

Write-Host "packed $zip"

# Nexus installs by unpacking into the game folder rather than reading a manifest, so its
# zip is the plugin folder at its real path and nothing else - no manifest, icon or README,
# which would land in the Valheim root as loose files.
$nexus = Join-Path $dist "$($manifest.name)-$version-Nexus.zip"
if (Test-Path $nexus) { Remove-Item $nexus }
$archive = [System.IO.Compression.ZipFile]::Open($nexus, "Create")
try {
    foreach ($file in Get-ChildItem $modDir -Recurse -File) {
        $rel = $file.FullName.Substring($modDir.Length + 1).Replace("\", "/")
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $file.FullName, "BepInEx/plugins/CarturUIHud/$rel") | Out-Null
    }
}
finally { $archive.Dispose() }

Write-Host "packed $nexus"
