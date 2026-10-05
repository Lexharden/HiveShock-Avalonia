# Changelog

Cambios relevantes de HiveShock. El versionado se encuentra en `Directory.Build.props`.

## [Unreleased]

## [2.2.0-zeldathon.3] - 2026-10-04

Edición especial para los streamers de Zeldatón (no pública). Suma la moderación de espectadores, la cola de enemigos sin pérdidas y el aviso animado del cronómetro.

### Moderación
* **Pausa de emergencia** (botón en la barra de arriba y en Moderación): los espectadores dejan de activar efectos y metas, y lo que ya esperaba turno se descarta. El chat en voz alta, los avisos y el tiempo por donaciones de Zeldatón siguen. No se guarda: al abrir HiveShock siempre empieza reanudada. Las pruebas del streamer no se ven afectadas.
* **Lista de bloqueo** (nueva página Moderación): ves quién activó efectos hace poco y lo bloqueas con un clic, o bloqueas por @usuario (TikTok o Twitch). Se reconoce por su **id**, así que cambiar de @usuario no lo libra, y otra persona que se quede con ese @usuario no queda bloqueada. Un bloqueado no activa efectos ni metas, no se lee su chat en voz alta, no cuenta como seguidor y sus regalos o bits no cambian el tiempo de la carrera.
* **Anti-spam de seguir más difícil de esquivar**: el efecto de seguir se decide por el id de la persona (antes, en TikTok caía al nombre visible si faltaba el id, que cualquiera puede repetir y que se cambia). Cambiar de @usuario ya no permite repetir el efecto, y un evento sin id se reconoce por el @usuario recordado. Si la plataforma no manda ni id ni @usuario, el follow pasa sin recordarse (y se avisa una vez) en vez de bloquear a inocentes con el mismo nombre.

### Cronómetro
* **Aviso animado al cambiar el tiempo por donaciones**: bajo el reloj sale «+1:30» (verde) o «−0:30» (rojo) con un pequeño rebote, un destello del número y un desvanecimiento. Aparece cuando el servidor **confirma** el cambio y muestra lo que aplicó de verdad (si un tope lo recortó, dice «tope»). Varias donaciones seguidas se juntan en un solo aviso con el total neto («+3:20 ×5»). No muestra nombres de espectadores.
* Solo con el reloj oficial. Se activa o desactiva y se cambian los colores en Zeldatón → Cronómetro en pantalla → «Aviso al cambiar el tiempo», donde también hay botones para probarlo sin tocar el reloj. Con la opción activa el cronómetro reserva una línea bajo el reloj para que nada salte.

### Cola de enemigos y entrega
* **Los enemigos de los regalos ya no se pierden ni se borran para hacer sitio.** El juego (Shipwright) lleva un presupuesto de carga (`gRemote.HiveShock.MaxLoad`, 14 por defecto; cada enemigo pesa 1–4): los que no caben esperan en orden y entran conforme mueren otros, de uno en uno. La cola sobrevive a cambios de escena, los enemigos vivos te siguen a la siguiente escena, uno que se queda lejos se reubica junto a Link y la cola se guarda en disco (caduca a las 12 h).
* **Reintentos si el juego está cerrado:** cada efecto se reintenta hasta `GAME_RETRY_SECONDS` (600 s por defecto; 0 = no reintentar) en vez de perderse. La pausa de emergencia sigue descartando lo pendiente.
* Nuevos eventos del juego `spawn_queue` (cuántos esperan, cuántos viven y la carga; disponible en `BridgeRuntime.SpawnQueue`) y `spawn_rejected`, y acción `clear_queue`.

## [2.2.0-zeldathon.2] - 2026-09-30

Edición especial para los streamers de Zeldatón (no pública).

