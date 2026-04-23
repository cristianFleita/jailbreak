# JAILBREAK — Game Design Document

**Version:** 1.3  
**Fecha:** 21 de abril de 2026  
**Plataforma:** PC (Unity WebGL)  
**Jugadores:** 2–4 online  
**Motor:** Unity 6 LTS  
**Duración de partida:** ~13 minutos (ver tabla detallada en §4.1)  

---

## 1. Vision Statement

> *"Un preso entre muchos. Un guardia que no sabe cuál. Y solo unos segundos para volver a mezclarse."*

**Jailbreak** es un juego multijugador asimétrico en primera persona ambientado en una prisión estilo Alcatraz. Uno a tres jugadores son **presos infiltrados** entre decenas de NPCs idénticos que deben cooperar para escapar sin ser detectados. Un jugador es el **guardia** que debe identificarlos observando comportamientos sospechosos, acercarse lo suficiente y capturarlos correctamente.

### 1.1 Pilares de Diseño

| Pilar | Descripción | Se manifiesta en... |
|-------|-------------|---------------------|
| **Tensión asimétrica** | Cada rol experimenta una tensión diferente: los presos temen ser descubiertos, el guardia teme equivocarse | Sistema de captura por foco con penalización por errores |
| **Engaño social** | Mezclarse con NPCs es la mecánica central de supervivencia, no el combate | Camuflaje basado en rutina + proximidad a NPCs |
| **Cooperación bajo presión** | Los presos deben coordinarse sin comunicación obvia mientras evitan detección | Rutas de escape que requieren contribuciones de todos |
| **Humor emergente** | Los momentos más memorables nacen de errores de ambos bandos | Mecánicas de molestia, penalizaciones del guardia, animaciones de NPCs |

### 1.2 Elevator Pitch

*"Spy Party meets The Escapists en primera persona — uno observa y atrapa, los demás se esconden a plena vista."*

### 1.3 Público Objetivo

- Jugadores de juegos sociales/deducción (Among Us, Spy Party, Goose Goose Duck)
- Edad: 16–30 años
- Sesiones cortas (10–15 min por partida)
- Grupos de amigos que buscan experiencias cooperativas con humor

### 1.4 Análisis MDA (Mechanics-Dynamics-Aesthetics)

| Capa | Elementos |
|------|-----------|
| **Mechanics** | Captura por foco, sistema de rutina/fases, inventario limitado, camuflaje por posición, errores con penalización |
| **Dynamics** | Dilema del guardia (¿es jugador o NPC?), ventanas de oportunidad cuando el guardia se compromete a corta distancia, planificación emergente de ruta de escape |
| **Aesthetics** | Tensión, descubrimiento, humor, fellowship (cooperación) |

---

## 2. Core Loop

### 2.1 Loop de los Presos (1–3 jugadores)

```
┌─────────────────────────────────────────────────────────────┐
│  Seguir rutina → Recoger objetos → Cooperar en escape       │
│       ↑                                          ↓          │
│  Volver a rutina ← Romper foco / alejarse ← Ser enfocado   │
└─────────────────────────────────────────────────────────────┘
```

**Detalle por paso:**

1. **Seguir rutina** — Estar en la zona correcta según la fase actual. Imitar comportamiento de NPCs (caminar, sentarse, comer). Desviarse genera sospecha visual.
2. **Recoger objetos** — Aprovechar momentos seguros para tomar ítems de escape. Máximo 2 slots de inventario. Los objetos son visibles brevemente al recogerlos.
3. **Cooperar en escape** — Cada ruta de escape necesita 3 ítems/acciones de diferentes jugadores. Comunicación vía señales en el juego (golpes en pared, tos).
4. **Ser enfocado** — El guardia logra acercarse y sostener la mira sobre el preso durante un instante corto para intentar capturarlo.
5. **Romper foco / alejarse** — Ganar distancia, cortar línea de visión o usar el tráfico de NPCs para impedir que el guardia complete la captura.
6. **Volver a rutina** — Retomar comportamiento creíble dentro de la fase actual. El guardia pierde la oportunidad y debe volver a leer la situación.

**Tiempo por ciclo completo:** ~2–3 minutos (una fase de rutina).

### 2.2 Loop del Guardia (1 jugador)

```
┌─────────────────────────────────────────────────────────────┐
│  Observar → Acercarse → Fijar foco → Capturar              │
│       ↑                                          ↓          │
│  Repatrullar ← Foco roto / Error / Captura                 │
└─────────────────────────────────────────────────────────────┘
```

**Detalle por paso:**

1. **Observar** — Patrullar zonas, revisar cámaras de seguridad, escuchar sonidos sospechosos. Buscar: personas en zona incorrecta, movimiento errático, interacción con objetos prohibidos.
2. **Acercarse** — Reducir la distancia sin delatar demasiado la intención. El guardia necesita quedar a rango corto.
3. **Fijar foco** — Mantener el input de captura sobre un personaje visible durante ~0.5 seg para evitar misclicks.
4. **Capturar** — Si el foco se completa sobre un preso jugador → captura exitosa. Si era un NPC → penalización por error.
5. **Repatrullar** — Si el foco se rompe antes de completarse, el guardia debe reposicionarse y volver a intentarlo.

### 2.3 Condiciones de Fin de Partida

La partida simula **un solo día** en la prisión (06:00 → 00:00 hora ficticia). Al llegar a medianoche (fin de Fase 9 — Luces apagadas) la jornada termina y se evalúa el resultado.

| Condición | Resultado | Cuándo se evalúa |
|-----------|-----------|-------------------|
| Al menos 1 preso jugador escapa por cualquier ruta | **Presos ganan** | Inmediato al escapar |
| Se activa un motín exitoso (3 errores del guardia) | **Presos ganan** | Inmediato al activar motín |
| El guardia captura a todos los presos jugadores | **Guardia gana** | Inmediato al capturar al último |
| La jornada termina (00:00) sin que ningún preso escape | **Guardia gana** | Al finalizar Fase 9 |

**Nota:** Si quedan presos vivos pero ninguno escapó al llegar medianoche, el guardia gana — los prisioneros no lograron fugarse a tiempo.

---

## 3. Mecánica Central — Sistema de Captura por Foco

Esta es la mecánica core que diferencia a Jailbreak. No hay medidor de sospecha pasivo: el guardia debe **observar, acercarse y sostener un foco corto de captura** sobre un sospechoso. La tensión no viene de una persecución larga estilo slasher, sino de lograr ese medio segundo decisivo sin equivocarse.

### 3.1 Flujo de Captura por Foco (Detalle Técnico)

```
ESTADO: PATRULLA (guardia)
  │
  ├─ Guardia se acerca a ≤ 2.0m de un personaje visible
  ├─ Mantiene [CAPTURAR] sobre el mismo target durante 0.5 seg
  │
  ▼
ESTADO: FOCO DE CAPTURA
  │
  ├─ SI el objetivo sale de rango > 2.0m:
  │     └─ Foco se cancela → vuelve a PATRULLA
  │
  ├─ SI el guardia pierde línea de visión:
  │     └─ Foco se cancela → vuelve a PATRULLA
  │
  ├─ SI el guardia suelta el input o cambia de target:
  │     └─ Foco se cancela → vuelve a PATRULLA
  │
  └─ SI el foco llega a 0.5 seg:
        ├─ SI es jugador preso → CAPTURA (preso eliminado)
        └─ SI es NPC → ERROR (penalización al guardia)
```

