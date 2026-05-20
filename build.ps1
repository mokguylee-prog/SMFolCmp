# SMFolCmp Build Script
# Builds a self-contained release executable

param(
    [string]$Configuration = "Release"
)

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputDir = "$projectDir\bin\$Configuration\net8.0-windows\win-x64\publish"
$parentDir = Split-Path -Parent $projectDir
$targetExe = "$parentDir\SMFolCmp.exe"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SMFolCmp Build Started" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "Project Path: $projectDir"
Write-Host ""

# Remove existing publish folder
if (Test-Path $outputDir) {
    Write-Host "Removing existing publish folder..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $outputDir
}

# Publish as self-contained executable
Write-Host "Publishing self-contained executable..." -ForegroundColor Yellow
Push-Location $projectDir
dotnet publish -c $Configuration -r win-x64 --self-contained
$publishResult = $?
Pop-Location

if (-not $publishResult) {
    Write-Host "Publish failed!" -ForegroundColor Red
    exit 1
}

# Copy entire publish folder to D:\utils\SMFolCmp
$utilsDir = "D:\utils\SMFolCmp"
Write-Host ""
Write-Host "Copying publish folder to $utilsDir..." -ForegroundColor Yellow

if (Test-Path $utilsDir) {
    Remove-Item -Path $utilsDir -Recurse -Force
}
Copy-Item -Path $outputDir -Destination $utilsDir -Recurse -Force
Write-Host "Source: $outputDir"
Write-Host "Target: $utilsDir"

$targetExe = "$utilsDir\SMFolCmp.exe"

if (Test-Path $targetExe) {
    $fileSize = "{0:F2} MB" -f ((Get-Item $targetExe).Length / 1MB)
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "Build Complete!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "Output: $targetExe" -ForegroundColor Green
    Write-Host "Size: $fileSize" -ForegroundColor Green
    Write-Host ""
    Write-Host "Launching application..." -ForegroundColor Cyan
    Start-Process $targetExe
} else {
    Write-Host "Copy failed!" -ForegroundColor Red
    exit 1
}
