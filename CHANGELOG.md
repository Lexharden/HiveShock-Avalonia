# Changelog

Cambios relevantes de HiveShock. El versionado está en `Directory.Build.props`.

## [Unreleased]

- Cabeza Android (Avalonia): live en el teléfono hacia el APK local (`127.0.0.1:43000`) o el PC en LAN. Sin overlays OBS.
- Android: servicio en primer plano al conectar (notificación «HiveShock conectado») para no cortar TikTok/Twitch al cambiar de app.

## [1.1.2] - 2026-09-14

- Perfil Ocarina of Time (Ship of Harkinian): efectos propios de SoH (Dark Link, Arwing, canciones de warp), no el catálogo de 2s2h.
- Chat: lista de palabras vacía de fábrica. Los desplegables de efectos tienen scroll; las listas se pueden redimensionar.

## [1.1.1] - 2026-09-14

- Tema “Igual que el sistema” sigue al claro/oscuro de Windows.
- En vivo en verde (barra, TikTok y el punto parpadeante), legible en tema claro y oscuro.
- Comandos de chat: espera por viewer y hueco entre todos para no saturar el juego (TikTok y Twitch).
- Menú hamburguesa (☰). Cuenta TikTok/Twitch plegable. Regalos, vistos, metas, chat y logs al ancho de la página.

## [1.1.0] - 2026-09-14

- TikTok y Twitch como canales separados; pueden ir a la vez. Cada uno guarda chat y follows por su lado.
- Twitch: Entrar con Twitch (Device Code), chat, follows y bits. El Client ID no se pega en la UI.
- Bits: umbrales en la página Twitch (desde X bits → efecto).
- Metas del live: varios viewers suman el mismo regalo (p. ej. 2 Galaxy = reinicio). Overlay para OBS.
- Inicio, TikTok y Twitch como páginas propias. Copy del programa genérico; los nombres de juego quedan en el perfil.
- Ayuda y actividad alineadas con los dos canales.

## [1.0.0] - 2026-09-12

- GUI Avalonia en Windows, macOS y Linux (self-contained).
- Pack de release: zip full/update; en Mac también DMG con firma ad hoc.
- Perfiles Majora’s Mask y Twilight Princess Dusklight.
