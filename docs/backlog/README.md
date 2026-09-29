# Backlog — PixelRealms MVP

## Hitos (implementar en este orden)
| Hito | Resultado jugable | HUs |
|---|---|---|
| **M1 · Caminar juntos** | 2+ amigos inician sesión, crean personaje y se ven moverse por el mapa | HU-001 → HU-026 |
| **M2 · Pelear** | matar slimes con hechizos de clase, morir y reaparecer, ganar XP | HU-030 → HU-041 |
| **M3 · Botín** | lootear, inventario, equipar, pociones, vendedor | HU-050 → HU-058 |
| **M4 · Grupo** | chat, grupos, XP compartida, subir a nivel 10 | HU-042, HU-043, HU-060 → HU-063 |
| **M5 · Online** | servidor en VPS con `wss://`, versión web, contenido y arte completos, jefe | HU-070 → HU-084 |

## Índice
| ID | Título | Épica | Prioridad | Est. | Estado |
|---|---|---|---|---|---|
| HU-001 | Monorepo, solución .NET y CI | E0 | Must | M | Pendiente |
| HU-002 | Infra local con Docker (PostgreSQL) | E0 | Must | S | Pendiente |
| HU-003 | Carga y validación de contenido (ContentValidator) | E0 | Must | M | Pendiente |
| HU-004 | Esqueleto del game loop de 20 Hz | E0 | Must | M | Pendiente |
| HU-005 | Proyecto Godot base (autoloads, pixel-perfect, GUT) | E0 | Must | M | Pendiente |
| HU-006 | Protocolo base: sobre, registro, Ping/Pong | E0 | Must | M | Pendiente |
| HU-010 | Registro de cuenta | E1 | Must | S | Pendiente |
| HU-011 | Inicio de sesión | E1 | Must | S | Pendiente |
| HU-012 | Crear personaje | E1 | Must | M | Pendiente |
| HU-013 | Listar y borrar personajes | E1 | Must | S | Pendiente |
| HU-014 | Entrar al mundo (ticket + Hello/Welcome) | E1 | Must | M | Pendiente |
| HU-020 | Cargar mapa Tiled en servidor y cliente | E2 | Must | M | Pendiente |
| HU-021 | Movimiento autoritativo con colisión | E2 | Must | L | Pendiente |
| HU-022 | Predicción y reconciliación del jugador propio | E2 | Must | L | Pendiente |
| HU-023 | Ver a otros jugadores (AOI + interpolación) | E2 | Must | L | Pendiente |
| HU-024 | Cámara, capas y nombres sobre personajes | E2 | Must | S | Pendiente |
| HU-025 | Desconexión, linkdead y reconexión | E2 | Must | M | Pendiente |
| HU-026 | Guardado de posición y estado | E2 | Must | M | Pendiente |
| HU-030 | Seleccionar objetivo | E3 | Must | S | Pendiente |
| HU-031 | Monstruos: spawn, patrulla y respawn | E3 | Must | M | Pendiente |
| HU-032 | Auto-ataque | E3 | Must | M | Pendiente |
| HU-033 | Lanzar hechizos (casteo, GCD, CD, recurso) | E3 | Must | L | Pendiente |
| HU-034 | Resolución de efectos y fórmulas | E3 | Must | L | Pendiente |
| HU-035 | Auras (DoT, HoT, stun, root, slow, shield, stat_mod) | E3 | Must | L | Pendiente |
| HU-036 | IA de monstruos: aggro, persecución, amenaza, evadir | E3 | Must | L | Pendiente |
| HU-037 | Muerte y reaparición | E3 | Must | M | Pendiente |
| HU-038 | HUD de combate (marcos, cast bar, hotbar, textos) | E3 | Must | L | Pendiente |
| HU-039 | Recursos: maná, ira, energía y regeneración | E3 | Must | M | Pendiente |
| HU-040 | Ganar experiencia | E4 | Must | S | Pendiente |
| HU-041 | Subir de nivel y desbloquear hechizos | E4 | Must | M | Pendiente |
| HU-042 | Panel de personaje (stats) | E4 | Should | M | Pendiente |
| HU-043 | Libro de hechizos y hotbar configurable | E4 | Should | M | Pendiente |
| HU-050 | Botín de monstruos | E5 | Must | L | Pendiente |
| HU-051 | Inventario (bolsa de 24) | E5 | Must | L | Pendiente |
| HU-052 | Equipar y desequipar | E5 | Must | M | Pendiente |
| HU-053 | Tooltips y comparación | E5 | Must | M | Pendiente |
| HU-054 | Usar consumibles | E5 | Must | M | Pendiente |
| HU-055 | Oro y vendedor NPC | E5 | Must | M | Pendiente |
| HU-056 | Dividir, fusionar y destruir stacks | E5 | Should | S | Pendiente |
| HU-057 | Persistencia de inventario y auditoría | E5 | Must | M | Pendiente |
| HU-058 | Equipo inicial por clase | E5 | Must | S | Pendiente |
| HU-060 | Chat (decir, global, susurro) | E6 | Must | M | Pendiente |
| HU-061 | Grupos (invitar, aceptar, salir, expulsar) | E6 | Must | M | Pendiente |
| HU-062 | Marcos de grupo y XP/oro compartidos | E6 | Must | M | Pendiente |
| HU-063 | Lista de jugadores en línea | E6 | Could | S | Pendiente |
| HU-070 | Comandos de administrador | E7 | Should | M | Pendiente |
| HU-071 | Rate limiting y protección de mensajes | E7 | Must | M | Pendiente |
| HU-072 | Métricas y logs del servidor | E7 | Should | S | Pendiente |
| HU-073 | Despliegue en VPS con TLS (wss) | E7 | Must | M | Pendiente |
| HU-074 | Build web y de escritorio del cliente | E7 | Must | M | Pendiente |
| HU-075 | Backups automáticos | E7 | Must | S | Pendiente |
| HU-080 | Mapa "meadow" completo | E8 | Must | L | Pendiente |
| HU-081 | Arte de clases y monstruos | E8 | Must | L | Pendiente |
| HU-082 | Íconos de items y hechizos | E8 | Must | M | Pendiente |
| HU-083 | Mazmorra y jefe Rey Liche Menor | E8 | Should | L | Pendiente |
| HU-084 | Pasada de balance | E8 | Should | M | Pendiente |

Estimación: **S** ≤ 1 sesión de Claude Code · **M** 1–3 sesiones · **L** 3+ sesiones (considera dividirla).

## Plantilla de HU
```markdown
### HU-XXX · Título
**Como** <rol> **quiero** <acción> **para** <beneficio>.
- Prioridad: Must | Should | Could · Estimación: S | M | L · Estado: Pendiente | En curso | Hecha
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
