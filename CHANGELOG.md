# Changelog

Cambios relevantes de HiveShock. El versionado se encuentra en `Directory.Build.props`.

## [Unreleased]

## [2.5.1] - 2026-10-16

### Guardado
* **Arreglo:** los comandos (y las reglas de bits, likes, seguir, compartir, regalos y metas) podían perder su efecto al cambiar de página, por ejemplo de Twitch a TikTok. Los selectores de efecto devolvían un valor vacío al quedarse sin opciones y el guardado automático lo tomaba como una edición. Ahora ese vacío se ignora.
* Un comando sin efecto («Ninguno») ya no se borra al guardar: conserva su palabra. En vivo no hace nada ni gasta el tiempo de espera del espectador.

## [2.5.0] - 2026-10-16

### Perfiles propios
* **Nuevo botón «+ Nuevo perfil» en Inicio.** Crea un perfil tuyo, sin juego del catálogo: solo comandos de chat y macros de teclas. Puede empezar en blanco, con un comando de ejemplo (`!saltar`) o como copia del perfil actual.
* Los perfiles propios se guardan en tu carpeta de datos, así que las actualizaciones no los tocan. Llevan la etiqueta PROPIO y se pueden borrar (la carpeta se mueve a `profiles-deleted` por si fue un error). Los perfiles del catálogo no se pueden borrar.
* El perfil activo se ve siempre en la barra superior, para saber a qué perfil pertenecen los regalos, comandos y metas que editas.
* La página Eventos avisa en rojo de los comandos o reglas cuyo efecto ya no existe (por ejemplo, si borraste la macro a la que apuntaban).

### Teclas
* **Las macros y la ventana del juego ahora son globales:** ya no dependen del perfil elegido. Se guardan en `macros.json` en tu carpeta de datos (con una copia junto al programa). La primera vez se importan las del perfil activo; los `keys.json` antiguos no se tocan.

### Guardado
* **Arreglo:** los comandos y las macros recién creados podían perderse al cambiar de pestaña, cambiar de juego o cerrar la app. Ahora lo pendiente de todos los editores se guarda antes de navegar, cambiar de perfil, guardar el perfil o los efectos, y al cerrar; si algo no se puede guardar se avisa en vez de perderlo en silencio.
* Recargar desde disco ya no pisa lo que acabas de escribir, y un guardado en espera ya no se escribe en el perfil equivocado.
* **Escritura segura:** `gifts.json`, `profile.json`, `effects.json` y `active-profile.txt` se escriben a un temporal y se reemplazan, dejando la copia anterior como `.bak`. Si un archivo queda cortado o vacío, se recupera el `.bak` y el dañado se conserva como `.corrupt`.

## [2.4.1] - 2026-10-15

### Actualizaciones
* **HiveShock ya no descarga nada por su cuenta.** Solo te avisa de que hay una versión nueva (chip arriba, punto en el menú y línea en «Qué está pasando»); la versión se descarga, se verifica e instala cuando tú pulsas Instalar. Se retira la casilla «Descargar solo la versión nueva en segundo plano».

## [2.4.0] - 2026-10-15

### Teclas
* **Nueva página «Teclas»** (solo Windows): crea macros que pulsan teclas en la ventana del juego. Cada macro es una secuencia de pasos (pulsar, mantener, dejar pulsada, soltar) con repeticiones, esperas y una variación aleatoria opcional de los tiempos.
* **Cada macro es un efecto más:** aparece en los selectores de Regalos, Chat y Eventos, así que `!saltar`, un regalo, bits, un follow o una meta pueden disparar teclas. Mantienen el cooldown, el anti-spam, el bloqueo de espectadores y la pausa de emergencia que ya tenían.
* **Funciona aunque estés en otra ventana:** HiveShock trae el juego al frente, envía las teclas y te devuelve a la ventana que estabas usando. Si hay macros seguidas, no te devuelve el foco entre una y otra.
* **Ventana del juego por perfil** (proceso, parte del título o expresión regular), con selector de ventanas abiertas y un indicador de estado: lista, minimizada, no encontrada o juego ejecutándose como administrador (en ese caso Windows exige que HiveShock también lo sea; se avisa).
* **Atajo global opcional por macro** (por ejemplo `Ctrl+Alt+F5`) para lanzarla a mano desde cualquier ventana. Exige un modificador para no quitarle esa tecla al juego; si otra aplicación ya lo usa, se avisa.
* **Sin teclas pegadas:** al cancelar, fallar, pausar o cerrar, se sueltan todas las teclas que la macro dejó pulsadas. Hay un tope de duración por macro.
* **Combinaciones bloqueadas por defecto:** Win, Alt+F4, Alt+Tab, Ctrl+Alt+Supr y Ctrl+Esc; se pueden permitir en una macro concreta.
* Los cambios en las macros valen al instante, también con el puente conectado. Se guardan en `keys.json` dentro de la carpeta del perfil.
* Arreglo: guardar el perfil desde su editor ya no borra la ventana del juego de `profile.json`.

