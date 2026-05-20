param(
    [string]$OutputPath = "bin\Debug\net8.0-windows"
)

$src = Join-Path (Get-Location) $OutputPath
$dst = "D:\utils\SMFolCmp"

if (!(Test-Path $dst)) {
    New-Item -ItemType Directory -Path $dst -Force | Out-Null
}

Copy-Item -Path "$src\*" -Destination $dst -Recurse -Force
Write-Host "✓ Copied to $dst"
