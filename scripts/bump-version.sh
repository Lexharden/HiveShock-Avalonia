#!/usr/bin/env bash
set -euo pipefail

# ============================================================
# Sube o fija la versión de HiveShock (Directory.Build.props + CHANGELOG).
#
# Uso:
#   ./scripts/bump-version.sh patch
#   ./scripts/bump-version.sh minor
#   ./scripts/bump-version.sh major
#   ./scripts/bump-version.sh 1.2.3
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

if [[ -z "$ARG" ]]; then
  echo "Uso: $0 patch|minor|major|X.Y.Z" >&2
  exit 1
fi

current="$(
  grep -m1 '<Version>' "$PROPS" |
  sed -E 's/.*<Version>([^<]+)<\/Version>.*/\1/' |
  tr -d '[:space:]'
)"

if [[ -z "$current" ]]; then
  echo "ERROR: no hay <Version> en $PROPS" >&2
  exit 1
fi

IFS=. read -r maj min pat <<<"$current"
maj="${maj:-0}"
min="${min:-0}"
pat="${pat:-0}"

case "$ARG" in
  patch) next="$maj.$min.$((pat + 1))" ;;
  minor) next="$maj.$((min + 1)).0" ;;
  major) next="$((maj + 1)).0.0" ;;
  *)
    if [[ ! "$ARG" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
      echo "ERROR: versión inválida '$ARG' (usa X.Y.Z, patch, minor o major)" >&2
      exit 1
    fi
    next="$ARG"
    ;;
esac

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
echo
echo "Siguiente (tú, cuando quieras):"
echo "  git add Directory.Build.props CHANGELOG.md"
echo "  git commit -m \"release: v$next\""
echo "  git tag v$next"
echo "  git push && git push --tags"
echo
echo "El tag v$next dispara el release de Windows, macOS y Linux en GitHub Actions."
