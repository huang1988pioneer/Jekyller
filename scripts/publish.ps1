#Requires -Version 5.1
<#
.SYNOPSIS
  Publish Jekyller as a single-file EXE and optionally build an installer.

.DESCRIPTION
  1) dotnet publish -> self-contained single-file Jekyller.exe
  2) If `vpk` (Velopack) is available -> create portable + Setup.exe installer
  3) If Inno Setup (ISCC) is available -> also build classic Setup from installer/jekyller.iss

.EXAMPLE
  .\scripts\publish.ps1
  .\scripts\publish.ps1 -Version 1.2.7 -Runtime win-x64
#>
param(
    [string]$Version = "1.2.7",
    [string]$Runtime = "win-x64",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $Root "Jekyller.csproj"))) {
    $Root = (Get-Location).Path
}

$PublishDir = Join-Path $Root "dist\publish\$Runtime"
$ReleaseDir = Join-Path $Root "dist\releases"
$SingleDir  = Join-Path $Root "dist\single"

Write-Host "==> Jekyller publish v$Version ($Runtime)" -ForegroundColor Cyan
Write-Host "    Root: $Root"

foreach ($d in @($PublishDir, $SingleDir)) {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
    New-Item -ItemType Directory -Path $d -Force | Out-Null
}
New-Item -ItemType Directory -Path $ReleaseDir -Force | Out-Null

Write-Host "==> dotnet publish (single-file, self-contained)..." -ForegroundColor Cyan
dotnet publish (Join-Path $Root "Jekyller.csproj") `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:Version=$Version `
    -p:DebugType=embedded `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$exe = Join-Path $PublishDir "Jekyller.exe"
if (-not (Test-Path $exe)) { throw "Jekyller.exe not found at $exe" }

Copy-Item $exe (Join-Path $SingleDir "Jekyller.exe") -Force
$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "    Single EXE: $SingleDir\Jekyller.exe ($sizeMb MB)" -ForegroundColor Green

if ($SkipInstaller) {
    Write-Host "==> SkipInstaller set; done." -ForegroundColor Yellow
    exit 0
}

$vpk = Get-Command vpk -ErrorAction SilentlyContinue
if (-not $vpk) {
    Write-Host "==> Installing Velopack CLI (dotnet tool)..." -ForegroundColor Cyan
    try {
        dotnet tool update -g vpk 2>$null
        if ($LASTEXITCODE -ne 0) { dotnet tool install -g vpk }
    } catch {
        Write-Host "    Could not install vpk globally: $_" -ForegroundColor Yellow
    }
    $vpk = Get-Command vpk -ErrorAction SilentlyContinue
    if (-not $vpk) {
        $toolPath = Join-Path $env:USERPROFILE ".dotnet\tools\vpk.exe"
        if (Test-Path $toolPath) {
            $vpk = Get-Command $toolPath
        }
    }
}

if ($vpk) {
    Write-Host "==> Velopack pack -> installer..." -ForegroundColor Cyan
    $veloOut = Join-Path $ReleaseDir "velopack"
    if (Test-Path $veloOut) { Remove-Item $veloOut -Recurse -Force }
    New-Item -ItemType Directory -Path $veloOut -Force | Out-Null

    & $vpk.Source pack `
        --packId Jekyller `
        --packVersion $Version `
        --packDir $PublishDir `
        --mainExe Jekyller.exe `
        --packTitle "Jekyller" `
        --outputDir $veloOut

    if ($LASTEXITCODE -eq 0) {
        Write-Host "    Velopack output: $veloOut" -ForegroundColor Green
        Get-ChildItem $veloOut -File | ForEach-Object {
            Write-Host ("      - {0} ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB))
        }
    } else {
        Write-Host "    Velopack pack failed (exit $LASTEXITCODE)" -ForegroundColor Yellow
    }
} else {
    Write-Host "==> vpk not found; skip Velopack installer." -ForegroundColor Yellow
    Write-Host "    Install: dotnet tool install -g vpk" -ForegroundColor Yellow
}

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    Write-Host "==> Inno Setup..." -ForegroundColor Cyan
    $iss = Join-Path $Root "installer\jekyller.iss"
    if (Test-Path $iss) {
        & $iscc $iss /DMyAppVersion=$Version /DMyPublishDir=$PublishDir
        if ($LASTEXITCODE -eq 0) {
            Write-Host "    Inno Setup output under dist\releases" -ForegroundColor Green
        }
    }
} else {
    Write-Host "==> Inno Setup (ISCC) not found; skip classic installer." -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Portable single EXE : $SingleDir\Jekyller.exe"
Write-Host "  Full publish folder : $PublishDir"
Write-Host "  Installers          : $ReleaseDir"
