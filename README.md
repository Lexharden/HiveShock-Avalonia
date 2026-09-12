# HiveShock (Avalonia)

Puente entre **TikTok Live** y el juego: regalos, likes, follows, shares y comandos de chat se convierten en efectos dentro de la partida. GUI en Avalonia 12 + Fluent, con marca Yafel (`#075BAA` / `#EBA00A`).

La app WPF original sigue en el repo CrowdBridge (solo Windows) hasta confirmar paridad.

## Solución

- `HiveShock.Avalonia` — GUI (`HiveShock` / `HiveShock.exe`)
- `HiveShock.Core` — runtime, perfiles, TikTok, TCP al juego
- `HiveShock.Cli` — misma lógica en consola
- `libs/TikTokLive` — cliente TikTok Live
- `config/` — perfiles, catálogo, imágenes, `.env.example`

## Requisitos

- .NET 10 SDK (`global.json`, banda 10.0.x)
- Windows x64, macOS 13+ (Apple Silicon / Intel) o Linux x64/ARM64

El puente al juego (Majora’s Mask / Twilight Princess) puede correr en el mismo equipo o en otro PC (`GAME_HOST` en `.env`).

## Arranque en desarrollo

Windows:

```powershell
dotnet run --project HiveShock.Avalonia/HiveShock.Avalonia.csproj
```

macOS / Linux:

```bash
dotnet run --project HiveShock.Avalonia/HiveShock.Avalonia.csproj
```

No hay RID fijo en el `.csproj`: `dotnet run` usa el sistema donde compilas.

Abre Inicio, elige el juego (Majora’s Mask o Twilight Princess Dusklight), guarda tu usuario de TikTok y conecta.

## Versionado

La versión vive en un solo sitio: [`Directory.Build.props`](Directory.Build.props). La GUI (Acerca de) y los zips/DMG la leen de ahí.

```bash
./scripts/bump-version.sh patch    # 1.0.0 → 1.0.1
./scripts/bump-version.sh minor    # 1.0.0 → 1.1.0
./scripts/bump-version.sh major    # 1.0.0 → 2.0.0
./scripts/bump-version.sh 1.2.3    # fija X.Y.Z
```

En Windows: `.\scripts\Bump-Version.ps1 patch`

Eso actualiza `Directory.Build.props` y añade una sección en `CHANGELOG.md`. **No hace commit ni tag.** Cuando quieras publicar:

```bash
git add Directory.Build.props CHANGELOG.md
git commit -m "release: v1.0.1"
git tag v1.0.1
git push && git push --tags
```

El tag `vX.Y.Z` tiene que coincidir con `Directory.Build.props`. GitHub Actions empaqueta Windows, macOS y Linux y crea el Release.

## Publicar en local

**Windows** (PowerShell):

```powershell
.\scripts\Pack-Release.ps1
```

**macOS / Linux**:

```bash
chmod +x scripts/pack-release.sh
./scripts/pack-release.sh
./scripts/pack-release.sh --cli
```

En Mac el pack firma ad hoc y genera el DMG. Hay que ejecutarlo **en un Mac** (`osx-arm64` en Tahoe).

Salida en `dist/`:

| Archivo | Para quién |
| --- | --- |
| `HiveShock-<ver>-osx-arm64.dmg` | Otras Mac (arrastrar a Aplicaciones) |
| `HiveShock-<ver>-win-x64-full.zip` | Windows, instalación nueva |
| `HiveShock-<ver>-linux-x64-full.zip` | Linux |
| `HiveShock-<ver>-<rid>-update.zip` | Actualizar sin pisar perfiles |

En otra Mac: abre el DMG, arrastra HiveShock a Aplicaciones, **clic derecho → Abrir** (solo la primera vez). En Tahoe, si no aparece Abrir: Ajustes del Sistema → Privacidad y seguridad → Abrir de todos modos.

En Linux: `chmod +x HiveShock && ./HiveShock`.

Opcional: `-IncludeCli` / `--cli` añade la consola.

## GitHub Actions

- [`ci.yml`](.github/workflows/ci.yml) — `dotnet build` en Windows, macOS y Ubuntu (push/PR a `main` o `master`).
- [`release.yml`](.github/workflows/release.yml) — al pushear `vX.Y.Z` (o *Run workflow*) genera:

  - `win-x64`
  - `osx-arm64` + `.dmg`
  - `osx-x64` + `.dmg`
  - `linux-x64`
  - `linux-arm64`

## Checklist de paridad (vs WPF)

- [ ] Inicio: perfiles, canal TikTok, modos En vivo / Anotar / Probar, simular
- [ ] Conectar: Listo → Conectando → En vivo / Anotando / En pruebas
- [ ] Contador de muertes/vidas, rescue y borrar partida (si el perfil lo permite)
- [ ] Overlays OBS on/off + aspecto (contador y regalos), capturables
- [ ] Regalos: lista, alta manual / desde Vistos, efecto por perfil, guardar, probar, thumbs
- [ ] Vistos: listar, editar, usar en este juego, carpeta de imágenes
- [ ] Likes y chat: likes / follow / share / palabras del chat (se guardan con Guardar)
- [ ] Ayuda + Acerca de (Yafel, web, Ko-fi, versión)
- [ ] Tema oscuro / claro / sistema
- [ ] Publish: zip full + update (Windows, macOS, Linux); DMG ad hoc en Mac

## Marca

- Producto: HiveShock · Yafel
- Web: https://hiveshock.yafel.dev
- Ko-fi: https://ko-fi.com/yafel
