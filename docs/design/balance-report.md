# Informe de balance de la Fase 1 (2026-09-30)

Primera pasada de números de los 16 hechizos de la Fase 1 (niveles 1–5), hecha por el `content-designer` con el método
de [class-kits.md](class-kits.md) (ADR-020). Son valores **esperados** de un modelo en Python (`tools/balance/`), sin azar
ni movimiento; HU-084 los contrasta con partidas reales.

Para repetirlo: `python tools/balance/check.py` (pentagrama y solitario) y `python tools/balance/boss.py` (jefe). Ambos
leen `content/` directamente.

## Qué simula el modelo
- 30 s de combate a nivel 6 con el equipo verde de su nivel y el arma de referencia de cada clase.
- Recursos (energía, ira, maná con el maná por golpe), cooldown global de 1 s, bloqueo de animación de 250 ms, pausa del
  básico al castear.
- Contra un objetivo de nivel 6 con 35 de armadura (entre el Kóbold y el Gólem).

## Decisiones de medición (aprobadas)
1. **Un hechizo de área cuenta entero en la punta de área**, también el daño a su objetivo principal. Si no, cada área
   sumaba de rebote ~60 % de su valor en mono-objetivo.
2. **Valores de referencia fijos** (`rules.balanceTargets.pentagram.references`), 100 puntos = :

   | Punta | Referencia a nivel 6 |
   |---|---|
   | Mono-objetivo | 26,6 de daño por segundo (el básico del Pícaro vale 30) |
   | Área | 63,8 (4 objetivos × 0,6 × mono) |
   | Control | 40 s equivalentes de control por minuto |
   | Movilidad | 120 casillas extra por minuto |
   | Armadura | 88,2 s de aguante (el del Guerrero vale 70) |

3. **El atacante de referencia pega físico**, porque todos los monstruos del Tier 1 pegan físico.
4. En el Sacerdote, las curas y los escudos a aliados cuentan en mono-objetivo (o en área si son de área), no en armadura.

## Números de la Fase 1
Con el pentagrama, los hechizos aportan tanto como el básico del arma. Por eso el daño y la curación bajaron a ~⅓ de los
valores provisionales y el maná, en proporción.

| Clase | Hechizo | Números |
|---|---|---|
| Pícaro | Golpe siniestro | 3 + 0,2 AP + 0,15 arma, cooldown 3 s |
| | Gubia | 10 de daño, aturde 2 s, cooldown 25 s |
| | Paso sombrío | +60 % de daño 3 s, ralentiza 0,5 s, cooldown 10 s |
| | Carrera | +50 % de velocidad 6 s, cooldown 40 s |
| Mago | Bola de fuego | 7 + 0,3 SP, casteo 2 s, 8 de maná |
| | Descarga de escarcha | 5 + 0,3 SP, ralentiza, cooldown 12 s, 8 de maná |
| | Nova de escarcha | 4 + 0,15 SP, raíz 3 s, cooldown 18 s, 15 de maná |
| | Estallido de llamas | 20 + 0,55 SP, casteo 1,5 s, 25 de maná |
| Guerrero | Golpe heroico | 2 + 0,1 AP + 0,1 arma, cooldown 3 s |
| | Provocar | 2 s, cooldown 10 s |
| | Carga | aturde 1,5 s, cooldown 16 s |
| | Torbellino | 8 + 0,4 AP + 0,6 arma |
| Sacerdote | Sanar | 5 + 0,2 SP (~12 por casteo), 6 de maná |
| | Castigo | 7 + 0,3 SP, sin ralentización, 6 de maná |
| | Palabra de poder: Escudo | +20 % de velocidad 4 s, cooldown 15 s, 20 de maná |
| | Pulso sagrado | cura 20 + 0,65 SP, ralentiza 1,5 s, 20 de maná |

- **Castigo pierde la ralentización:** como se lanza sin parar, dejaba al objetivo ralentizado todo el tiempo y valía 22
  puntos de control. Su control pasa a Pulso sagrado.
- **Sanar** quedó en 5 + 0,2 SP y 6 de maná. Con 7 + 0,28 y 8 de maná (lo que se propuso primero) ya no se quedaba sin
  maná en 30 s y valía 44 puntos, por encima del máximo de 40 por habilidad.
- **Fases 2 y 3:** los 16 hechizos se escalaron a la misma escala (daño ×0,35, cura ×0,3, maná ×0,4 con mínimo 5) y siguen
  con `"provisional": true` hasta su pasada al nivel 15.

## Resultado: pentagrama de la Fase 1 (nivel 6)
Entre paréntesis, el objetivo de la Fase 1.

| Clase | Mono | Área | Control | Movilidad | Armadura |
|---|---|---|---|---|---|
| Pícaro | 57 (60) | 0 (0) | 20 (20) | 50 (50) | 32 (30) |
| Mago | 41 (45) | 34 (35) | 45 (45) | 0 (0) | 23 (25) |
| Guerrero | 35 (35) | 21 (20) | 29 (30) | 16 (15) | 70 (70) |
| Sacerdote | 51 (50) | 29 (30) | 10 (15) | 11 (10) | 29 (30) |

- **Regla 40/75:** se cumple. La habilidad que más suma es Pulso sagrado (39) y la combinación más cargada, la del
  Guerrero (170 de 187).
- **Armadura del Pícaro y del Mago:** no bajaba a su objetivo original (25 y 15) sin dejarlos por debajo del piso de
  supervivencia en solitario; el pentagrama se ajustó a Pícaro armadura 30 / movilidad 85 y Mago armadura 25 / control 70.

## Solitario: nivel 5 contra Kóbold minero
160 de vida, 30 de armadura, 6–10 de daño cada 2,2 s; con 10 s de caminata entre monstruos.

| Clase | Mata en | Vida perdida | Descanso | Ciclo por monstruo | XP por hora | Solo con básicos |
|---|---|---|---|---|---|---|
| Pícaro | 12,5 s | 17 % | 10,8 s | 33,3 s | referencia | 21 s, 29 % de vida |
| Guerrero | 15 s | 9 % | 9,8 s | 34,8 s | −4 % | 26 s, 16 % |
| Sacerdote | 16,5 s | 24 % | 9,5 s | 36 s | −8 % | 34 s, 50 % |
| Mago | 11,5 s | 22 % | 15,6 s | 37,1 s | −10 % | 34 s, **65 %** |

- **XP por hora:** todas las clases dentro del ±15 % (`soloXpPerHourSpreadPct`).
- **Ciclo real ~36 s** (33–37 s), no 30: `killCycleSecTarget` pasó a 36 para que la curva siga en ~25 h.
- **Mago solo con básicos:** bajar el daño del Kóbold de 7–11 a 6–10 lo dejó en 65 % de vida perdida (antes 73 %). Sigue
  por encima del 50 %; el modelo no cuenta que el Mago se aleje mientras castea. Vigilar en HU-084; la siguiente palanca
  es +10 de vida base del Mago.

## Jefe: Capataz Grask
Con 2 000 de vida los grupos tardaban 117–137 s; la vida bajó a **1 400**. Resultados en
[balance-notes.md](balance-notes.md) §4: 85 s con 3 de nivel 4 y sanador, 54 s sin sanador (con pociones), 57 s con
Pícaro + Mago de nivel 6 y 101 s con Guerrero + Sacerdote de nivel 6.

## Sin medir
- **Triángulo de duelos:** necesita simular movimiento; queda para HU-084.
- **Parejas de habilidades que se potencian** (ralentizar + área, aturdir + golpe fuerte): el modelo suma aportes.
- **Pentagrama al nivel 15** (Fases 2 y 3).
