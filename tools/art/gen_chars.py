"""Sprites de personajes, NPC y monstruos (vista 3/4, luz arriba-izquierda, contorno oscuro).

Hoja por entidad: filas = direcciones s, n, e (w = e espejado en el cliente); columnas = idle0, idle1, walk0..walk3,
attack0..3, cast0..2, hurt0..1, death0..3 (HU-090). Cuadro de 32×32 (pies en y=28); jefes de 64×64 con los pies a 4 px
del borde inferior. Cada PNG va con un .json {frameSize, rows, columns, feetY, anims} que lee
client/scripts/world/entity_sprites.gd: `anims` dice qué columnas, a cuántos fps y si repite cada animación.
Cada clase y monstruo ataca a su manera (`ATTACK_STYLE`): tajo amplio, estocada, bastón con brillo, maza con luz, arco,
pico, látigo, embestida, salto del slime, golpe del gólem. Todo determinista: sin azar.
"""
from __future__ import annotations

import json
import math

import numpy as np

from pix import (ASSETS, canvas, ellipse, flip_h, hline, line, outline, put, rect, rgba, save, vline)

DIRS = ["s", "n", "e"]
BASE_COLS = ["idle0", "idle1", "walk0", "walk1", "walk2", "walk3"]
COMBAT_COLS = ["attack0", "attack1", "attack2", "attack3", "cast0", "cast1", "cast2", "hurt0", "hurt1",
               "death0", "death1", "death2", "death3"]
COLS = BASE_COLS + COMBAT_COLS
# Animaciones (skill pixel-art-assets): columnas, fps y si repiten. `hurt` y `death` también existen por dirección.
ANIMS = {
    "idle": {"from": "idle0", "frames": 2, "fps": 3, "loop": True},
    "walk": {"from": "walk0", "frames": 4, "fps": 8, "loop": True},
    "attack": {"from": "attack0", "frames": 4, "fps": 12, "loop": False},
    "cast": {"from": "cast0", "frames": 3, "fps": 6, "loop": True},
    "hurt": {"from": "hurt0", "frames": 2, "fps": 10, "loop": False},
    "death": {"from": "death0", "frames": 4, "fps": 8, "loop": False},
}


