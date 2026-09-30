# Notas de balance (2026-09-29) — valores iniciales de `rules.json`

Cálculos hechos con las fórmulas de `combat.md` sobre los valores actuales de `content/`. Son valores **esperados**
(sin varianza), para fijar el punto de partida; HU-084 los contrasta con partidas reales.

## 1. Multiplicadores de conversión por clase (`rules.classScaling`)
| Clase | ap.str | ap.agi | ap.int | sp.str | sp.agi | sp.int | hpPerSta | armorMult | haste |
|---|---|---|---|---|---|---|---|---|---|
| Guerrero | 2.0 | 0.5 | 0.3 | 0.5 | 0.2 | 0.8 | 12 | 1.00 | 1.00 |
| Pícaro | 1.0 | 2.0 | 0.3 | 0.2 | 0.5 | 0.8 | 10 | 0.85 | 1.15 |
| Mago | 0.6 | 0.4 | 1.4 | 0 | 0 | 1.5 | 10 | 0.75 | 0.90 |
| Sacerdote | 0.7 | 0.4 | 1.4 | 0 | 0 | 1.3 | 10 | 0.85 | 0.90 |

Por qué así: la afinidad sola (×0.85/×0.7) no basta, porque las **bases de stats** ya separan a las clases (un Sacerdote
tiene 4 de `str` y no sube; un Pícaro 13 de `agi` +2 por nivel). Con solo afinidad, Sacerdote + espada rendía el 31 %
del Pícaro. El `ap.int = 1.4` de los casters es lo que hace que su ataque básico con un arma física funcione.

## 2. Afinidad (`rules.affinity.byClass`), multiplicador alta 1.0 · media 0.85 · baja 0.7
| | Espada | Hacha | Maza | Daga | Bastón | Varita | Tela | Cuero | Malla | Placas | Escudo | Joyería |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Guerrero | alta | alta | alta | media | baja | baja | baja | media | alta | alta | alta | alta |
| Pícaro | alta | media | media | alta | baja | baja | media | alta | media | baja | baja | alta |
| Mago | baja | baja | baja | media | alta | alta | alta | media | baja | baja | baja | alta |
| Sacerdote | media | baja | alta | media | alta | alta | alta | media | alta | baja | baja | alta |

## 3. Ejemplo numérico: nivel 5 contra Goblin arquero (nv 5, 140 hp, armadura 20), solo ataque básico
| Combinación | Vida | Mitig. | AP | SP | DPS | Tiempo en matarlo | El goblin me mata en | Vida perdida por kill |
|---|---|---|---|---|---|---|---|---|
| Pícaro + daga (alta) | 190 | 16 % | 66 | 18 | **8.0** | 18 s | 82 s | 21 % |
| Pícaro + espada (alta) | 200 | 15 % | 64 | 17 | 7.9 | 18 s | 85 s | 21 % |
| Guerrero + espada + escudo + malla (alta) | 346 | 26 % | 53 | 16 | 6.6 | 21 s | **163 s** | 13 % |
| Mago + bastón (alta) | 160 | 6 % | 43 | 45 | 5.1 | 28 s | 59 s | 46 % |
| Sacerdote + varita (alta) | 175 | 4 % | 38 | 34 | 5.0 | 28 s | 63 s | 45 % |
| Sacerdote + maza (alta) | 175 | 4 % | 37 | 33 | 4.5 | 31 s | 63 s | 50 % |
| **Sacerdote + espada (media)** | 184 | 4 % | 36 | 29 | **4.7 (60 % del Pícaro)** | 30 s | 66 s | 45 % |
| Sacerdote + espada + placas/malla/escudo | 206 | 18 % | 34 | 26 | 4.6 | 30 s | 87 s (53 % del Guerrero) | 35 % |
| Pícaro + espada + placas/malla/escudo | 214 | 23 % | 60 | 16 | 7.6 | 18 s | 100 s (61 %) | 18 % |
| **Mago + espada + placas/malla/escudo (baja)** | 190 | 17 % | 38 | 33 | **4.4 (56 % del Pícaro)** | 32 s | 80 s (49 % del Guerrero) | 40 % |

Lecturas:
- **Piso de viabilidad cumplido:** ninguna combinación pierde más del 50 % de vida por kill solo con básicos, así que
  cualquiera puede farmear solo. Fuera de rol: 56–61 % del especialista en daño; 49–61 % en aguante.
- El Mago con espada y placas (baja en ambas) queda en 56 %, justo bajo la orientación del 60 %: es el caso de doble
  penalización y se acepta. Si se quiere subir, tocar `affinity.multipliers.baja` a 0.75.
- El aguante de los casters con armadura pesada queda en ~50 % del Guerrero: la diferencia la hacen `baseHp` y `sta`
  base (identidad de tanque), no la afinidad. Subirlo más haría que el Guerrero dejara de sentirse distinto.
- La varita **es** la opción correcta del Sacerdote en solitario: rinde como la maza pero a distancia (7 casillas, ADR-019), y no gasta maná.

