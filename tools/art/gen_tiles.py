"""Tileset del mundo: client/assets/tiles/terrain.png (16 columnas × 16 px).

Filas (ver client/scripts/world/terrain_baker.gd, que debe coincidir):
  0  pasto: 8 variantes base + 8 decoraciones (matas, flores, piedrita, seta…)
  1  tierra: 4 variantes + 4 decoraciones · suelo de cueva: 4 variantes + 4 decoraciones
  2..9  terrenos "dual-grid" (16 combinaciones de esquinas, bit tl=1 tr=2 bl=4 br=8):
        2 tierra · 3 camino · 4 agua con orilla de piedras · 5 roca (peñascos) · 6 bosque · 7 arbusto · 8 copa de árbol
        9 roca de cueva
  10 cerca de madera (16 combinaciones de vecinos, bit N=1 E=2 S=4 W=8)
  11 casa: tejado (12) + alero
  12 casa: paredes, puerta, ventanas · ruinas
  13 tronco, sombra de copa, tocón, tronco caído, cartel, farol, pozo, fogata, escalera de mina
  14 camino: 4 variantes interiores · 14-15 copas de árbol de 32×32 (2 claras, 2 oscuras de bosque) y pozo de 32×32
"""
from __future__ import annotations

import math
import random
import zlib

import numpy as np

from pix import (P, PeriodicNoise, canvas, ellipse, hash01, hline, line, outline, put, rect, rgba, save, vline)

T = 16
COLS = 16
ROWS = 16


def tile() -> np.ndarray:
    return canvas(T, T)


def blit(atlas: np.ndarray, t: np.ndarray, col: int, row: int) -> None:
    atlas[row * T:(row + 1) * T, col * T:(col + 1) * T] = t


# --- Texturas base ------------------------------------------------------------------------------------------------------

GRASS = ["sage", "sage_d", "sage_l", "moss", "olive", "green", "leaf"]


def grass_base(seed: int, tone: str = "normal") -> np.ndarray:
    """Pasto tranquilo: base plana con briznas sueltas (claras arriba, oscuras abajo). `tone`: normal, light o dark para
    las manchas grandes que elige el horneado con ruido de baja frecuencia."""
    rnd = random.Random(seed)
    base, light, dark, warm = "sage", "sage_l", "sage_d", "moss"
    blades = {"normal": 7, "light": 4, "dark": 12}[tone]
    if tone == "light":
        light = "moss"
    t = tile()
    t[:, :] = rgba(base)
    for _ in range(blades):
        x, y = rnd.randrange(T), rnd.randrange(1, T - 1)
        put(t, x, y, dark)
        put(t, (x + 1) % T, y, dark)
        put(t, x, y - 1, light)
    for _ in range(4):
        x, y = rnd.randrange(T), rnd.randrange(T)
        put(t, x, y, warm)
    for _ in range(3):
        x, y = rnd.randrange(T), rnd.randrange(T)
        put(t, x, y, light)
    return t