def frame_kind(frame: str) -> tuple[str, int]:
    """'attack2' → ('attack', 2); 'idle1' → ('idle', 1)."""
    name = frame.rstrip("0123456789")
    return name, int(frame[len(name):] or 0)


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
    kind_name, kind_i = frame_kind(frame)
    if kind_name in ("attack", "cast", "hurt", "death"):
        return humanoid_combat(spec, direction, kind_name, kind_i, size)
    img = canvas(size, size)
    feet = size - 4
    cx = size // 2
    lx, ly = spec.get("_lean", (0, 0))
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
        x = cx - torso_w // 2 + lx
        if robe:
            # Túnica larga hasta los pies, ensanchada abajo.
            for k in range(torso_h + leg_h - 1 * s):
                widen = k // (3 * s)
                yy = torso_top + ly + k if torso_top + ly + k < feet - 1 * s else feet - 1 * s - 1
                hline(img, x - widen, x + torso_w - 1 + widen, yy, top["b"])
                put(img, x - widen, yy, top["l"])
                put(img, x + torso_w - 1 + widen, yy, top["d"])
            hline(img, x - (torso_h + leg_h) // (3 * s), x + torso_w - 1 + (torso_h + leg_h) // (3 * s), feet - 1 * s, top["d"])
            if spec.get("trim"):
                if direction != "n":
                    vline(img, cx - (0 if direction == "e" else 0), torso_top + 1, feet - 2, spec["trim"])
                hline(img, x, x + torso_w - 1, torso_top + torso_h - 2 * s, spec["trim"])
        else:
            shaded_rect(img, x, torso_top + ly, torso_w, torso_h, top)
            if spec.get("belt"):
                hline(img, x, x + torso_w - 1, torso_top + ly + torso_h - 2 * s, spec["belt"])
                if direction == "s":
                    put(img, cx + lx, torso_top + ly + torso_h - 2 * s, "gold")
            if spec.get("trim") and direction == "s":
                vline(img, cx - 1 + lx, torso_top + ly + 1, torso_top + ly + torso_h - 3 * s, spec["trim"])

    def arm(side: int, front: bool):
        swing = {0: 1, 1: 0, 2: -1, 3: 0}.get(walk, 0) * side
        aw = 3 * s
        if direction == "e":
            x = cx - aw // 2 + swing + lx
        else:
            x = cx + side * (torso_w // 2) + (0 if side > 0 else -aw) + lx
        y = torso_top + 1 * s + ly
        if spec.get("_arms_up"):
            # Casteo: brazos en alto, manos a la altura de la cabeza.
            y -= 4 * s
        if side > 0 and spec.get("_weapon_pose") is not None:
            return (x + aw // 2, y + torso_h - 1 * s)  # el brazo del arma lo dibuja weapon_pose
        shaded_rect(img, x, y, aw, torso_h - 2 * s, spec.get("sleeve", top))
        shaded_rect(img, x, y + torso_h - 2 * s, aw, 2 * s, skin)
        return (x + aw // 2, y + torso_h - 1 * s)

    def head():
        x = cx - head_w // 2 + (1 if direction == "e" else 0) + lx
        y = head_top + ly
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
        if direction != "n" and spec.get("_eyes_closed"):
            # Golpe recibido / caído: ojos apretados (rayitas).
            ey = y + head_h // 2 + 1 * s + 1
            if direction == "s":
                rect(img, x + 2 * s - 1 * s // 2, ey, 2 * s, 1 * s, "outline")
                rect(img, x + head_w - 3 * s - 1 * s // 2, ey, 2 * s, 1 * s, "outline")
            else:
                rect(img, x + head_w - 3 * s, ey, 2 * s, 1 * s, "outline")
        elif direction != "n":
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
        if not w or spec.get("_weapon_pose") is not None:
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

    def posed_weapon():
        """Brazo del arma y arma en la pose de ataque/casteo (`_weapon_pose` = {hand, angle, kind?, draw?})."""
        pose = spec.get("_weapon_pose")
        if pose is None:
            return
        if direction == "e":
            shoulder = (cx + lx, torso_top + ly + 2 * s)
        else:
            shoulder = (cx + torso_w // 2 + lx + 1, torso_top + ly + 2 * s)
        hx, hy = pose["hand"]
        sleeve = spec.get("sleeve", top)
        mx, my = (shoulder[0] + hx) // 2, (shoulder[1] + hy) // 2
        for (ax, ay, bx, by, c) in [(shoulder[0], shoulder[1], mx, my, sleeve["b"]), (mx, my, hx, hy, sleeve["b"])]:
            line(img, ax, ay, bx, by, c)
            line(img, ax + 1, ay, bx + 1, by, c)
            line(img, ax, ay + 1, bx, by + 1, sleeve["d"])
        rect(img, hx - 1, hy - 1, 2 * s, 2 * s, skin["b"])
        put(img, hx - 1, hy - 1, skin["l"])
        weapon_at(img, pose.get("kind", spec.get("weapon")), hx, hy, pose["angle"], s, spec, pose)

    # Orden de dibujo según la dirección.
    if direction == "n":
        weapon_hand = arm(1, False) if not robe else (cx + torso_w // 2 + 1, torso_top + torso_h)
        weapon(weapon_hand)
        posed_weapon()
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
        posed_weapon()
    else:
        legs()
        torso()
        left = arm(-1, True)
        right = arm(1, True)
        head()
        weapon(right)
        posed_weapon()
        shield(left)
    img = outline(img)
    for fx in spec.get("_fx", []):
        fx(img)
    return img


# --- Combate de humanoides (HU-090) ---------------------------------------------------------------------------------------

def fwd_vec(direction: str) -> tuple[int, int]:
    return {"e": (1, 0), "s": (0, 1), "n": (0, -1)}[direction]


def dir_angle(direction: str, a: float) -> float:
    """Los ángulos de las tablas están pensados mirando al este (0° = delante, −90° = arriba); se adaptan a s y n."""
    if direction == "s":
        return a + 60
    if direction == "n":
        return a * 0.5 - 60
    return a


def weapon_at(img, kind, hx, hy, ang, s, spec, pose) -> None:
    """Arma dibujada por vector desde la mano (sin rotar sprites: píxeles enteros con line)."""
    if not kind:
        return
    a = math.radians(ang)
    dx, dy = math.cos(a), math.sin(a)
    px, py = -dy, dx  # perpendicular

    def at(t: float, o: float = 0.0) -> tuple[int, int]:
        return int(round(hx + dx * t + px * o)), int(round(hy + dy * t + py * o))

    if kind in ("sword", "dagger"):
        length = (10 if kind == "sword" else 6) * s
        line(img, *at(1), *at(length), "mist")
        line(img, *at(1, 1), *at(length - 1, 1), "silver")
        put(img, *at(length), "white")
        line(img, *at(0, -2), *at(0, 2), "amber")
        put(img, *at(-2), "rust")
    elif kind == "staff":
        line(img, *at(-5 * s), *at(12 * s), "rust")
        line(img, *at(-5 * s, 1), *at(11 * s, 1), "wine")
        gx, gy = at(13 * s)
        rect(img, gx - 1, gy - 1, 3, 3, spec.get("gem", "sky"))
        put(img, gx - 1, gy - 1, "white")
    elif kind == "mace":
        line(img, *at(-1), *at(7 * s), "rust")
        mx, my = at(8 * s)
        rect(img, mx - 1, my - 1, 3 * s, 3 * s, "silver")
        put(img, mx - 1, my - 1, "mist")
    elif kind == "pick":
        line(img, *at(-1), *at(8 * s), "rust")
        line(img, *at(8 * s, -3), *at(8 * s, 3), "silver")
        put(img, *at(7 * s, -3), "silver")
        put(img, *at(7 * s, 3), "silver")
    elif kind == "whip":
        line(img, *at(-1), *at(3 * s), "rust")
        reach = pose.get("lash", 7) * s
        prev = at(3 * s)
        for k in range(4 * s, reach + 1):
            cur = at(k, math.sin(k / 3.0) * pose.get("wave", 1.5))
            line(img, *prev, *cur, "wine")
            prev = cur
    elif kind == "bow":
        draw = pose.get("draw", 0)
        top_end = at(2, -6)
        bot_end = at(2, 6)
        for k in range(-6, 7):
            put(img, *at(2 + 3 - (k * k) / 12.0, k), "clay")
        mid = at(2 - draw)
        line(img, *top_end, *mid, "mist")
        line(img, *bot_end, *mid, "mist")
        if pose.get("arrow"):
            line(img, *mid, *at(10), "clay")
            put(img, *at(10), "mist")
            put(img, *at(11), "white")
    elif kind == "book":
        rect(img, hx - 2, hy - 2, 5, 4, "wine")
        hline(img, hx - 2, hx + 2, hy - 2, "gold")


def fx_trail(hx, hy, a0, a1, radius, colors):
    """Estela del arma: arco de píxeles entre dos ángulos alrededor de la mano (sin contorno, encima del cuerpo)."""
    def draw(img):
        steps = max(3, int(abs(a1 - a0) / 9))
        for k in range(steps + 1):
            a = math.radians(a0 + (a1 - a0) * k / steps)
            for j, (r, c) in enumerate([(radius, colors[0]), (radius - 2, colors[1])]):
                if j == 1 and k % 2:
                    continue
                put(img, int(round(hx + math.cos(a) * r)), int(round(hy + math.sin(a) * r)), c)
    return draw


def fx_sparkle(x, y, size, c1, c2):
    """Destello en cruz de `size` px de brazo con centro claro."""
    def draw(img):
        for k in range(1, size + 1):
            c = c1 if k < size else c2
            for (ox, oy) in ((k, 0), (-k, 0), (0, k), (0, -k)):
                put(img, x + ox, y + oy, c)
        put(img, x, y, "white")
    return draw


def fx_speedlines(cx, top, direction, color="mist"):
    """Rayas de velocidad detrás del cuerpo (estocada, embestida)."""
    def draw(img):
        fx_, fy_ = fwd_vec(direction)
        for k, off in enumerate((-4, 0, 4)):
            if direction == "e":
                y = top + 6 + off
                hline(img, cx - 12, cx - 9 + k % 2, y, color)
            else:
                x = cx + off
                y0 = top + (22 if direction == "n" else -2)
                vline(img, x, y0 - fy_ * 0, y0 + 2 + k % 2, color)
    return draw


def fx_dust(cx, feet, direction):
    """Polvo y piedritas en el suelo al golpear (gólem, pico)."""
    def draw(img):
        fx_, _ = fwd_vec(direction)
        bx = cx + fx_ * 8
        for (ox, oy, c) in [(-4, 0, "taupe"), (-3, -1, "sage_p"), (3, 0, "taupe"), (4, -1, "sage_p"), (0, -2, "mist"),
                            (-6, -1, "silver"), (6, -2, "silver"), (-1, 1, "dusty"), (2, 1, "dusty")]:
            put(img, bx + ox, feet + oy, c)
    return draw


GLOWS = {"mage": ("ice", "sky"), "priest": ("cream", "gold"), "lich": ("pink_p", "lilac"), "trainer": ("lilac", "violet"),
         "warrior": ("gold", "amber"), "rogue": ("leaf", "jade"), "default": ("gold", "amber")}

# Ataque por clase o monstruo (si no se indica, sale del arma).
ATTACK_BY_WEAPON = {"sword": "slash", "dagger": "slash", "staff": "staff", "mace": "mace", "bow": "bow", "pick": "pick",
                    "whip": "whip", "book": "punch", None: "punch"}

# Tablas de ataque mirando al este: (alcance de la mano, altura de la mano, ángulo del arma, inclinación del cuerpo).
ATTACK_POSES = {
    "slash": [(-2, -3, -125, 0), (0, -2, -60, 1), (3, 1, 25, 1), (2, 1, 60, 0)],
    "thrust": [(-3, 0, -8, -1), (3, 0, 0, 1), (5, 0, 0, 2), (1, 1, 15, 0)],
    "staff": [(-1, -4, -100, 0), (1, -3, -60, 1), (3, -1, -25, 1), (1, 0, -70, 0)],
    "mace": [(-1, -5, -115, 0), (1, -3, -50, 1), (3, 1, 30, 1), (2, 1, 55, 0)],
    "pick": [(-1, -5, -120, 0), (1, -3, -55, 1), (3, 1, 35, 1), (2, 1, 60, 0)],
    "whip": [(-2, -3, -150, 0), (0, -4, -95, 0), (3, 0, 0, 1), (1, 0, 30, 0)],
    "bow": [(2, -1, 0, 0), (2, -1, 0, 0), (2, -1, 0, -1), (2, -1, 0, 0)],
    "punch": [(-1, 0, 0, 0), (2, 0, 0, 1), (4, 0, 0, 1), (1, 0, 0, 0)],
}


def humanoid_combat(spec: dict, direction: str, kind: str, i: int, size: int) -> np.ndarray:
    s = spec.get("scale", 1)
    feet = size - 4
    cx = size // 2
    torso_top = feet - 5 * s - 7 * s
    fx_, fy_ = fwd_vec(direction)
    glow = GLOWS.get(spec.get("glow", "default"), GLOWS["default"])
    sp = dict(spec)
    if kind == "attack":
        style = spec.get("attack") or ATTACK_BY_WEAPON.get(spec.get("weapon"), "punch")
        reach, raise_, ang, lean = ATTACK_POSES[style][i]
        if direction == "e":
            hand = (cx + 2 + reach + lean, torso_top + 5 + raise_)
            sp["_lean"] = (lean, 0)
        elif direction == "s":
            hand = (cx + 6, torso_top + 5 + raise_ + max(0, reach))
            sp["_lean"] = (0, 1 if lean > 0 else 0)
        else:
            hand = (cx + 6, torso_top + 3 + raise_ - max(0, reach) // 2)
            sp["_lean"] = (0, -1 if lean > 0 else 0)
        # El arco apunta recto hacia donde mira; el resto sigue la tabla.
        a = {"e": 0, "s": 90, "n": -90}[direction] if style == "bow" else dir_angle(direction, ang)
        pose = {"hand": hand, "angle": a}
        fx = []
        if style == "bow":
            pose.update({"draw": [0, 2, 4, 0][i], "arrow": i in (1, 2)})
            if i == 3:
                fx.append(fx_sparkle(hand[0] + fx_ * 9, hand[1] + fy_ * 9, 1, "mist", "white"))
        if style == "whip":
            pose.update({"lash": [5, 6, 13, 8][i], "wave": [1.5, 2.0, 0.6, 1.2][i]})
            if i == 2:
                tip = (int(hand[0] + math.cos(math.radians(a)) * 13), int(hand[1] + math.sin(math.radians(a)) * 13))
                fx.append(fx_sparkle(tip[0], tip[1], 2, "cream", "gold"))
        if style in ("slash", "mace", "pick") and i in (1, 2):
            prev = dir_angle(direction, ATTACK_POSES[style][i - 1][2])
            colors = ("cream", "gold") if style == "mace" and spec.get("glow") == "priest" else ("mist", "white")
            fx.append(fx_trail(hand[0], hand[1], prev, a, 10 * s if style == "slash" else 9 * s, colors))
        if style == "thrust":
            if i in (1, 2):
                fx.append(fx_speedlines(cx, torso_top, direction))
            if i == 2:
                fx.append(fx_sparkle(hand[0] + fx_ * 7, hand[1] + fy_ * 7, 2, "mist", "white"))
        if style == "staff" and i in (1, 2):
            tip = (int(round(hand[0] + math.cos(math.radians(a)) * 13 * s)), int(round(hand[1] + math.sin(math.radians(a)) * 13 * s)))
            fx.append(fx_sparkle(tip[0], tip[1], 2 if i == 1 else 4, glow[1], glow[0]))
        if style == "mace" and i == 2 and spec.get("glow") == "priest":
            mt = (int(round(hand[0] + math.cos(math.radians(a)) * 8 * s)), int(round(hand[1] + math.sin(math.radians(a)) * 8 * s)))
            fx.append(fx_sparkle(mt[0], mt[1], 4, "gold", "cream"))
        if style == "pick" and i == 2:
            fx.append(fx_dust(cx, feet, direction))
        if style == "punch" and i == 2:
            fx.append(fx_sparkle(hand[0] + fx_ * 3, hand[1] + fy_ * 3, 2, glow[1], glow[0]))
        sp["_weapon_pose"] = pose
        sp["_fx"] = fx
        return humanoid(sp, direction, "idle0", size)
    if kind == "cast":
        sp["_arms_up"] = True
        sp["_lean"] = (0, [0, -1, 0][i])
        hand = (cx + (3 if direction == "e" else 6), torso_top - 1 + [0, -1, 0][i])
        if spec.get("weapon"):
            sp["_weapon_pose"] = {"hand": hand, "angle": -90 if spec.get("weapon") != "bow" else 0}
        offs = [[(-6, -4), (6, -8), (0, -12)], [(-7, -9), (7, -3), (2, -14)], [(-5, -13), (5, -11), (-1, -6)]][i]
        sp["_fx"] = [fx_sparkle(cx + ox, torso_top + oy + 4, 1 + (k + i) % 2, glow[1], glow[0]) for k, (ox, oy) in enumerate(offs)]
        return humanoid(sp, direction, "idle0", size)
    if kind == "hurt":
        sp["_lean"] = (-fx_, -fy_ if i == 0 else 0)
        sp["_eyes_closed"] = True
        return shift(humanoid(sp, direction, "idle0", size), -fx_ * (1 if i == 0 else 0), 0)
    # death: tambalea, se arrodilla, cae y queda tendido.
    sp["_eyes_closed"] = True
    sp["_lean"] = (-fx_ * (2 if i == 0 else 1), 1)
    standing = humanoid(sp, direction, "idle0", size)
    if i == 0:
        return standing
    if i == 1:
        return kneel(standing, feet, 3 * s)
    return lay_down(standing, direction, feet, lift=(3 * s if i == 2 else 0))


# --- Ayudas de cuadro --------------------------------------------------------------------------------------------------

def shift(img: np.ndarray, dx: int, dy: int) -> np.ndarray:
    """Desplaza el cuadro en píxeles enteros (lo que sale por el borde se pierde)."""
    out = np.zeros_like(img)
    h, w = img.shape[:2]
    ys0, ys1 = max(0, dy), min(h, h + dy)
    xs0, xs1 = max(0, dx), min(w, w + dx)
    out[ys0:ys1, xs0:xs1] = img[ys0 - dy:ys1 - dy, xs0 - dx:xs1 - dx]
    return out


def bbox(img: np.ndarray):
    ys, xs = np.nonzero(img[:, :, 3])
    return xs.min(), ys.min(), xs.max(), ys.max()


def kneel(img: np.ndarray, feet: int, drop: int) -> np.ndarray:
    """Arrodillado: el torso baja `drop` px sobre las botas (las piernas desaparecen bajo él)."""
    out = canvas(img.shape[1], img.shape[0])
    boots = img[feet - 2:feet + 1]
    upper = img[:feet - 2 - drop]
    out[drop:feet - 2] = upper
    m = boots[:, :, 3] > 0
    region = out[feet - 2:feet + 1]
    region[m] = boots[m]
    return out


def lay_down(img: np.ndarray, direction: str, feet: int, lift: int = 0, on_back: bool = False) -> np.ndarray:
    """Tendido en el suelo: giro de 90° exacto (o volteo vertical si `on_back`), con el borde inferior en los pies."""
    if on_back:
        rot = img[::-1].copy()
    else:
        rot = np.rot90(img, 1 if direction in ("e", "n") else -1).copy()
    x0, y0, x1, y1 = bbox(rot)
    out = canvas(img.shape[1], img.shape[0])
    cx = img.shape[1] // 2
    dx = cx - (x0 + x1 + 1) // 2
    dy = feet - y1 - lift
    return np.maximum(out, shift(rot, dx, dy))


def squash_rows(img: np.ndarray, keep_every: int, feet: int) -> np.ndarray:
    """Aplasta el cuadro quitando una de cada `keep_every` filas (sin interpolar) y lo apoya en los pies."""
    rows = [r for r in range(img.shape[0]) if r % keep_every != 0]
    small = img[rows]
    out = canvas(img.shape[1], img.shape[0])
    x0, y0, x1, y1 = bbox(small)
    h = small.shape[0]
    top = feet - y1
    for r in range(h):
        if 0 <= r + top < out.shape[0]:
            out[r + top] = small[r]
    return out


# --- Cuadrúpedos, slime y gólem ------------------------------------------------------------------------------------------

def quadruped(spec: dict, direction: str, frame: str, size: int = 32) -> np.ndarray:
    kind, i = frame_kind(frame)
    if kind in ("attack", "cast", "hurt", "death"):
        return beast_combat(spec, direction, kind, i, size, lambda f: quadruped(spec, direction, f, size), quad=True)
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


def slime_shape(spec: dict, direction: str, rx: float, ry: float, hop: float, fwd: int, eyes: str, size: int = 32) -> np.ndarray:
    """Slime con forma dada: `eyes` = open | closed | x | none; `fwd` desplaza hacia donde mira."""
    img = canvas(size, size)
    feet = size - 4
    m = spec["goo"]
    fx_, fy_ = fwd_vec(direction)
    ox, oy = fx_ * fwd, fy_ * fwd
    cy = feet - ry - hop + oy
    cxs = 16 + ox
    shaded_ellipse(img, cxs, cy, rx, ry, m)
    if ry >= 3:
        shaded_ellipse(img, cxs, cy - ry + 2, max(1.0, rx - 3), min(2.5, ry - 1), m)
        rect(img, int(cxs) - 4, int(cy - ry) + 2, 2, 1, m["l"])
        put(img, int(cxs) - 4, int(cy - ry) + 3, "white")
    if direction != "n" and eyes != "none":
        off = 3 if direction == "e" else 0
        ex, ey = int(cxs) - 3 + off, int(cy) - (1 if ry < 4 else 0)
        for e in (ex, ex + 5):
            if eyes == "open":
                rect(img, e, ey, 2, 3, "outline")
                put(img, e, ey, "white")
            elif eyes == "closed":
                hline(img, e, e + 1, ey + 1, "outline")
            else:  # x
                put(img, e, ey, "outline"); put(img, e + 1, ey + 1, "outline"); put(img, e + 1, ey, "outline"); put(img, e, ey + 1, "outline")
        if eyes == "open":
            hline(img, ex + 2, ex + 4, ey + 3, m["d"])
    return outline(img)


def slime(spec: dict, direction: str, frame: str, size: int = 32) -> np.ndarray:
    kind, i = frame_kind(frame)
    if kind == "attack":
        # Se aplasta, se estira, salta hacia delante y cae salpicando.
        rx, ry, hop, fwd = [(10, 4, 0, 0), (6, 8, 3, 1), (7, 7, 6, 3), (11, 4, 0, 3)][i]
        img = slime_shape(spec, direction, rx, ry, hop, fwd, "open", size)
        if i == 3:
            fx_, fy_ = fwd_vec(direction)
            for (dx, dy) in [(-13, -2), (13, -3), (-10, -6), (11, -7), (0, -10)]:
                put(img, 16 + fx_ * 3 + dx, size - 4 + fy_ * 3 + dy, spec["goo"]["l"])
        return img
    if kind == "cast":
        img = slime_shape(spec, direction, 8 + i % 2, 6 - i % 2 * 0.5, 0, 0, "open", size)
        fx_sparkle(16 + [-6, 6, 0][i], 14 + [0, -2, -5][i], 1, "lime", "leaf")(img)
        return img
    if kind == "hurt":
        return slime_shape(spec, direction, 10 - i, 4 + i, 0, -1 if i == 0 else 0, "closed", size)
    if kind == "death":
        # Se deshace en un charco.
        rx, ry, eyes = [(9, 5, "closed"), (10, 3.5, "x"), (11, 2.5, "x"), (12, 1.5, "none")][i]
        return slime_shape(spec, direction, rx, ry, 0, 0, eyes, size)
    walk = COLS.index(frame) - 2 if frame.startswith("walk") else -1
    squash = {"idle0": 0, "idle1": 1}.get(frame, [0, -2, 0, 2][walk] if walk >= 0 else 0)
    hop = [0, 3, 1, 0][walk] if walk >= 0 else 0
    return slime_shape(spec, direction, 8 + max(0, squash), 6 - squash * 0.5, hop, 0, "open", size)


def golem(spec: dict, direction: str, frame: str, size: int = 32, arm_dy: int = 0) -> np.ndarray:
    kind, i = frame_kind(frame)
    if kind == "attack":
        # Alza los brazos y golpea el suelo levantando polvo.
        img = golem(spec, direction, "idle0", size, arm_dy=[-6, -9, 3, 0][i])
        if i == 2:
            fx_dust(16, size - 4, direction)(img)
            fx_dust(16, size - 4, {"e": "e", "s": "n", "n": "s"}[direction])(img)
        return img
    if kind in ("cast", "hurt", "death"):
        return beast_combat(spec, direction, kind, i, size, lambda f: golem(spec, direction, f, size), quad=False)
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
    shaded_rect(img, 4 - step, feet - 15 + bob + arm_dy, 5, 10, stone)
    shaded_rect(img, 23 + step, feet - 15 + bob + arm_dy, 5, 10, stone)
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


def beast_combat(spec: dict, direction: str, kind: str, i: int, size: int, base, quad: bool) -> np.ndarray:
    """Cuadrúpedos (embestida y mordisco) y gólem (casteo, golpe recibido y derrumbe) a partir de su cuadro de reposo."""
    feet = size - 4
    fx_, fy_ = fwd_vec(direction)
    idle = base("idle0")
    if kind == "attack":
        fwd = [-1, 2, 4, 1][i]
        img = shift(idle, fx_ * fwd, fy_ * fwd + (1 if i == 0 else 0))
        if i in (1, 2):
            fx_speedlines(16 + fx_ * fwd, feet - 12, direction)(img)
        if i == 2 and direction != "n":
            # Boca abierta / colmillos al morder o embestir.
            if direction == "e":
                mx, my = 16 + 9 + fwd, feet - 10
                hline(img, mx, mx + 3, my, "outline")
                put(img, mx + 1, my + 1, "white"); put(img, mx + 3, my + 1, "white")
            else:
                mx, my = 16, feet - 6 + fwd
                hline(img, mx - 2, mx + 2, my, "outline")
                put(img, mx - 1, my + 1, "white"); put(img, mx + 1, my + 1, "white")
            fx_sparkle(16 + fx_ * (fwd + 12), feet - 10 + fy_ * (fwd + 6), 2, "mist", "white")(img)
        return img
    if kind == "cast":
        img = shift(idle, 0, -1 if i == 1 else 0)
        fx_sparkle(16 + [-7, 7, 0][i], feet - [14, 16, 20][i], 1, "gold", "amber")(img)
        return img
    if kind == "hurt":
        return shift(idle, -fx_ * (1 if i == 0 else 0), -fy_ * (1 if i == 0 else 0))
    # death
    if i == 0:
        return shift(idle, -fx_, 1)
    if i == 1:
        return squash_rows(idle, 4, feet)
    if not quad:
        # El gólem se derrumba en un montón de piedras.
        pile = squash_rows(squash_rows(idle, 2, feet), 3 if i == 2 else 2, feet)
        for (ox, oy, c) in [(-9, 0, "lavgray"), (9, -1, "silver"), (-6, -1, "plum"), (7, 0, "lavgray")]:
            rect(pile, 16 + ox, feet - 1 + oy, 2, 2, c)
        return pile
    return lay_down(idle, direction, feet, lift=(2 if i == 2 else 0), on_back=(direction == "e"))


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
                                       "weapon": "sword", "shield": mat("blue", "sky", "navy"), "attack": "slash", "glow": "warrior"}),
    "characters/mage": (humanoid, {"skin": SKIN, "hair": HAIR_BLOND, "head": "hat", "hat": mat("blue", "sky", "navy"),
                                    "hat_band": "gold", "top": mat("blue", "sky", "navy"), "robe": True, "trim": "gold",
                                    "pants": mat("navy", "indigo", "outline"), "boots": BOOTS, "weapon": "staff", "gem": "ice", "glow": "mage"}),
    "characters/priest": (humanoid, {"skin": SKIN, "hair": HAIR_BLOND, "head": "crown", "crown_color": "gold", "top": mat("mist", "white", "silver"),
                                      "robe": True, "trim": "gold", "pants": mat("silver", "mist", "lavgray"), "boots": BOOTS,
                                      "weapon": "mace", "glow": "priest"}),
    "characters/rogue": (humanoid, {"skin": SKIN, "hair": HAIR_BLACK, "head": "hood", "hood": mat("sage_d", "sage", "coal"),
                                     "mask": "ink", "top": mat("mud", "olive", "outline"), "sleeve": mat("sage_d", "sage", "coal"),
                                     "pants": mat("ink", "plum", "outline"), "boots": BOOTS, "belt": "rust", "weapon": "dagger", "attack": "thrust", "glow": "rogue"}),
    "npcs/shopkeeper": (humanoid, {"skin": SKIN, "hair": HAIR_BROWN, "beard": "rust", "head": "cap", "cap": mat("aqua", "aqua_l", "teal"),
                                    "top": mat("cream", "white", "sand"), "belt": "rust", "pants": mat("rust", "clay", "wine"),
                                    "boots": BOOTS, "weapon": None}),
    "npcs/trainer": (humanoid, {"skin": SKIN, "hair": HAIR_GRAY, "beard": "mist", "head": "hair",
                                 "top": mat("grape", "violet", "grape_d"), "robe": True, "trim": "gold",
                                 "pants": mat("grape_d", "grape", "outline"), "boots": BOOTS, "weapon": "book", "glow": "trainer"}),
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
                                "eye": "aqua_l", "blush": False, "scale": 1, "glow": "lich"}),
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
    anims = {name: {"column": COLS.index(a["from"]), "frames": a["frames"], "fps": a["fps"], "loop": a["loop"]} for name, a in ANIMS.items()}
    meta = {"frameSize": [size, size], "rows": DIRS, "columns": COLS, "feetY": size - 4, "anims": anims,
            "note": "w = e espejado; hurt y death por dirección; generado por tools/art/gen_chars.py"}
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
