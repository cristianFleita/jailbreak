# JAILBREAK — Game Design Document

**Version:** 1.1  
**Fecha:** 6 de abril de 2026  
**Plataforma:** PC (Unity WebGL)  
**Jugadores:** 2–4 online  
**Motor:** Unity 6 LTS  
**Duración de partida:** 10–15 minutos  

---

## 1. Vision Statement

> *"Un preso entre muchos. Un guardia que no sabe cuál. Y solo unos segundos para volver a mezclarse."*

**Jailbreak** es un juego multijugador asimétrico en primera persona ambientado en una prisión estilo Alcatraz. Uno a tres jugadores son **presos infiltrados** entre decenas de NPCs idénticos que deben cooperar para escapar sin ser detectados. Un jugador es el **guardia** que debe identificarlos observando comportamientos sospechosos y atraparlos físicamente.

### 1.1 Pilares de Diseño

| Pilar | Descripción | Se manifiesta en... |
|-------|-------------|---------------------|
| **Tensión asimétrica** | Cada rol experimenta una tensión diferente: los presos temen ser descubiertos, el guardia teme equivocarse | Sistema de persecución con penalización por errores |
| **Engaño social** | Mezclarse con NPCs es la mecánica central de supervivencia, no el combate | Camuflaje basado en rutina + proximidad a NPCs |
| **Cooperación bajo presión** | Los presos deben coordinarse sin comunicación obvia mientras evitan detección | Rutas de escape que requieren contribuciones de todos |
| **Humor emergente** | Los momentos más memorables nacen de errores de ambos bandos | Mecánicas de molestia, penalizaciones del guardia, animaciones de NPCs |

### 1.2 Elevator Pitch

*"Spy Party meets The Escapists en primera persona — uno persigue, los demás se esconden a plena vista."*

### 1.3 Público Objetivo

- Jugadores de juegos sociales/deducción (Among Us, Spy Party, Goose Goose Duck)
- Edad: 16–30 años
- Sesiones cortas (10–15 min por partida)
- Grupos de amigos que buscan experiencias cooperativas con humor

### 1.4 Análisis MDA (Mechanics-Dynamics-Aesthetics)

| Capa | Elementos |
|------|-----------|
| **Mechanics** | Persecución física, sistema de rutina/fases, inventario limitado, camuflaje por posición, errores con penalización |
| **Dynamics** | Dilema del guardia (¿es jugador o NPC?), ventanas de oportunidad cuando un compañero es perseguido, planificación emergente de ruta de escape |
| **Aesthetics** | Tensión, descubrimiento, humor, fellowship (cooperación) |

---

## 2. Core Loop

### 2.1 Loop de los Presos (1–3 jugadores)

```
┌─────────────────────────────────────────────────────────────┐
│  Seguir rutina → Recoger objetos → Cooperar en escape       │
│       ↑                                          ↓          │
│  Camuflarse ← Escapar persecución ← Ser detectado          │
└─────────────────────────────────────────────────────────────┘
```

**Detalle por paso:**

1. **Seguir rutina** — Estar en la zona correcta según la fase actual. Imitar comportamiento de NPCs (caminar, sentarse, comer). Desviarse genera sospecha visual.
2. **Recoger objetos** — Aprovechar momentos seguros para tomar ítems de escape. Máximo 2 slots de inventario. Los objetos son visibles brevemente al recogerlos.
3. **Cooperar en escape** — Cada ruta de escape necesita 3 ítems/acciones de diferentes jugadores. Comunicación vía señales en el juego (golpes en pared, tos).
4. **Ser detectado** — El guardia señala al preso. Aparece indicador de alerta en pantalla.
5. **Escapar persecución** — Correr, usar mecánicas de molestia, buscar ruta de evasión.
6. **Camuflarse** — Volver a zona correcta, mezclarse entre 3+ NPCs, comportarse normal. El guardia pierde el rastro.

**Tiempo por ciclo completo:** ~2–3 minutos (una fase de rutina).

### 2.2 Loop del Guardia (1 jugador)

```
┌─────────────────────────────────────────────────────────────┐
│  Observar → Detectar sospechoso → Perseguir físicamente     │
│       ↑                                          ↓          │
│  Reintentar ← Perder rastro / Atrapar                      │
└─────────────────────────────────────────────────────────────┘
```

