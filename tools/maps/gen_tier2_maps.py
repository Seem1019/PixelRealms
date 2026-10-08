#!/usr/bin/env python3
"""Genera maps/forest.tmj (HU-111) y maps/crypt.tmj (HU-115) de forma determinista con el tileset placeholder y los mismos GIDs
que el Tier 1. El Bosque del Tier 2 tiene el Linde del Bosque (6–8) y el Pantano (8–10) en un solo mapa (el atlas del Bosque,
`biome: forest`, le da el aspecto); la Cripta de Raíces es la cueva de transición al Tier 3, con las piezas de interior de la
Mina recoloreadas (`palette: crypt`). No toca meadow.tmj ni mine.tmj: regenerar el Tier 1 les cambia los fines de línea.

Uso: python tools/maps/gen_tier2_maps.py            (escribe maps/forest.tmj y maps/crypt.tmj)
     python tools/maps/gen_tier2_maps.py --check    (solo valida los dos y sale con código ≠ 0 si falla)

Cómo lo dibuja el cliente (TerrainBaker con terrain_forest.png): la roca unida al borde del mapa es bosque cerrado (muros
exteriores, la cresta entre zonas y las guaridas); la roca suelta y los bloques de muro fuera de una zona segura son peñascos; el
muro de una casilla de grosor es valla y, dentro de una zona segura, los bloques de muro son cabañas; `above` sin muro debajo
son copas de árbol; el agua es charca de pantano y el suelo interior (8), losas o el fondo de una cueva. En la Cripta (más de la
mitad del suelo es interior: TerrainBaker la dibuja como cueva) la roca es la pared de la cueva, el arbusto es maraña de raíces,
el agua es agua estancada, la tierra es mantillo de raíces y el camino, losas; el muro (4) no se usa: en una cueva, en bloque no
se dibuja y en fila es valla.
Tras cambiar algo aquí: `dotnet test --filter Map` y `godot --path client --headless -s ../tools/sync_content.gd`.
"""
from __future__ import annotations

import heapq
import json
import math
import random
import sys

from gen_tier1_maps import BUSH, DIRT, FLOOR, GRASS, PATH, ROCK, ROOT, TS, WALL, WATER, Grid, check_map, obj, obj_layer, prop, tile_layer, tmj


W, H = 214, 104
SPLIT_X = 107                       # frontera Linde | Pantano: centro de la cresta de bosque que las separa
PASS_Y = 52                         # fila central del cuello de botella (paso de 4 casillas en la cresta)
# Puntos seguros (fogatas) en la misma fila y a la misma distancia de la frontera: la mediatriz entre los dos es x = SPLIT_X,
# así que desde cualquier casilla de una zona el cementerio más cercano (MapData.NearestGraveyard) es el de esa zona (CA3).
SAFE_ROW = 52
GY_EDGE = (SPLIT_X - 52, SAFE_ROW)  # (55, 52): campamento de Brena, en el Linde
GY_SWAMP = (SPLIT_X + 52, SAFE_ROW)  # (159, 52): refugio del Pantano
EDGE_CAMP = (46, 44, 19, 17)        # zona segura del campamento de Brena (x, y, ancho, alto)
SWAMP_CAMP = (150, 48, 19, 15)      # zona segura del refugio del Pantano

# --- Sitios de otras HU (dibujados y reservados aquí; su mecánica no) --------------------------------------------------------
# HU-112: la Sala 3 de la Mina (mine.tmj, x 72..85 · y 16..35) tiene la salida hacia aquí en la hornacina (x 86..87 · y 25..27).
# El portal de vuelta del Linde deja en (85, 25): casilla libre de la Sala 3 junto a la hornacina y a más de 6 casillas del gólem
# (79, 28).
MINE_ROOM3_TARGET = (85, 25)
MINE_RETURN_PORTAL = (4, 28, 2, 3)  # en el fondo de la boca de la Mina, al oeste del Linde (x, y, ancho, alto)
MINE_ARRIVAL = (12, 29)             # donde debe dejar el portal de la Sala 3 (HU-112): en la boca, fuera de MINE_RETURN_PORTAL
# HU-113: lado alto del puente roto, en el borde oeste del Linde (el que da a las Colinas), sobre el río.
BRIDGE_DECK = (8, 88, 6, 3)         # tablero (x 8..13 · y 88..90); su extremo oeste da al río: ahí irá el paso a las Colinas
BRIDGE_CRANK = (15, 86)             # sitio de la manivela, junto al tablero
# HU-115: boca de la Cripta al fondo del Pantano (borde este), con el portal a la entrada de la Cripta.
CRYPT_ENTRANCE = (206, 73, 2, 3)    # rectángulo del portal (x 206..207 · y 73..75)
CRYPT_ARRIVAL = (201, 74)           # donde deja el portal de vuelta de la Cripta

# Sendero principal (3 casillas): boca de la Mina → campamento de Brena → cuello de botella → refugio del Pantano → Cripta.
MAIN_PATH = [
    (12, 29), (30, 29), (30, 42), (44, 42), (44, 56), (66, 56), (66, 66), (86, 66), (86, PASS_Y),
    (128, PASS_Y), (128, 64), (146, 64), (146, 58), (180, 58), (180, 70), (196, 70), (196, 74), (202, 74),
]
ARRIVAL_PATH_END = (44, 56)         # el sendero de llegada (sin monstruos al alcance) va de la boca de la Mina hasta aquí
# Ramal (camino de 2) del sendero al lado alto del puente.
BRIDGE_BRANCH = [(44, 58), (36, 58), (36, 74), (18, 74), (18, 89), (14, 89)]


def _clamp(v: int, lo: int, hi: int) -> int:
    return max(lo, min(hi, v))


def ellipse(cx: int, cy: int, rx: int, ry: int) -> set[tuple[int, int]]:
    """Casillas dentro de la elipse (aritmética entera: el mismo resultado en cualquier máquina)."""
    rx, ry = max(1, rx), max(1, ry)
    return {(x, y) for y in range(cy - ry, cy + ry + 1) for x in range(cx - rx, cx + rx + 1)
            if (x - cx) ** 2 * ry * ry + (y - cy) ** 2 * rx * rx <= rx * rx * ry * ry}


