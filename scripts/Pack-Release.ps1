<#
.SYNOPSIS
  Publica HiveShock (Release) para Windows, macOS o Linux y genera zips FULL y UPDATE.

.DESCRIPTION
  - full:   primera instalacion (app + profiles + catalogo + .env.example + gifts-images)
  - update: solo el ejecutable. Sin profiles, catalogo, .env ni datos del usuario.

  Salida en: dist\

  RID por defecto = el de esta maquina (win-x64, osx-arm64, osx-x64, linux-x64).
  En un Mac Apple Silicon (Tahoe, M1–M4) sale osx-arm64 y un HiveShock.app.

.EXAMPLE
  .\scripts\Pack-Release.ps1

.EXAMPLE
  .\scripts\Pack-Release.ps1 -Runtime osx-arm64

.EXAMPLE
  .\scripts\Pack-Release.ps1 -IncludeCli -SkipPublish
#>
[CmdletBinding()]
param(
    [switch]$SkipPublish,
    [switch]$IncludeCli,
    [string]$Version,
    [string]$Runtime
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $Root "HiveShock.Avalonia.slnx"))) {
    $Root = $PSScriptRoot
    if (-not (Test-Path (Join-Path $Root "HiveShock.Avalonia.slnx"))) {
        throw "Ejecuta este script desde el repo HiveShock.Avalonia (no se encontro HiveShock.Avalonia.slnx)."
    }
}

Set-Location $Root

function Get-HostRuntime {
    $isOsx = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::OSX
    )

    $isLinux = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Linux
    )

    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture

    if ($isOsx) {
        switch ($arch) {
            "Arm64" { return "osx-arm64" }
            default { return "osx-x64" }
        }
    }

    if ($isLinux) {
        switch ($arch) {
            "Arm64" { return "linux-arm64" }
            default { return "linux-x64" }
        }
    }

    return "win-x64"
}

function Write-Leeme {
    param(
        [string]$TemplatePath,
        [string]$OutPath,
        [string]$AppVersion,
        [string]$Launch,
        [string]$Replace
    )

    if (-not (Test-Path -LiteralPath $TemplatePath)) {
        throw "No se encontró la plantilla LEEME: $TemplatePath"
    }

    $bytes = [System.IO.File]::ReadAllBytes($TemplatePath)
    $text = [System.Text.Encoding]::UTF8.GetString($bytes)

    if ($text.Length -gt 0 -and [int][char]$text[0] -eq 0xFEFF) {
        $text = $text.Substring(1)
    }

    $text = $text.Replace("{VERSION}", $AppVersion)
    $text = $text.Replace("{LAUNCH}", $Launch)
    $text = $text.Replace("{REPLACE}", $Replace)
    $text = $text.TrimEnd() + "`r`n"

    $enc = New-Object System.Text.UTF8Encoding $true
    [System.IO.File]::WriteAllText($OutPath, $text, $enc)
}

function Get-AppVersion {
    param([string]$RootDir)

    foreach ($rel in @(
        "Directory.Build.props",
        "HiveShock.Avalonia\HiveShock.Avalonia.csproj"
    )) {
        $path = Join-Path $RootDir $rel

        if (-not (Test-Path -LiteralPath $path)) {
            continue
        }

        [xml]$xml = Get-Content -Raw $path

        $node = $xml.Project.PropertyGroup |
            ForEach-Object { $_.Version } |
            Where-Object { $null -ne $_ -and -not [string]::IsNullOrWhiteSpace($_.ToString()) } |
            Select-Object -First 1

        if ($null -ne $node) {
            $value = $node.ToString().Trim()

            if (-not [string]::IsNullOrWhiteSpace($value)) {
                return $value
            }
        }
    }

    return "0.0.0"
}