**Detalle por paso:**

1. **Observar** — Patrullar zonas, revisar cámaras de seguridad, escuchar sonidos sospechosos. Buscar: personas en zona incorrecta, movimiento errático, interacción con objetos prohibidos.
2. **Detectar sospechoso** — Señalar a una persona (tecla/click). Esto activa modo persecución.
3. **Perseguir físicamente** — Correr hacia el señalado. No puede atrapar a distancia. Debe llegar a rango de contacto (1.5m).
4. **Atrapar** — Si alcanza al preso → captura exitosa. Si señaló a un NPC → penalización.
5. **Perder rastro** — Si el preso se camufla → persecución termina. El guardia vuelve a observar.

### 2.3 Condiciones de Fin de Partida

| Condición | Resultado |
|-----------|-----------|
| Al menos 1 preso jugador escapa por cualquier ruta | **Presos ganan** |
| Se activa un motín exitoso (3 errores del guardia) | **Presos ganan** |
| Se acaba el jornada sin que el guardia capture a todos | **Presos ganan** |
| El guardia captura a todos los presos jugadores | **Guardia gana** |
| Jornada completa termina sin escape ni motín | **Guardia gana** |

---

## 3. Mecánica Central — Sistema de Persecución

Esta es la mecánica core que diferencia a Jailbreak. No hay medidor de sospecha pasivo: el guardia debe **actuar físicamente**, lo que genera tensión, errores y humor.

### 3.1 Flujo de Persecución (Detalle Técnico)

```
ESTADO: PATRULLA (guardia)
  │
  ├─ Guardia presiona [SEÑALAR] apuntando a un personaje
  │
  ▼
ESTADO: PERSECUCIÓN ACTIVA
  │
  ├─ Duración máxima: 15 segundos
  ├─ Guardia: velocidad +20% (sprint de persecución)
  ├─ Preso señalado: recibe alerta visual + sonora
  ├─ Otros presos: NO reciben alerta (deben darse cuenta solos)
  │
  ├─ SI guardia llega a rango ≤ 1.5m del señalado:
  │     ├─ SI es jugador preso → CAPTURA (preso eliminado)
  │     └─ SI es NPC → ERROR (penalización al guardia)
  │
  ├─ SI pasan 15 seg sin captura:
  │     └─ Persecución termina → vuelve a PATRULLA
  │
  └─ SI preso cumple condición de camuflaje:
        └─ Persecución termina → vuelve a PATRULLA
```

### 3.2 Condiciones de Camuflaje (Perder el Rastro)

| Acción del preso | Efecto | Implementación |
|------------------|--------|----------------|
| Volver a la zona correcta de la fase actual | Guardia pierde marcador visual del preso | Verificar zona del preso vs. zona de fase activa |
| Mezclarse físicamente entre 3+ NPCs | Guardia no puede distinguir cuál es | Raycast desde guardia; si hay 3+ personajes en radio de 3m alrededor del preso, se pierde |
| Cambiar de piso o zona alejada | Persecución se resetea si guardia no llega en 10 seg | Verificar distancia > 25m o cambio de NavMesh area |
| Tirar objeto al guardia | Guardia tropieza, preso gana 3–4 seg de ventaja | Animación de tropiezo + stun temporal del guardia |

### 3.3 Errores del Guardia (Señalar NPC Inocente)

| Error N° | Penalización | Duración | Implementación |
|----------|-------------|----------|----------------|
| 1er error | NPC enojado sigue al guardia, tapa parcialmente su visión | 60 seg | NPC entra en estado "follow_guard", se posiciona delante |
| 2do error | Grupo de 3–4 NPCs se vuelve hostil, guardia pierde acceso a esa zona | 120 seg | Zona marcada como bloqueada, NPCs patrullan la entrada |
| 3er error | Tensión de motín al máximo — presos pueden activar motín manualmente | Permanente | Flag global `riot_available = true` |
| Motín activado | Todos los NPCs rodean al guardia. Pantalla de derrota | Fin de partida | Todos los NPCs convergen en posición del guardia |

**Nota de diseño:** El guardia tiene exactamente 3 oportunidades de error. Esto genera un dilema constante: *¿estoy lo suficientemente seguro para señalar, o espero y arriesgo que escapen?*

