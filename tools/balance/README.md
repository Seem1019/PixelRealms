# Modelo de balance

Modelo en Python del `content-designer` para medir el pentagrama (ADR-020) y el farmeo en solitario. Valores esperados,
sin azar ni movimiento. Lee `content/` directamente; solo usa la biblioteca estándar.

| Script | Qué hace |
|---|---|
| `model.py` | Personajes (stats, equipo de referencia), simulación de 30 s y medidas de control, movilidad y aguante. |
| `pentagram.py` | Aportes de cada hechizo de la Fase 1 frente a su objetivo de `docs/design/class-kits.md`. |
| `check.py` | Pentagrama de la Fase 1, XP por hora en solitario contra el Kóbold minero y comparación con `rules.balanceTargets.pentagram.references`. |
| `boss.py` | Duración del combate contra el Capataz Grask con 4 grupos de referencia (`python boss.py 1400` prueba otra vida). |

Uso: `python tools/balance/check.py` y `python tools/balance/boss.py`. Resultados y decisiones de medición en
`docs/design/balance-report.md`.
