# Changelog

Cambios relevantes de HiveShock. El versionado se encuentra en `Directory.Build.props`.

## [Unreleased]

## [2.11.0] - 2026-10-25

### Varios efectos en todo lo que dispara efectos
* **Lo que ya podían hacer los regalos, ahora también:** comandos de chat (TikTok y Twitch), nuevos seguidores (TikTok y Twitch), likes, compartir, bits de Twitch y metas pueden lanzar varios efectos. Bajo el efecto principal aparece «Añadir otro efecto», cada uno con sus parámetros y una espera opcional en segundos (hasta 30 s, contada desde el efecto anterior). En las listas (comandos, bits, metas) el editor sale al seleccionar la fila y la fila muestra «+2 efectos».
* Salen en orden y la espera no frena a los demás espectadores. Si se pausan los efectos, los que seguían esperando se descartan. `reiniciar_pc` siempre va el último.
* «Simular» una meta y «Enviar al juego» de un regalo con meta ahora lanzan todos los efectos de la meta.
* El aviso de «Sin efecto» de Eventos revisa también los efectos adicionales.
* **Compatible hacia atrás:** con un solo efecto el archivo queda exactamente como antes. Con varios se añade `effects` junto a `effect` (que sigue siendo el primero). Excepción: un comando de chat con varios efectos se guarda como objeto (`{ "effect": …, "effects": [...] }`) y una versión anterior a la 2.11 no lo reconoce, así que lo ignoraría; si quitas los efectos extra vuelve a guardarse como texto.

## [2.10.0] - 2026-10-25

### Overlays para OBS por URL
* **Alertas, regalos, metas y contador ya se pueden usar como «Fuente de navegador» de OBS.** HiveShock abre un servidor local y en Estudio → «OBS (navegador)» copias la dirección de cada overlay (`http://127.0.0.1:7878/overlay/alertas`, `/regalos`, `/metas`, `/contador`) y la pegas en OBS: fondo transparente, sin ventanas sueltas en el escritorio y sin captura de ventana.
* **Alertas nuevas:** aparece una tarjeta animada con la imagen del regalo, quién lo envió y el combo (`Maria envió Doughnut ×3`), y otra cuando alguien te sigue. Si llegan varias, se encolan. Botón «Probar alerta» para verlas en OBS sin esperar a un regalo.
* Extras en la dirección: `?sonido=1` (aviso sonoro), `?duracion=8` (segundos en pantalla), `?tipos=gift` o `?tipos=follow`, y en cualquier overlay `?escala=1.2` y `?fondo=0` (sin tarjeta).
* Usan el mismo aspecto que ya configuras (colores, tamaños, alineación, fondo y opacidad) y se actualizan solos al cambiar un regalo, una meta o el contador. Las metas muestran los iconos del catálogo y una barra de progreso.
* Solo escucha en este equipo. «Permitir verlos desde otro PC de la red local» lo abre a la red y entonces la dirección lleva una clave (`?k=…`) sin la cual no responde. El puerto se puede cambiar (por defecto 7878) y, si está ocupado, la app lo avisa.
* Las ventanas nativas de siempre siguen funcionando igual.
* El servidor solo entrega las imágenes de regalos que la app registra, nunca archivos arbitrarios del disco.

## [2.9.1] - 2026-10-23

### Android
* **El APK ya se publica en cada release** (`HiveShock-<ver>-android.apk`), junto a Windows, macOS y Linux y con su suma en `SHA256SUMS.txt`.
* **Aviso de versión nueva** en Acerca de: busca en los releases y abre la descarga. No baja nada solo.
* **Perfiles propios**: copia el perfil activo desde la pestaña Perfil y bórrala cuando quieras (se guarda en `profiles-deleted`).
* Los perfiles del catálogo (Super Mario 64, etc.) llegan igual que en escritorio: se instalan solos al abrir, sin pisar los tuyos ni resucitar los que borraste.
* El `versionCode` sale de la versión de HiveShock (antes se quedó fijo en la 2.2.0, lo que habría impedido actualizar).
* Más robusto: sin copias de seguridad de Android (guarda tokens), los errores de copia de archivos y del servicio en segundo plano ahora aparecen en el log, y el servicio se cierra limpio al llegar al límite de 6 h de Android 15.
* Incluye todo lo de la 2.9.0 (varios efectos por regalo): la 2.9.0 salió sin esta parte de Android y la 2.9.1 junta las dos.

## [2.9.0] - 2026-10-23

