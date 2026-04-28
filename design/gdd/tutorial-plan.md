# Jailbreak - Tutorial Interactivo de 60s

**Status:** Ready for Implementation
**Ubicacion:** Escena dedicada `TutorialScene`
**Duracion:** 60 segundos autoritativos despues de que el host presiona Start Game
**Flujo:** Lobby -> Start Game -> asignacion de roles -> TutorialScene -> GameScene
**Rol principal:** Enseñar la fantasia jugable antes de la primera jornada real

---

## 1. Overview

El tutorial de Jailbreak es una **antesala jugable de 60 segundos** que ocurre despues de que el host presiona **Start Game**. En este punto, el backend ya asigno roles y Unity carga una escena compacta de entrenamiento. La escena no reemplaza el lobby: es una micro-version de la prision donde cada jugador practica lo minimo necesario para entender la partida real.

El objetivo no es explicar todos los sistemas, sino enseñar el loop central:

```text
Preso: seguir rutina -> encontrar objeto -> guardarlo -> hacer accion riesgosa -> volver a camuflarse
Guardia: observar rutina -> detectar anomalia -> acercarse -> sostener foco -> capturar o equivocarse
```

Todo lo que ocurre en el tutorial es descartable. Al terminar los 60s, el backend limpia inventario, errores, capturas, progreso de ruta y posiciones, y recien ahi empieza la partida real.

---

## 2. Player Fantasy

### Preso

"Estoy rodeado de NPCs que parecen saber exactamente que hacer. Si camino, como, trabajo o me siento como ellos, paso desapercibido. Pero mi plan de fuga me pide hacer cosas prohibidas: agarrar herramientas, revisar zonas peligrosas y sabotear sistemas. Tengo que actuar, esconder la evidencia y volver a la rutina antes de que el guardia lea mi comportamiento."

### Guardia

"La prision esta viva y todos se parecen. Mi trabajo no es hacer click a cualquiera: tengo que leer movimientos, detectar quien rompe la rutina y acercarme lo suficiente para sostener una captura. Si acuso mal tres veces, pierdo automaticamente."

---

## 3. Reglas del Tutorial

1. El tutorial empieza inmediatamente despues de **Start Game**.
2. El backend emite un estado transitorio `Tutorial` o `LobbyTutorial` con duracion de **60s**.
3. Unity carga `TutorialScene`.
4. Los jugadores conservan su rol asignado: `prisoner` o `guard`.
5. Cada rol recibe una UI de misiones distinta.
6. Los NPCs ejecutan una mini-rutina visible: comer, sentarse, trabajar, lavar, caminar, dormir.
7. Las interacciones usan los mismos controles de la partida real.
8. Capturas, errores, objetos y progreso del tutorial no afectan la partida real.
9. Al terminar el timer, Unity carga la escena principal y el backend inicia la jornada real limpia.

### Controles que debe enseñar

| Accion | Tecla/Input | Rol | Enseñanza |
|---|---|---|---|
| Moverse | WASD + Mouse | Ambos | Navegacion FPS basica |
| Correr | Shift | Ambos | Correr sirve, pero los NPCs normales no corren |
| Ver/ocultar misiones | TAB | Presos | Consultar la ruta y objetivos activos |
| Interactuar / recoger | E | Ambos | Usar objetos, sentarse, inspeccionar, agarrar |
| Slot anterior/siguiente | K / L | Presos | Elegir slot sin guardar ni equipar automaticamente |
| Guardar objeto | F | Presos | Pasar objeto en mano al slot seleccionado |
| Soltar objeto guardado | G | Presos | Soltar herramienta desde el slot seleccionado |
| Capturar/acusar | Click izquierdo sostenido | Guardia | Foco de captura sobre sospechoso |

**Nota de TAB:** en este tutorial compacto, TAB prioriza el panel de misiones de preso. Si el modo camaras del guardia ya existe, el panel del guardia debe quedar fijo o usar otro acceso contextual para no competir con las camaras.

---

## 4. Estructura de la Escena

`TutorialScene` debe sentirse como una postal jugable de la prision completa, no como una sala blanca. El layout recomendado es un pasillo corto con cuatro zonas conectadas y visibles rapidamente.

