"""Piezas de interfaz 9-slice (client/assets/ui/): marco de madera con esquinas de latón, botones de tablón, casillas
hundidas, marco de rareza para teñir, barras con marco, campo de texto, aviso y logo "PixelRealms".

Los márgenes 9-slice que usa client/scripts/ui/ui_theme.gd están en UI_MARGINS (y en el .json junto a cada PNG).
"""
from __future__ import annotations

import json

import numpy as np
from PIL import Image, ImageDraw, ImageFont

from pix import ASSETS, ROOT, canvas, hline, outline, put, rect, rgba, save, vline

WOOD = ("rust", "clay", "wine")  # base, luz, sombra
UI_MARGINS: dict[str, int] = {}


def frame(w: int, h: int, fill: tuple, fill_alpha: int, wood=WOOD, studs: bool = True, border: int = 3) -> np.ndarray:
    img = canvas(w, h)
    base, light, dark = wood
    rect(img, 0, 0, w, h, "outline")
    rect(img, 1, 1, w - 2, h - 2, base)
    hline(img, 1, w - 2, 1, light)
    vline(img, 1, 1, h - 2, light)
    hline(img, 1, w - 2, h - 2, dark)
    vline(img, w - 2, 1, h - 2, dark)
    # Veta de la madera.
    for x in range(3, w - 3, 5):
        put(img, x, 2, dark)
        put(img, x + 2, h - 3, light)
    rect(img, border, border, w - 2 * border, h - 2 * border, "outline")
    inner = border + 1
    for y in range(inner, h - inner):
        for x in range(inner, w - inner):
            img[y, x] = rgba(fill[0], fill_alpha)
    # Bisel interior: sombra arriba-izquierda (hundido), luz tenue abajo-derecha.
    hline(img, inner, w - inner - 1, inner, fill[1])
    vline(img, inner, inner, h - inner - 1, fill[1])
    if studs:
        for (x, y) in [(1, 1), (w - 3, 1), (1, h - 3), (w - 3, h - 3)]:
            rect(img, x, y, 2, 2, "gold")
            put(img, x, y, "cream")
            put(img, x + 1, y + 1, "amber")
    return img


def button(state: str) -> np.ndarray:
    w, h = 16, 16
    img = canvas(w, h)
    pal = {"normal": ("clay", "tan", "rust"), "hover": ("tan", "sand", "clay"), "pressed": ("rust", "wine", "clay"),
           "disabled": ("mauve", "dusty", "plum")}[state]
    base, light, dark = pal
    rect(img, 0, 0, w, h, "outline")
    rect(img, 1, 1, w - 2, h - 2, base)
    if state == "pressed":
        hline(img, 1, w - 2, 1, light)
        vline(img, 1, 1, h - 2, light)
        hline(img, 1, w - 2, h - 2, dark)
    else:
        hline(img, 1, w - 2, 1, light)
        vline(img, 1, 1, h - 3, light)
        hline(img, 1, w - 2, h - 2, dark)
        hline(img, 1, w - 2, h - 3, dark)
        vline(img, w - 2, 1, h - 2, dark)
    # Vetas solo en los bordes (el centro se estira bajo el texto y debe quedar liso).
    for (x0, x1) in ((2, 3), (w - 4, w - 3)):
        for y in (6, 10):
            hline(img, x0, x1, y, dark if state != "disabled" else "plum")
    if state == "hover":
        for (x, y) in [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)]:
            put(img, x, y, "gold")
    return img


def slot() -> np.ndarray:
    img = canvas(12, 12)
    rect(img, 0, 0, 12, 12, "outline")
    rect(img, 1, 1, 10, 10, "ink")
    hline(img, 1, 10, 1, "outline")
    vline(img, 1, 1, 10, "outline")
    hline(img, 2, 10, 10, "plum")
    vline(img, 10, 2, 10, "plum")
    return img


def slot_frame() -> np.ndarray:
    """Anillo blanco de 1 px con esquinas marcadas: se tiñe con el color de rareza."""
    img = canvas(12, 12)
    for i in range(12):
        put(img, i, 0, "white"); put(img, i, 11, "white"); put(img, 0, i, "white"); put(img, 11, i, "white")
    for (x, y) in [(1, 1), (10, 1), (1, 10), (10, 10)]:
        put(img, x, y, "white")
    return img


