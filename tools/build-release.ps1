# Builds the Windows release of Warcrown into build\Warcrown-<version>-windows.zip.
#
#   powershell -ExecutionPolicy Bypass -File tools\build-release.ps1
#   powershell -ExecutionPolicy Bypass -File tools\build-release.ps1 -SkipTests
#   powershell -ExecutionPolicy Bypass -File tools\build-release.ps1 -Godot "D:\Godot\Godot_v4.7.2-stable_mono_win64_console.exe"
#
# Steps: run the simulation tests (unless -SkipTests), export the "Windows Desktop" preset into build\windows,
# add the players' guide, zip it with the version from Scripts\Net\GameVersion.cs.
param(
    [string]$Godot = "C:\main\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

if (-not (Test-Path $Godot)) {
    throw "Godot not found at '$Godot'. Pass its console executable with -Godot."
}

if (-not $SkipTests) {
    Write-Host "== Tests"
    dotnet test tests/Warcrown.Sim.Tests
    if ($LASTEXITCODE -ne 0) { throw "Tests failed: nothing was built." }
}

Write-Host "== Export"
if (Test-Path build\windows) { Remove-Item -Recurse -Force build\windows }
New-Item -ItemType Directory -Force build\windows | Out-Null
& $Godot --headless --path . --export-release "Windows Desktop" build/windows/Warcrown.exe
if (-not (Test-Path build\windows\Warcrown.exe)) { throw "Export failed: build\windows\Warcrown.exe is missing." }
Copy-Item "docs\Как запустить.txt" "build\windows\Как запустить.txt"

Write-Host "== Zip"
$version = [regex]::Match((Get-Content Scripts\Net\GameVersion.cs -Raw), 'Number = "([^"]+)"').Groups[1].Value
if (-not $version) { throw "Could not read the version from Scripts\Net\GameVersion.cs." }
$zip = "build\Warcrown-$version-windows.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path build\windows\* -DestinationPath $zip

Write-Host ("== Done: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