```text
┌──────────────────┬──────────────────┐
│ Celdas           │ Oficina Guardia  │
│ catre + NPC sleep│ escritorio       │
├──────────────────┼──────────────────┤
│ Lavanderia/Taller│ Sala tecnica     │
│ ropa + carro     │ power supply     │
├──────────────────┴──────────────────┤
│ Comedor: comida, mesa, bacha        │
└─────────────────────────────────────┘
```

### Zonas requeridas

| Zona | Funcion de tutorial | Objetos clave | NPCs sugeridos |
|---|---|---|---|
| Celdas | Introducir rutina e interacciones inocentes | Catre usable, punto de spawn preso | 1 durmiendo, 1 idle |
| Comedor | Enseñar camuflaje social basico | Estacion comida, mesa, bacha/deposito | 2 comiendo |
| Lavanderia/Taller | Enseñar trabajo, inventario y escondite | Pila ropa, lavadora, estanteria, mesa trabajo, carro ropa | 2 trabajando |
| Oficina Guardia | Enseñar riesgo de invadir zona prohibida | Escritorio inspeccionable | 0-1 NPC cerca como distraccion |
| Sala tecnica | Enseñar sabotaje/progreso/ruido | Power supply o servidor de practica | 0 |

### NPCs de tutorial

Cantidad recomendada: **6 a 8 NPCs**.

Rutina sugerida:

- 2 NPCs hacen flujo comedor: agarrar comida -> sentarse -> dejar bandeja.
- 2 NPCs hacen flujo lavanderia: agarrar ropa -> lavar -> llevar a estanteria.
- 1 NPC trabaja en mesa de trabajo.
- 1 NPC duerme en celda.
- 1 NPC camina de transicion entre zonas.
- 1 NPC "sospechoso falso" rompe levemente la rutina para que el guardia practique dudar.

Los NPCs no deben correr. Si alguien corre, debe sentirse como una senial humana visible.

---

## 5. Interactuables Priorizados

### P0 - Deben estar en la escena

| Interactable | Rol que lo usa | Motivo de diseno | Resultado esperado |
|---|---|---|---|
| Estacion de comida | Preso/NPC | Enseña rutina publica | Jugador agarra comida con E |
| Mesa/asiento | Preso/NPC | Enseña camuflaje social | Jugador se sienta con E |
| Bacha/deposito | Preso/NPC | Cierra mini-rutina comedor | Jugador deja comida/bandeja |
| Estanteria o mesa de trabajo | Preso/NPC | Enseña inspeccion normal y busqueda | Puede revelar herramienta tutorial |
| Herramienta de practica | Preso | Enseña pickup + inventario | Queda en mano al presionar E |
| Carro de ropa | Preso | Enseña cortar vision/esconderse | Oculta al jugador 3-5s |
| Escritorio del guardia | Preso/Guardia | Enseña zona riesgosa y pista | Interaccion con progreso corto |
| Power supply/servidor | Preso | Enseña sabotaje de Ruta 1 | Barra de progreso + ruido/alerta |
| NPC capturable | Guardia | Enseña error de acusacion | Suma error de practica |
| Jugador preso capturable | Guardia | Enseña captura correcta | Feedback de captura tutorial |

### P1 - Buenos si hay tiempo

| Interactable | Motivo |
|---|---|
| Catre para dormir | Refuerza que dormir es rutina inocente |
| Pila de ropa | Da variedad a lavanderia |
| Lavadora | Practica interaccion con progreso |
| Estanteria de ropa planchada | Cierra mini-rutina laboral |

### Interacciones descartadas del tutorial compacto

No hace falta enseñar todas las acciones posibles en 60s. Algunas quedan como interacciones libres si estan ya implementadas, pero no deben bloquear las misiones:

- Dormir en la celda.
- Lavar ropa completo.
- Llevar ropa planchada a la estanteria.
- Trabajar largo en mesa de trabajo.

---

## 6. Misiones del Preso

La UI del preso se muestra como checklist de entrenamiento. Debe poder expandirse/contraerse con **TAB**. Las misiones se completan aunque otros presos las hagan, salvo las que enseñan inputs personales como guardar/soltar.