### 3.2 Cómo los Presos Evitan la Captura

| Acción del preso | Efecto | Implementación |
|------------------|--------|----------------|
| Mantener distancia | El guardia no puede iniciar captura | Verificar rango > 2.0m |
| Cortar línea de visión | El foco se rompe inmediatamente | Requiere visión continua del target durante el foco |
| Moverse entre NPCs / cuerpos | Obliga al guardia a estabilizar el aim y evita misclicks limpios | Raycast local del guardia + colisiones visibles |
| Usar una distracción o molestia | Dificulta que el guardia llegue a rango o sostenga el foco | Integrar con objetos de distracción o micro-stun si llegan al MVP |

**Nota de diseño:** la defensa principal del preso no es una persecución larga, sino impedir que el guardia llegue a rango y complete el foco de captura.

### 3.3 Errores del Guardia (Capturar NPC Inocente)

| Error N° | Penalización | Duración | Implementación |
|----------|-------------|----------|----------------|
| 1er error | NPC enojado sigue al guardia, tapa parcialmente su visión | 60 seg | NPC entra en estado "follow_guard", se posiciona delante |
| 2do error | Grupo de 3–4 NPCs se vuelve hostil, guardia pierde acceso a esa zona | 120 seg | Zona marcada como bloqueada, NPCs patrullan la entrada |
| 3er error | Tensión de motín al máximo — presos pueden activar motín manualmente | Permanente | Flag global `riot_available = true` |
| Motín activado | Todos los NPCs rodean al guardia. Pantalla de derrota | Fin de partida | Todos los NPCs convergen en posición del guardia |

**Nota de diseño:** romper el foco nunca penaliza al guardia. El error existe recién cuando completa una captura sobre un inocente. Esto evita castigar los intentos fallidos por posicionamiento, pero mantiene el dilema al momento de resolver.

### 3.4 Parámetros de Balance (Tweakeables)

| Parámetro | Valor inicial | Rango de ajuste | Notas |
|-----------|--------------|-----------------|-------|
| `capture_focus_time` | 0.5 seg | 0.3–0.8 seg | Tiempo de foco necesario para resolver la captura |
| `capture_range` | 2.0m | 1.5–2.5m | Distancia máxima para iniciar y sostener captura |
| `capture_focus_break_tolerance` | 0.1 seg | 0.0–0.2 seg | Tolerancia opcional para jitter de cámara/target |
| `guard_error_penalty_1_duration` | 60 seg | 30–90 seg | Duración del NPC enojado siguiendo |
| `guard_error_penalty_2_duration` | 120 seg | 60–180 seg | Duración del bloqueo de zona |
| `stumble_stun_duration` | 3.5 seg | 2.0–5.0 seg | Duración del tropiezo por objeto lanzado |

---

## 4. Sistema de Rutina Diaria

### 4.1 Fases de la Jornada

La jornada es el "reloj" de la partida. Cada fase dura un tiempo real fijo y ocurre en una zona específica. Los NPCs siguen la rutina automáticamente. Los presos jugadores deben imitarla.

| # | Fase | Hora ficticia | Duración fase | Transición | Advertencia | Zona | Comportamiento NPC |
|---|------|---------------|---------------|------------|-------------|------|-------------------|
| 1 | Inicio | 06:00 | 30 seg | — | — | Celdas → Comedor | Spawn en celda, saludos, charlas, migran hacia comedor |
| 2 | Desayuno | 06:30 | 90 seg | 10 seg | Silbato 10s antes | Comedor | Agarrar comida → sentarse a comer → tirar bandeja |
| 3 | Trabajo (1er turno) | 08:00 | 90 seg | 10 seg | Silbato 10s antes | Taller / Lavandería | Bancos de trabajo, cargar cajas, lavar ropa |
| 4 | Hora libre | 09:30 | 120 seg | 10 seg | Silbato 10s antes | Patio / Comedor / Lavandería / Celdas | Libre: ejercicio, cartas, charlar, descansar en celda. NPCs cambian de sub-zona |
| 5 | Almuerzo | 11:30 | 90 seg | 10 seg | Silbato 10s antes | Comedor | Mismo flujo que Desayuno |
| 6 | Trabajo (2do turno) | 13:00 | 120 seg | 10 seg | Silbato 10s antes | Taller / Lavandería | Mismo pool que Trabajo 1er turno |
| 7 | Hora libre | 15:00 | 90 seg | 10 seg | Silbato 10s antes | Patio / Comedor / Lavandería / Celdas | Mismo pool que Hora libre (Fase 4) |
| 8 | Cena | 16:30 | 90 seg | 10 seg | Silbato 10s antes | Comedor | Mismo flujo que Desayuno |
| 9 | Luces apagadas | 18:00 → 00:00 | 120 seg | 10 seg | Silbato 10s antes | Celdas | Acostados, oscuridad, guardia con linterna |

**Desglose de tiempos:**

| Concepto | Cálculo | Total |
|----------|---------|-------|
| Fases (gameplay) | 30 + 90 + 90 + 120 + 90 + 120 + 90 + 90 + 120 | **840 seg** |
| Transiciones (8 cambios × 10 seg) | 8 × 10 | **80 seg** |
| **Total partida** | | **920 seg (~15 min 20 seg)** |

**Sistema de advertencias:**
- **10 seg antes** de cada cambio de fase: suena un **silbato** audible globalmente.
- Los NPCs comienzan a moverse hacia la zona de la siguiente fase durante la transición.
- Los presos tienen **10 seg de transición** para llegar a la zona correcta.
- Si un preso no llega a tiempo después de la transición, genera una alerta para el guardia: **"Alguien no está en su zona"** (sin identidad).

### 4.2 Comportamiento Sospechoso (Qué Detecta el Guardia)

| Tipo de sospecha | Descripción | Nivel de evidencia |
|------------------|-------------|-------------------|
| **Zona incorrecta** | Preso en zona que no corresponde a la fase actual | Alto — muy obvio |
| **Movimiento errático** | Correr cuando los NPCs caminan, cambiar dirección bruscamente | Medio — puede ser lag o confusión |
| **Interacción con objeto** | Recoger un ítem de escape (animación breve visible) | Alto — si el guardia ve la animación |
| **No seguir rutina** | Estar de pie cuando los NPCs están sentados, no usar herramienta en trabajo | Medio — el guardia debe comparar |
| **Comunicación** | Golpes en pared o tos entre presos | Bajo — el guardia escucha pero no sabe quién |
| **Proximidad sospechosa** | Dos presos jugadores cerca por mucho tiempo | Bajo — puede ser coincidencia |

### 4.3 Transiciones entre Fases

> Ver tabla detallada arriba. Cada transición tiene 10 seg de duración con silbato de advertencia previo.

- El **silbato** suena al inicio de los 10 seg de transición (audio global).
- Los presos deben llegar a la zona correcta antes de que termine la transición de 10 seg.
- Si un preso no está en la zona correcta al finalizar la transición, genera una alerta visual para el guardia: **"Alguien no está en su zona"** (sin identidad).
- **Hora libre (Fases 4 y 7):** Los NPCs pueden cambiar de sub-zona durante toda la fase (no solo al inicio), lo que genera tráfico orgánico que camufla los movimientos de los jugadores reales.

#### 4.3.1 Movimiento Orgánico post-silbato (Anti-NPC-Tell)

Los NPCs **no reaccionan al silbato todos a la vez**. Cada uno recibe un **perfil de salida** aleatorio que determina cuándo y cómo se mueve hacia la zona de la siguiente fase:

