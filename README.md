# HiveShock (Avalonia)

Puente entre **tu live** y el juego: TikTok y Twitch pueden escuchar a la vez. Regalos, chat y el resto de eventos se convierten en efectos dentro de la partida. GUI en Avalonia 12 + Fluent, marca Yafel (`#075BAA` / `#EBA00A`).

Cada **canal** (TikTok, Twitch) tiene su propia ficha en Inicio. El programa no asume una sola plataforma.

La app WPF original sigue en el repo CrowdBridge (solo Windows) hasta confirmar paridad.

## Solución

- `HiveShock.Avalonia` — GUI (`HiveShock` / `HiveShock.exe`)
- `HiveShock.Android` — compañero en el teléfono (misma lógica, sin overlays OBS)
- `HiveShock.Core` — runtime, perfiles, canales (TikTok/Twitch), TCP al juego
- `HiveShock.Cli` — misma lógica en consola
- `libs/TikTokLive` — cliente TikTok Live
- `config/` — perfiles, catálogo, imágenes, `.env.example`

## Requisitos

- .NET 10 SDK (`global.json`, banda 10.0.x)
- Windows x64, macOS 13+ (Apple Silicon / Intel) o Linux x64/ARM64

El puente al juego (Majora’s Mask / Ocarina of Time / Twilight Princess) puede correr en el mismo equipo o en otro PC (`GAME_HOST` en `.env`).

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

Abre Inicio, elige el juego y conecta. Las cuentas se enlazan en **TikTok** y **Twitch**.

## Android (compañero)

El live corre en el **teléfono**. Menú **Inicio / Regalos / Eventos / Twitch**: el mapeo es el mismo `gifts.json` que en el escritorio (sin overlays OBS). El juego es el APK en el mismo aparato (`GAME_HOST=127.0.0.1`, puerto 43000) o el del **PC** si el port escucha en la LAN. Al **Conectar** queda una notificación fija: puedes pasar al port y el live sigue. **Detener** quita la notificación. Algunos fabricantes (batería agresiva) pueden cortar igual; en esos casos deja HiveShock sin optimizar batería.

```powershell
dotnet workload install android
dotnet run --project HiveShock.Android/HiveShock.Android.csproj
```

En el **emulador**, si el juego está en el PC usa `10.0.2.2`. En un móvil, `127.0.0.1` para el APK local o la IP LAN del PC (firewall al puerto 43000).

Pack (APK desde la carpeta `publish/`, no intermedios de `bin/`):

```bash
./scripts/Pack-Android.sh
adb uninstall dev.yafel.hiveshock
adb install -r dist/HiveShock-*-android.apk
```

En Windows: `.\scripts\Pack-Android.ps1`. Firma opcional con `ANDROID_SIGNING_*`. No subas el `.keystore` al git.

El CI de escritorio **no** restaura el `.slnx` entero (el proyecto Android exige el workload). Hay un job aparte `android`.

## Twitch (empaquetado)

El Client ID de la app HiveShock vive en `TWITCH_CLIENT_ID` o en `TwitchApp.ClientId`. El streamer solo pulsa **Entrar con Twitch**. Device Code + EventSub WebSocket en el PC.

TikTok y Twitch guardan chat y follows por separado en `gifts.json` (`chat` vs `twitch`). Los bits van en `twitch.bits`.

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

- [`ci.yml`](.github/workflows/ci.yml) — escritorio en Windows, macOS y Ubuntu; job `android` aparte (workload + SDK).
- [`release.yml`](.github/workflows/release.yml) — al pushear `vX.Y.Z` (o *Run workflow*) genera:

  - `win-x64`
  - `osx-arm64` + `.dmg`
  - `osx-x64` + `.dmg`
  - `linux-x64`
  - `linux-arm64`

## Checklist de paridad (vs WPF)

- [ ] Inicio: perfiles, canales TikTok/Twitch, modos En vivo / Anotar / Probar, simular
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