### Mision P1 - Revisa la ruta

**Texto UI:** `Pulsa TAB para revisar tu plan de escape.`
**Accion:** Presionar TAB.
**Completa cuando:** El panel cambia de estado colapsado a expandido al menos una vez.
**Enseña:** El preso no juega a ciegas; consulta misiones y progreso.

### Mision P2 - Pareces uno mas

**Texto UI:** `Agarra comida con E, sientate en la mesa y deja la bandeja en la bacha.`
**Acciones:**

1. Interactuar con comida.
2. Interactuar con asiento.
3. Interactuar con bacha/deposito.

**Completa cuando:** Las tres acciones se ejecutan en orden.
**Enseña:** La rutina publica es camuflaje. El guardia busca a quien no encaja.

### Mision P3 - Corre solo cuando valga la pena

**Texto UI:** `Mantén Shift para correr. Es util, pero los NPCs normales caminan.`
**Accion:** Sprint durante al menos 1s en zona visible.
**Completa cuando:** El cliente detecta sprint local.
**Enseña:** Correr no causa derrota automatica, pero es una pista visual fuerte para el guardia.

### Mision P4 - Guarda el contrabando

**Texto UI:** `Busca una herramienta. Recogela con E, elige slot con K/L y guardala con F.`
**Acciones:**

1. Inspeccionar estanteria o mesa de trabajo.
2. Recoger herramienta con E.
3. Cambiar slot con K o L.
4. Guardar con F.

**Completa cuando:** El backend confirma el item en `inventorySlots[slotIndex]`.
**Enseña:** Diferencia entre objeto en mano y objeto guardado.

### Mision P5 - Suelta sin perder el control

**Texto UI:** `Selecciona el slot de la herramienta y sueltala con G.`
**Accion:** Presionar G con herramienta guardada en el slot seleccionado.
**Completa cuando:** El backend confirma `item:state = dropped`.
**Enseña:** Las herramientas se sueltan desde slots, no directamente desde la mano.

### Mision P6 - Haz algo prohibido

**Texto UI:** `Inspecciona el escritorio del guardia o deshabilita el power supply sin que te vean.`
**Acciones validas:**

- Inspeccionar escritorio del guardia durante 2-3s.
- Deshabilitar power supply/servidor de practica durante 4-6s.

**Completa cuando:** Una de las dos acciones llega a 100%.
**Enseña:** Las acciones de escape requieren tiempo, hacen ruido o exponen al jugador.

### Mision P7 - Rompe la linea de vision

**Texto UI:** `Escondete en un carro de ropa durante unos segundos.`
**Accion:** Interactuar con carro de ropa.
**Completa cuando:** El jugador permanece escondido 3s o sale manualmente tras completar el minimo.
**Enseña:** La defensa principal del preso es cortar vision, mezclarse y reposicionarse.

---

## 7. Misiones del Guardia

El guardia debe recibir un panel distinto. No debe ver checklist de Ruta 1 ni informacion de servidor correcto. El tutorial del guardia se centra en observacion, foco de captura y costo del error.

### Mision G1 - Lee la rutina

**Texto UI:** `Observa a los presos. Los NPCs comen, trabajan, caminan y duermen.`
**Accion:** Mirar hacia una zona con NPCs durante 3s.
**Completa cuando:** El crosshair/camara del guardia permanece orientado hacia NPCs de rutina.
**Enseña:** El guardia juega leyendo patrones, no persiguiendo al azar.

### Mision G2 - Busca una anomalia

**Texto UI:** `Encuentra a alguien corriendo, escondiendose o usando un objeto sospechoso.`
**Accion:** Acercarse a un objetivo marcado internamente como `tutorial_suspicious_target`.
**Completa cuando:** El guardia queda a menos de 4m del objetivo.
**Enseña:** Observar primero, comprometer posicion despues.

### Mision G3 - Captura por foco

**Texto UI:** `Acercate a menos de 2m y manten click izquierdo durante 0.5s.`
**Accion:** Sostener foco sobre un target valido.
**Completa cuando:** Unity completa el radial y envia `guard:catch`.
**Enseña:** La captura no es click instantaneo; requiere distancia, vision y foco continuo.

