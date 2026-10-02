"""Íconos de 16×16 (se muestran ×2 en la interfaz): client/assets/icons/items/*.png y icons/spells/*.png.

Los nombres salen de content/*.json ("icon": "items/sword_worn"). Contorno oscuro de 1 px y fondo transparente en los
objetos; los hechizos llevan un fondo de color de escuela con marco para distinguirlos de un vistazo. También dibuja las
siluetas tenues de las casillas de equipo vacías (icons/slots/*.png) y los íconos de clase (icons/classes/*.png).
"""
from __future__ import annotations

import json
import math

import numpy as np

from pix import (ROOT, canvas, ellipse, hline, line, outline, put, rect, rgba, save, vline)


def ic() -> np.ndarray:
    return canvas(16, 16)


def blade(img, x0, y0, x1, y1, metal="mist", edge="silver", tip="white"):
    line(img, x0, y0, x1, y1, metal)
    line(img, x0 + 1, y0, x1 + 1, y1, edge)
    put(img, x1, y1, tip)


def sword(steel=("mist", "silver"), guard="amber", grip="rust", gem=None, long=True):
    img = ic()
    blade(img, 4, 11, 12 if long else 10, 3 if long else 5, steel[0], steel[1])
    line(img, 2, 10, 6, 14, guard)
    line(img, 3, 13, 1, 15, grip)
    put(img, 2, 14, grip)
    if gem:
        put(img, 4, 12, gem)
    return outline(img)


def dagger(steel="mist", grip="rust", curved=False):
    img = ic()
    blade(img, 6, 10, 11, 4, steel, "silver")
    if curved:
        put(img, 12, 5, steel)
    line(img, 4, 9, 7, 12, "amber")
    line(img, 5, 11, 3, 13, grip)
    return outline(img)


def axe(head="silver", haft="rust"):
    img = ic()
    line(img, 3, 14, 11, 4, haft)
    line(img, 4, 14, 12, 4, "wine")
    ellipse(img, 11, 5, 4, 3.5, head)
    ellipse(img, 12, 4, 2, 2, "mist")
    hline(img, 9, 13, 8, "lavgray")
    return outline(img)


def mace(head="silver", haft="rust", glow=None):
    img = ic()
    line(img, 3, 14, 9, 7, haft)
    ellipse(img, 11, 5, 3.5, 3.5, head)
    put(img, 10, 4, "white" if not glow else glow)
    for (x, y) in [(11, 1), (15, 5), (7, 5), (11, 9)]:
        put(img, x, y, "lavgray")
    if glow:
        put(img, 12, 6, glow)
    return outline(img)


def staff(wood="rust", gem="sky", top="claw"):
    img = ic()
    line(img, 3, 15, 11, 5, wood)
    line(img, 4, 15, 12, 5, "wine")
    ellipse(img, 12, 4, 2.6, 2.6, gem)
    put(img, 11, 3, "white")
    if top == "claw":
        put(img, 14, 2, wood)
        put(img, 10, 1, wood)
    return outline(img)


def wand(wood="clay", tip="ice"):
    img = ic()
    line(img, 4, 13, 11, 6, wood)
    line(img, 5, 13, 12, 6, "rust")
    rect(img, 11, 4, 2, 2, tip)
    put(img, 13, 3, "white")
    put(img, 14, 5, tip)
    put(img, 10, 3, tip)
    return outline(img)


def shield(face="clay", rim="silver", boss="gold"):
    img = ic()
    for y in range(2, 14):
        half = 6 if y < 9 else 6 - (y - 8)
        hline(img, 8 - half, 7 + half, y, face)
    hline(img, 2, 13, 2, rim)
    vline(img, 2, 2, 8, rim)
    vline(img, 13, 2, 8, "lavgray")
    vline(img, 7, 3, 12, "rust")
    put(img, 7, 7, boss)
    put(img, 8, 7, boss)
    return outline(img)


