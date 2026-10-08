"""Hojas de los monstruos del Tier 2 (HU-114 CA1): Linde del Bosque, Pantano y Cripta de Raíces.

Mismo formato que gen_chars.py (cuadro de 32×32 con los pies en y=28, filas s, n, e, las 19 columnas de HU-090 y su .json
con `write_meta`), la misma paleta Resurrect 64 y el mismo contorno. Cada monstruo tiene silueta propia, legible a distancia
de combate y distinta de los del Tier 1: los cuerpos se dibujan con una función `body(d, pose)` y `animate` saca de ella
reposo, andar, golpe recibido y muerte; el ataque y el casteo encajan con sus hechizos (content/spells.json). Los élites
(oso viejo, bruja del pantano y guardián de la cripta) llenan la celda como el gólem. Los humanoides (leñador, hombre lagarto
y esqueleto de raíces) usan `gen_chars.humanoid` con sus rasgos en `extras`. El jefe de la Cripta, el Árbol Podrido (HU-114
CA2, HU-117), va en `BOSSES` con cuadro de 64×64 (pies en y=60), como el Capataz, y sus retoños en `MONSTERS`. Determinista:
sin azar.

    python -c "import sys; sys.path.insert(0, 'tools/art'); import gen_monsters_t2; gen_monsters_t2.build()"
    python -c "import sys; sys.path.insert(0, 'tools/art'); import gen_monsters_t2; gen_monsters_t2.lamina_arbol()"
"""
from __future__ import annotations

import math

import numpy as np

import gen_chars as gc
from gen_chars import (BOOTS, SKIN, fwd_vec, frame_kind, fx_dust, fx_sparkle, fx_speedlines, fx_trail, humanoid, kneel, lay_down,
                       mat, shaded_ellipse, shaded_rect, shift, squash_rows)
from pix import canvas, ellipse, hash01, hline, line, outline, paste, put, rect, recolor, save, shade_edges, vline

FEET = 28
STEP = [1, 0, -1, 0]
WALK_BOB = [0, -1, 0, -1]


# --- Animación común ----------------------------------------------------------------------------------------------------

def animate(body, d: str, frame: str, attack, cast, death) -> np.ndarray:
    """Reposo y andar salen de `body` con `bob`/`step`; el golpe recibido cierra los ojos y retrocede 1 px; el ataque, el
    casteo y la muerte los decide cada monstruo."""
    kind, i = frame_kind(frame)
    fx_, fy_ = fwd_vec(d)
    if kind == "idle":
        return body(d, {"bob": i, "phase": i})
    if kind == "walk":
        return body(d, {"step": STEP[i], "bob": WALK_BOB[i], "phase": i, "walking": True})
    if kind == "attack":
        return attack(d, i)
    if kind == "cast":
        return cast(d, i)
    if kind == "hurt":
        return shift(body(d, {"eyes": "closed", "hurt": True}), -fx_ * (i == 0), -fy_ * (i == 0))
    return death(d, i)


def fall_over(body, d: str, i: int, on_back: bool | None = None) -> np.ndarray:
    """Muerte de bestia, como los cuadrúpedos del Tier 1: retrocede, se encoge y cae de lado (o panza arriba)."""
    fx_, _ = fwd_vec(d)
    if i == 0:
        return shift(body(d, {"eyes": "closed"}), -fx_, 1)
    if i == 1:
        return squash_rows(body(d, {"eyes": "closed"}), 4, FEET)
    back = (d == "e") if on_back is None else on_back
    return lay_down(body(d, {"eyes": "x"}), d, FEET, lift=(2 if i == 2 else 0), on_back=back)


def eye(img, x: int, y: int, mode: str, color: str, h: int = 1) -> None:
    """Ojo de 1 px de ancho: abierto (color), cerrado (rayita oscura) o muerto (aspa de 2×2)."""
    if mode == "open":
        vline(img, x, y, y + h - 1, color)
    elif mode == "closed":
        put(img, x, y + h - 1, "outline")
        put(img, x + 1, y + h - 1, "outline")
    else:
        for (ox, oy) in ((0, 0), (1, 1), (1, 0), (0, 1)):
            put(img, x + ox, y + oy, "outline")


def fx_arcs(x: int, y: int, direction: str, r: int, color: str):
    """Ondas de sonido (rugido, aullido): dos arcos que se abren hacia `direction` (s, n, e, w o ne)."""
    def draw(img):
        base = {"e": 0, "s": 90, "n": -90, "w": 180, "ne": -50}[direction]
        for rr in (r, r + 3):
            for a in range(-40, 41, 10):
                t = math.radians(base + a)
                put(img, int(round(x + math.cos(t) * rr)), int(round(y + math.sin(t) * rr)), color)
    return draw


def fx_pixels(points, color: str):
    """Píxeles sueltos encima del cuadro (chispas, gotas, esporas)."""
    def draw(img):
        for (x, y) in points:
            put(img, x, y, color)
    return draw


def finish(img: np.ndarray, fx=(), color: str = "outline") -> np.ndarray:
    out = outline(img, color)
    for f in fx:
        f(out)
    return out


def leg(img, root, knee, foot, m: dict) -> None:
    """Pata articulada de 1 px (araña) en el color base, con la rodilla clara."""
    line(img, *root, *knee, m["b"])
    line(img, *knee, *foot, m["b"])
    put(img, *knee, m["l"])


# --- Linde del Bosque -----------------------------------------------------------------------------------------------------

# Lobo del bosque: más largo de patas y de hocico que el lobo gris de las colinas, orejas en punta, cola tupida y una
# silla oscura en el lomo; pardo de bosque con ojos verdes.
WOLF_FUR = mat("taupe", "sage_p", "olive")
WOLF_SADDLE = mat("coal", "sage_d", "outline")
WOLF_PALE = mat("sage_p", "mist", "taupe")
WOLF_EYE = "lime"


def forest_wolf_body(d: str, p: dict) -> np.ndarray:
    img = canvas(32, 32)
    F, S, P = WOLF_FUR, WOLF_SADDLE, WOLF_PALE
    far = mat(F["d"], F["b"], F["d"])
    st, eyes, mouth, howl = p.get("step", 0), p.get("eyes", "open"), p.get("mouth", 0), p.get("howl", False)
    if d == "e":
        by = 19 + p.get("bob", 0)
        for x, ph in ((7, -st), (18, st)):
            shaded_rect(img, x + ph, by + 1, 2, FEET - by - 1, far)
        shaded_ellipse(img, 4.5, by - 1, 3, 1.7, F)
        shaded_ellipse(img, 2.2, by + 0.5 + 0.5 * st, 1.7, 1.4, P)
        shaded_ellipse(img, 13, by, 7.5, 3.5, F)
        ellipse(img, 12, by - 2, 5.5, 1.5, S["b"])
        hline(img, 8, 11, by - 3, S["l"])
        hline(img, 9, 17, by + 3, P["b"])
        for x, ph in ((9, st), (19, -st)):
            shaded_rect(img, x + ph, by + 2, 2, FEET - by - 2, F)
            put(img, x + ph + 1, FEET - 1, P["d"])
        hx, hy = 21 + p.get("reach", 0), by - 6
        if howl:
            hx, hy = hx - 1, hy - 2
        shaded_rect(img, 17, hy + 1, 4, by - hy, F)
        vline(img, 20, hy + 3, by + 1, P["b"])
        # Orejas en punta (la lejana más oscura).
        hline(img, hx - 3, hx - 2, hy - 3, F["d"])
        put(img, hx - 3, hy - 4, F["d"])
        hline(img, hx - 1, hx + 1, hy - 3, F["b"])
        hline(img, hx, hx + 1, hy - 4, F["b"])
        put(img, hx, hy - 5, F["l"])
        put(img, hx, hy - 3, F["d"])
        shaded_ellipse(img, hx, hy, 3.5, 2.8, F)
        if howl:
            for k, (x0, x1) in enumerate(((2, 3), (2, 4), (3, 5), (4, 5))):  # hocico hacia el cielo
                hline(img, hx + x0, hx + x1, hy - k, F["b"])
                put(img, hx + x0, hy - k, F["l"])
            put(img, hx + 5, hy - 4, "outline")
            hline(img, hx + 3, hx + 4, hy + 1, P["b"])
            put(img, hx + 5, hy, "outline")
        else:
            hline(img, hx + 2, hx + 5, hy - 1, F["l"])
            hline(img, hx + 2, hx + 6, hy, F["b"])
            put(img, hx + 6, hy - 1, "outline")
            if mouth:
                hline(img, hx + 3, hx + 6, hy + 1, "outline")
                put(img, hx + 5, hy + 1, "white")
                hline(img, hx + 2, hx + 5, hy + 2, P["b"])
            else:
                hline(img, hx + 2, hx + 5, hy + 1, P["b"])
        eye(img, hx + 1, hy - 1, eyes, WOLF_EYE)
        return shift(img, 1, 0)
    by = 20 + p.get("bob", 0)
    if d == "s":
        for x in (11, 19):
            shaded_rect(img, x, by + 3, 2, FEET - by - 3, far)
        shaded_ellipse(img, 16, by, 5, 4.5, F)
        ellipse(img, 16, by + 1, 2, 2.6, P["b"])
        for x, lift in ((13, max(0, st)), (17, max(0, -st))):
            shaded_rect(img, x, by + 2, 2, FEET - by - 2 - lift, F)
        hy = by - 6 - (1 if howl else 0)
        for side in (-1, 1):  # orejas en punta
            ex = 16 + side * 4
            hline(img, ex - 1, ex + 1, hy - 3, F["b"])
            hline(img, ex - (1 if side < 0 else 0), ex + (0 if side < 0 else 1), hy - 4, F["b"])
            put(img, ex + side, hy - 5, F["l"])
            put(img, ex, hy - 3, F["d"])
        shaded_ellipse(img, 16, hy, 4.5, 3.5, F)
        for x in (11, 12, 19, 20):  # carrillos claros
            put(img, x, hy + 1, P["b"])
        shaded_rect(img, 14, hy + 1, 4, 4, P)
        if mouth or howl:
            rect(img, 14, hy + 4, 4, 2, "outline")
            put(img, 14, hy + 4, "white")
            put(img, 17, hy + 4, "white")
            hline(img, 15, 16, hy + 2, "outline")
        else:
            hline(img, 15, 16, hy + 4, "outline")
        put(img, 14, hy - 2, F["d"])
        put(img, 17, hy - 2, F["d"])
        eye(img, 13, hy - 1, eyes, WOLF_EYE)
        eye(img, 18, hy - 1, eyes, WOLF_EYE)
        return img
    # n: de espaldas, con la raya oscura del lomo y la cola colgando entre las patas traseras.
    for x in (11, 19):
        shaded_rect(img, x, by + 2, 2, FEET - by - 2, far)
    shaded_ellipse(img, 16, by, 5, 4.5, F)
    ellipse(img, 16, by - 1, 1.6, 4, F["d"])
    hy = by - 6 - (1 if howl else 0)
    for side in (-1, 1):
        ex = 16 + side * 4
        hline(img, ex - 1, ex + 1, hy - 2, F["b"])
        hline(img, ex - (1 if side < 0 else 0), ex + (0 if side < 0 else 1), hy - 3, F["b"])
        put(img, ex + side, hy - 4, F["l"])
    shaded_ellipse(img, 16, hy, 4, 3, F)
    vline(img, 16, hy - 1, hy + 2, F["d"])
    for x, lift in ((13, max(0, st)), (17, max(0, -st))):
        shaded_rect(img, x, by + 3, 2, FEET - by - 3 - lift, F)
    shaded_ellipse(img, 16 + 0.6 * st, by + 4, 1.8, 3, F)
    put(img, int(16 + 0.6 * st), by + 7, P["b"])
    return img


def forest_wolf(spec: dict, d: str, frame: str, size: int = 32) -> np.ndarray:
    body = lambda dd, p: finish(forest_wolf_body(dd, p))
    fx_, fy_ = fwd_vec(d)

    def attack(dd, i):  # embestida y mordisco
        fwd = [-1, 1, 2, 0][i]
        img = shift(finish(forest_wolf_body(dd, {"bob": 1 if i == 0 else 0, "mouth": i in (1, 2), "reach": 1 if i == 2 else 0})),
                    fx_ * fwd, fy_ * fwd)
        if i in (1, 2):
            fx_speedlines(16 + fx_ * fwd, FEET - 12, dd)(img)
        if i == 2 and dd != "n":
            mx, my = (29, 14) if dd == "e" else (16, 27)
            fx_sparkle(mx, my, 2, "mist", "white")(img)
        return img

    def cast(dd, i):  # aullido
        mouth = {"e": (24, 9), "s": (16, 12), "n": (16, 9)}[dd]
        return finish(forest_wolf_body(dd, {"howl": True, "bob": 0}), [fx_arcs(*mouth, "ne" if dd == "e" else "n", 1 + i, "sage_p")])

    return animate(body, d, frame, attack, cast, lambda dd, i: fall_over(body, dd, i))


# Leñador bandido: humanoide fornido con gorro de lana, barba roja, camisa de cuadros y un hacha grande (ataque de arriba
# abajo que levanta polvo; el casteo alza el hacha: "Hachazo").
def woodcutter_extras(img, stage: str, d: str, c: dict) -> None:
    hx, hy, hw, hh = c["head"]
    if stage == "torso":
        x, y, w, h = c["torso"]
        for xx in range(x + 1, x + w - 1, 3):  # cuadros
            vline(img, xx, y + 1, y + h - 3, "blood")
        if d != "n":
            hline(img, x, x + w - 1, y + 2, "blood")
    if stage == "over" and d != "n":
        bx = hx + (2 if d == "s" else hw - 5)
        rect(img, bx, hy + hh - 1, 6 if d == "s" else 4, 1, "rust")  # barba que cae sobre el pecho
        put(img, bx + (2 if d == "s" else 1), hy + hh, "rust")
        put(img, bx + (3 if d == "s" else 2), hy + hh, "wine")


WOODCUTTER = {"skin": SKIN, "hair": mat("rust", "clay", "wine"), "head": "cap", "cap": mat("olive", "moss", "mud"),
              "beard": "rust", "top": mat("red", "redl", "blood"), "sleeve": mat("red", "redl", "blood"), "belt": "outline",
              "pants": mat("mud", "olive", "outline"), "boots": BOOTS, "weapon": "axe", "attack": "chop", "blush": False,
              "glow": "woodcutter", "extras": woodcutter_extras}


# Araña tejedora: vista desde arriba como pide la cámara 3/4, ocho patas largas en arco con las rodillas en alto, abdomen
# rojizo con galones claros y ojos rojos. Escupe a distancia y el casteo ("Telaraña") alza el abdomen soltando hilo.
SPIDER_ABD = mat("rust", "clay", "wine")
SPIDER_MARK = "sand"
SPIDER_BODY = mat("mud", "olive", "outline")
SPIDER_LEG = mat("mud", "taupe", "outline")
SPIDER_EYE = "scarlet"

