# Cola de enemigos y entrega al juego

Los spawns nunca se descartan para hacer sitio. El juego mantiene un presupuesto de carga
(`gRemote.HiveShock.MaxLoad`, 14 por defecto; cada enemigo pesa 1–4) y los que no caben esperan en orden hasta que
muera alguno. La cola sobrevive a cambios de escena y se guarda en `hiveshock_pending.json` (caduca a las 12 h).

Si el juego está cerrado, HiveShock reintenta cada efecto hasta `GAME_RETRY_SECONDS` (600 s por defecto; 0 = no
reintentar). `clear_queue` (puerto 43000) vacía los spawns en espera.

## Eventos del juego (puerto 43002, una línea JSON por evento)

| `event` | Campos | Significado |
| --- | --- | --- |
| `spawn_queue` | `pending`, `active`, `load`, `max` | cola de enemigos: cuántos esperan, cuántos viven y la carga (solo cuando cambia) |
| `spawn_rejected` | `action`, `user` | la cola del juego está llena (tope de 1000): ese spawn **no** se aceptó |

El juego puede emitir otros eventos por este puerto; HiveShock ignora los que no usa.