def grass_tuft(seed: int, big: bool) -> np.ndarray:
    rnd = random.Random(seed)
    t = tile()
    cx, base = 8, 12
    blades = 5 if big else 3
    for i in range(blades):
        x = cx - blades // 2 + i + rnd.choice([-1, 0, 0, 1])
        h = rnd.randint(3, 6 if big else 4)
        lean = rnd.choice([-1, 0, 1])
        for k in range(h):
            xx = x + (lean if k > h // 2 else 0)
            put(t, xx, base - k, "leaf" if k == h - 1 else ("moss" if k > h // 2 else "sage_l"))
        put(t, x, base + 1, "sage_d")
    return t


def flowers(seed: int, petal: str, centre: str) -> np.ndarray:
    rnd = random.Random(seed)
    t = tile()
    for _ in range(3):
        x, y = rnd.randint(3, 12), rnd.randint(4, 12)
        put(t, x, y + 1, "sage_d")
        put(t, x, y + 2, "green")
        for dx, dy in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
            put(t, x + dx, y + dy, petal)
        put(t, x, y, centre)
    return t


def pebble(seed: int, on: str = "grass") -> np.ndarray:
    rnd = random.Random(seed)
    t = tile()
    for _ in range(2 if on == "grass" else 4):
        x, y = rnd.randint(3, 12), rnd.randint(4, 12)
        w = rnd.choice([2, 3])
        rect(t, x, y, w, 2, "silver")
        hline(t, x, x + w - 1, y + 2, "lavgray" if on == "grass" else "mud")
        put(t, x, y, "mist")
    return t


def mushroom() -> np.ndarray:
    t = tile()
    for (x, y) in [(6, 9), (10, 11)]:
        vline(t, x, y, y + 2, "cream")
        hline(t, x - 2, x + 2, y, "red")
        hline(t, x - 1, x + 1, y - 1, "redl")
        put(t, x - 1, y - 1, "white")
        put(t, x + 1, y, "white")
        hline(t, x - 1, x + 1, y + 3, "sage_d")
    return t


def clover(seed: int) -> np.ndarray:
    rnd = random.Random(seed)
    t = tile()
    for _ in range(4):
        x, y = rnd.randint(3, 12), rnd.randint(3, 12)
        put(t, x, y, "green")
        put(t, x + 1, y, "jade")
        put(t, x, y + 1, "green")
        put(t, x + 1, y + 1, "sage_d")
    return t


def dirt_speck(seed: int) -> np.ndarray:
    rnd = random.Random(seed)
    t = tile()
    for _ in range(6):
        x, y = rnd.randint(3, 12), rnd.randint(3, 12)
        put(t, x, y, "taupe")
        put(t, x + 1, y, "dusty")
    ellipse(t, 8, 9, 3, 2, "taupe")
    put(t, 7, 8, "sand")
    return t


DIRT_BASE, DIRT_LIGHT, DIRT_DARK = "taupe", "sage_p", "dusty"


def dirt_texture(t: np.ndarray, seed: int, region: np.ndarray | None = None, base=DIRT_BASE, light=DIRT_LIGHT, dark=DIRT_DARK) -> None:
    """Tierra: base plana con motas claras (piedrecitas iluminadas arriba-izquierda) y algún hoyito oscuro."""
    rnd = random.Random(seed)
    for y in range(T):
        for x in range(T):
            if region is None or region[y, x]:
                put(t, x, y, base)
    for _ in range(6):
        x, y = rnd.randrange(T), rnd.randrange(T)
        if region is None or region[y, x]:
            put(t, x, y, light)
            if region is None or region[(y + 1) % T, x]:
                put(t, x, (y + 1) % T, dark)
    for _ in range(4):
        x, y = rnd.randrange(T), rnd.randrange(T)
        if region is None or region[y, x]:
            put(t, x, y, dark)


def dirt_base(seed: int) -> np.ndarray:
    t = tile()
    dirt_texture(t, seed)
    return t


def dry_tuft(seed: int) -> np.ndarray:
    rnd = random.Random(seed)
    t = tile()
    for i in range(4):
        x = 6 + i + rnd.choice([0, 1])
        h = rnd.randint(2, 4)
        for k in range(h):
            put(t, x, 12 - k, "moss" if k == h - 1 else "olive")
    return t


def crack() -> np.ndarray:
    t = tile()
    pts = [(3, 6), (6, 8), (8, 7), (11, 10), (13, 9)]
    for (a, b) in zip(pts, pts[1:]):
        line(t, a[0], a[1], b[0], b[1], "mauve")
    put(t, 8, 8, "plum")
    return t


FLOOR_BASE, FLOOR_LIGHT, FLOOR_DARK = "mauve", "dusty", "ink"


def floor_base(seed: int) -> np.ndarray:
    t = tile()
    dirt_texture(t, seed, None, FLOOR_BASE, FLOOR_LIGHT, FLOOR_DARK)
    return t


def bones() -> np.ndarray:
    t = tile()
    line(t, 4, 10, 10, 7, "mist")
    for (x, y) in [(4, 10), (10, 7)]:
        put(t, x - 1, y, "mist")
        put(t, x, y + 1, "mist")
    line(t, 5, 11, 11, 8, "silver")
    return t


def crystal() -> np.ndarray:
    t = tile()
    for (x, h, c1, c2) in [(6, 5, "aqua_l", "teal"), (9, 7, "foam", "aqua"), (11, 4, "aqua_l", "teal")]:
        for k in range(h):
            put(t, x, 12 - k, c1 if k < h - 1 else "white")
            put(t, x + 1, 12 - k, c2)
    hline(t, 5, 12, 13, "ink")
    return outline(t)


def puddle() -> np.ndarray:
    t = tile()
    ellipse(t, 8, 9, 5, 2.5, "navy")
    ellipse(t, 7, 8.5, 3, 1.5, "indigo")
    put(t, 6, 8, "silver")
    return t


# --- Dual-grid -------------------------------------------------------------------------------------------------------

def corner_field(cfg: int, noise: PeriodicNoise, amp: float):
    tl, tr, bl, br = (cfg >> 0) & 1, (cfg >> 1) & 1, (cfg >> 2) & 1, (cfg >> 3) & 1
    f = np.zeros((T, T))
    for y in range(T):
        for x in range(T):
            u, v = (x + 0.5) / T, (y + 0.5) / T
            top = tl * (1 - u) + tr * u
            bot = bl * (1 - u) + br * u
            f[y, x] = top * (1 - v) + bot * v + amp * (noise(x, y) - 0.5)
    return f


def dual_tiles(seed: int, amp: float, painter) -> list[np.ndarray]:
    noise = PeriodicNoise(seed, 16, 4)
    out = []
    for cfg in range(16):
        t = tile()
        if cfg:
            f = corner_field(cfg, noise, amp)
            m = f > 0.5
            painter(t, m, f, cfg)
        out.append(t)
    return out


def edge_masks(m: np.ndarray):
    """Píxeles del borde: con un vecino fuera de la máscara arriba/izquierda (luz) o abajo/derecha (sombra)."""
    up = np.zeros_like(m); up[1:] = m[:-1]; up[0] = m[0]
    dn = np.zeros_like(m); dn[:-1] = m[1:]; dn[-1] = m[-1]
    lf = np.zeros_like(m); lf[:, 1:] = m[:, :-1]; lf[:, 0] = m[:, 0]
    rt = np.zeros_like(m); rt[:, :-1] = m[:, 1:]; rt[:, -1] = m[:, -1]
    top_left = m & (~up | ~lf)
    bottom_right = m & (~dn | ~rt)
    return top_left, bottom_right


def paint_dirt(t, m, f, cfg):
    dirt_texture(t, 11, m)
    tl, br = edge_masks(m)
    t[br] = rgba("taupe")
    t[tl & (f < 0.56)] = rgba("sage_d")  # pasto que se mete sobre el borde superior


def paint_road(t, m, f, cfg):
    dirt_texture(t, 23, m, "sage_p", "pale", "taupe")
    # Bordes un poco más oscuros e irregulares, con huellas.
    tl, br = edge_masks(m)
    t[br] = rgba("taupe")
    t[tl & (f < 0.56)] = rgba("sage_d")


def paint_water(t, m, f, cfg):
    shore = m & (f < 0.6)
    deep = m & (f >= 0.6)
    rnd = random.Random(cfg * 31 + 5)
    for y in range(T):
        for x in range(T):
            if deep[y, x]:
                put(t, x, y, "teal_d" if f[y, x] > 0.85 else "teal")
            elif shore[y, x]:
                # Orilla de piedras: manchas de 2×2 en gris claro con sombra.
                h = hash01(x // 2, y // 2, 7)
                c = "silver" if h > 0.55 else ("lavgray" if h > 0.25 else "taupe")
                put(t, x, y, c)
                if (x % 2 == 0) and (y % 2 == 0) and h > 0.8:
                    put(t, x, y, "mist")
    # Brillos del agua: rayitas claras horizontales.
    for _ in range(3):
        x, y = rnd.randint(2, 11), rnd.randint(2, 13)
        if deep[y, x] and deep[y, min(x + 2, 15)]:
            hline(t, x, x + 2, y, "aqua")
            put(t, x + 1, y, "foam")
    # Línea de espuma entre orilla y agua.
    for y in range(T):
        for x in range(T):
            if deep[y, x] and ((y > 0 and shore[y - 1, x]) or (x > 0 and shore[y, x - 1])):
                put(t, x, y, "aqua")
    tl, br = edge_masks(m)
    t[br & shore] = rgba("plum")


def near_edge(m: np.ndarray, dx: int, dy: int, dist: int) -> np.ndarray:
    """Píxeles de la máscara a ≤ `dist` px del borde en la dirección (dx, dy)."""
    out = np.zeros_like(m)
    for k in range(1, dist + 1):
        sh = np.ones_like(m)
        ys0, ys1 = max(0, -dy * k), T - max(0, dy * k)
        xs0, xs1 = max(0, -dx * k), T - max(0, dx * k)
        sh[:, :] = True
        sh[ys0:ys1, xs0:xs1] = m[ys0 + dy * k:ys1 + dy * k, xs0 + dx * k:xs1 + dx * k]
        out |= m & ~sh
    return out


def paint_rock(t, m, f, cfg):
    # Facetas de 4×3 px en tres tonos (más claras hacia arriba-izquierda) y bandas de luz/sombra junto al borde.
    noise = PeriodicNoise(cfg + 77, 16, 4)
    for y in range(T):
        for x in range(T):
            if m[y, x]:
                n = noise(x, y)
                put(t, x, y, "mist" if n > 0.72 else ("silver" if n > 0.33 else "lavgray"))
    for (x, y) in [(3, 5), (9, 3), (12, 10), (5, 11)]:
        if m[y, x] and m[y + 1, x + 1] and m[y + 1, x]:
            put(t, x, y, "plum")
            put(t, x + 1, y + 1, "plum")
            put(t, x, y + 1, "mist")
    light = near_edge(m, 0, -1, 2) | near_edge(m, -1, 0, 1)
    dark = near_edge(m, 0, 1, 3) | near_edge(m, 1, 0, 2)
    t[light] = rgba("mist")
    t[dark] = rgba("lavgray")
    tl, br = edge_masks(m)
    t[br] = rgba("plum")


def leafy(t, m, f, base, light, dark, deep, seed, highlight=None):
    """Follaje de racimos redondos (periodo 16 px para que case entre casillas): cada racimo con luz arriba-izquierda y
    sombra abajo-derecha; se recorta por la máscara del terreno."""
    rnd = random.Random(seed)
    layer = tile()
    centres = []
    for gy in range(3):
        for gx in range(3):
            cx = gx * 6 + rnd.randint(0, 3) - 1
            cy = gy * 6 + rnd.randint(0, 3) - 1
            centres.append((cx, cy, rnd.choice([3.2, 3.6, 4.0])))
    centres.sort(key=lambda c: c[1])
    for (cx, cy, r) in centres:
        for oy in (-T, 0, T):
            for ox in (-T, 0, T):
                x0, y0 = cx + ox, cy + oy
                for y in range(int(y0 - r) - 1, int(y0 + r) + 2):
                    for x in range(int(x0 - r) - 1, int(x0 + r) + 2):
                        if not (0 <= x < T and 0 <= y < T):
                            continue
                        dx, dy = x + 0.5 - x0, y + 0.5 - y0
                        d = math.hypot(dx, dy)
                        if d > r:
                            continue
                        if d > r - 1.0 and (dx + dy) > 0.8:
                            c = dark
                        elif d > r - 1.6 and (dx + dy) < -1.5:
                            c = light
                        elif dx < -0.5 and dy < -0.5 and d < r - 1.6 and highlight:
                            c = highlight
                        else:
                            c = base
                        put(layer, x, y, c)
    for y in range(T):
        for x in range(T):
            if m[y, x]:
                t[y, x] = layer[y, x] if layer[y, x, 3] else rgba(deep)
    tl, br = edge_masks(m)
    t[br] = rgba(dark)


def paint_forest(t, m, f, cfg):
    leafy(t, m, f, "pine", "green", "sage_d", "coal", cfg + 101, "sage")


def paint_bush(t, m, f, cfg):
    leafy(t, m, f, "green", "leaf", "pine", "pine", cfg + 202, "jade")
    rnd = random.Random(cfg + 9)
    for _ in range(2):
        x, y = rnd.randint(2, 13), rnd.randint(2, 13)
        if m[y, x] and f[y, x] > 0.75:
            put(t, x, y, "red")  # bayas


def paint_canopy(t, m, f, cfg):
    leafy(t, m, f, "green", "leaf", "pine", "sage_d", cfg + 303, "pale")


def paint_cave_rock(t, m, f, cfg):
    for y in range(T):
        for x in range(T):
            if m[y, x]:
                h = hash01(x // 4, y // 3, cfg + 50)
                put(t, x, y, "ink" if f[y, x] > 0.85 else ("plum" if h > 0.5 else "mauve"))
    tl, br = edge_masks(m)
    t[tl] = rgba("lavgray")
    t[br] = rgba("outline")
    for y in range(T):
        for x in range(T):
            if m[y, x] and f[y, x] > 0.7 and hash01(x, y, 3) > 0.93:
                put(t, x, y, "dusty")


def outlined_dual(painter):
    """Envuelve un pintor para que el borde exterior lleve el contorno oscuro (objetos sólidos)."""
    def wrapped(t, m, f, cfg):
        painter(t, m, f, cfg)
        # contorno 1 px por fuera, dentro de la casilla
        edge = np.zeros_like(m)
        for dx, dy in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
            sh = np.zeros_like(m)
            ys0, ys1 = max(0, dy), T + min(0, dy)
            xs0, xs1 = max(0, dx), T + min(0, dx)
            sh[ys0:ys1, xs0:xs1] = m[ys0 - dy:ys1 - dy, xs0 - dx:xs1 - dx]
            edge |= sh
        ring = edge & ~m & (f > 0.38)
        t[ring] = rgba("outline")
    return wrapped


# --- Cerca, casas y objetos -------------------------------------------------------------------------------------------

def fence(cfg: int) -> np.ndarray:
    n, e, s, w = cfg & 1, (cfg >> 1) & 1, (cfg >> 2) & 1, (cfg >> 3) & 1
    t = tile()
    wood, light, dark = "clay", "tan", "rust"
    # Travesaños horizontales.
    for y in (6, 10):
        x0 = 0 if w else 6
        x1 = 15 if e else 9
        if w or e:
            hline(t, x0, x1, y, wood)
            hline(t, x0, x1, y - 1, light)
            hline(t, x0, x1, y + 1, dark)
    # Travesaños verticales (cerca vista desde arriba): tablón continuo.
    if n or s:
        y0 = 0 if n else 6
        y1 = 15 if s else 12
        rect(t, 7, y0, 2, y1 - y0 + 1, wood)
        vline(t, 7, y0, y1, light)
        vline(t, 9, y0, y1, dark)
    # Poste.
    rect(t, 6, 3, 4, 10, wood)
    hline(t, 6, 9, 3, light)
    vline(t, 6, 3, 12, light)
    vline(t, 9, 4, 12, dark)
    hline(t, 6, 9, 12, dark)
    hline(t, 6, 9, 2, "sand")
    return outline(t)


def roof_tile(kind: str, seed: int) -> np.ndarray:
    """Tejado de tejas en escama: `kind` en {'tl','t','tr','l','m','r','bl','b','br'} (bordes del rectángulo)."""
    t = tile()
    base, light, dark = "red", "redl", "blood"
    t[:, :] = rgba(base)
    for y in range(T):
        row = y // 4
        for x in range(T):
            xx = (x + (2 if row % 2 else 0)) % 4
            yy = y % 4
            if yy == 3 and xx in (0, 3):
                put(t, x, y, dark)
            elif yy == 3:
                put(t, x, y, "wine")
            elif xx == 3 and yy >= 1:
                put(t, x, y, dark)
            elif yy == 0 and xx in (0, 1):
                put(t, x, y, light)
    if kind in ("tl", "t", "tr"):
        hline(t, 0, 15, 0, "outline")
        hline(t, 0, 15, 1, "coral")
        hline(t, 0, 15, 2, "redl")
    if kind in ("tl", "l", "bl"):
        vline(t, 0, 0, 15, "outline")
        vline(t, 1, 0, 15, "coral")
    if kind in ("tr", "r", "br"):
        vline(t, 15, 0, 15, "outline")
        vline(t, 14, 0, 15, "blood")
    if kind in ("bl", "b", "br"):
        hline(t, 0, 15, 13, "blood")
        hline(t, 0, 15, 14, "wine")
        hline(t, 0, 15, 15, "outline")
    return t


def wall_tile(kind: str) -> np.ndarray:
    """Pared de tablones: 'l','m','r', 'door', 'window'."""
    t = tile()
    t[:, :] = rgba("clay")
    for y in range(T):
        if y % 4 == 3:
            hline(t, 0, 15, y, "rust")
        elif y % 4 == 0:
            hline(t, 0, 15, y, "tan")
    for y in range(0, T, 4):
        x = (y * 5 + 3) % 13
        vline(t, x, y, y + 2, "rust")
    hline(t, 0, 15, 0, "wine")
    hline(t, 0, 15, 15, "outline")
    hline(t, 0, 15, 14, "wine")
    if kind == "l":
        rect(t, 0, 0, 2, 16, "rust")
        vline(t, 0, 0, 15, "outline")
        vline(t, 1, 1, 14, "tan")
    if kind == "r":
        rect(t, 14, 0, 2, 16, "rust")
        vline(t, 15, 0, 15, "outline")
    if kind == "door":
        rect(t, 4, 3, 8, 12, "outline")
        rect(t, 5, 4, 6, 11, "wine")
        rect(t, 5, 4, 2, 11, "rust")
        vline(t, 8, 4, 14, "outline")
        put(t, 10, 9, "gold")
        hline(t, 3, 12, 2, "rust")
    if kind == "window":
        rect(t, 4, 3, 8, 8, "outline")
        rect(t, 5, 4, 6, 6, "navy")
        rect(t, 5, 4, 3, 3, "sky")
        put(t, 5, 4, "ice")
        vline(t, 8, 4, 9, "rust")
        hline(t, 5, 10, 6, "rust")
        rect(t, 3, 11, 10, 2, "rust")
        hline(t, 3, 12, 11, "tan")
        for x, c in [(4, "green"), (6, "pink"), (8, "gold"), (10, "jade"), (11, "pink")]:
            put(t, x, 10, c)
    return t


def crown(seed: int, base: str, light: str, dark: str, highlight: str) -> np.ndarray:
    """Copa de árbol redonda de 32×32 (racimos con luz arriba-izquierda), con contorno."""
    rnd = random.Random(seed)
    t = canvas(32, 32)
    blobs = [(16, 17, 12)]
    for _ in range(7):
        a = rnd.random() * math.tau
        r = rnd.uniform(5, 8)
        blobs.append((16 + math.cos(a) * rnd.uniform(5, 8), 16 + math.sin(a) * rnd.uniform(4, 7), r))
    for (cx, cy, r) in blobs:
        ellipse(t, cx, cy, r, r * 0.92, base)
    m = t[:, :, 3] > 0
    for (cx, cy, r) in sorted(blobs, key=lambda b: b[1]):
        for y in range(32):
            for x in range(32):
                if not m[y, x]:
                    continue
                dx, dy = x + 0.5 - cx, y + 0.5 - cy
                d = math.hypot(dx, dy)
                if r - 1.6 < d <= r and dx + dy < -2:
                    put(t, x, y, light)
                elif r - 1.0 < d <= r + 0.3 and dx + dy > 2:
                    put(t, x, y, dark)
    for _ in range(10):
        x, y = rnd.randint(6, 18), rnd.randint(5, 16)
        if m[y, x]:
            put(t, x, y, highlight)
    return outline(t)


def ruin_tile(kind: str) -> np.ndarray:
    """Muro de piedra en ruinas: 'top' (cara superior con musgo) o 'front' (cara de sillares)."""
    t = tile()
    rnd = random.Random(zlib.crc32(kind.encode()) & 0xFFFF)
    if kind == "top":
        t[:, :] = rgba("lavgray")
        for y in range(T):
            for x in range(T):
                h = hash01(x // 2, y // 2, 13)
                if h > 0.7:
                    put(t, x, y, "sage")
                elif h > 0.6:
                    put(t, x, y, "sage_l")
                elif h < 0.15:
                    put(t, x, y, "plum")
        hline(t, 0, 15, 0, "silver")
    else:
        t[:, :] = rgba("lavgray")
        for row in range(4):
            y = row * 4
            hline(t, 0, 15, y + 3, "plum")
            off = 0 if row % 2 == 0 else 4
            for x in range(off, T, 8):
                vline(t, x, y, y + 2, "plum")
            hline(t, 0, 15, y, "silver")
        for _ in range(4):
            put(t, rnd.randrange(T), rnd.randrange(T), "sage")
        hline(t, 0, 15, 15, "ink")
    return t


def trunk() -> np.ndarray:
    t = tile()
    rect(t, 6, 0, 4, 13, "rust")
    vline(t, 6, 0, 12, "clay")
    vline(t, 9, 0, 12, "wine")
    put(t, 5, 12, "rust")
    put(t, 10, 12, "rust")
    put(t, 4, 13, "rust")
    put(t, 11, 13, "wine")
    put(t, 8, 5, "wine")
    return outline(t)


def canopy_shadow() -> np.ndarray:
    t = tile()
    t[:, :] = rgba("sage_d", 110)
    return t


def stump() -> np.ndarray:
    t = tile()
    ellipse(t, 8, 10, 5, 3.5, "rust")
    ellipse(t, 8, 8, 5, 2.5, "tan")
    ellipse(t, 8, 8, 3, 1.5, "sand")
    put(t, 8, 8, "clay")
    vline(t, 3, 8, 11, "clay")
    t = outline(t)
    return t


def log() -> np.ndarray:
    t = tile()
    rect(t, 2, 7, 11, 5, "rust")
    hline(t, 2, 12, 7, "clay")
    hline(t, 2, 12, 11, "wine")
    ellipse(t, 13, 9.5, 2, 2.5, "tan")
    put(t, 13, 9, "clay")
    put(t, 6, 9, "wine")
    put(t, 9, 8, "clay")
    return outline(t)


def sign() -> np.ndarray:
    t = tile()
    rect(t, 7, 8, 2, 7, "rust")
    rect(t, 2, 2, 12, 7, "clay")
    hline(t, 2, 13, 2, "tan")
    hline(t, 2, 13, 8, "rust")
    hline(t, 4, 11, 4, "rust")
    hline(t, 4, 9, 6, "rust")
    return outline(t)


def lamp() -> np.ndarray:
    t = tile()
    vline(t, 8, 5, 14, "ink")
    vline(t, 7, 6, 14, "plum")
    rect(t, 6, 1, 4, 4, "gold")
    put(t, 7, 2, "cream")
    hline(t, 5, 10, 0, "ink")
    hline(t, 6, 9, 15, "ink")
    return outline(t)


def well() -> np.ndarray:
    t = tile()
    ellipse(t, 8, 9, 7, 6, "lavgray")
    ellipse(t, 8, 8, 5, 4, "navy")
    ellipse(t, 8, 8.5, 3.5, 2.5, "teal_d")
    hline(t, 4, 11, 3, "silver")
    put(t, 6, 7, "ice")
    return outline(t)


def campfire() -> np.ndarray:
    t = tile()
    for (x, y) in [(3, 12), (12, 12), (5, 14), (10, 14), (8, 15)]:
        rect(t, x - 1, y - 1, 2, 2, "silver")
    line(t, 4, 13, 11, 10, "rust")
    line(t, 4, 10, 11, 13, "clay")
    ellipse(t, 8, 9, 3, 4, "orange")
    ellipse(t, 8, 10, 2, 2.5, "gold")
    put(t, 8, 10, "cream")
    put(t, 8, 4, "amber")
    return outline(t)


def stairs() -> np.ndarray:
    t = tile()
    rect(t, 1, 1, 14, 14, "outline")
    for i in range(4):
        rect(t, 2, 2 + i * 3, 12, 3, ["silver", "lavgray", "plum", "ink"][i])
        hline(t, 2, 13, 2 + i * 3, "mist" if i < 2 else "lavgray")
    return t


def big_well() -> np.ndarray:
    """Pozo de piedra de 32×32 visto desde arriba (3/4): brocal de piedras, agua oscura, dos postes y tejadillo."""
    t = canvas(32, 32)
    ellipse(t, 16, 21, 13, 9, "lavgray")
    for k in range(18):
        a = k / 18 * math.tau
        x = 16 + math.cos(a) * 11.5
        y = 21 + math.sin(a) * 7.5
        ellipse(t, x, y, 2.2, 1.8, "silver" if math.sin(a) < 0.3 else "lavgray")
        put(t, int(x) - 1, int(y) - 1, "mist")
    ellipse(t, 16, 21, 8.5, 5.5, "outline")
    ellipse(t, 16, 21.5, 7.5, 4.5, "teal_d")
    hline(t, 12, 17, 20, "teal")
    put(t, 14, 20, "foam")
    for x in (5, 26):
        rect(t, x, 6, 2, 16, "rust")
        vline(t, x, 6, 21, "clay")
    hline(t, 6, 26, 9, "wine")
    vline(t, 16, 9, 18, "mist")
    rect(t, 15, 17, 3, 2, "rust")
    for k in range(5):
        hline(t, 3 + k, 28 - k, 2 + k, "red" if k % 2 else "redl")
    hline(t, 3, 28, 7, "blood")
    return outline(t)


def base_tiles_png() -> None:
    """Imágenes que referencian maps/tilesets/placeholder.tsj y collision.tsj (para verlas en Tiled)."""
    img = canvas(128, 16)
    samples = [grass_base(1), dirt_base(2), None, None, None, None, None, floor_base(4)]
    road = canvas(16, 16); dirt_texture(road, 23, None, "sage_p", "pale", "taupe")
    samples[2] = road
    wall = wall_tile("m"); samples[3] = wall
    bush = dual_tiles(707, 0.35, outlined_dual(paint_bush))[15]; samples[4] = bush
    water = dual_tiles(404, 0.35, paint_water)[15]; samples[5] = water
    rock = dual_tiles(505, 0.35, outlined_dual(paint_rock))[15]; samples[6] = rock
    for i, s in enumerate(samples):
        img[:, i * 16:(i + 1) * 16] = s
    save(img, "tiles/placeholder.png")
    col = canvas(16, 16)
    col[:, :] = rgba("scarlet", 120)
    save(col, "tiles/collision.png")


def build() -> None:
    atlas = canvas(COLS * T, ROWS * T)
    # Fila 0: pasto.
    for i, tone in enumerate(["normal", "normal", "normal", "normal", "light", "light", "dark", "dark"]):
        blit(atlas, grass_base(100 + i, tone), i, 0)
    decos = [grass_tuft(1, False), grass_tuft(2, True), flowers(3, "gold", "cream"), flowers(4, "pink_p", "gold"),
             pebble(5), mushroom(), clover(6), dirt_speck(7)]
    for i, d in enumerate(decos):
        blit(atlas, d, 8 + i, 0)
    # Fila 1: tierra y cueva.
    for i in range(4):
        blit(atlas, dirt_base(200 + i), i, 1)
    for i, d in enumerate([pebble(8, "dirt"), dry_tuft(9), crack(), pebble(10, "dirt")]):
        blit(atlas, d, 4 + i, 1)
    for i in range(4):
        blit(atlas, floor_base(300 + i), 8 + i, 1)
    for i, d in enumerate([pebble(11, "dirt"), bones(), crystal(), puddle()]):
        blit(atlas, d, 12 + i, 1)
    # Filas dual-grid.
    rows = [
        (2, dual_tiles(202, 0.45, paint_dirt)),
        (3, dual_tiles(303, 0.30, paint_road)),
        (4, dual_tiles(404, 0.35, paint_water)),
        (5, dual_tiles(505, 0.35, outlined_dual(paint_rock))),
        (6, dual_tiles(606, 0.40, outlined_dual(paint_forest))),
        (7, dual_tiles(707, 0.35, outlined_dual(paint_bush))),
        (8, dual_tiles(808, 0.40, outlined_dual(paint_canopy))),
        (9, dual_tiles(909, 0.35, outlined_dual(paint_cave_rock))),
    ]
    for row, tiles in rows:
        for cfg, t in enumerate(tiles):
            blit(atlas, t, cfg, row)
    # Cerca.
    for cfg in range(16):
        blit(atlas, fence(cfg), cfg, 10)
    # Casa: tejado (9 piezas) y alero (3).
    for i, k in enumerate(["tl", "t", "tr", "l", "m", "r", "bl", "b", "br"]):
        blit(atlas, roof_tile(k, i), i, 11)
    for i, k in enumerate(["tl", "t", "tr"]):
        blit(atlas, roof_tile(k, 20 + i), 9 + i, 11)
    for i, k in enumerate(["l", "m", "r", "door", "window"]):
        blit(atlas, wall_tile(k), i, 12)
    blit(atlas, ruin_tile("top"), 5, 12)
    blit(atlas, ruin_tile("front"), 6, 12)
    for i, o in enumerate([trunk(), canopy_shadow(), stump(), log(), sign(), lamp(), well(), campfire(), stairs()]):
        blit(atlas, o, i, 13)
    # Filas 14-15: camino interior (4 variantes) y copas de 32×32 (claras para árboles sueltos, oscuras para bosque).
    for i in range(4):
        road = tile()
        dirt_texture(road, 400 + i, None, "sage_p", "pale", "taupe")
        blit(atlas, road, i, 14)
    crowns = [crown(1, "green", "leaf", "pine", "pale"), crown(2, "green", "leaf", "pine", "pale"),
              crown(3, "pine", "green", "sage_d", "jade"), crown(4, "pine", "green", "coal", "sage")]
    for i, c in enumerate(crowns):
        atlas[14 * T:16 * T, (4 + i * 2) * T:(6 + i * 2) * T] = c
    atlas[14 * T:16 * T, 12 * T:14 * T] = big_well()
    save(atlas, "tiles/terrain.png")
    base_tiles_png()


if __name__ == "__main__":
    build()
