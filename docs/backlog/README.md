# Backlog — PixelRealms MVP · Fase 1 (Tier 1)

> El MVP completo son 3 fases, una por tier (niveles 1–15). Este backlog cubre la **Fase 1**; las Fases 2 y 3 se
> detallan cuando la Fase 1 esté terminada y probada con amigos (pilar 6 del GDD).

## Hitos (implementar en este orden)
| Hito | Resultado jugable | HUs |
|---|---|---|
| **M1 · Caminar juntos** | 2+ amigos inician sesión, crean personaje y se ven moverse por el mapa | HU-001 → HU-027 |
| **M2 · Pelear** | matar slimes con hechizos de clase (un objetivo y áreas apuntadas), morir y reaparecer, ganar XP | HU-030 → HU-041, HU-085 → HU-088 |
| **M3 · Botín** | lootear (por item, al azar), inventario, equipo libre con afinidad, pociones, vendedor, intercambio | HU-050 → HU-059 |
| **M4 · Grupo y duelos** | chat, grupos, XP compartida, duelos, subir de nivel en el Tier 1 | HU-042 → HU-044, HU-060 → HU-064 |
| **M5 · Online** | servidor en VPS con `wss://`, versión web, Tier 1 completo con la Mina y el Capataz | HU-070 → HU-084, HU-089 |
| *Sin hito (añadidas tras la prueba de juego)* | combate más legible y cómodo, arte de combate y fuente HD | HU-090 → HU-098 |

> Estados revisados contra el código en la auditoría del 2026-10-02 (`main` @ `0cd4f508`): **Parcial** = algún criterio de
> aceptación sin cumplir; lo que falta está en la ficha de cada HU.

## Índice
| ID | Título | Épica | Prioridad | Est. | Estado |
|---|---|---|---|---|---|
| HU-001 | Monorepo, solución .NET y CI | E0 | Must | M | Hecha |
| HU-002 | Infra local con Docker (PostgreSQL) | E0 | Must | S | Hecha |
| HU-003 | Carga y validación de contenido (ContentValidator) | E0 | Must | M | Hecha |
| HU-004 | Esqueleto del game loop de 20 Hz | E0 | Must | M | Hecha |
| HU-005 | Proyecto Godot base (autoloads, pixel-perfect, GUT) | E0 | Must | M | Hecha |
| HU-006 | Protocolo base: sobre, registro, Ping/Pong | E0 | Must | M | Hecha |
| HU-010 | Registro de cuenta | E1 | Must | S | Hecha |
| HU-011 | Inicio de sesión | E1 | Must | S | Hecha |
| HU-012 | Crear personaje | E1 | Must | M | Hecha |
| HU-013 | Listar y borrar personajes | E1 | Must | S | Hecha |
| HU-014 | Entrar al mundo (ticket + Hello/Welcome) | E1 | Must | M | Hecha |
| HU-015 | Volver a la selección de personaje desde el juego | E1 | Must | M | Hecha |
| HU-020 | Cargar mapa Tiled en servidor y cliente | E2 | Must | M | Hecha |
| HU-021 | Movimiento autoritativo con colisión | E2 | Must | L | Hecha |
| HU-022 | Predicción y reconciliación del jugador propio | E2 | Must | L | Hecha |
| HU-023 | Ver a otros jugadores (AOI + interpolación) | E2 | Must | L | Hecha |
| HU-024 | Cámara, capas y nombres sobre personajes | E2 | Must | S | Hecha |
| HU-025 | Desconexión, linkdead y reconexión | E2 | Must | M | Hecha |
| HU-026 | Guardado de posición y estado | E2 | Must | M | Hecha |
| HU-027 | Portales y cambio de mapa | E2 | Must | M | Hecha |
| HU-030 | Seleccionar objetivo | E3 | Must | S | Hecha |
| HU-031 | Monstruos: spawn, patrulla y respawn | E3 | Must | M | Hecha |
| HU-032 | Ataque básico (todas las clases, melee y varita) | E3 | Must | M | Hecha |
| HU-033 | Lanzar hechizos (casteo, GCD, CD, recurso) | E3 | Must | L | Hecha |
| HU-034 | Resolución de efectos y fórmulas | E3 | Must | L | Hecha |
| HU-035 | Auras (DoT, HoT, stun, root, slow, shield, stat_mod) | E3 | Must | L | Hecha |
| HU-036 | IA de monstruos: aggro, persecución, amenaza, evadir | E3 | Must | L | Hecha |
| HU-037 | Muerte y reaparición | E3 | Must | M | Hecha |
| HU-038 | HUD de combate (marcos, cast bar, hotbar, textos) | E3 | Must | L | Hecha |
| HU-039 | Recursos: maná, ira, energía y regeneración | E3 | Must | M | Hecha |
| HU-040 | Ganar experiencia | E4 | Must | S | Hecha |
| HU-041 | Subir de nivel y desbloquear hechizos | E4 | Must | M | Hecha |
| HU-042 | Panel de personaje (stats) | E4 | Should | M | Hecha |
| HU-043 | Libro de hechizos y barra (4 hechizos + 4 utilizables) | E4 | Must | M | Hecha |
| HU-044 | Cambio de clase en NPC (Fases 1–2) | E4 | Must | M | Hecha |
| HU-050 | Botín de monstruos | E5 | Must | L | Hecha |
| HU-051 | Inventario (bolsa de 24) | E5 | Must | L | Hecha |
| HU-052 | Equipar y desequipar (equipo libre con afinidad) | E5 | Must | M | Hecha |
| HU-053 | Tooltips y comparación | E5 | Must | M | Hecha |
| HU-054 | Usar consumibles | E5 | Must | M | Hecha |
| HU-055 | Oro y vendedor NPC | E5 | Must | M | Hecha |
| HU-056 | Dividir, fusionar y destruir stacks | E5 | Should | S | Hecha |
| HU-057 | Persistencia de inventario y auditoría | E5 | Must | M | Hecha |
| HU-058 | Equipo inicial por clase | E5 | Must | S | Hecha |
| HU-059 | Intercambio entre jugadores | E5 | Must | M | Hecha |
| HU-060 | Chat (decir, global, susurro) | E6 | Must | M | Hecha |
| HU-061 | Grupos (invitar, aceptar, salir, expulsar) | E6 | Must | M | Hecha |
| HU-062 | Marcos de grupo y XP/oro compartidos | E6 | Must | M | Hecha |
| HU-063 | Lista de jugadores en línea | E6 | Should | S | Hecha |
| HU-064 | Duelos (PvP amistoso) | E6 | Must | L | Hecha |
| HU-070 | Comandos de administrador | E7 | Should | M | Hecha |
| HU-071 | Rate limiting y protección de mensajes | E7 | Must | M | Hecha |
| HU-072 | Métricas y logs del servidor | E7 | Should | S | Hecha |
| HU-073 | Despliegue en VPS con TLS (wss) | E7 | Must | M | Hecha |
| HU-074 | Build web y de escritorio del cliente | E7 | Must | M | Parcial |
| HU-075 | Backups automáticos | E7 | Must | S | Parcial |
| HU-080 | Mapa "meadow" completo (Tier 1) | E8 | Must | L | Hecha |
| HU-081 | Arte de clases y monstruos | E8 | Must | L | Parcial |
| HU-082 | Íconos de items y hechizos | E8 | Must | M | Hecha |
| HU-083 | Mina Abandonada y jefe Capataz Grask | E8 | Must | L | Hecha |
| HU-084 | Pasada de balance | E8 | Must | M | Hecha |
| HU-085 | Hechizo de área del Sacerdote (`ground_aoe_all`) | E3 | Must | M | Hecha |
| HU-086 | Hechizos de área apuntados (combate híbrido) | E3 | Must | L | Hecha |
| HU-087 | Saltos a un punto (`leap`) | E3 | Must | M | Hecha |
| HU-088 | Rendimiento del combate | E3 | Must | L | Hecha |
| HU-089 | Prueba de carga del combate ("Mina llena") | E7 | Must | M | Hecha |
| HU-090 | Animaciones de combate del cuerpo | E8 | Should | L | Hecha |
| HU-091 | Efectos visuales de hechizos | E8 | Should | M | Hecha |
| HU-092 | Fuente HD para la interfaz | E8 | Should | S | Hecha |
| HU-093 | Guerrero y mago con hojas dibujadas | E8 | Should | M | Hecha |
| HU-094 | Ataque básico con Espacio | E3 | Must | S | Hecha |
| HU-095 | Acercarse solo al objetivo fuera de alcance | E3 | Should | M | Hecha |
| HU-096 | Ver el alcance al mantener la tecla | E3 | Should | S | Hecha |
| HU-097 | Historial del chat | E6 | Should | S | Hecha |
| HU-098 | Estados (buffos, perjuicios y control) legibles | E3 | Must | M | Hecha |
| HU-099 | Héroes en alta resolución | E8 | Should | M | Hecha |
| HU-100 | Áreas duraderas (Fase 2) | E3 | Should | M | Pendiente |
| HU-101 | Zona del duelo | E6 | Must | M | Hecha |

