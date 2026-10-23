# HiveShock (Avalonia)

Puente entre **tu live** y el juego: TikTok y Twitch pueden escuchar a la vez. Regalos, chat y el resto de eventos se convierten en efectos dentro de la partida. GUI en Avalonia 12 + Fluent, marca Yafel (`#075BAA` / `#EBA00A`).

Cada **canal** (TikTok, Twitch) tiene su propia ficha en Inicio. El programa no asume una sola plataforma.

## Solución

- `HiveShock.Avalonia` — GUI (`HiveShock` / `HiveShock.exe`)
- `HiveShock.Android` — compañero en el teléfono (misma lógica, sin overlays OBS)
- `HiveShock.Core` — runtime, perfiles, canales (TikTok/Twitch), TCP al juego
- `HiveShock.Cli` — misma lógica en consola
- `HiveShock.Tests` — pruebas xUnit (corren en Windows, macOS y Linux)
- `libs/TikTokLive` — cliente TikTok Live
- `config/` — perfiles, catálogo, imágenes, `.env.example`

## Requisitos

- .NET 10 SDK (`global.json`, banda 10.0.x)
- Windows x64, macOS 13+ (Apple Silicon / Intel) o Linux x64/ARM64

El puente al juego (Majora’s Mask / Ocarina of Time / Twilight Princess / Super Mario 64) puede correr en el mismo equipo o en otro PC (`GAME_HOST` en `.env`).

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

## Datos del usuario

Preferencias (contador de muertes, tema, overlays) se guardan con `UserDataStore` en **dos sitios**: la carpeta de datos del usuario (`%LOCALAPPDATA%\HiveShock` en Windows, `~/Library/Application Support/HiveShock` en macOS, `~/.local/share/HiveShock` en Linux), que ninguna actualización toca, y una copia junto al ejecutable. Cada guardado es atómico y deja un `.bak`; al abrir se usa la copia válida más reciente. Los archivos que dejaron las funciones retiradas (`.hiveshock-tts.json`, `.hiveshock-zeldathon*.json` y la carpeta `piper/`) ya no se usan y puedes borrarlos.

## Pruebas

```bash
dotnet test HiveShock.Tests/HiveShock.Tests.csproj
```

## Android (compañero)

El live corre en el **teléfono**. Menú **Inicio / Regalos / Eventos / Twitch**: el mapeo es el mismo `gifts.json` que en el escritorio (sin overlays OBS). El juego es el APK en el mismo aparato (`GAME_HOST=127.0.0.1`, puerto 43000) o el del **PC** si el port escucha en la LAN. Al **Conectar** queda una notificación fija: puedes pasar al port y el live sigue. **Detener** quita la notificación. Algunos fabricantes (batería agresiva) pueden cortar igual; en esos casos deja HiveShock sin optimizar batería.

```powershell
dotnet workload install android
dotnet run --project HiveShock.Android/HiveShock.Android.csproj
```

En el **emulador**, si el juego está en el PC usa `10.0.2.2`. En un móvil, `127.0.0.1` para el APK local o la IP LAN del PC (firewall al puerto 43000).

**Qué hace y qué no, frente al escritorio**

| Función | Android |
| --- | --- |
| TikTok / Twitch, regalos, eventos, metas (mapeo y efectos) | Sí |
| Editor de perfil, copiar y borrar perfiles propios | Sí |
| Perfiles del catálogo nuevos (p. ej. Super Mario 64) | Sí, se instalan solos al abrir la app |
| Aviso de versión nueva | Sí (Acerca de → abre el release; no descarga solo) |
| Overlays OBS | No, por diseño |
| Teclas / macros hacia el juego | No (solo Windows) |

La app guarda sus datos en la carpeta privada de la app y **no entra en las copias de seguridad de Android** (`allowBackup=false`): contiene tokens de Twitch.

Android corta los servicios en segundo plano de tipo *datos* a las ~6 h (Android 15+). HiveShock lo avisa en el log; vuelve a pulsar **Conectar**.

