# Changelog

Cambios relevantes de HiveShock. El versionado está en `Directory.Build.props`.

## [Unreleased]

## [1.2.2] - 2026-09-19

- Fix: los combos de TikTok sin mensaje de cierre (regalos "battle"/gallery como galaxias y ballenas) se quedaban sin procesar — nunca se enviaba el efecto ni se registraba en el log. Ahora se cierran solos por timeout (6s de inactividad) si TikTok nunca manda el `RepeatEnd=1`.
- Pestaña "🔬 Debug" en TikTok (solo en builds DEBUG): registro en vivo de eventos crudos de TikTok para diagnóstico.
- Perfil Dusklight: nuevo enemigo Helmsaurus y acción de reset.

## [1.2.0] - 2026-09-16

- Parámetros por efecto (daño/cura en corazones) en regalos; solo aparecen si el efecto lo declara.
- Envíos al juego espaciados (EFFECT_GAP_MS, default 300 ms) para que los combos xN no se pierdan.
- Botón Limpiar en el log de actividad del escritorio.
- Mejor detección de combos de TikTok (RepeatCount / ComboCount).


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
