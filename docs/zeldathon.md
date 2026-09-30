# Zeldathon en HiveShock

HiveShock se conecta al servidor de la carrera (repo `Zeldaton-web`: backend en Rust + web en Vue) para
sincronizar el reloj oficial y enviar lo que pasa en el juego. El servidor es la **única autoridad** del
tiempo, del ranking y del ganador: HiveShock cuenta hechos y muestra el reloj oficial.

```
Juego (Shipwright) ──JSON líneas :43002──▶ HiveShock ──WSS /ingest (token)──▶ servidor ──REST + WS──▶ web
      ▲  acciones :43000                        │  ▲ CLOCK · GAME_FORCE_CLOSE · ACK · ERROR
      └── quit_game · request_snapshot ◀────────┘  └──▶ cronómetro en pantalla + página «Zeldathon»
```

## Para el corredor
1. Menú **Zeldathon**: pega la dirección del servidor y **tu token** (te lo da el organizador; es solo tuyo).
2. **Conectar**. Con «Conectar automáticamente» queda listo cada vez que abres HiveShock.
3. Abre el juego con HiveShock activado en Enhancements y carga tu partida: la sesión y el reloj empiezan solos.
4. **Cronómetro en pantalla**: casilla en Inicio → Pantalla o en la página Zeldathon. Modo *reloj oficial* (cuenta
   atrás del día, con avisos de color a los 30 y 5 minutos) o *cronómetro manual* (sube/baja, iniciar/pausar/cero).
5. Cuando se acaba el tiempo del día el servidor lo ordena y HiveShock **cierra el juego** (primero un cierre limpio
   que guarda la partida; si sigue abierto a los 10 s, termina el proceso).

Estados del cronómetro: `EN VIVO`, `PAUSADO`, `EN ESPERA` (conectado, sin jugar), `AGOTADO`, `TERMINÓ`,
`SIN CONEXIÓN · RELOJ DETENIDO` (se congela al perder la conexión, igual que hace el servidor).

## Conexión
* Solo cifrada (`https`/`wss`), salvo en la propia máquina (`localhost`) para desarrollo. Ruta: `wss://<host>/ingest`,
  cabecera `Authorization: Bearer <token>`.
* Latido cada 8 s (el servidor da por caído a quien pasa 20 s sin latido). Reconexión con espera creciente (1→30 s).
* Token rechazado (401) o `replaced` (otra conexión con tu token): **no se reintenta**; se avisa en la página.
* Todo mensaje lleva `id`; lo no confirmado se reenvía tras reconectar (el servidor ignora repetidos).
* Ajustes en `.hiveshock-zeldathon.json` (carpeta de datos del usuario + copia junto al programa). El token no se
  escribe en el log.

## Qué se envía (contrato de ingesta)
`HELLO`, `HEARTBEAT`, `SESSION_STARTED/ENDED`, `GAME_PROGRESS`, `AREA_CHANGED`, `ITEM_ACQUIRED`, `BOSS_DEFEATED`,
`STATS_UPDATED`, `GAME_FINISHED`, `CHAT_EVENT` (por lotes) y `STREAM_STATE` (en directo + espectadores de TikTok).
Detalle en `docs/hiveshock-ingest.md` del repo del servidor.

`ZeldathonSession` guarda el estado del juego y envía solo la **diferencia** que el servidor aún no tiene; espera a
que la sesión esté en vivo (el servidor rechaza progreso fuera de sesión) y se corrige sola tras reconectar.
`GAME_FINISHED` solo se manda con el jefe final vencido **y** los 10 objetivos completos.

## Eventos del juego (puerto 43002, una línea JSON por evento)
El juego manda **números**; HiveShock los traduce con `profiles/<perfil>/zeldathon.json` (editable sin recompilar).

| `event` | Campos | Origen en el juego |
| --- | --- | --- |
| `game_session` | `state`: `loaded`/`exited`, `file` | `OnLoadGame` / `OnExitGame` |
| `scene` | `scene` (`SCENE_*`) | al cambiar de escena |
| `inventory` | `items`: lista de `ITEM_*` | ítems de progreso que tiene el jugador |
| `quest` | `items`: máscara `questItems` | medallas y piedras espirituales |
| `stats` | `hearts`, `maxHearts`, `rupees`, `skulltulas` | vida, rupias, skulltulas |
| `boss_defeated` | `actor` (`ACTOR_BOSS_*`) | `OnBossDefeat` |

Acciones hacia el juego (puerto 43000): `request_snapshot` (re-emitir todo el estado; HiveShock lo pide al
conectar) y `quit_game` (cierre limpio; `save: true` guarda antes). **No** están en `effects.json` a propósito: un
regalo de un espectador no debe poder cerrar el juego.

### `zeldathon.json`
`areas` (escena → id), `items` (ítem del juego → id del catálogo), `bosses` (actor → id de jefe),
`objectives.quest|items|bosses` (cómo se completa cada objetivo), `finishBoss` y `gameProcesses` (nombre del proceso
para el cierre forzado). Los ids de ítems y objetivos deben existir en el catálogo del servidor; lo demás se ignora
con un aviso en el log. Si un perfil no trae el archivo se usa la copia de fábrica incluida en el programa.

## Pruebas
* `dotnet test`: reloj, cliente (transporte falso), sesión, mapa, cierre del juego, cronómetro y reporte de stream.
* De punta a punta contra un servidor real (solo si se definen las variables; usa una base de datos de desarrollo):
  ```bash
  # servidor (repo Zeldaton-web)
  ADMIN_TOKEN=<>=16 caracteres> DEV_TOKENS_FILE=dev-tokens.json cargo run -p zeldathon-server
  # en este repo
  ZELDATHON_E2E_URL=http://127.0.0.1:8080 ZELDATHON_E2E_TOKEN=<token de cuaco> \
  ZELDATHON_E2E_ADMIN=<ADMIN_TOKEN> dotnet test --filter ZeldathonE2E
  ```
