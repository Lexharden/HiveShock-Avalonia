<#
.SYNOPSIS
  Prepara y publica una versión de HiveShock: comprueba, compila, prueba, hace el commit, el tag y (con -Push) lo sube.

.DESCRIPTION
  Paso previo: subir la versión con .\scripts\Bump-Version.ps1 (mueve lo de [Unreleased] del CHANGELOG a la nueva versión).
  Después este script:
    1. Comprueba la versión y las notas del CHANGELOG (scripts/check_release.py, lo mismo que el workflow).
    2. Exige que solo estén cambiados Directory.Build.props y CHANGELOG.md, y que el tag no exista (local ni remoto).
    3. Compila en Release y pasa los tests (salvo -SkipTests).
    4. Commit «release: vX.Y.Z» y tag anotado vX.Y.Z.
    5. Con -Push: sube la rama y después el tag (el tag dispara .github/workflows/release.yml).

  Las versiones estables se publican desde main; las preliminares (alpha/beta/rc) pueden salir de su rama,
  así main sigue libre para arreglos de la versión estable.

.EXAMPLE
  .\scripts\Bump-Version.ps1 major beta
  .\scripts\Release.ps1            # todo menos subir
  .\scripts\Release.ps1 -Push      # y lo publica
#>
[CmdletBinding()]
param(
    [switch]$Push,
    [switch]$SkipTests,
    [string]$Remote = "origin"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

function Invoke-Native([string]$What, [scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$What falló (código $LASTEXITCODE)." }
}

$python = if (Get-Command python3 -ErrorAction SilentlyContinue) { "python3" } else { "python" }
$utf8 = New-Object System.Text.UTF8Encoding $false
$props = [System.IO.File]::ReadAllText((Join-Path $Root "Directory.Build.props"), $utf8)
if ($props -notmatch "<Version>\s*([^<\s]+)\s*</Version>") { throw "No hay <Version> en Directory.Build.props." }
$version = $Matches[1]
$tag = "v$version"
$isPre = $version.Contains("-")
$branch = (git rev-parse --abbrev-ref HEAD).Trim()

Write-Host "== HiveShock $version ($(if ($isPre) { 'preliminar' } else { 'estable' })) desde '$branch' =="

# 1. Versión y notas.
Invoke-Native "La comprobación del CHANGELOG" { & $python scripts/check_release.py $version }

# 2. Estado de git.
if (-not $isPre -and $branch -ne "main") {
    throw "Una versión estable se publica desde main (estás en '$branch'). Haz el merge primero."
}
$dirty = git status --porcelain --untracked-files=no | ForEach-Object { $_.Substring(3).Trim('"') } |
    Where-Object { $_ -notin @("Directory.Build.props", "CHANGELOG.md") }
if ($dirty) { throw "Hay cambios sin commit además de la versión:`n  $($dirty -join "`n  ")" }
if (git tag --list $tag) { throw "El tag $tag ya existe en local." }
if ($Push) {
    Invoke-Native "git fetch" { git fetch $Remote --tags --quiet }
    if (git ls-remote --tags $Remote "refs/tags/$tag") { throw "El tag $tag ya existe en $Remote." }
    # Sin rama remota todavía no hay nada con qué comparar (la primera subida la crea).
    $upstream = $null
    try { $upstream = git rev-parse --abbrev-ref --symbolic-full-name "@{u}" 2>$null } catch { }
    if ($LASTEXITCODE -eq 0 -and $upstream) {
        $behind = [int](git rev-list --count "HEAD..$upstream")
        if ($behind -gt 0) { throw "La rama va $behind commit(s) por detrás de $upstream. Haz pull antes." }
    }
}

# 3. Compilar y probar (lo mismo que CI).
Invoke-Native "La compilación" { dotnet build HiveShock.Avalonia/HiveShock.Avalonia.csproj -c Release -nologo -v q }
if (-not $SkipTests) {
    Invoke-Native "Los tests" { dotnet test HiveShock.Tests/HiveShock.Tests.csproj -c Release -nologo -v q }
}

# 4. Commit y tag.
git add Directory.Build.props CHANGELOG.md
git diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
    Invoke-Native "git commit" { git commit -m "release: $tag" }
}
Invoke-Native "git tag" { git tag -a $tag -m "HiveShock $version" }
Write-Host "Commit y tag $tag listos."

# 5. Subir: primero la rama (el commit debe existir en remoto) y luego el tag, que dispara el release.
if ($Push) {
    Invoke-Native "git push de la rama" { git push -u $Remote $branch }
    Invoke-Native "git push del tag" { git push $Remote $tag }
    Write-Host "Publicado. Sigue el release en GitHub Actions (workflow «Release»)."
    if ($isPre) { Write-Host "Saldrá como pre-release: solo lo verán quienes tengan activadas las versiones beta." }
}
else {
    Write-Host "Para publicarlo:  git push -u $Remote $branch; git push $Remote $tag"
}