### Tiempo por donaciones
* **Los regalos de TikTok y los bits de Twitch pueden sumar o restar tiempo de la carrera.** En Zeldatón → «Tiempo por donaciones» eliges, para cada plataforma, si suman o restan y cuánto valen («cada 1 diamante = 1 segundo», «cada 100 bits = 60 segundos»), un mínimo para ignorar donaciones pequeñas y un tope propio por donación. Se ven ejemplos con tu tarifa.
* Un combo de TikTok cuenta una vez con todos sus diamantes; si TikTok no manda el precio se toma del catálogo. Las fracciones no se pierden (diez Rosas a «cada 10 diamantes = 1 s» suman 1 s).
* El servidor de la carrera aplica el cambio al reloj oficial dentro de los límites del organizador (activar, sumar/restar, tope por donación y topes diarios) y dice cuánto se aplicó de verdad; la página muestra las últimas donaciones y el total confirmado.
* A prueba de cortes: cada donación se guarda en disco hasta que el servidor la confirma y se reenvía sola al reconectar o al volver a abrir HiveShock; el servidor nunca aplica dos veces la misma. Las pendientes de más de 12 h se descartan.
* Los botones de prueba de regalos no cambian el tiempo: solo cuentan las donaciones reales del directo.

## [2.2.0-zeldathon.1] - 2026-09-30

Edición especial para los streamers del evento Zeldathon. No es una versión pública: se entrega directamente a los participantes y no se publica en Releases.

### Zeldatón
* **Cronómetro en pantalla**: nuevo overlay para OBS con el reloj oficial de la carrera (cuenta atrás del día, estado en vivo/pausado/agotado, avisos de color a 30 y 5 minutos, barra de tiempo usado y «reinicia en») o un cronómetro manual (sube o baja). Se personaliza como los demás overlays.
* **Página Zeldatón**: conecta HiveShock con el servidor de la carrera con tu token. El reloj oficial se sincroniza (y se congela al perder conexión), y tu progreso, ítems, jefes, corazones, rupias, chat y espectadores aparecen solos en la web. Reconexión automática.
* Cuando se acaba el tiempo del día el servidor ordena cerrar el juego: cierre limpio (guarda la partida) y, si hace falta, forzado. Detalles en `docs/zeldathon.md`.
* Perfil Ocarina of Time: `zeldathon.json` traduce lo que manda el juego a los ids de la carrera (editable sin recompilar). Ahora cubre unos 60 ítems (armas, escudos, túnicas, botas, flechas, hechizos, mejoras, canciones, medallas, piedras) y el Link que se está jugando (niño/adulto).
* El catálogo de ítems y objetivos lo administra el organizador en el servidor; HiveShock lo descarga al conectar y solo envía lo que el servidor conoce, y termina el juego con los objetivos que exige el evento.
* Nuevo efecto **Ralentizar a Link** (`slow_down`) en el perfil Ocarina of Time: baja la velocidad de Link durante unos segundos (intensidad ajustable de 0.2 a 0.9).

### Guardado
* **Guardado automático** en Regalos, Metas y Eventos con una etiqueta que dice si se guardó (verde), está pendiente (dorada) o falló (roja). Se puede desactivar. Perfil y efectos siguen guardándose solo con su botón.
* Metas: botón **Del catálogo** para añadir regalos a una meta.

### Seguidores
* **Anti-spam de seguir**: cada usuario dispara el efecto de seguir una sola vez (TikTok y Twitch), aunque siga y deje de seguir en bucle. Se recuerda entre directos; opción y botón «Olvidar seguidores» en Eventos.

### Correcciones
* Limpiar la caché de imágenes de regalos ya no provoca «Cannot access a disposed object» en los overlays.

## [2.1.2-alpha.1] - 2026-09-26