def helm(metal=("silver", "mist", "lavgray")):
    img = ic()
    ellipse(img, 8, 8, 6, 6, metal[0])
    rect(img, 2, 8, 12, 5, metal[0])
    rect(img, 4, 9, 8, 2, "outline")
    vline(img, 8, 2, 12, metal[1])
    hline(img, 3, 6, 4, metal[1])
    hline(img, 2, 13, 13, metal[2])
    return outline(img)


def hood(cloth=("teal", "aqua", "teal_d")):
    img = ic()
    ellipse(img, 8, 8, 6, 6.5, cloth[0])
    ellipse(img, 8, 10, 3.5, 4, "outline")
    hline(img, 4, 7, 3, cloth[1])
    vline(img, 3, 5, 10, cloth[1])
    rect(img, 2, 12, 12, 3, cloth[0])
    hline(img, 2, 13, 14, cloth[2])
    return outline(img)


def torso(cloth, trim=None, kind="shirt"):
    img = ic()
    base, light, dark = cloth
    rect(img, 4, 3, 8, 11, base)
    rect(img, 1, 4, 3, 6, base)
    rect(img, 12, 4, 3, 6, base)
    rect(img, 6, 2, 4, 2, "outline")
    vline(img, 4, 4, 13, light)
    hline(img, 1, 3, 4, light)
    vline(img, 11, 4, 13, dark)
    hline(img, 4, 11, 13, dark)
    if kind == "robe":
        rect(img, 3, 10, 10, 5, base)
        hline(img, 3, 12, 14, dark)
    if kind == "mail":
        for y in range(5, 13, 2):
            for x in range(5 + (y // 2) % 2, 11, 2):
                put(img, x, y, dark)
    if kind == "plate":
        hline(img, 4, 11, 7, dark)
        hline(img, 4, 11, 10, dark)
        put(img, 6, 5, "white")
    if trim:
        vline(img, 8, 4, 13, trim)
        hline(img, 4, 11, 9, trim)
    return outline(img)


def gloves(leather=("rust", "clay", "wine")):
    img = ic()
    rect(img, 3, 5, 6, 7, leather[0])
    rect(img, 3, 3, 1, 3, leather[0])
    rect(img, 5, 2, 1, 3, leather[0])
    rect(img, 7, 3, 1, 3, leather[0])
    rect(img, 9, 7, 2, 2, leather[0])
    rect(img, 3, 11, 6, 3, leather[2])
    vline(img, 3, 3, 11, leather[1])
    hline(img, 3, 8, 12, "amber")
    return outline(img)


def legs(cloth=("indigo", "blue", "navy")):
    img = ic()
    rect(img, 4, 2, 8, 3, cloth[0])
    rect(img, 4, 5, 3, 9, cloth[0])
    rect(img, 9, 5, 3, 9, cloth[0])
    hline(img, 4, 11, 2, "rust")
    put(img, 8, 2, "gold")
    vline(img, 4, 3, 13, cloth[1])
    vline(img, 11, 3, 13, cloth[2])
    return outline(img)


def boots(leather=("rust", "clay", "wine"), accent=None):
    img = ic()
    rect(img, 4, 3, 4, 8, leather[0])
    rect(img, 4, 10, 8, 3, leather[0])
    hline(img, 4, 12, 13, "outline")
    vline(img, 4, 3, 12, leather[1])
    hline(img, 4, 7, 3, leather[1])
    hline(img, 5, 11, 12, leather[2])
    if accent:
        hline(img, 4, 7, 6, accent)
    return outline(img)


def ring(band="gold", gem="mist"):
    img = ic()
    ellipse(img, 8, 9, 5, 5, band)
    ellipse(img, 8, 9, 3, 3, (0, 0, 0, 0))
    rect(img, 6, 3, 4, 3, gem)
    put(img, 6, 3, "white")
    return outline(img)


def necklace(chain="silver", pendant="mist", lantern=False):
    img = ic()
    for k in range(13):
        a = math.pi * k / 12
        put(img, 8 + int(round(math.cos(a) * 6)), 3 + int(round(math.sin(a) * 6)), chain)
    if lantern:
        rect(img, 6, 9, 4, 5, "amber")
        rect(img, 7, 10, 2, 3, "cream")
        hline(img, 6, 9, 8, "ink")
    else:
        line(img, 7, 9, 9, 14, pendant)
        line(img, 8, 9, 10, 13, "white")
    return outline(img)


def potion(liquid=("red", "redl", "blood")):
    img = ic()
    ellipse(img, 8, 10, 5, 4.5, liquid[0])
    ellipse(img, 8, 11, 4, 3, liquid[2])
    ellipse(img, 8, 10, 3.5, 2.5, liquid[0])
    rect(img, 6, 3, 4, 4, "mist")
    rect(img, 6, 2, 4, 1, "rust")
    put(img, 5, 8, "white")
    put(img, 6, 9, liquid[1])
    return outline(img)


def bread():
    img = ic()
    ellipse(img, 8, 9, 6.5, 4.5, "clay")
    ellipse(img, 8, 8, 5.5, 3, "tan")
    for x in (5, 8, 11):
        line(img, x, 7, x + 1, 9, "rust")
    put(img, 5, 6, "sand")
    return outline(img)


def bone():
    img = ic()
    line(img, 4, 11, 11, 4, "mist")
    line(img, 5, 11, 12, 4, "silver")
    for (x, y) in [(3, 11), (4, 12), (11, 3), (12, 4)]:
        ellipse(img, x, y, 1.6, 1.6, "white")
    return outline(img)


def goo():
    img = ic()
    ellipse(img, 8, 10, 6, 4, "jade")
    ellipse(img, 7, 8, 3, 2.5, "leaf")
    put(img, 6, 7, "white")
    put(img, 12, 13, "green")
    return outline(img)


def ore():
    img = ic()
    ellipse(img, 8, 9, 6, 5, "lavgray")
    for (x, y) in [(6, 7), (10, 9), (7, 11), (9, 6)]:
        rect(img, x, y, 2, 2, "clay")
        put(img, x, y, "sand")
    put(img, 5, 6, "silver")
    return outline(img)


def pelt():
    img = ic()
    rect(img, 3, 4, 10, 9, "rust")
    for (x, y) in [(2, 4), (13, 4), (2, 12), (13, 12)]:
        put(img, x, y, "rust")
    for y in range(5, 12, 2):
        hline(img, 4, 11, y, "clay")
    vline(img, 12, 5, 12, "wine")
    return outline(img)


def tusk():
    img = ic()
    for k in range(10):
        x = 4 + k
        y = 13 - int(round(math.sin(k / 9 * math.pi * 0.8) * 8))
        rect(img, x, y, 2, 2, "mist" if k < 7 else "white")
    rect(img, 3, 12, 2, 3, "taupe")
    return outline(img)


def purse():
    img = ic()
    ellipse(img, 8, 10, 5.5, 4.5, "rust")
    ellipse(img, 7, 9, 3, 2.5, "clay")
    hline(img, 6, 10, 5, "amber")
    rect(img, 7, 3, 2, 2, "rust")
    put(img, 5, 8, "tan")
    return outline(img)


ITEMS = {
    "items/sword_worn": lambda: sword(("silver", "lavgray"), "rust", "wine", long=False),
    "items/sword_iron": lambda: sword(),
    "items/dagger_worn": lambda: dagger("silver"),
    "items/dagger_fang": lambda: dagger("mist", "wine", curved=True),
    "items/axe_ruin": lambda: axe("lavgray"),
    "items/axe_foreman": lambda: axe("amber"),
    "items/mace_initiate": lambda: mace(),
    "items/mace_blessed": lambda: mace("gold", "rust", "white"),
    "items/staff_oak": lambda: staff("rust", "jade"),
    "items/staff_apprentice": lambda: staff("clay", "sky"),
    "items/staff_lich": lambda: staff("grape", "magenta"),
    "items/wand_novice": lambda: wand("clay", "ice"),
    "items/wand_willow": lambda: wand("sage", "leaf"),
    "items/shield_wood": lambda: shield(),
    "items/helm_iron": lambda: helm(),
    "items/hood_apprentice": lambda: hood(),
    "items/robe_novice": lambda: torso(("blue", "sky", "navy"), "gold", "robe"),
    "items/vest_leather": lambda: torso(("rust", "clay", "wine"), None, "shirt"),
    "items/mail_recruit": lambda: torso(("silver", "mist", "lavgray"), None, "mail"),
    "items/plate_foreman": lambda: torso(("amber", "gold", "rust"), None, "plate"),
    "items/plate_cryptguard": lambda: torso(("lavgray", "silver", "plum"), "violet", "plate"),
    "items/gloves_bandit": lambda: gloves(),
    "items/legs_miner": lambda: legs(),
    "items/boots_boar": lambda: boots(),
    "items/boots_shadowstep": lambda: boots(("ink", "plum", "outline"), "violet"),
    "items/ring_bone": lambda: ring("mist", "silver"),
    "items/necklace_wolf": lambda: necklace("rust", "mist"),
    "items/amulet_lantern": lambda: necklace("gold", "amber", lantern=True),
    "items/potion_red": lambda: potion(),
    "items/potion_blue": lambda: potion(("blue", "sky", "navy")),
    "items/bread": bread,
    "items/bone": bone,
    "items/goo": goo,
    "items/ore_copper": ore,
    "items/pelt": pelt,
    "items/tusk": tusk,
    "items/purse": purse,
}


# --- Hechizos ------------------------------------------------------------------------------------------------------------

SCHOOL = {
    "fire": ("blood", "red", "orange"), "frost": ("navy", "blue", "sky"), "holy": ("rust", "amber", "gold"),
    "shadow": ("grape_d", "grape", "violet"), "nature": ("pine", "green", "jade"), "phys": ("ink", "plum", "lavgray"),
    "earth": ("mud", "olive", "moss"),
}


def spell_bg(school: str) -> np.ndarray:
    dark, mid, light = SCHOOL[school]
    img = ic()
    rect(img, 0, 0, 16, 16, mid)
    for y in range(16):
        for x in range(16):
            if x + y > 18:
                put(img, x, y, dark)
    hline(img, 0, 15, 0, light)
    vline(img, 0, 0, 15, light)
    hline(img, 0, 15, 15, "outline")
    vline(img, 15, 0, 15, "outline")
    return img


def glyph_flame(img, big=False):
    ellipse(img, 8, 10, 4 if not big else 5, 4, "orange")
    for k in range(5):
        hline(img, 8 - (4 - k) // 2, 8 + (4 - k) // 2, 6 - k + (0 if not big else -1), "orange")
    ellipse(img, 8, 10, 2.5, 2.5, "gold")
    ellipse(img, 8, 11, 1.2, 1.2, "cream")


def glyph_ball(img, core="gold", outer="orange", tail=True):
    if tail:
        line(img, 2, 13, 7, 8, outer)
        line(img, 3, 13, 8, 9, outer)
    ellipse(img, 9.5, 6.5, 3.5, 3.5, outer)
    ellipse(img, 9.5, 6.5, 2, 2, core)
    put(img, 9, 6, "white")


def glyph_shard(img):
    line(img, 3, 13, 12, 4, "ice")
    line(img, 4, 13, 13, 4, "white")
    line(img, 3, 12, 11, 4, "sky")
    put(img, 13, 3, "white")


def glyph_snowflake(img, color="white"):
    for (dx, dy) in [(1, 0), (0, 1), (1, 1), (1, -1)]:
        line(img, 8 - dx * 5, 8 - dy * 5, 8 + dx * 5, 8 + dy * 5, color)
    ellipse(img, 8, 8, 1.6, 1.6, "ice")


def glyph_cone(img):
    for k in range(9):
        hline(img, 3 + k, 3 + k, 8 - k // 2, "ice")
        vline(img, 3 + k, 8 - k // 2, 8 + k // 2, "ice" if k % 2 else "white")


def glyph_slash(img, color="white"):
    line(img, 3, 12, 12, 3, color)
    line(img, 4, 12, 12, 4, "mist")
    line(img, 3, 11, 11, 3, "lavgray")


def glyph_sword(img):
    blade(img, 4, 11, 12, 3)
    line(img, 2, 10, 6, 14, "amber")


def glyph_shield(img, face="sky"):
    for y in range(3, 13):
        half = 5 if y < 9 else 5 - (y - 8)
        hline(img, 8 - half, 7 + half, y, face)
    vline(img, 7, 3, 12, "white")
    hline(img, 3, 12, 3, "white")


def glyph_cross(img, color="cream"):
    rect(img, 7, 2, 2, 12, color)
    rect(img, 3, 6, 10, 2, color)
    put(img, 7, 2, "white")


def glyph_heart(img):
    ellipse(img, 6, 6, 3, 3, "scarlet")
    ellipse(img, 10, 6, 3, 3, "scarlet")
    for k in range(6):
        hline(img, 3 + k, 12 - k, 7 + k, "scarlet")
    put(img, 5, 5, "white")


def glyph_sparkles(img, color="cream"):
    for (x, y, r) in [(6, 6, 3), (11, 10, 2), (4, 12, 1)]:
        hline(img, x - r, x + r, y, color)
        vline(img, x, y - r, y + r, color)
        put(img, x, y, "white")


def glyph_wave(img, color="cream"):
    for r in (2, 4, 6):
        for k in range(16):
            a = k / 16 * math.tau
            put(img, 8 + int(round(math.cos(a) * r)), 8 + int(round(math.sin(a) * r)), color if r < 6 else "white")


def glyph_boot(img, color="lavgray"):
    rect(img, 5, 3, 4, 7, color)
    rect(img, 5, 9, 7, 3, color)
    for k in range(3):
        hline(img, 1, 3, 5 + k * 2, "white")


def glyph_arrow(img):
    line(img, 3, 12, 12, 3, "clay")
    put(img, 12, 3, "white")
    hline(img, 10, 12, 3, "mist")
    vline(img, 12, 3, 5, "mist")
    line(img, 2, 12, 4, 14, "white")


def glyph_swirl(img, color="mist"):
    for k in range(40):
        a = k / 40 * math.tau * 1.6
        r = 1 + k / 40 * 5.5
        put(img, 8 + int(round(math.cos(a) * r)), 8 + int(round(math.sin(a) * r)), color)


def glyph_drop(img, color="jade"):
    ellipse(img, 8, 10, 4, 4, color)
    for k in range(5):
        hline(img, 8 - k // 2, 8 + k // 2, 3 + k, color)
    put(img, 7, 9, "white")


def glyph_skull(img):
    ellipse(img, 8, 7, 4.5, 4.5, "mist")
    rect(img, 6, 10, 5, 3, "mist")
    rect(img, 6, 6, 2, 2, "outline")
    rect(img, 9, 6, 2, 2, "outline")
    put(img, 8, 9, "outline")


def glyph_bolt(img, color="lilac"):
    ellipse(img, 9, 7, 3.5, 3.5, color)
    ellipse(img, 9, 7, 1.8, 1.8, "pink_p")
    line(img, 2, 13, 6, 9, color)
    put(img, 3, 11, "grape")


def glyph_fist(img):
    rect(img, 4, 5, 8, 7, "peach")
    for x in (4, 6, 8, 10):
        vline(img, x + 1, 5, 7, "salmon")
    rect(img, 4, 11, 6, 3, "peach")
    hline(img, 4, 11, 4, "skin_l")


def glyph_banner(img):
    vline(img, 4, 2, 14, "rust")
    rect(img, 5, 3, 7, 6, "scarlet")
    put(img, 11, 9, "scarlet")
    put(img, 9, 9, "scarlet")
    put(img, 7, 5, "gold")


def glyph_meteor(img):
    line(img, 1, 1, 8, 8, "gold")
    line(img, 2, 1, 9, 8, "orange")
    ellipse(img, 10, 10, 3.5, 3.5, "orange")
    ellipse(img, 10, 10, 2, 2, "ink")
    put(img, 9, 9, "plum")


def glyph_whip(img):
    for k in range(30):
        t = k / 29
        x = 3 + t * 10
        y = 12 - math.sin(t * math.pi * 1.5) * 6
        put(img, int(x), int(y), "clay")


def glyph_slam(img):
    rect(img, 6, 2, 4, 7, "silver")
    hline(img, 6, 9, 2, "mist")
    for (x0, x1) in [(1, 5), (11, 15)]:
        hline(img, x0, x1, 12, "sand")
    hline(img, 3, 13, 13, "outline")
    put(img, 4, 10, "sand")
    put(img, 12, 10, "sand")


def glyph_blink(img):
    ellipse(img, 4.5, 11.5, 2, 2, "lilac")
    ellipse(img, 11.5, 4.5, 3, 3, "pink_p")
    put(img, 11, 4, "white")
    for k in range(3):
        put(img, 6 + k * 2, 9 - k * 2, "lilac")


def glyph_field(img):
    for x in range(1, 15, 3):
        glyph_small_flame(img, x, 12)
    hline(img, 1, 14, 14, "blood")


def glyph_small_flame(img, x, y):
    vline(img, x, y - 3, y, "orange")
    put(img, x, y - 1, "gold")
    put(img, x + 1, y, "orange")


def glyph_nova(img):
    glyph_snowflake(img, "ice")
    for k in range(12):
        a = k / 12 * math.tau
        put(img, 8 + int(round(math.cos(a) * 6.5)), 8 + int(round(math.sin(a) * 6.5)), "white")


def glyph_shadowstep(img):
    for (x, a) in [(4, "grape"), (7, "violet"), (10, "lilac")]:
        ellipse(img, x, 6, 1.6, 1.6, a)
        rect(img, x - 1, 8, 3, 5, a)


def glyph_hymn(img):
    vline(img, 6, 3, 11, "cream")
    vline(img, 11, 2, 10, "cream")
    hline(img, 6, 11, 3, "cream")
    hline(img, 6, 11, 2, "cream")
    ellipse(img, 5, 12, 2, 1.5, "cream")
    ellipse(img, 10, 11, 2, 1.5, "cream")


def glyph_taunt(img):
    rect(img, 7, 2, 2, 8, "gold")
    rect(img, 7, 12, 2, 2, "gold")
    put(img, 7, 2, "cream")


def glyph_blades(img):
    for (x, y) in [(3, 11), (7, 7), (11, 3)]:
        line(img, x, y + 2, x + 2, y, "mist")
        put(img, x + 2, y, "white")


def glyph_eviscerate(img):
    glyph_slash(img)
    for k in range(3):
        put(img, 10 + k, 10 + k, "scarlet")
        put(img, 6 + k, 12, "scarlet")


def glyph_gouge(img):
    ellipse(img, 8, 8, 5, 3, "mist")
    ellipse(img, 8, 8, 2, 2, "outline")
    line(img, 3, 13, 13, 3, "scarlet")


def glyph_path(img):
    for k in range(6):
        rect(img, 2 + k * 2, 12 - k * 2, 3, 2, "gold")
    put(img, 13, 2, "white")


SPELLS = {
    "spells/fireball": ("fire", lambda i: glyph_ball(i)),
    "spells/flame_burst": ("fire", lambda i: glyph_flame(i, True)),
    "spells/burning_field": ("fire", glyph_field),
    "spells/meteor": ("fire", glyph_meteor),
    "spells/frostbolt": ("frost", glyph_shard),
    "spells/frost_nova": ("frost", glyph_nova),
    "spells/cone_of_cold": ("frost", glyph_cone),
    "spells/blink": ("shadow", glyph_blink),
    "spells/heal": ("holy", lambda i: glyph_cross(i)),
    "spells/smite": ("holy", lambda i: glyph_ball(i, "white", "cream")),
    "spells/power_shield": ("holy", lambda i: glyph_shield(i, "cream")),
    "spells/holy_pulse": ("holy", lambda i: glyph_wave(i)),
    "spells/renew": ("nature", glyph_heart),
    "spells/path_of_light": ("holy", glyph_path),
    "spells/hymn": ("holy", glyph_hymn),
    "spells/desperate_prayer": ("holy", lambda i: glyph_sparkles(i)),
    "spells/heroic_strike": ("phys", glyph_sword),
    "spells/taunt": ("fire", glyph_taunt),
    "spells/charge": ("phys", lambda i: glyph_boot(i, "silver")),
    "spells/whirlwind": ("phys", glyph_swirl),
    "spells/shield_block": ("phys", lambda i: glyph_shield(i, "silver")),
    "spells/cleave": ("phys", lambda i: glyph_slash(i)),
    "spells/hamstring": ("phys", lambda i: glyph_boot(i, "scarlet")),
    "spells/mighty_blow": ("phys", glyph_fist),
    "spells/sinister_strike": ("shadow", lambda i: glyph_slash(i)),
    "spells/gouge": ("phys", glyph_gouge),
    "spells/shadowstep": ("shadow", glyph_shadowstep),
    "spells/sprint": ("nature", lambda i: glyph_boot(i, "leaf")),
    "spells/eviscerate": ("shadow", glyph_eviscerate),
    "spells/throwing_blades": ("phys", glyph_blades),
    "spells/mortal_slash": ("fire", lambda i: glyph_slash(i, "scarlet")),
    "spells/crippling_poison": ("nature", lambda i: glyph_drop(i, "lime")),
    "spells/poison_blade": ("nature", lambda i: glyph_drop(i)),
    "spells/shoot": ("earth", glyph_arrow),
    "spells/slam": ("earth", glyph_slam),
    "spells/whip": ("earth", glyph_whip),
    "spells/rally": ("fire", glyph_banner),
    "spells/shadow_bolt": ("shadow", glyph_bolt),
}


def spell_icon(school: str, draw) -> np.ndarray:
    img = spell_bg(school)
    layer = ic()
    draw(layer)
    layer = outline(layer, "outline")
    for y in range(16):
        for x in range(16):
            if layer[y, x, 3] and 0 < x < 15 and 0 < y < 15:
                img[y, x] = layer[y, x]
    return img


# --- Siluetas de equipo y clases ----------------------------------------------------------------------------------------

def silhouette(src: np.ndarray) -> np.ndarray:
    out = ic()
    m = src[:, :, 3] > 0
    out[m] = rgba("plum", 150)
    return out


SLOTS = {
    "head": lambda: helm(), "neck": lambda: necklace(), "chest": lambda: torso(("silver", "mist", "lavgray")),
    "hands": lambda: gloves(), "legs": lambda: legs(), "feet": lambda: boots(), "ring": lambda: ring(),
    "main_hand": lambda: sword(), "off_hand": lambda: shield(),
}

CLASS_ICONS = {
    "warrior": ("phys", glyph_sword), "rogue": ("shadow", lambda i: glyph_blades(i)),
    "mage": ("frost", lambda i: glyph_ball(i, "white", "ice")), "priest": ("holy", lambda i: glyph_cross(i)),
}


def check_content() -> list[str]:
    """Íconos que pide content/ y que no se generan aquí (deben salir vacíos)."""
    wanted = set()
    for name in ("items.json", "spells.json", "auras.json"):
        data = json.loads((ROOT / "content" / name).read_text(encoding="utf-8"))
        rows = data if isinstance(data, list) else next(v for v in data.values() if isinstance(v, list))
        for r in rows:
            if r.get("icon"):
                wanted.add(r["icon"])
    return sorted(w for w in wanted if w not in ITEMS and w not in SPELLS)


def build() -> None:
    for rel, fn in ITEMS.items():
        save(fn(), "icons/" + rel + ".png")
    for rel, (school, draw) in SPELLS.items():
        save(spell_icon(school, draw), "icons/" + rel + ".png")
    for slot, fn in SLOTS.items():
        save(silhouette(fn()), "icons/slots/" + slot + ".png")
    for cls, (school, draw) in CLASS_ICONS.items():
        save(spell_icon(school, draw), "icons/classes/" + cls + ".png")
    missing = check_content()
    if missing:
        raise SystemExit("Faltan íconos: " + ", ".join(missing))


if __name__ == "__main__":
    build()
