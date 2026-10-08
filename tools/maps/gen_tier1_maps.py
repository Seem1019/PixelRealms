#!/usr/bin/env python3
"""Genera maps/meadow.tmj (HU-080) y maps/mine.tmj (HU-083) de forma determinista con el tileset placeholder.

Uso: python3 tools/maps/gen_tier1_maps.py            (escribe en maps/)
     python3 tools/maps/gen_tier1_maps.py --check    (solo valida conectividad y sale con código ≠ 0 si falla)

GIDs del tileset `placeholder.tsj`: 1 pasto · 2 tierra · 3 camino · 4 muro/árbol (sólido, tapa visión) · 5 arbusto (sólido)
· 6 agua (sólido) · 7 roca (sólido, tapa visión) · 8 suelo interior. La colisión del servidor y del cliente sale de `walls`.
Tras cambiar algo aquí: `dotnet test --filter Map` y `godot --headless -s tools/sync_content.gd`.
"""
from __future__ import annotations

import json
import pathlib
import random
import sys
from collections import deque

ROOT = pathlib.Path(__file__).resolve().parents[2]
TS = 16
GRASS, DIRT, PATH, WALL, BUSH, WATER, ROCK, FLOOR = 1, 2, 3, 4, 5, 6, 7, 8
SOLID = {WALL, BUSH, WATER, ROCK}


class Grid:
    def __init__(self, w: int, h: int, ground: int):
        self.w, self.h = w, h
        self.ground = [ground] * (w * h)
        self.detail = [0] * (w * h)
        self.walls = [0] * (w * h)
        self.above = [0] * (w * h)
        self.protected: set[tuple[int, int]] = set()  # casillas que ningún obstáculo aleatorio puede ocupar

    def i(self, x: int, y: int) -> int:
        return y * self.w + x

    def inside(self, x: int, y: int) -> bool:
        return 0 <= x < self.w and 0 <= y < self.h

    def solid(self, x: int, y: int) -> bool:
        return not self.inside(x, y) or self.walls[self.i(x, y)] in SOLID

    def fill_rect(self, layer: list[int], x0: int, y0: int, w: int, h: int, gid: int) -> None:
        for y in range(y0, y0 + h):
            for x in range(x0, x0 + w):
                if self.inside(x, y):
                    layer[self.i(x, y)] = gid

    def clear_rect(self, x0: int, y0: int, w: int, h: int, protect: bool = True, ground: int | None = None) -> None:
        for y in range(y0, y0 + h):
            for x in range(x0, x0 + w):
                if not self.inside(x, y):
                    continue
                self.walls[self.i(x, y)] = 0
                self.above[self.i(x, y)] = 0
                if ground is not None:
                    self.ground[self.i(x, y)] = ground
                if protect:
                    self.protected.add((x, y))

    def carve_path(self, points: list[tuple[int, int]], width: int, gid: int) -> None:
        """Polilínea en L (horizontal y luego vertical) de `width` casillas: suelo `gid`, sin obstáculos, protegida."""
        half = width // 2
        for (x0, y0), (x1, y1) in zip(points, points[1:]):
            sx = 1 if x1 >= x0 else -1
            for x in range(x0, x1 + sx, sx):
                self.clear_rect(x - half, y0 - half, width, width, ground=gid)
            sy = 1 if y1 >= y0 else -1
            for y in range(y0, y1 + sy, sy):
                self.clear_rect(x1 - half, y - half, width, width, ground=gid)

    def reachable_from(self, x: int, y: int) -> set[tuple[int, int]]:
        seen = {(x, y)}
        q = deque([(x, y)])
        while q:
            cx, cy = q.popleft()
            for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                if (nx, ny) not in seen and self.inside(nx, ny) and not self.solid(nx, ny):
                    seen.add((nx, ny))
                    q.append((nx, ny))
        return seen

    def seal_unreachable(self, origin: tuple[int, int], gid: int) -> int:
        """Rellena con `gid` las casillas transitables que no se alcanzan desde `origin` (CA4: sin huecos ni bolsas)."""
        reach = self.reachable_from(*origin)
        sealed = 0
        for y in range(self.h):
            for x in range(self.w):
                if not self.solid(x, y) and (x, y) not in reach:
                    self.walls[self.i(x, y)] = gid
                    sealed += 1
        return sealed


