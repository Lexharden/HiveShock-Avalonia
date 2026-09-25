<#
.SYNOPSIS
  Sube o fija la versión de HiveShock (Directory.Build.props + CHANGELOG).

.DESCRIPTION
  No hace git commit ni tag. Admite versiones preliminares SemVer (alpha → beta → rc → final):

    patch | minor | major          1.3.0 → 1.3.1 | 1.4.0 | 2.0.0
    major alpha                    1.3.0 → 2.0.0-alpha.1   (también con patch/minor y beta/rc)
    pre                            2.0.0-alpha.1 → 2.0.0-alpha.2
    beta | rc                      2.0.0-alpha.3 → 2.0.0-beta.1 → 2.0.0-rc.1
    release                        2.0.0-rc.2 → 2.0.0
    X.Y.Z  o  X.Y.Z-etiqueta.N     fija la versión exacta (p. ej. 2.0.0-alpha.1)

.EXAMPLE
  .\scripts\Bump-Version.ps1 patch
  .\scripts\Bump-Version.ps1 major alpha
  .\scripts\Bump-Version.ps1 beta
  .\scripts\Bump-Version.ps1 release
  .\scripts\Bump-Version.ps1 2.0.0-alpha.1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Bump,

    [Parameter(Position = 1)]
    [string]$Pre
)

$ErrorActionPreference = "Stop"
$Stages = @("alpha", "beta", "rc")
$VersionPattern = '^(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z]+(?:\.[0-9A-Za-z]+)*))?$'

$Root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $Root "Directory.Build.props"))) {
    $Root = Split-Path -Parent $Root
}

$propsPath = Join-Path $Root "Directory.Build.props"
$changelogPath = Join-Path $Root "CHANGELOG.md"
# Leer/escribir siempre en UTF-8 explícito: Windows PowerShell 5.1 lee como ANSI los
# archivos sin BOM y estropeaba los acentos de CHANGELOG.md y Directory.Build.props.
$utf8 = New-Object System.Text.UTF8Encoding $false
$props = [System.IO.File]::ReadAllText($propsPath, $utf8)
if ($props -notmatch "<Version>([^<]+)</Version>") {
    throw "No hay <Version> en Directory.Build.props."
}
$current = $Matches[1].Trim()
if ($current -notmatch $VersionPattern) {
    throw "La versión actual '$current' no tiene formato X.Y.Z o X.Y.Z-etiqueta.N."
}
$maj = [int]$Matches[1]; $min = [int]$Matches[2]; $pat = [int]$Matches[3]
$curPre = $Matches[4]
$base = "$maj.$min.$pat"

# Etapa y número de la preliminar actual: "beta.2" → ("beta", 2); "beta" → ("beta", 0).
$curStage = $null; $curNum = 0
if ($curPre) {
    $bits = $curPre.Split(".")
    $curStage = $bits[0].ToLowerInvariant()
    if ($bits.Count -gt 1) { [int]::TryParse($bits[-1], [ref]$curNum) | Out-Null }
}

$usage = "Usa: patch | minor | major [alpha|beta|rc] | pre | alpha | beta | rc | release | X.Y.Z[-etiqueta.N]"

if ($Pre) {
    $Pre = $Pre.ToLowerInvariant()
    if ($Bump -notmatch "^(patch|minor|major)$") {
        throw "El segundo argumento solo va con patch, minor o major (p. ej. 'major alpha'). Para fijar una versión exacta usa solo: .\Bump-Version.ps1 $Pre"
    }
    if ($Stages -notcontains $Pre) {
        throw "'$Pre' no es una etapa. Usa 'major alpha' (o beta/rc), o directamente la versión: .\Bump-Version.ps1 $Pre"
    }
}

switch -Regex ($Bump.ToLowerInvariant()) {
    "^patch$" { $next = "$maj.$min.$($pat + 1)" }
    "^minor$" { $next = "$maj.$($min + 1).0" }
    "^major$" { $next = "$($maj + 1).0.0" }
    "^pre$" {
        if (-not $curPre) { throw "$current es una versión final; no hay preliminar que subir. Ejemplo: .\Bump-Version.ps1 major alpha" }
        $next = "$base-$curStage.$($curNum + 1)"
    }
    "^(alpha|beta|rc)$" {
        $stage = $Matches[1]
        if (-not $curPre) {
            throw "$current es una versión final. Indica qué versión preparas: .\Bump-Version.ps1 major $stage (o minor/patch)."
        }
        if ($Stages.IndexOf($stage) -lt $Stages.IndexOf($curStage)) {
            throw "No se puede volver de '$curStage' a '$stage' (el orden es alpha → beta → rc)."
        }
        $next = if ($stage -eq $curStage) { "$base-$stage.$($curNum + 1)" } else { "$base-$stage.1" }
    }
    "^release$" {
        if (-not $curPre) { throw "$current ya es una versión final." }
        $next = $base
    }
    default {
        if ($Bump -notmatch $VersionPattern) { throw "Versión inválida: '$Bump'. $usage" }
        $next = $Bump
    }
}

if ($Pre) {
    $next = "$next-$Pre.1"
}

if ($next -eq $current) {
    Write-Host "Ya está en $current"
    exit 0
}

$props2 = [regex]::Replace($props, "<Version>[^<]+</Version>", "<Version>$next</Version>", 1)
[System.IO.File]::WriteAllText($propsPath, $props2.TrimEnd() + "`n", $utf8)

$today = Get-Date -Format "yyyy-MM-dd"
$section = "## [$next] - $today`n`n- `n`n"
if (Test-Path $changelogPath) {
    $body = [System.IO.File]::ReadAllText($changelogPath, $utf8)
    $marker = "## [Unreleased]"
    if ($body.Contains($marker)) {
        $body = $body.Replace($marker, "$marker`n`n$($section.TrimEnd())`n")
    }
    else {
        $body = $section + $body
    }
}
else {
    $body = "# Changelog`n`n## [Unreleased]`n`n$section"
}
[System.IO.File]::WriteAllText($changelogPath, $body.TrimEnd() + "`n", $utf8)

Write-Host "Versión: $current → $next"
if ($next.Contains("-")) {
    Write-Host "(Preliminar: GitHub la publicará marcada como pre-release, no como la última estable.)"
}
Write-Host ""
Write-Host "Siguiente (tú, cuando quieras):"
Write-Host "  git add Directory.Build.props CHANGELOG.md"
Write-Host "  git commit -m `"release: v$next`""
Write-Host "  git tag v$next"
Write-Host "  git push && git push --tags"
Write-Host ""
Write-Host "El tag v$next dispara el release de Windows, macOS y Linux en GitHub Actions."
