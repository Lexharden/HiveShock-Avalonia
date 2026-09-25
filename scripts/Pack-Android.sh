#!/usr/bin/env bash
set -euo pipefail

# Publica HiveShock Android (APK/AAB). Ver docs.avaloniaui.net/docs/deployment/android
# Firma Release si ANDROID_SIGNING_KEYSTORE, ANDROID_SIGNING_ALIAS y ANDROID_SIGNING_PASSWORD están definidos.

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
PROPS="$ROOT/Directory.Build.props"
PROJ="$ROOT/HiveShock.Android/HiveShock.Android.csproj"
VER="${1:-}"

if [[ -z "$VER" ]]; then
  VER="$(grep -m1 '<Version>' "$PROPS" | sed -E 's/.*<Version>([^<]+)<\/Version>.*/\1/' | tr -d '[:space:]')"
fi

# Limpia salidas viejas para no instalar un APK intermedio sin libs nativas.
rm -rf "$ROOT/HiveShock.Android/bin/Release" "$ROOT/HiveShock.Android/obj/Release"

args=(publish "$PROJ" -f net10.0-android -c Release)
if [[ -n "${ANDROID_SIGNING_KEYSTORE:-}" && -n "${ANDROID_SIGNING_ALIAS:-}" && -n "${ANDROID_SIGNING_PASSWORD:-}" ]]; then
  args+=(
    -p:AndroidKeyStore=true
    -p:AndroidSigningKeyStore="$ANDROID_SIGNING_KEYSTORE"
    -p:AndroidSigningKeyAlias="$ANDROID_SIGNING_ALIAS"
    -p:AndroidSigningKeyPass=env:ANDROID_SIGNING_PASSWORD
    -p:AndroidSigningStorePass=env:ANDROID_SIGNING_PASSWORD
  )
fi

dotnet "${args[@]}"

mkdir -p "$ROOT/dist"
PUBLISH_DIR="$ROOT/HiveShock.Android/bin/Release/net10.0-android/publish"
if [[ ! -d "$PUBLISH_DIR" ]]; then
  # Algunas versiones del SDK usan RID en la ruta
  PUBLISH_DIR="$(find "$ROOT/HiveShock.Android/bin/Release" -type d -name publish | head -n1 || true)"
fi
if [[ -z "${PUBLISH_DIR:-}" || ! -d "$PUBLISH_DIR" ]]; then
  echo "No hay carpeta publish. Revisa el log de dotnet publish." >&2
  exit 1
fi

# Preferir el APK firmado / de publish; nunca copiar intermedios sueltos de bin/.
mapfile -t files < <(find "$PUBLISH_DIR" -maxdepth 1 -type f \( -name '*-Signed.apk' -o -name '*.apk' -o -name '*.aab' \) | sort)
if [[ ${#files[@]} -eq 0 ]]; then
  echo "No hay APK/AAB en $PUBLISH_DIR" >&2
  exit 1
fi

for f in "${files[@]}"; do
  ext="${f##*.}"
  base="$(basename "$f")"
  if [[ "$base" == *-Signed.apk ]]; then
    dest="$ROOT/dist/HiveShock-${VER}-android-signed.apk"
  else
    dest="$ROOT/dist/HiveShock-${VER}-android.${ext}"
  fi
  cp -f "$f" "$dest"
  echo "  $base -> $dest"
done

echo "Listo. Instala el de dist/ (desinstala la app anterior antes)."
echo "  adb uninstall dev.yafel.hiveshock"
echo "  adb install -r dist/HiveShock-${VER}-android.apk"
