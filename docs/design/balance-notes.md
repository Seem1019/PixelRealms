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
- La varita **es** la opción correcta del Sacerdote en solitario: rinde como la maza pero a 6 tiles, y no gasta maná.

## 4. Jefe del Tier 1: Capataz Grask (nv 6, 2 000 hp, armadura 90, 18–26 de daño cada 2.4 s)
| Grupo | DPS del grupo | Duración | Notas |
|---|---|---|---|
| 3 × nivel 4 (Guerrero, Mago, Sacerdote curando) | ~38 → cae al quedarse el Mago sin maná a los ~30 s | **~60–75 s** | el jefe pega ~6 DPS al tanque; el Sacerdote cura 22 HPS |
| 3 × nivel 4 (Guerrero, Pícaro, Mago, sin sanador) | ~53 | ~45–55 s | el tanque aguanta ~50 s sin curas: hace falta poción o Pan |
| 2 × nivel 6 (Pícaro, Mago) | ~47 | ~50 s | justo: el Pícaro (200 hp) aguanta ~25 s por pasada, hay que alternar aggro |
| Nivel 7 + nivel 5 (Guerrero + Mago) | ~44 | ~55 s | cómodo con Bloqueo |

Los tres objetivos de `rules.boss` se cumplen sobre el papel. Lo que hay que verificar jugando: el Golpe de pico (AoE)
castiga a los melee que se apilan y el Latigazo obliga al sanador a curar a alguien distinto del tanque.

## 5. Curva de XP (K = 200, nivel máximo 15)
| Nivel | XP para subir | Kills de un mob normal de tu nivel | Nivel | XP | Kills |
|---|---|---|---|---|---|
| 1→2 | 200 | 33 | 8→9 | 5 572 | 136 |
| 2→3 | 606 | 55 | 9→10 | 6 727 | 146 |
| 3→4 | 1 160 | 72 | 10→11 | 7 962 | 156 |
| 4→5 | 1 838 | 88 | 11→12 | 9 274 | 166 |
| 5→6 | 2 627 | 101 | 12→13 | 10 659 | 175 |
| 6→7 | 3 516 | 113 | 13→14 | 12 115 | 184 |
| 7→8 | 4 500 | 125 | 14→15 | 13 641 | 192 |

Total: 80 397 XP, ~1 740 kills de tu nivel (menos si matas naranjas: +10 % por nivel de diferencia hasta +40 %).
Tier 1 (1→6): 6 431 XP, ~350 kills.

Reparto en grupo (niveles 10 / 8 / 5, monstruo normal nv 9 = 46 XP): referencia 10 → mod 0.9; bono(3) 1.55 → pool 64.2;
pesos 1.00 / 1.00 / 0.42 → **26.5 / 26.5 / 11.2 XP**. Sin piso: la brecha penaliza a propósito.

## 6. Posición del Sacerdote respecto al triángulo
| Contra | Resultado esperado | Por qué |
|---|---|---|
| Guerrero | **gana** | el Guerrero no interrumpe (sin Carga en CD, 15 s) y su DPS (≈14 a nivel 6) es menor que la cura del Sacerdote (Sanar ≈ 22 HPS + Renovar); el Sacerdote lo desgasta con Castigo y varita |
| Pícaro | **pierde** | Gubia (3 s de aturdimiento) interrumpe Sanar; Golpe siniestro + veneno superan la cura cuando el maná baja |
| Mago | parejo | burst del Mago vs. Escudo + Sanar; se decide por maná y por quién acierta primero el control |

Opción si se quiere meterlo en la figura: convertir el triángulo en un ciclo de 4 (Mago > Guerrero > Pícaro > Sacerdote > Mago)
dándole al Sacerdote un `silence` corto (Palabra de poder: Silencio, 3 s) que solo importa contra casters. Hoy no se hace: el
Sacerdote es soporte y su ventaja en PvP debe venir de curar a un aliado, no de ganar 1 vs 1.