def blob(rng: random.Random, cx: int, cy: int, rx: int, ry: int) -> set[tuple[int, int]]:
    """Mancha irregular: una elipse y dos o tres más pequeñas pegadas a su borde."""
    cells = ellipse(cx, cy, rx, ry)
    for _ in range(rng.randint(2, 3)):
        ox, oy = rng.randint(-rx, rx), rng.randint(-ry, ry)
        cells |= ellipse(cx + ox, cy + oy, max(1, rx * 2 // 3), max(1, ry * 2 // 3))
    return cells


def rect_gap(a: tuple[float, float, float, float], b: tuple[float, float, float, float]) -> float:
    """Distancia euclídea entre dos rectángulos (x, y, ancho, alto); 0 si se tocan o se solapan."""
    dx = max(0.0, max(a[0], b[0]) - min(a[0] + a[2], b[0] + b[2]))
    dy = max(0.0, max(a[1], b[1]) - min(a[1] + a[3], b[1] + b[3]))
    return math.hypot(dx, dy)


def polyline_cells(points: list[tuple[int, int]]) -> list[tuple[int, int]]:
    """Casillas centrales de una polilínea en L (como Grid.carve_path), en orden y sin repetir."""
    out: list[tuple[int, int]] = []
    for (x0, y0), (x1, y1) in zip(points, points[1:]):
        sx = 1 if x1 >= x0 else -1
        for x in range(x0, x1 + sx, sx):
            out.append((x, y0))
        sy = 1 if y1 >= y0 else -1
        for y in range(y0, y1 + sy, sy):
            out.append((x1, y))
    dedup: list[tuple[int, int]] = []
    for c in out:
        if not dedup or dedup[-1] != c:
            dedup.append(c)
    return dedup


# ----------------------------------------------------------------------------------------------------------------------------
# forest (HU-111): Linde del Bosque (6–8) | cresta con paso | Pantano (8–10) · boca de la Mina, puente roto y boca de la Cripta.
# ----------------------------------------------------------------------------------------------------------------------------

# Campamentos (rectángulo x, y, ancho, alto; ~8×8 casillas por monstruo) a ambos lados del sendero, cada uno con su ramal de
# tierra desde un punto del sendero (y los recodos que hagan falta) hasta su centro. Subniveles de oeste a este: los bajos junto
# a la entrada de cada zona y los altos hacia la salida. Entre grupos distintos queda al menos la suma de sus radios de aggro
# (lo comprueba check_forest).
CAMPS = [
    # Linde: lobos (6) junto a la boca de la Mina, leñadores (7) alrededor del campamento de Brena, arañas (8) hacia la cresta.
    ("wolves_north", "forest_wolf", 7, 3.0, (8, 6, 22, 16), [(19, 29)]),
    ("wolves_south", "forest_wolf", 7, 3.0, (12, 50, 20, 18), [(30, 42), (22, 42)]),
    ("woodcutters_north", "bandit_woodcutter", 7, 3.0, (41, 6, 20, 18), [(44, 42), (51, 42)]),
    ("woodcutters_south", "bandit_woodcutter", 7, 3.0, (40, 76, 18, 18), [(66, 66), (49, 66)]),
    ("spiders_north", "weaver_spider", 6, 2.0, (88, 8, 14, 22), [(90, PASS_Y), (90, 34)]),
    ("spiders_south", "weaver_spider", 6, 2.0, (70, 78, 16, 16), [(78, 66)]),
    # Pantano: sapos (8) a la entrada, hombres lagarto (9) en el centro, fuegos fatuos (10) hacia la Cripta.
    ("toads_north", "giant_toad", 7, 3.0, (116, 8, 22, 18), [(127, PASS_Y)]),
    ("toads_south", "giant_toad", 7, 3.0, (116, 72, 22, 20), [(128, 64)]),
    ("lizards_north", "lizardman", 7, 3.0, (149, 6, 18, 18), [(146, 58), (146, 15)]),
    ("lizards_south", "lizardman", 7, 3.0, (149, 74, 20, 20), [(159, 58)]),
    ("wisps_north", "will_o_wisp", 6, 2.0, (186, 6, 18, 16), [(180, 58), (197, 58)]),
    ("wisps_south", "will_o_wisp", 6, 2.0, (182, 84, 18, 14), [(191, 70)]),
]
# Puntos de referencia: el Árbol Madre (Linde), una copa enorme en un claro junto al sendero, y la Torre hundida (Pantano), un
# anillo de piedra en una laguna. La torre es también la rama lateral del Pantano: la bruja vive dentro.
LANDMARK_TREE = (76, 34)
LANDMARK_TOWER = (176, 34)
# Ramas laterales con élite (recompensa): la guarida del oso, un claro cerrado por el bosque al sureste del Linde con un solo
# ramal, y la Torre hundida, a la que se llega por una calzada desde el sendero.
BEAR_DEN = (98, 90)
BEAR_BRANCH = [(88, 66), (98, 66), (98, 84)]
TOWER_CAUSEWAY = [(176, 43), (176, 57)]


def build_forest() -> tuple[dict, dict]:
    g = Grid(W, H, GRASS)
    rng = random.Random(111)
    oid = 1
    spawns: list[dict] = []
    npcs: list[dict] = []
    graveyards: list[dict] = []
    zones: list[dict] = []
    portals: list[dict] = []
    reserved: list[tuple[str, tuple[int, int, int, int]]] = []  # sitios de otras HU: transitables y sin monstruos

    def nid() -> int:
        nonlocal oid
        oid += 1
        return oid - 1

    def put(layer: list[int], cells, gid: int, over_protected: bool = True) -> None:
        for x, y in cells:
            if g.inside(x, y) and (over_protected or (x, y) not in g.protected):
                layer[g.i(x, y)] = gid

    def protect(cells) -> None:
        g.protected.update((x, y) for x, y in cells if g.inside(x, y))

    # --- Bosque cerrado en los bordes (roca unida al borde: el cliente la dibuja como bosque), de grosor irregular.
    for side in range(4):
        n = W if side < 2 else H
        t = 3
        for k in range(n):
            t = _clamp(t + rng.choice((-1, 0, 0, 1)), 2, 6)
            if side == 0:
                g.fill_rect(g.walls, k, 0, 1, t, ROCK)
            elif side == 1:
                g.fill_rect(g.walls, k, H - t, 1, t, ROCK)
            elif side == 2:
                g.fill_rect(g.walls, 0, k, t, 1, ROCK)
            else:
                g.fill_rect(g.walls, W - t, k, t, 1, ROCK)
    # Cresta entre zonas (bosque cerrado de borde a borde) con el cuello de botella de 4 casillas en PASS_Y.
    left, right = 4, 4
    for y in range(H):
        left, right = _clamp(left + rng.choice((-1, 0, 1)), 3, 7), _clamp(right + rng.choice((-1, 0, 1)), 3, 7)
        g.fill_rect(g.walls, SPLIT_X - left, y, left + right, 1, ROCK)
    g.clear_rect(SPLIT_X - 10, PASS_Y - 2, 21, 4, ground=GRASS)
    # Bocas del paso: peñascos a los lados, para que se vea que la cresta solo se cruza por ahí.
    for bx, by in ((SPLIT_X - 13, PASS_Y - 5), (SPLIT_X - 13, PASS_Y + 3), (SPLIT_X + 11, PASS_Y - 5), (SPLIT_X + 11, PASS_Y + 3)):
        g.fill_rect(g.walls, bx, by, 3, 2, WALL)
        protect((x, y) for x in range(bx - 1, bx + 4) for y in range(by - 1, by + 3))

    # --- Linde: boca de la Mina (llegada desde la Sala 3, HU-112) en el borde oeste. Peñón de roca con un hueco hacia el este.
    put(g.walls, {(x, y) for x, y in ellipse(8, 29, 9, 8) | ellipse(4, 23, 5, 4) | ellipse(5, 36, 6, 4) if x >= 2}, WALL)
    g.clear_rect(4, 27, 14, 5, ground=DIRT)
    g.fill_rect(g.ground, 4, 27, 6, 5, FLOOR)  # fondo de la cueva
    px, py, pw, ph = MINE_RETURN_PORTAL
    portals.append(obj(nid(), "to_mine", "portal", px, py, pw, ph, [prop("portalId", "forest_to_mine"), prop("targetMapId", "mine"),
                       prop("targetX", MINE_ROOM3_TARGET[0]), prop("targetY", MINE_ROOM3_TARGET[1])]))
    reserved.append(("llegada desde la Mina (HU-112)", (MINE_ARRIVAL[0] - 1, MINE_ARRIVAL[1] - 1, 3, 3)))

    # --- Linde: río en el borde oeste y lado alto del puente roto (HU-113). El tablero acaba en el agua; la baranda es valla.
    bx, by, bw, bh = BRIDGE_DECK
    river = set()
    width = 5
    for y in range(62, H):
        width = _clamp(width + rng.choice((-1, 0, 0, 1)), 5, 7)
        river |= {(x, y) for x in range(2, 2 + (bx - 2 if by - 1 <= y <= by + bh else width))}
    put(g.walls, river, WATER)
    protect(river)
    g.clear_rect(bx, by - 1, bw + 4, bh + 2, ground=GRASS)
    g.fill_rect(g.ground, bx, by, bw, bh, PATH)
    g.fill_rect(g.walls, bx, by - 1, bw, 1, WALL)   # baranda norte
    g.fill_rect(g.walls, bx, by + bh, bw, 1, WALL)  # baranda sur
    g.clear_rect(BRIDGE_CRANK[0] - 1, BRIDGE_CRANK[1] - 1, 3, 3, ground=DIRT)
    reserved.append(("tablero del puente (HU-113)", BRIDGE_DECK))
    reserved.append(("manivela del puente (HU-113)", (BRIDGE_CRANK[0], BRIDGE_CRANK[1], 1, 1)))

    # --- Pantano: boca de la Cripta (HU-115) en el borde este: el hueco con el fondo de losas (su portal se añade al final).
    put(g.walls, {(x, y) for x, y in ellipse(W - 7, 74, 9, 8) | ellipse(W - 4, 67, 5, 4) | ellipse(W - 5, 81, 6, 4) if x < W - 2}, WALL)
    g.clear_rect(W - 17, 72, 15, 5, ground=DIRT)
    g.fill_rect(g.ground, W - 9, 72, 7, 5, FLOOR)
    reserved.append(("boca de la Cripta (HU-115)", CRYPT_ENTRANCE))
    reserved.append(("llegada desde la Cripta (HU-115)", (CRYPT_ARRIVAL[0] - 1, CRYPT_ARRIVAL[1] - 1, 3, 3)))

    # --- Puntos seguros: campamento de Brena (Linde, con su tienda) y refugio del Pantano, cada uno con su fogata (cementerio)
    # y su zona `safe` delante de las grandes en la lista (ZoneAt devuelve la primera que contiene el punto).
    def safe_camp(name: str, gy_id: str, gy: tuple[int, int], rect: tuple[int, int, int, int], cabins: list[tuple[int, int, int, int]]) -> None:
        x0, y0, w_, h_ = rect
        g.clear_rect(x0 - 1, y0 - 1, w_ + 2, h_ + 2, ground=GRASS)
        # Empalizada (valla de una casilla) con huecos al oeste y al este para el sendero y al sur.
        g.fill_rect(g.walls, x0, y0, w_, 1, WALL); g.fill_rect(g.walls, x0, y0 + h_ - 1, w_, 1, WALL)
        g.fill_rect(g.walls, x0, y0, 1, h_, WALL); g.fill_rect(g.walls, x0 + w_ - 1, y0, 1, h_, WALL)
        for gx, gy_, gw, gh in ((x0, y0 + h_ - 7, 1, 5), (x0 + w_ - 1, y0 + h_ - 7, 1, 5), (x0 + w_ // 2 - 2, y0 + h_ - 1, 4, 1)):
            g.fill_rect(g.walls, gx, gy_, gw, gh, 0)
        for cx, cy, cw, ch in cabins:  # cabañas: bloques de muro dentro de la zona segura, con su alero en `above`
            g.fill_rect(g.walls, cx, cy, cw, ch, WALL)
            g.fill_rect(g.above, cx, cy - 1, cw, 1, BUSH)
        g.fill_rect(g.ground, gy[0] - 2, gy[1] - 2, 5, 5, PATH)  # plaza de la fogata
        graveyards.append(obj(nid(), gy_id, "graveyard", gy[0], gy[1], point=True))
        zones.append(obj(nid(), name, "zone", x0, y0, w_, h_, [prop("name", name), prop("safe", True), prop("minLevel", 6), prop("maxLevel", 10)]))

    safe_camp("Campamento de Brena", "gy_forest_edge", GY_EDGE, EDGE_CAMP, [(48, 46, 5, 3), (58, 46, 5, 3)])
    npcs.append(obj(nid(), "Brena la trampera", "npc", 50, 50, props=[prop("vendorId", "forest_camp"), prop("name", "Brena la trampera")], point=True))
    safe_camp("Refugio del Pantano", "gy_swamp", GY_SWAMP, SWAMP_CAMP, [(152, 50, 4, 3)])

    # --- Zonas grandes. La frontera es SPLIT_X, la mediatriz de las dos fogatas.
    zones.append(obj(nid(), "Linde del Bosque", "zone", 2, 2, SPLIT_X - 2, H - 4,
                     [prop("name", "Linde del Bosque"), prop("safe", False), prop("minLevel", 6), prop("maxLevel", 8), prop("landmark", "Árbol Madre")]))
    zones.append(obj(nid(), "Pantano", "zone", SPLIT_X, 2, W - 2 - SPLIT_X, H - 4,
                     [prop("name", "Pantano"), prop("safe", False), prop("minLevel", 8), prop("maxLevel", 10), prop("landmark", "Torre hundida")]))

    # --- Campamentos: despejados (luego reciben unos pocos árboles o charcas) y con un claro de tierra húmeda en el centro.
    for name, monster, count, wander, (x, y, w_, h_), _ in CAMPS:
        g.clear_rect(x - 1, y - 1, w_ + 2, h_ + 2, ground=None)
        put(g.ground, blob(rng, x + w_ // 2, y + h_ // 2, w_ // 5, h_ // 5), DIRT)
        spawns.append(obj(nid(), name, "spawn", x, y, w_, h_, [prop("monsterId", monster), prop("count", count), prop("wanderRadius", wander, "float")]))

    # --- Árbol Madre: copa enorme (capa above) sobre un tronco de roca, con raíces (tierra) alrededor, en un claro.
    tx, ty = LANDMARK_TREE
    clearing = ellipse(tx, ty, 14, 12)
    put(g.walls, clearing, 0, over_protected=False)
    put(g.ground, ellipse(tx, ty + 2, 11, 9), DIRT, over_protected=False)
    put(g.above, ellipse(tx, ty, 10, 9) | ellipse(tx - 5, ty - 3, 6, 5) | ellipse(tx + 5, ty - 2, 6, 5), BUSH, over_protected=False)
    g.fill_rect(g.walls, tx - 2, ty, 4, 4, ROCK)
    protect(clearing)

    # --- Torre hundida: anillo de piedra con suelo de losas en medio de una laguna, con la puerta al sur hacia la calzada y
    # cascotes en el agua. Dentro vive la bruja (rama lateral del Pantano).
    ox, oy = LANDMARK_TOWER
    lagoon = ellipse(ox, oy - 1, 14, 10) | ellipse(ox, oy + 2, 11, 8) | ellipse(ox - 11, oy + 5, 6, 4) | ellipse(ox + 11, oy - 6, 5, 5)
    put(g.walls, lagoon, WATER, over_protected=False)
    disc = ellipse(ox, oy, 8, 8)
    inner = {(x, y) for x, y in disc if (x - ox) ** 2 + (y - oy) ** 2 <= 25}
    put(g.walls, disc - inner, ROCK, over_protected=False)
    put(g.walls, inner, 0, over_protected=False)
    put(g.ground, inner, FLOOR, over_protected=False)
    door = {(x, y) for x in range(ox - 1, ox + 2) for y in range(oy + 4, oy + 9)}
    put(g.walls, door, 0, over_protected=False)
    put(g.ground, door, FLOOR, over_protected=False)
    for cx, cy in ((ox - 11, oy - 5), (ox + 10, oy - 6), (ox - 12, oy + 3), (ox + 11, oy + 4), (ox - 4, oy - 10), (ox + 5, oy + 10)):
        if (cx, cy) in lagoon and (cx, cy) not in g.protected:
            g.walls[g.i(cx, cy)] = ROCK
    protect(lagoon | disc)
    g.carve_path(TOWER_CAUSEWAY, 3, DIRT)
    spawns.append(obj(nid(), "sunken_tower_witch", "spawn", ox, oy, 0, 0, [prop("monsterId", "swamp_witch"), prop("count", 1), prop("wanderRadius", 1.0, "float")], point=True))

    # --- Guarida del oso: claro rodeado de bosque cerrado (roca unida a la cresta y al borde) con un único ramal.
    cx, cy = BEAR_DEN
    put(g.walls, ellipse(cx, cy, 14, 12), ROCK, over_protected=False)
    den = ellipse(cx, cy, 8, 6) | ellipse(cx - 4, cy + 2, 5, 4) | ellipse(cx + 3, cy - 2, 5, 4)
    put(g.walls, den, 0)
    put(g.ground, ellipse(cx, cy, 6, 4), DIRT)
    protect(den)
    g.carve_path(BEAR_BRANCH + [BEAR_DEN], 2, DIRT)
    spawns.append(obj(nid(), "old_bear_den", "spawn", cx, cy, 0, 0, [prop("monsterId", "old_bear"), prop("count", 1), prop("wanderRadius", 1.0, "float")], point=True))

    # --- Ramales (al puente, camino; a cada campamento, tierra) y, encima, el sendero principal.
    g.carve_path(BRIDGE_BRANCH, 2, PATH)
    for _, _, _, _, (x, y, w_, h_), branch in CAMPS:
        g.carve_path(branch + [(x + w_ // 2, y + h_ // 2)], 2, DIRT)
    g.carve_path(MAIN_PATH, 3, PATH)
    main_cells = polyline_cells(MAIN_PATH)

    # Protección de puntos: NPC, fogatas y sitios reservados.
    for o in npcs + graveyards:
        g.clear_rect(o["x"] // TS - 1, o["y"] // TS - 1, 3, 3)
    for _, (x, y, w_, h_) in reserved:
        protect((xx, yy) for xx in range(x - 1, x + w_ + 1) for yy in range(y - 1, y + h_ + 1))

    # --- Vegetación y agua al azar, sin tocarse entre sí (una casilla libre alrededor) y fuera de lo protegido; dentro de cada
    # campamento, unos pocos árboles o charcas que no pisan su claro ni su ramal. Linde: lenguas de bosque cerrado, arboledas
    # (arbusto sólido bajo copa), árboles sueltos, arbustos y peñascos. Pantano: lagunas y charcas con orilla de barro, juncos,
    # árboles muertos y piedras.
    camp_cells = {(x, y) for *_, (cx, cy, w_, h_), _ in CAMPS for x in range(cx, cx + w_) for y in range(cy, cy + h_)}

    def free(cells, in_camp: bool = False) -> bool:
        for x, y in cells:
            if in_camp and ((x, y) not in camp_cells or g.ground[g.i(x, y)] != GRASS):
                return False
            for yy in range(y - 1, y + 2):
                for xx in range(x - 1, x + 2):
                    if not g.inside(xx, yy) or g.walls[g.i(xx, yy)] != 0:
                        return False
                    if (xx, yy) in g.protected and not (in_camp and (xx, yy) in camp_cells):
                        return False
        return True

    def mud(cells) -> None:
        for x, y in cells:
            if g.inside(x, y) and (x, y) not in g.protected and g.walls[g.i(x, y)] == 0 and g.ground[g.i(x, y)] == GRASS:
                g.ground[g.i(x, y)] = DIRT

    def grove(x: int, y: int) -> None:
        cells = blob(rng, x, y, rng.randint(2, 4), rng.randint(1, 3))
        if free(cells):
            put(g.walls, cells, BUSH)
            put(g.above, cells | {(cx, cy - 1) for cx, cy in cells}, BUSH)

    def tree(x: int, y: int, size: int, in_camp: bool = False) -> None:  # copa de size×size con el tronco (arbusto) debajo
        cells = {(x + dx, y + dy) for dx in range(size) for dy in range(size)}
        if free(cells, in_camp):
            g.walls[g.i(x + size // 2, y + size - 1)] = BUSH
            put(g.above, cells, BUSH)

    def pool(x: int, y: int, rx: int, ry: int, in_camp: bool = False, shore: bool = False) -> None:  # laguna: con orilla de barro
        cells = blob(rng, x, y, rx, ry)
        if free(cells, in_camp):
            put(g.walls, cells, WATER)
            if shore:
                mud({(cx + dx, cy + dy) for cx, cy in cells for dx in (-1, 0, 1) for dy in (-1, 0, 1)})

    def small(x: int, y: int, gid: int, w_: int, h_: int) -> None:
        cells = {(x + dx, y + dy) for dx in range(w_) for dy in range(h_)}
        if free(cells):
            put(g.walls, cells, gid)

    for _ in range(150):  # Linde: lenguas de bosque cerrado (roca unida al borde o a la cresta)
        x, y = rng.randint(4, SPLIT_X - 4), rng.choice((rng.randint(2, 8), rng.randint(H - 9, H - 3), rng.randint(4, H - 5)))
        cells = {c for c in blob(rng, x, y, rng.randint(2, 5), rng.randint(2, 4)) if g.inside(*c)}
        near = {(cx + dx, cy + dy) for cx, cy in cells for dx in (-1, 0, 1) for dy in (-1, 0, 1)}
        if any(g.inside(*c) and g.walls[g.i(*c)] == ROCK for c in near) and not any(c in g.protected for c in near):
            put(g.walls, cells, ROCK)
    for _ in range(4000):  # Linde
        kind, x, y = rng.random(), rng.randint(6, SPLIT_X - 8), rng.randint(4, H - 5)
        if kind < 0.3:
            grove(x, y)
        elif kind < 0.6:
            tree(x, y, 3)
        elif kind < 0.88:
            small(x, y, BUSH, rng.randint(1, 2), 1)
        else:  # peñasco: bloque de 2×2 o más (una sola fila sería valla)
            small(x, y, WALL, rng.randint(2, 3), 2)
    for _ in range(1500):  # Pantano: primero las lagunas (las grandes no caben si antes se reparte lo pequeño)
        pool(rng.randint(SPLIT_X + 8, W - 6), rng.randint(4, H - 5), rng.randint(3, 7), rng.randint(2, 4), shore=True)
    for _ in range(4000):
        kind, x, y = rng.random(), rng.randint(SPLIT_X + 8, W - 6), rng.randint(4, H - 5)
        if kind < 0.5:
            pool(x, y, rng.randint(1, 3), rng.randint(1, 2))
        elif kind < 0.72:  # juncos
            small(x, y, BUSH, rng.randint(1, 2), 1)
        elif kind < 0.9:   # árbol muerto
            tree(x, y, 2)
        else:              # piedra
            small(x, y, ROCK, rng.randint(1, 2), rng.randint(1, 2))
    for _, _, _, _, (cx, cy, w_, h_), _ in CAMPS:  # dentro de los campamentos
        for _ in range(8):
            x, y = rng.randint(cx, cx + w_ - 3), rng.randint(cy, cy + h_ - 3)
            if cx < SPLIT_X:
                tree(x, y, 3, in_camp=True)
            else:
                pool(x, y, rng.randint(1, 2), 1, in_camp=True)
    # Detalle: flores y setas en la hierba.
    for _ in range(900):
        x, y = rng.randint(3, W - 4), rng.randint(3, H - 4)
        if g.walls[g.i(x, y)] == 0 and g.ground[g.i(x, y)] == GRASS:
            g.detail[g.i(x, y)] = DIRT

    # --- Sin bolsas inaccesibles: lo que no se alcanza desde la entrada se rellena (bosque junto a la roca, agua en el
    # Pantano y arbusto en el Linde).
    origin = GY_EDGE
    reach = g.reachable_from(*origin)
    sealed = 0
    for y in range(H):
        for x in range(W):
            if not g.solid(x, y) and (x, y) not in reach:
                near_rock = any(g.inside(nx, ny) and g.walls[g.i(nx, ny)] == ROCK for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
                g.walls[g.i(x, y)] = ROCK if near_rock else (WATER if x >= SPLIT_X else BUSH)
                sealed += 1

    # Portal de la boca de la Cripta a su entrada (HU-115). Va al final para no mover los ids del resto de objetos.
    px, py, pw, ph = CRYPT_ENTRANCE
    portals.append(obj(nid(), "to_crypt", "portal", px, py, pw, ph, [prop("portalId", "forest_to_crypt"), prop("targetMapId", "crypt"),
                       prop("targetX", CRYPT_FOREST_ARRIVAL[0]), prop("targetY", CRYPT_FOREST_ARRIVAL[1])]))

    report = check_map(g, origin, spawns, graveyards, npcs, portals)
    report["sealed"] = sealed
    check_forest(g, report, spawns, graveyards, npcs, zones, reserved, main_cells)
    layers = [
        tile_layer(1, "ground", g.ground, W, H), tile_layer(2, "detail", g.detail, W, H), tile_layer(3, "walls", g.walls, W, H), tile_layer(4, "above", g.above, W, H),
        obj_layer(5, "spawns", spawns), obj_layer(6, "npcs", npcs), obj_layer(7, "graveyards", graveyards), obj_layer(8, "zones", zones), obj_layer(9, "portals", portals),
    ]
    data = tmj(g, "forest", "Bosque", "gy_forest_edge", layers, oid)
    data["properties"].append(prop("biome", "forest"))
    return data, report


def check_forest(g: Grid, report: dict, spawns: list[dict], graveyards: list[dict], npcs: list[dict], zones: list[dict],
                 reserved: list[tuple[str, tuple[int, int, int, int]]], main_cells: list[tuple[int, int]]) -> None:
    """Comprobaciones propias del Bosque (además de check_map): bordes, fogata más cercana por zona, separación del aggro,
    sitios reservados, subniveles y tiempo de cruce. Añade los fallos a report["errors"] y las medidas a report."""
    errors: list[str] = report["errors"]
    rules = json.loads((ROOT / "content" / "rules.json").read_text(encoding="utf-8"))
    monsters = {m["id"]: m for m in json.loads((ROOT / "content" / "monsters.json").read_text(encoding="utf-8"))["monsters"]}
    reach = g.reachable_from(*GY_EDGE)

    # Bordes sólidos (criterio 5).
    for x in range(g.w):
        for y in (0, g.h - 1):
            if not g.solid(x, y):
                errors.append(f"borde abierto en ({x}, {y})")
    for y in range(g.h):
        for x in (0, g.w - 1):
            if not g.solid(x, y):
                errors.append(f"borde abierto en ({x}, {y})")

    # Cuello de botella (criterio 1): la cresta solo se cruza por el paso de 4 casillas.
    open_ridge = [y for y in range(g.h) if not g.solid(SPLIT_X, y)]
    if open_ridge != list(range(PASS_Y - 2, PASS_Y + 2)):
        errors.append(f"la cresta se cruza fuera del paso, por las filas {open_ridge}")

    # Morir en una zona devuelve a la fogata de esa zona (criterio 3): la más cercana a cada casilla transitable es la suya.
    big = [z for z in zones if not next(p["value"] for p in z["properties"] if p["name"] == "safe")]
    gy_of_zone = {}
    for gy in graveyards:
        gx, gyy = gy["x"] / TS, gy["y"] / TS
        zone = next(z for z in big if z["x"] / TS <= gx < (z["x"] + z["width"]) / TS and z["y"] / TS <= gyy < (z["y"] + z["height"]) / TS)
        gy_of_zone[zone["name"]] = gy["name"]
    if len(gy_of_zone) != len(big):
        errors.append("no hay una fogata por zona")
    wrong = 0
    for (x, y) in reach:
        px_, py_ = x + 0.5, y + 0.5
        zone = next((z for z in big if z["x"] / TS <= px_ < (z["x"] + z["width"]) / TS and z["y"] / TS <= py_ < (z["y"] + z["height"]) / TS), None)
        if zone is None:
            errors.append(f"casilla ({x}, {y}) fuera de las zonas")
            continue
        nearest = min(graveyards, key=lambda o: (o["x"] / TS - px_) ** 2 + (o["y"] / TS - py_) ** 2)
        if nearest["name"] != gy_of_zone.get(zone["name"]):
            wrong += 1
    if wrong:
        errors.append(f"{wrong} casillas reaparecerían en la fogata de la otra zona")

    # Aggro: los círculos de aggro de grupos distintos no se solapan (distancia entre rectángulos ≥ suma de los radios).
    def rect(s: dict) -> tuple[float, float, float, float]:
        return (s["x"] / TS, s["y"] / TS, s["width"] / TS, s["height"] / TS)

    def aggro(s: dict) -> float:
        return float(monsters[next(p["value"] for p in s["properties"] if p["name"] == "monsterId")]["aggroRange"])

    min_gap = math.inf
    for i, a in enumerate(spawns):
        for b in spawns[i + 1:]:
            gap = rect_gap(rect(a), rect(b))
            need = aggro(a) + aggro(b)
            min_gap = min(min_gap, gap - need)
            if gap < need:
                errors.append(f"aggro solapado: {a['name']} y {b['name']} a {gap:.1f} (< {need:.0f})")
    report["aggro_margin"] = min_gap
    # Ningún campamento pisa el sendero principal; ninguno alcanza con su aggro el sendero de llegada (de la boca de la Mina
    # al campamento de Brena) ni las zonas seguras ni los sitios reservados.
    arrival = main_cells[:main_cells.index(ARRIVAL_PATH_END) + 1]
    safe_rects = [(z["x"] / TS, z["y"] / TS, z["width"] / TS, z["height"] / TS) for z in zones if z not in big]
    main_set = set()
    for x, y in main_cells:
        main_set |= {(x + dx, y + dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1)}
    for s in spawns:
        r = rect(s)
        cells = {(x, y) for x in range(int(r[0]), int(r[0] + max(1, r[2]))) for y in range(int(r[1]), int(r[1] + max(1, r[3])))}
        if cells & main_set:
            errors.append(f"{s['name']} pisa el sendero principal")
        if any(rect_gap(r, (x - 1, y - 1, 3, 3)) < aggro(s) for x, y in arrival):
            errors.append(f"{s['name']} alcanza con su aggro el sendero de llegada")
        for sr in safe_rects:
            if rect_gap(r, sr) < aggro(s) + 2:
                errors.append(f"{s['name']} demasiado cerca de una zona segura")
        for label, rr in reserved:
            if rect_gap(r, rr) < aggro(s):
                errors.append(f"{s['name']} alcanza con su aggro el sitio de {label}")
    for label, (x, y, w_, h_) in reserved:
        for yy in range(y, y + h_):
            for xx in range(x, x + w_):
                if (xx, yy) not in reach:
                    errors.append(f"sitio de {label}: ({xx}, {yy}) no es transitable o no se alcanza")

    # Muros: el cliente dibuja como valla los de una casilla de grosor; fuera de las zonas seguras solo la baranda del puente
    # puede serlo (un ramal que corte un peñasco dejaría un poste suelto).
    bx, by, bw, bh = BRIDGE_DECK
    railing = {(x, y) for x in range(bx, bx + bw) for y in (by - 1, by + bh)}
    seen: set[tuple[int, int]] = set()
    for y0 in range(g.h):
        for x0 in range(g.w):
            if g.walls[g.i(x0, y0)] != WALL or (x0, y0) in seen:
                continue
            comp: list[tuple[int, int]] = []
            stack = [(x0, y0)]
            while stack:
                c = stack.pop()
                if c in seen or not g.inside(*c) or g.walls[g.i(*c)] != WALL:
                    continue
                seen.add(c)
                comp.append(c)
                stack += [(c[0] + 1, c[1]), (c[0] - 1, c[1]), (c[0], c[1] + 1), (c[0], c[1] - 1)]
            cells = set(comp)
            thin = not any((x + 1, y) in cells and (x, y + 1) in cells and (x + 1, y + 1) in cells for x, y in comp)
            in_safe = all(any(sr[0] <= x < sr[0] + sr[2] and sr[1] <= y < sr[1] + sr[3] for sr in safe_rects) for x, y in comp)
            if thin and not in_safe and not cells <= railing:
                errors.append(f"valla suelta fuera de las zonas seguras en {min(comp)}")

    # Subniveles: en cada zona, los campamentos de menor nivel están más cerca de la entrada (al oeste).
    for z in big:
        zx0, zx1 = z["x"] / TS, (z["x"] + z["width"]) / TS
        by_level: dict[int, list[float]] = {}
        for s in spawns:
            m = monsters[next(p["value"] for p in s["properties"] if p["name"] == "monsterId")]
            cx = (s["x"] + s["width"] / 2) / TS
            if zx0 <= cx < zx1 and m["type"] != "elite":
                by_level.setdefault(m["level"], []).append(cx)
        levels = sorted(by_level)
        for lo, hi in zip(levels, levels[1:]):
            if max(by_level[lo]) >= min(by_level[hi]):
                errors.append(f"{z['name']}: los campamentos de nivel {lo} no quedan antes que los de nivel {hi}")

    # Tiempo de cruce por el sendero (criterio 1): longitud del sendero principal dentro de cada zona / velocidad base.
    speed = float(rules["movement"]["baseSpeedTilesPerSec"])
    lo_s, hi_s = rules["world"]["zoneCrossTimeSecTarget"]
    linde = [c for c in main_cells if c[0] < SPLIT_X]
    swamp = [c for c in main_cells if c[0] >= SPLIT_X]
    report["cross"] = {}
    for name, cells, start, goal in (("Linde del Bosque", linde, MINE_ARRIVAL, (SPLIT_X, PASS_Y)), ("Pantano", swamp, (SPLIT_X, PASS_Y), CRYPT_ARRIVAL)):
        length = len(cells) - 1
        direct = shortest(g, start, goal)
        report["cross"][name] = (length, length / speed, direct, direct / speed)
        if not lo_s <= length / speed <= hi_s:
            errors.append(f"{name}: el sendero se cruza en {length / speed:.1f} s (objetivo {lo_s}–{hi_s} s)")


def shortest(g: Grid, start: tuple[int, int], goal: tuple[int, int]) -> float:
    """Camino más corto en casillas con 8 vecinos (diagonal √2, sin cortar esquinas): lo que tarda quien no sigue el sendero."""
    dist = {start: 0.0}
    heap = [(0.0, start)]
    while heap:
        d, (x, y) = heapq.heappop(heap)
        if (x, y) == goal:
            return d
        if d > dist[(x, y)]:
            continue
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
            nx, ny = x + dx, y + dy
            if g.solid(nx, ny) or (dx and dy and (g.solid(x + dx, y) or g.solid(x, y + dy))):
                continue
            nd = d + (1.4142135623730951 if dx and dy else 1.0)
            if nd < dist.get((nx, ny), math.inf):
                dist[(nx, ny)] = nd
                heapq.heappush(heap, (nd, (nx, ny)))
    return math.inf


# ----------------------------------------------------------------------------------------------------------------------------
# crypt (HU-115): Umbral (zona segura) → Osario → Galería de raíces → Jardín de espinas (la trampa) → bifurcación: la sala del
# jefe, en una rama lateral sin salida, y la Antesala del guardián (élite) → salida al Tier 3, un derrumbe al fondo.
# ----------------------------------------------------------------------------------------------------------------------------

CRYPT_W, CRYPT_H = 118, 80
# Umbral: zona segura con la única fogata de la Cripta (morir dentro deja aquí, CA4) y el portal de vuelta al Bosque.
CRYPT_SAFE = (6, 63, 14, 11)            # x 6..19 · y 63..73
CRYPT_GY = (14, 70)
CRYPT_FOREST_PORTAL = (6, 66, 2, 3)     # portal de vuelta (x 6..7 · y 66..68) en la pared oeste; deja en CRYPT_ARRIVAL del Bosque
CRYPT_FOREST_ARRIVAL = (10, 67)         # donde deja el portal de la boca de la Cripta (CRYPT_ENTRANCE del Bosque)
# Cavernas (elipses unidas: centro y radios) y la casilla por la que se entra a cada sala.
OSSUARY = [(20, 45, 12, 9), (11, 41, 5, 5), (28, 50, 5, 4)]
OSSUARY_DOOR = (13, 54)
GALLERY = [(24, 16, 12, 9), (14, 11, 5, 4), (33, 21, 5, 4)]
GALLERY_DOOR = (22, 25)
# Monstruos de las dos primeras salas (rectángulo x, y, ancho, alto, dentro de la caverna y lejos de su puerta). Los espíritus
# del musgo curan a los monstruos a 5 casillas: van entre los esqueletos, y en la Galería hay más.
CRYPT_CAMPS = [
    ("ossuary_skeletons", "root_skeleton", 5, 2.0, (14, 39, 13, 7)),
    ("ossuary_spirits", "moss_spirit", 2, 2.0, (17, 40, 7, 4)),
    ("gallery_skeletons", "root_skeleton", 4, 2.0, (17, 10, 14, 7)),
    ("gallery_spirits", "moss_spirit", 3, 2.0, (20, 11, 8, 5)),
]
# Jardín de espinas (CA2): sala alargada de 9 casillas de alto con las plantas trampa (inmóviles, aggro 4) alternadas contra la
# pared norte y la sur cada 8 casillas, cada una en su lecho de mantillo. Por el centro se pasa a 3 casillas de todas (despiertan
# y enraízan); quien va con cuidado hace un eslalon de ~2 casillas de ancho pegado a la pared contraria a cada planta, o las limpia.
# La primera queda a más de aggro + 2 de la puerta: se ven antes de que alcancen.
TRAP_HALL = (44, 10, 46, 9)             # x 44..89 · y 10..18
TRAP_DOOR = (43, 14)                    # entrada desde la Galería, en el centro de la pared oeste
TRAP_EXIT = (90, 14)                    # salida a la bifurcación, en el centro de la pared este
TRAP_PLANTS = [(51, 11), (59, 17), (67, 11), (75, 17), (83, 11)]
# Bifurcación al salir del jardín: al sur, la Antesala del guardián y la salida; al oeste, la rama lateral del jefe.
FORK = [(100, 32, 5, 4)]
GUARD_ROOM = [(100, 53, 10, 9)]
GUARD_DOOR = (100, 44)
GUARDIAN = (100, 61)                    # el élite guarda la boca del pasillo de salida: no se llega a ella sin entrar en su aggro
# Sala del jefe (HU-117: el Árbol Podrido, inmóvil, con áreas marcadas sobre jugadores a distancia, áreas duraderas y retoños):
# una sola elipse amplia (convexa: desde el centro se ve todo el suelo), sin columnas, con el jefe en medio y sitio para esquivar
# alrededor. Todo el suelo a BOSS_REACH casillas o menos del jefe (su básico llega a 13,5), y todo lo que el jefe ve, también el
# pasillo recto de entrada, al alcance de sus áreas apuntadas (Raíces y Esporas, 24 + 1,5 de tolerancia; lo comprueba
# check_crypt con el contenido): no queda ningún rincón desde el que pegarle o curar al tanque sin que pueda responder.
BOSS_ROOM = (60, 49, 13, 13)            # x 47..73 · y 36..62
BOSS_DOOR = (74, 49)                    # el pasillo de la bifurcación entra por el este
BOSS_SPAWN = (60, 49)
BOSS_REACH = 13
# Salida al Tier 3 (CA3): boca al fondo del pasillo que sale de la Antesala, cerrada por un derrumbe: portal con `minPhase: 3`
# (HU-112) a la Montaña, que aún no existe (el servidor lo deja cerrado y lo avisa al arrancar). Su destino lo fija el mapa del
# Tier 3; TIER3_ARRIVAL es donde dejaría el portal de vuelta.
TIER3_EXIT = (99, 71, 3, 2)             # x 99..101 · y 71..72
TIER3_ARRIVAL = (100, 67)
TIER3_MAP = "mountain"
TIER3_TARGET = (0, 0)                   # provisional: con mountain.tmj, el test de llegadas exigirá una casilla libre
CRYPT_PATHS = [
    [(13, 64), (13, 53)],                               # Umbral → Osario
    [(22, 37), (22, 24)],                               # Osario → Galería
    [(35, 14), (44, 14)],                               # Galería → Jardín de espinas
    [(89, 14), (100, 14), (100, 32)],                   # Jardín → bifurcación
    [(100, 32), (100, 45)],                             # bifurcación → Antesala
    [(100, 32), (84, 32), (84, 49), (73, 49)],          # bifurcación → sala del jefe (rama lateral)
    [(100, 61), (100, 72)],                             # Antesala → salida
]
# Recorrido en 5–10 min (GDD §Cuevas de transición) para un grupo de `rules.boss.referencePartySize`: a pie por el camino más
# corto, cada normal un ciclo de `rules.progression.killCycleSecTarget` repartido entre el grupo, el jefe `rules.boss.targetDurationSec`
# y el élite lo que dura en trío (balance-report §Fase 2 · Monstruos: 20–34 s).
CAVE_RUN_MIN_TARGET = (5, 10)
ELITE_TRIO_SEC = (20, 34)


def line_of_sight(g: Grid, a: tuple[int, int], b: tuple[int, int]) -> bool:
    """Bresenham entre casillas como LineOfSight.Has del servidor: la roca y el muro tapan; el origen y el destino no cuentan."""
    (x0, y0), (x1, y1) = a, b
    dx, dy = abs(x1 - x0), -abs(y1 - y0)
    sx, sy = (1 if x0 < x1 else -1), (1 if y0 < y1 else -1)
    err = dx + dy
    while True:
        if (x0, y0) != a and (x0, y0) != b and g.walls[g.i(x0, y0)] in (WALL, ROCK):
            return False
        if (x0, y0) == (x1, y1):
            return True
        e2 = 2 * err
        if e2 >= dy:
            err += dy
            x0 += sx
        if e2 <= dx:
            err += dx
            y0 += sy


def reach_avoiding(g: Grid, start: tuple[int, int], blocked) -> set[tuple[int, int]]:
    """Casillas alcanzables desde `start` (4 vecinos) sin pisar ninguna de `blocked` (una función de la casilla)."""
    seen = {start}
    stack = [start]
    while stack:
        x, y = stack.pop()
        for n in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if n not in seen and not g.solid(*n) and not blocked(n):
                seen.add(n)
                stack.append(n)
    return seen


def put_ground(g: Grid, cells, gid: int) -> None:
    """Suelo `gid` en las casillas transitables (en la roca no cambia nada)."""
    for x, y in cells:
        if g.inside(x, y) and g.walls[g.i(x, y)] == 0:
            g.ground[g.i(x, y)] = gid


def build_crypt() -> tuple[dict, dict]:
    g = Grid(CRYPT_W, CRYPT_H, FLOOR)
    g.fill_rect(g.walls, 0, 0, CRYPT_W, CRYPT_H, ROCK)  # todo roca; se excavan las salas y los pasillos
    rng = random.Random(115)
    oid = 1

    def nid() -> int:
        nonlocal oid
        oid += 1
        return oid - 1

    def carve(cells, ground: int = FLOOR) -> None:
        for x, y in cells:
            if 2 <= x < g.w - 2 and 2 <= y < g.h - 2:
                g.walls[g.i(x, y)] = 0
                g.ground[g.i(x, y)] = ground

    def cavern(shape: list[tuple[int, int, int, int]]) -> set[tuple[int, int]]:
        cells: set[tuple[int, int]] = set()
        for cx, cy, rx, ry in shape:
            cells |= ellipse(cx, cy, rx, ry)
        carve(cells)
        return cells

    # --- Salas y pasillos (3 casillas de ancho).
    sx, sy, sw, sh = CRYPT_SAFE
    g.clear_rect(sx, sy, sw, sh, ground=FLOOR)
    rooms = {"Umbral": {(x, y) for x in range(sx, sx + sw) for y in range(sy, sy + sh)}, "Osario": cavern(OSSUARY),
             "Galería de raíces": cavern(GALLERY), "bifurcación": cavern(FORK), "Antesala del guardián": cavern(GUARD_ROOM)}
    # Jardín de espinas: el rectángulo y, detrás de cada planta, una hornacina en su pared con el lecho de mantillo.
    hx, hy, hw, hh = TRAP_HALL
    hall = {(x, y) for x in range(hx, hx + hw) for y in range(hy, hy + hh)}
    for i, (px, py) in enumerate(TRAP_PLANTS):
        rows = (hy - 1, hy - 2) if i % 2 == 0 else (hy + hh, hy + hh + 1)
        hall |= {(x, y) for depth, y in enumerate(rows, 1) for x in range(px - 3 + depth, px + 4 - depth)}
    carve(hall)
    for i, (px, py) in enumerate(TRAP_PLANTS):
        put_ground(g, ellipse(px, py, 2, 1) | ellipse(px, py + (-1 if i % 2 == 0 else 1), 1, 1), DIRT)
        # Matas de espinas a los lados del lecho, contra su pared: dentro del aggro de la planta, lejos del eslalon.
        wall_row = hy if i % 2 == 0 else hy + hh - 1
        for x in (px - 3, px + 3):
            for y in (wall_row, wall_row + (1 if i % 2 == 0 else -1)):
                g.walls[g.i(x, y)] = BUSH
    rooms["Jardín de espinas"] = hall
    bx, by, brx, bry = BOSS_ROOM
    rooms["sala del jefe"] = ellipse(bx, by, brx, bry)
    carve(rooms["sala del jefe"])
    # Sin los picos de una casilla que dejan las puntas de las elipses (casillas libres con 3 o 4 vecinos de roca).
    changed = True
    while changed:
        changed = False
        for x, y in sorted(set().union(*rooms.values())):
            if g.walls[g.i(x, y)] == 0 and sum(g.walls[g.i(x + dx, y + dy)] == ROCK for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))) >= 3:
                g.walls[g.i(x, y)] = ROCK
                changed = True
    rooms = {name: {c for c in cells if g.walls[g.i(*c)] == 0} for name, cells in rooms.items()}
    hall, boss_room = rooms["Jardín de espinas"], rooms["sala del jefe"]
    for path in CRYPT_PATHS:
        g.carve_path(path, 3, FLOOR)
    g.protected |= hall | boss_room | rooms["Umbral"]  # sin raíces ni charcas: el eslalon, la sala del jefe y el Umbral
    # Raíz podrida bajo el jefe (mantillo, el único suelo de tierra junto con los lechos de las plantas) con raíces de 2 casillas
    # que se abren hacia las paredes: solo suelo, nada sólido.
    put_ground(g, ellipse(bx, by, 4, 3), DIRT)
    for k in range(7):
        ang = k * 2 * math.pi / 7 + rng.uniform(-0.25, 0.25)
        for t in range(3, rng.randint(9, 12)):
            x, y = bx + round(math.cos(ang) * t * brx / bry), by + round(math.sin(ang) * t)
            put_ground(g, {(x, y), (x + 1, y), (x, y + 1), (x + 1, y + 1)} & boss_room, DIRT)
    # Boca de la salida al Tier 3: ensanche de losas al fondo del pasillo (la luz de fuera) cerrado por el derrumbe: cascotes
    # sueltos (roca de una casilla) por la mitad de abajo de la boca.
    ex, ey, ew, eh = TIER3_EXIT
    carve(ellipse(ex + ew // 2, ey + 2, 5, 3), PATH)
    for x, y in ((ex - 3, ey + 2), (ex - 1, ey + 4), (ex + 1, ey + 3), (ex + ew, ey + 5), (ex + ew + 1, ey + 3), (ex + ew + 3, ey + 2)):
        g.walls[g.i(x, y)] = ROCK
    g.protected |= {(x, y) for x in range(ex - 1, ex + ew + 1) for y in range(ey - 1, ey + eh + 1)}
    # Umbral: plaza de losas alrededor de la fogata.
    put_ground(g, ellipse(CRYPT_GY[0], CRYPT_GY[1], 3, 2), PATH)

    # --- Monstruos: los de las dos primeras salas (rectángulos), las plantas (puntos fijos que no pasean: `wanderRadius` 0) y
    # el élite delante del pasillo de salida.
    spawns: list[dict] = []
    for name, monster, count, wander, (x, y, w_, h_) in CRYPT_CAMPS:
        spawns.append(obj(nid(), name, "spawn", x, y, w_, h_, [prop("monsterId", monster), prop("count", count), prop("wanderRadius", wander, "float")]))
        g.protected |= {(xx, yy) for xx in range(x - 1, x + w_ + 1) for yy in range(y - 1, y + h_ + 1)}
    for i, (px, py) in enumerate(TRAP_PLANTS):
        spawns.append(obj(nid(), f"trap_plant_{i + 1}", "spawn", px, py, 0, 0,
                          [prop("monsterId", "trap_plant"), prop("count", 1), prop("wanderRadius", 0.0, "float")], point=True))
    gx, gy = GUARDIAN
    spawns.append(obj(nid(), "crypt_guardian", "spawn", gx, gy, 0, 0, [prop("monsterId", "crypt_guardian"), prop("count", 1), prop("wanderRadius", 1.0, "float")], point=True))
    g.protected |= {(x, y) for x in range(gx - 2, gx + 3) for y in range(gy - 2, gy + 3)}
    # El jefe (HU-117): fijo en el centro de su sala, sin pasear (es inmóvil).
    spawns.append(obj(nid(), "rotten_tree", "spawn", BOSS_SPAWN[0], BOSS_SPAWN[1], 0, 0,
                      [prop("monsterId", "rotten_tree"), prop("count", 1), prop("wanderRadius", 0.0, "float")], point=True))

    # --- Raíces (arbusto: sólido, deja ver) y charcas de agua estancada contra las paredes de las salas de paso, fuera de lo
    # protegido y sin tocarse entre sí.
    def touches_rock(c: tuple[int, int]) -> bool:
        return any(g.walls[g.i(c[0] + dx, c[1] + dy)] == ROCK for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))

    for name in ("Osario", "Galería de raíces", "bifurcación", "Antesala del guardián"):
        cells = rooms[name]
        rim = sorted(c for c in cells if g.walls[g.i(*c)] == 0 and c not in g.protected and touches_rock(c))
        for _ in range(len(rim) // 6):
            x, y = rng.choice(rim)
            if rng.random() < 0.65:
                gid, shape, size = BUSH, {(x, y)} | {(x + dx, y + dy) for dx, dy in rng.sample([(1, 0), (-1, 0), (0, 1), (0, -1)], rng.randint(2, 3))}, 2
            else:  # la charca, centrada una casilla hacia dentro de la pared
                ix, iy = next((-dx, -dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)) if g.walls[g.i(x + dx, y + dy)] == ROCK)
                gid, shape, size = WATER, ellipse(x + ix, y + iy, rng.randint(1, 2), 1), 4
            clump = {c for c in shape if c in cells and c not in g.protected and g.walls[g.i(*c)] == 0}
            ring = {(cx + dx, cy + dy) for cx, cy in clump for dx in (-1, 0, 1) for dy in (-1, 0, 1)}
            if len(clump) >= size and all(g.walls[g.i(*c)] in (0, ROCK) for c in ring):
                for c in clump:
                    g.walls[g.i(*c)] = gid

    # --- Sin bolsas inaccesibles (charcas o raíces que cierren un rincón): se rellenan de roca.
    sealed = g.seal_unreachable(CRYPT_GY, ROCK)

    graveyards = [obj(nid(), "gy_crypt_entrance", "graveyard", CRYPT_GY[0], CRYPT_GY[1], point=True)]
    zones = [
        obj(nid(), "Umbral de la Cripta", "zone", sx, sy, sw, sh, [prop("name", "Umbral de la Cripta"), prop("safe", True), prop("minLevel", 9), prop("maxLevel", 11)]),
        obj(nid(), "Cripta de Raíces", "zone", 1, 1, CRYPT_W - 2, CRYPT_H - 2,
            [prop("name", "Cripta de Raíces"), prop("safe", False), prop("minLevel", 9), prop("maxLevel", 11), prop("landmark", "Árbol Podrido")]),
    ]
    px, py, pw, ph = CRYPT_FOREST_PORTAL
    portals = [obj(nid(), "to_forest", "portal", px, py, pw, ph, [prop("portalId", "crypt_to_forest"), prop("targetMapId", "forest"),
                   prop("targetX", CRYPT_ARRIVAL[0]), prop("targetY", CRYPT_ARRIVAL[1])])]
    ex, ey, ew, eh = TIER3_EXIT
    portals.append(obj(nid(), "to_mountain", "portal", ex, ey, ew, eh, [
        prop("portalId", "crypt_to_mountain"), prop("targetMapId", TIER3_MAP), prop("targetX", TIER3_TARGET[0]),
        prop("targetY", TIER3_TARGET[1]), prop("minPhase", 3), prop("lockedText", "Las raíces y el derrumbe cierran el paso a la Montaña")]))

    report = check_map(g, CRYPT_GY, spawns, graveyards, [], portals)
    report["sealed"] = sealed
    check_crypt(g, report, spawns, zones, rooms)
    layers = [
        tile_layer(1, "ground", g.ground, CRYPT_W, CRYPT_H), tile_layer(2, "detail", g.detail, CRYPT_W, CRYPT_H), tile_layer(3, "walls", g.walls, CRYPT_W, CRYPT_H),
        tile_layer(4, "above", g.above, CRYPT_W, CRYPT_H), obj_layer(5, "spawns", spawns), obj_layer(6, "npcs", []), obj_layer(7, "graveyards", graveyards),
        obj_layer(8, "zones", zones), obj_layer(9, "portals", portals),
    ]
    data = tmj(g, "crypt", "Cripta de Raíces", "gy_crypt_entrance", layers, oid)
    data["properties"].append(prop("palette", "crypt"))
    return data, report


def check_crypt(g: Grid, report: dict, spawns: list[dict], zones: list[dict], rooms: dict[str, set[tuple[int, int]]]) -> None:
    """Comprobaciones propias de la Cripta (además de check_map): bordes, entrada segura, aggro desde las puertas, la trampa, el
    orden de las salas, la sala del jefe y el tiempo de recorrido. Añade los fallos a report["errors"] y las medidas a report."""
    errors: list[str] = report["errors"]
    rules = json.loads((ROOT / "content" / "rules.json").read_text(encoding="utf-8"))
    monsters = {m["id"]: m for m in json.loads((ROOT / "content" / "monsters.json").read_text(encoding="utf-8"))["monsters"]}
    reach = g.reachable_from(*CRYPT_GY)

    def pval(o: dict, name: str):
        return next(p["value"] for p in o["properties"] if p["name"] == name)

    def centre(c: tuple[float, float]) -> tuple[float, float]:
        return (c[0] + 0.5, c[1] + 0.5)

    def dist(a: tuple[int, int], b: tuple[int, int]) -> float:  # entre los centros de dos casillas
        return math.dist(centre(a), centre(b))

    def inside(c: tuple[int, int], r: tuple[int, int, int, int]) -> bool:
        return r[0] <= c[0] < r[0] + r[2] and r[1] <= c[1] < r[1] + r[3]

    for x in range(g.w):
        for y in (0, g.h - 1):
            if not g.solid(x, y):
                errors.append(f"borde abierto en ({x}, {y})")
    for y in range(g.h):
        for x in (0, g.w - 1):
            if not g.solid(x, y):
                errors.append(f"borde abierto en ({x}, {y})")

    # Entrada (CA4): la única fogata está en el Umbral, que es zona segura, con el portal de vuelta y la llegada desde el Bosque.
    if not pval(zones[0], "safe") or (zones[0]["x"] // TS, zones[0]["y"] // TS, zones[0]["width"] // TS, zones[0]["height"] // TS) != CRYPT_SAFE:
        errors.append("la primera zona tiene que ser el Umbral, segura")
    for label, c in (("la fogata", CRYPT_GY), ("la llegada desde el Bosque", CRYPT_FOREST_ARRIVAL), ("el portal de vuelta", CRYPT_FOREST_PORTAL[:2])):
        if not inside(c, CRYPT_SAFE):
            errors.append(f"{label} no está en el Umbral")
    if inside(CRYPT_FOREST_ARRIVAL, CRYPT_FOREST_PORTAL) or CRYPT_FOREST_ARRIVAL not in reach:
        errors.append("la llegada desde el Bosque cae en el portal de vuelta o no es transitable")

    # Aggro: ningún monstruo alcanza el Umbral ni la puerta por la que se entra a su sala; las plantas, ni a 2 casillas de la suya
    # (se ven antes de que alcancen).
    doors = {"Osario": OSSUARY_DOOR, "Galería de raíces": GALLERY_DOOR, "Jardín de espinas": TRAP_DOOR, "Antesala del guardián": GUARD_DOOR,
             "sala del jefe": BOSS_DOOR}
    safe_rect = tuple(float(v) for v in CRYPT_SAFE)
    for s in spawns:
        m = monsters[pval(s, "monsterId")]
        aggro = float(m["aggroRange"])
        r = (s["x"] / TS, s["y"] / TS, s["width"] / TS, s["height"] / TS)
        if rect_gap(r, safe_rect) < aggro + 2:
            errors.append(f"{s['name']} demasiado cerca del Umbral")
        room = next((n for n, cells in rooms.items() if (s["x"] // TS, s["y"] // TS) in cells), None)
        if room not in doors:
            errors.append(f"{s['name']} no está en ninguna sala con puerta")
            continue
        margin = 2 if m["id"] == "trap_plant" else 0
        if rect_gap(r, (doors[room][0], doors[room][1], 1, 1)) <= aggro + margin:
            errors.append(f"{s['name']} alcanza con su aggro la puerta de {room}")

    # La trampa (CA2): se ven desde la puerta del jardín; por el centro despiertan todas; con cuidado hay un eslalon.
    plants = [(s["x"] // TS, s["y"] // TS) for s in spawns if pval(s, "monsterId") == "trap_plant"]
    plant_aggro = float(monsters["trap_plant"]["aggroRange"])
    for s in spawns:
        if pval(s, "monsterId") == "trap_plant" and (not s.get("point") or pval(s, "count") != 1 or pval(s, "wanderRadius") != 0):
            errors.append(f"{s['name']}: las plantas son puntos con count 1 y wanderRadius 0")
    for p in plants:
        if not line_of_sight(g, TRAP_DOOR, p):
            errors.append(f"la planta de {p} no se ve desde la puerta del jardín")
    centre_line = [(x, TRAP_DOOR[1]) for x in range(TRAP_DOOR[0], TRAP_EXIT[0] + 1)]
    woken = sum(1 for p in plants if any(dist(c, p) <= plant_aggro for c in centre_line))
    if woken < len(plants):
        errors.append(f"por el centro del jardín solo despiertan {woken} de {len(plants)} plantas")
    lane = lane_width(g, plants, plant_aggro, TRAP_DOOR, TRAP_EXIT)
    if lane < 1.0:
        errors.append(f"el eslalon del jardín mide {lane:.2f} casillas en su paso más estrecho (mínimo 1)")
    report["trap"] = (woken, len(plants), lane)

    # Orden de las salas: el jardín va antes del élite, la salida y el jefe; la salida, detrás del élite; el jefe, en una rama
    # lateral (ni el camino a la salida pasa por su sala ni él queda detrás del élite).
    exit_cells = [(x, y) for x in range(TIER3_EXIT[0], TIER3_EXIT[0] + TIER3_EXIT[2]) for y in range(TIER3_EXIT[1], TIER3_EXIT[1] + TIER3_EXIT[3])]
    if not all(c in reach for c in exit_cells):
        errors.append("la salida al Tier 3 no es transitable o no se alcanza")
    if TIER3_ARRIVAL not in reach or TIER3_ARRIVAL in exit_cells:
        errors.append("la llegada desde el Tier 3 cae en la salida o no es transitable")
    guardian = next(s for s in spawns if pval(s, "monsterId") == "crypt_guardian")
    guard_pos, guard_aggro = (guardian["x"] // TS, guardian["y"] // TS), float(monsters["crypt_guardian"]["aggroRange"])
    if dist(TIER3_ARRIVAL, guard_pos) <= guard_aggro:
        errors.append("quien llega desde el Tier 3 cae en el aggro del guardián")
    # Lo más lejos que puede estar el guardián de su punto al pasear (`wanderRadius` en cada eje).
    sneak = guard_aggro - float(pval(guardian, "wanderRadius")) * math.sqrt(2)
    if exit_cells[0] in reach_avoiding(g, CRYPT_GY, lambda c: dist(c, guard_pos) <= sneak):
        errors.append("se llega a la salida sin entrar en el aggro del guardián")
    without_trap = reach_avoiding(g, CRYPT_GY, lambda c: c in rooms["Jardín de espinas"])
    for label, c in (("el élite", guard_pos), ("la salida", exit_cells[0]), ("el jefe", BOSS_SPAWN)):
        if c in without_trap:
            errors.append(f"se llega a {label} sin cruzar el Jardín de espinas")
    if exit_cells[0] not in reach_avoiding(g, CRYPT_GY, lambda c: c in rooms["sala del jefe"]):
        errors.append("la sala del jefe no es una rama lateral: el camino a la salida pasa por ella")
    without_guard = reach_avoiding(g, CRYPT_GY, lambda c: c in rooms["Antesala del guardián"])
    if BOSS_SPAWN not in without_guard:
        errors.append("el jefe queda detrás del élite")
    if exit_cells[0] in without_guard:
        errors.append("se llega a la salida sin pasar por la Antesala del guardián")

    # Sala del jefe: suelo libre (sin columnas) y todo a la vista desde el jefe.
    boss_cells = rooms["sala del jefe"]
    if any(g.solid(*c) for c in boss_cells):
        errors.append("hay obstáculos dentro de la sala del jefe")
    hidden = [c for c in boss_cells if not line_of_sight(g, BOSS_SPAWN, c)]
    if hidden:
        errors.append(f"{len(hidden)} casillas de la sala del jefe no se ven desde él, p. ej. {hidden[0]}")
    far = [c for c in boss_cells if dist(c, BOSS_SPAWN) > BOSS_REACH]
    if far:
        errors.append(f"{len(far)} casillas de la sala del jefe quedan a más de {BOSS_REACH} del jefe, p. ej. {far[0]}")
    # Revisión de autoridad (HU-117): lo que el jefe ve está al alcance de sus áreas apuntadas (si no, un sanador en el pasillo
    # curaría al tanque sin que el árbol pudiera responderle).
    spells = {s["id"]: s for s in json.loads((ROOT / "content" / "spells.json").read_text(encoding="utf-8"))["spells"]}
    aimed = [spells[s["spellId"]] for s in monsters["rotten_tree"]["spells"] if spells[s["spellId"]]["targeting"].startswith("ground_aoe")]
    reach = min(float(s["range"]) for s in aimed) + float(rules["combat"]["castRangeToleranceTiles"])
    seen_far = [(x, y) for y in range(g.h) for x in range(g.w)
                if not g.solid(x, y) and dist((x, y), BOSS_SPAWN) > reach and line_of_sight(g, BOSS_SPAWN, (x, y))]
    if seen_far:
        errors.append(f"{len(seen_far)} casillas ven al jefe más allá de sus áreas apuntadas ({reach}), p. ej. {seen_far[0]}")
    report["boss"] = (len(boss_cells), dist(BOSS_DOOR, BOSS_SPAWN))

    # Recorrido (GDD §Cuevas de transición): de la llegada al jefe y del jefe a la salida, a pie y con las peleas del grupo.
    speed = float(rules["movement"]["baseSpeedTilesPerSec"])
    to_boss, to_exit = shortest(g, CRYPT_FOREST_ARRIVAL, BOSS_SPAWN), shortest(g, BOSS_SPAWN, exit_cells[0])
    walk = (to_boss + to_exit) / speed
    normals = sum(pval(s, "count") for s in spawns if monsters[pval(s, "monsterId")]["type"] in ("normal", "hard") and (s["x"] // TS, s["y"] // TS) not in rooms["Jardín de espinas"])
    per_normal = float(rules["progression"]["killCycleSecTarget"]) / float(rules["boss"]["referencePartySize"])
    boss_lo, boss_hi = rules["boss"]["targetDurationSec"]
    careful = walk + normals * per_normal + ELITE_TRIO_SEC[0] + boss_lo         # cruzando el jardín por el eslalon
    cleared = walk + (normals + len(plants)) * per_normal + ELITE_TRIO_SEC[1] + boss_hi  # limpiando las plantas
    report["run"] = (to_boss, to_exit, walk, normals, careful, cleared)
    lo_m, hi_m = CAVE_RUN_MIN_TARGET
    if not (lo_m * 60 <= careful and cleared <= hi_m * 60):
        errors.append(f"el recorrido estimado ({careful / 60:.1f}–{cleared / 60:.1f} min) se sale de {lo_m}–{hi_m} min")


def lane_width(g: Grid, plants: list[tuple[int, int]], aggro: float, start: tuple[int, int], goal: tuple[int, int], step: float = 0.25) -> float:
    """Anchura del paso más estrecho del mejor camino de `start` a `goal` (casillas) que no entra en el aggro de ninguna planta:
    el doble del mayor margen `e` con el que sigue habiendo camino a más de aggro + e de las plantas y a e de las paredes.
    Muestrea el jardín cada `step` casillas; 0 si no hay camino."""
    x0, x1 = min(start[0], goal[0]), max(start[0], goal[0]) + 1
    ys = [y for y in range(g.h) if any(not g.solid(x, y) for x in range(x0, x1))]
    y0, y1 = min(ys), max(ys) + 1
    nx, ny = int((x1 - x0) / step), int((y1 - y0) / step)
    pts = [[(x0 + (i + 0.5) * step, y0 + (j + 0.5) * step) for j in range(ny)] for i in range(nx)]
    centres = [(px + 0.5, py + 0.5) for px, py in plants]

    def open_at(x: float, y: float) -> bool:
        return not g.solid(math.floor(x), math.floor(y))

    def passable(e: float) -> bool:
        free = [[open_at(x, y) and open_at(x - e, y) and open_at(x + e, y) and open_at(x, y - e) and open_at(x, y + e)
                 and all(math.dist((x, y), c) > aggro + e for c in centres) for x, y in col] for col in pts]
        frontier = [(i, j) for i in range(nx) for j in range(ny) if free[i][j] and (math.floor(pts[i][j][0]), math.floor(pts[i][j][1])) == start]
        seen = set(frontier)
        while frontier:
            i, j = frontier.pop()
            if (math.floor(pts[i][j][0]), math.floor(pts[i][j][1])) == goal:
                return True
            for a, b in ((i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1)):
                if 0 <= a < nx and 0 <= b < ny and free[a][b] and (a, b) not in seen:
                    seen.add((a, b))
                    frontier.append((a, b))
        return False

    if not passable(0.0):
        return 0.0
    lo, hi = 0.0, 2.0
    for _ in range(7):
        mid = (lo + hi) / 2
        lo, hi = (mid, hi) if passable(mid) else (lo, mid)
    return 2 * lo


def main() -> int:
    check_only = "--check" in sys.argv
    ok = True
    data, report = build_forest()
    print(f"forest: {data['width']}×{data['height']} · transitables {report['walkable']} · alcanzables {report['reachable']} · selladas {report['sealed']}"
          f" · margen de aggro {report['aggro_margin']:.1f} · errores {report['errors']}")
    for name, (length, secs, direct, direct_secs) in report["cross"].items():
        print(f"  {name}: sendero {length} casillas = {secs:.1f} s · atajo campo a través {direct:.0f} casillas = {direct_secs:.1f} s")
    ok &= not report["errors"] and report["walkable"] == report["reachable"]
    outputs = [("forest", data)]
    data, report = build_crypt()
    print(f"crypt: {data['width']}×{data['height']} · transitables {report['walkable']} · alcanzables {report['reachable']} · selladas {report['sealed']}"
          f" · errores {report['errors']}")
    woken, plants, lane = report["trap"]
    print(f"  Jardín de espinas: por el centro despiertan {woken} de {plants} plantas · eslalon de {lane:.2f} casillas en su paso más estrecho")
    cells, door = report["boss"]
    print(f"  sala del jefe: {cells} casillas libres, el jefe a {door:.0f} de la puerta")
    to_boss, to_exit, walk, normals, careful, cleared = report["run"]
    print(f"  recorrido: de la llegada al jefe {to_boss:.0f} casillas y del jefe a la salida {to_exit:.0f} = {walk:.0f} s a pie · {normals} monstruos en las salas"
          f" · estimado {careful / 60:.1f} min (eslalon) – {cleared / 60:.1f} min (limpiando las plantas)")
    ok &= not report["errors"] and report["walkable"] == report["reachable"]
    outputs.append(("crypt", data))
    if not check_only:
        for name, data in outputs:
            out = ROOT / "maps" / f"{name}.tmj"
            out.write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8", newline="\n")
            print(f"  escrito {out.relative_to(ROOT).as_posix()} ({out.stat().st_size // 1024} KB)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
