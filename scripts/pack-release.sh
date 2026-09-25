#!/usr/bin/env bash
set -euo pipefail

# ============================================================
# HiveShock Release Packager
#
# Uso:
#   ./scripts/pack-release.sh
#   ./scripts/pack-release.sh --cli
#   ./scripts/pack-release.sh --runtime osx-arm64
#   RUNTIME=osx-arm64 ./scripts/pack-release.sh
#
# macOS:
#   - Crea HiveShock.app
#   - Genera AppIcon.icns
#   - Firma ad-hoc (inside-out, hardened runtime)
#   - Verifica la firma (codesign --verify --deep --strict)
#   - Genera ZIP + DMG (arrastrar a Aplicaciones)
#
# ============================================================

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

INCLUDE_CLI=0
SKIP_PUBLISH=0
VERSION=""
RUNTIME="${RUNTIME:-}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --cli)
      INCLUDE_CLI=1
      ;;
    --skip-publish)
      SKIP_PUBLISH=1
      ;;
    --runtime)
      RUNTIME="$2"
      shift
      ;;
    --version)
      VERSION="$2"
      shift
      ;;
    *)
      echo "Opción desconocida: $1" >&2
      exit 1
      ;;
  esac

  shift
done

# ============================================================
# Detect runtime del host
# ============================================================

host_runtime() {
  local arch
  arch="$(uname -m)"

  case "$(uname -s)" in
    Darwin)
      if [[ "$arch" == "arm64" ]]; then
        echo "osx-arm64"
      else
        echo "osx-x64"
      fi
      ;;

    Linux)
      if [[ "$arch" == "aarch64" || "$arch" == "arm64" ]]; then
        echo "linux-arm64"
      else
        echo "linux-x64"
      fi
      ;;

    *)
      echo "win-x64"
      ;;
  esac
}

if [[ -z "$RUNTIME" ]]; then
  RUNTIME="$(host_runtime)"
fi

# ============================================================
# Version
# ============================================================

read_project_version() {
  local f v
  for f in \
    "$ROOT/Directory.Build.props" \
    "$ROOT/HiveShock.Avalonia/HiveShock.Avalonia.csproj"
  do
    [[ -f "$f" ]] || continue
    v="$(
      grep -m1 '<Version>' "$f" |
      sed -E 's/.*<Version>([^<]+)<\/Version>.*/\1/' |
      tr -d '[:space:]'
    )"
    if [[ -n "$v" ]]; then
      echo "$v"
      return 0
    fi
  done
  echo "0.0.0"
}

if [[ -z "$VERSION" ]]; then
  VERSION="$(read_project_version)"
fi

# ============================================================
# Paths
# ============================================================

PUBLISH_GUI="$ROOT/publish-gui"
PUBLISH_CLI="$ROOT/publish-cli"

DIST="$ROOT/dist"

STAGE_FULL="$DIST/_stage-full"
STAGE_UPDATE="$DIST/_stage-update"

PLIST_TPL="$ROOT/scripts/macos/Info.plist"
ENTITLEMENTS="$ROOT/scripts/macos/HiveShock.entitlements"
BUNDLE_ID="dev.yafel.hiveshock"
OPEN_RTF="$ROOT/scripts/macos/Como-abrir-HiveShock.rtf"

# Imagen fuente para macOS
ICON_SRC="$ROOT/HiveShock.Avalonia/Assets/logo-dark.png"

echo
echo "=============================================="
echo " HiveShock Release"
echo "=============================================="
echo "Version : $VERSION"
echo "RID     : $RUNTIME"
echo "Root    : $ROOT"
echo "=============================================="
echo

# ============================================================
# Publish
# ============================================================