### Mision G4 - El error cuesta

**Texto UI:** `Si acusas a un NPC inocente 3 veces, pierdes automaticamente.`
**Accion:** Capturar un NPC de practica o ver una simulacion guiada del contador de error.
**Completa cuando:** El contador de errores de tutorial muestra al menos 1 error y la UI explica el limite de 3.
**Enseña:** El guardia debe estar atento; acusar mal tiene consecuencia de derrota.

### Mision G5 - Lee pistas del mundo

**Texto UI:** `Escucha alarmas y revisa zonas donde alguien hizo ruido.`
**Accion:** Recibir cue del power supply o escritorio y entrar a la zona indicada.
**Completa cuando:** El guardia entra en la zona de la cue.
**Enseña:** El guardia no necesita HUD de ruta; debe reaccionar a sonidos, props y comportamiento.

---

## 8. Flujo de 60 Segundos

| Tiempo | Evento | Presos | Guardia |
|---|---|---|---|
| 0-5s | Carga escena + pantalla rol | Ven objetivo general | Ve objetivo general |
| 5-15s | Rutina visible | TAB + comida/sentarse | Observar NPCs |
| 15-30s | Inventario | Buscar herramienta, guardar | Detectar movimientos raros |
| 30-45s | Accion riesgosa | Escritorio o power supply | Acercarse e investigar |
| 45-55s | Foco/escondite | Esconderse o volver a rutina | Practicar captura |
| 55-60s | Transicion | UI avisa inicio de jornada | UI avisa inicio de jornada |

No todos los jugadores deben completar todo. El objetivo de 60s es que cada rol entienda el loop, no que obtenga una calificacion perfecta.

---

## 9. Feedback y UI

### UI de presos

- Panel lateral: `Entrenamiento - Preso`.
- Estado por mision: bloqueada, activa, completada.
- Boton/input destacado solo cuando corresponde.
- Timer global visible: `La jornada empieza en 00:60`.
- Misiones de ruta colapsables con TAB.

### UI de guardia

- Panel lateral: `Entrenamiento - Guardia`.
- Contador de errores visible: `Errores: X _ _`.
- Crosshair con radial de foco.
- Prompt contextual: `Mantener Click - Capturar`.
- Cue visual/audio para power supply o escritorio inspeccionado.

### Feedback audiovisual

| Evento | Feedback |
|---|---|
| Mision completada | Tick corto + check animado |
| Recoger herramienta | Sonido metalico bajo, audible cerca |
| Guardar en slot | Click seco de inventario |
| Soltar herramienta | Sonido de caida local |
| Power supply deshabilitado | Bajon electrico + luz parpadea |
| Guardia enfoca target | Radial central + pulso tenso |
| Guardia acusa NPC | Sonido de error + contador sube |
| 10s restantes | Silbato corto o campana de jornada |

---

## 10. Implementacion por Dominio

### Fase A - Backend: estado de tutorial

**Objetivo:** Agregar un estado transitorio antes de la partida real.

Tareas:

- Agregar estado `Tutorial` o `LobbyTutorial` al flujo de sala.
- Al recibir Start Game:
  - validar jugadores minimos,
  - asignar roles,
  - inicializar tutorial timer de 60s,
  - emitir `tutorial:start`.
- Emitir ticks o payload de timer para HUD.
- Al terminar:
  - limpiar inventario tutorial,
  - limpiar errores/capturas tutorial,
  - limpiar progreso de interacciones tutorial,
  - emitir `tutorial:end`,
  - iniciar `Playing` / jornada real.
- Asegurar que `game:end` no pueda dispararse desde el tutorial.

Eventos sugeridos:

| Evento | Direccion | Payload |
|---|---|---|
| `tutorial:start` | Server -> Clients | `{ duration: 60, role, seed }` |
| `tutorial:state` | Server -> Clients | `{ remainingSeconds, completedMissionIds }` |
| `tutorial:mission:complete` | Client -> Server | `{ missionId }` para misiones client-only |
| `tutorial:end` | Server -> Clients | `{ nextScene: 'GameScene' }` |

