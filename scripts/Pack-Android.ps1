<#
.SYNOPSIS
  Publica HiveShock Android (APK / AAB) según docs.avaloniaui.net/docs/deployment/android

.DESCRIPTION
  Debug usa el keystore de .NET. Release firma si existen:
    ANDROID_SIGNING_KEYSTORE  (ruta al .keystore)
    ANDROID_SIGNING_ALIAS
    ANDROID_SIGNING_PASSWORD  (se pasa como env:, no en claro en logs de script si ya está en el entorno)

  Salida: dist\HiveShock-<ver>-android.apk (y .aab si el SDK lo genera)

.EXAMPLE
  .\scripts\Pack-Android.ps1
#>
[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $Root "HiveShock.Android\HiveShock.Android.csproj"))) {
    throw "Ejecuta este script desde el repo HiveShock-Avalonia."
}

Set-Location $Root

if (-not $Version) {
    $props = Get-Content -Raw (Join-Path $Root "Directory.Build.props")
    if ($props -notmatch "<Version>([^<]+)</Version>") {
        throw "No hay Version en Directory.Build.props"
    }
    $Version = $Matches[1].Trim()
}

$proj = Join-Path $Root "HiveShock.Android\HiveShock.Android.csproj"
$args = @(
    "publish", $proj,
    "-f", "net10.0-android",
    "-c", "Release"
)

$store = $env:ANDROID_SIGNING_KEYSTORE
$alias = $env:ANDROID_SIGNING_ALIAS
$pass = $env:ANDROID_SIGNING_PASSWORD
if ($store -and $alias -and $pass) {
    $args += @(
        "-p:AndroidKeyStore=true",
        "-p:AndroidSigningKeyStore=$store",
        "-p:AndroidSigningKeyAlias=$alias",
        "-p:AndroidSigningKeyPass=env:ANDROID_SIGNING_PASSWORD",
        "-p:AndroidSigningStorePass=env:ANDROID_SIGNING_PASSWORD"
    )
}

Write-Host "dotnet $($args -join ' ')"
& dotnet @args
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish Android falló."
}

$dist = Join-Path $Root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$publish = Get-ChildItem -Recurse (Join-Path $Root "HiveShock.Android\bin\Release") -Include *.apk, *.aab |
    Where-Object { $_.FullName -match "publish|net10.0-android" } |
    Sort-Object LastWriteTime -Descending

if (-not $publish) {
    $publish = Get-ChildItem -Recurse (Join-Path $Root "HiveShock.Android\bin\Release") -Include *.apk, *.aab |
        Sort-Object LastWriteTime -Descending
}

foreach ($f in $publish | Select-Object -First 4) {
    $dest = Join-Path $dist ("HiveShock-{0}-android{1}" -f $Version, $f.Extension)
    Copy-Item $f.FullName $dest -Force
    Write-Host "  $($f.Name) -> $dest"
}

Write-Host "Listo. No subas el .keystore al git."