* **Vistos pasa a llamarse Catálogo**: ya muestra todos los regalos de TikTok, no solo los que salieron en tus lives.
* Buscador al elegir un regalo del catálogo (escritorio y Android): por nombre, alias o id, sin importar acentos; Enter elige el primero. Cada regalo muestra cuántas veces salió en tus lives.
* Sin repetidos en el Catálogo: las variantes de TikTok del mismo regalo (mismo nombre y precio, otro id) se muestran como una sola fila; los que comparten nombre pero cuestan distinto siguen separados. Manda el nombre oficial y el visto en el live queda como alias.

## [2.1.1-alpha.1] - 2026-09-25

* **Regalos de TikTok actualizados**: 684 imágenes nuevas en `gifts-images/` (formato `{id}_{Nombre}.webp`) y lista oficial en `tiktok_gifts.json` (id, nombre, diamantes e imagen).
* **Vistos** muestra también los regalos que aún no salieron en tus lives, con buscador y un interruptor para ver solo los vistos. Se pueden asignar efectos a cualquier regalo sin esperar a verlo.
* El catálogo completa diamantes, nombre oficial e imagen de lo visto sin modificar `gift-catalog.json`, y ya no se pierde si el archivo se daña (se recupera del respaldo o se aparta).
* Imágenes: se aceptan `{id}_{Nombre}.webp`, `{id}.webp` y `{nombre}.webp`; la carpeta se revisa sola al añadir imágenes.
* Las actualizaciones incluyen la lista oficial y las imágenes nuevas (sin tocar tus perfiles ni tu catálogo).
* Voz: nueva opción en los filtros para no leer mensajes que etiquetan a alguien con @usuario.

## [2.1.0-beta.1] - 2026-09-25

Primera beta de la 2.x: une en una sola versión Android, la Voz y las mejoras visuales de la 1.3.1.

### Voz (Alpha, solo Windows)
* Nueva sección **Voz**: lee en voz alta el chat de TikTok y Twitch, y puede anunciar regalos, bits y seguidores.
* Tres tipos de voz: **Microsoft Edge** (naturales, con internet), **Voces locales HD (Piper)** (naturales, sin internet, se descargan una vez) y **Windows** (sin internet).
* Voces locales HD: instalación del motor con un clic, catálogo oficial por idioma, descargas verificadas, licencia de cada voz, voces propias (`.onnx` + `.onnx.json`) y botón para abrir su carpeta.
* **Respaldo automático**: si el tipo de voz elegido falla (sin internet o bloqueado), el chat se sigue leyendo con otro.
* Si Microsoft bloquea sus voces (403), la sección se bloquea con un aviso claro para el streamer.
* **Voz por plataforma** (una para TikTok y otra para Twitch) y **voz aleatoria** por idiomas, con la misma voz para cada persona si se quiere.
* Selector de voces agrupado por idioma; velocidad, tono, volumen y salida de audio configurables.
* Micrófono elegible: la voz se calla cuando hablas y **repite entero el mensaje cortado** al terminar.
* Filtros del chat: comandos, enlaces, emojis, spam repetido, bots, mensajes propios, roles (suscriptores/moderadores), palabras prohibidas, espera por persona y longitud máxima.
* Atajos globales para silenciar (Ctrl+Alt+M) y saltar mensaje (Ctrl+Alt+N); silenciar, saltar y vaciar desde la página.
* Aviso y botón de descarga de **Visual C++** si a Windows le faltan librerías para las voces locales.

### Otros cambios
* **Contador de muertes y ajustes que ya no se pierden**: se guardan en la carpeta del usuario y junto al programa, con copia de respaldo. Corrige que no se guardaran mientras no se colocaran todos los overlays.
* **Combos de TikTok**: cuando TikTok reinicia el conteo de un combo (x1, x1…), lo anterior se acumula en vez de perderse.
* **Acerca de**: Discord y YouTube de HiveShock, Instagram del desarrollador y sección de equipo (testers, soporte, administradores).
* Ayuda actualizada con todo lo de Voz.
* Scripts de versión con versiones preliminares (alpha/beta/rc) y sin estropear acentos en Windows.
* Pruebas automáticas en el CI.

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
