"""Importa los héroes desde hojas dibujadas a mano (HU-093, HU-099) a hojas HD que el juego dibuja a ×1/3.

    pip install pillow numpy scipy
    python tools/art/import_heroes.py

Lee tools/art/refs/<clase>_sheet.webp (personajes de ~90 px sobre fondo negro), detecta cada cuadro por filas y
columnas de píxeles no negros (partiendo los que un brillo une), quita el fondo conectado al borde, reduce con promedio
de caja hasta STAND_HEIGHT y centra los pies en x=SIZE/2, y=FEET_Y. Escribe client/assets/sprites/characters/<clase>.png
y .json: filas s, n, e (w = e espejado) y columnas según ANIMS. `pixelScale` = 3 le dice al cliente que la hoja va a
×3 de la resolución lógica (480×270): el personaje mide lo mismo en el mundo (26 px) pero con el detalle de la ventana
de 1440×810. generate_all.py lo ejecuta después de gen_chars; una clase sin hoja de referencia se queda con la procedural.
"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

from gen_chars import DIRS
from pix import ASSETS

REFS = Path(__file__).parent / "refs"
## Píxeles de la hoja por píxel lógico del juego (la ventana es 1440×810 = ×3 de 480×270).
PIXEL_SCALE = 3
## Cuadro de 40×40 lógicos (cabe el arco de un tajo) con los pies a 36, como un 32×32 con 4 px de margen extra arriba.
SIZE = 40 * PIXEL_SCALE
FEET_Y = 36 * PIXEL_SCALE
## Altura del personaje de pie (reposo sur): 26 px lógicos, la misma que el resto de personajes.
STAND_HEIGHT = 26 * PIXEL_SCALE
BG_LEVEL = 40  # canal máximo por debajo del cual un píxel cuenta como fondo negro
## Ropa casi negra (el pícaro): umbral de fondo más bajo para que botas y pantalones no se borren con él.
BG_LEVEL_BY_CLASS = {"rogue": 12}
ENCLOSED_BG_MIN_PX = 300  # fondo encerrado por un efecto: al menos este tamaño (los contornos son mucho menores)

## Animaciones: cuadros (los mismos en las tres direcciones), fps y bucle. Más cuadros que las hojas procedurales;
## las duraciones del ataque (333 ms), el golpe y la muerte (500 ms) se mantienen.
ANIMS = {
    "idle": {"frames": 4, "fps": 5, "loop": True},
    "walk": {"frames": 8, "fps": 12, "loop": True},
    "attack": {"frames": 6, "fps": 18, "loop": False},
    "cast": {"frames": 6, "fps": 10, "loop": True},
    "hurt": {"frames": 2, "fps": 10, "loop": False},
    "death": {"frames": 4, "fps": 8, "loop": False},
}


def even(row: int, available: int, count: int) -> list[str]:
    """`count` cuadros repartidos por igual entre los `available` de una fila (para ciclos de caminar y reposo)."""
    return ["%d,%d" % (row, int(i * available / count)) for i in range(count)]


def keys(row: int, cols: list[int]) -> list[str]:
    return ["%d,%d" % (row, c) for c in cols]


def pl(anim: str, frames: list[int]) -> list[str]:
    """Cuadros generados con PixelLab (refs/<clase>_pixellab/<anim>/frame_NNN.png) para vistas que la hoja no trae."""
    return ["pl:%s/frame_%03d.png" % (anim, f) for f in frames]


## Cuadro de la hoja de referencia ("fila,columna") para cada animación y dirección. Las hojas traen vista 3/4 de frente
## (sur y este usan la misma, el oeste es el este espejado) y, salvo el sacerdote, algunas de espaldas para el norte.
FRAMES: dict[str, dict[str, dict[str, list[str]]]] = {
    "warrior": {
        "s": {"idle": keys(0, [0, 3, 6, 9]), "walk": even(2, 13, 8), "attack": ["4,3", "6,0", "6,1", "6,7", "6,8", "6,12"],
              "cast": keys(7, [2, 3, 4, 2, 3, 4]), "hurt": ["7,0", "8,1"], "death": keys(8, [2, 3, 4, 5])},
        "n": {"idle": keys(1, [0, 3, 6, 9]), "walk": even(1, 13, 8), "attack": keys(1, [2, 5, 8, 11, 1, 4]),
              "cast": keys(1, [3, 9, 12, 3, 9, 12]), "hurt": keys(1, [0, 6]), "death": keys(8, [2, 3, 4, 5])},
        "e": {"idle": keys(0, [0, 3, 6, 9]), "walk": even(3, 13, 8), "attack": ["5,0", "6,0", "6,1", "6,5", "6,11", "6,12"],
              "cast": keys(7, [2, 3, 4, 2, 3, 4]), "hurt": ["7,0", "8,1"], "death": keys(8, [2, 3, 4, 5])},
    },
    "mage": {
        "s": {"idle": keys(0, [0, 4, 9, 11]), "walk": even(2, 12, 8), "attack": ["5,0", "6,0", "6,1", "6,2", "6,3", "5,0"],
              "cast": keys(4, [0, 2, 4, 6, 8, 4]), "hurt": keys(7, [6, 7]), "death": keys(7, [7, 8, 10, 11])},
        "n": {"idle": ["1,1", "1,6", "1,1", "1,6"], "walk": ["0,1", "1,1", "0,6", "1,6", "1,9", "1,10", "1,1", "1,6"],
              "attack": ["1,1", "1,6", "1,9", "1,10", "1,9", "1,6"], "cast": ["1,1", "1,6", "1,10", "1,1", "1,6", "1,10"],
              "hurt": ["1,1", "1,6"], "death": keys(7, [7, 8, 10, 11])},
        "e": {"idle": keys(0, [0, 4, 9, 11]), "walk": even(3, 12, 8), "attack": ["5,0", "6,0", "6,1", "6,2", "6,3", "5,0"],
              "cast": keys(4, [0, 2, 4, 6, 8, 4]), "hurt": keys(7, [6, 7]), "death": keys(7, [7, 8, 10, 11])},
    },
    "rogue": {
        "s": {"idle": keys(0, [0, 4, 8, 12]), "walk": even(2, 16, 8), "attack": keys(4, [3, 4, 6, 7, 10, 11]),
              "cast": keys(7, [0, 1, 2, 3, 4, 6]), "hurt": keys(8, [3, 4]), "death": keys(8, [6, 7, 8, 10])},
        "n": {"idle": keys(1, [0, 1, 2, 1]), "walk": keys(1, [0, 1, 2, 4, 5, 6, 7, 1]), "attack": keys(1, [2, 4, 5, 6, 7, 2]),
              "cast": keys(1, [0, 1, 2, 4, 5, 6]), "hurt": keys(1, [0, 1]), "death": keys(8, [6, 7, 8, 10])},
        "e": {"idle": keys(0, [0, 4, 8, 12]), "walk": even(3, 16, 8), "attack": keys(4, [3, 4, 6, 7, 10, 11]),
              "cast": keys(7, [0, 1, 2, 3, 4, 6]), "hurt": keys(8, [3, 4]), "death": keys(8, [6, 7, 8, 10])},
    },
    # La hoja no trae vista de espaldas: el norte sale de PixelLab (rotación v3 del reposo sur y animaciones v3 al norte).
    "priest": {d: {"idle": keys(0, [0, 4, 8, 12]), "walk": even(1 if d != "e" else 2, 15, 8),
                   "attack": keys(3, [8, 9, 10, 11, 12, 13]), "cast": keys(7, [0, 1, 2, 3, 4, 5]),
                   "hurt": keys(5, [12, 13]), "death": keys(6, [6, 7, 11, 12])} for d in DIRS}
              | {"n": {"idle": pl("idle", [0, 1, 2, 3]), "walk": pl("walk", list(range(8))),
                       "attack": pl("attack", list(range(6))), "cast": pl("cast", list(range(6))),
                       "hurt": pl("idle", [0, 2]), "death": keys(6, [6, 7, 11, 12])}},
}


def _split_tall(lo: int, hi: int, density: np.ndarray, typical: float) -> list[tuple[int, int]]:
    """Parte un tramo [lo, hi) que mide más de 1,6× lo típico por su línea menos densa (brillos que unen dos cuadros)."""
    if hi - lo <= 1.6 * typical:
        return [(lo, hi)]
    a, b = lo + int(0.3 * (hi - lo)), lo + int(0.7 * (hi - lo))
    cut = a + int(np.argmin(density[a:b]))
    return _split_tall(lo, cut, density, typical) + _split_tall(cut, hi, density, typical)


def _runs(filled: list[bool], min_len: int) -> list[tuple[int, int]]:
    runs, start = [], None
    for i, f in enumerate(list(filled) + [False]):
        if f and start is None:
            start = i
        elif not f and start is not None:
            if i - start > min_len:
                runs.append((start, i))
            start = None
    return runs


def find_frames(img: Image.Image) -> list[list[tuple[int, int, int, int]]]:
    """Cajas (x0, y0, x1, y1) de cada cuadro, por filas de la hoja."""
    m = np.asarray(img).astype(int).max(axis=2) > BG_LEVEL
    row_density = m.sum(axis=1)
    bands = _runs(list(row_density > 0), 8)
    typical_h = float(np.median([b - a for a, b in bands]))
    bands = [part for a, b in bands for part in _split_tall(a, b, row_density, typical_h)]
    out = []
    for y0, y1 in bands:
        col_density = m[y0:y1].sum(axis=0)
        lab, k = ndimage.label(ndimage.binary_dilation(col_density > 0, iterations=6))
        groups = [(int(np.where(lab == i)[0].min()), int(np.where(lab == i)[0].max()) + 1) for i in range(1, k + 1)]
        typical_w = float(np.median([b - a for a, b in groups]))
        row = []
        for gx0, gx1 in sorted(part for a, b in groups for part in _split_tall(a, b, col_density, typical_w)):
            sub = m[y0:y1, gx0:gx1]
            ys, xx = np.where(sub.any(axis=1))[0], np.where(sub.any(axis=0))[0]
            if len(ys) == 0 or len(xx) == 0:
                continue
            row.append((gx0 + int(xx.min()), y0 + int(ys.min()), gx0 + int(xx.max()) + 1, y0 + int(ys.max()) + 1))
        out.append(row)
    return out


def cut_out(img: Image.Image, box: tuple[int, int, int, int], bg_level: int = BG_LEVEL) -> Image.Image:
    """Recorte con el fondo negro conectado al borde transparente (el contorno oscuro interior se conserva)."""
    x0, y0, x1, y1 = box
    c = np.asarray(img.crop((x0 - 2, y0 - 2, x1 + 2, y1 + 2))).astype(np.uint8)
    lab, n = ndimage.label(c.max(axis=2) < bg_level - 2)
    background = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
    # Fondo encerrado (p. ej. dentro del arco de un tajo): negro casi puro y grande; los contornos son finos y no lo son.
    darkest = c.max(axis=2)
    sizes = ndimage.sum(np.ones_like(lab), lab, range(1, n + 1))
    means = ndimage.mean(darkest, lab, range(1, n + 1))  # la compresión deja bordes de hasta ~37: cuenta la media
    background |= {i + 1 for i in range(n) if sizes[i] >= ENCLOSED_BG_MIN_PX and means[i] < 12}
    alpha = np.where(np.isin(lab, list(background)), 0, 255).astype(np.uint8)
    # Motas oscuras sueltas que deja la compresión alrededor de la silueta con un umbral bajo.
    solid, k = ndimage.label(alpha > 0)
    if k > 1:
        areas = ndimage.sum(np.ones_like(solid), solid, range(1, k + 1))
        lum = ndimage.mean(darkest, solid, range(1, k + 1))
        for i in range(k):
            if areas[i] < 25 and lum[i] < 40:
                alpha[solid == i + 1] = 0
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


def frame_cell(frame: Image.Image) -> Image.Image:
    """Celda de SIZE×SIZE con los pies centrados en x=SIZE/2 y la planta en FEET_Y."""
    alpha = np.asarray(frame)[..., 3]
    # Los pies salen del cuerpo (la mancha mayor), no de destellos sueltos bajo él.
    lab, n = ndimage.label(alpha > 0)
    body = lab == (1 + int(np.argmax(ndimage.sum(np.ones_like(lab), lab, range(1, n + 1))))) if n > 1 else alpha > 0
    ys, xs = np.where(body)
    low = ys >= ys.max() - max(3, int(0.12 * (ys.max() - ys.min())))
    cx = int(round(xs[low].mean()))
    cell = Image.new("RGBA", (SIZE, SIZE))
    cell.paste(frame, (SIZE // 2 - cx, FEET_Y - int(ys.max()) - 1), frame)
    return cell


def build_class(class_id: str) -> bool:
    path = REFS / f"{class_id}_sheet.webp"
    if not path.exists():
        return False
    img = Image.open(path).convert("RGB")
    boxes = find_frames(img)
    box = lambda key: boxes[int(key.split(",")[0])][int(key.split(",")[1])]
    ref = box(FRAMES[class_id]["s"]["idle"][0])
    scale = STAND_HEIGHT / (ref[3] - ref[1])
    pl_dir = REFS / f"{class_id}_pixellab"
    pl_frame = lambda key: (lambda im: im.crop(im.getbbox()))(Image.open(pl_dir / key[3:]).convert("RGBA"))
    # Los cuadros de PixelLab se escalan por su propio reposo para medir lo mismo que la vista de frente.
    pl_scale = STAND_HEIGHT / pl_frame("pl:idle/frame_000.png").height if pl_dir.exists() else 1.0
    columns = sum(a["frames"] for a in ANIMS.values())
    sheet = Image.new("RGBA", (SIZE * columns, SIZE * len(DIRS)))
    anims = {}
    for r, d in enumerate(DIRS):
        col = 0
        for name, a in ANIMS.items():
            frames = FRAMES[class_id][d][name]
            assert len(frames) == a["frames"], f"{class_id}/{d}/{name}: {len(frames)} cuadros, se esperan {a['frames']}"
            anims[name] = {"column": col, **a}
            for key in frames:
                if key.startswith("pl:"):
                    frame = shrink(pl_frame(key), pl_scale)
                else:
                    frame = shrink(cut_out(img, box(key), BG_LEVEL_BY_CLASS.get(class_id, BG_LEVEL)), scale)
                sheet.paste(frame_cell(frame), (col * SIZE, r * SIZE))
                col += 1
    out = ASSETS / "sprites" / "characters"
    sheet.save(out / f"{class_id}.png")
    meta = {"frameSize": [SIZE, SIZE], "pixelScale": PIXEL_SCALE, "rows": DIRS, "feetY": FEET_Y, "anims": anims,
            "note": "w = e espejado; hoja HD (pixelScale 3) importada de tools/art/refs/%s_sheet.webp por tools/art/import_heroes.py" % class_id}
    (out / f"{class_id}.json").write_text(json.dumps(meta, indent=1) + "\n", encoding="utf-8")
    return True


def build() -> list[str]:
    return [c for c in FRAMES if build_class(c)]


if __name__ == "__main__":
    print("héroes importados en client/assets/sprites/characters/:", ", ".join(build()))
