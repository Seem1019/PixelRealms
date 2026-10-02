"""Utilidades de pixel art para el generador de assets de PixelRealms.

Todo se dibuja en la paleta Resurrect 64 (client/assets/palette.png): luz desde arriba a la izquierda, contorno oscuro
que no es negro puro (OUTLINE) y nada de antialias. Las imágenes son arrays numpy RGBA uint8.
"""
from __future__ import annotations

import math
import random
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "client" / "assets"

# Resurrect 64 (Kerrie Lake, https://lospec.com/palette-list/resurrect-64), en el orden original.
RESURRECT_64 = [
    "2e222f", "3e3546", "625565", "966c6c", "ab947a", "694f62", "7f708a", "9babb2",
    "c7dcd0", "ffffff", "6e2727", "b33831", "ea4f36", "f57d4a", "ae2334", "e83b3b",
    "fb6b1d", "f79617", "f9c22b", "7a3045", "9e4539", "cd683d", "e6904e", "fbb954",
    "4c3e24", "676633", "a2a947", "d5e04b", "fbff86", "165a4c", "239063", "1ebc73",
    "91db69", "cddf6c", "313638", "374e4a", "547e64", "92a984", "b2ba90", "0b5e65",
    "0b8a8f", "0eaf9b", "30e1b9", "8ff8e2", "323353", "484a77", "4d65b4", "4d9be6",
    "8fd3ff", "45293f", "6b3e75", "905ea9", "a884f3", "eaaded", "753c54", "a24b6f",
    "cf657f", "ed8099", "831c5d", "c32454", "f04f78", "f68181", "fca790", "fdcbb0",
]

# Nombres de trabajo para los colores que más se usan.
P = {
    "outline": "2e222f", "ink": "3e3546", "plum": "625565", "dusty": "966c6c", "taupe": "ab947a",
    "mauve": "694f62", "lavgray": "7f708a", "silver": "9babb2", "mist": "c7dcd0", "white": "ffffff",
    "blood": "6e2727", "red": "b33831", "redl": "ea4f36", "coral": "f57d4a", "crimson": "ae2334", "scarlet": "e83b3b",
    "orange": "fb6b1d", "amber": "f79617", "gold": "f9c22b",
    "wine": "7a3045", "rust": "9e4539", "clay": "cd683d", "tan": "e6904e", "sand": "fbb954",
    "mud": "4c3e24", "olive": "676633", "moss": "a2a947", "lime": "d5e04b", "cream": "fbff86",
    "pine": "165a4c", "green": "239063", "jade": "1ebc73", "leaf": "91db69", "pale": "cddf6c",
    "coal": "313638", "sage_d": "374e4a", "sage": "547e64", "sage_l": "92a984", "sage_p": "b2ba90",
    "teal_d": "0b5e65", "teal": "0b8a8f", "aqua": "0eaf9b", "aqua_l": "30e1b9", "foam": "8ff8e2",
    "navy": "323353", "indigo": "484a77", "blue": "4d65b4", "sky": "4d9be6", "ice": "8fd3ff",
    "grape_d": "45293f", "grape": "6b3e75", "violet": "905ea9", "lilac": "a884f3", "pink_p": "eaaded",
    "berry_d": "753c54", "berry": "a24b6f", "rose": "cf657f", "rose_l": "ed8099",
    "magenta_d": "831c5d", "magenta": "c32454", "pink": "f04f78", "salmon": "f68181", "peach": "fca790", "skin_l": "fdcbb0",
}

OUTLINE = P["outline"]


def rgba(name_or_hex: str, a: int = 255) -> tuple[int, int, int, int]:
    h = P.get(name_or_hex, name_or_hex)
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


def canvas(w: int, h: int) -> np.ndarray:
    return np.zeros((h, w, 4), dtype=np.uint8)


def save(img: np.ndarray, rel: str) -> Path:
    path = ASSETS / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(img, "RGBA").save(path)
    return path


def put(img: np.ndarray, x: int, y: int, c, a: int | None = None) -> None:
    h, w = img.shape[:2]
    if 0 <= x < w and 0 <= y < h:
        col = rgba(c) if isinstance(c, str) else c
        if a is not None:
            col = (col[0], col[1], col[2], a)
        img[y, x] = col


def rect(img: np.ndarray, x: int, y: int, w: int, h: int, c) -> None:
    for yy in range(y, y + h):
        for xx in range(x, x + w):
            put(img, xx, yy, c)


def hline(img, x0, x1, y, c):
    for x in range(min(x0, x1), max(x0, x1) + 1):
        put(img, x, y, c)


def vline(img, x, y0, y1, c):
    for y in range(min(y0, y1), max(y0, y1) + 1):
        put(img, x, y, c)


def line(img, x0, y0, x1, y1, c):
    dx, dy = abs(x1 - x0), -abs(y1 - y0)
    sx, sy = (1 if x0 < x1 else -1), (1 if y0 < y1 else -1)
    err = dx + dy
    while True:
        put(img, x0, y0, c)
        if x0 == x1 and y0 == y1:
            return
        e2 = 2 * err
        if e2 >= dy:
            err += dy
            x0 += sx
        if e2 <= dx:
            err += dx
            y0 += sy