### 3.4 Parámetros de Balance (Tweakeables)

| Parámetro | Valor inicial | Rango de ajuste | Notas |
|-----------|--------------|-----------------|-------|
| `chase_duration_max` | 15 seg | 10–20 seg | Tiempo máximo de persecución activa |
| `guard_sprint_multiplier` | 1.20x | 1.10–1.35x | Velocidad extra del guardia en persecución |
| `prisoner_sprint_multiplier` | 1.15x | 1.05–1.25x | Velocidad extra del preso huyendo |
| `camouflage_npc_count` | 3 | 2–5 | NPCs necesarios cerca para camuflarse |
| `camouflage_radius` | 3.0m | 2.0–5.0m | Radio para contar NPCs cercanos |
| `catch_range` | 1.5m | 1.0–2.5m | Distancia para capturar |
| `guard_error_penalty_1_duration` | 60 seg | 30–90 seg | Duración del NPC enojado siguiendo |
| `guard_error_penalty_2_duration` | 120 seg | 60–180 seg | Duración del bloqueo de zona |
| `stumble_stun_duration` | 3.5 seg | 2.0–5.0 seg | Duración del tropiezo por objeto lanzado |

---

## 4. Sistema de Rutina Diaria

### 4.1 Fases de la Jornada

La jornada es el "reloj" de la partida. Cada fase dura un tiempo real fijo y ocurre en una zona específica. Los NPCs siguen la rutina automáticamente. Los presos jugadores deben imitarla.

| # | Fase | Hora (ficticia) | Duración real | Zona | Comportamiento NPC |
|---|------|-----------------|---------------|------|-------------------|
| 1 | Formación | 06:00 | 60 seg | Patio central | Fila ordenada, quietos |
| 2 | Desayuno | 07:00 | 90 seg | Comedor | Sentados comiendo, algunos caminan a servirse |
| 3 | Limpieza | 08:00 | 90 seg | Pasillos / celdas | Caminan con trapeadores, entran/salen de celdas |
| 4 | Patio libre | 10:00 | 120 seg | Patio exterior | Caminan libre, grupos conversando, ejercicio |
| 5 | Almuerzo | 12:00 | 90 seg | Comedor | Igual que desayuno |
| 6 | Trabajo | 14:00 | 120 seg | Taller / cocina / lavandería | NPCs usan herramientas, cargan objetos |
| 7 | Celda (siesta) | 16:00 | 90 seg | Celdas | Acostados o sentados en la celda |
| 8 | Cena | 18:00 | 90 seg | Comedor | Igual que desayuno/almuerzo |
| 9 | Luces apagadas | 22:00 | 120 seg | Celdas | Acostados, oscuridad, guardia con linterna |

**Duración total de la partida:** ~15 minutos (930 seg de fases + transiciones de ~30 seg entre fases).

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

- Un **silbato** suena 5 seg antes de cada cambio de fase (audio global).
- Los NPCs comienzan a moverse hacia la zona de la siguiente fase.
- Los presos tienen **10 seg de gracia** para llegar a la zona correcta sin generar sospecha.
- Si un preso no llega a tiempo, genera una alerta visual para el guardia: **"Alguien no está en su zona"** (sin identidad).

---

## 5. Sistema de Inventario y Objetos

### 5.1 Inventario del Preso

- **2 slots** de inventario máximo.
- Los objetos se recogen con **[INTERACTUAR]** (tecla E). Animación de 1.5 seg visible para quien mire.
- Los objetos se usan con **[USAR]** (tecla Q) en la ubicación correcta.
- Los objetos **no se dropean** voluntariamente (evitar griefing).
- Si un preso es capturado, sus objetos caen al suelo. Otro preso puede recogerlos.

### 5.2 Objetos de Escape (por Ruta)

#### Ruta 1 — Conducto de Ventilación (Cooperativa)

Los presos fabrican una herramienta improvisada para abrir la rejilla de seguridad del conducto de ventilación del taller y usan un mapa de conductos para navegar hacia el exterior.