| Perfil | Porcentaje | Delay | Comportamiento |
|--------|-----------|-------|----------------|
| **Salida temprana** | 30% | 0–5 seg | Se pone en marcha casi de inmediato, sin desvíos |
| **Salida normal** | 50% | 5–15 seg | Termina lo que estaba haciendo, puede hacer un desvío por pasillo |
| **Rezagado** | 20% | 15–20 seg | Se queda parado (bostezo/estiramiento), luego deambula por el pasillo |

Además, el ~40% de los NPCs toma un **desvío por pasillo** antes de llegar a su destino de fase. Un 30% de esos se detiene a conversar brevemente en el camino.

**Por qué importa al gameplay:**
1. El jugador preso que tarda en reaccionar al silbato queda camuflado entre los rezagados
2. El guardia no puede usar "nadie se mueve todavía" como señal de que alguien es jugador
3. El tráfico continuo de NPCs por pasillos crea ruido visual persistente entre zonas
4. Durante Hora libre (fases 4 y 7), los NPCs también cambian de sub-zona libremente (~25% en cada reassign), creando cruce de zonas que confunde la búsqueda del guardia

---

## 5. Sistema de Inventario y Objetos

### 5.1 Inventario del Preso

- **2 slots** de inventario máximo.
- Los objetos se recogen con **[INTERACTUAR]** (tecla E). Animación de 1.5 seg visible para quien mire.
- Los objetos se usan con **[USAR]** (tecla Q) en la ubicación correcta.
- Los objetos **no se dropean** voluntariamente (evitar griefing).
- Si un preso es capturado, sus objetos caen al suelo. Otro preso puede recogerlos.

### 5.2 Objetos de Escape (por Ruta)

#### Ruta 1 — La Rejilla de Ventilación Industrial (Cooperativa)

> **Tipo:** Cooperativa (2–3 jugadores recomendados)  
> **Zonas clave:** Taller, Sala de Electricidad, Oficina del Guardia  
> **Spec técnica:** `design/gdd/ruta-1-ventilacion-industrial.md`

**Concepto:** El conducto de ventilación del Taller es lo suficientemente grande para escapar, pero está bloqueado por una pesada rejilla atornillada y un ventilador industrial mortal que está encendido. Los presos deben encontrar las herramientas, identificar y cortar el suministro eléctrico correcto en la otra punta de la prisión, y desatornillar la reja haciendo el menor ruido posible.

**Objetivo MVP:** implementar una ruta cooperativa completa, sincronizada y robusta contra softlocks. La reducción de ruido por cooperación queda como polish; para el MVP, el segundo preso solo acelera la barra de la rejilla.

**Fase 1 — Recolección de herramientas:**

| Objeto | Ubicación | Mecánica | Efecto en inventario |
|--------|-----------|----------|---------------------|
| **Pinzas (cizallas)** | Taller — banco de trabajo o cajón | Interacción rápida (3 seg) | Ocupa 1 slot |
| **Llave inglesa pesada** | Sala de Electricidad — tablero de herramientas o caja | Interacción rápida (3 seg) | Ocupa 1 slot. **Preso camina 5% más lento** mientras la lleva |

**Regla anti-softlock:** cada herramienta crítica tiene 2 spawns posibles: 1 principal y 1 backup. Solo puede existir 1 copia activa de cada herramienta por partida. Si la herramienta queda en el suelo durante demasiado tiempo o en una posición inválida, reaparece en su spawn backup. Si un preso es capturado, sus objetos caen al suelo y pueden ser recuperados por otros presos.

**Fase 2 — Sabotaje eléctrico (puzzle de tensión):**

Un preso debe ir a la Sala de Electricidad con las **Pinzas** para cortar el cable que alimenta el ventilador del Taller.

- **El problema:** Hay un servidor eléctrico con 4 cajas de fusibles (cables gruesos) etiquetadas 1, 2, 3 y 4. Solo una apaga el ventilador del Taller.
- **La pista (plano eléctrico):** La información de qué fusible apaga el Taller está oculta en la **Oficina del Guardia**. El preso debe infiltrarse e interactuar con archivadores/cajones para encontrarla (el mueble exacto cambia aleatoriamente en cada partida). Interacción de **1 seg** — es un "hit and run" táctico.
- Al leer el plano, todos los presos reciben el dato compartido: **"Taller = Fusible N"**. El guardia no recibe esta información.
- El preso interactúa con el cable correcto usando las Pinzas. Si corta un fusible incorrecto, se genera un apagón breve en otra zona y una señal de alerta ambiental para el guardia.
- **Barra de progreso:** 15 seg netos.

**Fase 3 — Fuerza bruta silenciosa (desatornillar la rejilla):**

Con el ventilador apagado (las aspas se detienen, el ruido de fondo del Taller cesa), los presos van a la rejilla con la **Llave Inglesa Pesada**.

- Barra de progreso: **25 seg** para 1 solo jugador.
- Animación: El personaje hace fuerza con todo el cuerpo. Rechinido de metal constante.
- **Cooperación (acelerador MVP):** Si un segundo preso se acerca a la rejilla e interactúa, entra en animación de "sostener la reja". Esto **acelera el progreso 50%** (baja a ~12 seg).
- **Polish post-MVP:** el segundo preso también amortigua el ruido para reducir la distancia audible del rechinido.
- **Interrupción:** Si el guardia se acerca, pueden soltar la interacción al instante. La barra de progreso decae lentamente **(-1%/seg)** mientras nadie trabaja.

**Fase 4 — La fuga (ventana de vulnerabilidad):**

Una vez la barra llega a 100%, la reja cae al piso (ruido moderado).

- **Escape:** Interactuar con el hueco abierto inicia animación de trepar al conducto.
- **Duración:** 4 seg de animación ininterrumpible.
- **Peligro final:** Durante estos 4 seg la hitbox del preso sigue en la sala. Si el guardia entra en ese preciso instante, puede atraparlo de las piernas y sacarlo del conducto. Una vez terminada la animación, el preso es inmune, desaparece y **gana**.

**Comunicación cooperativa y HUD de presos:**

Los presos comparten un progreso discreto de Ruta 1 para evitar depender de voz externa o adivinanzas. Este HUD no revela identidades ni posiciones exactas.

| Estado | HUD presos | Señal de mundo | Visible para guardia |
|--------|------------|----------------|----------------------|
| Plano no encontrado | `Ruta 1: Plano ?` | Ninguna | No |
| Plano leído | `Fusible Taller: N` | Breve papel/ícono en inventario mental compartido | No |
| Ventilador activo | `Ventilador: ON` | Ventilador girando + ruido constante en Taller | Sí, si está cerca o por cámara |
| Ventilador apagado | `Ventilador: OFF` | Aspas detenidas + cesa ruido de fondo | Sí, si lo percibe |
| Rejilla en progreso | Barra `Rejilla: X%` | Rejilla visualmente suelta + rechinido local | Sí, si inspecciona o escucha cerca |
| Rejilla abierta | `Conducto abierto` | Reja caída, hueco interactuable | Sí, si ve la zona |

**Flujo ideal de ejemplo:**