## 4. Jefe del Tier 1: Capataz Grask (nv 6, 1 400 hp, armadura 90, 18–26 de daño cada 2.4 s)
Recalculado el 2026-09-30 con el modelo de `tools/balance/` y los números de la Fase 1 (maná por golpe incluido). Con
2 000 de vida los grupos tardaban 117–137 s; con 1 400:

| Grupo | DPS del grupo | Duración | Notas |
|---|---|---|---|
| 3 × nivel 4 (Guerrero, Mago, Sacerdote curando) | ~16,5 | **~85 s** | el tanque recibe ~7,6/s; el Sacerdote cura ~9,8/s y pasa el 90 % del tiempo curando |
| 3 × nivel 4 (Guerrero, Pícaro, Mago, sin sanador) | ~26 | ~54 s | el tanque (288 hp) aguanta ~33 s sin curas: hacen falta pociones o rotar quién aguanta |
| 2 × nivel 6 (Pícaro, Mago) | ~25 | ~57 s | justo: el Pícaro (200 hp) aguanta ~24 s por pasada, hay que alternar aggro |
| 2 × nivel 6 (Guerrero, Sacerdote) | ~14 | ~101 s | seguro pero lento: el Sacerdote cura el 69 % del tiempo |

Nivel 7 + nivel 5 no se puede dar en la Fase 1 (tope 6); se valida al abrir la Fase 2. Lo que hay que verificar jugando
(HU-084): el Golpe de pico (área marcada, esquivable) castiga a los melee que se apilan y el Latigazo obliga al sanador a
curar a alguien distinto del tanque.

## 5. Curva de XP por tiempo (ADR-017; nivel máximo 15)
`xpParaSubir(L) = round(minutos(L) · (60 / killCycleSecTarget) · (5·L + 1))`, con `killCycleSecTarget = 36` ⇒ kills por nivel = minutos × 5/3.

| Nivel | Minutos | XP para subir | Kills | Nivel | Minutos | XP | Kills |
|---|---|---|---|---|---|---|---|
| 1→2 | 10 | 100 | 17 | 8→9 | 115 | 7 858 | 192 |
| 2→3 | 20 | 367 | 33 | 9→10 | 130 | 9 967 | 217 |
| 3→4 | 35 | 933 | 58 | 10→11 | 150 | 12 750 | 250 |
| 4→5 | 50 | 1 750 | 83 | 11→12 | 165 | 15 400 | 275 |
| 5→6 | 65 | 2 817 | 108 | 12→13 | 180 | 18 300 | 300 |
| 6→7 | 85 | 4 392 | 142 | 13→14 | 195 | 21 450 | 325 |
| 7→8 | 100 | 6 000 | 167 | 14→15 | 210 | 24 850 | 350 |

Total: 126 934 XP, ~2 517 kills de tu nivel, ~25,2 h (menos si matas naranjas: +10 % por nivel de diferencia hasta +40 %).
Fase 1 (1→6): 5 967 XP, ~300 kills, 3 h · Fase 2 (6→10): 7,2 h · Fase 3 (10→15): 15 h.
El ciclo de 36 s por kill sale del modelo de balance de la Fase 1 (34–38 s según la clase, `balance-report.md`); se
confirma jugando en HU-084 y, si no se cumple, se cambia `killCycleSecTarget` y la tabla se recalcula.

Reparto en grupo (niveles 10 / 8 / 5, monstruo normal nv 9 = 46 XP): referencia 10 → mod 0.9; bono(3) 1.55 → pool 64.2;
pesos 1.00 / 1.00 / 0.42 → **26.5 / 26.5 / 11.2 XP**. Sin piso: la brecha penaliza a propósito.

## 6. Posición del Sacerdote respecto al triángulo
> **Pendiente:** esta tabla usa los números anteriores al balance de la Fase 1 (Sanar hoy cura ~12 por casteo, ~8 por
> segundo, y Castigo ya no ralentiza). El triángulo necesita simular movimiento y se mide en HU-084.

| Contra | Resultado esperado | Por qué |
|---|---|---|
| Guerrero | **gana** | el Guerrero no interrumpe (sin Carga en CD, 15 s) y su DPS (≈14 a nivel 6) es menor que la cura del Sacerdote (Sanar ≈ 22 HPS + Renovar); el Sacerdote lo desgasta con Castigo y varita |
| Pícaro | **pierde** | Gubia (3 s de aturdimiento) interrumpe Sanar; Golpe siniestro + veneno superan la cura cuando el maná baja |
| Mago | parejo | burst del Mago vs. Escudo + Sanar; se decide por maná y por quién acierta primero el control |

Opción si se quiere meterlo en la figura: convertir el triángulo en un ciclo de 4 (Mago > Guerrero > Pícaro > Sacerdote > Mago)
dándole al Sacerdote un `silence` corto (Palabra de poder: Silencio, 3 s) que solo importa contra casters. Hoy no se hace: los
duelos son siempre 1 vs 1 (pilar 7), así que en PvP el Sacerdote nunca tiene un aliado al que curar. Su identidad en duelo es
el desgaste: gana al Guerrero, empata con el Mago y pierde con el Pícaro.
