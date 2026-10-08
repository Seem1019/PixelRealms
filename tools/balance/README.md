# Modelo de balance

Modelo en Python del `content-designer` para medir el pentagrama (ADR-020) y el farmeo en solitario. Valores esperados,
sin azar ni movimiento. Lee `content/` directamente; solo usa la biblioteca estándar.

| Script | Qué hace |
|---|---|
| `model.py` | Personajes (stats, equipo de referencia), simulación de 30 s y medidas de control, movilidad y aguante. |
| `pentagram.py` | Aportes de cada hechizo de la Fase 1 frente a su objetivo de `docs/design/class-kits.md`. |
| `check.py` | Pentagrama de la Fase 1, XP por hora en solitario contra el Kóbold minero y comparación con `rules.balanceTargets.pentagram.references`. |
| `boss.py` | Duración del combate contra el Capataz Grask con 4 grupos de referencia (`python boss.py 1400` prueba otra vida) y contra el Árbol Podrido (HU-117): combate paso a paso de los tríos de nivel 9 con el equipo de `gear.py` (esperado, recién llegado y completo), esquivando o no Raíces y Esporas, con los retoños a por el sanador (atacados o ignorados), cada clase sola al nivel 10 y, orientativo, los casos de la Fase 3. `--arbol` solo el Árbol; `--hp`, `--dmg`, `--spores` y `--sapling-hp` prueban otros números en memoria. |
| `tier2.py` | Monstruos del Tier 2 (HU-109): solitario por clase (rotación y solo básicos), ciclo y XP por hora contra cada monstruo de los niveles 6 a 10, y élites solo y en grupo (con pociones, curas del Sacerdote y esquivando áreas). `--tier1` repite el solitario sin equipo nuevo; `--quick` se salta los élites. |
| `phase2.py` | Hechizos de nivel 7 y 9 (HU-106): pentagrama al nivel 10 con el segundo rango y la regla 40/75 en las 15 combinaciones de cada clase (y del nivel 7 al 10), cuánto se nota el rango, qué toca cada cono o línea, y XP por hora en solitario con los equipos nuevos. `--rank 0.2` repite todo con otro `spellRankBonusPct` en memoria. |
| `gear.py` | Equipo, botín y vendedor de los niveles 6 a 10 (HU-110): comparador del tooltip, `tier2.py` con el equipo real de `items.json` (recién llegado, esperado y completo) en lugar de `gear_factor`, horas del 6 al 10, economía (oro por hora frente a pociones y comida) y élites. `--quick` se salta los élites; `--elite-dmg 1.3` y `--regen-delay 5` prueban las propuestas en memoria. |
| `upgrades.py` | Mejoras 1-de-2 de los hechizos (HU-107): las aplica como `SpellUpgrades.Apply` (lo comprueba con `shared/test-vectors/spell_upgrades.json`), mide cada mejora sobre el pentagrama al nivel 10 y lo que cambia fuera de él (recurso por minuto, valor de un lanzamiento, alcance, área con un grupo disperso), marca las parejas en las que una domina a la otra, comprueba la regla 40/75 en las 1 215 builds de cada clase (4 hechizos × sin mejora, A o B) del nivel 8 al 10 y da la XP por hora con la mejor mejora para farmear. `--quick` se salta la XP por hora; `--draft f.json` mide las mejoras de un borrador `{spellId: [mejora, mejora]}` sin tocar el contenido. |

Uso: `python tools/balance/check.py`, `python tools/balance/boss.py`, `python tools/balance/tier2.py`, `python tools/balance/phase2.py`, `python tools/balance/gear.py` y `python tools/balance/upgrades.py`. Resultados y decisiones
de medición en `docs/design/balance-report.md`.
