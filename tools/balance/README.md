# Modelo de balance

Modelo en Python del `content-designer` para medir el pentagrama (ADR-020) y el farmeo en solitario. Valores esperados,
sin azar ni movimiento. Lee `content/` directamente; solo usa la biblioteca estándar.

| Script | Qué hace |
|---|---|
| `model.py` | Personajes (stats, equipo de referencia), simulación de 30 s y medidas de control, movilidad y aguante. |
| `pentagram.py` | Aportes de cada hechizo de la Fase 1 frente a su objetivo de `docs/design/class-kits.md`. |
| `check.py` | Pentagrama de la Fase 1, XP por hora en solitario contra el Kóbold minero y comparación con `rules.balanceTargets.pentagram.references`. |
| `boss.py` | Duración del combate contra el Capataz Grask con 4 grupos de referencia (`python boss.py 1400` prueba otra vida). |
| `tier2.py` | Monstruos del Tier 2 (HU-109): solitario por clase (rotación y solo básicos), ciclo y XP por hora contra cada monstruo de los niveles 6 a 10, y élites solo y en grupo (con pociones, curas del Sacerdote y esquivando áreas). `--tier1` repite el solitario sin equipo nuevo; `--quick` se salta los élites. |
| `phase2.py` | Hechizos de nivel 7 y 9 (HU-106): pentagrama al nivel 10 con el segundo rango y la regla 40/75 en las 15 combinaciones de cada clase (y del nivel 7 al 10), cuánto se nota el rango, qué toca cada cono o línea, y XP por hora en solitario con los equipos nuevos. `--rank 0.2` repite todo con otro `spellRankBonusPct` en memoria. |
| `gear.py` | Equipo, botín y vendedor de los niveles 6 a 10 (HU-110): comparador del tooltip, `tier2.py` con el equipo real de `items.json` (recién llegado, esperado y completo) en lugar de `gear_factor`, horas del 6 al 10, economía (oro por hora frente a pociones y comida) y élites. `--quick` se salta los élites; `--elite-dmg 1.3` y `--regen-delay 5` prueban las propuestas en memoria. |

Uso: `python tools/balance/check.py`, `python tools/balance/boss.py`, `python tools/balance/tier2.py`, `python tools/balance/phase2.py` y `python tools/balance/gear.py`. Resultados y decisiones
de medición en `docs/design/balance-report.md`.
