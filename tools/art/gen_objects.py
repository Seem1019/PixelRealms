"""Objetos de mapa (HU-083): client/assets/sprites/objects/*.png, en Resurrect 64.

- `lever.png`: 32×16, dos cuadros de 16×16: sin activar (mango a la izquierda, pomo rojo) | activada (a la derecha, verde).
  El pie del poste está en (8, 12): `MapObjectsLayer` lo coloca sobre el punto de la palanca en el .tmj.
- `door.png`: 32×16, dos cuadros de 16×16 que se repiten por cada casilla de la puerta: cerrada (tablones con bandas de
  hierro) | abierta (umbral de piedra y las puntas del rastrillo levantado).
Determinista: sin azar.
"""
from __future__ import annotations

import numpy as np

from pix import canvas, ellipse, hline, line, outline, put, rect, save, vline


def strip(frames: list[np.ndarray]) -> np.ndarray:
    h, w = frames[0].shape[:2]
    out = canvas(w * len(frames), h)
    for i, f in enumerate(frames):
        out[:, i * w:(i + 1) * w] = f
    return out


def lever(on: bool) -> np.ndarray:
    img = canvas(16, 16)
    # Base de piedra.
    rect(img, 4, 12, 8, 3, "lavgray")
    hline(img, 4, 11, 12, "silver")
    hline(img, 4, 11, 14, "plum")
    # Mango de madera inclinado y pomo.
    tip = (12, 4) if on else (4, 4)
    line(img, 8, 12, tip[0], tip[1], "rust")
    line(img, 8 + (1 if on else -1), 12, tip[0] + (1 if on else -1), tip[1], "clay")
    knob, light = ("green", "leaf") if on else ("red", "redl")
    ellipse(img, tip[0], tip[1], 1.6, 1.6, knob)
    put(img, tip[0] - 1, tip[1] - 1, light)
    put(img, 8, 12, "mist")  # eje
    return outline(img)


def door(open_: bool) -> np.ndarray:
    img = canvas(16, 16)
    if open_:
        # Umbral de piedra y las puntas del rastrillo, recogido arriba.
        hline(img, 0, 15, 14, "lavgray")
        hline(img, 0, 15, 15, "plum")
        for x in (2, 6, 10, 14):
            vline(img, x, 0, 1, "silver")
            put(img, x, 2, "lavgray")
        return img
    # Tablones (sin contorno a los lados: las casillas de la puerta se juntan en una sola pieza).
    rect(img, 0, 1, 16, 15, "rust")
    for x in (0, 5, 10, 15):
        vline(img, x, 1, 15, "wine")
    for x in (2, 7, 12):
        vline(img, x, 2, 14, "clay")
    for y in (4, 11):
        hline(img, 0, 15, y, "lavgray")
        hline(img, 0, 15, y + 1, "plum")
        for x in (3, 8, 13):
            put(img, x, y, "mist")
    hline(img, 0, 15, 0, "outline")
    hline(img, 0, 15, 15, "blood")
    return img


def build() -> None:
    save(strip([lever(False), lever(True)]), "sprites/objects/lever.png")
    save(strip([door(False), door(True)]), "sprites/objects/door.png")


if __name__ == "__main__":
    build()