| Paso | Objeto | Ubicación | Fase disponible | Quién | Dificultad |
|------|--------|-----------|-----------------|-------|------------|
| 1 | **Destornillador** | Taller — banco de trabajo | Trabajo | Preso A | Media — guardia patrulla el taller |
| 2 | **Palo de madera** → combinar con destornillador para fabricar **herramienta improvisada** (más torque para tornillos de seguridad de la rejilla) | Taller — zona de carpintería → fabricar en Celda | Trabajo → Siesta | Preso B | Media — debe llevar el palo a la celda sin ser visto |
| 3 | **Mapa de conductos** de la oficina del guardia → necesario para navegar el sistema de ventilación sin llegar a un callejón sin salida | Pasillo — oficina del guardia | Cualquier fase (muy arriesgado) | Preso C | Alta — la oficina es zona de alto riesgo |

**Acción final:** Cuando los 3 objetos están listos, los presos van al taller, abren la rejilla con la herramienta improvisada y navegan los conductos con el mapa. Salen por el techo. Se puede ejecutar en **cualquier fase** — el riesgo está en llegar al taller sin ser detectados.
**Tiempo de escape final:** 15 seg (animación de apertura + gateo por conducto).

#### Ruta 2 — Túnel (Individual)

Un solo preso excava un túnel desde su celda, saca la tierra disimuladamente y usa un mapa de cloacas para navegar la salida. La dificultad está en que necesita 3 objetos pero solo tiene 2 slots de inventario, forzando una planificación cuidadosa.

| Paso | Objeto | Ubicación | Fase disponible | Quién | Dificultad |
|------|--------|-----------|-----------------|-------|------------|
| 1 | **Cuchara** → cavar detrás del inodoro de la celda (requiere 2 fases de siesta para completar la excavación) | Comedor — cajón de cubiertos | Desayuno / Almuerzo / Cena | El mismo preso | Baja — muchos NPCs cerca, fácil disimular |
| 2 | **Bolsa de lona** → sacar la tierra del túnel durante el patio (vaciarla en esquina NE, punto ciego de cámara) | Lavandería — canastos | Trabajo | El mismo preso | Media — debe ir a la lavandería fuera de su zona |
| 3 | **Mapa de cloacas** → navegar el túnel subterráneo sin perderse | Pasillo — cuarto de servicio | Limpieza | El mismo preso | Alta — cuarto de servicio es zona restringida |

**Gestión de inventario:** El preso tiene 2 slots pero necesita 3 objetos. Debe usar la cuchara (cavar) y descartarla antes de buscar el mapa. Orden obligatorio: cuchara → bolsa → (vaciar tierra, libera slot) → mapa.
**Acción final:** De noche (luces apagadas), entra al túnel desde la celda y sale por una alcantarilla fuera del muro este.
**Tiempo de escape final:** 20 seg (animación de entrada + carrera por túnel).

#### Ruta 3 — Carro de Ropa Sucia (Cooperativa)

Los presos preparan un dummy para cubrir la ausencia de uno de ellos, sobornan a un NPC para destrabar el carro de lavandería y roban el horario de recolección para saber exactamente cuándo sale el carro.

| Paso | Objeto/Acción | Ubicación | Fase disponible | Quién | Dificultad |
|------|---------------|-----------|-----------------|-------|------------|
| 1 | **Almohada extra + ropa** → fabricar **dummy** y dejarlo en el catre propio (engaña la inspección nocturna del guardia) | Celda / Lavandería | Siesta / Trabajo | Preso A | Media — debe conseguir ropa de la lavandería |
| 2 | **Sobornar a un NPC** del turno de lavandería con un objeto de valor (cuchara afilada o cigarrillos del patio) → el NPC deja el carro destrabado cerca de la puerta de servicio | Patio → Lavandería | Patio libre → Trabajo | Preso B | Media — debe conseguir el objeto de soborno primero |
| 3 | **Horario de recolección** de la oficina del guardia → saber exactamente cuándo pasa el carro por la puerta de servicio (ventana de 30 seg) | Pasillo — oficina del guardia | Cualquier fase (muy arriesgado) | Preso C | Alta — la oficina es zona de alto riesgo |

**Acción final:** Durante la cena (última fase antes de la noche), el preso se mete en el carro. El dummy cubre su ausencia en la inspección nocturna. El carro sale por la puerta de servicio en la ventana exacta del horario. Si el guardia inspecciona el catre de cerca, descubre el dummy y la ruta falla.
**Tiempo de escape final:** 10 seg (meterse en el carro + salida automática).