# Patas del lado izquierdo como (raíz, rodilla, pie) relativas a `by`; la primera es la delantera. El lado derecho es su
# espejo (x → 31 − x). En `e` van primero las cuatro del lado lejano (arriba) y luego las del cercano (abajo).
SPIDER_LEGS = {
    "s": [((13, 0), (9, -3), (7, 6)), ((13, -1), (7, -5), (3, 2)), ((13, -2), (6, -8), (2, -3)), ((14, -3), (8, -11), (4, -8))],
    "n": [((14, -9), (9, -13), (5, -9)), ((13, -8), (7, -11), (3, -5)), ((13, -7), (6, -7), (2, 0)), ((14, -6), (8, -3), (6, 6))],
    "e": [((21, -2), (25, -8), (29, -4)), ((20, -2), (22, -9), (24, -6)), ((18, -2), (15, -9), (12, -6)), ((17, -2), (10, -9), (6, -5)),
          ((21, 1), (25, -3), (28, 6)), ((20, 1), (22, -2), (23, 6)), ((18, 1), (14, -2), (12, 6)), ((17, 1), (9, -3), (5, 6))],
}


def spider_legs(d: str, by: int, st: int, rear: int, curl: bool) -> list:
    """Patas como (raíz, rodilla, pie, lejana). Al andar alternan; `rear` alza las delanteras; `curl` las encoge."""
    table = SPIDER_LEGS[d]
    legs = []
    if d == "e":
        sides = [(-1, k, leg_) for k, leg_ in enumerate(table)]
    else:
        sides = [(side, k, leg_) for side in (-1, 1) for k, leg_ in enumerate(table)]
    for side, k, ((rx, ry), (kx, ky), (fx, fy)) in sides:
        if side > 0:
            rx, kx, fx = 31 - rx, 31 - kx, 31 - fx
        front = (k % 4 == 0) if d != "n" else (k == 0)
        up = 1 if st and ((k + (side > 0)) % 2 == 0) == (st > 0) else 0
        r = (rx, by + ry)
        kn = (kx, by + ky - up - (2 * rear if front else 0))
        f = (fx, min(FEET - 1, by + fy - up - (4 * rear if front else 0)))
        if curl:
            kn = ((rx + kx) // 2, (r[1] + kn[1]) // 2 - 1)
            f = (kn[0] + (1 if kx > rx else -1), r[1] + 1)
        legs.append((r, kn, f, d == "e" and k < 4))
    return legs


def weaver_spider_body(d: str, p: dict) -> np.ndarray:
    img = canvas(32, 32)
    eyes, lift = p.get("eyes", "open"), p.get("abd", 0)
    by = 21 + p.get("bob", 0)
    lg = spider_legs(d, by, p.get("step", 0), p.get("rear", 0), p.get("curl", False))
    far = mat("mud", "olive", "outline")
    for (r, kn, f, back) in lg:
        if back or d != "e":
            leg(img, r, kn, f, far if back else SPIDER_LEG)
    if d == "e":
        ay = by - 2 - lift
        shaded_ellipse(img, 10.5, ay, 6, 4.2, SPIDER_ABD)
        for x in (8, 11, 14):  # galones
            put(img, x, ay - 2, SPIDER_MARK)
            put(img, x - 1, ay - 1, SPIDER_MARK)
            put(img, x, ay, SPIDER_MARK)
        put(img, 4, ay + 1, "clay")
        shaded_ellipse(img, 19.5, by - 1, 3.6, 2.8, SPIDER_BODY)
        eye(img, 22, by - 2, eyes, SPIDER_EYE)
        put(img, 21, by - 3, SPIDER_EYE if eyes == "open" else SPIDER_BODY["b"])
        put(img, 23, by + 1, "mist")
        put(img, 22, by + 2, "mist")
        for (r, kn, f, back) in lg:
            if not back:
                leg(img, r, kn, f, SPIDER_LEG)
        return img
    if d == "s":
        ay = by - 6 - lift
        shaded_ellipse(img, 16, ay, 5.5, 4.2, SPIDER_ABD)
        for k in range(2):
            line(img, 13 + k, ay - 2 + 2 * k, 15, ay - 1 + 2 * k, SPIDER_MARK)
            line(img, 16, ay - 1 + 2 * k, 18 - k, ay - 2 + 2 * k, SPIDER_MARK)
        shaded_ellipse(img, 16, by - 1, 3.8, 3, SPIDER_BODY)
        for x in (14, 17):
            eye(img, x, by - 2, eyes, SPIDER_EYE)
        if eyes == "open":
            put(img, 13, by - 3, SPIDER_EYE)
            put(img, 18, by - 3, SPIDER_EYE)
        put(img, 15, by + 1, "mist")
        put(img, 16, by + 1, "mist")
        return img
    shaded_ellipse(img, 16, by - 8, 3.6, 2.8, SPIDER_BODY)
    ay = by - 2 - lift
    shaded_ellipse(img, 16, ay, 6, 4.8, SPIDER_ABD)
    for k in range(3):  # galones vistos desde atrás
        line(img, 13 + k, ay - 3 + 2 * k, 15, ay - 2 + 2 * k, SPIDER_MARK)
        line(img, 16, ay - 2 + 2 * k, 18 - k, ay - 3 + 2 * k, SPIDER_MARK)
    put(img, 15, ay + 4, "wine")
    put(img, 16, ay + 4, "wine")
    return img


def weaver_spider(spec: dict, d: str, frame: str, size: int = 32) -> np.ndarray:
    body = lambda dd, p: finish(weaver_spider_body(dd, p))
    fx_, fy_ = fwd_vec(d)

    def attack(dd, i):  # se alza y escupe hilo
        fwd = [-1, -1, 1, 0][i]
        img = shift(finish(weaver_spider_body(dd, {"rear": [1, 2, 0, 0][i], "abd": [0, 1, 0, 0][i]})), fx_ * fwd, fy_ * fwd)
        if i == 2:
            tip = {"e": (29, 19), "s": (16, 28), "n": (16, 7)}[dd]
            fx_sparkle(*tip, 2, "mist", "white")(img)
        return img

    def cast(dd, i):  # "Telaraña": el abdomen en alto soltando hilos
        img = finish(weaver_spider_body(dd, {"abd": 2, "rear": 1}))
        cx, cy = {"e": (5, 13), "s": (16, 8), "n": (16, 24)}[dd]
        strands = []
        for k in range(3):
            a = math.radians((-150 if dd != "n" else 30) + 50 * k + 15 * i)
            strands += [(int(round(cx + math.cos(a) * r)), int(round(cy + math.sin(a) * r))) for r in range(2, 6)]
        fx_pixels(strands, "mist")(img)
        fx_sparkle(cx + [-2, 2, 0][i], cy - [2, 1, 3][i], 1, "mist", "white")(img)
        return img

    def death(dd, i):  # se encoge y queda patas arriba
        if i < 2:
            return fall_over(body, dd, i)
        dead = finish(weaver_spider_body(dd, {"eyes": "x", "curl": True}))
        return lay_down(dead, dd, FEET, lift=(2 if i == 2 else 0), on_back=True)

    return animate(body, d, frame, attack, cast, death)


# Oso viejo (élite): mole parda que llena la celda, con el manto de los hombros canoso, musgo en el lomo, flechas rotas
# clavadas, una cicatriz en el ojo y ojos ámbar que brillan. El zarpazo deja tres estelas; el casteo ("Rugido",
# "Zarpazo") lo pone de pie rugiendo.
BEAR = mat("mud", "olive", "outline")
BEAR_FAR = mat("coal", "mud", "outline")
BEAR_MANTLE = mat("silver", "mist", "lavgray")
BEAR_MUZZLE = mat("taupe", "sage_p", "dusty")
BEAR_MOSS = mat("green", "leaf", "pine")
BEAR_EYE = "amber"


def mantle(img, cx: float, cy: float, rx: float, ry: float, seed: int = 1) -> None:
    """Pelo canoso: mancha gris de borde deshilachado, solo sobre el pelaje (no agranda la silueta)."""
    for y in range(int(cy - ry) - 1, int(cy + ry) + 2):
        for x in range(int(cx - rx) - 1, int(cx + rx) + 2):
            if not (0 <= x < 32 and 0 <= y < 32) or not img[y, x, 3]:
                continue
            dx, dy = (x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry
            dd = dx * dx + dy * dy
            if dd > 0.5 + 0.5 * hash01(seed, x, y):
                continue
            if dy < -0.45 and dd > 0.35:
                put(img, x, y, BEAR_MANTLE["l"])
            elif hash01(seed + 7, x, y) < 0.22:
                put(img, x, y, BEAR_MANTLE["d"])
            else:
                put(img, x, y, BEAR_MANTLE["b"])


def arrow_stub(img, x: int, y: int, dx: int, dy: int) -> None:
    """Flecha rota clavada: astil y plumas."""
    line(img, x, y, x + dx * 3, y + dy * 3, "clay")
    put(img, x + dx * 4, y + dy * 4, "white")
    put(img, x + dx * 4 - 1, y + dy * 4, "scarlet")


def moss(img, x: float, y: float, rx: float) -> None:
    shaded_ellipse(img, x, y, rx, 1.3, BEAR_MOSS)
    put(img, int(x) - 1, int(y) - 1, "pale")


def bear_head_s(img, hy: int, eyes: str, mouth: bool) -> None:
    B, M = BEAR, BEAR_MUZZLE
    for ex in (10.5, 21.5):
        shaded_ellipse(img, ex, hy - 4, 2, 2, B)
        put(img, int(ex), hy - 4, "dusty")
    shaded_ellipse(img, 16, hy, 6, 5, B)
    shaded_ellipse(img, 16, hy + 2.5, 3.2, 2.4, M)
    rect(img, 15, hy + 1, 2, 1, "outline")
    if mouth:
        rect(img, 14, hy + 3, 4, 2, "outline")
        put(img, 14, hy + 3, "white")
        put(img, 17, hy + 3, "white")
    else:
        hline(img, 15, 16, hy + 3, M["d"])
    eye(img, 13, hy - 1, eyes, BEAR_EYE)
    eye(img, 18, hy - 1, eyes, BEAR_EYE)
    put(img, 12, hy - 3, "rose")  # cicatriz
    put(img, 14, hy, "rose")


def bear_head_e(img, hx: int, hy: int, eyes: str, mouth: bool) -> None:
    B, M = BEAR, BEAR_MUZZLE
    shaded_ellipse(img, hx - 2, hy - 4, 1.7, 1.7, B)
    shaded_ellipse(img, hx + 1, hy - 4.5, 1.7, 1.7, B)
    put(img, hx + 1, hy - 4, "dusty")
    shaded_ellipse(img, hx, hy, 4.5, 4, B)
    shaded_rect(img, hx + 3, hy, 4, 3, M)
    put(img, hx + 6, hy, "outline")
    if mouth:
        hline(img, hx + 3, hx + 6, hy + 2, "outline")
        put(img, hx + 5, hy + 2, "white")
        hline(img, hx + 3, hx + 5, hy + 3, M["d"])
    eye(img, hx + 1, hy - 1, eyes, BEAR_EYE)
    put(img, hx, hy - 2, "rose")
    put(img, hx + 2, hy, "rose")


def old_bear_body(d: str, p: dict) -> np.ndarray:
    if p.get("rear"):
        return old_bear_rearing(d, p)
    img = canvas(32, 32)
    B = BEAR
    st, eyes, mouth, paw = p.get("step", 0), p.get("eyes", "open"), p.get("mouth", 0), p.get("paw", 0)
    if d == "e":
        by = 18 + p.get("bob", 0)
        for x, ph in ((6, -st), (17, st)):
            shaded_rect(img, x + ph, by + 3, 4, FEET - by - 3, BEAR_FAR)
        shaded_ellipse(img, 12, by, 10, 6.5, B)
        mantle(img, 16, by - 4, 5.5, 3.5)
        moss(img, 8, by - 5, 3)
        arrow_stub(img, 6, by - 4, -1, -1)
        arrow_stub(img, 11, by - 6, 0, -1)
        put(img, 2, by - 2, B["l"])
        shaded_rect(img, 8 + st, by + 3, 4, FEET - by - 3, B)
        for k in range(0, 4, 2):
            put(img, 9 + st + k, FEET - 1, "mist")
        if paw:
            # Zarpa alzada (1) o lanzada hacia delante (2).
            px, py = (24, by - 9) if paw == 1 else (26, by + 1)
            line(img, 19, by, px, py, B["b"])
            line(img, 20, by, px + 1, py, B["b"])
            line(img, 19, by + 1, px, py + 1, B["d"])
            shaded_ellipse(img, px + 0.5, py + 0.5, 2, 2, B)
            for k in range(3):
                put(img, px + 2, py - 1 + k, "white")
        else:
            shaded_rect(img, 19 - st, by + 3, 4, FEET - by - 3, B)
            for k in range(0, 4, 2):
                put(img, 20 - st + k, FEET - 1, "mist")
        bear_head_e(img, 22, by - 3, eyes, mouth)
        return shift(img, 1, 0)
    by = 19 + p.get("bob", 0)
    if d == "s":
        shaded_ellipse(img, 16, by + 1, 10.5, 7, B)
        mantle(img, 16, by - 3, 8.5, 3)
        moss(img, 8, by - 3, 2.5)
        arrow_stub(img, 23, by - 4, 1, -1)
        for x, lift in ((8, max(0, st)), (19, max(0, -st))):
            if paw and x == 19:
                continue
            shaded_rect(img, x, by + 3, 5, FEET - by - 3 - lift, B)
            for k in range(0, 5, 2):
                put(img, x + k, FEET - 1 - lift, "mist")
        if paw:
            px, py = (23, by - 9) if paw == 1 else (21, by + 4)
            shaded_rect(img, px - 2, py, 5, 6, B)
            for k in range(0, 5, 2):
                put(img, px - 2 + k, py - 1 if paw == 1 else py + 6, "white")
        bear_head_s(img, by - 2, eyes, mouth)
        return img
    for x, lift in ((8, max(0, st)), (19, max(0, -st))):
        shaded_rect(img, x, by + 3, 5, FEET - by - 3 - lift, BEAR_FAR)
    shaded_ellipse(img, 16, by, 11, 8, B)
    mantle(img, 16, by - 4, 9, 4)
    moss(img, 11, by - 4, 3)
    moss(img, 21, by + 2, 2.5)
    arrow_stub(img, 18, by - 2, 1, -1)
    arrow_stub(img, 12, by + 2, -1, -1)
    shaded_ellipse(img, 16, by + 6, 2, 1.5, B)
    hy = by - 9
    for ex in (11.5, 20.5):
        shaded_ellipse(img, ex, hy - 3, 1.8, 1.8, B)
    shaded_ellipse(img, 16, hy, 4.5, 3.5, B)
    if paw:
        shaded_rect(img, 22, by - 10 if paw == 1 else by - 4, 5, 6, B)
    return img


def old_bear_rearing(d: str, p: dict) -> np.ndarray:
    """De pie sobre las patas traseras, zarpas en alto (casteo: rugido)."""
    img = canvas(32, 32)
    B = BEAR
    roar, eyes = p.get("mouth", 0), p.get("eyes", "open")
    if d == "e":
        shaded_rect(img, 7, 21, 5, 7, BEAR_FAR)
        shaded_ellipse(img, 12, 16, 7, 9, B)
        mantle(img, 9, 11, 4, 4)
        moss(img, 6, 16, 1.5)
        arrow_stub(img, 6, 19, -1, 0)
        shaded_rect(img, 11, 21, 5, 7, B)
        for k in range(0, 4, 2):
            put(img, 12 + k, FEET - 1, "mist")
        for (ax, ay) in ((20, 8), (21, 13)):
            line(img, 15, ay + 3, ax, ay, B["b"])
            line(img, 15, ay + 4, ax, ay + 1, B["d"])
            shaded_ellipse(img, ax + 0.5, ay + 0.5, 1.8, 1.8, B)
            for k in range(3):
                put(img, ax + 2, ay - 1 + k, "white")
        bear_head_e(img, 16, 7, eyes, roar)
        return shift(img, 1, 0)
    shaded_rect(img, 9, 22, 5, 6, B)
    shaded_rect(img, 18, 22, 5, 6, B)
    shaded_ellipse(img, 16, 16, 9, 9, B)
    for side in (-1, 1):
        ax = 16 + side * 11
        line(img, 16 + side * 6, 15, ax, 7, B["b"])
        line(img, 16 + side * 7, 15, ax + side, 7, B["b"])
        shaded_ellipse(img, ax + 0.5 * side, 6, 2, 2, B)
        for k in range(0, 4, 2):
            put(img, ax - 1 + k, 3, "white")
    if d == "s":
        mantle(img, 16, 12, 7, 2.5)
        ellipse(img, 16, 19, 4, 5, BEAR["l"])
        moss(img, 9, 13, 1.5)
        bear_head_s(img, 9, eyes, roar)
    else:
        mantle(img, 16, 13, 8, 4)
        moss(img, 13, 17, 2.5)
        arrow_stub(img, 19, 14, 1, -1)
        for ex in (11.5, 20.5):
            shaded_ellipse(img, ex, 5, 1.8, 1.8, B)
        shaded_ellipse(img, 16, 8, 4.5, 3.8, B)
    return img


def old_bear(spec: dict, d: str, frame: str, size: int = 32) -> np.ndarray:
    body = lambda dd, p: finish(old_bear_body(dd, p))
    fx_, fy_ = fwd_vec(d)

    def attack(dd, i):  # zarpazo
        fwd = [-1, 0, 2, 1][i]
        img = shift(finish(old_bear_body(dd, {"paw": [1, 1, 2, 0][i], "mouth": i in (1, 2)})), fx_ * fwd, fy_ * fwd)
        if i == 2 and dd != "n":
            cx, cy = (29, 18) if dd == "e" else (24, 28)
            for k in (-2, 0, 2):  # tres zarpas
                line(img, cx - 2 + k, cy - 3, cx + k, cy + 1, "mist")
                put(img, cx + k, cy + 1, "white")
        if i == 1:
            hx, hy = {"e": (24, 9), "s": (23, 10), "n": (24, 9)}[dd]
            fx_trail(hx, hy + 6, -100, -30, 6, ("mist", "white"))(img)
        return img

    def cast(dd, i):  # de pie, rugiendo
        img = finish(old_bear_body(dd, {"rear": True, "mouth": i > 0}))
        if i > 0:
            if dd == "e":
                fx_arcs(24, 8, "e", 1 + i, "cream")(img)
            else:
                fx_arcs(9, 9, "w", 1 + i, "cream")(img)
                fx_arcs(22, 9, "e", 1 + i, "cream")(img)
        return img

    return animate(body, d, frame, attack, cast, lambda dd, i: fall_over(body, dd, i))


# --- Pantano --------------------------------------------------------------------------------------------------------------

# Sapo gigante: ancho y achaparrado, ojos saltones dorados encima, boca de lado a lado, verrugas y panza ocre. Avanza a
# saltos, ataca con la lengua y el casteo ("Salpicón de lodo") lo hincha soltando gotas de barro.
TOAD = mat("olive", "moss", "mud")
TOAD_BELLY = mat("sand", "cream", "tan")
TOAD_EYE = "gold"
TOAD_WARTS = ((-4, -4), (-1, -5), (-7, -2), (1, -3), (-3, -1), (4, -4))


def toad_eye(img, x: int, y: int, mode: str) -> None:
    if mode == "open":
        rect(img, x, y, 2, 2, TOAD_EYE)
        put(img, x, y, "cream")
        put(img, x + 1, y + 1, "outline")
    elif mode == "closed":
        hline(img, x, x + 1, y + 1, "outline")
    else:
        eye(img, x, y, "x", "")


def giant_toad_body(d: str, p: dict) -> np.ndarray:
    img = canvas(32, 32)
    T, Bl = TOAD, TOAD_BELLY
    far = mat(T["d"], T["b"], T["d"])
    eyes, mouth, tongue, puff = p.get("eyes", "open"), p.get("mouth", 0), p.get("tongue", 0), p.get("puff", 0)
    hop, sq = p.get("hop", 0), p.get("squash", 0)
    by = 21 - hop + sq
    rx, ry = 9 + puff + sq, 6 + puff - sq
    if d == "e":
        shaded_ellipse(img, 8, by + 3, 3, 2.5, far)
        shaded_ellipse(img, 15, by, rx, ry, T)
        ellipse(img, 18, by + 3.5, 5.5 + puff, 2.2 + puff * 0.5, Bl["b"])
        hline(img, 14, 21, by + 5 + puff // 2, Bl["d"])
        for (x, y) in TOAD_WARTS:
            put(img, 13 + x, by + y, T["d"])
            put(img, 12 + x, by + y - 1, T["l"])
        shaded_ellipse(img, 19, by - 5 - puff, 2.6, 2.6, T)
        toad_eye(img, 19, by - 6 - puff, eyes)
        hline(img, 18, 23 + puff, by, "outline")
        put(img, 17, by - 1, "outline")
        if mouth:
            hline(img, 19, 23 + puff, by + 1, "magenta_d")
            hline(img, 19, 22 + puff, by + 2, "outline")
        if tongue:
            hline(img, 23, 23 + tongue, by + 1, "rose")
            rect(img, 23 + tongue, by, 2, 2, "pink")
        shaded_rect(img, 20, by + 3, 2, FEET - by - 3, T)
        hline(img, 19, 23, FEET - 1, T["d"])
        shaded_ellipse(img, 10, by + 2, 4, 3.5, T)
        shaded_rect(img, 4, FEET - 2, 7, 2, T)
        put(img, 4, FEET - 1, T["d"])
        return img
    if d == "s":
        for side in (-1, 1):
            shaded_ellipse(img, 16 + side * 10, by + 3, 2.5, 2.5, T)
            shaded_rect(img, 16 + side * 11 - (2 if side > 0 else 1), FEET - 1, 4, 1, T)
        shaded_ellipse(img, 16, by, rx + 1, ry, T)
        ellipse(img, 16, by + 3.5, 6 + 2 * puff, 2.2 + puff, Bl["b"])
        if puff:
            put(img, 13, by + 3, Bl["l"])
            put(img, 14, by + 2, Bl["l"])
        for (x, y) in TOAD_WARTS:
            put(img, 16 + x, by + y, T["d"])
            put(img, 15 + x, by + y - 1, T["l"])
        for ex in (11, 21):
            shaded_ellipse(img, ex, by - 5 - puff, 2.8, 2.8, T)
        toad_eye(img, 10, by - 6 - puff, eyes)
        toad_eye(img, 21, by - 6 - puff, eyes)
        hline(img, 8, 23, by + 1, "outline")
        put(img, 7, by, "outline")
        put(img, 24, by, "outline")
        if mouth:
            hline(img, 10, 21, by + 2, "magenta_d")
            hline(img, 11, 20, by + 3, "outline")
        if tongue:
            vline(img, 16, by + 2, by + 2 + tongue, "rose")
            rect(img, 15, by + 2 + tongue, 2, 2, "pink")
        for x in (11, 19):
            shaded_rect(img, x, by + 4, 2, FEET - by - 4, T)
            hline(img, x - 1, x + 2, FEET - 1, T["d"])
        return img
    for side in (-1, 1):
        shaded_ellipse(img, 16 + side * 9, by + 3, 3, 2.8, T)
        shaded_rect(img, 16 + side * 10 - (2 if side > 0 else 1), FEET - 2, 4, 2, T)
    shaded_ellipse(img, 16, by, rx + 1, ry, T)
    for (x, y) in TOAD_WARTS + ((2, 1), (-5, 2), (5, 0)):
        put(img, 16 + x, by + y, T["d"])
        put(img, 15 + x, by + y - 1, T["l"])
    vline(img, 16, by - 4, by + 4, T["l"])
    for ex in (11, 21):
        shaded_ellipse(img, ex, by - 5 - puff, 2.8, 2.8, T)
    return img


def giant_toad(spec: dict, d: str, frame: str, size: int = 32) -> np.ndarray:
    fx_, fy_ = fwd_vec(d)

    def body(dd, p):
        q = dict(p)
        if p.get("walking"):  # a saltos: se agacha, vuela, cae
            ph = p["phase"]
            q.update({"squash": [1, 0, 0, 0][ph], "hop": [0, 3, 1, 0][ph]})
        elif "bob" in p and not p.get("hurt"):
            q["squash"] = p["bob"]
        return finish(giant_toad_body(dd, q))

    def attack(dd, i):  # lengüetazo
        fwd = [0, 0, 1, 0][i] if dd == "n" else 0
        img = finish(giant_toad_body(dd, {"squash": 1 if i == 0 else 0, "mouth": i > 0, "tongue": [0, 0, 7, 3][i]}))
        return shift(img, fx_ * fwd, fy_ * fwd)

    def cast(dd, i):  # "Salpicón de lodo": se hincha y salpica barro
        img = finish(giant_toad_body(dd, {"puff": 1 + (i == 1), "mouth": i == 2}))
        drops = [(9, 9), (23, 8), (16, 5), (6, 13), (26, 12)]
        for k, (x, y) in enumerate(drops[i:] + drops[:i]):  # gotas de barro de 2 px que suben y bajan
            if k < 3:
                rect(img, x, y + [0, -1, 1][i], 2, 2, "rust")
                put(img, x, y + [0, -1, 1][i], "clay")
        return img

    return animate(body, d, frame, attack, cast, lambda dd, i: fall_over(body, dd, i, on_back=True))


# Hombre lagarto: humanoide de escamas verdeazuladas con hocico, cresta ocre, cola, panza de placas claras y lanza; la
# estocada es corta para que la punta no se salga del cuadro y el casteo ("Lanza envenenada") la alza con brillo de veneno.
LIZ = mat("teal", "aqua", "teal_d")
LIZ_CREST = mat("clay", "tan", "rust")


def lizard_tail(img, pts, widths) -> None:
    for (x, y), w in zip(pts, widths):
        shaded_ellipse(img, x, y, w, max(0.8, w * 0.75), LIZ)


def lizardman_extras(img, stage: str, d: str, c: dict) -> None:
    hx, hy, hw, hh = c["head"]
    cx, feet = c["cx"], c["feet"]
    sway = {0: 1, 1: 0, 2: -1, 3: 0}.get(c["walk"], 0)
    if stage == "under":
        if d == "e":
            lizard_tail(img, [(cx - 3, feet - 6), (cx - 6, feet - 4), (cx - 9, feet - 3 + sway), (cx - 12, feet - 4 + sway)],
                        [2.2, 1.8, 1.4, 1.0])
            put(img, cx - 13, feet - 5 + sway, LIZ["l"])
        elif d == "s":
            lizard_tail(img, [(cx - 7, feet - 3), (cx - 9, feet - 2 + sway)], [1.6, 1.1])
    elif stage == "torso":
        x, y, w, h = c["torso"]
        if d == "s":
            for yy in range(y + 1, y + h - 2, 2):  # placas del vientre
                hline(img, x + 3, x + w - 4, yy, "sand")
                hline(img, x + 3, x + w - 4, yy + 1, "tan")
            line(img, x, y, x + w - 1, y + h - 3, "rust")  # correa
        elif d == "e":
            vline(img, x + w - 2, y + 1, y + h - 2, "sand")
        else:
            line(img, x + w - 1, y, x, y + h - 3, "rust")
            for yy in range(y + 1, y + h - 1, 2):
                put(img, x + w // 2, yy, LIZ_CREST["d"])
    else:
        if d == "n":
            for k, yy in enumerate(range(hy - 1, hy + hh + 2, 2)):  # cresta por la nuca
                put(img, hx + hw // 2, yy, LIZ_CREST["b"])
                put(img, hx + hw // 2 - 1, yy + 1, LIZ_CREST["d"])
            lizard_tail(img, [(cx, feet - 5), (cx + 1, feet - 2), (cx + 4 + sway, feet - 1)], [1.8, 1.5, 1.1])
            return
        if d == "s":
            for k, x in enumerate((hx + 2, hx + 4, hx + 6)):
                vline(img, x + (k == 1), hy - 1 - (k == 1), hy, LIZ_CREST["b"])
            rect(img, hx + 2, hy + hh - 2, 6, 2, LIZ["b"])
            hline(img, hx + 2, hx + 7, hy + hh - 2, LIZ["l"])
            hline(img, hx + 2, hx + 7, hy + hh, "sand")
            put(img, hx + 3, hy + hh - 2, "outline")
            put(img, hx + 6, hy + hh - 2, "outline")
        else:
            for k, (x, y) in enumerate(((hx + 1, hy), (hx + 3, hy - 1), (hx + 5, hy - 1), (hx, hy + 3), (hx - 1, hy + 5))):
                put(img, x, y, LIZ_CREST["b"])
                put(img, x, y + 1, LIZ_CREST["d"])
            rect(img, hx + hw - 2, hy + 4, 5, 3, LIZ["b"])
            hline(img, hx + hw - 2, hx + hw + 2, hy + 4, LIZ["l"])
            hline(img, hx + hw - 2, hx + hw + 1, hy + 6, "sand")
            put(img, hx + hw + 2, hy + 4, "outline")


LIZARDMAN = {"skin": LIZ, "hair": None, "head": "none", "top": LIZ, "sleeve": LIZ, "pants": mat("olive", "moss", "mud"),
             "boots": mat("teal_d", "teal", "outline"), "belt": "sand", "weapon": "spear", "attack": "spear", "eye": "gold",
             "blush": False, "glow": "poison", "extras": lizardman_extras}


# Fuego fatuo: llama flotante en gota, verdeazulada con el corazón blanco, cara de dos ojos huecos, chispas sueltas y
# contorno verde azulado oscuro; no toca el suelo. El ataque es un fogonazo que lanza una chispa, el casteo ("Destello")
# la agranda con destellos alrededor, y al morir se apaga en unas brasas.
WISP = ("aqua", "aqua_l", "foam", "white")
WISP_DIM = ("teal", "aqua", "aqua_l", "foam")
WISP_LINE = "teal_d"


def flame(img, cx: float, cy: float, r: float, h: float, lean: float, colors, tip: str | None = None) -> None:
    """Llama en gota: medio círculo abajo y punta a `h` px por encima, desplazada `lean`; cuatro capas concéntricas y la
    punta de la capa de fuera en `tip` (el brillo azul del fuego fatuo)."""
    for f, c in zip((1.0, 0.7, 0.45, 0.22), colors):
        rr, hh = r * f, h * f
        ccy = cy + (r - rr) * 0.5
        for y in range(int(ccy - hh) - 1, int(ccy + rr) + 2):
            yc = y + 0.5
            if yc >= ccy:
                w2 = rr * rr - (yc - ccy) ** 2
                if w2 < 0:
                    continue
                half, off = math.sqrt(w2), 0.0
            else:
                t = (ccy - yc) / hh
                if t > 1:
                    continue
                half, off = rr * (1 - t) ** 1.3, lean * t * t
            cc = tip if tip and f == 1.0 and yc < ccy - hh * 0.5 else c
            for x in range(int(cx - rr - abs(lean)) - 2, int(cx + rr + abs(lean)) + 3):
                if abs(x + 0.5 - (cx + off)) <= half:
                    put(img, x, y, cc)


def wisp_frame(d: str, cy: float, r: float, h: float, lean: float, colors, eyes: str = "open", sparks=()) -> np.ndarray:
    img = canvas(32, 32)
    flame(img, 16, cy, r, h, lean, colors, "sky" if colors is WISP else None)
    ey = int(cy) - 1
    if eyes == "none":
        pass
    elif d == "s":
        for ex in (13, 18):
            eye(img, ex, ey, eyes, WISP_LINE, 2)
        if eyes == "open":
            hline(img, 15, 16, ey + 3, WISP_LINE)
    elif d == "e":
        eye(img, 18, ey, eyes, WISP_LINE, 2)
        if eyes == "open":
            put(img, 19, ey + 3, WISP_LINE)
    out = finish(img, color=WISP_LINE)
    fx_pixels(sparks, "ice")(out)
    return out


WISP_SPARKS = [[(9, 20), (23, 14), (14, 25)], [(10, 13), (22, 21), (18, 26)], [(8, 17), (24, 18), (13, 24)], [(11, 22), (21, 11), (17, 25)]]


def will_o_wisp(spec: dict, d: str, frame: str, size: int = 32) -> np.ndarray:
    fx_, fy_ = fwd_vec(d)
    trail = {"e": -3, "s": 0, "n": 0}[d]

    def body(dd, p):
        ph = p.get("phase", 0)
        cy = 15 - p.get("bob", 0) if not p.get("walking") else 15 + [0, -1, 0, -1][ph]
        lean = ([-1, 1][ph % 2] if not p.get("walking") else trail + [-1, 0, 1, 0][ph] * (1 if dd != "e" else 0.5))
        colors = WISP_DIM if p.get("hurt") or p.get("eyes") == "closed" else WISP
        return wisp_frame(dd, cy, 5.5, 11, lean, colors, p.get("eyes", "open"), WISP_SPARKS[ph % 4])

    def attack(dd, i):  # fogonazo: se encoge, se hincha y escupe una chispa
        r = [4.5, 6.5, 6.5, 5.5][i]
        img = wisp_frame(dd, 15, r, 11 + (i in (1, 2)), trail * 0.5, WISP, "open", WISP_SPARKS[i])
        if i == 2:
            tip = {"e": (27, 15), "s": (16, 26), "n": (16, 4)}[dd]
            fx_sparkle(*tip, 2, "foam", "white")(img)
        return img

    def cast(dd, i):  # "Destello": crece y destella alrededor
        img = wisp_frame(dd, 14, 6.5, 13, [-1, 0, 1][i], WISP, "open", WISP_SPARKS[i])
        for k in range(3):
            a = math.radians(-90 + 120 * k + 40 * i)
            fx_sparkle(int(round(16 + math.cos(a) * 10)), int(round(14 + math.sin(a) * 9)), 1 + (k == i), "ice", "white")(img)
        return img

    def death(dd, i):  # se apaga y quedan brasas
        if i == 0:
            return shift(wisp_frame(dd, 16, 4.5, 9, 0, WISP_DIM, "closed"), -fx_, 0)
        if i == 1:
            return wisp_frame(dd, 19, 3.5, 7, 0, WISP_DIM, "x", [(9, 22), (23, 20)])
        if i == 2:
            return wisp_frame(dd, 23, 2.2, 4, 0, WISP_DIM, "none", [(10, 25), (21, 24), (14, 21)])
        img = wisp_frame(dd, 26, 1.2, 2.5, 0, ("teal_d", "teal", "aqua", "aqua"), "none")
        fx_pixels([(12, 27), (19, 27), (21, 26)], "teal")(img)
        return img

    return animate(body, d, frame, attack, cast, death)


# Bruja del pantano (élite): más alta que un humanoide por el sombrero puntiagudo y torcido; túnica morada con chal de
# musgo y bajo deshilachado, pelo gris, nariz ganchuda, ojos que brillan y un bastón retorcido con un orbe verdeazulado.
# Ataca a distancia con el orbe; el casteo ("Ciénaga", "Maldición", "Brebaje") alza el bastón y la otra mano entre chispas,
# y al morir se queda en un montón de ropa con el sombrero encima.
WITCH_ROBE = mat("grape", "violet", "grape_d")
WITCH_HAT = mat("grape_d", "grape", "outline")
WITCH_SHAWL = mat("olive", "moss", "mud")
WITCH_SKIN = mat("sage_p", "mist", "sage_l")
WITCH_HAIR = mat("lavgray", "silver", "plum")
WITCH_STAFF = mat("mud", "olive", "outline")
WITCH_ORB = ("aqua", "aqua_l", "foam")
WITCH_ORB_DIM = ("teal_d", "teal", "aqua")
WITCH_EYE = "aqua_l"


def witch_orb(img, x: float, y: float, r: float, colors) -> None:
    ellipse(img, x, y, r, r, colors[0])
    ellipse(img, x - 0.3, y - 0.3, r * 0.65, r * 0.65, colors[1])
    put(img, int(x) - 1, int(y) - 1, colors[2])


def witch_staff(img, bx: int, by_: int, tx: int, ty: int, orb) -> None:
    """Bastón retorcido de (bx, by_) a la horquilla en (tx, ty), con el orbe dentro."""
    line(img, bx, by_, tx, ty, WITCH_STAFF["b"])
    line(img, bx + 1, by_, tx + 1, ty + 1, WITCH_STAFF["d"])
    put(img, (bx + tx) // 2, (by_ + ty) // 2, WITCH_STAFF["l"])
    for ox in (-2, 2):
        line(img, tx, ty, tx + ox, ty - 3, WITCH_STAFF["b"])
    if orb:
        witch_orb(img, tx + 0.5, ty - 2.5, 2.2, orb)


def witch_robe(img, cx: int, top: int, sway: int, hunch: int = 0) -> None:
    R = WITCH_ROBE
    for y in range(top, FEET):
        half = 3.5 + (y - top) * 0.36
        x0, x1 = int(round(cx - half)) + hunch * (y < top + 4), int(round(cx + half))
        hline(img, x0, x1, y, R["b"])
        put(img, x0, y, R["l"])
        put(img, x1, y, R["d"])
    half = 3.5 + (FEET - 1 - top) * 0.36
    for x in range(int(round(cx - half)), int(round(cx + half)) + 1):  # bajo deshilachado
        if (x + sway) % 3 == 0:
            put(img, x, FEET - 1, (0, 0, 0, 0))
        elif (x + sway) % 3 == 1:
            put(img, x, FEET - 2, R["d"])


def witch_hat(img, cx: int, brim_y: int, bend: int) -> None:
    H = WITCH_HAT
    shaded_ellipse(img, cx, brim_y + 0.5, 9, 1.6, H)
    for k in range(6):
        w = 6 - k
        x0 = cx - w // 2 - 1 + (k * bend) // 3
        hline(img, x0, x0 + w, brim_y - 1 - k, H["b"])
        put(img, x0, brim_y - 1 - k, H["l"])
    tip = cx - 1 + (6 * bend) // 3
    put(img, tip + bend, brim_y - 6, H["b"])
    put(img, tip + 2 * bend, brim_y - 5, H["b"])
    hline(img, cx - 4, cx + 2, brim_y - 1, WITCH_SHAWL["b"])
    put(img, cx - 3, brim_y - 1, WITCH_SHAWL["l"])


def swamp_witch_body(d: str, p: dict) -> np.ndarray:
    img = canvas(32, 32)
    S, Hr, Sh = WITCH_SKIN, WITCH_HAIR, WITCH_SHAWL
    bob, st, eyes = p.get("bob", 0), p.get("step", 0), p.get("eyes", "open")
    staff, raise_ = p.get("staff", (0, 0)), p.get("arms_up", False)
    orb = WITCH_ORB if not p.get("dim") else WITCH_ORB_DIM
    top = 15 + bob
    if d == "n":
        witch_staff(img, 24 + st, FEET - 1, 24 + staff[0], 7 + staff[1] + bob, orb)
    witch_robe(img, 16, top, st)

    def shawl(cy: float, ry: float) -> None:
        shaded_ellipse(img, 16, cy, 6.5, ry, Sh)
        for x in (11, 14, 19):
            put(img, x, top + 3 + (x % 2), Sh["d"])

    if d == "n":
        shawl(top + 1, 2.5)
    if d != "n":
        hline(img, 11, 21, top + 7, "sand")
        rect(img, 18, top + 8, 2, 2, "aqua_l")
        put(img, 18, top + 8, "foam")
    hy = top - 3
    if d == "s":
        for x in (11, 12, 19, 20):  # pelo lacio a los lados
            vline(img, x, hy - 2, hy + 4 + (x in (11, 20)), Hr["b"] if x in (12, 19) else Hr["d"])
        shaded_ellipse(img, 16, hy, 4, 3.4, S)
        eye(img, 14, hy - 1, eyes, WITCH_EYE)
        eye(img, 18, hy - 1, eyes, WITCH_EYE)
        put(img, 16, hy - 1, S["l"])
        put(img, 16, hy, S["b"])
        put(img, 16, hy + 1, S["d"])
        put(img, 17, hy + 1, S["d"])
        hline(img, 14, 18, hy + 2, "outline")
        put(img, 15, hy + 2, "cream")
    elif d == "e":
        for x in (12, 13, 14):  # melena por la espalda
            vline(img, x, hy - 2, hy + 5 + (x == 13), Hr["b"] if x != 13 else Hr["l"])
        shaded_ellipse(img, 17, hy, 3, 3, S)
        for (x, y, c) in ((20, hy, S["l"]), (21, hy + 1, S["b"]), (22, hy + 2, S["d"]), (19, hy + 2, S["d"])):
            put(img, x, y, c)
        eye(img, 18, hy - 1, eyes, WITCH_EYE)
        hline(img, 18, 19, hy + 2, "outline")
    else:
        shaded_ellipse(img, 16, hy + 1, 4.5, 4.5, Hr)  # melena larga en punta, deshilachada
        for x, n in ((13, 2), (15, 4), (16, 5), (17, 3), (19, 1)):
            vline(img, x, hy + 4, hy + 4 + n, Hr["b"] if x != 16 else Hr["d"])
        vline(img, 14, hy - 1, hy + 3, Hr["l"])
    if d != "n":
        shawl(top + 2, 2)  # bajo la barbilla, sobre las puntas del pelo
    witch_hat(img, 16, hy - 4, {"s": 1, "n": 1, "e": -1}[d])
    # Brazos: el del bastón y la mano libre (en alto al castear).
    if d != "n":
        hx = 24 + staff[0] if d == "s" else 22 + staff[0]
        line(img, 19 if d == "s" else 17, top + 2, hx - 1, top + 5 + staff[1], WITCH_ROBE["b"])
        line(img, 19 if d == "s" else 17, top + 3, hx - 1, top + 6 + staff[1], WITCH_ROBE["d"])
        witch_staff(img, (24 if d == "s" else 23) + st, FEET - 1, hx, 7 + staff[1] + bob, orb)
        rect(img, hx - 1, top + 5 + staff[1], 2, 2, S["b"])
        if d == "s":
            if raise_:
                line(img, 12, top + 2, 9, top - 4, WITCH_ROBE["b"])
                line(img, 11, top + 2, 8, top - 4, WITCH_ROBE["l"])
                rect(img, 8, top - 6, 2, 2, S["b"])
            else:
                vline(img, 10, top + 2, top + 7, WITCH_ROBE["l"])
                vline(img, 11, top + 2, top + 7, WITCH_ROBE["b"])
                rect(img, 10, top + 8, 2, 1, S["b"])
                put(img, 10, top + 9, "outline")
    return img


def witch_pile(dim: bool, flat: bool) -> np.ndarray:
    """Lo que queda de la bruja: túnica caída con el chal, el sombrero encima y el bastón en el suelo."""
    img = canvas(32, 32)
    line(img, 4, FEET - 1, 27, FEET - 2, WITCH_STAFF["b"])
    line(img, 4, FEET, 27, FEET - 1, WITCH_STAFF["d"])
    witch_orb(img, 3.5, FEET - 2.5, 1.8, WITCH_ORB_DIM if dim else WITCH_ORB)
    shaded_ellipse(img, 15, FEET - 2.5 + flat * 0.5, 9, 2 if flat else 2.8, WITCH_ROBE)
    shaded_ellipse(img, 12, FEET - 4 + flat, 4.5, 1.3, WITCH_SHAWL)
    H = WITCH_HAT
    by = FEET - 5 + flat
    shaded_ellipse(img, 17, by, 7, 1.6, H)  # el sombrero sigue de pie sobre el montón, algo hundido
    rows = 7 - 2 * flat
    for k in range(rows):
        w = 5 - (k * 5) // 7
        x0 = 15 + k // 2
        hline(img, x0, x0 + max(1, w), by - 1 - k, H["b"])
        put(img, x0, by - 1 - k, H["l"])
    put(img, 16 + rows // 2, by - rows, H["b"])
    put(img, 17 + rows // 2, by - rows + 1, H["b"])
    hline(img, 15, 20, by - 1, WITCH_SHAWL["b"])
    return img


def swamp_witch(spec: dict, d: str, frame: str, size: int = 32) -> np.ndarray:
    body = lambda dd, p: finish(swamp_witch_body(dd, p))
    fx_, fy_ = fwd_vec(d)

    def attack(dd, i):  # el orbe dispara
        staff = [(-1, 1), (1, 0), (2, 0), (0, 0)][i] if dd == "e" else [(0, 1), (0, -1), (0, -1), (0, 0)][i]
        img = finish(swamp_witch_body(dd, {"staff": staff}))
        if i in (1, 2):
            ox, oy = (24 + staff[0] if dd == "s" else 22 + staff[0]) if dd != "n" else 24, 4 + staff[1]
            fx_sparkle(ox, oy, 1 + i, "aqua_l", "foam")(img)
        if i == 2:
            tip = {"e": (29, 10), "s": (16, 28), "n": (16, 3)}[dd]
            fx_sparkle(*tip, 2, "aqua_l", "white")(img)
        return img

    def cast(dd, i):  # alza el bastón y la mano entre chispas verdes
        img = finish(swamp_witch_body(dd, {"staff": (0, -1 if i != 1 else 0), "arms_up": True, "bob": [0, -1, 0][i]}))
        for k in range(3):
            a = math.radians(-90 + 120 * k + 35 * i)
            fx_sparkle(int(round(16 + math.cos(a) * 11)), int(round(15 + math.sin(a) * 9)), 1 + (k == i), "lime", "aqua_l")(img)
        return img

    def death(dd, i):
        if i == 0:
            return shift(body(dd, {"eyes": "closed", "dim": True}), -fx_, 0)
        if i == 1:
            return kneel(body(dd, {"eyes": "closed", "dim": True}), FEET, 5)
        return finish(witch_pile(True, i == 3))

    return animate(body, d, frame, attack, cast, death)


# --- Cripta de Raíces -----------------------------------------------------------------------------------------------------

# Esqueleto de raíces: huesos con costillas a la vista, peto y grebas de corteza (mucha armadura), una cornamenta de raíces
# que le sale del cráneo, ojos verdes y una rama espinosa por espada. Nada que ver con el esqueleto guerrero (yelmo y
# escudo): la silueta la dan los cuernos de raíz.
BONE = mat("mist", "white", "silver")
BARK = mat("rust", "clay", "wine")
CRYPT_MOSS = ("jade", "leaf")


def antlers(img, x: int, y: int, side: int) -> None:
    """Cornamenta de raíz que sale del cráneo en (x, y) hacia `side` (−1 izquierda, 1 derecha), ramificada y con hojas."""
    line(img, x, y, x + side * 3, y - 3, BARK["b"])
    line(img, x + side * 3, y - 3, x + side * 6, y - 4, BARK["d"])
    line(img, x + side * 3, y - 3, x + side * 2, y - 5, BARK["b"])
    line(img, x + side * 5, y - 4, x + side * 6, y - 5, BARK["b"])
    put(img, x + side * 6, y - 5, "leaf")
    put(img, x + side * 7, y - 4, "leaf")
    put(img, x + side * 2, y - 5, "jade")


def root_skeleton_extras(img, stage: str, d: str, c: dict) -> None:
    hx, hy, hw, hh = c["head"]
    x, y, w, h = c["torso"]
    if stage == "torso":
        if d == "n":
            vline(img, x + w // 2, y, y + h - 1, "silver")
            rect(img, x, y + 3, w, h - 3, BARK["b"])
            hline(img, x, x + w - 1, y + 3, BARK["l"])
            for xx in range(x + 1, x + w, 3):
                vline(img, xx, y + 4, y + h - 1, BARK["d"])
        else:
            for yy in (y + 1, y + 3):  # costillas
                hline(img, x + 1, x + w - 2, yy, "lavgray")
            vline(img, x + w // 2 - (d == "s"), y, y + 3, "silver")
            rect(img, x, y + 4, w, h - 4, BARK["b"])  # peto de corteza
            hline(img, x, x + w - 1, y + 4, BARK["l"])
            for xx in range(x + 1, x + w, 3):
                vline(img, xx, y + 5, y + h - 1, BARK["d"])
        put(img, x, y, CRYPT_MOSS[0])
        put(img, x + 1, y, CRYPT_MOSS[1])
        put(img, x, y + 1, CRYPT_MOSS[0])
    elif stage == "over":
        if d == "s":
            antlers(img, hx + 2, hy + 1, -1)
            antlers(img, hx + hw - 3, hy + 1, 1)
            ey = hy + hh // 2 + 1
            if not c["eyes_closed"]:
                put(img, hx + 1, ey, "outline")
                put(img, hx + 1, ey + 1, "outline")
                put(img, hx + hw - 2, ey, "outline")
                put(img, hx + hw - 2, ey + 1, "outline")
            hline(img, hx + 4, hx + 5, hy + hh - 2, "outline")
            for k in range(hx + 3, hx + 7, 2):
                put(img, k, hy + hh - 1, "outline")
        elif d == "e":
            antlers(img, hx + 3, hy + 1, -1)
            antlers(img, hx + 6, hy, 1)
            ey = hy + hh // 2 + 1
            if not c["eyes_closed"]:
                put(img, hx + hw - 4, ey, "outline")
                put(img, hx + hw - 4, ey + 1, "outline")
            put(img, hx + hw - 2, hy + hh - 2, "outline")
            for k in range(hx + hw - 5, hx + hw - 1, 2):
                put(img, k, hy + hh - 1, "outline")
        else:
            antlers(img, hx + 2, hy + 1, -1)
            antlers(img, hx + hw - 3, hy + 1, 1)
        put(img, hx + hw // 2, hy, CRYPT_MOSS[0])
        put(img, hx + hw // 2 - 1, hy, CRYPT_MOSS[1])


ROOT_SKELETON = {"skin": BONE, "hair": None, "head": "none", "top": BONE, "sleeve": BONE, "pants": mat("mud", "olive", "outline"),
                 "boots": mat("rust", "clay", "wine"), "belt": "olive", "weapon": "thorn", "attack": "slash", "eye": "lime",
                 "blush": False, "glow": "roots", "extras": root_skeleton_extras}


# Espíritu del musgo: espíritu encapuchado de musgo que flota, con máscara clara de ojos que brillan, un brote en la
# cabeza, brazos de zarcillo y flecos de musgo colgando. Ataca con esporas y su casteo ("Savia") alza los brazos entre
# destellos verdes que curan; al morir queda un montón de musgo con la máscara encima.
MOSS = mat("green", "jade", "pine")
MOSS_MASK = mat("sage_p", "mist", "taupe")
MOSS_GLOW = ("cream", "pale")


def moss_strands(img, cy: int, sway: int, short: int = 0) -> None:
    for x in range(10, 23):
        n = 2 + int(4 * hash01(31, x)) - short
        if n <= 0:
            continue
        tip = sway if n > 3 else 0
        line(img, x, cy + 8, x + tip, cy + 8 + n, MOSS["d"] if x % 2 else MOSS["b"])


def moss_tendril(img, x0: int, y0: int, x1: int, y1: int) -> None:
    line(img, x0, y0, x1, y1, MOSS["b"])
    line(img, x0, y0 + 1, x1, y1 + 1, MOSS["d"])
    put(img, x1, y1, "leaf")
    put(img, x1 + (1 if x1 > x0 else -1), y1 - 1, "leaf")


def moss_spirit_body(d: str, p: dict) -> np.ndarray:
    img = canvas(32, 32)
    ph = p.get("phase", 0)
    cy = 13 - p.get("bob", 0) if not p.get("walking") else 13 + [0, -1, 0, -1][ph]
    sway = {"e": -1, "s": 0, "n": 0}[d] + (p.get("step", 0) if d != "e" else 0)
    eyes, arms = p.get("eyes", "open"), p.get("arms", 0)
    moss_strands(img, cy, sway)
    # Capucha y manto.
    shaded_ellipse(img, 16, cy, 6, 5.5, MOSS)
    for k in range(9):
        yy = cy + 1 + k
        half = 5.5 + k * 0.2
        hline(img, int(round(16 - half)), int(round(15 + half)), yy, MOSS["b"])
        put(img, int(round(16 - half)), yy, MOSS["l"])
        put(img, int(round(15 + half)), yy, MOSS["d"])
    for (tx, ty) in ((12, -4), (19, -3), (10, 0), (21, 2), (13, 5), (18, 7)):  # mechones de musgo
        put(img, tx, cy + ty, "leaf")
        put(img, tx + 1, cy + ty + 1, MOSS["d"])
    # Brote.
    lean = {"e": -1, "s": 0, "n": 0}[d]
    vline(img, 16, cy - 7, cy - 5, "green")
    hline(img, 13 + lean, 15 + lean, cy - 8, "leaf")
    hline(img, 17 + lean, 19 + lean, cy - 7, "leaf")
    put(img, 16 + lean, cy - 9, "cream")
    if d == "s":
        shaded_ellipse(img, 16, cy + 1, 3.6, 3.2, MOSS_MASK)
        for ex in (14, 17):
            if eyes == "open":
                rect(img, ex, cy, 1, 2, "outline")
                put(img, ex, cy, MOSS_GLOW[0])
            else:
                eye(img, ex, cy, eyes, "", 2)
        hline(img, 15, 16, cy + 3, "outline")
    elif d == "e":
        shaded_ellipse(img, 19, cy + 1, 2.5, 3.2, MOSS_MASK)
        if eyes == "open":
            rect(img, 20, cy, 1, 2, "outline")
            put(img, 20, cy, MOSS_GLOW[0])
        else:
            eye(img, 20, cy, eyes, "", 2)
        put(img, 21, cy + 3, "outline")
    # Brazos de zarcillo: abajo (0), hacia delante (1) o en alto (2).
    if arms == 2:
        moss_tendril(img, 10, cy + 4, 6, cy - 3)
        moss_tendril(img, 21, cy + 4, 25, cy - 3)
    elif arms == 1:
        if d == "e":
            moss_tendril(img, 18, cy + 5, 26, cy + 4)
        else:
            moss_tendril(img, 21, cy + 4, 24, cy + 9 if d == "s" else cy - 1)
            moss_tendril(img, 10, cy + 4, 7, cy + 9)
    else:
        if d == "e":
            moss_tendril(img, 17, cy + 5, 21, cy + 9)
        else:
            moss_tendril(img, 10, cy + 4, 7, cy + 9)
            moss_tendril(img, 21, cy + 4, 24, cy + 9)
    return img


def moss_mound(flat: bool) -> np.ndarray:
    img = canvas(32, 32)
    shaded_ellipse(img, 16, FEET - 2.5, 8, 2 if flat else 3, MOSS)
    for x in (10, 13, 19, 22):
        put(img, x, FEET - 4 + flat, "leaf")
    shaded_ellipse(img, 15, FEET - 5 + flat, 3, 2.4, MOSS_MASK)
    eye(img, 13, FEET - 6 + flat, "x", "")
    put(img, 16, FEET - 5 + flat, "outline")
    line(img, 20, FEET - 3, 23, FEET - 5 + flat, "olive")
    put(img, 24, FEET - 5 + flat, "mud")
    return img


def moss_spirit(spec: dict, d: str, frame: str, size: int = 32) -> np.ndarray:
    body = lambda dd, p: finish(moss_spirit_body(dd, p))
    fx_, fy_ = fwd_vec(d)

    def attack(dd, i):  # esporas
        img = finish(moss_spirit_body(dd, {"arms": 1 if i in (1, 2) else 0}))
        hand = {"e": (27, 17), "s": (24, 23), "n": (24, 11)}[dd]
        if i in (0, 1):
            fx_sparkle(*hand, 1, "pale", "cream")(img)
        if i == 2:
            tip = {"e": (29, 15), "s": (20, 27), "n": (21, 6)}[dd]
            fx_sparkle(*tip, 2, "pale", "cream")(img)
            fx_pixels([(hand[0] + 2, hand[1] - 2), (hand[0] - 1, hand[1] + 2)], "lime")(img)
        return img

    def cast(dd, i):  # "Savia": brazos en alto y destellos que suben
        img = finish(moss_spirit_body(dd, {"arms": 2, "bob": [0, 1, 0][i]}))
        for k, (x, y) in enumerate(((6, 18), (26, 16), (16, 4), (9, 8), (23, 7))):
            if (k + i) % 2 == 0:
                fx_sparkle(x, y + 1 - i, 1 + (k == 2), "leaf", "cream")(img)
        return img

    def death(dd, i):
        if i == 0:
            return shift(body(dd, {"eyes": "closed"}), -fx_, 1)
        if i == 1:
            return squash_rows(shift(finish(moss_spirit_body(dd, {"eyes": "closed"})), 0, 3), 3, FEET)
        return finish(moss_mound(i == 3))

    return animate(body, d, frame, attack, cast, death)


# Planta trampa: atrapamoscas enorme sobre un tallo grueso, con fauces rojas y dientes claros, hojas en el suelo y manchas
# pálidas. Es inmóvil: su "andar" es un balanceo en el sitio. Escupe espinas (ataque), al castear ("Raíces trampa") baja
# la cabeza y brotan raíces alrededor, y al morir se marchita.
PLANT = mat("jade", "leaf", "green")
PLANT_STALK = mat("green", "jade", "pine")
PLANT_MAW = ("magenta", "magenta_d")
PLANT_TEETH = "cream"
WITHER = {"jade": "olive", "leaf": "moss", "green": "mud", "pine": "mud", "magenta": "wine", "magenta_d": "berry_d",
          "cream": "taupe", "pale": "taupe"}
WITHER_MORE = {"jade": "mud", "leaf": "olive", "green": "mud", "pine": "outline", "magenta": "berry_d", "magenta_d": "grape_d",
               "cream": "dusty", "pale": "dusty", "olive": "mud", "moss": "olive"}


def plant_leaves(img, d: str, rustle: int) -> None:
    for (x, y, rx) in ((9 - rustle, FEET - 2.5, 5), (23 + rustle, FEET - 2.5, 5), (16, FEET - 2, 4)):
        shaded_ellipse(img, x, y, rx, 1.8, PLANT_STALK)
        hline(img, int(x - rx) + 2, int(x + rx) - 2, int(y), PLANT_STALK["l"])


def plant_stalk(img, x0: float, y0: float, x1: float, y1: float, bend: float) -> None:
    for k in range(13):
        t = k / 12
        cx = (1 - t) ** 2 * x0 + 2 * (1 - t) * t * (x0 + bend) + t * t * x1
        cy = (1 - t) ** 2 * y0 + 2 * (1 - t) * t * ((y0 + y1) / 2) + t * t * y1
        shaded_ellipse(img, cx, cy, 1.8 - 0.3 * t, 1.6, PLANT_STALK)


def plant_head_front(img, hx: int, hy: int, o: int, back: bool) -> None:
    """Cabeza de frente (o de espaldas si `back`): mandíbula de arriba en cúpula, la de abajo, fauces y dientes."""
    shaded_ellipse(img, hx, hy + 2 + o / 2, 6.5, 3, PLANT)
    if back:
        shaded_ellipse(img, hx, hy - o / 2, 7, 4, PLANT)
        for (sx, sy) in ((-3, -2), (2, -3), (4, 0), (-1, 1)):
            put(img, hx + sx, hy + sy, "pale")
        return
    if o:
        ellipse(img, hx, hy + 1.5, 5.5, 0.7 + o * 0.7, PLANT_MAW[0])
        hline(img, hx - 3, hx + 2, int(hy + 1.5), PLANT_MAW[1])
    shaded_ellipse(img, hx, hy - 1 - o / 2, 7, 3.5, PLANT)
    for (sx, sy) in ((-3, -3), (2, -4), (4, -2)):
        put(img, hx + sx, int(hy + sy - o / 2), "pale")
    top_edge = int(hy + 2 - o / 2)
    bot_edge = int(hy + 1 + o / 2)
    for k, x in enumerate(range(hx - 5, hx + 5, 2)):  # dientes
        put(img, x, top_edge, PLANT_TEETH)
        put(img, x + 1, bot_edge if o else top_edge + 1, PLANT_TEETH)


def plant_head_side(img, hx: int, hy: int, o: int) -> None:
    """Cabeza de perfil, con la bisagra atrás y las fauces abiertas hacia delante."""
    shaded_ellipse(img, hx + 1, hy + 2 + o * 0.6, 5, 2.2, PLANT)
    if o:
        for k in range(1 + o):
            hline(img, hx, hx + 5, hy + 1 + k - o // 2, PLANT_MAW[0] if k else PLANT_MAW[1])
    shaded_ellipse(img, hx + 1, hy - 1 - o * 0.6, 5.5, 2.8, PLANT)
    put(img, hx - 1, hy - 3 - o // 2, "pale")
    put(img, hx + 2, hy - 3 - o // 2, "pale")
    top_edge = int(hy + 1 - o * 0.6)
    bot_edge = int(hy + 1 + o * 0.6)
    for x in range(hx + 1, hx + 6, 2):
        put(img, x, top_edge, PLANT_TEETH)
        put(img, x + 1, bot_edge if o else top_edge + 1, PLANT_TEETH)


def trap_plant_body(d: str, p: dict) -> np.ndarray:
    img = canvas(32, 32)
    sway = p.get("step", 0) if p.get("walking") else 0
    o = p.get("open", [1, 0][p.get("phase", 0) % 2] if not p.get("walking") else 1)
    if p.get("eyes") == "closed":
        o = 0
    lower, lunge, wilt = p.get("lower", 0), p.get("lunge", 0), p.get("wilt", 0)
    fx_, fy_ = fwd_vec(d)
    hx = 16 + sway + (fx_ * lunge if d == "e" else 0) + (wilt if d == "e" else 0)
    hy = 12 + lower + (fy_ * lunge if d != "e" else 0) + wilt * 3
    plant_leaves(img, d, abs(sway))
    plant_stalk(img, 16, FEET - 2, hx - (2 if d == "e" else 0), hy + 4, -sway * 2 - (3 if d == "e" else 0) + wilt)
    if d == "e":
        plant_head_side(img, hx, hy, o)
    else:
        plant_head_front(img, hx, hy, o, d == "n")
    if p.get("roots"):
        for k, (rx, ry) in enumerate(((5, FEET - 1), (27, FEET - 2), (10, FEET - 3), (22, FEET - 1))):
            n = p["roots"] - (k % 2)
            if n > 0:
                line(img, rx, ry, rx + (1 if k % 2 else -1), ry - n, "rust")
                put(img, rx + (1 if k % 2 else -1), ry - n, "clay")
    return img


def trap_plant(spec: dict, d: str, frame: str, size: int = 32) -> np.ndarray:
    body = lambda dd, p: finish(trap_plant_body(dd, p))
    fx_, fy_ = fwd_vec(d)

    def attack(dd, i):  # escupe espinas
        img = finish(trap_plant_body(dd, {"open": [0, 3, 3, 1][i], "lunge": [-2, -1, 2, 0][i]}))
        if i == 2:
            tip = {"e": (29, 12), "s": (16, 22), "n": (16, 4)}[dd]
            fx_sparkle(*tip, 2, "pale", "cream")(img)
            spikes = {"e": [(26, 11), (27, 13)], "s": [(14, 24), (18, 25)], "n": [(14, 6), (18, 7)]}[dd]
            fx_pixels(spikes, "pale")(img)
        return img

    def cast(dd, i):  # "Raíces trampa": baja la cabeza y brotan raíces
        img = finish(trap_plant_body(dd, {"open": 2, "lower": 3, "roots": 2 + i}))
        fx_sparkle([6, 26, 16][i], [21, 20, 24][i], 1, "lime", "leaf")(img)
        return img

    def death(dd, i):  # se marchita
        if i == 0:
            return shift(finish(trap_plant_body(dd, {"open": 0, "lower": 2})), -fx_, 0)
        raw = trap_plant_body(dd, {"open": 0, "wilt": [0, 1, 3, 4][i]})
        if i == 1:
            return finish(recolor(raw, WITHER))
        img = finish(recolor(recolor(raw, WITHER), WITHER_MORE) if i == 3 else recolor(raw, WITHER))
        return squash_rows(img, 3, FEET)

    return animate(body, d, frame, attack, cast, death)


# Guardián de la cripta (élite): coloso de piedra oscura con costillar de hueso en el pecho, hombreras con musgo, yelmo de
# visera que brilla en verde con cuernos de raíz y una losa de tumba enorme por arma. "Golpe de losa": la alza y la deja
# caer levantando polvo; el casteo la sostiene en alto mientras le trepan raíces ("Coraza de raíces").
GUARD = mat("plum", "lavgray", "ink")
GUARD_FAR = mat("ink", "plum", "outline")
SLAB = mat("silver", "mist", "lavgray")
GUARD_EYE = "lime"


def tombstone(img, x: int, y: int, w: int, h: int, back: bool = False) -> None:
    """Losa de tumba: rectángulo de remate redondo, con cruz grabada, grieta y musgo arriba."""
    shaded_rect(img, x, y + 2, w, h - 2, SLAB)
    shaded_ellipse(img, x + w / 2, y + 2.5, w / 2, 2.5, SLAB)
    if not back:
        cx = x + w // 2
        vline(img, cx - (w % 2 == 0), y + 3, y + 3 + min(7, h - 6), SLAB["d"])
        hline(img, cx - 2, cx + 1, y + 5, SLAB["d"])
        line(img, x + 1, y + h - 5, x + 3, y + h - 3, "plum")
    put(img, x + 2, y + 1, "jade")
    put(img, x + 3, y, "leaf")


def slab_horizontal(img, x: int, y: int, w: int) -> None:
    shaded_rect(img, x, y, w, 4, SLAB)
    put(img, x + w - 1, y, (0, 0, 0, 0))
    hline(img, x + 2, x + w - 4, y + 1, SLAB["d"])


def root_horn(img, x: int, y: int, side: int) -> None:
    """Cuerno corto de raíz retorcida, con un brote en la punta."""
    line(img, x, y, x + side * 2, y - 2, BARK["b"])
    put(img, x, y - 1, BARK["l"])
    line(img, x + side * 2, y - 2, x + side * 2, y - 3, BARK["b"])
    put(img, x + side * 3, y - 2, BARK["d"])
    put(img, x + side, y - 4, "leaf")


def guardian_head(img, hx: int, hy: int, d: str, eyes: str) -> None:
    if d == "e":
        root_horn(img, hx - 1, hy, -1)
        root_horn(img, hx + 2, hy, 1)
    else:
        root_horn(img, hx - 3, hy, -1)
        root_horn(img, hx + 2, hy, 1)
    shaded_ellipse(img, hx, hy + 1, 3.6, 3.6, GUARD)
    put(img, hx - 1, hy - 2, "jade")
    if d == "s":
        hline(img, hx - 3, hx + 2, hy + 1, "outline")
        if eyes == "open":
            put(img, hx - 2, hy + 1, GUARD_EYE)
            put(img, hx + 1, hy + 1, GUARD_EYE)
        vline(img, hx - 1, hy + 2, hy + 3, "outline")
    elif d == "e":
        hline(img, hx, hx + 3, hy + 1, "outline")
        if eyes == "open":
            put(img, hx + 2, hy + 1, GUARD_EYE)


def crypt_guardian_body(d: str, p: dict) -> np.ndarray:
    img = canvas(32, 32)
    st, eyes, slab = p.get("step", 0), p.get("eyes", "open"), p.get("slab", "rest")
    by = p.get("bob", 0)
    G = GUARD
    roots = p.get("roots", 0)
    if d == "e":
        # Pierna lejana, torso, hombrera, losa delante y pierna cercana.
        shaded_rect(img, 11 - st, 20 + by, 4, FEET - 20 - by, GUARD_FAR)
        shaded_rect(img, 9, 10 + by, 11, 12, G)
        for yy in (13, 15, 17):
            hline(img, 15, 18, yy + by, "mist")
        hline(img, 9, 19, 20 + by, "ink")
        shaded_ellipse(img, 13, 11 + by, 4, 3.2, G)
        put(img, 11, 8 + by, "jade")
        put(img, 12, 8 + by, "leaf")
        shaded_rect(img, 15 + st, 20 + by, 4, FEET - 20 - by, G)
        guardian_head(img, 16, 6 + by, d, eyes)
        if slab == "slam":
            slab_horizontal(img, 18, FEET - 4, 13)
            line(img, 17, 14 + by, 19, FEET - 4, G["b"])
        else:  # en reposo, delante; alzada o en alto, delante de la cara
            top = {"rest": 9, "raised": 4, "high": 1}[slab] + by
            tombstone(img, 20, top, 7, 17 if slab == "rest" else 14)
            rect(img, 19, top + 7, 3, 3, G["l"])
    else:
        front = d == "s"
        for x, lift in ((10, max(0, st)), (18, max(0, -st))):
            shaded_rect(img, x, 20 + by, 4, FEET - 20 - by - lift, G if front else GUARD_FAR)
        sx = 1 if front else 22  # la losa a un lado (de frente, en la mano de la izquierda de la imagen)
        if slab == "slam" and not front:
            slab_horizontal(img, 5, 15, 22)  # en el suelo delante: tapada por el cuerpo
        shaded_rect(img, 9, 10 + by, 14, 12, G)
        if front:
            for yy in (13, 15, 17):  # costillar de hueso
                hline(img, 12, 14, yy + by, "mist")
                hline(img, 17, 19, yy + by, "mist")
            vline(img, 15, 12 + by, 18 + by, "silver")
            vline(img, 16, 12 + by, 18 + by, "silver")
            hline(img, 9, 22, 20 + by, "ink")
            rect(img, 15, 20 + by, 2, 1, "mist")
        else:
            vline(img, 15, 11 + by, 20 + by, G["l"])
            vline(img, 16, 11 + by, 20 + by, G["d"])
        for side in (-1, 1):
            shaded_ellipse(img, 16 + side * 7.5, 11 + by, 4, 3.2, G)
            put(img, 16 + side * 8 - 1, 8 + by, "jade")
            put(img, 16 + side * 8, 8 + by, "leaf")
        if slab != "slam":
            shaded_rect(img, 24 if front else 5, 14 + by, 4, 7, G)
            rect(img, 24 if front else 5, 21 + by, 4, 2, G["l"])
        guardian_head(img, 16, 6 + by, d, eyes)
        if slab != "slam":  # a un lado; alzada o en alto, a la altura de la cabeza
            top = {"rest": 9, "raised": 4, "high": 1}[slab] + by
            tombstone(img, sx + (1 if front else 0), top, 8, 17 if slab == "rest" else 15, back=not front)
            rect(img, (7 if front else 22), top + 7, 3, 3, G["l"])
        else:
            for side in (-1, 1):
                line(img, 16 + side * 7, 13 + by, 16 + side * 6, FEET - 5 if front else 17, G["b"])
                line(img, 16 + side * 8, 13 + by, 16 + side * 7, FEET - 5 if front else 17, G["d"])
            if front:
                slab_horizontal(img, 8, FEET - 4, 16)
    if roots:
        for (rx, top) in ((11, 27 - roots * 2), (19, 27 - roots * 2), (9, 22 - roots)):
            line(img, rx, FEET - 1, rx + 1, top, BARK["b"])
            put(img, rx + 1, top, "leaf")
    return img


def guardian_pile(flat: bool) -> np.ndarray:
    img = canvas(32, 32)
    slab_horizontal(img, 16, FEET - 4, 14)
    shaded_ellipse(img, 13, FEET - 3, 9, 3 if not flat else 2.2, GUARD)
    for x in (8, 12, 16):
        hline(img, x, x + 1, FEET - 4 + flat, "mist")
    shaded_ellipse(img, 10, FEET - 6 + flat, 3.2, 2.8, GUARD)
    hline(img, 8, 11, FEET - 6 + flat, "outline")
    root_horn(img, 8, FEET - 8 + flat, -1)
    if flat:
        line(img, 5, FEET - 1, 7, FEET - 6, BARK["b"])
        put(img, 7, FEET - 6, "leaf")
        line(img, 19, FEET - 1, 18, FEET - 5, BARK["b"])
    return img


def crypt_guardian(spec: dict, d: str, frame: str, size: int = 32) -> np.ndarray:
    body = lambda dd, p: finish(crypt_guardian_body(dd, p))
    fx_, fy_ = fwd_vec(d)

    def attack(dd, i):  # "Golpe de losa"
        img = finish(crypt_guardian_body(dd, {"slab": ["raised", "high", "slam", "rest"][i], "bob": [0, 0, 1, 0][i]}))
        if i == 2:
            fx_dust(16, FEET, dd)(img)
            if dd != "e":
                fx_dust(16, FEET, "n" if dd == "s" else "s")(img)
        if i == 1:
            fx_trail(16, 12, -150, -60, 9, ("mist", "white"))(img)
        return img

    def cast(dd, i):  # losa en alto y raíces que trepan
        img = finish(crypt_guardian_body(dd, {"slab": "high", "roots": 1 + i}))
        for k, (x, y) in enumerate(((5, 14), (27, 12), (4, 22), (28, 20))):
            if (k + i) % 2 == 0:
                fx_sparkle(x, y - i, 1, "leaf", "jade")(img)
        return img

    def death(dd, i):
        if i == 0:
            return shift(body(dd, {"eyes": "closed"}), -fx_, 0)
        if i == 1:
            return kneel(body(dd, {"eyes": "closed"}), FEET, 5)
        return finish(guardian_pile(i == 3))

    return animate(body, d, frame, attack, cast, death)


# --- Jefe de la Cripta: Árbol Podrido (HU-114 CA2, HU-117) ----------------------------------------------------------------

# Árbol Podrido (jefe): hoja de 64×64 como el Capataz, pero dibujada a esa escala (no un cuerpo de 32 ampliado). Tronco muerto y
# retorcido que se abre en contrafuertes, una cara en el tronco (cuencas hondas que brillan en verde, ceño de corteza y boca de
# astillas), copa rala de ramas secas con matas de hojas podridas y musgo colgando, hongos morados en la corteza y raíces gruesas
# que se abren por el suelo. Es inmóvil: su "andar" es la copa que se mece. Azota con raíces (ataque: alza una rama y la descarga
# mientras las raíces revientan delante), al castear (Raíces, Esporas, Retoños) abre la boca, se le encienden los ojos y las
# raíces y suelta esporas, y al morir se agrieta, se le cae la copa y queda un tocón.
TREE = 64
TF = TREE - 4                      # pies
TREE_TOP = 14                      # donde empiezan las ramas
TREE_BARK = mat("mauve", "dusty", "ink")
TREE_LEAF = mat("olive", "moss", "mud")
TREE_MOSS = ("sage", "sage_l", "sage_d")
TREE_FUNGUS = mat("berry", "rose", "berry_d")
TREE_EYE = ("lime", "cream")
TREE_SPORES = ("sage_p", "pale", "lime")
TREE_WITHER = {"olive": "mud", "moss": "olive", "sage": "sage_d", "sage_l": "sage", "lime": "olive", "cream": "taupe",
               "rose": "berry", "berry": "berry_d", "pale": "sage_p"}
TREE_WITHER_MORE = {"dusty": "mauve", "taupe": "dusty", "mud": "coal", "olive": "mud", "sage_d": "coal", "sage": "sage_d",
                    "berry_d": "grape_d", "sage_p": "taupe"}
# Ramas (desde el tronco hasta la punta, grosor) y matas de hojas podridas (centro y radios) de la copa.
TREE_BRANCHES = (((28, 17), (12, 8), 2), ((36, 17), (52, 7), 2), ((32, 15), (31, 3), 2), ((26, 24), (6, 17), 2),
                 ((38, 24), (58, 16), 2), ((19, 12), (21, 6), 1), ((44, 11), (42, 4), 1), ((14, 20), (10, 25), 1))
TREE_CLUMPS = ((12, 7, 6, 3.5), (52, 6, 6.5, 3.5), (31, 4, 5.5, 3), (6, 16, 4.5, 3), (58, 15, 4.5, 3), (21, 6, 4, 2.5),
               (42, 4, 4, 2.5))
TREE_HANGING = ((9, 9, 7), (15, 10, 5), (47, 9, 8), (55, 8, 5), (4, 18, 5), (60, 17, 6), (27, 6, 4), (36, 6, 6), (19, 8, 3))
TREE_ROOTS = (((26, TF - 4), (14, TF - 1), (3, TF), 3.2), ((38, TF - 4), (50, TF - 1), (61, TF - 1), 3.2),
              ((29, TF - 2), (24, TF + 1), (17, TF + 2), 2.4), ((35, TF - 2), (41, TF + 1), (47, TF + 2), 2.4))


def tree_x(y: int, lean: int) -> tuple[int, int]:
    """Bordes del tronco a la altura `y`: se abre en contrafuertes abajo, se retuerce y se inclina `lean` arriba (sin azar)."""
    k = (y - TREE_TOP) / (TF - TREE_TOP)
    half = 7.5 + 1.5 * k + 6 * k ** 4
    bulge = math.sin(k * 7.0)
    cx = 32 + lean * (1 - k) + 0.7 * bulge
    return (int(round(cx - half - max(0.0, bulge))) - (hash01(y, 3) > 0.8),
            int(round(cx + half + max(0.0, -bulge))) + (hash01(y, 5) > 0.8))


def tree_trunk(img, lean: int, top: int = TREE_TOP, crack: bool = False) -> None:
    B = TREE_BARK
    for y in range(top, TF):
        x0, x1 = tree_x(y, lean)
        hline(img, x0, x1, y, B["b"])
        hline(img, x0, x0 + 2, y, B["l"])
        hline(img, x1 - 1, x1, y, B["d"])
        if hash01(y, 9) > 0.7:
            put(img, x0 + 1, y, "taupe")  # luz de arriba a la izquierda en las vetas
    for sx, seed in ((-4, 1), (0, 2), (4, 3), (8, 4)):  # vetas y grietas de la corteza
        x = 32 + sx
        for y in range(max(top, 31), TF - 1):
            if hash01(y, seed) > 0.7:
                x += 1 if hash01(y, seed + 7) > 0.5 else -1
            x0, x1 = tree_x(y, lean)
            xx = max(x0 + 3, min(x1 - 3, x + round(lean * (1 - (y - TREE_TOP) / (TF - TREE_TOP)))))
            if hash01(y, seed + 3) > 0.2:  # veta casi continua, con luz a la izquierda a ratos
                put(img, xx, y, "ink")
                if hash01(y, seed + 5) > 0.75:
                    put(img, xx - 1, y, B["l"])
    for (x, y) in ((26, 38), (37, 44), (29, 52), (36, 34)):  # nudos
        if y >= top:
            ellipse(img, x, y, 1.6, 1.2, "ink")
            put(img, x - 1, y - 1, B["l"])
    if crack:  # al morir: una grieta abre el tronco de arriba abajo
        x = 32
        for y in range(top, TF - 4):
            if hash01(y, 21) > 0.6:
                x += 1 if (hash01(y, 22) > 0.5) == (x < 33) else -1
            hline(img, x, x + (y % 3 == 0), y, "outline")


def tree_roots(img, wiggle: int = 0, glow: int = 0) -> None:
    """Raíces gruesas que se afinan hacia la punta (círculos a lo largo de dos tramos), sombreadas aparte."""
    layer = canvas(TREE, TREE)
    for k, (a, b, c, w) in enumerate(TREE_ROOTS):
        lift = (k + wiggle) % 2 if wiggle else 0
        for (p0, p1, r0, r1) in ((a, b, w, w * 0.6), (b, c, w * 0.6, 0.6)):
            n = max(abs(p1[0] - p0[0]), abs(p1[1] - p0[1])) * 2
            for j in range(n + 1):
                t = j / n
                r = r0 + (r1 - r0) * t
                ellipse(layer, p0[0] + (p1[0] - p0[0]) * t, p0[1] + (p1[1] - p0[1]) * t - lift * t, r, r * 0.7, TREE_BARK["b"])
    shade_edges(layer, TREE_BARK["l"], TREE_BARK["d"])
    if glow:  # Raíces: se encienden al castear
        for k, (a, b, c, w) in enumerate(TREE_ROOTS):
            for j in range(3):
                t = (j + glow / 3) / 3
                put(layer, round(b[0] + (c[0] - b[0]) * t), round(b[1] + (c[1] - b[1]) * t) - 1, "lime" if (j + k) % 2 else "leaf")
    paste(img, layer, 0, 0)


def tree_canopy(img, sway: int, droop: int = 0, holes: bool = True) -> None:
    B = TREE_BARK
    for (a, b, w) in TREE_BRANCHES:
        ex, ey = b[0] + sway, b[1] + droop
        line(img, a[0], a[1], ex, ey, B["d"])
        if w > 1:
            line(img, a[0], a[1] - 1, ex, ey - 1, B["l"])
    for k, (x, y, rx, ry) in enumerate(TREE_CLUMPS):
        cx, cy = x + sway * (1 + k % 2), y + droop
        for (ox, oy, f) in ((-0.45, 0.25, 0.65), (0.45, 0.35, 0.6), (0.0, -0.3, 0.7)):
            shaded_ellipse(img, cx + ox * rx, cy + oy * ry, rx * f, ry * f + 0.4, TREE_LEAF)
        if holes:  # la copa está podrida: huecos y hojas muertas
            for j in range(4):
                put(img, int(cx + (hash01(k, j, 1) - 0.5) * rx * 1.4), int(cy + (hash01(k, j, 2) - 0.45) * ry), (0, 0, 0, 0))
            put(img, int(cx + (hash01(k, 9) - 0.5) * rx), int(cy + 1), "mud")
        put(img, int(cx - rx / 2), int(cy - ry / 2), TREE_LEAF["l"])
    for k, (x, y, n) in enumerate(TREE_HANGING):  # musgo que cuelga y se mece con la copa
        for j in range(n):
            put(img, x + sway + ((j + k) // 3) % 2 - (j > n // 2 and sway < 0), y + droop + j,
                TREE_MOSS[(j + k) % 3 if j < n - 1 else 1])


def tree_fungus(img, d: str, lean: int) -> None:
    """Hongos de repisa en la corteza (en otro sitio por detrás)."""
    spots = ((-1, 38, 5), (1, 47, 4), (-1, 54, 3), (1, 31, 3)) if d != "n" else ((1, 34, 5), (-1, 43, 4), (1, 53, 3))
    for side, y, w in spots:
        x0, x1 = tree_x(y, lean)
        x = x0 - w + 2 if side < 0 else x1 - 1
        hline(img, x, x + w - 1, y, TREE_FUNGUS["b"])
        hline(img, x + 1, x + w - 2, y - 1, TREE_FUNGUS["l"])
        hline(img, x + 1, x + w - 2, y + 1, TREE_FUNGUS["d"])
        put(img, x + w // 2, y - 1, "pink_p")


def tree_face(img, d: str, eyes: str, mouth: int, glow: int, lean: int) -> None:
    """Cara en el tronco: cuencas hondas con brillo verde (crema si castea), ceño de corteza y boca de astillas."""
    cx = 32 + round(lean * (1 - (20 - TREE_TOP) / (TF - TREE_TOP)))
    B = TREE_BARK
    if d == "n":  # por detrás: un hueco oscuro en la corteza
        ellipse(img, cx + 1, 26, 3, 5, "outline")
        ellipse(img, cx + 1, 27, 2, 3.5, "ink")
        hline(img, cx - 1, cx + 3, 20, B["l"])
        return
    eye_xs = (cx - 6, cx + 3) if d == "s" else (cx + 3,)
    core = TREE_EYE[1] if glow else TREE_EYE[0]
    for n, ex in enumerate(eye_xs):
        inner = (n == 0) == (d == "s")  # el lado del ceño que baja hacia la nariz
        line(img, ex - 1, 16 + (0 if inner else 1), ex + 4, 16 + (1 if inner else 0), "ink")  # ceño
        hline(img, ex - 1, ex + 4, 15 + (0 if inner else 1), B["l"])
        rect(img, ex, 18, 4, 3, "outline")
        if eyes == "open":
            hline(img, ex + 1, ex + 2, 19, TREE_EYE[0])
            put(img, ex + 1 + (n == 0), 19, core)
            if glow > 1:
                hline(img, ex + 1, ex + 2, 20, TREE_EYE[0])
                put(img, ex + 1, 18, "pale")
        elif eyes == "closed":
            rect(img, ex, 18, 4, 2, B["b"])
            hline(img, ex, ex + 3, 20, "outline")
        else:  # muerto: aspa
            for (ox, oy) in ((0, 0), (1, 1), (2, 1), (3, 0), (0, 2), (3, 2)):
                put(img, ex + ox, 18 + oy, "ink")
            hline(img, ex + 1, ex + 2, 19, "outline")
    if d == "s":
        vline(img, cx, 19, 22, B["l"])  # caballete de la nariz
        put(img, cx + 1, 22, B["d"])
    # Boca: una raja dentada que se abre al castear y al pegar.
    x0, x1 = (cx - 5, cx + 5) if d == "s" else (cx + 1, cx + 7)
    rows = 2 + mouth
    for r in range(rows):
        taper = 1 if r in (0, rows - 1) and rows > 2 else 0
        hline(img, x0 + taper, x1 - taper, 25 + r, "outline" if r else "ink")
    for x in range(x0 + 1, x1, 3):  # astillas arriba y abajo
        put(img, x, 25, "taupe")
        put(img, x, 26, "taupe" if rows > 2 else "outline")
        if rows > 2:
            put(img, x + 1, 25 + rows - 1, "taupe")
    hline(img, x0 + 1, x1 - 1, 25 + rows, B["l"])  # labio de corteza
    if d == "e":  # nariz de nudo en el perfil
        put(img, x1 + 1, 21, B["b"])
        put(img, x1 + 2, 22, B["d"])
        put(img, x1 + 1, 22, B["l"])


def tree_arm(img, pose: str, d: str) -> None:
    """La rama con la que azota: en alto, más alta, golpeando hacia delante o volviendo."""
    base = (41, 31)
    tip = {"raised": (55, 15), "high": (50, 5), "mid": (57, 33),
           "slam": {"s": (50, 56), "e": (61, 47), "n": (54, 41)}[d]}[pose]
    for o in range(3):
        line(img, base[0], base[1] + o - 1, tip[0], tip[1] + (o > 1), (TREE_BARK["l"], TREE_BARK["b"], TREE_BARK["d"])[o])
    for (ox, oy) in ((3, -2), (3, 2), (0, 3), (-2, 3)):  # dedos de rama
        line(img, tip[0], tip[1], tip[0] + ox, tip[1] + oy, TREE_BARK["d"])


def tree_spores(img, phase: int, n: int) -> None:
    pts = ((8, 28), (56, 26), (18, 1), (46, 0), (3, 8), (61, 9), (22, 30), (43, 29), (11, 36), (53, 38))
    for k in range(n):
        x, y = pts[(k + phase * 3) % len(pts)]
        put(img, x, y - phase, TREE_SPORES[(k + phase) % 3])
        if k % 3 == 0:
            put(img, x + 1, y - phase - 1, TREE_SPORES[0])


def rotten_tree_body(d: str, p: dict) -> np.ndarray:
    img = canvas(TREE, TREE)
    sway = p.get("step", 0) if p.get("walking") else [0, 1][p.get("phase", 0) % 2] * (1 if d != "n" else -1)
    lean = {"s": 0, "n": 0, "e": -1}[d]
    eyes, mouth, glow = p.get("eyes", "open"), p.get("mouth", 0), p.get("glow", 0)
    tree_canopy(img, sway, p.get("droop", 0))
    tree_trunk(img, lean, crack=p.get("crack", False))
    tree_roots(img, wiggle=p.get("wiggle", 0), glow=p.get("root_glow", 0))
    tree_fungus(img, d, lean)
    tree_face(img, d, eyes, mouth, glow if eyes == "open" else 0, lean)
    if p.get("arm"):
        tree_arm(img, p["arm"], d)
    if p.get("hurt"):  # astillas al recibir el golpe
        for (x, y) in ((21, 30), (44, 26), (19, 40), (45, 37)):
            put(img, x, y, "taupe")
    return img


def tree_stump(fallen: bool) -> np.ndarray:
    """Muerte: tocón partido con raíces y la copa caída a un lado, ya marchita."""
    img = canvas(TREE, TREE)
    top = 38
    if fallen:
        for (x, y, rx, ry) in ((10, TF - 4, 7, 3), (19, TF - 6, 5, 2.5), (54, TF - 4, 6, 2.5)):
            shaded_ellipse(img, x, y, rx, ry, TREE_LEAF)
        for (x0, y0, x1, y1) in ((4, TF - 7, 20, TF - 2), (44, TF - 8, 59, TF - 3)):
            line(img, x0, y0, x1, y1, TREE_BARK["b"])
            line(img, x0, y0 + 1, x1, y1 + 1, TREE_BARK["d"])
    tree_trunk(img, 0, top=top)
    for x in range(tree_x(top, 0)[0], tree_x(top, 0)[1] + 1):  # borde roto en dientes de sierra
        for y in range(top, top + 1 + int(hash01(x, 31) * 5)):
            put(img, x, y, (0, 0, 0, 0))
    tree_roots(img)
    tree_fungus(img, "s", 0)
    return img


def rotten_tree(spec: dict, d: str, frame: str, size: int = TREE) -> np.ndarray:
    body = lambda dd, p: finish(rotten_tree_body(dd, p))
    fx_, fy_ = fwd_vec(d)

    def attack(dd, i):  # azote de raíces: alza la rama, la descarga y las raíces revientan delante
        arm = ["raised", "high", "slam", "mid"][i]
        img = finish(rotten_tree_body(dd, {"arm": arm, "mouth": [1, 1, 2, 0][i], "glow": int(i in (1, 2)), "wiggle": int(i == 2)}))
        if i == 2:
            burst = {"s": (32, 63), "e": (57, 59), "n": (32, 50)}[dd]
            for k, (ox, h) in enumerate(((-7, 5), (-3, 8), (2, 7), (6, 5))):
                bx = burst[0] + ox
                for o in range(2):
                    line(img, bx + o, burst[1], bx + o + (1 if k % 2 else -1), burst[1] - h, TREE_BARK["d" if o else "l"])
                put(img, bx + (1 if k % 2 else -1), burst[1] - h - 1, "outline")
            fx_dust(burst[0] - fx_ * 8, min(burst[1], TF + 2), dd)(img)
        if i == 1:
            fx_trail(46, 20, -120, -40, 14, ("mist", "white"))(img)
        return img

    def cast(dd, i):  # Raíces, Esporas y Retoños: boca abierta, ojos encendidos, esporas y raíces que brillan
        img = finish(rotten_tree_body(dd, {"mouth": 2, "glow": 2, "root_glow": i + 1, "phase": i % 2}))
        tree_spores(img, i, 8)
        fx_sparkle(*((32, 8), (14, 12), (50, 11))[i], 1, "lime", "cream")(img)
        return img

    def death(dd, i):
        if i == 0:
            img = shift(body(dd, {"eyes": "closed", "mouth": 1}), -fx_, 0)
            fx_pixels([(14, 20), (48, 22), (24, 30), (40, 34)], "moss")(img)  # caen hojas
            return img
        if i == 1:
            return finish(recolor(rotten_tree_body(dd, {"eyes": "x", "mouth": 2, "droop": 3, "crack": True}), TREE_WITHER))
        if i == 2:
            raw = rotten_tree_body(dd, {"eyes": "x", "mouth": 1, "droop": 7, "crack": True})
            return squash_rows(finish(recolor(recolor(raw, TREE_WITHER), TREE_WITHER_MORE)), 9, TF)
        return finish(recolor(recolor(tree_stump(True), TREE_WITHER), TREE_WITHER_MORE))

    return animate(body, d, frame, attack, cast, death)


# Retoño podrido (invocación, 32×32): un arbolito que anda sobre dos raíces, con la misma madera, ojos verdes, un penacho de
# hojas podridas y brazos de rama. Araña con las ramas (ataque), se sacude soltando esporas (casteo) y al morir se deshace.
SAPLING_LEAF = mat("olive", "moss", "mud")


def rotten_sapling_body(d: str, p: dict) -> np.ndarray:
    img = canvas(32, 32)
    B = TREE_BARK
    st, bob, eyes = p.get("step", 0), p.get("bob", 0), p.get("eyes", "open")
    lunge, arms = p.get("lunge", 0), p.get("arms", 0)
    fx_, fy_ = fwd_vec(d)
    cx = 16 + (fx_ * lunge if d == "e" else 0)
    cyl = 13 + bob + (fy_ * lunge if d != "e" else 0)
    # Piernas de raíz.
    if d == "e":
        for lx, ph in ((cx - 2, -st), (cx + 2, st)):
            line(img, lx, cyl + 10, lx + ph, FEET - 1, B["d"])
            line(img, lx + 1, cyl + 10, lx + ph + 1, FEET - 1, B["b"])
            put(img, lx + ph + 2, FEET - 1, B["d"])
    else:
        for lx, ph, out in ((cx - 2, st, -1), (cx + 2, -st, 1)):
            foot = FEET - 1 + min(0, ph)
            line(img, lx, cyl + 10, lx + out, foot, B["d"])
            line(img, lx + 1, cyl + 10, lx + 1 + out, foot, B["b"])
            put(img, lx + out * 2 + (1 if out > 0 else 0), foot, B["d"])
    # Tronco (cuerpo).
    for y in range(cyl, cyl + 11):
        half = 3 + (y - cyl) * 0.15
        hline(img, int(cx - half), int(cx + half), y, B["b"])
        put(img, int(cx - half), y, B["l"])
        put(img, int(cx + half), y, B["d"])
    put(img, cx - 1, cyl + 8, "ink")
    put(img, cx + 1, cyl + 9, B["d"])
    # Brazos de rama: abajo (0), hacia delante (1) o en alto (2).
    for side in (-1, 1):
        sx = cx + side * 4
        if arms == 2:
            line(img, sx, cyl + 4, sx + side * 3, cyl - 2, B["d"])
            put(img, sx + side * 4, cyl - 3, B["d"])
        elif arms == 1:
            tx = sx + (fx_ * 5 if d == "e" else side)
            ty = cyl + 4 + (fy_ * 5 if d != "e" else 1)
            line(img, sx, cyl + 4, tx, ty, B["d"])
            put(img, tx, ty, B["l"])
        else:
            line(img, sx, cyl + 4, sx + side * 2, cyl + 8, B["d"])
    # Penacho de hojas podridas.
    shaded_ellipse(img, cx - (1 if d == "e" else 0), cyl - 2, 5, 3, SAPLING_LEAF)
    put(img, cx - 3, cyl - 4, (0, 0, 0, 0))
    put(img, cx + 2, cyl - 3, (0, 0, 0, 0))
    put(img, cx + 3, cyl, TREE_MOSS[1])
    put(img, cx + 3, cyl + 1, TREE_MOSS[0])
    put(img, cx - 4, cyl, TREE_MOSS[0])
    vline(img, cx + 1, cyl - 7, cyl - 5, B["d"])
    put(img, cx + 2, cyl - 7, SAPLING_LEAF["l"])
    # Cara.
    if d != "n":
        for ex in ((cx - 2, cx + 1) if d == "s" else (cx + 2,)):
            eye(img, ex, cyl + 2, eyes, TREE_EYE[0], 2)
        if d == "s":
            hline(img, cx - 1, cx, cyl + 5, "ink")
        else:
            put(img, cx + 3, cyl + 5, "ink")
    else:
        put(img, cx, cyl + 3, "ink")
        put(img, cx, cyl + 4, "ink")
    if p.get("spores"):
        k = p["spores"]
        fx_pixels([(cx - 6, cyl - 3 - k), (cx + 6, cyl - 2 - k), (cx, cyl - 8 - k)], "pale")(img)
        fx_pixels([(cx - 5, cyl - 6 - k), (cx + 5, cyl - 5 - k)], "sage_p")(img)
    return img


def rotten_sapling(spec: dict, d: str, frame: str, size: int = 32) -> np.ndarray:
    body = lambda dd, p: finish(rotten_sapling_body(dd, p))

    def attack(dd, i):  # arañazo de ramas
        img = finish(rotten_sapling_body(dd, {"arms": [2, 2, 1, 0][i], "lunge": [-1, 0, 2, 0][i]}))
        if i == 2:
            tip = {"e": (27, 17), "s": (16, 27), "n": (16, 7)}[dd]
            fx_sparkle(*tip, 1, "mist", "white")(img)
        return img

    def cast(dd, i):  # se sacude y suelta esporas
        return finish(rotten_sapling_body(dd, {"arms": 2, "bob": [0, 1, 0][i], "spores": i + 1}))

    def death(dd, i):  # se deshace en ramitas
        if i < 2:
            return fall_over(body, dd, i)
        img = canvas(32, 32)
        for k, (x0, y0, x1, y1) in enumerate(((9, FEET - 1, 15, FEET - 3), (16, FEET - 1, 22, FEET - 2), (12, FEET - 3, 19, FEET - 4))):
            line(img, x0, y0, x1, y1 + (i == 3), TREE_BARK["d" if k % 2 else "b"])
        shaded_ellipse(img, 21, FEET - 3 + (i == 3), 3.5, 1.8, SAPLING_LEAF)
        out = finish(recolor(img, TREE_WITHER) if i == 3 else img)
        eye(out, 13, FEET - 5 + (i == 3), "x", "")
        return out

    return animate(body, d, frame, attack, cast, death)


MONSTERS = {
    "monsters/forest_wolf": (forest_wolf, {}),
    "monsters/bandit_woodcutter": (humanoid, WOODCUTTER),
    "monsters/weaver_spider": (weaver_spider, {}),
    "monsters/old_bear": (old_bear, {}),
    "monsters/giant_toad": (giant_toad, {}),
    "monsters/lizardman": (humanoid, LIZARDMAN),
    "monsters/will_o_wisp": (will_o_wisp, {}),
    "monsters/swamp_witch": (swamp_witch, {}),
    "monsters/root_skeleton": (humanoid, ROOT_SKELETON),
    "monsters/moss_spirit": (moss_spirit, {}),
    "monsters/trap_plant": (trap_plant, {}),
    "monsters/crypt_guardian": (crypt_guardian, {}),
    "monsters/rotten_sapling": (rotten_sapling, {}),
}
# Jefes del Tier 2 a escala de jefe (64×64, como el Capataz de gen_chars.BOSSES), con su tamaño de cuadro.
BOSSES = {
    "monsters/rotten_tree": (rotten_tree, {}, TREE),
}


def build() -> list[str]:
    for rel, (draw, spec) in MONSTERS.items():
        save(gc.sheet(draw, spec), "sprites/" + rel + ".png")
        gc.write_meta(rel, 32)
    for rel, (draw, spec, size) in BOSSES.items():
        save(gc.sheet(draw, spec, size), "sprites/" + rel + ".png")
        gc.write_meta(rel, size)
    return list(MONSTERS) + list(BOSSES)


# Lámina de revisión: cuadros sueltos de cada hoja a ×3 sobre el suelo de su bioma y, debajo, los 12 junto a los del Tier 1
# a ×2 sobre hierba y sobre camino. No la llama `build`; la escribe en docs/screenshots/tier2/monsters.png.
LAMINA_FRAMES = [("s", "idle0"), ("n", "idle0"), ("e", "idle0"), ("e", "walk1"), ("e", "attack2"), ("s", "attack2"),
                 ("s", "cast1"), ("s", "hurt0"), ("s", "death3")]
BIOMES = {"Linde del Bosque": ("forest_wolf", "bandit_woodcutter", "weaver_spider", "old_bear"),
          "Pantano": ("giant_toad", "lizardman", "will_o_wisp", "swamp_witch"),
          "Cripta de Raíces": ("root_skeleton", "moss_spirit", "trap_plant", "crypt_guardian")}
TIER1 = ("slime", "boar", "boar_alpha", "bandit", "wolf", "wolf_alpha", "goblin_archer", "kobold_miner", "rubble_golem",
         "skeleton_warrior")


def lamina(path: str = "docs/screenshots/tier2/monsters.png") -> str:
    from pathlib import Path

    from PIL import Image, ImageDraw, ImageFont

    from pix import ASSETS, ROOT, rgba
    z, label_w, cell = 3, 150, 96
    ground = {"Linde del Bosque": "sage_d", "Pantano": "teal_d", "Cripta de Raíces": "coal"}
    sheets = {n: Image.fromarray(gc.sheet(*MONSTERS["monsters/" + n]), "RGBA") for names in BIOMES.values() for n in names}
    old = {n: Image.open(ROOT / "client/assets/sprites/monsters" / (n + ".png")).convert("RGBA") for n in TIER1}
    lineup = list(sheets) + list(TIER1)
    width = max(label_w + cell * len(LAMINA_FRAMES), 64 * len(lineup) + 16)
    height = 24 + sum(20 + cell * len(v) for v in BIOMES.values()) + 2 * (64 + 20) + 24
    out = Image.new("RGBA", (width, height), rgba("navy"))
    dr = ImageDraw.Draw(out)
    font = ImageFont.truetype(str(ASSETS / "fonts" / "AlegreyaSans-Medium.ttf"), 15)
    dr.text((8, 4), "HU-114 · monstruos del Tier 2 (×3): reposo s/n/e, andar, ataque e/s, casteo, golpe, muerte", fill=rgba("mist"), font=font)
    y = 24
    for biome, names in BIOMES.items():
        dr.text((8, y + 2), biome, fill=rgba("gold"), font=font)
        y += 20
        for n in names:
            dr.rectangle([label_w, y, width, y + cell - 1], fill=rgba(ground[biome]))
            dr.text((8, y + cell // 2 - 8), n, fill=rgba("white"), font=font)
            for k, (d, f) in enumerate(LAMINA_FRAMES):
                r, c = gc.DIRS.index(d), gc.COLS.index(f)
                fr = sheets[n].crop((c * 32, r * 32, c * 32 + 32, r * 32 + 32)).resize((cell, cell), Image.NEAREST)
                out.alpha_composite(fr, (label_w + k * cell, y))
                dr.line([(label_w + k * cell, y + 29 * z), (label_w + k * cell + 3, y + 29 * z)], fill=rgba("scarlet"))
            y += cell
    for title, bg in (("A ×2 sobre hierba: los 12 del Tier 2 y los 10 del Tier 1", "sage_d"), ("Sobre camino", "dusty")):
        dr.text((8, y + 2), title, fill=rgba("gold"), font=font)
        y += 20
        dr.rectangle([0, y, width, y + 63], fill=rgba(bg))
        for k, n in enumerate(lineup):
            src = sheets[n] if n in sheets else old[n]
            out.alpha_composite(src.crop((0, 0, 32, 32)).resize((64, 64), Image.NEAREST), (8 + k * 64, y))
        y += 64
    dest = Path(ROOT) / path
    dest.parent.mkdir(parents=True, exist_ok=True)
    out.save(dest)
    return str(dest)


# Lámina del jefe (HU-114 CA2): el Árbol Podrido a ×2 y sus retoños a ×3, en cada dirección, sobre el suelo de la Cripta, y
# la escala a ×2 junto al Capataz, el Guardián de la cripta y un humanoide de 32 (los héroes HD no caben en 256 colores). Texto sin suavizar para que quepa en una paleta de
# 256 colores (docs/screenshots no usa LFS). No la llama `build`; la escribe en docs/screenshots/tier2/rotten_tree.png.
LAMINA_BOSS_FRAMES = ["idle0", "walk1", "attack0", "attack1", "attack2", "attack3", "cast0", "cast1", "hurt0", "death0", "death1",
                      "death2", "death3"]


def save_indexed(img, dest) -> None:
    """PNG de paleta si la imagen tiene como mucho 256 colores (sin pérdida); si no, RGB."""
    from PIL import Image
    rgb = img.convert("RGB")
    colors = rgb.getcolors(256)
    if colors is None:
        rgb.save(dest, optimize=True)
        return
    pal = [c for _, c in colors]
    index = {c: i for i, c in enumerate(pal)}
    arr = np.asarray(rgb)
    flat = arr.reshape(-1, 3)
    idx = np.fromiter((index[tuple(p)] for p in flat), dtype=np.uint8, count=flat.shape[0]).reshape(arr.shape[:2])
    out = Image.fromarray(idx, "P")
    out.putpalette([v for c in pal for v in c])
    out.save(dest, optimize=True)


def lamina_arbol(path: str = "docs/screenshots/tier2/rotten_tree.png") -> str:
    from pathlib import Path

    from PIL import Image, ImageDraw, ImageFont

    from pix import ASSETS, ROOT, rgba
    label_w = 70
    tree = Image.fromarray(gc.sheet(rotten_tree, {}, TREE), "RGBA")
    sap = Image.fromarray(gc.sheet(rotten_sapling, {}), "RGBA")
    rows = [("Árbol Podrido", tree, TREE, 2, LAMINA_BOSS_FRAMES), ("Retoño", sap, 32, 3, gc.COLS)]
    width = label_w + max(size * z * len(frames) for _, _, size, z, frames in rows)
    height = 24 + sum(20 + size * z * 3 for _, _, size, z, _ in rows) + 20 + 140 + 8
    out = Image.new("RGBA", (width, height), rgba("navy"))
    dr = ImageDraw.Draw(out)
    dr.fontmode = "1"
    font = ImageFont.truetype(str(ASSETS / "fonts" / "AlegreyaSans-Medium.ttf"), 15)
    dr.text((8, 4), "HU-114 CA2 · Árbol Podrido (64×64, ×2) y retoños (32×32, ×3): " + ", ".join(LAMINA_BOSS_FRAMES),
            fill=rgba("mist"), font=font)
    y = 24
    for title, sheet, size, z, frames in rows:
        dr.text((8, y + 2), title, fill=rgba("gold"), font=font)
        y += 20
        for r, d in enumerate(gc.DIRS):
            cell = size * z
            dr.rectangle([label_w, y, width, y + cell - 1], fill=rgba("coal"))
            dr.text((8, y + cell // 2 - 8), d, fill=rgba("white"), font=font)
            for k, f in enumerate(frames):
                c = gc.COLS.index(f)
                fr = sheet.crop((c * size, r * size, c * size + size, r * size + size)).resize((cell, cell), Image.NEAREST)
                out.alpha_composite(fr, (label_w + k * cell, y))
                feet = y + (size - 4) * z
                dr.line([(label_w + k * cell, feet), (label_w + k * cell + 3, feet)], fill=rgba("scarlet"))
            y += cell
    dr.text((8, y + 2), "Escala (×2, pies alineados): Capataz, Guardián de la cripta, Árbol Podrido, retoño y Esqueleto de raíces (de la talla de un héroe)", fill=rgba("gold"), font=font)
    y += 20
    dr.rectangle([0, y, width, y + 139], fill=rgba("coal"))
    feet = y + 128
    lineup = [Image.open(ROOT / "client/assets/sprites/monsters/foreman.png").convert("RGBA").crop((0, 0, 64, 64)).resize((128, 128), Image.NEAREST),
              Image.open(ROOT / "client/assets/sprites/monsters/crypt_guardian.png").convert("RGBA").crop((0, 0, 32, 32)).resize((64, 64), Image.NEAREST),
              tree.crop((0, 0, TREE, TREE)).resize((128, 128), Image.NEAREST), sap.crop((0, 0, 32, 32)).resize((64, 64), Image.NEAREST),
              Image.fromarray(gc.sheet(*MONSTERS["monsters/root_skeleton"]), "RGBA").crop((0, 0, 32, 32)).resize((64, 64), Image.NEAREST)]
    feet_in = [120, 56, 120, 56, 56]  # fila de los pies de cada recorte ya ampliado
    x = label_w
    for im, fy in zip(lineup, feet_in):
        out.alpha_composite(im, (x, feet - fy))
        x += im.width + 16
    dest = Path(ROOT) / path
    dest.parent.mkdir(parents=True, exist_ok=True)
    save_indexed(out, dest)
    return str(dest)


if __name__ == "__main__":
    print("monstruos del Tier 2:", ", ".join(build()))