### Varios efectos por regalo
* **Un regalo puede lanzar varios efectos.** En Regalos, bajo el efecto principal, «Añadir otro efecto»: cada uno con sus propios parámetros y una espera opcional en segundos (contada desde el efecto anterior, máximo 30 s). Salen en orden; la espera no frena a los demás espectadores.
* Con «Repetir el efecto en cada uno», el conjunto completo se repite por cada regalo del combo (A B C A B C). Los efectos de una sola vez (borrar partida, reiniciar el equipo) salen solo en la primera vuelta, y reiniciar el equipo siempre va el último. Un regalo no encola más de 200 efectos.
* «Enviar al juego» prueba todos los efectos del regalo. En la lista, los regalos con varios muestran «N efectos».
* **Compatible hacia atrás:** un regalo con un solo efecto se guarda exactamente igual que antes. Con varios se añade `effects` a `gifts.json` y `effect`/`params` siguen siendo el primero, así que una versión anterior al menos lanza ese. Tus regalos y perfiles actuales no cambian.
* Si en `gifts.json` un efecto de la lista no existe se omite con aviso. Lo mismo para chat, bits, seguir, compartir, likes y metas llegará en una versión próxima.

## [2.8.0] - 2026-10-21

### Metas
* **El overlay de metas enseña el regalo de cada meta.** Encima del título de la meta aparece el icono del regalo (hasta 4) tomado del catálogo, con su nombre al pasar el ratón, para que quien mira sepa qué regalo enviar. Si el regalo no tiene imagen en el catálogo se escribe su nombre.

## [2.7.3] - 2026-10-21

### Perfiles
* **La lista de efectos ya no repite entradas.** Los perfiles de Ocarina of Time, Majora's Mask, Banjo-Kazooie y Super Mario 64 traían varios ids que hacían lo mismo (`instakill`/`kill`, `wipe_save`/`nuke_save`/`new_run`, `warp_random`, `times_up`/`time_zero`, `enemy`, `banana_peel`…) y cada uno salía como una línea igual. Se dejó un único id por acción.
* Los perfiles que ya tienes instalados no se modifican: si quieres limpiar la lista en ellos, reasigna lo que usaba esos ids y bórralos del perfil, o reinstala el perfil.

## [2.7.2] - 2026-10-21

### Los perfiles nuevos llegan a todos al actualizar
* **Quien venía de la 2.6.1 o anterior ahora recibe los perfiles que le faltan** (Super Mario 64, Banjo-Kazooie…). Su actualizador viejo protege toda la carpeta `profiles/`, así que los perfiles viajan también en `profiles-catalog/` (una carpeta que sí se copia) y HiveShock los instala solo al arrancar.
* **No cambia nada de lo que ya tienes.** Solo se instalan los perfiles que no existen en tu carpeta `profiles/`; los demás, con sus regalos, comandos y ajustes, quedan exactamente igual. Un perfil que borres a propósito no vuelve.
* Tras actualizar, abre HiveShock una vez para que los instale; el log lo dice («Perfiles nuevos instalados: …»).

## [2.7.1] - 2026-10-21

### Recuperar lo que pasó durante un corte
* **Twitch: seguidores recuperados.** Al reconectar tras un corte de más de 5 s, HiveShock consulta a Twitch los seguidores nuevos de ese tramo (EventSub no los reenvía) y los procesa una sola vez; si ya se habían visto en vivo, no se repiten. Usa el permiso que ya tenías (`moderator:read:followers`). Si falta, el log lo avisa y el resto sigue igual.
* **TikTok: reanudación experimental.** Con `HIVESHOCK_TIKTOK_RESUME=1` en el `.env`, al reconectar se manda a TikTok el último punto leído (cursor) por si reenvía lo perdido. TikTok no documenta esto, así que viene **apagado**: el log indica «TikTok reenvió un regalo de antes de reconectar» y cuántos, para saber con datos reales si funciona. Lo reenviado pasa por el registro de la 2.7.0: lo ya ejecutado se ignora y lo nuevo, dentro de la ventana, se ejecuta.
* El cursor solo se usa en la misma sala y durante 10 minutos; se borra al cerrar el live.

## [2.7.0] - 2026-10-21

### Regalos sin duplicados y sin perder lo ya recibido
* **Cada mensaje se ejecuta una sola vez.** HiveShock lleva un registro (`.hiveshock-ledger.jsonl`, en tu carpeta de datos) con el id de cada regalo, comentario y evento de Twitch. Si TikTok o Twitch reenvían algo al reconectar, el repetido se ignora. Antes un regalo reenviado o el historial de chat que TikTok manda al reconectar podían ejecutarse de nuevo.
* **Los comandos de chat viejos no se ejecutan** (más de 60 s de retraso) y los regalos que llegan con más retraso que la ventana de reintento del juego (mínimo 2 min) se ignoran con un aviso. Se mide contra el retraso de los mensajes en vivo, así que un reloj del equipo desajustado no descarta regalos buenos.
* **Lo aceptado y no entregado sobrevive a un cierre.** Si cierras HiveShock (o falla) con efectos en cola porque el juego estaba apagado, al volver a abrirlo se entregan una vez, siempre que no hayan caducado. Desconectar a propósito o la pausa de emergencia los descartan. Los efectos del sistema (reiniciar el equipo) y las macros de teclas nunca se repiten.
* **Cortes de conexión avisados.** El corte se detecta antes (45 s sin señal en vez de 90) y al reconectar el log y el estado del canal dicen cuánto duró. TikTok y Twitch no reenvían lo que pasó durante un corte, así que esos regalos no se pueden recuperar; lo que sí se garantiza es no duplicar nada.

