#!/usr/bin/env python3
"""Genera maps/forest.tmj (HU-111): el Bosque del Tier 2, con el Linde del Bosque (6–8) y el Pantano (8–10) en un solo mapa,
de forma determinista con el tileset placeholder y los mismos GIDs que el Tier 1 (el atlas del Bosque, `biome: forest`, le da
el aspecto). No toca meadow.tmj ni mine.tmj: regenerar el Tier 1 les cambia los fines de línea.

Uso: python tools/maps/gen_tier2_maps.py            (escribe maps/forest.tmj)
     python tools/maps/gen_tier2_maps.py --check    (solo valida y sale con código ≠ 0 si falla)

Cómo lo dibuja el cliente (TerrainBaker con terrain_forest.png): la roca unida al borde del mapa es bosque cerrado (muros
exteriores, la cresta entre zonas y las guaridas); la roca suelta y los bloques de muro fuera de una zona segura son peñascos; el
muro de una casilla de grosor es valla y, dentro de una zona segura, los bloques de muro son cabañas; `above` sin muro debajo
son copas de árbol; el agua es charca de pantano y el suelo interior (8), losas o el fondo de una cueva.
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
# HU-112: la Sala 3 de la Mina (mine.tmj, x 72..85 · y 16..35) tiene la hornacina tapiada en x 86..87 · y 25..27. El portal de
# vuelta del Linde deja en (85, 25): casilla libre de la Sala 3 junto a la hornacina y a más de 6 casillas del gólem (79, 28).
MINE_ROOM3_TARGET = (85, 25)
MINE_RETURN_PORTAL = (4, 28, 2, 3)  # en el fondo de la boca de la Mina, al oeste del Linde (x, y, ancho, alto)
MINE_ARRIVAL = (12, 29)             # donde debe dejar el portal de la Sala 3 (HU-112): en la boca, fuera de MINE_RETURN_PORTAL
# HU-113: lado alto del puente roto, en el borde oeste del Linde (el que da a las Colinas), sobre el río.
BRIDGE_DECK = (8, 88, 6, 3)         # tablero (x 8..13 · y 88..90); su extremo oeste da al río: ahí irá el paso a las Colinas
BRIDGE_CRANK = (15, 86)             # sitio de la manivela, junto al tablero
# HU-115: boca de la Cripta al fondo del Pantano (borde este). Sin objeto portal: el cargador exige que crypt.tmj exista.
CRYPT_ENTRANCE = (206, 73, 2, 3)    # rectángulo del portal (x 206..207 · y 73..75)
CRYPT_ARRIVAL = (201, 74)           # donde debe dejar el portal de vuelta de la Cripta

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

    # --- Pantano: boca de la Cripta (HU-115) en el borde este. Sin portal: solo el hueco con el fondo de losas.
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


def main() -> int:
    check_only = "--check" in sys.argv
    data, report = build_forest()
    print(f"forest: {data['width']}×{data['height']} · transitables {report['walkable']} · alcanzables {report['reachable']} · selladas {report['sealed']}"
          f" · margen de aggro {report['aggro_margin']:.1f} · errores {report['errors']}")
    for name, (length, secs, direct, direct_secs) in report["cross"].items():
        print(f"  {name}: sendero {length} casillas = {secs:.1f} s · atajo campo a través {direct:.0f} casillas = {direct_secs:.1f} s")
    ok = not report["errors"] and report["walkable"] == report["reachable"]
    if not check_only:
        out = ROOT / "maps" / "forest.tmj"
        out.write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8", newline="\n")
        print(f"  escrito {out.relative_to(ROOT).as_posix()} ({out.stat().st_size // 1024} KB)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
