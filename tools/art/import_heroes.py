"""Importa el guerrero y el mago desde hojas dibujadas a mano (HU-093) a la rejilla del juego.

    pip install pillow numpy scipy
    python tools/art/import_heroes.py

Lee tools/art/refs/<clase>_sheet.webp (personajes de ~90 px sobre fondo negro), detecta cada cuadro por filas y
columnas de píxeles no negros, quita el fondo conectado al borde, reduce con promedio de caja a la altura del juego,
centra los pies en x=16 / y=feetY y añade un contorno de 1 px. Escribe client/assets/sprites/characters/<clase>.png y
.json con las mismas columnas y animaciones que gen_chars.py (19 columnas × filas s, n, e; w = e espejado).
generate_all.py lo ejecuta después de gen_chars, así que estas dos clases no vuelven al arte procedural.
"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

from gen_chars import ANIMS, COLS, DIRS
from pix import ASSETS

REFS = Path(__file__).parent / "refs"
SIZE = 32
FEET_Y = SIZE - 4
## Altura del personaje de pie (reposo sur) en píxeles del juego.
STAND_HEIGHT = 26
BG_LEVEL = 40  # canal máximo por debajo del cual un píxel cuenta como fondo negro
OUTLINE_RGB = (0x2E, 0x22, 0x2F)

## Cuadro de la hoja de referencia ("fila,columna") para cada animación, dirección y fotograma.
## Las hojas solo traen vista 3/4 de frente y de espaldas: el sur usa la 3/4 frontal, el este el perfil al caminar y
## el norte, que no tiene ataque ni casteo propios, repite cuadros de espaldas.
FRAMES: dict[str, dict[str, dict[str, list[str]]]] = {
    "warrior": {
        "s": {"idle": ["0,0", "0,6"], "walk": ["2,0", "2,3", "2,6", "2,9"], "attack": ["4,4", "6,9", "6,8", "6,12"],
              "cast": ["7,2", "7,3", "7,4"], "hurt": ["7,0", "8,1"], "death": ["8,2", "8,3", "8,4", "8,5"]},
        "n": {"idle": ["1,0", "1,6"], "walk": ["1,1", "1,4", "1,7", "1,10"], "attack": ["1,2", "1,5", "1,8", "1,11"],
              "cast": ["1,3", "1,9", "1,12"], "hurt": ["1,0", "1,6"], "death": ["8,2", "8,3", "8,4", "8,5"]},
        "e": {"idle": ["3,0", "3,6"], "walk": ["3,1", "3,4", "3,7", "3,10"], "attack": ["5,0", "6,1", "6,5", "6,11"],
              "cast": ["7,2", "7,3", "7,4"], "hurt": ["7,0", "8,1"], "death": ["8,2", "8,3", "8,4", "8,5"]},
    },
    "mage": {
        "s": {"idle": ["0,0", "0,4"], "walk": ["2,0", "2,3", "2,6", "2,9"], "attack": ["5,0", "6,0", "6,1", "6,2"],
              "cast": ["4,0", "4,4", "4,8"], "hurt": ["7,6", "7,7"], "death": ["7,7", "7,8", "7,10", "7,11"]},
        "n": {"idle": ["1,1", "1,6"], "walk": ["1,1", "1,9", "1,6", "1,10"], "attack": ["1,1", "1,6", "1,9", "1,10"],
              "cast": ["1,1", "1,6", "1,10"], "hurt": ["1,1", "1,6"], "death": ["7,7", "7,8", "7,10", "7,11"]},
        "e": {"idle": ["3,0", "3,6"], "walk": ["3,1", "3,4", "3,7", "3,10"], "attack": ["5,0", "6,0", "6,1", "6,2"],
              "cast": ["4,0", "4,4", "4,8"], "hurt": ["7,6", "7,7"], "death": ["7,7", "7,8", "7,10", "7,11"]},
    },
}


def find_frames(img: Image.Image) -> list[list[tuple[int, int, int, int]]]:
    """Cajas (x0, y0, x1, y1) de cada cuadro, por filas de la hoja."""
    m = np.asarray(img).astype(int).max(axis=2) > BG_LEVEL
    rows = list(m.any(axis=1)) + [False]
    bands, start = [], None
    for y, filled in enumerate(rows):
        if filled and start is None:
            start = y
        elif not filled and start is not None:
            if y - start > 8:
                bands.append((start, y))
            start = None
    out = []
    for y0, y1 in bands:
        lab, k = ndimage.label(ndimage.binary_dilation(m[y0:y1].any(axis=0), iterations=6))
        row = []
        for i in range(1, k + 1):
            xs = np.where(lab == i)[0]
            sub = m[y0:y1, xs.min():xs.max() + 1]
            ys, xx = np.where(sub.any(axis=1))[0], np.where(sub.any(axis=0))[0]
            row.append((int(xs.min() + xx.min()), int(y0 + ys.min()), int(xs.min() + xx.max() + 1), int(y0 + ys.max() + 1)))
        out.append(row)
    return out


def cut_out(img: Image.Image, box: tuple[int, int, int, int]) -> Image.Image:
    """Recorte con el fondo negro conectado al borde transparente (el contorno oscuro interior se conserva)."""
    x0, y0, x1, y1 = box
    c = np.asarray(img.crop((x0 - 2, y0 - 2, x1 + 2, y1 + 2))).astype(np.uint8)
    lab, _ = ndimage.label(c.max(axis=2) < BG_LEVEL - 2)
    border = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
    alpha = np.where(np.isin(lab, list(border)), 0, 255).astype(np.uint8)
    return Image.fromarray(np.dstack([c, alpha]), "RGBA")


def shrink(im: Image.Image, scale: float) -> Image.Image:
    """Reducción por promedio de caja con alfa premultiplicado y alfa binario (sin bordes semitransparentes)."""
    w, h = im.size
    a = np.asarray(im).astype(float)
    k = a[..., 3:] / 255.0
    pre = Image.fromarray(np.dstack([(a[..., :3] * k).astype(np.uint8), a[..., 3].astype(np.uint8)]), "RGBA")
    s = np.asarray(pre.resize((max(1, round(w * scale)), max(1, round(h * scale))), Image.BOX)).astype(float)
    al = s[..., 3]
    rgb = np.where(al[..., None] > 0, s[..., :3] / np.maximum(al[..., None], 1) * 255, 0)
    return Image.fromarray(np.dstack([np.clip(rgb, 0, 255), np.where(al > 110, 255, 0)]).astype(np.uint8), "RGBA")


def add_outline(sheet: np.ndarray) -> np.ndarray:
    """Quita píxeles sueltos y rodea la silueta con un contorno de 1 px (como el resto de sprites)."""
    a = sheet.copy()
    solid = a[..., 3] > 0
    neighbours = sum(np.roll(solid, s, axis) for axis in (0, 1) for s in (1, -1))
    solid &= neighbours > 0
    a[..., 3] = np.where(solid, 255, 0)
    ring = ndimage.binary_dilation(solid, structure=[[0, 1, 0], [1, 1, 1], [0, 1, 0]]) & ~solid
    a[ring] = [*OUTLINE_RGB, 255]
    return a


def frame_cell(frame: Image.Image) -> Image.Image:
    """Celda de SIZE×SIZE con los pies centrados en x=16 y la planta en FEET_Y."""
    alpha = np.asarray(frame)[..., 3]
    ys, xs = np.where(alpha > 0)
    low = ys >= ys.max() - max(3, int(0.25 * (ys.max() - ys.min())))
    cx = int(round(xs[low].mean()))
    cell = Image.new("RGBA", (SIZE, SIZE))
    cell.paste(frame, (SIZE // 2 - cx, FEET_Y - int(ys.max()) - 1), frame)
    return cell


def build_class(class_id: str) -> None:
    img = Image.open(REFS / f"{class_id}_sheet.webp").convert("RGB")
    boxes = find_frames(img)
    box = lambda key: boxes[int(key.split(",")[0])][int(key.split(",")[1])]
    ref = box(FRAMES[class_id]["s"]["idle"][0])
    scale = STAND_HEIGHT / (ref[3] - ref[1])
    sheet = Image.new("RGBA", (SIZE * len(COLS), SIZE * len(DIRS)))
    for r, d in enumerate(DIRS):
        for name, a in ANIMS.items():
            keys = FRAMES[class_id][d][name]
            assert len(keys) == a["frames"], f"{class_id}/{d}/{name}: {len(keys)} cuadros, se esperan {a['frames']}"
            col0 = COLS.index(a["from"])
            for i, key in enumerate(keys):
                sheet.paste(frame_cell(shrink(cut_out(img, box(key)), scale)), ((col0 + i) * SIZE, r * SIZE))
    out = ASSETS / "sprites" / "characters"
    Image.fromarray(add_outline(np.asarray(sheet))).save(out / f"{class_id}.png")
    anims = {name: {"column": COLS.index(a["from"]), "frames": a["frames"], "fps": a["fps"], "loop": a["loop"]} for name, a in ANIMS.items()}
    meta = {"frameSize": [SIZE, SIZE], "rows": DIRS, "columns": COLS, "feetY": FEET_Y, "anims": anims,
            "note": "w = e espejado; importado de tools/art/refs/%s_sheet.webp por tools/art/import_heroes.py" % class_id}
    (out / f"{class_id}.json").write_text(json.dumps(meta, indent=1) + "\n", encoding="utf-8")


def build() -> None:
    for class_id in FRAMES:
        build_class(class_id)


if __name__ == "__main__":
    build()
    print("guerrero y mago importados en client/assets/sprites/characters/")