### Fase B - Unity: escena y transiciones

**Objetivo:** Crear `TutorialScene` y conectarla al flujo de red.

Tareas:

- Crear escena `TutorialScene`.
- Agregarla a Build Settings antes de `GameScene`.
- Crear spawn points:
  - `tutorial_prisoner_spawn_01..03`,
  - `tutorial_guard_spawn_01`.
- Configurar mini-layout de celdas, comedor, lavanderia/taller, oficina y sala tecnica.
- Crear `TutorialSceneController`:
  - escucha `tutorial:start`,
  - carga escena,
  - instancia/configura rol local,
  - inicia timer,
  - escucha `tutorial:end`,
  - carga `GameScene`.
- Asegurar que la escena principal empiece sin objetos del tutorial.

### Fase C - Unity: interactuables

**Objetivo:** Reusar o adaptar los interactables reales en una version segura de tutorial.

Tareas:

- Colocar `FoodPickupInteractable`.
- Colocar `SeatInteractable`.
- Colocar `TrayDepositInteractable` o bacha equivalente.
- Colocar `TutorialSearchInteractable` en estanteria/mesa.
- Colocar herramienta tutorial compatible con inventario.
- Colocar `LaundryCartHideInteractable`.
- Colocar `GuardDeskTutorialInteractable`.
- Colocar `PowerSupplyTutorialInteractable`.
- Conectar prompts uGUI y barras de progreso.
- Exponer eventos locales para completar misiones:
  - `tutorial.foodPicked`,
  - `tutorial.seated`,
  - `tutorial.trayDeposited`,
  - `tutorial.itemStored`,
  - `tutorial.itemDropped`,
  - `tutorial.deskSearched`,
  - `tutorial.powerDisabled`,
  - `tutorial.hiddenInCart`.

### Fase D - Unity: NPC mini-rutina

**Objetivo:** Dar contexto inmersivo sin depender de toda la jornada real.

Tareas:

- Instanciar 6-8 NPCs.
- Configurar acciones loop:
  - comedor,
  - lavanderia,
  - trabajo,
  - celda,
  - caminata de transicion.
- Asegurar que ningun NPC corra.
- Agregar 1 NPC sospechoso falso opcional.
- Mantener animaciones legibles y exageradas solo lo justo.

### Fase E - UI Toolkit: misiones por rol

**Objetivo:** Mostrar onboarding jugable sin pausar la escena.

Tareas:

- Crear `TutorialMissions.uxml`.
- Crear estilos en `TutorialMissions.uss`.
- Crear `TutorialMissionController.cs`.
- Cargar mission set segun rol.
- Implementar expand/collapse con TAB para presos.
- Mantener panel fijo para guardia si TAB se reserva para camaras.
- Mostrar timer global.
- Reproducir feedback de completitud.

### Fase F - Guardia: captura y errores de practica

**Objetivo:** Enseñar la decision central del guardia.

Tareas:

- Reusar sistema de captura por foco:
  - rango 2.0m,
  - foco 0.5s,
  - reset por target switch, distancia, LOS o input release.
- Permitir target NPC y target preso en tutorial.
- Registrar errores solo en contador tutorial.
- Mostrar explicacion al primer error.
- Simular derrota tutorial al tercer error sin terminar la sala real.
- Resetear contador antes de `GameScene`.

### Fase G - QA y tuning

**Objetivo:** Validar que el tutorial entra en 60s y no contamina la partida.

Tareas:

- Probar 1 guardia + 1 preso.
- Probar 1 guardia + 3 presos.
- Probar que el timer termina aunque nadie complete misiones.
- Probar que desconexion durante tutorial no bloquea inicio real.
- Probar que inventario queda limpio al iniciar `GameScene`.
- Probar que errores de guardia del tutorial no cuentan en partida.
- Probar que capturas de tutorial no eliminan jugadores de la jornada.

---

## 11. Edge Cases