## [2.3.1] - 2026-10-12

### Actualizaciones
* La lista de versiones ya no muestra el botón «Ver en GitHub»: cada versión solo ofrece instalarla.

## [2.3.0] - 2026-10-12

### Actualizaciones
* **Nueva página «Actualizaciones»** (menú INFO). HiveShock consulta los releases de GitHub al abrir y cada pocas horas, y avisa con un chip «Nueva versión X» arriba, un punto en el menú y una línea en «Qué está pasando». Un solo aviso por versión; puedes omitir una.
* **Descarga sola en segundo plano** la última versión estable y la verifica (SHA-256 y zip completo); instalar es siempre un clic tuyo: HiveShock nunca se cierra solo en medio de un directo. Se reanuda si se corta la conexión.
* **Instala la última o cualquier versión publicada**, incluida una anterior (con confirmación). Casilla para incluir versiones de prueba (alpha, beta, rc), apagada por defecto.
* **A prueba de fallos:** se respalda lo que se va a reemplazar, tus perfiles, regalos, metas y credenciales no se tocan, y si la versión nueva no arranca bien se restaura la anterior sola y te lo cuenta. Funciona en Windows, macOS y Linux (en macOS y Linux se ha cubierto con pruebas automáticas; confirma en un equipo real).
* Los releases publican un `SHA256SUMS.txt`. Para instalar 2.3.0 hay que hacerlo a mano una vez: las versiones anteriores no traen el actualizador.

## [2.2.0] - 2026-10-08

HiveShock vuelve a ser solo el puente entre tu live y el juego. La **voz** y **Zeldatón** (con su cronómetro) salen de este programa; la voz pasará a ser una app aparte. Se quedan todas las mejoras que no dependían de ellas.

### Retirado
* **Voz (Smart TTS)**: página Voz, motores Edge, Piper y Windows, micrófono y atajos. Con esto HiveShock compila un único `net10.0` para Windows, macOS y Linux.
* **Zeldatón**: conexión con el servidor de la carrera, reloj oficial, cierre del juego, tiempo por donaciones y el cronómetro en pantalla.
* Los archivos que dejaron esas funciones en tu carpeta de datos se ignoran y no estorban.

### Moderación
* **Pausa de emergencia** (botón en la barra de arriba y en Moderación): los espectadores dejan de activar efectos y metas, y lo que ya esperaba turno se descarta. No se guarda: al abrir HiveShock siempre empieza reanudada. Las pruebas del streamer no se ven afectadas.
* **Lista de bloqueo** (nueva página Moderación): ves quién activó efectos hace poco y lo bloqueas con un clic, o bloqueas por @usuario (TikTok o Twitch). Se reconoce por su **id**, así que cambiar de @usuario no lo libra, y otra persona que se quede con ese @usuario no queda bloqueada. Un bloqueado no activa efectos ni metas y no cuenta como seguidor.
* **Anti-spam de seguir más difícil de esquivar**: el efecto de seguir se decide por el id de la persona. Cambiar de @usuario ya no permite repetir el efecto, y un evento sin id se reconoce por el @usuario recordado. Si la plataforma no manda ni id ni @usuario, el follow pasa sin recordarse (y se avisa una vez) en vez de bloquear a inocentes con el mismo nombre.

### Cola de enemigos y entrega
* **Los enemigos de los regalos ya no se pierden ni se borran para hacer sitio.** El juego (Shipwright) lleva un presupuesto de carga (`gRemote.HiveShock.MaxLoad`, 14 por defecto; cada enemigo pesa 1–4): los que no caben esperan en orden y entran conforme mueren otros. La cola sobrevive a cambios de escena, los enemigos vivos te siguen a la siguiente escena y la cola se guarda en disco (caduca a las 12 h). Ver `docs/spawn-queue.md`.
* **Reintentos si el juego está cerrado:** cada efecto se reintenta hasta `GAME_RETRY_SECONDS` (600 s por defecto; 0 = no reintentar) en vez de perderse. La pausa de emergencia sigue descartando lo pendiente.
* Nuevos eventos del juego `spawn_queue` (disponible en `BridgeRuntime.SpawnQueue`) y `spawn_rejected`, y acción `clear_queue`.

### Efectos y guardado
* Nuevo efecto **Ralentizar a Link** (`slow_down`) en el perfil Ocarina of Time: baja la velocidad de Link durante unos segundos (intensidad ajustable de 0.2 a 0.9).
* **Guardado automático** en Regalos, Metas y Eventos con una etiqueta que dice si se guardó (verde), está pendiente (dorada) o falló (roja). Se puede desactivar.
* Metas: botón **Del catálogo** para añadir regalos a una meta.
* **Anti-spam de seguir**: cada usuario dispara el efecto de seguir una sola vez (TikTok y Twitch), aunque siga y deje de seguir en bucle. Se recuerda entre directos; opción y botón «Olvidar seguidores» en Eventos.
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