function Get-PublishedGuiName {
    param([string]$Dir)

    $exe = Join-Path $Dir "HiveShock.exe"
    $unix = Join-Path $Dir "HiveShock"

    if (Test-Path -LiteralPath $exe) {
        return "HiveShock.exe"
    }

    if (Test-Path -LiteralPath $unix) {
        return "HiveShock"
    }

    return $null
}

function New-MacAppBundle {
    param(
        [string]$PublishDir,
        [string]$AppPath,
        [string]$AppVersion,
        [string]$PlistTemplate
    )

    if (-not (Test-Path -LiteralPath $PlistTemplate)) {
        throw "No se encontró Info.plist: $PlistTemplate"
    }

    if (Test-Path -LiteralPath $AppPath) {
        Remove-Item -LiteralPath $AppPath -Recurse -Force
    }

    $macos = Join-Path $AppPath "Contents\MacOS"
    $resources = Join-Path $AppPath "Contents\Resources"

    New-Item -ItemType Directory -Force -Path $macos | Out-Null
    New-Item -ItemType Directory -Force -Path $resources | Out-Null

    Copy-Item (Join-Path $PublishDir "*") $macos -Recurse -Force

    $plist = [System.IO.File]::ReadAllText($PlistTemplate)
    $plist = $plist.Replace("{VERSION}", $AppVersion)

    [System.IO.File]::WriteAllText(
        (Join-Path $AppPath "Contents\Info.plist"),
        $plist
    )

    [System.IO.File]::WriteAllText(
        (Join-Path $AppPath "Contents\PkgInfo"),
        "APPL????"
    )
}

# ============================================================
# Resolver Runtime y Version de forma segura
# ============================================================

if ([string]::IsNullOrWhiteSpace($Runtime)) {
    $Runtime = Get-HostRuntime
}

if ([string]::IsNullOrWhiteSpace($Runtime)) {
    throw "No fue posible determinar el Runtime automáticamente."
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-AppVersion $Root
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "0.0.0"
}

# Evitamos .StartsWith() sobre un posible $null
$isOsx = $Runtime -like "osx-*"

$hostIsOsx = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::OSX
)

if ($isOsx -and -not $hostIsOsx) {
    throw "El pack de macOS (firma ad hoc + DMG) hay que hacerlo en un Mac con scripts/pack-release.sh."
}

$PublishGui = Join-Path $Root "publish-gui"
$PublishCli = Join-Path $Root "publish-cli"
$Dist = Join-Path $Root "dist"
$StageFull = Join-Path $Dist "_stage-full"
$StageUpdate = Join-Path $Dist "_stage-update"
$TplFull = Join-Path $PSScriptRoot "templates\LEEME-full.txt"
$TplUpdate = Join-Path $PSScriptRoot "templates\LEEME-update.txt"
$PlistTemplate = Join-Path $PSScriptRoot "macos\Info.plist"

Write-Host "HiveShock pack v$Version ($Runtime)" -ForegroundColor Cyan
Write-Host "Root: $Root"

if (-not $SkipPublish) {
    Write-Host "`n==> Publish GUI (Release, $Runtime)..." -ForegroundColor Yellow
    if (Test-Path $PublishGui) { Remove-Item $PublishGui -Recurse -Force }
    dotnet publish (Join-Path $Root "HiveShock.Avalonia\HiveShock.Avalonia.csproj") -c Release -r $Runtime --self-contained true -o $PublishGui
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish GUI fallo ($LASTEXITCODE)." }

    if ($IncludeCli) {
        Write-Host "`n==> Publish CLI (Release, $Runtime)..." -ForegroundColor Yellow
        if (Test-Path $PublishCli) { Remove-Item $PublishCli -Recurse -Force }
        dotnet publish (Join-Path $Root "HiveShock.Cli\HiveShock.Cli.csproj") -c Release -r $Runtime --self-contained true -o $PublishCli
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish CLI fallo ($LASTEXITCODE)." }
    }
}

