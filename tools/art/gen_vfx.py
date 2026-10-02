"""Efectos de hechizos (HU-091): client/assets/sprites/vfx/*.png, en Resurrect 64, sin degradados ni blur.

- `cast_<elemento>.png`: brillo bajo los pies mientras se castea, 32×16, 4 cuadros en loop.
- Proyectiles de 16×16: filas = 8 direcciones (e, se, s, sw, w, nw, n, ne), 4 cuadros por fila. Los nombres que ya usa
  content/spells.json (`vfx/fireball`, `vfx/frostbolt`, `vfx/arrow`, `vfx/shadow_bolt`) más `bolt_arcane`, `bolt_holy` y
  `blade` para básicos a distancia y hechizos sin sprite propio. Orientación por cuadros, nunca rotando el sprite.
- `impact_slash`, `impact_spark` (físico) e `impact_<elemento>` (mágico): 32×32, 5 cuadros, sin loop.
- `heal.png`: destellos verdes que suben, 32×32, 5 cuadros.
- `area_<elemento>.png`: erupción de 16×16, 5 cuadros, que el cliente reparte por el círculo del área al resolverse.
Un `.json` junto a cada hoja: {frameSize, frames, fps, loop, rows?}. Determinista: sin azar.
"""
from __future__ import annotations

import json
import math

import numpy as np

from pix import ASSETS, canvas, ellipse, hline, line, outline, put, rect, save, vline

# Elemento → (oscuro, medio, claro, brillo)
ELEMENTS = {
    "fire": ("red", "orange", "gold", "cream"),
    "frost": ("blue", "sky", "ice", "white"),
    "arcane": ("indigo", "blue", "ice", "white"),
    "holy": ("amber", "gold", "cream", "white"),
    "shadow": ("grape_d", "grape", "violet", "lilac"),
    "nature": ("green", "jade", "leaf", "pale"),
    "steel": ("lavgray", "silver", "mist", "white"),
}
DIRS8 = ["e", "se", "s", "sw", "w", "nw", "n", "ne"]


def meta(rel: str, size, frames: int, fps: int, loop: bool, rows: list[str] | None = None) -> None:
    data = {"frameSize": list(size), "frames": frames, "fps": fps, "loop": loop}
    if rows:
        data["rows"] = rows
    path = ASSETS / "sprites" / (rel + ".json")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=1) + "\n", encoding="utf-8")


def strip(frames: list[np.ndarray]) -> np.ndarray:
    h, w = frames[0].shape[:2]
    out = canvas(w * len(frames), h)
    for i, f in enumerate(frames):
        out[:, i * w:(i + 1) * w] = f
    return out


def pixel_ring(img, cx, cy, rx, ry, color, step_deg=8, skip=None) -> None:
    for k in range(int(360 / step_deg)):
        if skip is not None and skip(k):
            continue
        a = math.radians(k * step_deg)
        put(img, int(round(cx + math.cos(a) * rx)), int(round(cy + math.sin(a) * ry)), color)


def star(img, x, y, arm, c1, c2) -> None:
    for k in range(1, arm + 1):
        c = c1 if k < arm else c2
        for (ox, oy) in ((k, 0), (-k, 0), (0, k), (0, -k)):
            put(img, x + ox, y + oy, c)
    put(img, x, y, "white" if arm > 1 else c1)


# --- Brillo de casteo ---------------------------------------------------------------------------------------------------

def cast_glow(el: str) -> list[np.ndarray]:
    dark, mid, light, shine = ELEMENTS[el]
    frames = []
    for i in range(4):
        img = canvas(32, 16)
        # Anillo de runa achatado bajo los pies: discontinuo y girando un tramo por cuadro.
        pixel_ring(img, 16, 9, 12, 4, dark, 10, skip=lambda k: (k + i * 2) % 6 in (4, 5))
        pixel_ring(img, 16, 9, 10, 3, mid, 12, skip=lambda k: (k + i) % 5 == 4)
        for k in range(4):
            a = math.radians(k * 90 + i * 22)
            put(img, int(round(16 + math.cos(a) * 12)), int(round(9 + math.sin(a) * 4)), light)
        # Chispas que suben.
        for j, (x, base) in enumerate([(9, 9), (16, 11), (23, 9)]):
            y = base - ((i + j * 2) % 4) * 2 - 1
            put(img, x, y, light if (i + j) % 2 else shine)
        frames.append(img)
    return frames


# --- Proyectiles ----------------------------------------------------------------------------------------------------------

def dir_vec(d: str) -> tuple[float, float]:
    a = {"e": 0, "se": 45, "s": 90, "sw": 135, "w": 180, "nw": 225, "n": 270, "ne": 315}[d]
    return math.cos(math.radians(a)), math.sin(math.radians(a))


