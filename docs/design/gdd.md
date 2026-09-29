# Documento de diseño (GDD) — MVP

## Pilares
1. **Jugar con amigos en 5 minutos:** entrar por navegador, crear personaje, formar grupo, pelear.
2. **Roles claros:** tanque (Guerrero), daño físico (Pícaro), daño mágico (Mago), sanador (Sacerdote).
3. **Botín que se siente:** rarezas por color, equipo que cambia números visibles.
4. **Pequeño pero pulido:** 1 zona, 1 mazmorra corta con jefe, nivel máximo 10.

## Bucle principal
Explorar → matar monstruos → botín/XP → subir nivel (nuevo hechizo) → mejor equipo → zona más difícil → jefe con amigos.

## Mundo MVP (mapa `meadow`, 128×128 tiles)
| Zona | Niveles | Monstruos |
|---|---|---|
| Pueblo Robledal (seguro) | — | Vendedor, cementerio/respawn |
| Pradera | 1–3 | Slime, Jabalí |
| Bosque Sombrío | 3–6 | Lobo, Goblin Arquero |
| Ruinas | 6–9 | Esqueleto Guerrero |
| Cripta (mazmorra) | 9–10 | Jefe: Rey Liche Menor (grupo de 3–5) |

## Clases
| Clase | Recurso | Rol | Arma | Armadura |
|---|---|---|---|---|
| Guerrero | Ira (0–100, sube al golpear/recibir, baja fuera de combate) | Tanque / melee | Espada, Hacha, Maza | Placas, Malla, Cuero, Tela, Escudo |
| Pícaro | Energía (0–100, +10/s) | Daño melee burst | Daga, Espada | Cuero, Tela |
| Mago | Maná | Daño a distancia, control | Bastón, Varita | Tela |
| Sacerdote | Maná | Curación, escudos | Maza, Bastón | Tela, Malla |

Cada clase tiene 5 hechizos en el MVP (desbloqueo niveles 1, 1, 3, 5, 8). Ver `content/spells.json`.

## Progresión
- Nivel máx. 10. XP para pasar de `L` a `L+1`: `round(100 · L^1.6)` → 100, 303, 580, 919, 1313, 1758, 2250, 2786, 3363.
- XP por monstruo: `base · (1 + 0.1·(nivelMonstruo − nivelJugador))`, mínimo 0 si la diferencia ≤ −5 (gris).
- En grupo: XP total ×1.2 repartida en partes iguales entre miembros vivos a ≤ 40 tiles.
- Al subir de nivel: stats base por clase (`statsPerLevel`), vida y recurso llenos.

## Economía
- Monedas en cobre. Monstruos sueltan cobre (`gold` en tabla de botín).
- Vendedor compra a `sellPrice` y vende a `sellPrice × 4`.
- Consumibles de vendedor: Poción menor de vida, Poción menor de maná, Pan.

## Muerte
- Al morir: pantalla "Has muerto" → botón *Reaparecer* → cementerio más cercano con 50 % de vida/recurso.
- Sin pérdida de items ni XP en el MVP.

## Social
- Chat: `say` (radio 20 tiles), `global`, `party`, susurro `/w Nombre`.
- Grupo hasta 5, botín libre (quien lo toma primero), XP compartida.

## Fuera del MVP (backlog "después")
Oficios (minería, herrería), gremios, comercio entre jugadores, subasta, monturas, PvP, talentos, misiones, sonido espacial.
