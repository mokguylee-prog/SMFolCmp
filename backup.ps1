# backup.ps1 — 프로젝트 zip 백업 스크립트
# 생성: makeback 스킬

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── 설정 ──────────────────────────────────────────
$ProjectDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectName = Split-Path -Leaf $ProjectDir
$BackupDir   = Split-Path -Parent $ProjectDir
$Timestamp   = Get-Date -Format "yyyyMMdd_HHmmss"
$ArchiveName = "${ProjectName}_${Timestamp}.zip"
$ArchivePath = Join-Path $BackupDir $ArchiveName

# ── 제외 패턴 ─────────────────────────────────────
$ExcludePatterns = @(
    # .NET / C#
    "bin", "obj", ".vs",
    # Node.js / npm
    "node_modules", "vendor", ".pnp",
    # General build output
    "dist", "build", "out", "target",
    # Frontend frameworks
    ".next", ".nuxt", ".svelte-kit",
    # Python
    "__pycache__", ".cache", ".parcel-cache", ".turbo",
    # Version control
    ".git", ".svn", ".hg",
    # IDE
    ".idea", ".vscode",
    # OS files
    ".DS_Store", "Thumbs.db", "desktop.ini",
    # Temporary
    "tmp", "temp",
    "*.log", "*.pid", "*.sock",
    "*.tmp", "*.temp", "*.swp",
    ".env.local"
)

# ── 파일 수집 ─────────────────────────────────────
Write-Host "📦 백업 시작: $ProjectName"
Write-Host "   저장 위치: $ArchivePath"

$Files = Get-ChildItem -Path $ProjectDir -Recurse -File | Where-Object {
    $relativePath = $_.FullName.Substring($ProjectDir.Length + 1)
    $parts = $relativePath -split [regex]::Escape([IO.Path]::DirectorySeparatorChar)
    $skip = $false
    foreach ($pattern in $ExcludePatterns) {
        foreach ($part in $parts) {
            if ($part -like $pattern) { $skip = $true; break }
        }
        if ($skip) { break }
        if ($_.Name -like $pattern) { $skip = $true; break }
        # .env.*.local 패턴 처리
        if ($_.Name -match '\.env\..+\.local$') { $skip = $true; break }
    }
    -not $skip
}

# ── 압축 실행 ─────────────────────────────────────
$TempDir = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName())
New-Item -ItemType Directory -Path $TempDir | Out-Null

try {
    foreach ($File in $Files) {
        $relative = $File.FullName.Substring((Split-Path -Parent $ProjectDir).Length + 1)
        $dest = Join-Path $TempDir $relative
        $destDir = Split-Path -Parent $dest
        if (-not (Test-Path $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }
        Copy-Item -Path $File.FullName -Destination $dest
    }
    Compress-Archive -Path (Join-Path $TempDir $ProjectName) -DestinationPath $ArchivePath -Force
} finally {
    Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
}

$Size = [math]::Round((Get-Item $ArchivePath).Length / 1MB, 2)
Write-Host "✅ 완료: $ArchiveName (${Size} MB)"
