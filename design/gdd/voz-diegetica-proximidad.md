# Voz Diegetica de Proximidad — Plan de Implementacion

> **Status**: Design ready for implementation
> **GDD section**: `design/GDD.md` §11.3
> **Authors**: Cris + Codex
> **Last Updated**: 2026-04-27
> **Implements Pillars**: Tension asimetrica, cooperacion bajo presion, engano social, inmersion

---

## 1. Decision de Diseno

La voz en partida sera **diegética, espacial y audible por proximidad para todos los roles**.

El modo default no tendra canal privado global entre presos. Si un preso habla cerca del guardia, el guardia puede escucharlo. Si el guardia habla cerca de los presos, los presos tambien pueden escucharlo. La voz no revela nombre, posicion exacta ni marcador de hablante: solo aporta informacion imperfecta que los jugadores deben interpretar.

Esta decision convierte la comunicacion en una mecanica de riesgo/recompensa:

- Los presos pueden coordinar Ruta 1 de forma natural.
- El guardia gana una herramienta de patrullaje y deduccion sin volverse omnisciente.
- La prision se siente viva porque las conversaciones pertenecen al espacio fisico.
- Hablar en el momento equivocado puede delatar una zona, pero no identifica automaticamente a un jugador.

## 2. Reglas de Gameplay

| Regla | Valor MVP | Notas |
|-------|-----------|-------|
| Tipo de voz | Proximidad 3D | Todos los vivos en la misma sala comparten el mismo mundo sonoro |
| Input | Push-to-talk | Tecla default: `V` |
| Rango de voz normal | 10m | Se tunea en playtest |
| Canal privado presos | No en modo default | Puede existir en modo casual/custom post-MVP |
| Identidad del hablante | Oculta | Sin nombre, subtitulo, icono ni ping de posicion |
| Guardia escucha presos | Si, por proximidad | Solo si esta dentro del rango audible |
| Presos escuchan guardia | Si, por proximidad | Refuerza presencia fisica del guardia |
| Capturados | Sin voz con vivos | Pueden ir a canal espectador o post-game |
| Camaras de seguridad | No transmiten voz | El guardia debe estar fisicamente cerca para escuchar |
| Lobby/post-game | Voz global opcional | Comodidad social fuera de la partida |

## 3. Modelo Espacial

### Rango y Atenuacion

| Modo | Status | Rango | Uso esperado |
|------|--------|-------|--------------|
| Voz normal | MVP | 10m | Comunicacion principal |
| Susurro | Post-MVP | 3–4m | Coordinacion muy segura, requiere cercania |
| Grito | Post-MVP | 15–18m | Emergencias, humor, panico, alto riesgo |

Curva inicial recomendada para voz normal:

- 0–2m: volumen completo.
- 2–10m: caida progresiva.
- >10m: inaudible.
- Paredes/puertas: reduccion fuerte de volumen.
- Pisos diferentes: reduccion adicional si no hay linea abierta.

### Oclusion MVP

La oclusion puede empezar simple:

1. Raycast entre oyente y hablante.
2. Si el raycast golpea pared/puerta/barrote marcado como oclusor, reducir volumen.
3. Si ambos jugadores estan en zonas no adyacentes, reducir aun mas o cortar.

No hace falta simular acustica real. El objetivo es que hablar detras de una pared sea menos claro, no perfectamente fisico.

## 4. Arquitectura Tecnica Recomendada

La voz debe correr separada del estado autoritativo de gameplay.

**Regla tecnica clave:** no enviar audio crudo por Socket.io.

Socket.io puede usarse para:

- unir jugadores al canal de voz de la room;
- intercambiar senales de WebRTC o SDK;
- limpiar conexiones en leave/reconnect;
- publicar mute/deafen local como metadata no critica.

El transporte real de audio debe ser:

1. **WebRTC mesh para MVP**, viable por el limite de 2–4 jugadores.
2. **SFU/servicio de voz** si el spike muestra problemas de estabilidad en WebGL.
3. **SDK Unity compatible con WebGL** si reduce riesgo de integracion y cumple latencia/espacializacion.

Para Unity WebGL, el camino mas pragmatico es:

- Captura de microfono y conexiones de voz en JavaScript/WebRTC.
- Unity envia posiciones y estado de jugadores al bridge JS.
- JS mezcla audio remoto con Web Audio API usando posiciones 3D.
- Unity mantiene la autoridad de roles, captura, estado vivo/capturado y sala.