#### Comparación de Rutas

| | Ventilación | Túnel | Carro de Ropa |
|---|---|---|---|
| **Tipo** | Cooperativa (3 presos) | Individual (1 preso) | Cooperativa (3 presos) |
| **Pasos** | 3 | 3 | 3 |
| **Cuándo se puede escapar** | Cualquier fase | Luces apagadas | Cena |
| **Mayor riesgo** | Robar mapa de oficina del guardia | Gestión de inventario (3 objetos, 2 slots) | Robar horario de oficina + inspección del catre |
| **Escapa** | Todos los presos juntos | Solo 1 preso | Solo 1 preso (los otros cubren) |
| **Contramedida del guardia** | Ver a alguien en el taller fuera de fase | Escuchar excavación / ver tierra en el patio | Inspeccionar catre de cerca / revisar el carro |

### 5.3 Objetos de Molestia (Tácticos)

| Objeto | Ubicación | Efecto | Duración | Usos |
|--------|-----------|--------|----------|------|
| Jabón | Baños (celda) | Guardia tropieza, pierde orientación | 3 seg stun | 1 uso, se consume |
| Bandeja de comida | Comedor | Visión del guardia reducida (salpicadura) | 2 seg | 1 uso, se consume |
| Interruptor de luz | Panel eléctrico (pasillo) | Sector a oscuras, guardia debe reparar | 15 seg o hasta que el guardia repare | Ilimitado pero ruidoso |
| Dummy (muñeco) | Celda — fabricado con almohada + ropa | Engaña al guardia en inspección nocturna | Hasta que el guardia inspeccione de cerca | 1 uso |

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
| **Dummy en la cama** | Colocar en celda propia | Engaña inspección visual nocturna del guardia | — | Bulto visible bajo la sábana |

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
| CAM-3 | Taller + lavandería (entrada) | Interior de conductos |
| CAM-4 | Patio exterior (esquina SW) | Esquina NE (muro perimetral) |

### 7.3 Contramedidas de los Presos

- **Timing**: actuar cuando la luz roja indica que el guardia está mirando otra cámara.
- **Punto ciego natural**: la esquina NE del patio no tiene cobertura.
- **Apagar luces** (panel eléctrico): las cámaras del sector afectado pierden visibilidad.

---

## 8. Diseño de Nivel — La Prisión

### 8.1 Layout General

```
                    ┌──────────────────────────┐
                    │      PATIO EXTERIOR       │
                    │   (punto ciego NE →) ○    │
                    │                          │
                    └──────────┬───────────────┘
                               │
    ┌──────────────┬───────────┼───────────────┬──────────────┐
    │              │           │               │              │
    │   TALLER     │  PASILLO PRINCIPAL        │  LAVANDERÍA  │
    │              │  (panel eléctrico,        │  (desagüe,   │
    │  (herram.)   │   oficina guardia,        │   canastos)  │
    │              │   cámaras)                │              │
    ├──────────────┤           │               ├──────────────┤
    │              │           │               │              │
    │   COMEDOR    │           │               │   BAÑOS      │
    │              │           │               │              │
    └──────────────┘           │               └──────────────┘
                               │
                    ┌──────────┴───────────────┐
                    │    BLOQUE DE CELDAS       │
                    │    (2 pisos, 20 celdas)   │
                    │    Pasillo central        │
                    └──────────────────────────┘
```

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
- **Estructura:** Zona de carpintería con bancos, zona de metal con herramientas, conductos de ventilación visibles en el techo.
- **Objetos interactuables:** Destornillador (escape ruta 1), palo de madera (escape ruta 1), cajones con llave.
- **Punto de interés:** Rejilla del conducto de ventilación con tornillos de seguridad (ruta de escape 1). Requiere herramienta improvisada para abrirla.

#### Lavandería
- **Estructura:** Canastos grandes, máquinas industriales, tuberías visibles, puerta de servicio hacia exterior, carro de ropa sucia.
- **Objetos interactuables:** Canastos (esconderse brevemente, 5 seg máx), bolsa de lona (escape ruta 2), ropa extra (escape ruta 3), carro de ropa sucia (escape ruta 3).
- **Punto de interés:** Puerta de servicio — el carro de ropa sale por aquí en horarios fijos.