def bar_frame() -> np.ndarray:
    img = canvas(10, 8)
    rect(img, 0, 0, 10, 8, "outline")
    rect(img, 1, 1, 8, 6, "rust")
    hline(img, 1, 8, 1, "clay")
    rect(img, 2, 2, 6, 4, "outline")
    return img


def bar_fill(base: str, light: str, dark: str) -> np.ndarray:
    img = canvas(4, 4)
    hline(img, 0, 3, 0, light)
    hline(img, 0, 3, 1, base)
    hline(img, 0, 3, 2, base)
    hline(img, 0, 3, 3, dark)
    return img


def bar_back() -> np.ndarray:
    img = canvas(4, 4)
    rect(img, 0, 0, 4, 4, "outline")
    hline(img, 0, 3, 3, "ink")
    return img


def line_edit(focus: bool) -> np.ndarray:
    img = canvas(12, 12)
    rect(img, 0, 0, 12, 12, "gold" if focus else "rust")
    rect(img, 1, 1, 10, 10, "outline")
    hline(img, 1, 10, 10, "ink")
    return img


def toast() -> np.ndarray:
    img = canvas(12, 12)
    rect(img, 1, 0, 10, 12, "outline")
    rect(img, 0, 1, 12, 10, "outline")
    rect(img, 1, 1, 10, 10, "blood")
    hline(img, 1, 10, 1, "red")
    hline(img, 1, 10, 10, "wine")
    for (x, y) in [(1, 1), (10, 1), (1, 10), (10, 10)]:
        put(img, x, y, "outline")
    return img


def portrait_frame() -> np.ndarray:
    img = canvas(22, 22)
    rect(img, 1, 0, 20, 22, "outline")
    rect(img, 0, 1, 22, 20, "outline")
    rect(img, 1, 1, 20, 20, "rust")
    hline(img, 2, 19, 1, "tan")
    vline(img, 1, 2, 19, "tan")
    hline(img, 2, 20, 20, "wine")
    vline(img, 20, 2, 20, "wine")
    rect(img, 3, 3, 16, 16, "outline")
    rect(img, 4, 4, 14, 14, (0, 0, 0, 0))
    for (x, y) in [(1, 1), (19, 1), (1, 19), (19, 19)]:
        rect(img, x, y, 2, 2, "gold")
        put(img, x, y, "cream")
    return img


def scroll_grabber() -> np.ndarray:
    img = canvas(6, 8)
    rect(img, 0, 0, 6, 8, "outline")
    rect(img, 1, 1, 4, 6, "clay")
    vline(img, 1, 1, 6, "tan")
    return img


def scroll_track() -> np.ndarray:
    img = canvas(6, 8)
    rect(img, 1, 0, 4, 8, "ink")
    return img


def separator() -> np.ndarray:
    img = canvas(8, 3)
    hline(img, 0, 7, 0, "outline")
    hline(img, 0, 7, 1, "rust")
    hline(img, 0, 7, 2, "outline")
    return img