if [[ "$SKIP_PUBLISH" -eq 0 ]]; then

  echo "==> Publish GUI ($RUNTIME)..."

  rm -rf "$PUBLISH_GUI"

  dotnet publish \
    "$ROOT/HiveShock.Avalonia/HiveShock.Avalonia.csproj" \
    -c Release \
    -r "$RUNTIME" \
    --self-contained true \
    -o "$PUBLISH_GUI"

  if [[ "$INCLUDE_CLI" -eq 1 ]]; then

    echo
    echo "==> Publish CLI ($RUNTIME)..."

    rm -rf "$PUBLISH_CLI"

    dotnet publish \
      "$ROOT/HiveShock.Cli/HiveShock.Cli.csproj" \
      -c Release \
      -r "$RUNTIME" \
      --self-contained true \
      -o "$PUBLISH_CLI"

  fi

fi

# ============================================================
# Detect GUI executable
# ============================================================

if [[ -f "$PUBLISH_GUI/HiveShock.exe" ]]; then

  GUI_NAME="HiveShock.exe"

elif [[ -f "$PUBLISH_GUI/HiveShock" ]]; then

  GUI_NAME="HiveShock"

else

  echo "ERROR: No se encontró HiveShock en:"
  echo "       $PUBLISH_GUI"
  exit 1

fi

# ============================================================
# Runtime type
# ============================================================

IS_OSX=0

if [[ "$RUNTIME" == osx-* ]]; then
  IS_OSX=1
fi

# ============================================================
# Launch / replace information
# ============================================================

if [[ "$IS_OSX" -eq 1 ]]; then

  LAUNCH="HiveShock.app"
  REPLACE="HiveShock.app"

elif [[ "$GUI_NAME" == *.exe ]]; then

  LAUNCH="HiveShock.exe"
  REPLACE="HiveShock.exe"

else

  LAUNCH="./HiveShock"
  REPLACE="los archivos de tu carpeta de HiveShock por los de este zip (no trae perfiles ni catálogo: no se pisan)."

fi

# ============================================================
# README
# ============================================================

write_leeme() {
  local tpl="$1"
  local out="$2"

  sed \
    -e "s/{VERSION}/$VERSION/g" \
    -e "s|{LAUNCH}|$LAUNCH|g" \
    -e "s|{REPLACE}|$REPLACE|g" \
    "$tpl" |
    tr -d '\r' > "$out"
}

# ============================================================
# Generate .icns
# ============================================================

make_icns() {
  local dest="$1"

  if [[ ! -f "$ICON_SRC" ]]; then
    echo "WARNING: No existe icon source:"
    echo "         $ICON_SRC"
    return 0
  fi

  if ! command -v sips >/dev/null 2>&1; then
    echo "WARNING: sips no disponible."
    return 0
  fi

  if ! command -v iconutil >/dev/null 2>&1; then
    echo "WARNING: iconutil no disponible."
    return 0
  fi

  local tmpdir
  local iconset
  local size

  tmpdir="$(mktemp -d)"
  iconset="$tmpdir/HiveShock.iconset"

  mkdir -p "$iconset"

  echo "==> Generando AppIcon.icns..."

  for size in 16 32 128 256 512; do

    sips \
      -z "$size" "$size" \
      "$ICON_SRC" \
      --out "$iconset/icon_${size}x${size}.png" \
      >/dev/null

    sips \
      -z $((size * 2)) $((size * 2)) \
      "$ICON_SRC" \
      --out "$iconset/icon_${size}x${size}@2x.png" \
      >/dev/null

  done

  iconutil \
    -c icns \
    "$iconset" \
    -o "$dest"

  rm -rf "$tmpdir"
}

# ============================================================
# Ad-hoc signing
# ============================================================

is_macho() {
  file -b "$1" 2>/dev/null | grep -q 'Mach-O'
}

sign_macos_binary() {
  local target="$1"
  shift

  codesign \
    --force \
    --sign - \
    --timestamp=none \
    "$@" \
    "$target"
}