#### Patio Exterior
- **Estructura:** Espacio abierto, muro perimetral alto, torre de vigilancia (decorativa), esquina NE sin cámara.
- **Objetos interactuables:** Tierra cavable en esquina NE (punto ciego).
- **Punto de interés:** Zona más amplia, difícil para el guardia cubrir todo.

#### Pasillo Principal
- **Estructura:** Conecta todas las zonas. Contiene oficina del guardia, panel eléctrico, sistema de cámaras.
- **Objetos interactuables:** Panel eléctrico (apagar luces), oficina del guardia (mapa de conductos ruta 1, horario de recolección ruta 3 — muy arriesgado), cuarto de servicio (mapa de cloacas ruta 2).
- **Punto de interés:** Zona de alto tráfico NPC, fácil mezclarse pero también fácil ser visto.

### 8.3 Rutas de Escape (Mapa Detallado)

#### Ruta 1 — Conducto de Ventilación (Cooperativa)
```
Taller (robar destornillador) + Taller (robar palo de madera) → 
Celda (fabricar herramienta improvisada) + Oficina guardia (robar mapa de conductos) →
Taller (abrir rejilla con herramienta) → Conducto (navegar con mapa) → Techo → Exterior
```
- **Acciones requeridas:** 3 (una por jugador).
- **Cuándo se puede ejecutar:** Cualquier fase — el riesgo es llegar al taller sin ser detectado.
- **Tiempo de escape final:** 15 seg.

#### Ruta 2 — Túnel (Individual)
```
Comedor (robar cuchara) → Celda (cavar túnel, 2 fases de siesta) →
Lavandería (robar bolsa de lona) → Patio NE (vaciar tierra, punto ciego) →
Cuarto de servicio (robar mapa de cloacas) → Celda (entrar al túnel de noche) →
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
| Uniforme | Gris Alcatraz, número en el pecho | Marrón oscuro, gorra |
| Distinción visual | **Ninguna** entre jugadores y NPCs (intencional) | Único — siempre visible |
| Identificación aliada | Ícono discreto sobre compañeros presos cuando están a <5m | — |
| Accesorio nocturno | — | Linterna en mano |

### 10.3 Iluminación

| Fase | Iluminación |
|------|-------------|
| Día (formación → cena) | Fluorescente interior, luz natural en patio |
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
| Alarma de persecución | Sonido tenso cuando el guardia señala (solo para el preso señalado) | Alta |

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
│  ○ ○ ●                  ⚠ PERSECUCIÓN ⚠     │  ← Progreso escape (3 piezas)
│  Compañeros: ← →                             │  ← Posición aliados (periférica)
└──────────────────────────────────────────────┘
```

| Elemento | Posición | Descripción |
|----------|----------|-------------|
| Fase actual + timer | Top-center | Nombre de la fase + tiempo restante |
| Inventario | Bottom-left | 2 slots con ícono del objeto (◆ = ocupado, ◇ = vacío) |
| Prompt contextual | Bottom-center | "[E] Recoger" / "[Q] Usar" cuando hay interacción disponible |
| Alerta de persecución | Bottom-right | Aparece solo cuando el guardia te señaló. Rojo pulsante. |
| Progreso de escape | Bottom-left (sobre inventario) | Círculos: ○ = falta, ● = conseguido. 3 por ruta. |
| Posición de aliados | Bordes de pantalla | Flechas direccionales indicando dónde están los compañeros |

### 12.2 HUD del Guardia

```
┌──────────────────────────────────────────────┐
│ [FASE: Desayuno]          [Timer: 1:12]  (↗) │
│                                     [CAM] ▣  │  ← Feed de cámaras (miniatura)
│                                     [CAM] ▣  │
│                                              │
│                     ◎                        │  ← Crosshair de señalamiento
│                                              │
│                                              │
│  Errores: ✕ ○ ○                              │  ← Contador de errores (máx 3)
│  Tensión motín: ██░░░░                       │  ← Barra de tensión
│  [TAB] Cámaras                               │  ← Prompt de cámaras
│  ⚡ Alerta: Zona Taller — alguien fuera      │  ← Alerta de comportamiento
└──────────────────────────────────────────────┘
```