def orb(el: str, d: str, i: int, size: float = 3.0, tail: int = 5) -> np.ndarray:
    """Orbe con estela opuesta a la dirección: la cola parpadea un píxel por cuadro."""
    dark, mid, light, shine = ELEMENTS[el]
    img = canvas(16, 16)
    dx, dy = dir_vec(d)
    cx, cy = 8 + dx * 2, 8 + dy * 2
    for k in range(1, tail + 1):
        tx, ty = cx - dx * (k + 1), cy - dy * (k + 1)
        c = mid if k < 3 else dark
        if (k + i) % 3 != 2:
            put(img, int(round(tx)), int(round(ty)), c)
        if k < 3:
            put(img, int(round(tx - dy)), int(round(ty + dx)), dark)
    ellipse(img, cx, cy, size, size, mid)
    ellipse(img, cx - 0.5, cy - 0.5, size - 1.2, size - 1.2, light)
    put(img, int(cx) - 1, int(cy) - 1, shine)
    img = outline(img)
    # Chispa suelta alrededor (sin contorno).
    a = math.radians(i * 90 + 45)
    put(img, int(round(cx + math.cos(a) * (size + 2))), int(round(cy + math.sin(a) * (size + 2))), light)
    return img


def shard(d: str, i: int) -> np.ndarray:
    """Descarga de escarcha: aguja de hielo con escarcha detrás."""
    img = canvas(16, 16)
    dx, dy = dir_vec(d)
    x0, y0 = 8 - dx * 5, 8 - dy * 5
    x1, y1 = 8 + dx * 5, 8 + dy * 5
    line(img, int(round(x0)), int(round(y0)), int(round(x1)), int(round(y1)), "sky")
    line(img, int(round(x0 + dy)), int(round(y0 - dx)), int(round(x1 - dx)), int(round(y1 - dy)), "ice")
    put(img, int(round(x1)), int(round(y1)), "white")
    img = outline(img)
    for k in range(2):
        put(img, int(round(x0 - dx * (2 + k + i % 2))), int(round(y0 - dy * (2 + k + i % 2) + (k - 0.5) * 2)), "ice")
    return img


def arrow(d: str, i: int) -> np.ndarray:
    img = canvas(16, 16)
    dx, dy = dir_vec(d)
    x0, y0 = 8 - dx * 6, 8 - dy * 6
    x1, y1 = 8 + dx * 5, 8 + dy * 5
    line(img, int(round(x0)), int(round(y0)), int(round(x1)), int(round(y1)), "clay")
    put(img, int(round(x1)), int(round(y1)), "mist")
    put(img, int(round(x1 + dx)), int(round(y1 + dy)), "white")
    # Plumas.
    put(img, int(round(x0 - dy)), int(round(y0 + dx)), "mist")
    put(img, int(round(x0 + dy)), int(round(y0 - dx)), "mist")
    img = outline(img)
    if i % 2:
        put(img, int(round(x0 - dx * 2)), int(round(y0 - dy * 2)), "sage_p")
    return img


def blade(d: str, i: int) -> np.ndarray:
    """Cuchilla arrojada: estrella de 4 puntas que gira un cuarto por cuadro (cuadros dibujados, no rotación)."""
    img = canvas(16, 16)
    arms = [((0, -4), (0, 4), (-4, 0), (4, 0)), ((-3, -3), (3, 3), (-3, 3), (3, -3))][i % 2]
    for (ax, ay) in arms:
        line(img, 8, 8, 8 + ax, 8 + ay, "mist")
    put(img, 8, 8, "silver")
    img = outline(img)
    dx, dy = dir_vec(d)
    put(img, int(round(8 - dx * 6)), int(round(8 - dy * 6)), "mist")
    return img


def projectile_sheet(draw) -> np.ndarray:
    out = canvas(16 * 4, 16 * 8)
    for r, d in enumerate(DIRS8):
        for c in range(4):
            out[r * 16:(r + 1) * 16, c * 16:(c + 1) * 16] = draw(d, c)
    return out


# --- Impactos, curación y áreas ---------------------------------------------------------------------------------------------