def prop(name: str, value, type_: str | None = None) -> dict:
    if type_ is None:
        type_ = {bool: "bool", int: "int", float: "float"}.get(type(value), "string")
    return {"name": name, "type": type_, "value": value}


def obj(oid: int, name: str, type_: str, x: int, y: int, w: int = 0, h: int = 0, props: list[dict] | None = None, point: bool = False) -> dict:
    o = {"id": oid, "name": name, "type": type_, "x": x * TS, "y": y * TS, "width": w * TS, "height": h * TS, "rotation": 0, "visible": True, "properties": props or []}
    if point:
        o["point"] = True
    return o


def tile_layer(lid: int, name: str, data: list[int], w: int, h: int, visible: bool = True) -> dict:
    return {"id": lid, "name": name, "type": "tilelayer", "width": w, "height": h, "x": 0, "y": 0, "opacity": 1, "visible": visible, "data": data}


def obj_layer(lid: int, name: str, objects: list[dict]) -> dict:
    return {"id": lid, "name": name, "type": "objectgroup", "draworder": "topdown", "x": 0, "y": 0, "opacity": 1, "visible": True, "objects": objects}


def tmj(g: Grid, map_id: str, display: str, default_gy: str, layers: list[dict], next_obj: int) -> dict:
    return {
        "compressionlevel": -1, "height": g.h, "width": g.w, "infinite": False, "orientation": "orthogonal", "renderorder": "right-down",
        "tiledversion": "1.11.2", "type": "map", "version": "1.10", "tileheight": TS, "tilewidth": TS, "nextlayerid": len(layers) + 1, "nextobjectid": next_obj,
        "properties": [prop("mapId", map_id), prop("displayName", display), prop("defaultGraveyard", default_gy)],
        "tilesets": [{"firstgid": 1, "source": "tilesets/placeholder.tsj"}, {"firstgid": 9, "source": "tilesets/collision.tsj"}],
        "layers": layers,
    }


# ----------------------------------------------------------------------------------------------------------------------------
# meadow (HU-080): Aldea Robledal (safe) · Campos 1–3 · Colinas 3–5 · portal a la Mina al final de las Colinas.
# ----------------------------------------------------------------------------------------------------------------------------