## 5. Eventos y Contratos

### Socket.io Signaling

| Evento | Direccion | Payload | Uso |
|--------|-----------|---------|-----|
| `voice:join` | Cliente → Servidor | `{ roomId, userId }` | Entrar al grafo de voz de la sala |
| `voice:peers` | Servidor → Cliente | `{ peers: VoicePeer[] }` | Lista inicial de peers conectables |
| `voice:signal` | Cliente ↔ Servidor ↔ Cliente | `{ toUserId, fromUserId, signal }` | Offer/answer/ICE o equivalente |
| `voice:leave` | Cliente → Servidor | `{ roomId, userId }` | Salir y cerrar conexiones |
| `voice:peer-left` | Servidor → Cliente | `{ userId }` | Limpiar peer remoto |
| `voice:state` | Cliente → Servidor → Clientes | `{ userId, muted, deafened }` | Metadata no critica para UI |

### Unity → JS Voice Bridge

| Metodo | Payload | Uso |
|--------|---------|-----|
| `Voice_Init` | `{ roomId, userId }` | Inicializa permisos y signaling |
| `Voice_SetPushToTalk` | `{ active }` | Abre/cierra envio de microfono |
| `Voice_SetLocalMuted` | `{ muted }` | Mute local |
| `Voice_SetListenerPose` | `{ position, forward, up }` | Posicion y orientacion del jugador local |
| `Voice_SetSpeakerPose` | `{ userId, position, alive, captured, role }` | Posicion/estado de cada peer remoto |
| `Voice_Dispose` | `{}` | Cierra conexiones al salir/reconectar |

## 6. Plan de Implementacion por Fases

### Fase 0 — Cierre de Diseno

**Objetivo:** dejar la feature cerrada como sistema de juego antes de escribir codigo.

**Tareas:**

- V0-01: Registrar en GDD que el default es voz espacial 3D audible por todos los roles.
- V0-02: Definir `V` como push-to-talk y mover golpes/tos a post-MVP.
- V0-03: Documentar tunables iniciales: rango 10m, oclusion simple, sin canal privado presos.
- V0-04: Confirmar que lobby/post-game pueden tener voz global no espacial.

**Done:**

- GDD actualizado.
- Este plan existe en `design/gdd/voz-diegetica-proximidad.md`.
- Decision registrada en `memory/decisions.md`.

### Fase 1 — Tech Spike WebGL

**Objetivo:** reducir el riesgo tecnico antes de integrarlo al gameplay.

**Owner sugerido:** unity-specialist + network-programmer.

**Tareas:**

- V1-01: Crear una escena/pagina de prueba que pida permiso de microfono en WebGL.
- V1-02: Probar loopback local con Web Audio API.
- V1-03: Probar conexion WebRTC entre dos tabs o dos browsers usando signaling minimo.
- V1-04: Medir latencia perceptual y estabilidad en Chrome.
- V1-05: Decidir si MVP usa WebRTC mesh, SFU/servicio externo o SDK Unity compatible.

**Done:**

- Dos clientes WebGL se escuchan en una room de prueba.
- El spike confirma estrategia tecnica o documenta bloqueo.
- La decision de transporte queda registrada antes de entrar a produccion.

### Fase 2 — Signaling y Ciclo de Vida de Sala

**Objetivo:** conectar la voz al lifecycle de rooms sin tocar aun la espacializacion.

**Owner sugerido:** network-programmer.

**Tareas:**

- V2-01: Agregar handlers `voice:join`, `voice:leave`, `voice:signal`.
- V2-02: Mantener lista de peers por room usando `userId`, no socket transitorio.
- V2-03: Enviar `voice:peers` al entrar y `voice:peer-left` al salir.
- V2-04: Limpiar peers en disconnect, reconnect timeout y game end.
- V2-05: Agregar tests de signaling basico: join, leave, relay de signal, cleanup.

**Done:**

- El backend enruta signaling solo dentro de la room correcta.
- Reconnect no deja peers fantasmas.
- No se envia audio crudo por Socket.io.

### Fase 3 — Integracion Unity WebGL + JS Bridge

**Objetivo:** que Unity pueda iniciar, controlar y cerrar voz desde la partida.