def ellipse(img, cx: float, cy: float, rx: float, ry: float, c) -> None:
    """Elipse rellena centrada en (cx, cy), por centros de píxel."""
    for y in range(int(cy - ry) - 1, int(cy + ry) + 2):
        for x in range(int(cx - rx) - 1, int(cx + rx) + 2):
            if rx <= 0 or ry <= 0:
                continue
            if ((x + 0.5 - cx) / rx) ** 2 + ((y + 0.5 - cy) / ry) ** 2 <= 1.0:
                put(img, x, y, c)


def mask_of(img: np.ndarray) -> np.ndarray:
    return img[:, :, 3] > 0


def outline(img: np.ndarray, color: str = OUTLINE, diagonal: bool = False) -> np.ndarray:
    """Contorno de 1 px por fuera de lo opaco."""
    m = mask_of(img)
    out = img.copy()
    h, w = m.shape
    offs = [(-1, 0), (1, 0), (0, -1), (0, 1)] + ([(-1, -1), (1, -1), (-1, 1), (1, 1)] if diagonal else [])
    grown = np.zeros_like(m)
    for dx, dy in offs:
        sh = np.zeros_like(m)
        ys0, ys1 = max(0, dy), h + min(0, dy)
        xs0, xs1 = max(0, dx), w + min(0, dx)
        sh[ys0:ys1, xs0:xs1] = m[ys0 - dy:ys1 - dy, xs0 - dx:xs1 - dx]
        grown |= sh
    edge = grown & ~m
    out[edge] = rgba(color)
    return out


def shade_edges(img: np.ndarray, light: str | None, dark: str | None, region: np.ndarray | None = None) -> None:
    """Luz arriba-izquierda: los píxeles cuyo vecino de arriba/izquierda está vacío se aclaran; abajo/derecha, se oscurecen."""
    m = mask_of(img) if region is None else region
    h, w = m.shape
    snap = img.copy()
    for y in range(h):
        for x in range(w):
            if not m[y, x]:
                continue
            up = y == 0 or not m[y - 1, x]
            left = x == 0 or not m[y, x - 1]
            down = y == h - 1 or not m[y + 1, x]
            right = x == w - 1 or not m[y, x + 1]
            if dark and (down or right):
                img[y, x] = rgba(dark)
            elif light and (up or left):
                img[y, x] = rgba(light)
    del snap


def paste(dst: np.ndarray, src: np.ndarray, x: int, y: int) -> None:
    """Pega `src` sobre `dst` (alfa binario: los píxeles opacos sustituyen)."""
    h, w = src.shape[:2]
    H, W = dst.shape[:2]
    for yy in range(h):
        for xx in range(w):
            if src[yy, xx, 3] > 0 and 0 <= x + xx < W and 0 <= y + yy < H:
                if src[yy, xx, 3] == 255 or dst[y + yy, x + xx, 3] == 0:
                    dst[y + yy, x + xx] = src[yy, xx]
                else:
                    a = src[yy, xx, 3] / 255.0
                    d = dst[y + yy, x + xx].astype(float)
                    s = src[yy, xx].astype(float)
                    dst[y + yy, x + xx] = np.array([s[0] * a + d[0] * (1 - a), s[1] * a + d[1] * (1 - a), s[2] * a + d[2] * (1 - a), max(d[3], s[3])], dtype=np.uint8)


def flip_h(img: np.ndarray) -> np.ndarray:
    return img[:, ::-1].copy()


def recolor(img: np.ndarray, mapping: dict[str, str]) -> np.ndarray:
    out = img.copy()
    for src, dst in mapping.items():
        s = rgba(src)[:3]
        sel = (img[:, :, 0] == s[0]) & (img[:, :, 1] == s[1]) & (img[:, :, 2] == s[2]) & (img[:, :, 3] > 0)
        out[sel, :3] = rgba(dst)[:3]
    return out


class PeriodicNoise:
    """Ruido de valor periódico (periodo `period` px) para bordes irregulares que casan entre casillas."""

    def __init__(self, seed: int, period: int = 16, cells: int = 4):
        rnd = random.Random(seed)
        self.cells = cells
        self.period = period
        self.grid = [[rnd.random() for _ in range(cells)] for _ in range(cells)]

    def __call__(self, x: float, y: float) -> float:
        fx = (x / self.period) * self.cells
        fy = (y / self.period) * self.cells
        x0, y0 = math.floor(fx), math.floor(fy)
        tx, ty = fx - x0, fy - y0
        tx = tx * tx * (3 - 2 * tx)
        ty = ty * ty * (3 - 2 * ty)
        c = self.cells
        g = self.grid
        a = g[y0 % c][x0 % c]
        b = g[y0 % c][(x0 + 1) % c]
        d = g[(y0 + 1) % c][x0 % c]
        e = g[(y0 + 1) % c][(x0 + 1) % c]
        return (a * (1 - tx) + b * tx) * (1 - ty) + (d * (1 - tx) + e * tx) * ty


def hash01(*vals: int) -> float:
    h = 2166136261
    for v in vals:
        h = ((h ^ (v & 0xFFFFFFFF)) * 16777619) & 0xFFFFFFFF
    return h / 0xFFFFFFFF


def palette_png() -> None:
    img = canvas(8, 8)
    for i, hx in enumerate(RESURRECT_64):
        img[i // 8, i % 8] = rgba(hx)
    big = np.kron(img, np.ones((4, 4, 1), dtype=np.uint8))
    save(big, "palette.png")