| Caso | Resolucion |
|---|---|
| Un jugador carga lento la escena | El timer es autoritativo; si llega tarde, ve el tiempo restante |
| El guardia captura a todos en tutorial | No termina la partida; solo muestra feedback de practica |
| El guardia acusa 3 NPCs en tutorial | Muestra derrota de entrenamiento, pero no dispara `game:end` |
| Un preso queda escondido al terminar | Se fuerza salida/reset antes de cargar `GameScene` |
| Un preso tiene herramienta al terminar | Backend limpia `heldItemId` e `inventorySlots` |
| Un jugador desconecta durante tutorial | Se mantiene slot de sala; al reconectar recibe estado actual o entra directo a partida si ya empezo |
| Nadie completa misiones | La partida real igual empieza al terminar los 60s |
| Varios presos agarran la misma herramienta | Backend/tutorial controller debe tratarla como practica local o autoritativa sin duplicar estado real |
| TAB compite con camaras del guardia | En tutorial, panel de guardia fijo; TAB queda para camaras si existen |

---

## 12. Tuning Knobs

| Parametro | Valor inicial | Rango sugerido | Motivo |
|---|---:|---:|---|
| `tutorial_duration` | 60s | 45-75s | Mantener ritmo sin saltar aprendizaje |
| `tutorial_food_flow_target` | 3 acciones | 2-3 | Ajustar si comedor se siente largo |
| `tutorial_sabotage_duration` | 5s | 3-8s | Enseñar barra sin consumir media escena |
| `tutorial_desk_search_duration` | 3s | 2-4s | Similar a Ruta 1 real |
| `tutorial_hide_min_duration` | 3s | 2-5s | Enseñar escondite rapido |
| `tutorial_capture_focus_time` | 0.5s | 0.3-0.8s | Igual a partida real |
| `tutorial_capture_range` | 2.0m | 1.5-2.5m | Igual a partida real |
| `tutorial_npc_count` | 8 | 6-10 | Densidad suficiente sin ruido excesivo |

---

## 13. Dependencias

| Sistema | Dependencia |
|---|---|
| Lobby/Matchmaking | Start Game debe asignar roles antes del tutorial |
| Sincronizacion de Estado | Timer y transicion deben ser autoritativos |
| Movimiento FPS | WASD, mouse y Shift |
| Interactables | E, progress bars, prompts |
| Inventario | Mano, K/L, F, G, slots |
| Captura por Foco | Click sostenido, rango, LOS, errores |
| NPC Rutina/NavMesh | Mini-rutinas de comedor/lavanderia/celda |
| UI Toolkit | Panel de misiones y timer |
| Audio | Feedback, cues y silbato |

---

## 14. Acceptance Criteria

1. Al presionar Start Game, todos los clientes cargan `TutorialScene` antes de la partida real.
2. El tutorial dura 60s autoritativos.
3. Los presos ven misiones de preso; el guardia ve misiones de guardia.
4. TAB expande/contrae misiones del preso.
5. Un preso puede practicar comida -> sentarse -> dejar comida.
6. Un preso puede recoger una herramienta, cambiar slot con K/L, guardar con F y soltar con G.
7. Un preso puede completar una accion riesgosa: escritorio o power supply.
8. Un preso puede esconderse en carro de ropa.
9. El guardia puede practicar captura por foco con click izquierdo sostenido.
10. El guardia ve claramente que 3 acusaciones falsas significan derrota.
11. Los NPCs muestran al menos tres rutinas distintas en escena.
12. Al pasar a `GameScene`, inventario, errores, capturas y progreso de tutorial quedan en cero.
13. El guardia no recibe informacion de Ruta 1 ni servidor correcto por HUD.
14. Si nadie completa misiones, la partida real igual comienza.

---

## 15. MVP Cut

Si el tiempo de jam aprieta, el tutorial minimo viable conserva:

1. Timer de 60s post-Start Game.
2. `TutorialScene` compacta.
3. Misiones de preso:
   - TAB,
   - comida/sentarse,
   - recoger/guardar herramienta,
   - power supply.
4. Misiones de guardia:
   - observar,
   - capturar por foco,
   - ver contador de errores.
5. Reset completo antes de la partida real.

Se puede cortar sin romper la experiencia:

- lavar ropa completo,
- dormir en celda como mision,
- NPC sospechoso falso,
- esconderse en carro de ropa,
- cues avanzadas de audio.
