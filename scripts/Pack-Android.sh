#!/usr/bin/env bash
set -euo pipefail

# Publica HiveShock Android (APK/AAB). Ver docs.avaloniaui.net/docs/deployment/android
# Firma Release si ANDROID_SIGNING_KEYSTORE, ANDROID_SIGNING_ALIAS y ANDROID_SIGNING_PASSWORD están definidos.
# ANDROID_REQUIRE_SIGNING=1 (lo usa el release) falla si falta la firma: un APK con otra clave no actualiza al instalado.
# Salida: dist/HiveShock-<ver>-android.apk (el mismo nombre que busca el aviso de versión nueva de la app).

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
if [[ "${ANDROID_REQUIRE_SIGNING:-0}" == "1" ]] && \
   [[ -z "${ANDROID_SIGNING_KEYSTORE:-}" || -z "${ANDROID_SIGNING_ALIAS:-}" || -z "${ANDROID_SIGNING_PASSWORD:-}" ]]; then
  echo "ERROR: ANDROID_REQUIRE_SIGNING=1 pero faltan ANDROID_SIGNING_KEYSTORE/ALIAS/PASSWORD." >&2
  exit 1
fi
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

# Un solo APK por versión: el firmado si existe; si no, el de publish (firmado con la clave de depuración).
SIGNED="$(find "$PUBLISH_DIR" -maxdepth 1 -type f -name '*-Signed.apk' | sort | head -n1 || true)"
APK="${SIGNED:-$(find "$PUBLISH_DIR" -maxdepth 1 -type f -name '*.apk' | sort | head -n1 || true)}"
if [[ -z "$APK" ]]; then
  echo "No hay APK en $PUBLISH_DIR" >&2
  exit 1
fi
if [[ -z "$SIGNED" && -z "${ANDROID_SIGNING_KEYSTORE:-}" ]]; then
  echo "AVISO: sin keystore de release; este APK usa la clave de depuración y no actualizará sobre uno firmado distinto." >&2
fi

dest="$ROOT/dist/HiveShock-${VER}-android.apk"
cp -f "$APK" "$dest"
echo "  $(basename "$APK") -> $dest"

echo "Listo. Instala el de dist/ (desinstala la app anterior antes)."
echo "  adb uninstall dev.yafel.hiveshock"
echo "  adb install -r dist/HiveShock-${VER}-android.apk"
