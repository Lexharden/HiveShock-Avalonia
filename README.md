# HiveShock (Avalonia)

Puente entre **tu live** y el juego: TikTok y Twitch pueden escuchar a la vez. Regalos, chat y el resto de eventos se convierten en efectos dentro de la partida. GUI en Avalonia 12 + Fluent, marca Yafel (`#075BAA` / `#EBA00A`).

Cada **canal** (TikTok, Twitch) tiene su propia ficha en Inicio. El programa no asume una sola plataforma.

La app WPF original sigue en el repo CrowdBridge (solo Windows) hasta confirmar paridad.

## Solución

- `HiveShock.Avalonia` — GUI (`HiveShock` / `HiveShock.exe`)
- `HiveShock.Android` — compañero en el teléfono (misma lógica, sin overlays OBS)
- `HiveShock.Core` — runtime, perfiles, canales (TikTok/Twitch), TCP al juego, voz (cola, filtros, motores)
- `HiveShock.Desktop` — voz en Windows: micrófono y salida (WASAPI), voces de Windows, detector de voz, atajos globales
- `HiveShock.Voice.Piper` — voces locales HD con [Piper](https://github.com/rhasspy/piper) (instalador, catálogo, procesos)
- `HiveShock.Cli` — misma lógica en consola
- `HiveShock.Tests` — pruebas xUnit (Core + Piper con dobles; corren en Windows, macOS y Linux)
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

El framework se elige solo según el sistema: en Windows (o `-r win-*`) se compila `net10.0-windows10.0.19041.0`, que incluye la **Voz** (`HiveShock.Desktop`); en macOS/Linux, `net10.0`, donde la página Voz sale como no disponible. Para forzar otro: `-p:TargetFramework=net10.0`.

Abre Inicio, elige el juego y conecta. Las cuentas se enlazan en **TikTok** y **Twitch**.

## Voz (Smart TTS, alpha)

Lee el chat de TikTok y Twitch en voz alta (y opcionalmente regalos, bits y follows). Solo Windows por ahora. Piezas:

- **`SmartVoiceManager`** (Core): cola acotada con precarga (sintetiza el siguiente mientras suena el actual), pausa cuando el streamer habla (micrófono + WebRTC VAD), silenciar/saltar/vaciar, atajos globales y estado para la UI.
- **`TtsMessageFilter`**: comandos, enlaces, emojis, letras repetidas, spam copiado, bots, mensajes propios, roles (subs/mods), palabras prohibidas (sin mayúsculas ni acentos), cooldown por usuario, longitud máxima y frase final («Juan dice: …»).
- **Motores** (`ITtsEngine` + `TtsEngineRegistry`). El orden del registro es la preferencia y el **orden de respaldo** (`TtsSettings.UseFallbackEngines`): si el elegido falla, ese mensaje se lee con el siguiente disponible.

  | Id | Motor | Internet | Dónde |
  | --- | --- | --- | --- |
  | `edge` | Voces neurales de Microsoft Edge | Sí | `EdgeTtsSpeechSynthesizer` (Core) |
  | `piper` | Voces locales HD (Piper) | No | `HiveShock.Voice.Piper` |
  | `windows` | Voces de Windows (OneCore) | No | `WindowsSpeechSynthesizer` (Desktop) |

  Añadir un motor = implementar `ITtsEngine` (o heredar de `TtsEngineBase`) y registrarlo en `MainViewModel.AttachVoiceIfSupported`.
- **Voz por motor, por plataforma y aleatoria**: `TtsSettings.VoiceIds` (una voz por motor), `PlatformVoiceIds` (`"motor:plataforma"`) y el sorteo por idiomas con hash estable por usuario.

### Edge TTS (endpoint no oficial)

Usa el mismo endpoint que el proyecto `edge-tts`; no hay API oficial. `Sec-MS-GEC` y `Sec-MS-GEC-Version` van **en la URL**. Si Microsoft responde **403**, se corrige el desfase de reloj y se reintenta una vez; si sigue, la página Voz se bloquea con un aviso para el streamer (y, con respaldo activo, el chat se sigue leyendo con otro motor). Ante un 403 nuevo, lo primero es subir `ChromiumVersion` en `EdgeTtsSpeechSynthesizer` a la versión estable de Edge que use `edge-tts`.

### Piper (voces locales HD)

- Motor: último binario autónomo de `rhasspy/piper` (**2023.11.14-2**, MIT, repositorio archivado). El proyecto activo (`OHF-Voice/piper1-gpl`, GPL-3.0) solo publica paquetes de Python. Hashes SHA-256 por sistema fijados en `PiperRuntime.Assets` (GitHub no los publica). macOS Apple Silicon: sin verificar.
- Voces: catálogo oficial `huggingface.co/rhasspy/piper-voices` (`voices.json`), verificadas por MD5. **Cada voz tiene su licencia** (ficha `MODEL_CARD`, botón «Licencia» en la UI).
- Piper corre como **proceso aparte** (JSON por stdin, UTF-8 sin BOM, un proceso por voz y velocidad, pool LRU de 3, Job Object en Windows para no dejar huérfanos). Nunca se enlaza: la GPL de espeak-ng no se extiende a HiveShock.
- Se descarga en la carpeta de datos del usuario (ver abajo), no va en los zips: `…/HiveShock/piper/runtime/` (motor) y `…/HiveShock/piper/voices/` (una carpeta por voz del catálogo, con su `voice.json`).
- **Voces propias**: cualquier par `nombre.onnx` + `nombre.onnx.json` copiado a `voices/` (o a una subcarpeta) se reconoce como voz; idioma, calidad y hablantes se leen del `.onnx.json`. En la UI: «Abrir carpeta de voces» → copiar → «Buscar voces nuevas».
- **Librerías faltantes**: si `piper.exe` termina con `0xC0000135`, `0xC000007B` o `0xC0000142` (Windows no pudo cargar una DLL), se lanza `PiperDependencyException` y la UI ofrece descargar el [Visual C++ Redistributable x64](https://aka.ms/vs/17/release/vc_redist.x64.exe) o reinstalar el motor. Ojo al probarlo: Windows 11 trae su propio `onnxruntime.dll` en `System32`, así que quitar ese archivo no reproduce el fallo.

## Datos del usuario

Preferencias (contador de muertes, tema, overlays) y configuración de voz se guardan con `UserDataStore` en **dos sitios**: la carpeta de datos del usuario (`%LOCALAPPDATA%\HiveShock` en Windows, `~/Library/Application Support/HiveShock` en macOS, `~/.local/share/HiveShock` en Linux), que ninguna actualización toca, y una copia junto al ejecutable. Cada guardado es atómico y deja un `.bak`; al abrir se usa la copia válida más reciente. Piper vive en `…/HiveShock/piper/`.

## Pruebas

```bash
dotnet test HiveShock.Tests/HiveShock.Tests.csproj
```

La prueba de integración real de Piper (descarga ~40 MB) se salta salvo que definas `HIVESHOCK_PIPER_IT=1`.

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

- [`ci.yml`](.github/workflows/ci.yml) — escritorio en Windows, macOS y Ubuntu, con `dotnet test`; job `android` aparte (workload + SDK).
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