def logo() -> None:
    """Título "PixelRealms": Departure Mono (OFL, tools/art/fonts) a su tamaño nativo de 11 px, ampliado ×2 entero,
    relleno dorado por bandas, contorno de 1 px y sombra de 1 px."""
    font = ImageFont.truetype(str(ROOT / "tools" / "art" / "fonts" / "DepartureMono-Regular.otf"), 11)
    text = "PixelRealms"
    tmp = Image.new("L", (120, 18), 0)
    d = ImageDraw.Draw(tmp)
    d.fontmode = "1"
    d.text((2, 2), text, font=font, fill=255)
    m = np.array(tmp) > 127
    ys, xs = np.nonzero(m)
    m = m[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    k = 2
    big = np.kron(m, np.ones((k, k), dtype=bool))
    h, w = big.shape
    img = canvas(w + 4, h + 5)
    top = h * 0.45
    for y in range(h):
        for x in range(w):
            if big[y, x]:
                c = "cream" if y < 2 else ("gold" if y < top else ("amber" if y < h - 2 else "orange"))
                put(img, x + 1, y + 1, c)
    img = outline(img, "outline", diagonal=True)
    shadow = canvas(img.shape[1], img.shape[0])
    mask = img[:, :, 3] > 0
    shadow[1:, 1:][mask[:-1, :-1]] = rgba("outline", 160)
    shadow[mask] = img[mask]
    save(shadow, "ui/logo.png")


def checkbox(checked: bool) -> np.ndarray:
    img = canvas(10, 10)
    rect(img, 0, 0, 10, 10, "outline")
    rect(img, 1, 1, 8, 8, "rust")
    rect(img, 2, 2, 6, 6, "outline")
    if checked:
        for (x, y) in [(3, 5), (4, 6), (5, 5), (6, 4), (7, 3)]:
            put(img, x, y, "gold")
            put(img, x, y - 1, "cream")
    return img


def gear_icon() -> np.ndarray:
    """Engranaje de 12×12 para el botón de menú del HUD: latón con luz arriba a la izquierda y agujero central."""
    img = canvas(12, 12)
    body = [(x, y) for y in range(12) for x in range(12) if (x - 5.5) ** 2 + (y - 5.5) ** 2 <= 3.6 ** 2]
    teeth = [(5, 0), (6, 0), (5, 11), (6, 11), (0, 5), (0, 6), (11, 5), (11, 6), (2, 1), (1, 2), (9, 1), (10, 2), (1, 9), (2, 10), (10, 9), (9, 10)]
    for (x, y) in body + teeth:
        put(img, x, y, "amber")
    for (x, y) in body:
        if x + y <= 8:
            put(img, x, y, "gold")
        elif x + y >= 14:
            put(img, x, y, "clay")
    for (x, y) in [(5, 5), (6, 5), (5, 6), (6, 6)]:
        put(img, x, y, "ink")
    put(img, 4, 3, "cream")
    put(img, 3, 4, "cream")
    return outline(img)


def build() -> None:
    parts = {
        "ui/panel.png": (frame(24, 24, ("grape_d", "outline"), 255), 7),
        "ui/panel_light.png": (frame(24, 24, ("ink", "outline"), 245, studs=False), 7),
        "ui/tooltip.png": (frame(16, 16, ("outline", "grape_d"), 255, studs=False, border=2), 4),
        "ui/button_normal.png": (button("normal"), 4),
        "ui/button_hover.png": (button("hover"), 4),
        "ui/button_pressed.png": (button("pressed"), 4),
        "ui/button_disabled.png": (button("disabled"), 4),
        "ui/slot.png": (slot(), 3),
        "ui/slot_frame.png": (slot_frame(), 2),
        "ui/bar_frame.png": (bar_frame(), 3),
        "ui/bar_back.png": (bar_back(), 1),
        "ui/bar_hp.png": (bar_fill("red", "salmon", "blood"), 1),
        "ui/bar_mana.png": (bar_fill("blue", "sky", "navy"), 1),
        "ui/bar_energy.png": (bar_fill("amber", "gold", "rust"), 1),
        "ui/bar_rage.png": (bar_fill("crimson", "scarlet", "blood"), 1),
        "ui/bar_xp.png": (bar_fill("violet", "lilac", "grape"), 1),
        "ui/bar_cast.png": (bar_fill("gold", "cream", "amber"), 1),
        "ui/bar_enemy.png": (bar_fill("red", "coral", "blood"), 1),
        "ui/line_edit.png": (line_edit(False), 3),
        "ui/line_edit_focus.png": (line_edit(True), 3),
        "ui/toast.png": (toast(), 4),
        "ui/portrait_frame.png": (portrait_frame(), 0),
        "ui/scroll_grabber.png": (scroll_grabber(), 2),
        "ui/scroll_track.png": (scroll_track(), 2),
        "ui/separator.png": (separator(), 0),
        "ui/check_on.png": (checkbox(True), 0),
        "ui/check_off.png": (checkbox(False), 0),
        "ui/icon_menu.png": (gear_icon(), 0),
    }
    for rel, (img, margin) in parts.items():
        save(img, rel)
        UI_MARGINS[rel] = margin
    logo()
    (ASSETS / "ui" / "margins.json").write_text(json.dumps(UI_MARGINS, indent=1) + "\n", encoding="utf-8")


if __name__ == "__main__":
    build()
