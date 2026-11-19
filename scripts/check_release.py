#!/usr/bin/env python3
"""
Comprueba que una versión está lista para publicarse, antes de compilar nada.

Uso:
    python scripts/check_release.py              # usa la versión de Directory.Build.props
    python scripts/check_release.py 3.0.0-beta.1 # además exige que coincida (p. ej. con el tag)

Lo usan el workflow de Release (antes de arrancar los runners) y scripts/Release.ps1.
Falla (código 1) si:
  * la versión no es SemVer X.Y.Z[-etiqueta.N] o no coincide con la pedida;
  * CHANGELOG.md no tiene la sección «## [X.Y.Z] - AAAA-MM-DD»;
  * la sección está vacía o conserva el marcador «- » que deja el script de versión
    (las notas del release en GitHub salen de esa sección).
"""
from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
VERSION_RE = re.compile(r"^\d+\.\d+\.\d+(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$")


def props_version() -> str:
    text = (ROOT / "Directory.Build.props").read_text(encoding="utf-8-sig")
    m = re.search(r"<Version>\s*([^<\s]+)\s*</Version>", text)
    if not m:
        raise SystemExit("ERROR: no hay <Version> en Directory.Build.props")
    return m.group(1)


def changelog_section(version: str) -> tuple[str | None, str]:
    """Devuelve (cabecera, cuerpo) de la sección de esa versión, o (None, "")."""
    lines = (ROOT / "CHANGELOG.md").read_text(encoding="utf-8-sig").splitlines()
    header, body = None, []
    for line in lines:
        if line.startswith("## ["):
            if header is not None:
                break
            if line.startswith(f"## [{version}]"):
                header = line
            continue
        if header is not None:
            body.append(line)
    return header, "\n".join(body).strip()


def main(argv: list[str]) -> int:
    errors: list[str] = []
    version = props_version()

    if not VERSION_RE.match(version):
        errors.append(f"la versión '{version}' no tiene formato X.Y.Z o X.Y.Z-etiqueta.N")

    if len(argv) > 1:
        wanted = argv[1].removeprefix("v")
        if wanted != version:
            errors.append(
                f"se pide v{wanted} pero Directory.Build.props dice {version}. "
                f"Ejecuta ./scripts/bump-version.sh {wanted} y sube el commit antes del tag."
            )

    header, body = changelog_section(version)
    if header is None:
        errors.append(f"CHANGELOG.md no tiene la sección «## [{version}] - AAAA-MM-DD»")
    else:
        if not re.match(rf"^## \[{re.escape(version)}\] - \d{{4}}-\d{{2}}-\d{{2}}\s*$", header):
            errors.append(f"la cabecera «{header}» debe ser «## [{version}] - AAAA-MM-DD»")
        if not body:
            errors.append(f"la sección {version} del CHANGELOG está vacía (de ahí salen las notas del release)")
        elif any(l.strip() in ("-", "*") for l in body.splitlines()):
            errors.append(f"la sección {version} del CHANGELOG conserva un punto vacío («- »)")

    if errors:
        for e in errors:
            print(f"ERROR: {e}", file=sys.stderr)
        return 1

    kind = "preliminar: saldrá como pre-release" if "-" in version else "estable"
    print(f"HiveShock {version} lista para publicar ({kind}).")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