**Owner sugerido:** unity-specialist.

**Tareas:**

- V3-01: Crear `VoiceChatManager` en Unity con estado: unavailable, permission_pending, connected, muted, error.
- V3-02: Crear bindings WebGL en `.jslib` para `Voice_Init`, `Voice_SetPushToTalk`, `Voice_SetListenerPose`, `Voice_SetSpeakerPose`, `Voice_Dispose`.
- V3-03: Conectar `V` como push-to-talk solo cuando el chat de lobby/input UI no tenga foco.
- V3-04: Pasar `roomId`, `userId`, rol y estado vivo/capturado al bridge.
- V3-05: Cerrar voz en leave, game end, reconnect fallido y cambio de escena.

**Done:**

- Un jugador puede habilitar microfono al entrar a partida.
- `V` abre/cierra transmision.
- Salir de la sala limpia conexiones y permisos activos.

### Fase 4 — Espacializacion y Reglas de Gameplay

**Objetivo:** convertir el audio funcional en una mecanica espacial.

**Owner sugerido:** unity-specialist.

**Tareas:**

- V4-01: Enviar pose del listener local al bridge a 10 Hz.
- V4-02: Enviar pose de peers remotos cuando cambian posicion o estado relevante.
- V4-03: Aplicar atenuacion 0–10m en el mixer JS.
- V4-04: Aplicar paneo/orientacion segun posicion relativa.
- V4-05: Implementar oclusion simple por raycast Unity o por zonas adyacentes.
- V4-06: Silenciar capturados para jugadores vivos.
- V4-07: Deshabilitar transmision de voz por camaras de seguridad.

**Done:**

- La voz se oye fuerte cerca, baja con distancia y desaparece fuera de rango.
- El guardia puede escuchar presos cercanos sin recibir informacion perfecta.
- Presos capturados no pueden coordinar con vivos.

### Fase 5 — UX, Seguridad y Accesibilidad

**Objetivo:** que la feature sea usable y no rompa partidas.

**Owner sugerido:** unity-ui-toolkit + unity-specialist.

**Tareas:**

- V5-01: Agregar estado discreto de microfono local: muted, transmitting, permission denied.
- V5-02: Agregar mute local por jugador desde lobby o pausa si hay UI disponible.
- V5-03: Agregar fallback visible si el browser bloquea microfono.
- V5-04: Evitar indicadores de hablante en gameplay que revelen identidad.
- V5-05: Agregar opcion de desactivar voz localmente.
- V5-06: Agregar volumen de voz separado en opciones.

**Done:**

- Un jugador puede jugar aunque niegue permisos de microfono.
- La UI no delata al hablante.
- Hay control basico para mutear o bajar volumen.

### Fase 6 — QA y Balance

**Objetivo:** validar que la voz mejora el juego sin romper el camuflaje.

**Owner sugerido:** producer + QA + unity-specialist + network-programmer.

**Tareas:**

- V6-01: Probar 1v1, 2v1 y 3v1.
- V6-02: Probar Ruta 1 con presos coordinando por voz.
- V6-03: Probar al guardia patrullando por sonido sin camaras.
- V6-04: Medir si 10m es demasiado generoso o demasiado corto.
- V6-05: Probar oclusion en pasillo, celdas, taller, lavanderia y patio.
- V6-06: Probar reconnect durante voz activa.
- V6-07: Probar captura de un preso que estaba hablando.
- V6-08: Registrar feedback de playtest: "me delato demasiado", "no sirve para coordinar", "el guardia oye demasiado", "no se entiende nada".

**Done:**

- La voz genera decisiones tacticas, no solo ruido.
- El guardia obtiene pistas, pero no identificacion gratis.
- Los presos pueden coordinar sin que el juego se vuelva trivial.

## 7. Tareas Resumidas por Dominio

### Network Programmer

- Implementar signaling `voice:*`.
- Mantener peers por room y `userId`.
- Limpiar peers en disconnect/reconnect/game end.
- Agregar tests de lifecycle.
- Confirmar que ningun audio crudo pasa por Socket.io.

### Unity Specialist

- Crear `VoiceChatManager`.
- Crear JS/WebGL bridge.
- Integrar push-to-talk con input.
- Enviar poses a la capa de audio.
- Aplicar reglas de vivo/capturado y camaras.

