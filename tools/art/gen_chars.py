"""Sprites de personajes, NPC y monstruos (vista 3/4, luz arriba-izquierda, contorno oscuro).

Hoja por entidad: filas = direcciones s, n, e (w = e espejado en el cliente); columnas = idle0, idle1, walk0..walk3.
Cuadro de 32×32 (pies en y=28); jefes de 48×48 o 64×64 con los pies a 4 px del borde inferior. Cada PNG va con un
.json {frameSize, rows, columns} que lee client/scripts/world/entity_sprites.gd.
"""
from __future__ import annotations

import json
import math

import numpy as np

from pix import (ASSETS, canvas, ellipse, flip_h, hline, line, outline, put, rect, rgba, save, vline)

DIRS = ["s", "n", "e"]
COLS = ["idle0", "idle1", "walk0", "walk1", "walk2", "walk3"]


def mat(base: str, light: str, dark: str) -> dict:
    return {"b": base, "l": light, "d": dark}


def shaded_rect(img, x, y, w, h, m: dict, top_light: bool = True) -> None:
    """Rectángulo con luz arriba-izquierda y sombra abajo-derecha."""
    rect(img, x, y, w, h, m["b"])
    if w >= 3 and h >= 3:
        if top_light:
            hline(img, x, x + w - 2, y, m["l"])
            vline(img, x, y, y + h - 2, m["l"])
        vline(img, x + w - 1, y + 1, y + h - 1, m["d"])
        hline(img, x + 1, x + w - 1, y + h - 1, m["d"])
    elif w >= 2:
        vline(img, x + w - 1, y, y + h - 1, m["d"])


def shaded_ellipse(img, cx, cy, rx, ry, m: dict) -> None:
    ellipse(img, cx, cy, rx, ry, m["b"])
    for y in range(int(cy - ry) - 1, int(cy + ry) + 2):
        for x in range(int(cx - rx) - 1, int(cx + rx) + 2):
            dx, dy = (x + 0.5 - cx) / max(rx, 0.1), (y + 0.5 - cy) / max(ry, 0.1)
            d = dx * dx + dy * dy
            if d <= 1.0:
                if d > 0.55 and dx + dy > 0.5:
                    put(img, x, y, m["d"])
                elif d > 0.45 and dx + dy < -0.6:
                    put(img, x, y, m["l"])


# --- Humanoides --------------------------------------------------------------------------------------------------------

SKIN = mat("peach", "skin_l", "salmon")
SKIN_DARK = mat("tan", "sand", "clay")