**Instalar:** baja `HiveShock-<ver>-android.apk` del [Release](https://github.com/Lexharden/HiveShock-Avalonia/releases) de la versión, permite instalar de orígenes desconocidos y ábrelo. Para actualizar, instala el APK nuevo encima (misma firma; no hace falta desinstalar). Comprueba la integridad con `SHA256SUMS.txt` del release.

Pack local (APK desde la carpeta `publish/`, no intermedios de `bin/`):

```bash
./scripts/Pack-Android.sh
adb uninstall dev.yafel.hiveshock
adb install -r dist/HiveShock-*-android.apk
```

En Windows: `.\scripts\Pack-Android.ps1`. Firma opcional con `ANDROID_SIGNING_*`. No subas el `.keystore` al git.

**Firma del release (una sola vez).** Un APK solo actualiza a otro firmado con la **misma clave**: guarda el keystore a buen recaudo (si se pierde, nadie podrá actualizar). Crea uno y súbelo como secrets del repo:

```bash
keytool -genkeypair -v -keystore hiveshock-release.keystore -alias hiveshock -keyalg RSA -keysize 4096 -validity 10000
base64 -w0 hiveshock-release.keystore   # → secret ANDROID_KEYSTORE_BASE64
# además: ANDROID_SIGNING_ALIAS (hiveshock) y ANDROID_SIGNING_PASSWORD
```

El `versionCode` se calcula solo desde `Version` (`major*10000 + minor*100 + patch`), así que subir la versión con `bump-version` basta.

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

Versiones preliminares (alpha → beta → rc → final):

```bash
./scripts/bump-version.sh major alpha     # 1.3.0 → 2.0.0-alpha.1 (también patch/minor y beta/rc)
./scripts/bump-version.sh pre             # 2.0.0-alpha.1 → 2.0.0-alpha.2
./scripts/bump-version.sh beta            # 2.0.0-alpha.2 → 2.0.0-beta.1 (luego rc)
./scripts/bump-version.sh release         # 2.0.0-rc.1 → 2.0.0
./scripts/bump-version.sh 2.0.0-beta.1    # fija una exacta
```

En Windows: `.\scripts\Bump-Version.ps1 patch` (mismos argumentos). Una versión con `-` se publica en GitHub como **pre-release**, y en el `Info.plist` de macOS se usa solo la parte numérica.

Eso actualiza `Directory.Build.props` y añade una sección en `CHANGELOG.md`. **No hace commit ni tag.** Cuando quieras publicar:

```bash
git add Directory.Build.props CHANGELOG.md
git commit -m "release: v1.0.1"
git tag v1.0.1
git push && git push --tags
```

El tag `vX.Y.Z` tiene que coincidir con `Directory.Build.props`. GitHub Actions empaqueta Windows, macOS, Linux y Android y crea el Release.

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
| `HiveShock-<ver>-android.apk` | Teléfono Android (compañero, sin overlays) |

En otra Mac: abre el DMG, arrastra HiveShock a Aplicaciones, **clic derecho → Abrir** (solo la primera vez). En Tahoe, si no aparece Abrir: Ajustes del Sistema → Privacidad y seguridad → Abrir de todos modos.

En Linux: `chmod +x HiveShock && ./HiveShock`.

Opcional: `-IncludeCli` / `--cli` añade la consola.

## GitHub Actions

- [`ci.yml`](.github/workflows/ci.yml) — escritorio en Windows, macOS y Ubuntu, con `dotnet test`; job `android` aparte (workload + SDK).
- [`release.yml`](.github/workflows/release.yml) — al pushear `vX.Y.Z` (o *Run workflow*) genera:

  - `win-x64`
  - `osx-arm64` + `.dmg`
  - `osx-x64` + `.dmg`
  - `linux-x64`
  - `linux-arm64`
  - `android` (`HiveShock-<ver>-android.apk`, firmado con los secrets `ANDROID_*`; en un tag es obligatorio)

## Checklist de paridad (vs WPF)

- [ ] Inicio: perfiles, canales TikTok/Twitch, modos En vivo / Anotar / Probar, simular
- [ ] Conectar: Listo → Conectando → En vivo / Anotando / En pruebas
- [ ] Contador de muertes/vidas, rescue y borrar partida (si el perfil lo permite)
- [ ] Overlays OBS on/off + aspecto (contador y regalos), capturables
- [ ] Regalos: lista, alta manual / desde el Catálogo, efecto por perfil, guardar, probar, thumbs
- [ ] Catálogo: todos los regalos de TikTok + vistos en lives, buscar, editar, usar en este juego, carpeta de imágenes
- [ ] Likes y chat: likes / follow / share / palabras del chat (se guardan con Guardar)
- [ ] Ayuda + Acerca de (Yafel, web, Instagram, Discord, YouTube, equipo, Ko-fi, versión)
- [ ] Tema oscuro / claro / sistema
- [ ] Publish: zip full + update (Windows, macOS, Linux); DMG ad hoc en Mac

## Marca

- Producto: HiveShock · Yafel
- Web: https://hiveshock.yafel.dev
- Ko-fi: https://ko-fi.com/yafel
- Discord: https://discord.gg/QTdQffuZF3
- YouTube: https://www.youtube.com/@HiveShock
- Instagram (desarrollador): https://www.instagram.com/yaafel/

El equipo que aparece en **Acerca de** (testers, soporte, administradores…) se edita en [`HiveShock.Avalonia/Assets/credits.json`](HiveShock.Avalonia/Assets/credits.json): categorías con personas (`name` y `tiktok` opcional, sin @). Las categorías vacías no se muestran. Los logos de redes son de [Simple Icons](https://simpleicons.org) (CC0).
