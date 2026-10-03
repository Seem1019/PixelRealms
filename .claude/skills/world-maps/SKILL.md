---
name: world-maps
description: Crear y editar mapas en Tiled (.tmj) para PixelRealms — capas obligatorias, colisión, línea de visión, spawns de monstruos, NPCs, cementerios y zonas — y cómo los leen el servidor (TiledMapLoader) y el cliente (TmjMap). Úsala al tocar maps/ o la carga de mapas.
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
| `portals` | objetos | cambio de mapa (MVP: entrada a la Mina; ADR-007) |

## Propiedades personalizadas
- Tile (en el tileset): `solid: bool`, `blocksSight: bool` (paredes sí, arbustos bajos no).
- Objeto en `spawns`: `monsterId: string` (existe en `content/monsters.json`), `count: int` (1–10), `wanderRadius: float` (tiles).
  Punto = 1 spawn fijo; rectángulo = `count` posiciones aleatorias libres dentro (determinista por seed del mapa).
- Objeto en `npcs`: `vendorId: string` (en `content/vendors.json`), `name: string`.
- Objeto en `zones`: `name: string`, `safe: bool`, `minLevel`, `maxLevel`, `landmark: string` (punto de referencia visible).
- Objeto en `portals`: `portalId`, `targetMapId`, `targetX`, `targetY` (tiles), `minLevel?`. Rectángulo = se activa al pisarlo.
- Objeto en `graveyards`: uno **por zona** (punto seguro: fogata/santuario); `defaultGraveyard` del mapa es el de la aldea.
- Mapa: `mapId: string`, `displayName: string`, `defaultGraveyard: string`.

## Servidor: `TiledMapLoader` (PixelRealms.Game/Map)
1. Lee `.tmj` con System.Text.Json (solo los campos necesarios; ignorar el resto).
2. Decodifica capas de tiles (`encoding: csv` o array; **configura Tiled en CSV sin compresión**). Aplica flags de
   flip (bits altos del GID) enmascarándolos: `gid & 0x1FFFFFFF`.
3. Construye `CollisionGrid(width, height)` con `solid` y `blocksSight` desde `walls` + `collision`.
4. Lee objetos → `SpawnDef`, `NpcDef`, `GraveyardDef`, `ZoneDef`. Coordenadas de objetos en píxeles; los puntos de
   Tiled están en la esquina superior-izquierda del objeto si es rectángulo.
5. Valida: `monsterId`/`vendorId` existen, spawns no caen en sólido, al menos un cementerio, `targetMapId` de portales existe en `maps/`. Error → no arranca.
5b. Un `MapData` por archivo; el `GameLoop` crea una `MapInstance` por `MapData` al arrancar (ADR-007). `MapInstance.Id` es un int propio, distinto de `mapId`.
6. Test: `TiledMapLoaderTests` con `maps/test_small.tmj` (10×10) versionado para tests.

## Cliente
- Sin plugin ni `TileMapLayer`: `scripts/world/tmj_map.gd` (`TmjMap`) lee el `.tmj` que `tools/sync_content.gd` copia a
  `res://maps/`, y `TerrainBaker` hornea `ground`/`detail`/`walls` y `above` con autotile dual-grid (ADR-026);
  `TerrainRenderer` dibuja `above` con `z_index` mayor que `Entities`.
- De las capas de objetos el cliente solo lee `zones` (nombre al entrar, "Bosque Sombrío" con fade), `graveyards` y
  `portals` (para dibujarlos); NPCs y monstruos llegan del servidor como entidades.
- `collision` no se dibuja: solo alimenta la colisión.
- El cliente construye su propia `CollisionGrid` desde el mismo `.tmj` para la predicción de movimiento (mismo algoritmo).

## Buenas prácticas de diseño
- Bordes del mapa siempre sólidos. Caminos de ≥ 3 tiles de ancho. Zonas separadas por cuellos de botella naturales.
- Densidad: 1 spawn cada ~8×8 tiles en zonas de farmeo; aggro radius no debe solaparse entre grupos.
- Zonas abiertas: 60–90 s de caminata para cruzarlas (~100×100 tiles útiles a 4 tiles/s), un punto de referencia visible,
  sendero principal obvio, 2–3 campamentos con subniveles (bajos en la entrada, altos en la salida), ramas laterales con recompensa.
- Cuevas (mapas aparte): 3–5 salas, 5–10 min; sala del jefe en rama lateral; sala élite antes de la salida al tier siguiente.
  Todas comparten el tileset `interior` cambiando paleta (mina marrón, cripta verde, fortaleza gris).
- El pueblo (`safe=true`) debe estar a < 40 tiles de las zonas 1–3.
- Tras editar: `dotnet test --filter Map`, copiar los mapas al cliente (`godot --path client --headless -s ../tools/sync_content.gd`)
  y abrirlo para revisar capas `above`.