1. **Preso A** se infiltra en la Oficina del Guardia, revisa un archivador (1s) → descubre: "Taller = Fusible 3". Luego, en fase de Trabajo, roba las Pinzas en el Taller.
2. **Preso B** roba la Llave Inglesa en la Sala de Electricidad durante Hora libre (aprovechando que hay tráfico de NPCs moviéndose entre zonas).
3. **Preso A** se cuela en la Sala de Electricidad, corta el Fusible 3 → apaga el ventilador.
4. Ambos se reúnen en el Taller (ej. durante Hora libre, aprovechando que está más vacío). B desatornilla la rejilla mientras A sostiene. Completan la barra y escapan.

**Contramedidas del guardia:**
- Si cesa el ruido de ventilación del Taller → sabe que la Ruta 1 está en marcha.
- Si hay apagón en otra zona (ej. luces del comedor parpadean) → sabe que están adivinando fusibles en la Sala de Electricidad.
- Si patrullando el Taller nota que la rejilla está a medio abrir (tornillos visualmente sueltos) → puede "campear" la zona u observarla por cámaras.

#### Ruta 2 — Túnel (Individual)

Un solo preso excava un túnel desde su celda, saca la tierra disimuladamente y usa un mapa de cloacas para navegar la salida. La dificultad está en que necesita 3 objetos pero solo tiene 2 slots de inventario, forzando una planificación cuidadosa.

| Paso | Objeto | Ubicación | Fase disponible | Quién | Dificultad |
|------|--------|-----------|-----------------|-------|------------|
| 1 | **Cuchara** → cavar detrás del inodoro de la celda (requiere 2 fases de hora libre en celda para completar la excavación) | Comedor — cajón de cubiertos | Desayuno / Almuerzo / Cena | El mismo preso | Baja — muchos NPCs cerca, fácil disimular |
| 2 | **Bolsa de lona** → sacar la tierra del túnel durante el patio (vaciarla en esquina NE, punto ciego de cámara) | Lavandería — canastos | Trabajo | El mismo preso | Media — debe ir a la lavandería fuera de su zona |
| 3 | **Mapa de cloacas** → navegar el túnel subterráneo sin perderse | Sala de Electricidad — armario lateral | Hora libre / Trabajo | El mismo preso | Alta — sala de electricidad es zona restringida |

**Gestión de inventario:** El preso tiene 2 slots pero necesita 3 objetos. Debe usar la cuchara (cavar) y descartarla antes de buscar el mapa. Orden obligatorio: cuchara → bolsa → (vaciar tierra, libera slot) → mapa.
**Acción final:** De noche (luces apagadas), entra al túnel desde la celda y sale por una alcantarilla fuera del muro este.
**Tiempo de escape final:** 20 seg (animación de entrada + carrera por túnel).

#### Ruta 3 — Carro de Ropa Sucia (Cooperativa)

Los presos preparan un dummy para cubrir la ausencia de uno de ellos, sobornan a un NPC para destrabar el carro de lavandería y roban el horario de recolección para saber exactamente cuándo sale el carro.

| Paso | Objeto/Acción | Ubicación | Fase disponible | Quién | Dificultad |
|------|---------------|-----------|-----------------|-------|------------|
| 1 | **Almohada extra + ropa** → fabricar **dummy** y dejarlo en el catre propio (engaña la inspección nocturna del guardia) | Celda / Lavandería | Hora libre (sub-zona celda) / Trabajo | Preso A | Media — debe conseguir ropa de la lavandería |
| 2 | **Sobornar a un NPC** del turno de lavandería con un objeto de valor (cuchara afilada o cigarrillos del patio) → el NPC deja el carro destrabado cerca de la puerta de servicio | Patio → Lavandería | Patio libre → Trabajo | Preso B | Media — debe conseguir el objeto de soborno primero |
| 3 | **Horario de recolección** de la oficina del guardia → saber exactamente cuándo pasa el carro por la puerta de servicio (ventana de 30 seg) | Pasillo — oficina del guardia | Cualquier fase (muy arriesgado) | Preso C | Alta — la oficina es zona de alto riesgo |

**Acción final:** Durante la cena (última fase antes de la noche), el preso se mete en el carro. El dummy cubre su ausencia en la inspección nocturna. El carro sale por la puerta de servicio en la ventana exacta del horario. Si el guardia inspecciona el catre de cerca, descubre el dummy y la ruta falla.
**Tiempo de escape final:** 10 seg (meterse en el carro + salida automática).

#### Comparación de Rutas

| | Ventilación Industrial | Túnel | Carro de Ropa |
|---|---|---|---|
| **Tipo** | Cooperativa (2–3 presos) | Individual (1 preso) | Cooperativa (3 presos) |
| **Objetos** | 2 + pista | 3 | 3 |
| **Cuándo se puede escapar** | Cualquier fase | Luces apagadas | Cena |
| **Mayor riesgo** | Infiltrar Oficina + ruido de sabotaje/rejilla | Gestión de inventario (3 objetos, 2 slots) | Robar horario de oficina + inspección del catre |
| **Escapa** | Todos los presos en la rejilla | Solo 1 preso | Solo 1 preso (los otros cubren) |
| **Contramedida del guardia** | Ruido de ventilador cesa + CLANG audible | Escuchar excavación / ver tierra en el patio | Inspeccionar catre de cerca / revisar el carro |

### 5.3 Objetos de Molestia (Tácticos)

| Objeto | Ubicación | Efecto | Duración | Usos |
|--------|-----------|--------|----------|------|
| Jabón | Lavanderia | Guardia tropieza, pierde orientación | 3 seg stun | 1 uso, se consume |
| Bandeja de comida | Comedor | Visión del guardia reducida (salpicadura) | 2 seg | 1 uso, se consume |
| Interruptor de luz | Panel eléctrico (pasillo) | Sector a oscuras, guardia debe reparar | 15 seg o hasta que el guardia repare | Ilimitado pero ruidoso |

---

## 6. Mecánicas de Molestia (Humor + Táctica)

Estas mecánicas son opcionales para los presos pero proporcionan ventaja táctica y momentos de humor. Ninguna es necesaria para ganar, pero todas ayudan.

| Mecánica | Trigger | Efecto en el guardia | Efecto en NPCs | Audio/Visual |
|----------|---------|---------------------|-----------------|-------------|
| **Jabón en el piso** | Soltar jabón en zona de paso | Tropieza, pierde control 3 seg | NPCs cercanos ríen (audio) | Sonido de resbalón + risa |
| **Tirar comida** | Usar bandeja en comedor | Visión reducida 2 seg | NPCs cercanos se agitan | Salpicadura en pantalla del guardia |
| **Falsa alarma** | Activar puerta/ruido a distancia | Guardia recibe alerta falsa en zona lejana | NPCs miran hacia el ruido | Sonido de puerta/golpe |
| **Apagar luz** | Interruptor en panel eléctrico | Sector a oscuras, debe reparar o patrullar a ciegas | NPCs en esa zona se detienen, confundidos | Apagón visual + murmullos |
| **Señales entre presos** | Golpear pared / toser (tecla) | Guardia escucha si está cerca (audio 3D) | NPCs no reaccionan | Sonido posicional 3D |
---

## 7. Sistema de Cámaras de Seguridad (Guardia)

### 7.1 Funcionamiento

- El guardia puede activar el **modo cámara** en cualquier momento con **[TAB]**.
- Mientras está en modo cámara:
  - Ve un **feed de 4 cámaras** en esquinas del HUD (vista cenital simplificada).
  - **Pierde visión de su entorno inmediato** — su personaje queda quieto y vulnerable.
  - Puede hacer **zoom** en una cámara específica con click.