| Elemento | Posición | Descripción |
|----------|----------|-------------|
| Fase actual + timer | Top-center | Igual que presos |
| Mini-cámaras | Top-right | 4 thumbnails pequeños de las cámaras |
| Crosshair de señalamiento | Center | Más grande que el de presos, indica "puedo señalar" |
| Contador de errores | Bottom-left | Cruces rojas por cada error (máx 3) |
| Barra de tensión de motín | Bottom-left | Sube con errores. Al máximo, presos pueden activar motín |
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
| Señalar sospechoso | Click izq. | | | x |
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
| Persecución (señalar/atrapar) | **Servidor** | Validación de distancia server-side |
| Cámaras | **Cliente del guardia** (notifica servidor) | Baja latencia para toggle |
| Movimiento input | **Cliente** (enviado al servidor) | Input prediction |

### 14.3 Eventos Socket.io Clave

| Evento | Dirección | Payload |
|--------|-----------|---------|
| `player:move` | Cliente → Servidor | `{ position, rotation, velocity }` |
| `player:interact` | Cliente → Servidor | `{ objectId, action }` |
| `guard:mark` | Cliente → Servidor | `{ targetId }` |
| `guard:catch` | Servidor → Todos | `{ guardId, prisonerId, success }` |
| `phase:change` | Servidor → Todos | `{ phase, duration, zone }` |
| `npc:positions` | Servidor → Todos | `{ npcs: [{ id, pos, rot, anim }] }` (delta compressed) |
| `chase:start` | Servidor → Presos | `{ targetId }` (solo al señalado) |
| `chase:end` | Servidor → Todos | `{ reason: 'caught' | 'lost' | 'timeout' }` |
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
| Sistema de persecución (señalar → correr → atrapar/perder) | P0 | 2 días | Movimiento FPS |
| Lógica de camuflaje (zona correcta + entre NPCs = perder rastro) | P0 | 1 día | Persecución + NPCs |
| Sistema de inventario (recoger, guardar, usar objetos) | P0 | 1.5 días | — |
| 1 ruta de escape completa (conducto de ventilación — cooperativa) | P0 | 2 días | Inventario + Mapa |
| Penalizaciones por errores del guardia (NPC enojado, zona bloqueada, motín) | P1 | 1 día | Persecución |
| Mecánicas de molestia (jabón, tirar comida) | P1 | 0.5 día | Inventario |
| Condiciones de victoria/derrota | P0 | 1 día | Persecución + Escape |
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
| Balance de tiempos y dificultad de persecución | P0 | 1 día | Playtesting |
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
| Balance persecución (fácil/difícil) | Alta | Alto | Playtesting desde semana 2. Todos los parámetros de persecución son tweakeables (ver sección 3.4). |
| Scope creep | Alta | Medio | Priorizar 1 ruta funcional antes de agregar la segunda. Scope cuts definidos. |
| Bugs Socket.io en partidas de 4 | Media | Medio | Testear con 2 jugadores primero, escalar de a uno. Reconexión automática (30 seg). |
| WebGL build pesado | Media | Medio | Texturas comprimidas, asset bundles, streaming de assets. Target: <50MB initial load. |
| Latencia alta en persecuciones | Media | Alto | Server-authoritative con client prediction. Compensación de lag en detección de captura. |

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
| **Fase** | Período de tiempo dentro de la jornada (formación, desayuno, etc.) |
| **Rutina** | Comportamiento esperado de un preso/NPC durante una fase específica |
| **Señalar** | Acción del guardia para marcar a alguien como sospechoso e iniciar persecución |
| **Camuflaje** | Acción de mezclarse con NPCs o volver a la rutina para perder al guardia |
| **Motín** | Condición de victoria de los presos que se activa tras 3 errores del guardia |
| **Ruta de escape** | Secuencia de objetos y acciones que los presos deben completar para escapar |
| **Delta compression** | Enviar solo los cambios de posición de NPCs, no las posiciones completas |
| **Client prediction** | El cliente predice el movimiento local antes de recibir confirmación del servidor |

---

*GDD v1.0 — Documento vivo. Prioridad absoluta: que sea divertido con 2 jugadores desde el día 1.*
