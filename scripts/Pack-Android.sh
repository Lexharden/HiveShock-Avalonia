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
mapfile -t files < <(find "$ROOT/HiveShock.Android/bin/Release" -type f \( -name '*.apk' -o -name '*.aab' \) | sort)
for f in "${files[@]:0:4}"; do
  ext="${f##*.}"
  cp -f "$f" "$ROOT/dist/HiveShock-${VER}-android.${ext}"
  echo "  $(basename "$f") -> dist/HiveShock-${VER}-android.${ext}"
done

echo "Listo. No subas el .keystore al git."