sign_macos_app() {
  local app="$1"
  local main="$app/Contents/MacOS/HiveShock"
  local nested
  local other_count=0

  if ! command -v codesign >/dev/null 2>&1; then
    echo "ERROR: codesign no está disponible."
    exit 1
  fi

  if [[ ! -f "$ENTITLEMENTS" ]]; then
    echo "ERROR: No existe $ENTITLEMENTS"
    exit 1
  fi

  if [[ ! -f "$main" ]]; then
    echo "ERROR: No se encontró HiveShock executable."
    exit 1
  fi

  local ent_file
  ent_file="$(mktemp "${TMPDIR:-/tmp}/hiveshock-ent.XXXXXX")"
  tr -d '\r' < "$ENTITLEMENTS" > "$ent_file"

  echo
  echo "==> Firmando ad-hoc..."

  if command -v xattr >/dev/null 2>&1; then
    xattr -cr "$app" || true
  fi

  # Managed .dll from `dotnet publish` often have +x. Strip it so they are
  # not the only reason codesign treats them as nested code.
  while IFS= read -r -d '' nested; do
    is_macho "$nested" && continue
    chmod a-x "$nested" || true
  done < <(find "$app/Contents" -type f -print0)

  chmod +x "$main" || true

  while IFS= read -r -d '' nested; do
    codesign --remove-signature "$nested" 2>/dev/null || true
  done < <(find "$app/Contents" -type f -print0)

  # Nested Mach-O (dylibs, createdump, CLI). Unique id per file.
  # Do not apply app entitlements to libraries.
  # Do not sign Contents/MacOS/HiveShock here: codesign treats that path as
  # the whole bundle.
  # Other files in Contents/MacOS (managed .dll, json, txt) also count as
  # nested code on recent macOS and must be signed or the bundle fails.
  while IFS= read -r -d '' nested; do
    [[ "$nested" == "$main" ]] && continue

    if is_macho "$nested"; then
      echo "    nested: ${nested#"$app/"}"
      sign_macos_binary "$nested" --options runtime
    else
      sign_macos_binary "$nested" 2>/dev/null
      other_count=$((other_count + 1))
    fi
  done < <(find "$app/Contents/MacOS" -type f -print0)

  echo "    nested data files: $other_count"

  # Bundle last, no --deep: nested binaries and dlls are already signed.
  echo "    bundle: HiveShock.app"
  sign_macos_binary \
    "$app" \
    --options runtime \
    --identifier "$BUNDLE_ID" \
    --entitlements "$ent_file"

  rm -f "$ent_file"
}

verify_macos_app() {
  local app="$1"

  echo
  echo "==> Verificando firma..."

  if ! codesign --verify --deep --strict "$app"; then
    echo "ERROR: codesign --verify --deep --strict falló."
    codesign -dv --verbose=4 "$app" || true
    exit 1
  fi

  codesign \
    -dv \
    --verbose=4 \
    "$app" \
    2>&1 |
    grep -E \
      'Identifier=|Format=|CodeDirectory|Signature=|TeamIdentifier=|Info.plist|Sealed Resources' \
      || true

  echo
  echo "    Firma ad-hoc válida (spctl --assess fallará: es esperado sin Developer ID)."
}

make_macos_dmg() {
  local app="$1"
  local dmg="$2"
  local stage
  local volname="HiveShock $VERSION"

  echo
  echo "==> Generando DMG..."

  if ! command -v hdiutil >/dev/null 2>&1; then
    echo "ERROR: hdiutil no está disponible."
    exit 1
  fi

  stage="$(mktemp -d "${TMPDIR:-/tmp}/hiveshock-dmg.XXXXXX")"

  ditto "$app" "$stage/HiveShock.app"

  if [[ -f "$OPEN_RTF" ]]; then
    cp "$OPEN_RTF" "$stage/Cómo abrir HiveShock.rtf"
  fi

  if command -v osascript >/dev/null 2>&1; then
    osascript \
      -e 'tell application "Finder"' \
      -e "make alias file to POSIX file \"/Applications\" at POSIX file \"$stage\"" \
      -e "set name of result to \"Aplicaciones\"" \
      -e 'end tell' \
      >/dev/null \
      || ln -s /Applications "$stage/Aplicaciones"
  else
    ln -s /Applications "$stage/Aplicaciones"
  fi

  rm -f "$dmg"

  hdiutil create \
    -volname "$volname" \
    -srcfolder "$stage" \
    -ov \
    -format UDZO \
    -fs HFS+ \
    "$dmg" \
    >/dev/null

  rm -rf "$stage"

  echo "    Firmando DMG ad-hoc..."
  codesign \
    --force \
    --sign - \
    --timestamp=none \
    --identifier "$BUNDLE_ID.dmg" \
    "$dmg"

  codesign --verify "$dmg"
}