- Los presos ven una **luz roja parpadeante** en las cámaras cuando el guardia las usa.
- Cada cámara cubre una zona fija. Hay **1 punto ciego** en el patio exterior (esquina NE).

### 7.2 Cámaras por Zona

| Cámara | Zona cubierta | Punto ciego |
|--------|--------------|-------------|
| CAM-1 | Bloque de celdas (pasillo central) | Interior de las celdas |
| CAM-2 | Comedor (vista cenital) | Debajo de las mesas |
| CAM-3 | Taller (entrada + zona de trabajo) | Zona del conducto de ventilación (ángulo muerto) |
| CAM-4 | Patio exterior (esquina SW) | Esquina NE (muro perimetral) |

> **Nota:** La Sala de Electricidad y la Lavandería **no tienen cámaras**. El guardia debe patrullarlas en persona.

### 7.3 Contramedidas de los Presos

- **Timing**: actuar cuando la luz roja indica que el guardia está mirando otra cámara.
- **Punto ciego natural**: la esquina NE del patio no tiene cobertura.
- **Apagar luces** (panel eléctrico): las cámaras del sector afectado pierden visibilidad.

---

## 8. Diseño de Nivel — La Prisión

### 8.1 Layout General

```
┌──────────────────┬───────────────────────────────┬──────────────┐
│                  │                               │              │
│  OFICINA DEL     │     BLOQUE DE CELDAS          │   TALLER     │
│  GUARDIA         │     (2 pisos, 20 celdas)      │              │
│  (cámaras,       │     Pasillo central           │  (herram.,   │
│   archivos)      │                               │   ventilac.) │
│                  │                               │              │
├──────────────────┤                               ├──────────────┤
│                  │                               │              │
│                  │     P A S I L L O             │              │
│                  │                               │              │
├──────────────────┤     (conecta todo,            ├──────────────┤
│                  │      alto tráfico NPC)        │              │
│  LAVANDERÍA      │                               │   COMEDOR    │
│                  │                               │              │
│  (lavadoras,     ├───────────────────────────────┤  (mesas,     │
│   canastos,      │                               │   counter,   │
│   carro ropa)    │     SALA DE ELECTRICIDAD      │   depósito)  │
│                  │     (servidores, fusibles)     │              │
├──────────────────┴───────────────────────────────┴──────────────┤
│                                                                 │
│                       PATIO EXTERIOR                            │
│               (bancos, ejercicio, cartas)                       │
│                     (punto ciego NE →) ○                        │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Distribución espacial:**
- **Fila norte:** Oficina del guardia — Bloque de celdas — Taller
- **Centro:** Pasillo principal (conecta todas las zonas)
- **Fila sur:** Lavandería — Sala de electricidad — Comedor
- **Borde sur:** Patio exterior

### 8.2 Zonas Detalladas

#### Bloque de Celdas
- **Estructura:** 2 pisos, 10 celdas por piso, pasillo central con barandilla en el piso 2.
- **Objetos interactuables:** Catre (dummy ruta 3), inodoro (detrás se cava el túnel ruta 2), almohada (dummy ruta 3), reja de la celda.
- **Puntos de interés:** Cada celda tiene un NPC asignado. Los presos jugadores tienen celdas específicas.
- **Iluminación:** Fluorescente de día, apagada de noche (guardia usa linterna).

#### Comedor
- **Estructura:** Mesa central larga (20 asientos), zona de servicio con mostrador, cocina trasera.
- **Objetos interactuables:** Bandejas (arma de molestia), cubiertos/cuchara (objeto de escape), sillas (obstáculo al correr).
- **Punto de interés:** Debajo de las mesas es punto ciego de la cámara.

#### Taller
- **Estructura:** Zona de carpintería con bancos, zona de metal con herramientas, conducto de ventilación industrial visible en el techo con ventilador encendido (ruido constante de fondo).
- **Objetos interactuables:** Pinzas/cizallas (escape ruta 1, spawn aleatorio en banco o cajón), cajones con llave.
- **Punto de interés:** Rejilla del conducto de ventilación con 4 pernos de seguridad (ruta de escape 1). El ventilador industrial debe estar apagado primero. Requiere llave inglesa pesada para desatornillar.

#### Lavandería
- **Estructura:** Canastos grandes, máquinas industriales, tuberías visibles, puerta de servicio hacia exterior, carro de ropa sucia.
- **Objetos interactuables:** Canastos (esconderse brevemente, 5 seg máx), bolsa de lona (escape ruta 2), ropa extra (escape ruta 3), carro de ropa sucia (escape ruta 3).
- **Punto de interés:** Puerta de servicio — el carro de ropa sale por aquí en horarios fijos.

#### Patio Exterior
- **Estructura:** Espacio abierto, muro perimetral alto, torre de vigilancia (decorativa), esquina NE sin cámara.
- **Objetos interactuables:** Tierra cavable en esquina NE (punto ciego).
- **Punto de interés:** Zona más amplia, difícil para el guardia cubrir todo.

#### Oficina del Guardia
- **Estructura:** Zona de alto riesgo en la esquina noroeste. Escritorio, archivadores, monitores de cámaras.
- **Objetos interactuables:** Archivadores/cajones (plano eléctrico ruta 1 — el mueble que lo contiene cambia aleatoriamente cada partida), horario de recolección (ruta 3).
- **Punto de interés:** Territorio del guardia — entrar es extremadamente arriesgado. Interacción rápida (1 seg) tipo "hit and run".

#### Sala de Electricidad
- **Estructura:** Sala técnica entre la lavandería y el comedor. Contiene servidores eléctricos con 4 cajas de fusibles etiquetadas (1, 2, 3, 4).
- **Objetos interactuables:** Cables/fusibles (sabotaje ruta 1 — cortar el cable correcto apaga el ventilador del taller). Panel eléctrico (apagar luces de sectores — molestia táctica).
- **Punto de interés:** Cortar el cable equivocado apaga las luces de otra zona, generando pánico en NPCs y alertando al guardia.

#### Pasillo Principal
- **Estructura:** Conecta todas las zonas. Alto tráfico de NPCs durante transiciones.
- **Objetos interactuables:** —
- **Punto de interés:** Zona de alto tráfico NPC, fácil mezclarse pero también fácil ser visto.

### 8.3 Rutas de Escape (Mapa Detallado)

#### Ruta 1 — La Rejilla de Ventilación Industrial (Cooperativa)
```
Oficina guardia (leer plano eléctrico, 1s) → descubrir qué fusible apaga el Taller
Taller (robar pinzas, 3s) + Sala de electricidad (robar llave inglesa, 3s) →
Sala de electricidad (cortar fusible correcto con pinzas, 15s) → ventilador se apaga →
Taller (desatornillar rejilla con llave inglesa, 25s solo / 12s cooperativo) →
Taller (trepar al conducto, 4s animación) → Exterior
```
- **Objetos requeridos:** 2 (pinzas + llave inglesa). Pista separada (plano eléctrico).
- **Jugadores recomendados:** 2–3.
- **Cuándo se puede ejecutar:** Cualquier fase — el riesgo es el ruido y el tiempo expuesto.
- **Tiempo de escape final:** 4 seg (trepar al conducto).
- **Spec técnica:** `design/gdd/ruta-1-ventilacion-industrial.md`.

#### Ruta 2 — Túnel (Individual)
```
Comedor (robar cuchara) → Celda (cavar túnel, 2 fases de hora libre en celda) →
Lavandería (robar bolsa de lona) → Patio NE (vaciar tierra, punto ciego) →
Sala de electricidad (robar mapa de cloacas) → Celda (entrar al túnel de noche) →
Cloacas (navegar con mapa) → Exterior muro este
```
- **Acciones requeridas:** 3 (un solo preso, gestión de inventario forzada por 2 slots).
- **Cuándo se puede ejecutar:** Luces apagadas (noche).
- **Tiempo de escape final:** 20 seg.

#### Ruta 3 — Carro de Ropa Sucia (Cooperativa)
```
Lavandería (ropa) + Celda (almohada) → Celda (fabricar dummy, dejarlo en catre) →
Patio (conseguir objeto de soborno) → Lavandería (sobornar NPC, destraba carro) →
Oficina guardia (robar horario de recolección) →
Cena (meterse en el carro en la ventana exacta del horario) → Puerta de servicio → Exterior
```
- **Acciones requeridas:** 3 (una por jugador).
- **Cuándo se puede ejecutar:** Cena (el carro sale por la puerta de servicio).
- **Contramedida:** Si el guardia inspecciona el catre de cerca, descubre el dummy y la ruta falla.
- **Tiempo de escape final:** 10 seg.

---

## 9. Perspectiva y Cámara

### 9.1 Vista General

Todos los jugadores juegan en **primera persona (FPS)**. No hay opción de tercera persona.

### 9.2 Cámara de los Presos

| Parámetro | Valor |
|-----------|-------|
| FOV | 70° (angosto deliberadamente — aumenta tensión) |
| Head bob | Sutil al caminar, pronunciado al correr |
| Look speed | Configurable (sensibilidad del mouse) |
| Restricción | No puede mirar más de 80° arriba/abajo |

### 9.3 Cámara del Guardia

| Parámetro | Valor |
|-----------|-------|
| FOV | 80° (ligeramente más amplio que presos) |
| Linterna | Activa automáticamente en "luces apagadas" — cono de 40° |
| Modo cámara | Overlay en esquinas del HUD, click para zoom |

### 9.4 NPCs

- Animaciones de rutina simples y **claramente legibles** (caminar, sentarse, comer, trabajar).
- Los jugadores aprenden a distinguir presos de NPCs por **comportamiento**, no por apariencia.
- Los NPCs nunca corren (excepto durante motín). Si alguien corre, es un jugador.

---

## 10. Dirección de Arte

### 10.1 Estilo Visual

- **Realista estilizado** — proporciones realistas, texturas con nivel de detalle reducido.
- **Paleta principal:** Escala de grises y beige para la prisión (concreto, metal oxidado, pintura descascarada).
- **Paleta de acento:** Colores cálidos (naranja, amarillo) para **objetos interactuables** — contraste intencional para legibilidad.
- **Referencia visual:** The Escapists (concepto) + A Way Out (atmósfera) + Alcatraz real (arquitectura).

### 10.2 Personajes

| Elemento | Presos (jugadores + NPCs) | Guardia |
|----------|---------------------------|---------|
| Uniforme | Gris Alcatraz, sin número legible durante gameplay | Marrón oscuro, gorra |
| Distinción visual | **Ninguna** entre jugadores y NPCs (intencional) | Único — siempre visible |
| Identificación aliada | Ícono discreto sobre compañeros presos cuando están a <5m | — |
| Accesorio nocturno | — | Linterna en mano |

### 10.3 Iluminación

| Fase | Iluminación |
|------|-------------|
| Día (inicio → cena) | Fluorescente interior, luz natural en patio |
| Luces apagadas | Oscuridad casi total, linterna del guardia es la única fuente principal |
| Cámaras | Luz roja cuando activas, verde cuando inactivas |

---

## 11. Diseño de Audio

### 11.1 Ambientación

| Elemento | Descripción | Prioridad |
|----------|-------------|-----------|
| Ambiente prisión | Eco metálico, murmullos lejanos, puertas de metal | Alta |
| Pasos | Diferenciados por superficie (concreto, metal, tierra) | Alta |
| Silbato de fase | Marca el cambio de fase — fuerte, reconocible | Alta |
| Pulso de foco | Sonido tenso breve mientras el guardia sostiene foco de captura sobre ti (opcional MVP) | Media |

### 11.2 Audio 3D (Gameplay)

| Sonido | Tipo | Rango audible | Quién lo escucha |
|--------|------|---------------|-----------------|
| Pasos corriendo | 3D posicional | 15m | Todos |
| Golpes en pared (señal) | 3D posicional | 10m | Todos (el guardia también) |
| Tos (señal) | 3D posicional | 8m | Todos |
| Recoger objeto | 3D posicional | 5m | Todos cercanos |
| Cavar túnel | 3D posicional | 5m | Todos cercanos |
| Risa de NPCs (jabón) | 3D posicional | 12m | Todos |

### 11.3 Música

- **No hay música durante gameplay** — la ausencia de música aumenta la tensión.
- **Stinger musical** en eventos clave: captura, escape, motín, inicio/fin de partida.
- **Lobby:** Música ambiental low-key estilo prison drama.

---

## 12. HUD y UI

### 12.1 HUD de Presos

```
┌──────────────────────────────────────────────┐
│ [FASE: Desayuno]          [Timer: 1:12]  (↗) │
│                                              │
│                                              │
│                                              │
│                     +                        │  ← Crosshair minimalista
│                                              │
│                                              │
│  ◆ ◇                                        │  ← Inventario (2 slots)
│  [E] Recoger                                 │  ← Prompt contextual
│                                              │
│  ○ ○ ●                                        │  ← Progreso escape (3 piezas)
│  Compañeros: ← →                             │  ← Posición aliados (periférica)
└──────────────────────────────────────────────┘
```

| Elemento | Posición | Descripción |
|----------|----------|-------------|
| Fase actual + timer | Top-center | Nombre de la fase + tiempo restante |
| Inventario | Bottom-left | 2 slots con ícono del objeto (◆ = ocupado, ◇ = vacío) |
| Prompt contextual | Bottom-center | "[E] Recoger" / "[Q] Usar" cuando hay interacción disponible |
| Progreso de escape | Bottom-left (sobre inventario) | Círculos: ○ = falta, ● = conseguido. 3 por ruta. |
| Posición de aliados | Bordes de pantalla | Flechas direccionales indicando dónde están los compañeros |

### 12.2 HUD del Guardia

```
┌──────────────────────────────────────────────┐
│ [FASE: Desayuno]          [Timer: 1:12]  (↗) │
│                                     [CAM] ▣  │  ← Feed de cámaras (miniatura)
│                                     [CAM] ▣  │
│                                              │
│                     ◎                        │  ← Crosshair de captura
│                                              │
│                                              │
│  Errores: ✕ ○ ○        [Hold Click] Capturar │  ← Contador de errores + prompt
│  Tensión motín: ██░░░░                       │  ← Barra de tensión
│  [TAB] Cámaras                               │  ← Prompt de cámaras
│  ⚡ Alerta: Zona Taller — alguien fuera      │  ← Alerta de comportamiento
└──────────────────────────────────────────────┘
```

| Elemento | Posición | Descripción |
|----------|----------|-------------|
| Fase actual + timer | Top-center | Igual que presos |
| Mini-cámaras | Top-right | 4 thumbnails pequeños de las cámaras |
| Crosshair de captura | Center | Más grande que el de presos; muestra progreso radial cuando hay target válido |
| Contador de errores | Bottom-left | Cruces rojas por cada error (máx 3) |
| Barra de tensión de motín | Bottom-left | Sube con errores. Al máximo, presos pueden activar motín |
| Prompt de captura | Bottom-center | "[Hold Click] Capturar" cuando hay target válido a rango |
| Prompt de cámaras | Bottom-center | "[TAB] Cámaras" |
| Alertas de comportamiento | Bottom-right | Notificaciones: "Zona X — alguien fuera de rutina" (sin identidad) |

### 12.3 Pantallas de UI

| Pantalla | Contenido |
|----------|-----------|
| **Menú principal** | Logo + "Buscar partida" / "Crear sala" / "Opciones" / "Salir" |
| **Lobby** | Lista de jugadores (2–4), botón "Listo", chat de texto |
| **Asignación de rol** | Pantalla breve: "Eres PRESO" o "Eres GUARDIA" (3 seg) |
| **En partida** | HUD por rol (descritos arriba) |
| **Captura** | Pantalla para el preso: "CAPTURADO" — puede observar como espectador |
| **Victoria/Derrota** | "PRESOS ESCAPAN" / "GUARDIA GANA" / "MOTÍN" + estadísticas breves |
| **Revancha** | Botón "Jugar de nuevo" (reasigna roles) / "Volver al lobby" |

---

## 13. Controles

### 13.1 Controles de Teclado + Mouse

| Acción | Tecla | Ambos roles | Solo presos | Solo guardia |
|--------|-------|-------------|-------------|-------------|
| Mover | WASD | x | | |
| Mirar | Mouse | x | | |
| Sprint | Shift | x | | |
| Agacharse | C (toggle) | x | | |
| Interactuar / Recoger | E | x | | |
| Usar objeto | Q | | x | |
| Capturar sospechoso (mantener 0.5s) | Click izq. | | | x |
| Modo cámaras | TAB | | | x |
| Señal (golpe/tos) | F | | x | |
| Activar motín | M (hold 3 seg) | | x (solo si disponible) | |

---

## 14. Networking y Multiplayer

### 14.1 Arquitectura

Unity maneja **toda** la lógica del juego: lobby, partida, resultados, revancha. React es solo un wrapper que carga el build WebGL en un iframe — no tiene lógica de negocio.

```
┌─────────────────────────┐     WebSocket      ┌──────────────────┐
│  Cliente Unity (WebGL)   │ ◄──────────────► │  Servidor Node.js │
│  ┌─────────────────────┐│   Socket.io       │  (Express)        │
│  │ Lobby / Matchmaking  ││  (plugin C#:     │                   │
│  │ Gameplay             ││   NativeWebSocket ├──────────────────┤
│  │ HUD / UI             ││   o SocketIO-    │  Game State       │
│  │ Resultados/Revancha  ││   Unity)         │  NPC Positions    │
│  └─────────────────────┘│                   │  Phase Timer      │
└─────────────────────────┘                   │  Inventory State  │
        x 2-4                                  └────────┬─────────┘
                                                        │
