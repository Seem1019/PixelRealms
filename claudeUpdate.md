The regex ate too much; I'll rewrite the file cleanly instead.Numbers locked. Now the config file and its schema.Now the schemas: schools, `type` in monsters, `scaling`/`dash`/`groups`/`removesKinds`, recommended lists, level 15.Let me validate everything now before writing docs on top of it.Content is consistent. Now the design documents.Now skills, CLAUDE.md, architecture, protocol, database and the agent definition.Listo. Apliqué las 11 decisiones más la XP sin piso y la escuela mágica única en todo PixelRealms: 34 archivos cambiados y 3 nuevos, todo validado contra los schemas y sin referencias rotas. Nota: `git status` te va a mostrar más archivos tocados de los que son; es solo ruido de saltos de línea (CRLF/LF) entre Windows y el entorno donde edité. El diff real es el que describo aquí.

## Qué quedó en el repo

- **`content/rules.json` + `schemas/rules.schema.json`** (nuevos): las ~90 constantes con descripción de cada una. XP, grupo, combate, conversión por clase, afinidad, `classAdvantage`, PvP como rulesets, botín, economía, objetivo de jefes.
- **`gdd.md`** reescrito: 5.º pilar, mundo por tiers con el MVP = Tier 1 (Aldea, Campos 1–3, Colinas 3–5, Mina con el Capataz Grask nv 6), cuevas como mapa aparte con portal, equipo libre, ataque básico, triángulo, XP nueva, botín por item al azar, duelos, intercambio.
- **`combat.md`** reescrito con las fórmulas nuevas y una tabla de referencia de balance.
- **`decisions.md`**: ADR-007 a ADR-012 (mapas múltiples, rules.json, afinidad, escuela única, PvpRuleset, botín).
- **`content/*.json`**: escuelas `physical|magic`; `monsters` con `type` y sin `xp`; nivel máximo 15; monstruos del Tier 1 completos (Bandido nv 3, Kóbold nv 5, Gólem élite nv 6, Capataz); varitas (`novice_wand`, `willow_wand`); items para manos y piernas; kits de 5 hechizos con desbloqueo en 1/1/3/6/10, **Carga** para el Guerrero (efecto `dash` nuevo) y **Carrera** que rompe raíz/ralentización; `groups` de botín garantizado para jefes; el cooldown de hechizos de monstruo ya vive solo en `spells.json`.
- **Backlog**: HUs nuevas **HU-027** portales, **HU-059** intercambio, **HU-064** duelos; actualizadas HU-003, 004, 020, 032, 034, 035, 036, 037, 039, 040, 041, 042, 050, 052, 053, 058, 062, 080, 083, 084.
- Skills, agentes, `CLAUDE.md`, `architecture.md`, `protocol.md`, `database.md` alineados.
- **`docs/design/balance-notes.md`** (nuevo): las tablas de abajo con más detalle.

## Multiplicadores propuestos (`rules.classScaling`)

| Clase | str→AP | agi→AP | int→AP | int→SP | Vida/sta | Armadura | Velocidad |
|---|---|---|---|---|---|---|---|
| Guerrero | 2.0 | 0.5 | 0.3 | 0.8 | 12 | ×1.00 | ×1.00 |
| Pícaro | 1.0 | 2.0 | 0.3 | 0.8 | 10 | ×0.85 | ×1.15 |
| Mago | 0.6 | 0.4 | 1.4 | 1.5 | 10 | ×0.75 | ×0.90 |
| Sacerdote | 0.7 | 0.4 | 1.4 | 1.3 | 10 | ×0.85 | ×0.90 |

Afinidad alta ×1.0, media ×0.85, baja ×0.7 sobre todo lo numérico del item. Un hallazgo importante: la afinidad sola no alcanzaba. Con solo eso, Sacerdote + espada rendía el 31 % del Pícaro, porque las bases de stats ya los separan (el Sacerdote tiene 4 de fuerza y no sube). El `int→AP 1.4` de los casters es lo que hace viable su básico con arma física.

