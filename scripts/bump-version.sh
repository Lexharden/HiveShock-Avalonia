#!/usr/bin/env bash
set -euo pipefail

# ============================================================
# Sube o fija la versión de HiveShock (Directory.Build.props + CHANGELOG).
# Admite versiones preliminares SemVer (alpha → beta → rc → final).
#
# Uso:
#   ./scripts/bump-version.sh patch|minor|major     1.3.0 → 1.3.1 | 1.4.0 | 2.0.0
#   ./scripts/bump-version.sh major alpha           1.3.0 → 2.0.0-alpha.1 (también beta/rc)
#   ./scripts/bump-version.sh pre                   2.0.0-alpha.1 → 2.0.0-alpha.2
#   ./scripts/bump-version.sh beta|rc               2.0.0-alpha.3 → 2.0.0-beta.1
#   ./scripts/bump-version.sh release               2.0.0-rc.2 → 2.0.0
#   ./scripts/bump-version.sh 2.0.0-alpha.1         fija la versión exacta
#
# No hace git commit ni tag. Después:
#   git add Directory.Build.props CHANGELOG.md
#   git commit -m "release: vX.Y.Z"
#   git tag vX.Y.Z
#   git push && git push --tags
# ============================================================

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROPS="$ROOT/Directory.Build.props"
CHANGELOG="$ROOT/CHANGELOG.md"
ARG="${1:-}"
PRE="${2:-}"
STAGES=(alpha beta rc)
VERSION_RE='^([0-9]+)\.([0-9]+)\.([0-9]+)(-([0-9A-Za-z]+(\.[0-9A-Za-z]+)*))?$'
USAGE="patch | minor | major [alpha|beta|rc] | pre | alpha | beta | rc | release | X.Y.Z[-etiqueta.N]"

fail() { echo "ERROR: $*" >&2; exit 1; }
stage_index() { local i; for i in "${!STAGES[@]}"; do [[ "${STAGES[$i]}" == "$1" ]] && { echo "$i"; return; }; done; echo -1; }

[[ -n "$ARG" ]] || { echo "Uso: $0 $USAGE" >&2; exit 1; }

current="$(
  grep -m1 '<Version>' "$PROPS" |
  sed -E 's/.*<Version>([^<]+)<\/Version>.*/\1/' |
  tr -d '[:space:]'
)"
[[ -n "$current" ]] || fail "no hay <Version> en $PROPS"
[[ "$current" =~ $VERSION_RE ]] || fail "la versión actual '$current' no tiene formato X.Y.Z o X.Y.Z-etiqueta.N"

maj="${BASH_REMATCH[1]}"; min="${BASH_REMATCH[2]}"; pat="${BASH_REMATCH[3]}"
cur_pre="${BASH_REMATCH[5]:-}"
base="$maj.$min.$pat"

# Etapa y número de la preliminar actual: "beta.2" → beta / 2; "beta" → beta / 0.
cur_stage=""; cur_num=0
if [[ -n "$cur_pre" ]]; then
  cur_stage="$(echo "${cur_pre%%.*}" | tr '[:upper:]' '[:lower:]')"
  last="${cur_pre##*.}"
  [[ "$cur_pre" == *.* && "$last" =~ ^[0-9]+$ ]] && cur_num="$last"
fi

if [[ -n "$PRE" ]]; then
  PRE="$(echo "$PRE" | tr '[:upper:]' '[:lower:]')"
  [[ "$ARG" =~ ^(patch|minor|major)$ ]] ||
    fail "el segundo argumento solo va con patch, minor o major (p. ej. 'major alpha'). Para fijar una versión exacta usa solo: $0 $PRE"
  [[ "$(stage_index "$PRE")" -ge 0 ]] ||
    fail "'$PRE' no es una etapa. Usa 'major alpha' (o beta/rc), o directamente la versión: $0 $PRE"
fi

case "$(echo "$ARG" | tr '[:upper:]' '[:lower:]')" in
  patch) next="$maj.$min.$((pat + 1))" ;;
  minor) next="$maj.$((min + 1)).0" ;;
  major) next="$((maj + 1)).0.0" ;;
  pre)
    [[ -n "$cur_pre" ]] || fail "$current es una versión final; no hay preliminar que subir. Ejemplo: $0 major alpha"
    next="$base-$cur_stage.$((cur_num + 1))"
    ;;
  alpha|beta|rc)
    stage="$(echo "$ARG" | tr '[:upper:]' '[:lower:]')"
    [[ -n "$cur_pre" ]] || fail "$current es una versión final. Indica qué versión preparas: $0 major $stage (o minor/patch)."
    [[ "$(stage_index "$stage")" -ge "$(stage_index "$cur_stage")" ]] ||
      fail "no se puede volver de '$cur_stage' a '$stage' (el orden es alpha → beta → rc)."
    if [[ "$stage" == "$cur_stage" ]]; then next="$base-$stage.$((cur_num + 1))"; else next="$base-$stage.1"; fi
    ;;
  release)
    [[ -n "$cur_pre" ]] || fail "$current ya es una versión final."
    next="$base"
    ;;
  *)
    [[ "$ARG" =~ $VERSION_RE ]] || fail "versión inválida '$ARG'. Usa: $USAGE"
    next="$ARG"
    ;;
esac

[[ -z "$PRE" ]] || next="$next-$PRE.1"

if [[ "$next" == "$current" ]]; then
  echo "Ya está en $current"
  exit 0
fi

tmp="$(mktemp "${TMPDIR:-/tmp}/hiveshock-ver.XXXXXX")"
python3 - "$PROPS" "$CHANGELOG" "$current" "$next" "$tmp" <<'PY'
import datetime
import pathlib
import re
import sys

props_path, changelog_path, current, next_ver, tmp_path = sys.argv[1:6]
props = pathlib.Path(props_path)
text = props.read_text(encoding="utf-8")
new_text, n = re.subn(
    r"(<Version>)[^<]+(</Version>)",
    rf"\g<1>{next_ver}\g<2>",
    text,
    count=1,
)
if n != 1:
    raise SystemExit("No se pudo actualizar <Version> en Directory.Build.props")
props.write_text(new_text, encoding="utf-8", newline="\n")

today = datetime.date.today().isoformat()
section = f"## [{next_ver}] - {today}\n\n- \n\n"
clog = pathlib.Path(changelog_path)
if clog.exists():
    body = clog.read_text(encoding="utf-8")
    marker = "## [Unreleased]"
    if marker in body:
        body = body.replace(marker, marker + "\n\n" + section.rstrip() + "\n", 1)
    else:
        body = section + body
else:
    body = (
        "# Changelog\n\n"
        "All notable changes to HiveShock are documented here.\n\n"
        "## [Unreleased]\n\n"
        + section
    )
clog.write_text(body, encoding="utf-8", newline="\n")
pathlib.Path(tmp_path).write_text(next_ver, encoding="utf-8")
PY

rm -f "$tmp"

echo "Versión: $current → $next"
if [[ "$next" == *-* ]]; then
  echo "(Preliminar: GitHub la publicará marcada como pre-release, no como la última estable.)"
fi
echo
echo "Siguiente (tú, cuando quieras):"
echo "  git add Directory.Build.props CHANGELOG.md"
echo "  git commit -m \"release: v$next\""
echo "  git tag v$next"
echo "  git push && git push --tags"
echo
echo "El tag v$next dispara el release de Windows, macOS y Linux en GitHub Actions."
