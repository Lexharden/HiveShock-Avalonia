# Changelog

Cambios relevantes de HiveShock. El versionado se encuentra en `Directory.Build.props`.

## [Unreleased]

## [2.1.0-alpha.1] - 2026-09-24

-


## [2.0.0-alpha.1] - 2026-09-24

-


## [1.3.1] - 2026-09-22

* Mejora de la interfaz de usuario.
* Se agregó un candado de seguridad para proteger la información del perfil.

## [1.3.0] - 2026-09-21

* TikTok/Twitch: reconexión con espera creciente (8 → 60 s) en lugar de intentar reconectar cada 8 s indefinidamente mientras el streamer no está en vivo.
* Nueva pestaña **Perfil** (Android y escritorio): permite editar `profile.json` y `effects.json` del perfil activo (metadatos, puertos, etiquetas y catálogo de efectos), con opciones independientes de importación y exportación para cada archivo.
* El panel **«Qué está pasando»** ahora está recogido por defecto. Antes reservaba espacio fijo en toda la página; ahora se abre automáticamente al conectar o cuando el usuario lo solicita.
* Se eliminó el bloque de marca duplicado en el sidebar.
* Se eliminó la descripción de perfil repetida.
* Se ajustó la interfaz para que las tarjetas de juego mantengan la misma altura.
* Android: *wake lock* del servicio en segundo plano con límite de 6 h y renovación automática, en vez de indefinido.
* Cabeza Android (Avalonia): live en el teléfono hacia el APK local (`127.0.0.1:43000`) o el PC en LAN. Sin overlays OBS.
* Android: menú Inicio / Regalos / Eventos / Twitch con el mismo mapeo de efectos que el escritorio (`gifts.json`).
* Android: servicio en primer plano al conectar (notificación «HiveShock conectado») para no cortar TikTok/Twitch al cambiar de app.

## [1.2.2] - 2026-09-19

* **Fix:** los combos de TikTok sin mensaje de cierre (regalos de tipo *battle/gallery*, como galaxias y ballenas) podían quedar sin procesar. En esos casos, nunca se enviaba el efecto ni se registraba el evento en el log. Ahora los combos se cierran automáticamente después de 6 segundos de inactividad si TikTok nunca envía `RepeatEnd=1`.
* Se agregó la pestaña **🔬 Debug** en TikTok (solo en builds `DEBUG`), con registro en tiempo real de eventos crudos de TikTok para facilitar el diagnóstico.
* Perfil **Dusklight**: se agregó el enemigo Helmsaurus y una acción de reset.

## [1.2.0] - 2026-09-16

* Se agregaron parámetros por efecto (daño/curación en corazones) para los regalos; solo aparecen cuando el efecto los declara.
* Se agregaron envíos espaciados al juego mediante `EFFECT_GAP_MS` (300 ms por defecto) para evitar que los combos `xN` se pierdan.
* Se agregó el botón **Limpiar** al log de actividad del escritorio.
* Se mejoró la detección de combos de TikTok mediante `RepeatCount` / `ComboCount`.

## [1.1.2] - 2026-09-14

* Perfil **Ocarina of Time (Ship of Harkinian)**: se agregaron efectos propios de SoH, como Dark Link, Arwing y canciones de warp, en lugar del catálogo de 2s2h.
* **Chat:** la lista de palabras viene vacía de fábrica.
* Los desplegables de efectos ahora cuentan con scroll.
* Las listas pueden redimensionarse.

## [1.1.1] - 2026-09-14

* El tema **«Igual que el sistema»** ahora sigue automáticamente el modo claro u oscuro de Windows.
* El estado **En vivo** se muestra en verde en la barra, TikTok y el indicador parpadeante, con buena legibilidad en ambos temas.
* **Comandos de chat:** se agregó espera por viewer y un intervalo entre comandos para evitar saturar el juego, tanto en TikTok como en Twitch.
* Se agregó el **menú hamburguesa (☰)**.
* Las cuentas de TikTok y Twitch ahora pueden plegarse.
* Regalos, espectadores, metas, chat y logs aprovechan mejor el ancho disponible de la página.

## [1.1.0] - 2026-09-14

* TikTok y Twitch ahora funcionan como canales independientes y pueden utilizarse simultáneamente.
* Cada canal mantiene su propio chat y registro de follows.
* **Twitch:** inicio de sesión mediante Device Code, soporte para chat, follows y bits.
* El Client ID de Twitch ya no se introduce desde la interfaz.
* **Bits:** se pueden configurar umbrales en la página de Twitch para activar efectos a partir de una determinada cantidad de bits.
* **Metas del live:** varios viewers pueden contribuir al mismo regalo (por ejemplo, 2 Galaxy = reinicio).
* Se agregó un overlay para OBS.
* Inicio, TikTok y Twitch ahora son páginas independientes.
* Se hizo más genérico el contenido de la aplicación; los nombres de los juegos ahora pertenecen al perfil correspondiente.
* Las páginas de ayuda y actividad se adaptaron para funcionar con ambos canales.

## [1.0.0] - 2026-09-12

* GUI basada en Avalonia para Windows, macOS y Linux (self-contained).
* Paquete de release con ZIP completo y ZIP de actualización.
* En macOS, también se incluye DMG con firma ad hoc.
* Perfiles **Majora's Mask** y **Twilight Princess Dusklight**.