def impact_slash(i: int) -> np.ndarray:
    """Tajo: media luna blanca que se abre y se deshace (físico cuerpo a cuerpo)."""
    img = canvas(32, 32)
    if i < 4:
        r = 6 + i * 2
        a0, a1 = -150 + i * 10, -30 - i * 10
        for k in range(int(a0), int(a1), 6):
            a = math.radians(k)
            for (rr, c) in [(r, "white" if i < 2 else "mist"), (r - 1, "mist"), (r - 2, "silver" if i > 1 else "mist")]:
                put(img, int(round(16 + math.cos(a) * rr)), int(round(18 + math.sin(a) * rr)), c)
    for (dx, dy) in [(-7, -3), (6, -6), (8, 2)]:
        if i in (1, 2, 3):
            put(img, 16 + dx * (i + 1) // 2, 16 + dy * (i + 1) // 2, "mist")
    return img


def impact_spark(i: int) -> np.ndarray:
    """Chispa: estrella que crece y suelta motas (físico a distancia, crítico)."""
    img = canvas(32, 32)
    if i < 3:
        star(img, 16, 16, 2 + i * 2, "gold" if i else "white", "cream")
    for k in range(6):
        a = math.radians(k * 60 + 15)
        r = 4 + i * 3
        if i > 0:
            put(img, int(round(16 + math.cos(a) * r)), int(round(16 + math.sin(a) * r)), "amber" if i < 4 else "gold")
    return img


def impact_burst(el: str, i: int) -> np.ndarray:
    """Estallido mágico: núcleo, anillo que se expande y esquirlas del color del elemento."""
    dark, mid, light, shine = ELEMENTS[el]
    img = canvas(32, 32)
    if i < 2:
        ellipse(img, 16, 16, 3 + i * 2, 3 + i * 2, mid)
        ellipse(img, 16, 16, 2 + i, 2 + i, light)
        put(img, 15, 15, shine)
        img = outline(img)
    r = 5 + i * 3
    pixel_ring(img, 16, 16, r, r, light if i < 3 else mid, 12, skip=(lambda k: k % 2 == 1) if i >= 3 else None)
    for k in range(8):
        a = math.radians(k * 45 + 22)
        rr = r + 2
        if i >= 1:
            put(img, int(round(16 + math.cos(a) * rr)), int(round(16 + math.sin(a) * rr)), shine if i < 3 else dark)
    return img


def heal(i: int) -> np.ndarray:
    """Curación: cruces y motas verdes que suben y se apagan."""
    img = canvas(32, 32)
    for j, (x, y0) in enumerate([(10, 24), (17, 26), (23, 22), (14, 18), (21, 15)]):
        y = y0 - i * 3 - (j % 2)
        if y < 2:
            continue
        c1, c2 = ("leaf", "pale") if (i + j) % 2 else ("jade", "leaf")
        if j < 3:
            for (ox, oy) in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)):
                put(img, x + ox, y + oy, c1)
            put(img, x, y, "white" if i < 3 else c2)
        else:
            put(img, x, y, c2)
    return img


def area_burst(el: str, i: int) -> np.ndarray:
    """Erupción pequeña que se repite por el área: llama, pico de hielo, columna de luz, humo de sombra…"""
    dark, mid, light, shine = ELEMENTS[el]
    img = canvas(16, 16)
    h = [3, 8, 11, 7, 3][i]
    w = [2, 3, 3, 2, 1][i]
    base = 13
    if el == "frost":
        for k in range(h):
            half = max(0, w - k * w // max(1, h))
            hline(img, 8 - half, 8 + half, base - k, light if k > h // 2 else mid)
        put(img, 8, base - h, shine)
    else:
        for k in range(h):
            wob = int(round(math.sin((k + i) * 0.9))) if el in ("fire", "shadow", "nature") else 0
            half = max(0, w - k // 4)
            hline(img, 8 - half + wob, 8 + half + wob, base - k, mid if k < h - 2 else light)
            if half > 0:
                put(img, 8 - half + wob, base - k, dark)
        put(img, 8, base - h + 1, shine)
    hline(img, 4, 12, base + 1, dark)
    img = outline(img) if i < 4 else img
    return img


def build() -> None:
    for el in ELEMENTS:
        save(strip(cast_glow(el)), f"sprites/vfx/cast_{el}.png")
        meta(f"vfx/cast_{el}", (32, 16), 4, 8, True)
        save(strip([impact_burst(el, i) for i in range(5)]), f"sprites/vfx/impact_{el}.png")
        meta(f"vfx/impact_{el}", (32, 32), 5, 15, False)
        save(strip([area_burst(el, i) for i in range(5)]), f"sprites/vfx/area_{el}.png")
        meta(f"vfx/area_{el}", (16, 16), 5, 12, False)
    projectiles = {
        "fireball": lambda d, i: orb("fire", d, i, 3.2, 6),
        "frostbolt": shard,
        "shadow_bolt": lambda d, i: orb("shadow", d, i, 3.0, 6),
        "arrow": arrow,
        "bolt_arcane": lambda d, i: orb("arcane", d, i, 2.4, 4),
        "bolt_holy": lambda d, i: orb("holy", d, i, 2.4, 4),
        "blade": blade,
    }
    for name, draw in projectiles.items():
        save(projectile_sheet(draw), f"sprites/vfx/{name}.png")
        meta(f"vfx/{name}", (16, 16), 4, 12, True, DIRS8)
    save(strip([impact_slash(i) for i in range(5)]), "sprites/vfx/impact_slash.png")
    meta("vfx/impact_slash", (32, 32), 5, 18, False)
    save(strip([impact_spark(i) for i in range(5)]), "sprites/vfx/impact_spark.png")
    meta("vfx/impact_spark", (32, 32), 5, 15, False)
    save(strip([heal(i) for i in range(5)]), "sprites/vfx/heal.png")
    meta("vfx/heal", (32, 32), 5, 10, False)


if __name__ == "__main__":
    build()