def humanoid(spec: dict, direction: str, frame: str, size: int = 32) -> np.ndarray:
    """Dibuja un humanoide chibi. `spec`: skin, hair, top, pants, boots, head ('hair'|'hood'|'hat'|'helm'|'cap'|'bandana'|
    'crown'|'none'), weapon ('sword'|'staff'|'dagger'|'mace'|'bow'|'pick'|'whip'|'book'|None), shield, robe (bool), scale."""
    s = spec.get("scale", 1)
    img = canvas(size, size)
    feet = size - 4
    cx = size // 2
    walk = COLS.index(frame) - 2 if frame.startswith("walk") else -1
    bob = 0
    if frame == "idle1":
        bob = 1
    if walk in (1, 3):
        bob = -1
    leg_a = {0: -1, 1: 0, 2: 1, 3: 0}.get(walk, 0)  # pierna izquierda adelantada (−) o atrás
    # Medidas (escaladas para jefes).
    head_w, head_h = 10 * s, 9 * s
    torso_w, torso_h = (10 if direction != "e" else 8) * s, 7 * s
    leg_h = 5 * s
    leg_w = 3 * s
    torso_top = feet - leg_h - torso_h + bob
    head_top = torso_top - head_h + 1 * s
    skin, top, pants, boots = spec["skin"], spec["top"], spec["pants"], spec["boots"]
    robe = spec.get("robe", False)

    def legs():
        if direction == "e":
            for i, off in enumerate([leg_a, -leg_a]):
                x = cx - leg_w // 2 + off * s
                shaded_rect(img, x, feet - leg_h, leg_w, leg_h - 1 * s, pants)
                shaded_rect(img, x + (1 if i == 0 else 0), feet - 2 * s, leg_w + 1, 2 * s, boots)
        else:
            for i, side in enumerate([-1, 1]):
                lift = (leg_a if side < 0 else -leg_a)
                x = cx + (side * 3 * s) - (leg_w if side < 0 else 0) + (1 if side < 0 else -1)
                y = feet - leg_h - min(0, lift)
                shaded_rect(img, x, y, leg_w, leg_h - 1 * s, pants)
                shaded_rect(img, x, feet - 2 * s - max(0, -lift), leg_w, 2 * s, boots)

    def torso():
        x = cx - torso_w // 2
        if robe:
            # Túnica larga hasta los pies, ensanchada abajo.
            for k in range(torso_h + leg_h - 1 * s):
                widen = k // (3 * s)
                hline(img, x - widen, x + torso_w - 1 + widen, torso_top + k, top["b"])
                put(img, x - widen, torso_top + k, top["l"])
                put(img, x + torso_w - 1 + widen, torso_top + k, top["d"])
            hline(img, x - (torso_h + leg_h) // (3 * s), x + torso_w - 1 + (torso_h + leg_h) // (3 * s), feet - 1 * s, top["d"])
            if spec.get("trim"):
                if direction != "n":
                    vline(img, cx - (0 if direction == "e" else 0), torso_top + 1, feet - 2, spec["trim"])
                hline(img, x, x + torso_w - 1, torso_top + torso_h - 2 * s, spec["trim"])
        else:
            shaded_rect(img, x, torso_top, torso_w, torso_h, top)
            if spec.get("belt"):
                hline(img, x, x + torso_w - 1, torso_top + torso_h - 2 * s, spec["belt"])
                if direction == "s":
                    put(img, cx, torso_top + torso_h - 2 * s, "gold")
            if spec.get("trim") and direction == "s":
                vline(img, cx - 1, torso_top + 1, torso_top + torso_h - 3 * s, spec["trim"])

    def arm(side: int, front: bool):
        swing = {0: 1, 1: 0, 2: -1, 3: 0}.get(walk, 0) * side
        aw = 3 * s
        if direction == "e":
            x = cx - aw // 2 + swing
        else:
            x = cx + side * (torso_w // 2) + (0 if side > 0 else -aw)
        y = torso_top + 1 * s
        shaded_rect(img, x, y, aw, torso_h - 2 * s, spec.get("sleeve", top))
        shaded_rect(img, x, y + torso_h - 2 * s, aw, 2 * s, skin)
        return (x + aw // 2, y + torso_h - 1 * s)

    def head():
        x = cx - head_w // 2 + (1 if direction == "e" else 0)
        y = head_top
        shaded_ellipse(img, x + head_w / 2, y + head_h / 2, head_w / 2, head_h / 2, skin)
        hair = spec.get("hair")
        kind = spec.get("head", "hair")
        if hair and kind in ("hair", "bandana", "crown", "cap"):
            if direction == "n":
                shaded_ellipse(img, x + head_w / 2, y + head_h / 2 - 0.5, head_w / 2, head_h / 2, hair)
            else:
                shaded_ellipse(img, x + head_w / 2, y + 2 * s, head_w / 2, 2.5 * s, hair)
                if direction == "s":
                    vline(img, x, y + 2 * s, y + head_h - 3 * s, hair["d"])
                    vline(img, x + head_w - 1, y + 2 * s, y + head_h - 3 * s, hair["d"])
                else:
                    rect(img, x, y + 1 * s, 3 * s, head_h - 3 * s, hair["b"])
        if kind == "hood":
            hood = spec["hood"]
            if direction == "n":
                shaded_ellipse(img, x + head_w / 2, y + head_h / 2, head_w / 2 + 1, head_h / 2 + 1, hood)
            else:
                shaded_ellipse(img, x + head_w / 2, y + 2 * s, head_w / 2 + 1, 4 * s, hood)
                vline(img, x - 1, y + 2 * s, y + head_h - 1 * s, hood["b"])
                if direction == "s":
                    vline(img, x + head_w, y + 2 * s, y + head_h - 1 * s, hood["d"])
                else:
                    rect(img, x - 1, y + 1 * s, 4 * s, head_h - 1 * s, hood["b"])
        if kind == "hat":
            hat = spec["hat"]
            brim_y = y + 2 * s
            hline(img, x - 2 * s, x + head_w + 1 * s, brim_y, hat["d"])
            hline(img, x - 2 * s, x + head_w + 1 * s, brim_y - 1, hat["b"])
            for k in range(7 * s):
                wdt = max(1, (head_w - 2 * s) - k * 2 // 2)
                xx = x + head_w // 2 - wdt // 2 + (k // 3 if direction != "n" else 0)
                hline(img, xx, xx + wdt - 1, brim_y - 2 - k, hat["b"])
                put(img, xx, brim_y - 2 - k, hat["l"])
            if spec.get("hat_band"):
                hline(img, x, x + head_w - 1, brim_y - 2, spec["hat_band"])
        if kind == "helm":
            helm = spec["helm"]
            shaded_ellipse(img, x + head_w / 2, y + 2 * s, head_w / 2 + 1, 3.5 * s, helm)
            if direction == "s":
                hline(img, x + 1, x + head_w - 2, y + head_h // 2, helm["d"])
                vline(img, cx, y, y + 3 * s, helm["l"])
            elif direction == "e":
                rect(img, x + head_w - 3 * s, y + head_h // 2, 3 * s, 1 * s, helm["d"])
            else:
                shaded_ellipse(img, x + head_w / 2, y + head_h / 2, head_w / 2 + 1, head_h / 2, helm)
            if spec.get("plume"):
                rect(img, cx - 1, y - 3 * s, 2 * s, 4 * s, spec["plume"])
        if kind == "cap":
            cap = spec["cap"]
            shaded_ellipse(img, x + head_w / 2, y + 2 * s, head_w / 2 + 1, 2.5 * s, cap)
            if direction != "n":
                hline(img, x - 1, x + head_w, y + 3 * s, cap["d"])
            if spec.get("lamp") and direction != "n":
                rect(img, cx - 1 + (2 if direction == "e" else 0), y, 3, 2, "gold")
                put(img, cx + (2 if direction == "e" else 0), y, "cream")
        if kind == "bandana":
            band = spec["bandana"]
            hline(img, x - 1, x + head_w, y + 2 * s, band["b"])
            hline(img, x - 1, x + head_w, y + 3 * s, band["d"])
            if direction in ("n", "e"):
                rect(img, x - 2, y + 3 * s, 2, 3 * s, band["b"])
        if kind == "crown":
            c = spec.get("crown_color", "gold")
            hline(img, x + 1, x + head_w - 2, y, c)
            for k in range(0, head_w - 2, 3 * s):
                put(img, x + 1 + k, y - 1, c)
                put(img, x + 1 + k, y - 2, "amber")

        if kind == "hood" and direction != "n":
            fx = x + 1 + (1 if direction == "e" else 0)
            shaded_ellipse(img, fx + (head_w - 2) / 2, y + head_h / 2 + 1, (head_w - 2) / 2 - (1 if direction == "e" else 0), head_h / 2 - 1.5, skin)
        if direction != "n":
            ey = y + head_h // 2 + 1 * s
            eye = spec.get("eye", "outline")
            if direction == "s":
                rect(img, x + 2 * s, ey, 1 * s, 2 * s, eye)
                rect(img, x + head_w - 3 * s, ey, 1 * s, 2 * s, eye)
                if spec.get("blush", True):
                    put(img, x + 1 * s, ey + 2 * s, "salmon" if skin is SKIN else skin["d"])
                    put(img, x + head_w - 2 * s, ey + 2 * s, "salmon" if skin is SKIN else skin["d"])
                if spec.get("beard"):
                    rect(img, x + 2 * s, ey + 2 * s, head_w - 4 * s, 2 * s, spec["beard"])
                if kind == "hood" and spec.get("mask"):
                    rect(img, x + 1, ey + 2 * s, head_w - 2, 2 * s, spec["mask"])
            else:
                rect(img, x + head_w - 3 * s, ey, 1 * s, 2 * s, eye)
                if spec.get("beard"):
                    rect(img, x + head_w - 5 * s, ey + 2 * s, 4 * s, 2 * s, spec["beard"])

    def weapon(at):
        w = spec.get("weapon")
        if not w:
            return
        hx, hy = at
        if direction == "n":
            hx += 0
        if w == "sword":
            line(img, hx, hy, hx + (3 if direction == "e" else 0), hy - 9 * s, "mist")
            put(img, hx + (3 if direction == "e" else 0), hy - 9 * s, "white")
            hline(img, hx - 2, hx + 2, hy - 1, "amber")
            line(img, hx + 1, hy, hx + (4 if direction == "e" else 1), hy - 8 * s, "silver")
        elif w == "dagger":
            line(img, hx, hy, hx + (2 if direction == "e" else 0), hy - 5, "mist")
            put(img, hx, hy, "rust")
        elif w == "staff":
            vline(img, hx, hy - 14 * s, hy + 4 * s, "rust")
            vline(img, hx + 1, hy - 13 * s, hy + 4 * s, "wine")
            gem = spec.get("gem", "sky")
            rect(img, hx - 1, hy - 17 * s, 3, 3 * s, gem)
            put(img, hx - 1, hy - 17 * s, "white")
        elif w == "mace":
            vline(img, hx, hy - 6 * s, hy + 1, "rust")
            rect(img, hx - 1, hy - 9 * s, 3, 3 * s, "silver")
            put(img, hx - 1, hy - 9 * s, "mist")
        elif w == "bow":
            for k in range(-6, 7):
                put(img, hx + 2 - int(round(math.cos(k / 6 * 1.3) * 3)), hy - 3 + k, "clay")
            vline(img, hx + 2, hy - 9, hy + 3, "mist")
        elif w == "pick":
            vline(img, hx, hy - 8 * s, hy + 1, "rust")
            hline(img, hx - 3, hx + 3, hy - 8 * s, "silver")
            put(img, hx - 3, hy - 7 * s, "silver")
            put(img, hx + 3, hy - 7 * s, "silver")
        elif w == "whip":
            line(img, hx, hy, hx + 4 * s, hy + 3 * s, "rust")
            line(img, hx + 4 * s, hy + 3 * s, hx + 7 * s, hy + 1 * s, "wine")
        elif w == "book":
            rect(img, hx - 2, hy - 2, 5, 4, "wine")
            hline(img, hx - 2, hx + 2, hy - 2, "gold")

    def shield(at):
        if not spec.get("shield") or direction == "n":
            return
        hx, hy = at
        sh = spec["shield"]
        shaded_rect(img, hx - 3, hy - 6 * s, 6 * s, 7 * s, sh)
        put(img, hx - 1 + 2 * s, hy - 3 * s, "gold")

    # Orden de dibujo según la dirección.
    if direction == "n":
        weapon_hand = arm(1, False) if not robe else (cx + torso_w // 2 + 1, torso_top + torso_h)
        weapon(weapon_hand)
        legs()
        torso()
        arm(-1, True)
        arm(1, True)
        head()
    elif direction == "e":
        legs()
        torso()
        head()
        hand = arm(1, True)
        shield((hand[0] - 2, hand[1]))
        weapon(hand)
    else:
        legs()
        torso()
        left = arm(-1, True)
        right = arm(1, True)
        head()
        weapon(right)
        shield(left)
    return outline(img)


# --- Cuadrúpedos, slime y gólem ------------------------------------------------------------------------------------------

def quadruped(spec: dict, direction: str, frame: str, size: int = 32) -> np.ndarray:
    img = canvas(size, size)
    feet = size - 4
    fur = spec["fur"]
    belly = spec.get("belly", fur)
    walk = COLS.index(frame) - 2 if frame.startswith("walk") else -1
    bob = 1 if frame == "idle1" else 0
    step = {0: 1, 1: 0, 2: -1, 3: 0}.get(walk, 0)
    if direction == "e":
        body_y = feet - 9 + bob
        for i, (x, ph) in enumerate([(9, step), (12, -step), (19, -step), (22, step)]):
            shaded_rect(img, x + ph, body_y + 4, 3, feet - body_y - 4, fur if i % 2 else mat(fur["d"], fur["b"], fur["d"]))
        shaded_ellipse(img, 16, body_y + 3, 9, 5, fur)
        hline(img, 10, 21, body_y + 7, belly["b"])
        # Cola.
        line(img, 7, body_y + 1, 4, body_y - 2 + bob, fur["d"])
        put(img, 4, body_y - 3 + bob, fur["l"])
        # Cabeza.
        hx, hy = 24, body_y - 2
        shaded_ellipse(img, hx, hy, 4.5, 4, fur)
        snout = spec.get("snout", fur)
        shaded_rect(img, hx + 2, hy, 5, 3, snout)
        put(img, hx + 6, hy, "outline")
        put(img, hx + 1, hy - 1, "outline")
        put(img, hx + 1, hy - 2, "white")
        for ex in (hx - 2, hx):
            rect(img, ex, hy - 6, 2, 3, fur["d"])
        if spec.get("tusk"):
            put(img, hx + 4, hy + 3, "mist")
            put(img, hx + 5, hy + 2, "mist")
    else:
        body_y = feet - 10 + bob
        back = direction == "n"
        for i, x in enumerate([10, 19]):
            ph = step if i == 0 else -step
            shaded_rect(img, x, body_y + 5 + max(0, ph), 3, feet - body_y - 5 - max(0, ph), fur)
        shaded_ellipse(img, 16, body_y + 3, 7, 6, fur)
        for i, x in enumerate([12, 17]):
            ph = -step if i == 0 else step
            shaded_rect(img, x, body_y + 7 + max(0, ph), 3, feet - body_y - 7 - max(0, ph), mat(fur["d"], fur["b"], fur["d"]))
        if back:
            line(img, 16, body_y, 16, body_y - 5 + bob, fur["d"])
            put(img, 16, body_y - 6 + bob, fur["l"])
            shaded_ellipse(img, 16, body_y - 2, 4.5, 3.5, fur)
            for ex in (12, 18):
                rect(img, ex, body_y - 7, 2, 3, fur["d"])
        else:
            hy = body_y + 1
            shaded_ellipse(img, 16, hy, 5, 4.5, fur)
            snout = spec.get("snout", fur)
            shaded_rect(img, 14, hy + 1, 5, 4, snout)
            put(img, 16, hy + 1, "outline")
            put(img, 17, hy + 1, "outline")
            rect(img, 13, hy - 2, 1, 2, "outline")
            rect(img, 19, hy - 2, 1, 2, "outline")
            put(img, 13, hy - 2, "white")
            put(img, 19, hy - 2, "white")
            for ex in (11, 19):
                rect(img, ex, hy - 6, 2, 3, fur["d"])
            if spec.get("tusk"):
                put(img, 13, hy + 4, "mist")
                put(img, 19, hy + 4, "mist")
    return outline(img)


def slime(spec: dict, direction: str, frame: str, size: int = 32) -> np.ndarray:
    img = canvas(size, size)
    feet = size - 4
    walk = COLS.index(frame) - 2 if frame.startswith("walk") else -1
    squash = {"idle0": 0, "idle1": 1}.get(frame, [0, -2, 0, 2][walk] if walk >= 0 else 0)
    hop = [0, 3, 1, 0][walk] if walk >= 0 else 0
    m = spec["goo"]
    rx = 8 + max(0, squash)
    ry = 6 - squash * 0.5
    cy = feet - ry - hop
    shaded_ellipse(img, 16, cy, rx, ry, m)
    shaded_ellipse(img, 16, cy - ry + 2, rx - 3, 2.5, m)
    rect(img, 12, int(cy - ry) + 2, 2, 1, m["l"])
    put(img, 12, int(cy - ry) + 3, "white")
    if direction != "n":
        off = 3 if direction == "e" else 0
        rect(img, 13 + off, int(cy), 2, 3, "outline")
        rect(img, 18 + off, int(cy), 2, 3, "outline")
        put(img, 13 + off, int(cy), "white")
        put(img, 18 + off, int(cy), "white")
        hline(img, 15 + off, 17 + off, int(cy) + 3, m["d"])
    return outline(img)


def golem(spec: dict, direction: str, frame: str, size: int = 32) -> np.ndarray:
    img = canvas(size, size)
    feet = size - 4
    walk = COLS.index(frame) - 2 if frame.startswith("walk") else -1
    bob = 1 if frame == "idle1" else (-1 if walk in (1, 3) else 0)
    step = {0: 1, 1: 0, 2: -1, 3: 0}.get(walk, 0)
    stone = spec["stone"]
    shaded_rect(img, 10, feet - 6 - max(0, step), 5, 6 + max(0, step), stone)
    shaded_rect(img, 17, feet - 6 - max(0, -step), 5, 6 + max(0, -step), stone)
    shaded_ellipse(img, 16, feet - 12 + bob, 9, 7, stone)
    for (x, y, r) in [(9, feet - 16, 3), (23, feet - 16, 3), (13, feet - 10, 2), (20, feet - 9, 2)]:
        shaded_ellipse(img, x, y + bob, r, r, stone)
    shaded_rect(img, 4 - step, feet - 15 + bob, 5, 10, stone)
    shaded_rect(img, 23 + step, feet - 15 + bob, 5, 10, stone)
    shaded_ellipse(img, 16, feet - 21 + bob, 5, 4, stone)
    if direction != "n":
        off = 2 if direction == "e" else 0
        rect(img, 13 + off, feet - 22 + bob, 2, 1, "amber")
        rect(img, 18 + off, feet - 22 + bob, 2, 1, "amber")
        put(img, 13 + off, feet - 22 + bob, "cream")
    for (x, y) in [(12, feet - 14), (19, feet - 12), (15, feet - 8)]:
        put(img, x, y + bob, "sage")
        put(img, x + 1, y + bob, "sage_l")
    return outline(img)


# --- Catálogo -------------------------------------------------------------------------------------------------------

HAIR_BROWN = mat("rust", "clay", "wine")
HAIR_BLOND = mat("amber", "gold", "rust")
HAIR_BLACK = mat("ink", "plum", "outline")
HAIR_GRAY = mat("silver", "mist", "lavgray")
LEATHER = mat("rust", "clay", "wine")
BOOTS = mat("wine", "rust", "outline")
STEEL = mat("silver", "mist", "lavgray")

CHARACTERS = {
    "characters/warrior": (humanoid, {"skin": SKIN, "hair": HAIR_BROWN, "head": "helm", "helm": STEEL, "plume": "scarlet",
                                       "top": mat("silver", "mist", "lavgray"), "sleeve": mat("red", "redl", "blood"),
                                       "pants": mat("rust", "clay", "wine"), "boots": BOOTS, "belt": "wine",
                                       "weapon": "sword", "shield": mat("blue", "sky", "navy")}),
    "characters/mage": (humanoid, {"skin": SKIN, "hair": HAIR_BLOND, "head": "hat", "hat": mat("blue", "sky", "navy"),
                                    "hat_band": "gold", "top": mat("blue", "sky", "navy"), "robe": True, "trim": "gold",
                                    "pants": mat("navy", "indigo", "outline"), "boots": BOOTS, "weapon": "staff", "gem": "ice"}),
    "characters/priest": (humanoid, {"skin": SKIN, "hair": HAIR_BLOND, "head": "crown", "crown_color": "gold", "top": mat("mist", "white", "silver"),
                                      "robe": True, "trim": "gold", "pants": mat("silver", "mist", "lavgray"), "boots": BOOTS,
                                      "weapon": "mace"}),
    "characters/rogue": (humanoid, {"skin": SKIN, "hair": HAIR_BLACK, "head": "hood", "hood": mat("sage_d", "sage", "coal"),
                                     "mask": "ink", "top": mat("mud", "olive", "outline"), "sleeve": mat("sage_d", "sage", "coal"),
                                     "pants": mat("ink", "plum", "outline"), "boots": BOOTS, "belt": "rust", "weapon": "dagger"}),
    "npcs/shopkeeper": (humanoid, {"skin": SKIN, "hair": HAIR_BROWN, "beard": "rust", "head": "cap", "cap": mat("aqua", "aqua_l", "teal"),
                                    "top": mat("cream", "white", "sand"), "belt": "rust", "pants": mat("rust", "clay", "wine"),
                                    "boots": BOOTS, "weapon": None}),
    "npcs/trainer": (humanoid, {"skin": SKIN, "hair": HAIR_GRAY, "beard": "mist", "head": "hair",
                                 "top": mat("grape", "violet", "grape_d"), "robe": True, "trim": "gold",
                                 "pants": mat("grape_d", "grape", "outline"), "boots": BOOTS, "weapon": "book"}),
    "monsters/slime": (slime, {"goo": mat("jade", "leaf", "green")}),
    "monsters/wolf": (quadruped, {"fur": mat("lavgray", "silver", "plum"), "belly": mat("silver", "mist", "lavgray"),
                                   "snout": mat("silver", "mist", "lavgray")}),
    "monsters/boar": (quadruped, {"fur": mat("rust", "clay", "wine"), "snout": mat("dusty", "taupe", "mauve"), "tusk": True}),
    "monsters/bandit": (humanoid, {"skin": SKIN_DARK, "hair": HAIR_BLACK, "head": "bandana", "bandana": mat("red", "redl", "blood"),
                                    "beard": "ink", "top": mat("mud", "olive", "outline"), "sleeve": LEATHER,
                                    "pants": mat("plum", "lavgray", "ink"), "boots": BOOTS, "belt": "outline", "weapon": "dagger",
                                    "blush": False}),
    "monsters/goblin_archer": (humanoid, {"skin": mat("moss", "lime", "olive"), "hair": None, "head": "hood",
                                           "hood": mat("olive", "moss", "mud"), "top": mat("mud", "olive", "outline"),
                                           "pants": mat("rust", "clay", "wine"), "boots": BOOTS, "weapon": "bow", "eye": "scarlet",
                                           "blush": False}),
    "monsters/kobold_miner": (humanoid, {"skin": mat("clay", "tan", "rust"), "hair": None, "head": "cap", "cap": mat("amber", "gold", "rust"),
                                          "lamp": True, "top": mat("indigo", "blue", "navy"), "pants": mat("mud", "olive", "outline"),
                                          "boots": BOOTS, "weapon": "pick", "eye": "gold", "blush": False}),
    "monsters/rubble_golem": (golem, {"stone": mat("lavgray", "silver", "plum")}),
    "monsters/skeleton_warrior": (humanoid, {"skin": mat("mist", "white", "silver"), "hair": None, "head": "helm",
                                              "helm": mat("lavgray", "silver", "plum"), "top": mat("mist", "white", "silver"),
                                              "sleeve": mat("mist", "white", "silver"), "pants": mat("plum", "lavgray", "ink"),
                                              "boots": mat("lavgray", "silver", "ink"), "weapon": "sword",
                                              "shield": mat("rust", "clay", "wine"), "eye": "scarlet", "blush": False}),
}

BOSSES = {
    "monsters/foreman": (64, {"skin": mat("moss", "lime", "olive"), "hair": None, "head": "cap", "cap": mat("amber", "gold", "rust"),
                              "lamp": True, "beard": "mud", "top": mat("rust", "clay", "wine"), "sleeve": mat("moss", "lime", "olive"),
                              "pants": mat("indigo", "blue", "navy"), "boots": BOOTS, "belt": "outline", "weapon": "whip",
                              "eye": "scarlet", "blush": False, "scale": 1}),
    "monsters/lich_king": (64, {"skin": mat("mist", "white", "silver"), "hair": None, "head": "crown", "crown_color": "gold",
                                "top": mat("grape", "violet", "grape_d"), "robe": True, "trim": "lilac",
                                "pants": mat("grape_d", "grape", "outline"), "boots": BOOTS, "weapon": "staff", "gem": "magenta",
                                "eye": "aqua_l", "blush": False, "scale": 1}),
}


def boss_frame(spec: dict, size: int, direction: str, frame: str) -> np.ndarray:
    """Jefes: humanoide de 32 dibujado a escala entera ×2 sobre un lienzo de 64."""
    small = humanoid(dict(spec, scale=1), direction, frame, 32)
    big = np.kron(small, np.ones((2, 2, 1), dtype=np.uint8))
    img = canvas(size, size)
    off_x = (size - 64) // 2
    off_y = size - 64
    for y in range(64):
        for x in range(64):
            yy, xx = y + off_y, x + off_x
            if 0 <= yy < size and 0 <= xx < size and big[y, x, 3]:
                img[yy, xx] = big[y, x]
    return img


def sheet(draw, spec: dict, size: int = 32) -> np.ndarray:
    out = canvas(size * len(COLS), size * len(DIRS))
    for r, d in enumerate(DIRS):
        for c, f in enumerate(COLS):
            fr = draw(spec, d, f, size)
            out[r * size:(r + 1) * size, c * size:(c + 1) * size] = fr
    return out


def write_meta(rel: str, size: int) -> None:
    meta = {"frameSize": [size, size], "rows": DIRS, "columns": COLS, "feetY": size - 4,
            "note": "w = e espejado; generado por tools/art/gen_chars.py"}
    path = ASSETS / "sprites" / (rel + ".json")
    path.write_text(json.dumps(meta, indent=1) + "\n", encoding="utf-8")


def shadow() -> None:
    img = canvas(16, 6)
    ellipse(img, 8, 3, 7, 2.5, rgba("outline", 110))
    ellipse(img, 8, 3, 5, 1.6, rgba("outline", 150))
    save(img, "sprites/shadow.png")
    big = canvas(32, 10)
    ellipse(big, 16, 5, 15, 4.5, rgba("outline", 110))
    ellipse(big, 16, 5, 11, 3, rgba("outline", 150))
    save(big, "sprites/shadow_big.png")


def build() -> None:
    for rel, (draw, spec) in CHARACTERS.items():
        save(sheet(draw, spec), "sprites/" + rel + ".png")
        write_meta(rel, 32)
    for rel, (size, spec) in BOSSES.items():
        out = canvas(size * len(COLS), size * len(DIRS))
        for r, d in enumerate(DIRS):
            for c, f in enumerate(COLS):
                out[r * size:(r + 1) * size, c * size:(c + 1) * size] = boss_frame(spec, size, d, f)
        save(out, "sprites/" + rel + ".png")
        write_meta(rel, size)
    shadow()


if __name__ == "__main__":
    build()