### Unity UI Toolkit

- Agregar indicadores locales de microfono.
- Agregar opcion de volumen/mute.
- Evitar UI de hablante en partida.

### Producer / QA

- Coordinar test de 2–4 jugadores reales.
- Validar rangos y oclusion por zona.
- Decidir si susurro/grito entran post-MVP.
- Definir fallback si WebGL voice no llega a tiempo.

## 8. Tuning Knobs

| Knob | Default | Rango seguro | Impacto |
|------|---------|--------------|---------|
| `voice_normal_range` | 10m | 6–14m | Distancia de coordinacion y riesgo |
| `voice_full_volume_range` | 2m | 1–3m | Claridad en conversaciones cercanas |
| `voice_occlusion_multiplier` | 0.35 | 0.15–0.6 | Cuanto atraviesa paredes/puertas |
| `voice_update_rate` | 10/s | 5–20/s | Suavidad espacial vs costo |
| `voice_ptt_key` | V | Configurable | Ergonomia |
| `voice_captured_can_talk_to_alive` | false | false/true custom | Riesgo de ghosting |
| `voice_lobby_global` | true | true/false | Comodidad social |

## 9. Riesgos y Mitigaciones

| Riesgo | Impacto | Mitigacion |
|--------|---------|------------|
| Microfono en WebGL falla o pide permisos tarde | Alto | Tech spike antes de produccion; fallback sin voz |
| WebRTC mesh inestable | Alto | Mantener opcion SFU/SDK tras spike |
| Guardia oye demasiado | Medio/Alto | Bajar rango, subir oclusion, sumar ruido ambiente |
| Presos coordinan demasiado facil | Medio | Sin canal privado global; capturados muteados |
| UI delata hablante | Alto | Solo indicador local de transmision; sin nombres ni pings |
| Jugadores usan Discord externo | Medio | La voz in-game debe ser mas inmersiva y tactica; no intentar bloquear comportamiento externo |
| Scope crece con susurro/grito | Medio | MVP solo voz normal PTT |

## 10. Acceptance Criteria

| # | Criterio | Verificacion |
|---|----------|--------------|
| AC-1 | Dos jugadores en WebGL se escuchan en la misma room | Abrir dos clientes, hablar con `V` |
| AC-2 | Un jugador fuera de 10m no escucha voz normal | Separarse en escena y comprobar silencio |
| AC-3 | El guardia escucha presos cercanos sin ver nombre ni marcador | Guardia patrulla cerca de presos hablando |
| AC-4 | Presos no tienen canal privado global en partida default | Separar presos en zonas opuestas y hablar |
| AC-5 | Capturado no puede hablar con vivos | Capturar preso que mantiene `V` |
| AC-6 | Reconnect limpia y reconstruye peers | Recargar un cliente durante partida |
| AC-7 | Negar permiso de microfono no rompe gameplay | Bloquear permiso y seguir jugando |
| AC-8 | Socket.io no transporta audio crudo | Revisar eventos: solo signaling/control |
| AC-9 | Oclusion reduce voz a traves de paredes | Hablar desde sala contigua |
| AC-10 | Voice volume es configurable | Cambiar volumen y verificar mezcla |

## 11. MVP Cuts

Si el tiempo se complica, mantener en este orden:

1. PTT con WebRTC funcional.
2. Rango por distancia.
3. Mute de capturados.
4. Indicador local de microfono.
5. Oclusion simple.

Cortar primero:

- Susurro y grito.
- Golpes/tos como señales dedicadas.
- Mute individual por jugador si no hay UI de pausa lista.
- Oclusion fina por materiales.
- Canal global de lobby si retrasa la voz en partida.

## 12. Open Questions

| Pregunta | Recomendacion inicial |
|----------|-----------------------|
| ¿PTT o voice activation? | PTT default; voice activation solo como setting casual/accesibilidad |
| ¿El guardia puede hablar? | Si, por proximidad, porque refuerza presencia y humor |
| ¿Los presos capturados oyen vivos? | Pueden escuchar como espectadores, pero no hablarles |
| ¿Discord externo invalida el sistema? | No; el modo default debe ser la experiencia intencionada, aunque grupos casuales usen herramientas externas |
| ¿Voz en camaras? | No para MVP; el guardia debe patrullar para escuchar |