┌─────────────────────────┐                    ┌────────┴─────────┐
│  React (wrapper)         │                    │   PostgreSQL      │
│  Solo: iframe + resize   │                    │   (Vercel)        │
│  Sin lógica de negocio   │                    │   - Salas         │
└─────────────────────────┘                    │   - Estadísticas  │
                                               └──────────────────┘
```

### 14.2 Modelo de Autoridad

| Dato | Autoridad | Razón |
|------|-----------|-------|
| Posición de jugadores | **Servidor** (con predicción cliente) | Evitar speed hacks |
| Posición de NPCs | **Servidor** | Consistencia para todos los clientes |
| Timer de fases | **Servidor** | Sincronización exacta |
| Inventario | **Servidor** | Evitar item duplication |
| Captura por foco | **Servidor** (con foco local para selección visible) | Validación de distancia, identidad y resolución |
| Cámaras | **Cliente del guardia** (notifica servidor) | Baja latencia para toggle |
| Movimiento input | **Cliente** (enviado al servidor) | Input prediction |

### 14.3 Eventos Socket.io Clave

| Evento | Dirección | Payload |
|--------|-----------|---------|
| `player:move` | Cliente → Servidor | `{ position, rotation, velocity }` |
| `player:interact` | Cliente → Servidor | `{ objectId, action }` |
| `guard:catch` | Cliente → Servidor | `{ entityId, entityType }` |
| `guard:catch:result` | Servidor → Todos | `{ guardId, entityId, success, isPlayer }` |
| `catch:failed` | Servidor → Guardia | `{ reason }` |
| `phase:change` | Servidor → Todos | `{ phase, duration, zone }` |
| `npc:positions` | Servidor → Todos | `{ npcs: [{ id, pos, rot, anim }] }` (delta compressed) |
| `escape:progress` | Servidor → Presos | `{ route, items_collected, items_needed }` |
| `game:end` | Servidor → Todos | `{ winner: 'prisoners' | 'guard', reason }` |
| `riot:available` | Servidor → Presos | `{}` |
| `riot:activate` | Cliente → Servidor | `{}` |

### 14.4 Optimización de Red

- **NPCs:** Posiciones enviadas como **delta** cada 200ms (no cada frame). Clientes interpolan.
- **Jugadores:** Posiciones enviadas cada 50ms con **predicción cliente-side**.
- **Tick rate servidor:** 20 ticks/seg (50ms).
- **Máximo jugadores por sala:** 4.
- **Reconexión:** Si un jugador se desconecta, tiene 30 seg para reconectarse. Su personaje queda quieto (como NPC).

---

## 15. IA de NPCs

### 15.1 State Machine

```
┌──────────┐    fase cambia    ┌──────────────┐
│  IDLE    │ ────────────────► │  TRANSITION  │
│ (rutina) │                   │  (caminando   │
│          │ ◄──────────────── │   a zona)    │
└──────────┘    llega a zona   └──────────────┘
     │                              
     │ guardia señala NPC           
     ▼                              