# ============================================================
# Prepare stages
# ============================================================

rm -rf \
  "$STAGE_FULL" \
  "$STAGE_UPDATE"

mkdir -p \
  "$STAGE_FULL" \
  "$STAGE_UPDATE" \
  "$DIST"

# ============================================================
# FULL
# ============================================================

echo
echo "=============================================="
echo " Empaquetando FULL"
echo "=============================================="

if [[ "$IS_OSX" -eq 1 ]]; then

  APP="$STAGE_FULL/HiveShock.app"

  mkdir -p \
    "$APP/Contents/MacOS" \
    "$APP/Contents/Resources"

  echo "==> Copiando publish dentro de .app..."

  ditto \
    "$PUBLISH_GUI" \
    "$APP/Contents/MacOS"

  chmod +x \
    "$APP/Contents/MacOS/HiveShock" \
    || true

  # CLI opcional
  if [[ "$INCLUDE_CLI" -eq 1 && -f "$PUBLISH_CLI/HiveShock.Cli" ]]; then

    echo "==> Incluyendo CLI..."

    cp \
      "$PUBLISH_CLI/HiveShock.Cli" \
      "$APP/Contents/MacOS/"

    chmod +x \
      "$APP/Contents/MacOS/HiveShock.Cli" \
      || true

  fi

  # Info.plist
  echo "==> Generando Info.plist..."

  # CFBundleShortVersionString/CFBundleVersion solo admiten números: 2.0.0-beta.1 → 2.0.0.
  sed \
    "s/{VERSION}/${VERSION%%-*}/g" \
    "$PLIST_TPL" \
    > "$APP/Contents/Info.plist"

  # PkgInfo
  printf 'APPL????' \
    > "$APP/Contents/PkgInfo"

  # Icon
  make_icns \
    "$APP/Contents/Resources/AppIcon.icns"

  # ----------------------------------------------------------
  # Firma AD-HOC
  # ----------------------------------------------------------

  sign_macos_app "$APP"

  verify_macos_app "$APP"

else

  cp -R \
    "$PUBLISH_GUI"/. \
    "$STAGE_FULL/"

  if [[ -f "$STAGE_FULL/HiveShock" ]]; then
    chmod +x "$STAGE_FULL/HiveShock" || true
  fi

  if [[ "$INCLUDE_CLI" -eq 1 ]]; then

    if [[ -f "$PUBLISH_CLI/HiveShock.Cli.exe" ]]; then

      cp \
        "$PUBLISH_CLI/HiveShock.Cli.exe" \
        "$STAGE_FULL/"

    elif [[ -f "$PUBLISH_CLI/HiveShock.Cli" ]]; then

      cp \
        "$PUBLISH_CLI/HiveShock.Cli" \
        "$STAGE_FULL/"

    fi

  fi

fi

write_leeme \
  "$ROOT/scripts/templates/LEEME-full.txt" \
  "$STAGE_FULL/LEEME.txt"

# ============================================================
# UPDATE
# ============================================================

echo
echo "=============================================="
echo " Empaquetando UPDATE"
echo "=============================================="

if [[ "$IS_OSX" -eq 1 ]]; then

  # En macOS actualizamos el bundle completo.
  # NO solamente Contents/MacOS/HiveShock.
  # ditto preserva el sello de codesign; cp -R puede romperlo.

  ditto \
    "$STAGE_FULL/HiveShock.app" \
    "$STAGE_UPDATE/HiveShock.app"

