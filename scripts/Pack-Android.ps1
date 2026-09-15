<#
.SYNOPSIS
  Publica HiveShock Android (APK / AAB) según docs.avaloniaui.net/docs/deployment/android

.DESCRIPTION
  Debug usa el keystore de .NET. Release firma si existen:
    ANDROID_SIGNING_KEYSTORE  (ruta al .keystore)
    ANDROID_SIGNING_ALIAS
    ANDROID_SIGNING_PASSWORD  (se pasa como env:, no en claro en logs de script si ya está en el entorno)

  Solo copia APKs de la carpeta publish/ (no intermedios de bin/).

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
$binRelease = Join-Path $Root "HiveShock.Android\bin\Release"
$objRelease = Join-Path $Root "HiveShock.Android\obj\Release"
if (Test-Path $binRelease) { Remove-Item -Recurse -Force $binRelease }
if (Test-Path $objRelease) { Remove-Item -Recurse -Force $objRelease }

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

$publish = Join-Path $Root "HiveShock.Android\bin\Release\net10.0-android\publish"
if (-not (Test-Path $publish)) {
    $publish = Get-ChildItem -Recurse (Join-Path $Root "HiveShock.Android\bin\Release") -Directory -Filter publish |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $publish -or -not (Test-Path $publish)) {
    throw "No hay carpeta publish."
}

$files = Get-ChildItem $publish -File -Include *.apk, *.aab |
    Sort-Object { if ($_.Name -like '*-Signed.apk') { 0 } else { 1 } }, Name

if (-not $files) {
    throw "No hay APK/AAB en $publish"
}

foreach ($f in $files) {
    $destName = if ($f.Name -like '*-Signed.apk') {
        "HiveShock-$Version-android-signed.apk"
    } else {
        "HiveShock-$Version-android$($f.Extension)"
    }
    $dest = Join-Path $dist $destName
    Copy-Item $f.FullName $dest -Force
    Write-Host "  $($f.Name) -> $dest"
}

Write-Host "Listo. Instala el de dist/ (desinstala la app anterior antes)."
Write-Host "  adb uninstall dev.yafel.hiveshock"
Write-Host "  adb install -r dist\HiveShock-$Version-android.apk"