┌──────────┐                   ┌──────────────┐
│  ANGRY   │ ──── timer ─────►│  IDLE        │
│ (sigue   │    expira         │  (vuelve a   │
│  guardia)│                   │   rutina)    │
└──────────┘                   └──────────────┘
     │
     │ 2do error en zona
     ▼
┌──────────┐
│ HOSTILE  │ ──── timer expira ──► IDLE
│ (bloquea │
│  zona)   │
└──────────┘
     │
     │ 3er error (motín activado)
     ▼
┌──────────┐
│  RIOT    │ ──── fin de partida
│ (rodea   │
│  guardia)│
└──────────┘
```

### 15.2 Comportamiento por Fase

Cada NPC tiene:
- **Celda asignada** (posición base).
- **Ruta de NavMesh** por fase (precalculada, con variación aleatoria ±2m).
- **Animación de actividad** por fase (sentarse, comer, trabajar, dormir).
- **Velocidad de caminata:** 2.5 m/s (nunca corren en estado normal).

### 15.3 Cantidad de NPCs

| Modo | Jugadores | NPCs | Total personajes |
|------|-----------|------|-----------------|
| 2 jugadores (1v1) | 2 | 18 | 20 |
| 3 jugadores (2v1) | 3 | 17 | 20 |
| 4 jugadores (3v1) | 4 | 16 | 20 |

Siempre **20 personajes en total** para mantener consistencia visual y de performance.

---

## 16. Stack Técnico

| Componente | Tecnología | Notas |
|------------|-----------|-------|
| Motor de juego | Unity 6 LTS | WebGL build — maneja TODO: lobby, gameplay, UI, resultados |
| Wrapper web | React + Vite + TypeScript | Solo iframe que carga el build WebGL, sin lógica de negocio |
| UI del juego | Unity UI Toolkit o UGUI | Lobby, HUD, menús, resultados — todo dentro de Unity |
| Backend | Node.js + Express + Socket.io | Game server |
| Base de datos | PostgreSQL (Vercel Marketplace) | Salas, stats |
| Multiplayer | Socket.io desde Unity (plugin C#: NativeWebSocket o SocketIOUnity) | Conexión directa Unity→Backend |
| NPC AI | NavMesh + State Machine (Unity) | Server-authoritative positions |
| Deploy frontend | Vercel | React wrapper + Unity WebGL build |
| Deploy backend | Render | Auto-deploy desde main |

---

## 17. Scope y Milestones

### Semana 1 — Foundation (Días 1–7)

| Tarea | Prioridad | Estimación | Dependencias |
|-------|-----------|------------|-------------|
| Mapa base de la prisión (bloque celdas + comedor + pasillo) | P0 | 2 días | — |
| Movimiento FPS básico (presos + guardia) | P0 | 1 día | — |
| Sistema de rooms online (Socket.io, 2–4 jugadores) | P0 | 2 días | — |
| Spawn de NPCs con NavMesh y rutina básica (A→B según fase) | P0 | 1.5 días | Mapa base |
| Timer de fases funcionando (server-side) | P0 | 0.5 día | Rooms |
| **Entregable:** 2 jugadores se conectan, se mueven en el mapa con NPCs | | | |

### Semana 2 — Core Mechanics (Días 8–14)

| Tarea | Prioridad | Estimación | Dependencias |
|-------|-----------|------------|-------------|
| Sistema de captura por foco (acercarse → sostener 0.5s → resolver) | P0 | 2 días | Movimiento FPS |
| Lógica de evasión corta (distancia + romper foco) | P0 | 1 día | Captura + NPCs |
| Sistema de inventario (recoger, guardar, usar objetos) | P0 | 1.5 días | — |
| 1 ruta de escape completa (conducto de ventilación — cooperativa) | P0 | 2 días | Inventario + Mapa |
| Penalizaciones por errores del guardia (NPC enojado, zona bloqueada, motín) | P1 | 1 día | Captura por foco |
| Mecánicas de molestia (jabón, tirar comida) | P1 | 0.5 día | Inventario |
| Condiciones de victoria/derrota | P0 | 1 día | Captura + Escape |
| **Entregable:** Partida jugable completa con 1 ruta de escape | | | |

### Semana 3 — Polish + Segunda Ruta (Días 15–21)

| Tarea | Prioridad | Estimación | Dependencias |
|-------|-----------|------------|-------------|
| Segunda ruta de escape (túnel — individual) | P1 | 1.5 días | Inventario |
| Cámara de seguridad del guardia (HUD + lógica) | P1 | 1.5 días | — |
| Audio: pasos, ambiente, risas NPCs, alarmas | P1 | 1 día | — |
| Señales entre presos (golpes en pared — audio 3D) | P2 | 0.5 día | Audio |
| UI/HUD completo por rol | P1 | 1.5 días | — |
| Lobby y asignación aleatoria de roles | P1 | 1 día | Rooms |
| **Entregable:** Juego con 2 rutas, audio, UI completa | | | |

### Últimos 3–4 Días — Final Polish (Días 22–25)

| Tarea | Prioridad | Estimación | Dependencias |
|-------|-----------|------------|-------------|
| Balance de tiempos y dificultad de captura por foco | P0 | 1 día | Playtesting |
| Bug fixing multiplayer | P0 | 1 día | — |
| Deploy a Vercel + Render | P0 | 0.5 día | — |
| Pantallas de inicio, resultados y revancha | P1 | 0.5 día | — |
| Trailer / GIF de demo | P2 | 0.5 día | — |
| **Entregable:** Juego listo para entregar | | | |

### Scope Cuts (Si No Hay Tiempo)

| Feature | Impacto si se corta | Alternativa |
|---------|---------------------|-------------|
| Ruta de escape 3 (carro de ropa sucia) | Bajo — 2 rutas son suficientes | Dejar para post-jam |
| Señales entre presos | Bajo — pueden usar Discord | Eliminar |
| Dummy en la cama | Medio — pierde mecánica nocturna | Simplificar fase nocturna |
| Cámara de seguridad | Alto — pierde herramienta clave del guardia | Implementar versión minimal (1 cámara fija) |

---

## 18. Riesgos y Mitigaciones

| Riesgo | Probabilidad | Impacto | Mitigación |
|--------|-------------|---------|------------|
| Sincronización NPCs en multiplayer | Alta | Alto | NPCs simulados en servidor, clientes interpolan posiciones. Delta compression. |
| Performance WebGL con 20+ NPCs | Media | Alto | LOD agresivo, animaciones simples (max 3 por estado), frustum culling, occlusion culling. |
| Balance captura por foco (fácil/difícil) | Alta | Alto | Playtesting desde semana 2. Todos los parámetros del sistema son tweakeables (ver sección 3.4). |
| Scope creep | Alta | Medio | Priorizar 1 ruta funcional antes de agregar la segunda. Scope cuts definidos. |
| Bugs Socket.io en partidas de 4 | Media | Medio | Testear con 2 jugadores primero, escalar de a uno. Reconexión automática (30 seg). |
| WebGL build pesado | Media | Medio | Texturas comprimidas, asset bundles, streaming de assets. Target: <50MB initial load. |
| Latencia alta en capturas | Media | Alto | El foco se resuelve del lado cliente para el feel; el servidor valida rango e identidad antes de confirmar el resultado. |

---

## 19. Métricas de Éxito (Post-Lanzamiento)

| Métrica | Objetivo | Cómo medir |
|---------|----------|------------|
| Partidas completadas / iniciadas | >80% | Eventos `game:end` vs `game:start` en backend |
| Tiempo promedio de partida | 10–15 min | Timer en servidor |
| Win rate presos vs guardia | 45–55% (balance) | Estadísticas en PostgreSQL |
| Errores promedio del guardia por partida | 1.5–2.0 | Contador en servidor |
| Tasa de revancha (jugar de nuevo) | >60% | Eventos de lobby |

---

## 20. Glosario

| Término | Definición |
|---------|-----------|
| **Fase** | Período de tiempo dentro de la jornada (inicio, desayuno, trabajo, etc.) |
| **Rutina** | Comportamiento esperado de un preso/NPC durante una fase específica |
| **Captura por foco** | Acción del guardia de mantener la mira e input sobre un personaje cercano durante un breve tiempo para intentar atraparlo |
| **Foco** | Tiempo continuo de apuntado requerido para que la captura se resuelva |
| **Camuflaje** | Acción de mezclarse con NPCs o volver a la rutina para impedir que el guardia llegue a rango o complete el foco |
| **Motín** | Condición de victoria de los presos que se activa tras 3 errores del guardia |
| **Ruta de escape** | Secuencia de objetos y acciones que los presos deben completar para escapar |
| **Delta compression** | Enviar solo los cambios de posición de NPCs, no las posiciones completas |
| **Client prediction** | El cliente predice el movimiento local antes de recibir confirmación del servidor |

---

*GDD v1.3 — Documento vivo. Prioridad absoluta: que sea divertido con 2 jugadores desde el día 1.*
