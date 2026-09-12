<#
.SYNOPSIS
  Sube o fija la versión de HiveShock (Directory.Build.props + CHANGELOG).

.DESCRIPTION
  No hace git commit ni tag.

.EXAMPLE
  .\scripts\Bump-Version.ps1 patch
  .\scripts\Bump-Version.ps1 1.2.3
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Bump
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $Root "Directory.Build.props"))) {
    $Root = Split-Path -Parent $Root
}

$propsPath = Join-Path $Root "Directory.Build.props"
$changelogPath = Join-Path $Root "CHANGELOG.md"
$props = Get-Content -Raw -Path $propsPath
if ($props -notmatch "<Version>([^<]+)</Version>") {
    throw "No hay <Version> en Directory.Build.props."
}
$current = $Matches[1].Trim()
$parts = $current.Split(".")
while ($parts.Count -lt 3) { $parts += "0" }
$maj = [int]$parts[0]; $min = [int]$parts[1]; $pat = [int]$parts[2]

switch -Regex ($Bump) {
    "^patch$" { $next = "$maj.$min.$($pat + 1)" }
    "^minor$" { $next = "$maj.$($min + 1).0" }
    "^major$" { $next = "$($maj + 1).0.0" }
    "^\d+\.\d+\.\d+$" { $next = $Bump }
    default { throw "Usa patch, minor, major o X.Y.Z (recibido: $Bump)." }
}

if ($next -eq $current) {
    Write-Host "Ya está en $current"
    exit 0
}

$props2 = [regex]::Replace($props, "<Version>[^<]+</Version>", "<Version>$next</Version>", 1)
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($propsPath, $props2.TrimEnd() + "`n", $utf8)

$today = Get-Date -Format "yyyy-MM-dd"
$section = "## [$next] - $today`r`n`r`n- `r`n`r`n"
if (Test-Path $changelogPath) {
    $body = Get-Content -Raw -Path $changelogPath
    $marker = "## [Unreleased]"
    if ($body.Contains($marker)) {
        $body = $body.Replace($marker, "$marker`r`n`r`n$($section.TrimEnd())`r`n")
    }
    else {
        $body = $section + $body
    }
}
else {
    $body = "# Changelog`r`n`r`n## [Unreleased]`r`n`r`n$section"
}
[System.IO.File]::WriteAllText($changelogPath, $body.TrimEnd() + "`n", $utf8)

Write-Host "Versión: $current → $next"
Write-Host ""
Write-Host "Siguiente (tú, cuando quieras):"
Write-Host "  git add Directory.Build.props CHANGELOG.md"
Write-Host "  git commit -m `"release: v$next`""
Write-Host "  git tag v$next"
Write-Host "  git push && git push --tags"
Write-Host ""
Write-Host "El tag v$next dispara el release de Windows, macOS y Linux en GitHub Actions."