$guiName = Get-PublishedGuiName $PublishGui
if (-not $guiName) {
    throw "No hay HiveShock / HiveShock.exe en publish-gui. Quita -SkipPublish o publica antes."
}

$launch = if ($isOsx) { "HiveShock.app" } elseif ($guiName.EndsWith(".exe")) { "HiveShock.exe" } else { "./HiveShock" }
$replace = if ($isOsx) { "HiveShock.app/Contents/MacOS/HiveShock" } else { $guiName }

New-Item -ItemType Directory -Force -Path $Dist | Out-Null
foreach ($p in @($StageFull, $StageUpdate)) {
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $p | Out-Null
}

Write-Host "`n==> Empaquetando FULL..." -ForegroundColor Yellow
if ($isOsx) {
    New-MacAppBundle -PublishDir $PublishGui -AppPath (Join-Path $StageFull "HiveShock.app") -AppVersion $Version -PlistTemplate $PlistTemplate
    if ($IncludeCli -and (Test-Path (Join-Path $PublishCli "HiveShock.Cli"))) {
        Copy-Item (Join-Path $PublishCli "HiveShock.Cli") (Join-Path $StageFull "HiveShock.app\Contents\MacOS") -Force
    }
}
else {
    Copy-Item (Join-Path $PublishGui "*") $StageFull -Recurse -Force
    $cliWin = Join-Path $PublishCli "HiveShock.Cli.exe"
    $cliUnix = Join-Path $PublishCli "HiveShock.Cli"
    if ($IncludeCli -and (Test-Path $cliWin)) { Copy-Item $cliWin $StageFull -Force }
    elseif ($IncludeCli -and (Test-Path $cliUnix)) { Copy-Item $cliUnix $StageFull -Force }
}
Write-Leeme -TemplatePath $TplFull -OutPath (Join-Path $StageFull "LEEME.txt") -AppVersion $Version -Launch $launch -Replace $replace

Write-Host "==> Empaquetando UPDATE..." -ForegroundColor Yellow
if ($isOsx) {
    Copy-Item (Join-Path $PublishGui $guiName) $StageUpdate -Force
}
else {
    Copy-Item (Join-Path $PublishGui $guiName) $StageUpdate -Force
}
$cliWinU = Join-Path $PublishCli "HiveShock.Cli.exe"
$cliUnixU = Join-Path $PublishCli "HiveShock.Cli"
if ($IncludeCli -and (Test-Path $cliWinU)) { Copy-Item $cliWinU $StageUpdate -Force }
elseif ($IncludeCli -and (Test-Path $cliUnixU)) { Copy-Item $cliUnixU $StageUpdate -Force }
Write-Leeme -TemplatePath $TplUpdate -OutPath (Join-Path $StageUpdate "LEEME.txt") -AppVersion $Version -Launch $launch -Replace $replace

$fullZip = Join-Path $Dist "HiveShock-$Version-$Runtime-full.zip"
$updateZip = Join-Path $Dist "HiveShock-$Version-$Runtime-update.zip"
foreach ($z in @($fullZip, $updateZip)) {
    if (Test-Path $z) { Remove-Item $z -Force }
}

Write-Host "`n==> Comprimiendo..." -ForegroundColor Yellow
Compress-Archive -Path (Join-Path $StageFull "*") -DestinationPath $fullZip -CompressionLevel Optimal
Compress-Archive -Path (Join-Path $StageUpdate "*") -DestinationPath $updateZip -CompressionLevel Optimal

Remove-Item $StageFull -Recurse -Force
Remove-Item $StageUpdate -Recurse -Force

Write-Host "`nListo:" -ForegroundColor Green
Write-Host "  RID:    $Runtime"
Write-Host "  FULL:   $fullZip"
Write-Host "  UPDATE: $updateZip"
Write-Host ""
Write-Host "UPDATE = solo ejecutable (+ CLI si -IncludeCli). Sin profiles ni catalogo."