## [2.6.3] - 2026-10-20

### Perfiles
* **Nuevo perfil «Banjo-Kazooie (Recompiled)».** Efectos sobre la salud, vidas, huevos y plumas, y enemigos (Snippet, Bigbutt, Shrapnel, murciélago), con regalos de ejemplo. Requiere instalar el mod HiveShock para Banjo-Kazooie: Recompiled y activarlo en su menú de mods.
* **Super Mario 64:** nuevo efecto para borrar la partida (`delete_save`). El perfil ahora declara que lo soporta. No se puede deshacer, así que no los asignes a regalos baratos.
* Los perfiles nuevos llegan a quien actualiza desde la app; los que ya tenías no se modifican.

## [2.6.2] - 2026-10-20

### Actualizar trae los perfiles nuevos
* **Al actualizar desde la app ahora se instalan los perfiles nuevos del catálogo** (por ejemplo Super Mario 64). Antes el paquete de actualización no los traía y el actualizador protegía toda la carpeta `profiles/`, así que quien ya tenía HiveShock nunca los recibía.
* **Tus perfiles no se tocan:** solo se copian las carpetas de perfil que todavía no existen. Los que ya tienes conservan tus regalos, comandos y ajustes. Si algo falla y se deshace la actualización, también se quitan los perfiles recién instalados.
* **Para recibirlos** hay que instalar una vez esta versión de la forma normal; las actualizaciones siguientes ya traerán los perfiles nuevos.

## [2.6.1] - 2026-10-20

### Limpieza de nombres
* **Se quitó el nombre antiguo «CrowdControl / CrowdBridge / CC»** del código, los logs y la documentación. El servidor local de pruebas (el «Puerto pruebas» de Inicio) pasa a llamarse *TestServer* y su ajuste `testPort`.
* **Nada se rompe:** los `profile.json` con `crowdControlPort`, y las variables `CC_PORT`, `CROWDBRIDGE_PROFILE`, `DISABLE_CC` y el flag `--no-cc` se siguen leyendo. Al guardar, el perfil usa `testPort`. Los nombres nuevos son `HIVESHOCK_TEST_PORT`, `HIVESHOCK_PROFILE`, `HIVESHOCK_NO_TEST` y `--no-test`.

## [2.6.0] - 2026-10-19

### Reiniciar el equipo (solo Windows)
* **Nuevo efecto «Reiniciar el equipo (con cuenta atrás)»,** disponible en los selectores de Regalos, Chat, Eventos, Bits y Metas. En macOS y Linux todavía no existe y no aparece.
* **Empieza desarmado en cada sesión.** Se arma en Moderación escribiendo REINICIAR; al reabrir HiveShock vuelve a estar desarmado. Al armarlo se asigna el regalo **León (Lion)** como disparador si tu perfil no lo usa ya.
* **Cuenta atrás visible y cancelable:** de 10 a 300 segundos (30 por defecto), con un aviso rojo en la ventana, un botón Cancelar y el atajo global **Ctrl+Alt+Shift+F12**, que funciona aunque estés dentro del juego y solo existe mientras cuenta.
* **Salvaguardas:** solo una vez por sesión, no se ejecuta con «Solo simular» ni con la pausa de emergencia (que además lo cancela), y desconectar el puente también lo cancela.
* **Forzar** (opcional) cierra los programas sin preguntar, así que se pierde lo que no esté guardado. Sin forzar, Windows puede esperar a que cierres lo pendiente.
* Un botón permite **probar la cuenta atrás sin reiniciar**.

## [2.5.2] - 2026-10-19

### Super Mario 64
* **Nuevo perfil «Super Mario 64 (Ghostship)».** 37 efectos: cáscara de plátano, escopeta (se equipa al pasar y dispara con L), enemigos (Goomba, Bob-omb, Chuckya que persigue por todo el terreno, hongo venenoso, Amp, Fly Guy, bolas de boliche), efectos sobre Mario (fuego, descarga, empujón, lanzamiento, gorras) y temporales (gravedad baja o alta, turbo). Incluye regalos de ejemplo y las etiquetas en español.
* Se activa en Ghostship desde **Network > HiveShock**. Las muertes de Mario vuelven al puente por el puerto de eventos (43002).

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