def build_meadow() -> tuple[dict, dict]:
    W, H = 250, 110
    g = Grid(W, H, GRASS)
    rng = random.Random(80)
    oid = 1
    spawns: list[dict] = []
    npcs: list[dict] = []
    graveyards: list[dict] = []
    zones: list[dict] = []
    portals: list[dict] = []

    def nid() -> int:
        nonlocal oid
        oid += 1
        return oid - 1

    # Bordes sólidos.
    g.fill_rect(g.walls, 0, 0, W, 2, ROCK); g.fill_rect(g.walls, 0, H - 2, W, 2, ROCK)
    g.fill_rect(g.walls, 0, 0, 2, H, ROCK); g.fill_rect(g.walls, W - 2, 0, 2, H, ROCK)

    # --- Aldea Robledal: x 2..44, y 30..80 (safe). Valla con puerta este en y 52..56.
    VX0, VY0, VX1, VY1 = 2, 30, 44, 80
    g.fill_rect(g.walls, VX0, VY0, VX1 - VX0, 1, WALL); g.fill_rect(g.walls, VX0, VY1 - 1, VX1 - VX0, 1, WALL)
    g.fill_rect(g.walls, VX1 - 1, VY0, 1, VY1 - VY0, WALL)
    g.fill_rect(g.walls, VX1 - 1, 52, 1, 4, 0)  # puerta
    g.clear_rect(VX0 + 1, VY0 + 1, VX1 - VX0 - 2, VY1 - VY0 - 2, protect=True)
    g.fill_rect(g.ground, VX0 + 1, VY0 + 1, VX1 - VX0 - 2, VY1 - VY0 - 2, DIRT)
    # Plaza con pozo (landmark) y casas.
    g.fill_rect(g.ground, 12, 44, 22, 20, PATH)
    g.fill_rect(g.walls, 22, 53, 2, 2, WATER)  # Pozo de la plaza
    for hx, hy in ((6, 34), (16, 34), (28, 34), (6, 68), (18, 70), (30, 68), (6, 50)):
        g.fill_rect(g.walls, hx, hy, 5, 4, WALL)
        g.fill_rect(g.above, hx, hy - 1, 5, 1, BUSH)  # "tejado"
    npcs.append(obj(nid(), "Marta la tendera", "npc", 17, 48, props=[prop("vendorId", "robledal_general_goods"), prop("name", "Marta la tendera")], point=True))
    npcs.append(obj(nid(), "class_master", "npc", 28, 48, props=[prop("name", "Maestro Aldo"), prop("kind", "class_change")], point=True))
    graveyards.append(obj(nid(), "gy_village", "graveyard", 23, 60, point=True))
    zones.append(obj(nid(), "Aldea Robledal", "zone", VX0, VY0, VX1 - VX0, VY1 - VY0,
                     [prop("name", "Aldea Robledal"), prop("safe", True), prop("minLevel", 1), prop("maxLevel", 6), prop("landmark", "Pozo de la plaza")]))

    # --- Campos: x 44..148, y 2..108 (nv 1–3). Cresta rocosa hacia las Colinas en x 146..148 con paso en y 52..56.
    CX0, CX1 = 44, 148
    g.fill_rect(g.walls, 146, 0, 2, H, ROCK); g.fill_rect(g.walls, 146, 52, 2, 4, 0)
    zones.append(obj(nid(), "Campos", "zone", CX0, 2, CX1 - CX0, H - 4,
                     [prop("name", "Campos"), prop("safe", False), prop("minLevel", 1), prop("maxLevel", 3), prop("landmark", "Molino")]))
    # Molino: bloque de roca con aspas de arbustos, visible desde el camino.
    g.fill_rect(g.walls, 100, 26, 4, 4, ROCK); g.fill_rect(g.above, 98, 24, 8, 1, BUSH); g.fill_rect(g.above, 101, 22, 2, 8, BUSH)
    g.protected.update((x, y) for x in range(96, 108) for y in range(22, 32))
    graveyards.append(obj(nid(), "gy_fields", "graveyard", 96, 58, point=True))

    # --- Colinas: x 148..248, y 2..108 (nv 3–5). Suelo más terroso.
    HX0, HX1 = 148, 248
    g.fill_rect(g.ground, HX0, 2, HX1 - HX0, H - 4, DIRT)
    zones.append(obj(nid(), "Colinas", "zone", HX0, 2, HX1 - HX0, H - 4,
                     [prop("name", "Colinas"), prop("safe", False), prop("minLevel", 3), prop("maxLevel", 5), prop("landmark", "Roble centenario")]))
    # Roble centenario: tronco de muro con copa grande en `above`.
    g.fill_rect(g.walls, 196, 34, 3, 3, WALL); g.fill_rect(g.above, 192, 30, 11, 9, BUSH)
    g.protected.update((x, y) for x in range(190, 206) for y in range(28, 42))
    graveyards.append(obj(nid(), "gy_hills", "graveyard", 198, 60, point=True))
    # Portal a la Mina al final de las Colinas (boca de mina: rocas alrededor).
    g.fill_rect(g.walls, 238, 48, 8, 3, ROCK); g.fill_rect(g.walls, 238, 57, 8, 3, ROCK); g.fill_rect(g.walls, 244, 51, 2, 6, ROCK)
    g.clear_rect(236, 51, 8, 6, ground=DIRT)
    portals.append(obj(nid(), "to_mine", "portal", 242, 52, 2, 3, [prop("portalId", "meadow_to_mine"), prop("targetMapId", "mine"), prop("targetX", 11), prop("targetY", 28), prop("minLevel", 4)]))

    # --- Sendero principal (3 de ancho) con curvas: puerta de la aldea → paso a las Colinas → boca de la Mina.
    main = [(40, 54), (60, 54), (70, 40), (92, 40), (92, 62), (130, 62), (140, 54), (160, 54), (172, 72), (200, 72), (214, 46), (236, 46), (236, 54), (241, 54)]
    g.carve_path(main, 3, PATH)
    # Ramales de tierra (2 de ancho) a los puntos seguros y a los campamentos.
    def branch(a: tuple[int, int], b: tuple[int, int]) -> None:
        g.carve_path([a, b], 2, PATH if b[0] >= HX0 else DIRT)  # en las Colinas el suelo ya es tierra: el ramal va en camino

    # --- Campamentos (subniveles: los más bajos cerca de la entrada de cada zona). count ≤ 10 por objeto.
    camps = [
        # Campos: slimes (nv 1) al oeste, jabalíes (nv 2) al centro, bandidos (nv 3) al este + escondite (rama lateral con recompensa).
        ("slimes_west", "slime", 7, 3.0, (50, 36, 12, 10)), ("slimes_pond", "slime", 7, 3.0, (52, 66, 12, 10)), ("slimes_road", "slime", 7, 3.0, (68, 48, 10, 10)),
        ("boars_north", "boar", 7, 4.0, (80, 16, 14, 10)), ("boars_south", "boar", 7, 4.0, (84, 82, 14, 10)), ("boars_mill", "boar", 7, 4.0, (108, 44, 12, 10)),
        ("bandits_east", "bandit", 8, 4.0, (122, 30, 14, 10)), ("bandits_south", "bandit", 8, 4.0, (124, 76, 14, 10)), ("bandit_hideout", "bandit", 4, 2.0, (130, 6, 10, 8)),
        # Colinas: lobos (nv 4) primero, goblins (nv 5, hard) hacia la Mina + atalaya goblin (rama lateral).
        ("wolves_pass", "wolf", 7, 4.0, (156, 32, 14, 10)), ("wolves_den", "wolf", 7, 4.0, (158, 80, 14, 10)), ("wolves_oak", "wolf", 7, 4.0, (180, 50, 12, 10)),
        ("goblins_north", "goblin_archer", 8, 3.0, (208, 24, 14, 10)), ("goblins_south", "goblin_archer", 8, 3.0, (212, 80, 14, 10)), ("goblin_lookout", "goblin_archer", 4, 2.0, (222, 6, 10, 8)),
    ]
    for name, monster, count, wander, (x, y, w, h) in camps:
        g.clear_rect(x - 1, y - 1, w + 2, h + 2, ground=None)
        spawns.append(obj(nid(), name, "spawn", x, y, w, h, [prop("monsterId", monster), prop("count", count), prop("wanderRadius", wander, "float")]))
    # Ramales: cada campamento y cementerio conecta con el sendero por el punto del camino más cercano.
    path_cells = [(x, y) for (x, y) in g.protected if g.ground[g.i(x, y)] == PATH and x > 44]
    def nearest_path(px: int, py: int) -> tuple[int, int]:
        return min(path_cells, key=lambda c: abs(c[0] - px) + abs(c[1] - py))
    for _, _, _, _, (x, y, w, h) in camps:
        cx, cy = x + w // 2, y + h // 2
        branch(nearest_path(cx, cy), (cx, cy))
    for gy in graveyards[1:]:
        gx, gy_ = gy["x"] // TS, gy["y"] // TS
        branch(nearest_path(gx, gy_), (gx, gy_))
        g.clear_rect(gx - 2, gy_ - 2, 5, 5, ground=PATH)
    # Protección de puntos: NPCs y cementerio de la aldea.
    for o in npcs + graveyards[:1]:
        g.clear_rect(o["x"] // TS - 1, o["y"] // TS - 1, 3, 3)

    # --- Obstáculos aleatorios (bosquecillos, arbustos, charcas) fuera de lo protegido.
    def free(x: int, y: int, w: int, h: int) -> bool:
        for yy in range(y - 1, y + h + 1):
            for xx in range(x - 1, x + w + 1):
                if not g.inside(xx, yy) or (xx, yy) in g.protected or xx < 44 or g.walls[g.i(xx, yy)] != 0:
                    return False
        return True

    for _ in range(900):
        kind = rng.random()
        if kind < 0.45:   # bosquecillo
            w, h = rng.randint(2, 5), rng.randint(2, 4)
            x, y = rng.randint(46, W - 4 - w), rng.randint(3, H - 4 - h)
            if free(x, y, w, h):
                g.fill_rect(g.walls, x, y, w, h, WALL); g.fill_rect(g.above, x, y - 1, w, 1, BUSH)
        elif kind < 0.8:  # arbustos
            w, h = rng.randint(1, 3), rng.randint(1, 2)
            x, y = rng.randint(46, W - 4 - w), rng.randint(3, H - 4 - h)
            if free(x, y, w, h):
                g.fill_rect(g.walls, x, y, w, h, BUSH)
        else:             # charca
            w, h = rng.randint(2, 4), rng.randint(2, 3)
            x, y = rng.randint(46, W - 4 - w), rng.randint(3, H - 4 - h)
            if free(x, y, w, h):
                g.fill_rect(g.walls, x, y, w, h, WATER)
    # Rocas en las Colinas para que se sientan distintas.
    for _ in range(140):
        w, h = rng.randint(1, 3), rng.randint(1, 3)
        x, y = rng.randint(HX0 + 2, HX1 - 2 - w), rng.randint(3, H - 4 - h)
        if free(x, y, w, h):
            g.fill_rect(g.walls, x, y, w, h, ROCK)
    # Detalle: florecillas (tierra) en el pasto.
    for _ in range(600):
        x, y = rng.randint(3, W - 4), rng.randint(3, H - 4)
        if g.walls[g.i(x, y)] == 0 and g.ground[g.i(x, y)] == GRASS:
            g.detail[g.i(x, y)] = DIRT

    # Recompensa de las ramas laterales (HU-080 CA1): un élite en el centro del escondite de bandidos (Campos) y otro en la
    # atalaya goblin (Colinas), con sus guardias alrededor. Van al final para no mover los ids ni el azar del resto del mapa.
    spawns.append(obj(nid(), "war_boar", "spawn", 135, 10, 0, 0, [prop("monsterId", "boar_alpha"), prop("count", 1), prop("wanderRadius", 1.0, "float")], point=True))
    spawns.append(obj(nid(), "watchtower_warg", "spawn", 227, 10, 0, 0, [prop("monsterId", "wolf_alpha"), prop("count", 1), prop("wanderRadius", 1.0, "float")], point=True))

    origin = (graveyards[0]["x"] // TS, graveyards[0]["y"] // TS)
    sealed = g.seal_unreachable(origin, WALL)
    report = check_map(g, origin, spawns, graveyards, npcs, portals)
    report["sealed"] = sealed
    layers = [
        tile_layer(1, "ground", g.ground, W, H), tile_layer(2, "detail", g.detail, W, H), tile_layer(3, "walls", g.walls, W, H), tile_layer(4, "above", g.above, W, H),
        obj_layer(5, "spawns", spawns), obj_layer(6, "npcs", npcs), obj_layer(7, "graveyards", graveyards), obj_layer(8, "zones", zones), obj_layer(9, "portals", portals),
    ]
    return tmj(g, "meadow", "Pradera de Robledal", "gy_village", layers, oid), report


# ----------------------------------------------------------------------------------------------------------------------------
# mine (HU-083): entrada → Sala 1 (kóbolds) → Sala 2 (kóbolds + palancas) → rama lateral tras una puerta: sala del jefe · Sala 3
# (gólem élite) → salida al Bosque, cerrada hasta la Fase 2 (HU-112).
# ----------------------------------------------------------------------------------------------------------------------------

def build_mine() -> tuple[dict, dict]:
    W, H = 90, 60
    g = Grid(W, H, FLOOR)
    g.fill_rect(g.walls, 0, 0, W, H, ROCK)  # todo roca; se excavan salas y pasillos
    rng = random.Random(83)
    oid = 1

    def nid() -> int:
        nonlocal oid
        oid += 1
        return oid - 1

    def room(x: int, y: int, w: int, h: int) -> None:
        g.clear_rect(x, y, w, h, protect=True)
        # Bordes irregulares: algunas esquinas vuelven a ser roca.
        for _ in range(w * h // 12):
            rx, ry = rng.choice((x, x + w - 1)), rng.randint(y, y + h - 1)
            if rng.random() < 0.5:
                rx, ry = rng.randint(x, x + w - 1), rng.choice((y, y + h - 1))
            g.walls[g.i(rx, ry)] = ROCK

    def corridor(points: list[tuple[int, int]]) -> None:
        g.carve_path(points, 3, FLOOR)

    entrance = (6, 24, 12, 10)      # Entrada: portal de vuelta + punto seguro
    room1 = (22, 18, 18, 22)        # Sala 1: kóbolds
    room2 = (46, 10, 22, 26)        # Sala 2: kóbolds + dos palancas que abren la puerta de la rama del jefe
    boss = (46, 42, 26, 15)         # Sala del jefe (rama lateral al sur de la Sala 2)
    room3 = (72, 16, 14, 20)        # Sala 3: gólem élite → salida al Tier 2 (cerrada hasta la Fase 2)
    for r in (entrance, room1, room2, boss, room3):
        room(*r)
    corridor([(17, 29), (22, 29)])
    corridor([(39, 29), (46, 29)])
    corridor([(57, 35), (57, 42)])
    corridor([(67, 26), (72, 26)])
    # Pilares de la Sala 2 (obstáculos entre las dos palancas).
    for px, py in ((52, 16), (60, 16), (52, 28), (60, 28)):
        g.fill_rect(g.walls, px, py, 2, 2, ROCK)
    # Salida al Tier 2: hornacina al este de la Sala 3 (su portal y su dibujo, más abajo: HU-112).
    g.clear_rect(86, 25, 2, 3, ground=FLOOR); g.fill_rect(g.walls, 88, 24, 2, 5, ROCK)
    # Vagonetas/escombros decorativos (arbustos = bultos) y charcos.
    for _ in range(60):
        x, y = rng.randint(2, W - 3), rng.randint(2, H - 3)
        if g.walls[g.i(x, y)] == 0 and (x, y) not in {(11, 28), (11, 31)} and rng.random() < 0.5:
            g.detail[g.i(x, y)] = DIRT
    # Linterna del Capataz (landmark): charco de luz = agua decorativa dentro de la sala del jefe (esquina, no bloquea el paso).
    g.fill_rect(g.walls, 70, 55, 1, 1, WATER)

    spawns = [
        obj(nid(), "kobolds_1", "spawn", 24, 20, 14, 18, [prop("monsterId", "kobold_miner"), prop("count", 6), prop("wanderRadius", 2.0, "float")]),
        obj(nid(), "kobolds_2", "spawn", 48, 12, 18, 22, [prop("monsterId", "kobold_miner"), prop("count", 6), prop("wanderRadius", 2.0, "float")]),
        obj(nid(), "kobolds_3", "spawn", 74, 18, 10, 6, [prop("monsterId", "kobold_miner"), prop("count", 3), prop("wanderRadius", 2.0, "float")]),
        obj(nid(), "golem", "spawn", 79, 28, 0, 0, [prop("monsterId", "rubble_golem"), prop("count", 1), prop("wanderRadius", 1.0, "float")], point=True),
        obj(nid(), "foreman", "spawn", 59, 50, 0, 0, [prop("monsterId", "foreman_grask"), prop("count", 1), prop("wanderRadius", 1.0, "float")], point=True),
    ]
    for s in spawns:
        g.clear_rect(s["x"] // TS - 1, s["y"] // TS - 1, max(1, s["width"] // TS) + 2, max(1, s["height"] // TS) + 2)
    # HU-112: la hornacina es la salida al Linde del Bosque, una boca de galería derrumbada al otro lado del gólem. Se dibuja aquí,
    # después de los escombros y los spawns, para no cambiar la secuencia del rng ni lo que despejan los spawns: sin el peñasco
    # que tapaba la boca, con la roca adelantada a los lados y piedras del derrumbe caídas delante. (85, 24) queda libre: la caja de
    # los pies de quien llega del Bosque a (85, 25) la toca.
    g.walls[g.i(85, 26)] = 0
    for bx, by in ((85, 23), (85, 28), (82, 29), (84, 31)):
        g.walls[g.i(bx, by)] = ROCK
    graveyards = [obj(nid(), "gy_entrance", "graveyard", 11, 31, point=True)]
    portals = [obj(nid(), "to_meadow", "portal", 9, 27, 2, 3, [prop("portalId", "mine_to_meadow"), prop("targetMapId", "meadow"), prop("targetX", 238), prop("targetY", 54)])]
    zones = [obj(nid(), "Mina Abandonada", "zone", 1, 1, W - 2, H - 2,
                 [prop("name", "Mina Abandonada"), prop("safe", False), prop("minLevel", 4), prop("maxLevel", 6), prop("landmark", "Linterna del Capataz")])]
    # Puzle de la Sala 2 (HU-083 CA1): una palanca a cada lado de la boca del pasillo que baja a la sala del jefe (x 56..58) y la
    # puerta justo en esa boca, para que se vean juntas en pantalla (con las palancas lejos, en la fila 22, nadie veía la puerta);
    # con las dos activadas se abre y se cierra sola a los `rules.world.doorResetSec`. La puerta cubre el pasillo de pared a pared.
    # Del lado del jefe hay una tercera palanca que abre sola (`opensAlone`): quien se quede dentro al cerrarse puede salir.
    levers = [
        obj(nid(), "lever_west", "lever", 51.5, 32.5, props=[prop("leverId", "mine_lever_west"), prop("doorId", "mine_boss_door")], point=True),
        obj(nid(), "lever_east", "lever", 62.5, 32.5, props=[prop("leverId", "mine_lever_east"), prop("doorId", "mine_boss_door")], point=True),
        obj(nid(), "lever_inside", "lever", 57.5, 40.5, props=[prop("leverId", "mine_lever_inside"), prop("doorId", "mine_boss_door"), prop("opensAlone", True)], point=True),
    ]
    doors = [obj(nid(), "boss_door", "door", 55, 36, 5, 1, [prop("doorId", "mine_boss_door")])]
    # HU-112: la salida de la hornacina, cerrada hasta la Fase 2 (minPhase), deja en la llegada reservada del Linde (MINE_ARRIVAL
    # de gen_tier2_maps.py). Es el último objeto para no renumerar los demás.
    portals.append(obj(nid(), "to_forest", "portal", 86, 25, 2, 3, [prop("portalId", "mine_to_forest"), prop("targetMapId", "forest"), prop("targetX", 12), prop("targetY", 29),
                                                                    prop("minPhase", 2), prop("lockedText", "El derrumbe aún bloquea el paso")]))
    origin = (11, 31)
    sealed = g.seal_unreachable(origin, ROCK)
    report = check_map(g, origin, spawns, graveyards, [], portals)
    report["sealed"] = sealed
    reach = g.reachable_from(*origin)
    for lv in levers:
        if (int(lv["x"]) // TS, int(lv["y"]) // TS) not in reach:
            report["errors"].append(f"palanca {lv['name']} inaccesible")
    layers = [
        tile_layer(1, "ground", g.ground, W, H), tile_layer(2, "detail", g.detail, W, H), tile_layer(3, "walls", g.walls, W, H), tile_layer(4, "above", g.above, W, H),
        obj_layer(5, "spawns", spawns), obj_layer(6, "npcs", []), obj_layer(7, "graveyards", graveyards), obj_layer(8, "zones", zones), obj_layer(9, "portals", portals),
        obj_layer(10, "levers", levers), obj_layer(11, "doors", doors),
    ]
    return tmj(g, "mine", "Mina Abandonada", "gy_entrance", layers, oid), report


def check_map(g: Grid, origin: tuple[int, int], spawns, graveyards, npcs, portals) -> dict:
    reach = g.reachable_from(*origin)
    walkable = sum(1 for y in range(g.h) for x in range(g.w) if not g.solid(x, y))
    errors: list[str] = []
    for o in graveyards + npcs:
        if (o["x"] // TS, o["y"] // TS) not in reach:
            errors.append(f"{o['name']} inaccesible")
    for p in portals:
        cells = [(x, y) for x in range(p["x"] // TS, (p["x"] + p["width"]) // TS) for y in range(p["y"] // TS, (p["y"] + p["height"]) // TS)]
        if not any(c in reach for c in cells):
            errors.append(f"portal {p['name']} inaccesible")
    for s in spawns:
        w, h = max(1, s["width"] // TS), max(1, s["height"] // TS)
        cells = [(x, y) for x in range(s["x"] // TS, s["x"] // TS + w) for y in range(s["y"] // TS, s["y"] // TS + h)]
        count = next(pr["value"] for pr in s["properties"] if pr["name"] == "count")
        free = sum(1 for c in cells if c in reach)
        if free < count:
            errors.append(f"spawn {s['name']}: {free} casillas libres para {count} monstruos")
    return {"walkable": walkable, "reachable": len(reach), "errors": errors}


def main() -> int:
    check_only = "--check" in sys.argv
    ok = True
    for name, builder in (("meadow", build_meadow), ("mine", build_mine)):
        data, report = builder()
        print(f"{name}: {data['width']}×{data['height']} · transitables {report['walkable']} · alcanzables {report['reachable']} · selladas {report['sealed']} · errores {report['errors']}")
        if report["errors"] or report["walkable"] != report["reachable"]:
            ok = False
        if not check_only:
            out = ROOT / "maps" / f"{name}.tmj"
            out.write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
            print(f"  → {out.relative_to(ROOT)} ({out.stat().st_size // 1024} KB)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
