---
name: world-maps
description: Crear y editar mapas en Tiled (.tmj) para PixelRealms — capas obligatorias, colisión, línea de visión, spawns de monstruos, NPCs, cementerios y zonas — y cómo los leen el servidor (TiledMapLoader) y el cliente (YATI). Úsala al tocar maps/ o la carga de mapas.
---

# Mapas del mundo (Tiled)

Fuente de verdad: `maps/<mapId>.tmj` (Tiled JSON, **no** .tmx). Tilesets externos `maps/tilesets/*.tsj` con imágenes en
`client/assets/tiles/`. Tile = **16×16 px**. Orientación ortogonal. Orden de render right-down.

## Capas (nombres exactos, en este orden)
| Capa | Tipo | Uso |
|---|---|---|
| `ground` | tiles | suelo; nunca colisiona |
| `detail` | tiles | decoración a nivel de suelo |
| `walls` | tiles | paredes/árboles/rocas; los tiles con propiedad `solid=true` colisionan |
| `above` | tiles | copas de árboles, techos: se dibujan por encima de las entidades |
| `collision` | tiles (tileset `collision.tsj`, invisible en juego) | **opcional**: fuerza sólido donde el arte no basta |
| `spawns` | objetos | puntos/rectángulos de spawn de monstruos |
| `npcs` | objetos | NPCs (vendedores) |
| `graveyards` | objetos (puntos) | respawn de jugadores |
| `zones` | objetos (rectángulos) | nombre de zona, `safe=true` (sin combate), rango de niveles |
| `portals` | objetos | cambio de mapa (post-MVP) |

## Propiedades personalizadas
- Tile (en el tileset): `solid: bool`, `blocksSight: bool` (paredes sí, arbustos bajos no).
- Objeto en `spawns`: `monsterId: string` (existe en `content/monsters.json`), `count: int` (1–10), `wanderRadius: float` (tiles).
  Punto = 1 spawn fijo; rectángulo = `count` posiciones aleatorias libres dentro (determinista por seed del mapa).
- Objeto en `npcs`: `vendorId: string` (en `content/vendors.json`), `name: string`.
- Objeto en `zones`: `name: string`, `safe: bool`, `minLevel`, `maxLevel`.
- Mapa: `mapId: string`, `displayName: string`, `defaultGraveyard: string`.

## Servidor: `TiledMapLoader` (PixelRealms.Game/Map)
1. Lee `.tmj` con System.Text.Json (solo los campos necesarios; ignorar el resto).
2. Decodifica capas de tiles (`encoding: csv` o array; **configura Tiled en CSV sin compresión**). Aplica flags de
   flip (bits altos del GID) enmascarándolos: `gid & 0x1FFFFFFF`.
3. Construye `CollisionGrid(width, height)` con `solid` y `blocksSight` desde `walls` + `collision`.
4. Lee objetos → `SpawnDef`, `NpcDef`, `GraveyardDef`, `ZoneDef`. Coordenadas de objetos en píxeles; los puntos de
   Tiled están en la esquina superior-izquierda del objeto si es rectángulo.
5. Valida: `monsterId`/`vendorId` existen, spawns no caen en sólido, al menos un cementerio. Error → no arranca.
6. Test: `TiledMapLoaderTests` con `maps/test_small.tmj` (10×10) versionado para tests.

## Cliente
- Plugin **YATI** importa `.tmj` a una escena con `TileMapLayer` por capa. `above` se coloca con `z_index` mayor que `Entities`.
- Las capas de objetos se ignoran en el cliente (el servidor envía NPCs/monstruos como entidades); `zones` sí se usa para
  mostrar el nombre al entrar ("Bosque Sombrío" con fade).
- `collision` se oculta (`visible = false`).
- El cliente construye su propia `CollisionGrid` desde el mismo `.tmj` para la predicción de movimiento (mismo algoritmo).

## Buenas prácticas de diseño
- Bordes del mapa siempre sólidos. Caminos de ≥ 3 tiles de ancho. Zonas separadas por cuellos de botella naturales.
- Densidad: 1 spawn cada ~8×8 tiles en zonas de farmeo; aggro radius no debe solaparse entre grupos.
- El pueblo (`safe=true`) debe estar a < 40 tiles de las zonas 1–3.
- Tras editar: `dotnet test --filter Map` y abrir el cliente para revisar capas `above`.
