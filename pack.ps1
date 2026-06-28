#Requires -Version 5.1
<#
.SYNOPSIS
    Build & publish MangaReader.Native to _release/{version}/ directory (no zip, direct overwrite)
.PARAMETER Mode
    standalone (default, ~60MB, no .NET needed) | runtime-dep (lightweight, needs .NET 8)
.PARAMETER OutDir
    Output directory (default: project-root/_release/{version})
.PARAMETER Version
    Version string (default: git tag or 1.0.0)
#>

param(
    [ValidateSet('standalone', 'runtime-dep')]
    [string]$Mode = 'standalone',
    [string]$OutDir = '',
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectFile = Join-Path $ProjectRoot 'Theater\Theater.csproj'

# version: explicit param > project file (auto-increment patch) > git tag > default
if (-not $Version) {
    $Version = '1.0.0'
    try {
        $projectXml = Get-Content $ProjectFile -Raw
        $projectVersionMatch = [regex]::Match($projectXml, '<Version>\s*([^<]+)\s*</Version>', 'IgnoreCase')
        if ($projectVersionMatch.Success) { $Version = $projectVersionMatch.Groups[1].Value.Trim() }
    } catch {}
    try {
        $gitTag = git -C $ProjectRoot describe --tags --abbrev=0 2>$null
        if ($Version -eq '1.0.0' -and $gitTag) { $Version = $gitTag.TrimStart('v') }
    } catch {}

    # 自动小幅度递增版本号：每次打包将最后一段数字 +1，并写回 csproj
    # 例如 0.1.1.0 -> 0.1.1.1，0.1.1.9 -> 0.1.1.10
    try {
        $segments = $Version -split '\.'
        if ($segments.Count -ge 2) {
            $lastIndex = $segments.Count - 1
            $patch = 0
            if ([int]::TryParse($segments[$lastIndex], [ref]$patch)) {
                $segments[$lastIndex] = [string]($patch + 1)
                $newVersion = ($segments -join '.')
                if ($newVersion -ne $Version) {
                    $projectXml = Get-Content $ProjectFile -Raw
                    $updatedXml = $projectXml
                    foreach ($tag in @('Version','AssemblyVersion','FileVersion','InformationalVersion')) {
                        $updatedXml = [regex]::Replace(
                            $updatedXml,
                            "<$tag>\s*([^<]+)\s*</$tag>",
                            "<$tag>$newVersion</$tag>",
                            'IgnoreCase')
                    }
                    [System.IO.File]::WriteAllText($ProjectFile, $updatedXml)
                    Write-Host "  [BUMP] Version: $Version -> $newVersion (written back to csproj)" -ForegroundColor Magenta
                    $Version = $newVersion
                }
            }
        }
    } catch {
        Write-Host "  [WARN] Auto-increment version failed: $_" -ForegroundColor DarkYellow
    }
}

# output to versioned subdirectory so UpdateService.EnumerateLocalPackages() can find it
if (-not $OutDir) {
    $OutDir = Join-Path $ProjectRoot "_release\$Version"
}

# preserve root _release/ data if upgrading in-place
$PreserveOutDir = Split-Path $OutDir -Parent  # e.g. _release/

# Generate AppIcon.ico from icon.png (32-bit multi-frame)
Write-Host "`n  [GEN] Generating AppIcon.ico from icon.png..." -ForegroundColor Yellow
& python (Join-Path $ProjectRoot '_scripts/gen_icon.py')
if ($LASTEXITCODE -ne 0) {
    Write-Host "  [WARN] Icon generation failed, using existing AppIcon.ico" -ForegroundColor DarkYellow
}

# kill running instance if any (尝试 Stop-Process 和 taskkill 两种方式)
$running = Get-Process -Name 'Theater' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "  [KILL] Closing running Theater (PID $($running.Id))..." -ForegroundColor Yellow
    try {
        $running | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    } catch {}
    $still = Get-Process -Name 'Theater' -ErrorAction SilentlyContinue
    if ($still) {
        # 管理员权限进程：taskkill 可能也失败，静默尝试
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = "taskkill.exe"
        $psi.Arguments = "/F /PID $($running.Id)"
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        try {
            $tk = [System.Diagnostics.Process]::Start($psi)
            $tk.WaitForExit(3000) | Out-Null
            Start-Sleep -Seconds 1
        } catch {}
    }
    $still = Get-Process -Name 'Theater' -ErrorAction SilentlyContinue
    if ($still) {
        Write-Host "  [WARN] 无法关闭 Theater（PID $($running.Id)），可能是管理员权限进程。" -ForegroundColor Red
        Write-Host "         请手动关闭后重试，或继续打包（输出到新版本目录，可能不冲突）。" -ForegroundColor Yellow
    }
}

# preserve user config & data from the versioned output dir
$preserveList = @('Theater_DataLocation.txt', 'Theater_Data')
$backupDir = Join-Path $env:TEMP "theater_pack_backup_$(Get-Random)"
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
foreach ($name in $preserveList) {
    $src = Join-Path $OutDir $name
    if (Test-Path $src) {
        Copy-Item $src $backupDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# clean output dir
if (Test-Path $OutDir) { Remove-Item "$OutDir\*" -Recurse -Force -ErrorAction SilentlyContinue }
else { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

# restore preserved files
if (Test-Path $backupDir) {
    Copy-Item "$backupDir\*" $OutDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $backupDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`n=== Build Theater ($Mode) v$Version ===" -ForegroundColor Cyan

$versionProps = @(
    "-p:Version=$Version",
    "-p:AssemblyVersion=$Version",
    "-p:FileVersion=$Version",
    "-p:InformationalVersion=$Version"
)

if ($Mode -eq 'standalone') {
    & dotnet publish $ProjectFile -c Release -o $OutDir -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=false `
        -p:DebugType=none -p:DebugSymbols=false `
        @versionProps
} else {
    & dotnet publish $ProjectFile -c Release -o $OutDir `
        --self-contained false `
        @versionProps
}

if ($LASTEXITCODE -ne 0) {
    Write-Host "BUILD FAILED" -ForegroundColor Red
    exit 1
}

# also symlink/unzip-friendly root copy: _release/Theater.exe
$rootExe = Join-Path $PreserveOutDir 'Theater.exe'
$builtExe = Join-Path $OutDir 'Theater.exe'
if (Test-Path $builtExe) {
    Copy-Item $builtExe $rootExe -Force
    Write-Host "  [OK] Root copy: $rootExe" -ForegroundColor Green
}

# attach README
$ReadmeSrc = Join-Path $ProjectRoot 'README.md'
if (Test-Path $ReadmeSrc) {
    Copy-Item $ReadmeSrc (Join-Path $OutDir 'README.txt')
    Write-Host '  [OK] README attached' -ForegroundColor Green
}

Write-Host "`n=== Done ===" -ForegroundColor Green
Write-Host "  Version : v$Version   Mode: $Mode" -ForegroundColor White
Write-Host "  Output  : $OutDir" -ForegroundColor Cyan
Write-Host "  Run     : $(Join-Path $OutDir 'Theater.exe')" -ForegroundColor White