elif [[ "$GUI_NAME" == *.exe ]]; then

  cp \
    "$PUBLISH_GUI/$GUI_NAME" \
    "$STAGE_UPDATE/"

else

  # Linux no publica en un solo archivo: el ejecutable necesita sus librerías.
  # Se copia todo el programa menos los datos del usuario, que no se deben pisar.
  cp -R \
    "$PUBLISH_GUI"/. \
    "$STAGE_UPDATE/"

  rm -rf \
    "$STAGE_UPDATE/profiles" \
    "$STAGE_UPDATE/gifts-images" \
    "$STAGE_UPDATE/gift-catalog.json" \
    "$STAGE_UPDATE/active-profile.txt" \
    "$STAGE_UPDATE/.env.example"

  chmod +x "$STAGE_UPDATE/HiveShock" || true

  if [[ "$INCLUDE_CLI" -eq 1 ]]; then

    if [[ -f "$PUBLISH_CLI/HiveShock.Cli.exe" ]]; then

      cp \
        "$PUBLISH_CLI/HiveShock.Cli.exe" \
        "$STAGE_UPDATE/"

    elif [[ -f "$PUBLISH_CLI/HiveShock.Cli" ]]; then

      cp \
        "$PUBLISH_CLI/HiveShock.Cli" \
        "$STAGE_UPDATE/"

    fi

  fi

fi

write_leeme \
  "$ROOT/scripts/templates/LEEME-update.txt" \
  "$STAGE_UPDATE/LEEME.txt"

# ============================================================
# ZIP
# ============================================================

FULL_ZIP="$DIST/HiveShock-$VERSION-$RUNTIME-full.zip"
UPDATE_ZIP="$DIST/HiveShock-$VERSION-$RUNTIME-update.zip"
FULL_DMG=""

rm -f \
  "$FULL_ZIP" \
  "$UPDATE_ZIP"

echo
echo "=============================================="
echo " Comprimiendo"
echo "=============================================="

if command -v ditto >/dev/null 2>&1; then

  ditto \
    -c \
    -k \
    --sequesterRsrc \
    "$STAGE_FULL" \
    "$FULL_ZIP"

  ditto \
    -c \
    -k \
    --sequesterRsrc \
    "$STAGE_UPDATE" \
    "$UPDATE_ZIP"

else

  (
    cd "$STAGE_FULL"
    zip -r -q "$FULL_ZIP" .
  )

  (
    cd "$STAGE_UPDATE"
    zip -r -q "$UPDATE_ZIP" .
  )

fi

# ============================================================
# DMG (macOS only)
# ============================================================

if [[ "$IS_OSX" -eq 1 ]]; then
  FULL_DMG="$DIST/HiveShock-$VERSION-$RUNTIME.dmg"
  rm -f "$FULL_DMG"
  make_macos_dmg "$STAGE_FULL/HiveShock.app" "$FULL_DMG"
fi

# ============================================================
# Cleanup
# ============================================================

rm -rf \
  "$STAGE_FULL" \
  "$STAGE_UPDATE"

# ============================================================
# Result
# ============================================================

echo
echo "=============================================="
echo " RELEASE LISTO"
echo "=============================================="
echo
echo "Version : $VERSION"
echo "RID     : $RUNTIME"
echo
echo "FULL:"
echo "  $FULL_ZIP"
echo
echo "UPDATE:"
echo "  $UPDATE_ZIP"
echo

if [[ "$IS_OSX" -eq 1 ]]; then

  echo "DMG (pásalo a otras Mac):"
  echo "  $FULL_DMG"
  echo
  echo "Firma:"
  echo "  AD-HOC"
  echo
  echo "En otra Mac:"
  echo "  Abre el DMG → arrastra HiveShock a Aplicaciones"
  echo "  Clic derecho → Abrir (solo la primera vez)"
  echo "  Tahoe: Ajustes → Privacidad y seguridad → Abrir de todos modos"
  echo
  echo "=============================================="

fi