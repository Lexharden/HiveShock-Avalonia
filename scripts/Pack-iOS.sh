#!/usr/bin/env bash
set -euo pipefail

# Empaqueta HiveShock para iOS como .ipa SIN FIRMAR. Requiere macOS con Xcode y el workload ios.
#   dotnet workload install ios
# Sin cuenta de pago de Apple no se puede distribuir un .ipa firmado: cada usuario lo instala con su Apple ID
# usando AltStore o Sideloadly, que lo firman al instalar (la firma gratuita dura 7 días).
# Salida: dist/HiveShock-<ver>-ios-unsigned.ipa

if [[ "$(uname)" != "Darwin" ]]; then
  echo "ERROR: el empaquetado de iOS solo funciona en macOS (Xcode)." >&2
  exit 1
fi

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
PROPS="$ROOT/Directory.Build.props"
PROJ="$ROOT/HiveShock.iOS/HiveShock.iOS.csproj"
VER="${1:-}"

if [[ -z "$VER" ]]; then
  VER="$(grep -m1 '<Version>' "$PROPS" | sed -E 's/.*<Version>([^<]+)<\/Version>.*/\1/' | tr -d '[:space:]')"
fi

rm -rf "$ROOT/HiveShock.iOS/bin/Release" "$ROOT/HiveShock.iOS/obj/Release"

# Sin firma: el .app resultante se mete a mano en Payload/. EnableCodeSigning=false evita pedir certificado.
dotnet build "$PROJ" -f net10.0-ios -c Release -r ios-arm64 \
  -p:EnableCodeSigning=false \
  -p:CodesignRequireProvisioningProfile=false \
  -p:BuildIpa=false

APP="$(find "$ROOT/HiveShock.iOS/bin/Release" -type d -name '*.app' | head -n1 || true)"
if [[ -z "$APP" ]]; then
  echo "ERROR: no se generó ningún .app en HiveShock.iOS/bin/Release. Revisa el log de dotnet build." >&2
  exit 1
fi

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
mkdir -p "$STAGE/Payload"
cp -R "$APP" "$STAGE/Payload/"

mkdir -p "$ROOT/dist"
DEST="$ROOT/dist/HiveShock-${VER}-ios-unsigned.ipa"
rm -f "$DEST"
(cd "$STAGE" && zip -qry "$DEST" Payload)

echo "  $(basename "$APP") -> $DEST"
echo "Listo. Instálalo con AltStore o Sideloadly usando tu Apple ID (la firma gratuita dura 7 días)."