## Ejemplo numérico: nivel 5 vs Goblin arquero (nv 5), solo ataque básico

| Combinación | DPS | Lo mato en | Me mata en | Vida perdida |
|---|---|---|---|---|
| Pícaro + espada (alta) | 7.9 | 18 s | 85 s | 21 % |
| Guerrero + espada/escudo/malla (alta) | 6.6 | 21 s | 163 s | 13 % |
| Sacerdote + varita (alta) | 5.0 | 28 s | 63 s | 45 % |
| **Sacerdote + espada (media)** | **4.7 (60 % del Pícaro)** | 30 s | 66 s | 45 % |
| Sacerdote + espada + placas (media/baja) | 4.6 | 30 s | 87 s (53 % del Guerrero) | 35 % |
| Mago + espada + placas (baja/baja) | 4.4 (56 %) | 32 s | 80 s (49 % del Guerrero) | 40 % |

**Piso de viabilidad confirmado:** ninguna combinación pierde más del 50 % de vida por kill, así que todas farmean solas. Fuera de rol queda en 56–61 % de daño y 49–61 % de aguante. Dos cosas a decidir con calma: el Mago con espada y placas (doble penalización) queda en 56 %, un poco bajo tu 60 %; se sube con `affinity.multipliers.baja: 0.75` si quieres. Y el aguante de casters con pesado se queda en ~50 % porque la diferencia la ponen `baseHp` y el aguante base, no la afinidad; subirlo más borra la identidad del tanque.

## Sacerdote y el triángulo

Queda fuera, como soporte: **gana al Guerrero** por desgaste (cura más de lo que él pega y no tiene interrupción), **pierde con el Pícaro** (Gubia interrumpe Sanar) y va **parejo con el Mago**. Si quieres meterlo en la figura, la opción es un ciclo de 4 dándole un `silence` corto que solo importa contra casters (Mago > Guerrero > Pícaro > Sacerdote > Mago). Recomiendo no hacerlo por ahora: su ventaja en PvP debería ser curar a un aliado, no ganar 1 vs 1.

## Dudas y riesgos que quedan abiertos

1. **Arranque lento.** Con `(5·nivel+1)` y K=200, subir a nivel 2 son 33 slimes y el primer hechizo nuevo llega tras ~90 kills. Choca con "jugar en 5 minutos". Palanca fácil: `xpCurveK` a 100–150 solo mientras probamos.
2. **Maná del Mago contra el jefe:** se agota a los ~30 s de un combate de 60–75 s. Lo dejé así a propósito (obliga a usar varita/básicos y pociones), pero puede sentirse mal. Verificar en HU-084.
3. **Carga es código nuevo** (efecto `dash`) y **Carrera** también (`removesKinds`/`immuneKinds`). Están en HU-034/035.
4. **El Guerrero perdió Torbellino** para que Carga entrara en los 5 hechizos. Sin AoE en el MVP para él.
5. **Los tiers 2 y 3** tienen dos placeholders (Esqueleto nv 13, Rey Liche nv 15) para no perder los items épicos; el Árbol Podrido nv 11 no existe aún en datos.
6. **Cuidado con los cementerios por zona + jefe compartido:** sin penalización por morir, un grupo puede matar al Capataz a punta de morir y volver. Opción: bloqueo de la sala del jefe mientras está en combate.
7. **Intercambio + botín por dueño = mercado gris entre amigos.** Con 20 personas no importa, pero define si el oro se puede intercambiar libremente (hoy sí).
8. **Duelos en la aldea con `say` a 20 tiles:** el spam de "Ana ganó el duelo" puede molestar; lo puse en `say`, se puede pasar a un mensaje solo para los dos.

Si te parece bien, el siguiente paso natural es hacer el commit de todo esto y arrancar por HU-003 (validador + `rules.json`), que es lo que cierra el círculo de "nada hardcodeado".