## Pendiente de diseño
- **Números de los 16 hechizos de las Fases 2 y 3** con el `content-designer` (`tools/balance/`, pentagrama al nivel 15).
  Los 16 de la Fase 1 ya están medidos (`"provisional": false`, `docs/design/balance-report.md`); los demás están escalados
  a la misma escala pero siguen con `"provisional": true`.
- **Fase 2:** mejoras 1-de-2 al subir de rango y cuándo se pueden cambiar los hechizos equipados. No aplica en la Fase 1:
  cada clase tiene 4 hechizos y van todos equipados (la barra de HU-043 solo los ordena y asigna los utilizables).
- **Fase 2 (balance):** Tajo amplio, Cuchillas arrojadizas y Cono de frío son áreas apuntadas de daño sin casteo, contra la regla
  de ADR-015 ("las áreas apuntadas de daño llevan casteo"); se decide en su pasada de balance si llevan casteo/retardo o si el
  cono queda exento. Hasta entonces el validador (`server/tools/ContentValidator`) lo avisa.
- *Decididos e implementados:* rangos en los niveles 4, 8 y 12 con +15 % por rango (ADR-024; en la Fase 1 solo el del nivel 4),
  acumulación por (aura, lanzador) e inmunidad de 1,5 s tras un control (ADR-022, `AuraSystem`) y contenido no disponible
  (ADR-023, `EngineCapabilities`).

Estimación: **S** ≤ 1 sesión de Claude Code · **M** 1–3 sesiones · **L** 3+ sesiones (considera dividirla).

## Plantilla de HU
```markdown
### HU-XXX · Título
**Como** <rol> **quiero** <acción> **para** <beneficio>.
- Prioridad: Must | Should | Could · Estimación: S | M | L · Estado: Pendiente | En curso | Parcial | Hecha
- Dependencias: HU-...
- Skills: `...`

**Criterios de aceptación**
1. **Dado** ... **cuando** ... **entonces** ...

**Notas técnicas**
- ...

**Notas de implementación** (se completa al cerrar)
```

## Definition of Done (todas las HUs)
Ver skill `hu-implementation` §5. Resumen: tests verdes (xUnit + GUT + validador), revisión de autoridad si tocó servidor,
docs actualizadas, HU marcada `Hecha` con notas, commit convencional con el ID.
