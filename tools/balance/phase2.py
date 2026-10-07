"""Hechizos de nivel 7 y 9 (HU-106): pentagrama al nivel 10, regla 40/75, rangos, conos y línea, y solitario. Sin azar.

Uso: python tools/balance/phase2.py              (todo)
     python tools/balance/phase2.py --rank 0.2   (otro spellRankBonusPct en memoria, para la sensibilidad; no toca rules.json)

Supuestos (docs/design/balance-report.md §HU-106):
- Nivel 10 (tope de la Fase 2) con el equipo aproximado de tier2.py (verde del Tier 1 × gear_factor, ×1,8 al 10) y el segundo
  rango: +30 % del `base` de los efectos y auras numéricos, sin escalar los coeficientes (ADR-027 D3), como el servidor.
- Valores de referencia del nivel 10 con la misma definición que los fijos del nivel 6 (rules.balanceTargets.pentagram):
  mono = básico del Pícaro / 0,30; área = 4 × 0,6 × mono; control 40 s por minuto; movilidad 120 casillas por minuto;
  armadura = aguante del Guerrero / 0,70. Con los fijos del nivel 6, el equipo del 10 sube todas las puntas a la vez.
- Objetivo de referencia de nivel 10 con la misma mitigación que el del nivel 6 (armadura 35 · 300 / 220 ≈ 48).
- Valor de la clase en una punta: el del pentagrama completo (rules.balanceTargets.pentagram.classes). Objetivo de la Fase 2:
  base + los 4 mejores aportes objetivo de class-kits.md entre los hechizos hasta el nivel 10.
- Solitario: el de tier2.py con varios equipos de 4 por clase (FARM_KITS); cada clase farmea con el mejor. "Suavizado" = media
  con la vida del monstruo ×0,9–1,1, porque sin azar el tiempo para matar va a saltos.
"""
import sys, os, math, random, itertools, statistics
sys.path.insert(0, os.path.dirname(__file__))
import model
from model import *
import tier2
from tier2 import char, fmt, NAMES, CLASSES, MONS, NORMAL_BY_LEVEL

if '--rank' in sys.argv:
    R['progression']['spellRankBonusPct'] = float(sys.argv[sys.argv.index('--rank') + 1])
PROG = R['progression']
PG = R['balanceTargets']['pentagram']
CAP = PROG['levelCapByPhase'][1]                       # tope de nivel de la Fase 2
BUDGET = PG['budget'] * PG['loadoutMaxPct']            # 187,5
AX = ['single', 'aoe', 'cc', 'mobility', 'armor']
AX_ES = {'single': 'Mono', 'aoe': 'Área', 'cc': 'Control', 'mobility': 'Movilidad', 'armor': 'Armadura'}
TGT_BASE = {'rogue': {'single': 30, 'armor': 30}, 'mage': {'single': 15, 'armor': 25},
            'warrior': {'single': 15, 'armor': 70}, 'priest': {'armor': 25}}
TGT = {  # aportes objetivo de docs/design/class-kits.md
    'rogue_sinister_strike': {'single': 20}, 'rogue_gouge': {'cc': 15, 'single': 5},
    'rogue_shadowstep': {'mobility': 25, 'single': 5, 'cc': 5}, 'rogue_sprint': {'mobility': 25},
    'rogue_eviscerate': {'single': 20}, 'rogue_throwing_blades': {'aoe': 10, 'mobility': 15},
    'mage_fireball': {'single': 20}, 'mage_frostbolt': {'cc': 15, 'single': 5}, 'mage_frost_nova': {'cc': 30, 'aoe': 5},
    'mage_flame_burst': {'aoe': 30, 'single': 5}, 'mage_burning_field': {'aoe': 20}, 'mage_blink': {'mobility': 20},
    'warrior_heroic_strike': {'single': 15}, 'warrior_taunt': {'cc': 15}, 'warrior_charge': {'mobility': 15, 'cc': 15},
    'warrior_whirlwind': {'aoe': 20, 'single': 5}, 'warrior_shield_block': {'armor': 20}, 'warrior_cleave': {'aoe': 15, 'single': 5},
    'priest_heal': {'single': 30}, 'priest_smite': {}, 'priest_power_shield': {'single': 20, 'mobility': 10, 'armor': 5},
    'priest_holy_pulse': {'aoe': 30, 'cc': 10}, 'priest_renew': {'single': 20},
    'priest_path_of_light': {'aoe': 15, 'mobility': 15, 'cc': 10}}
NEW = ['rogue_eviscerate', 'rogue_throwing_blades', 'mage_burning_field', 'mage_blink',
       'warrior_shield_block', 'warrior_cleave', 'priest_renew', 'priest_path_of_light']


def kit(cls, cap=CAP):
    return [s['id'] for s in sorted(tier2.SP0.values(), key=lambda s: s.get('levelReq', 0))
            if s['source'] == 'class' and s.get('classId') == cls and s['levelReq'] <= cap]

def ref_target(level):
    """Objetivo de referencia con la misma mitigación que el del nivel 6 (model.TARGET: armadura 35)."""
    k = (CB['mitigationPerLevel'] * level + CB['mitigationConstant']) / (CB['mitigationPerLevel'] * 6 + CB['mitigationConstant'])
    return {'level': level, 'armor': round(35 * k)}


# ---------------------------------------------------------------- pentagrama
def pentagram(level):
    """Puntos de la base y aporte de cada hechizo (puntos(base + hechizo) − puntos(base)) al nivel `level`."""
    SP, AU = tier2.ranked(level)
    old = dict(model.TARGET); model.TARGET.update(ref_target(level)); model.AOE_N = 3
    rog, war = char('rogue', level), char('warrior', level)
    ref = {'single': basic_hit(rog) / rog.swing / 0.30}
    ref['aoe'] = 4 * 0.6 * ref['single']
    ref['cc'] = PG['references']['ccSecPerMin']; ref['mobility'] = PG['references']['mobilityTilesPerMin']
    ref['armor'] = armor_ttl(war, SP, AU, (), att_level=level) / 0.70
    res = {}
    for cls in CLASSES:
        ch = char(cls, level); heal = cls == 'priest'
        bm, _ = simulate(ch, [], SP, AU, heal_mode=heal)
        ttl = armor_ttl(ch, SP, AU, (), att_level=level)
        base = {'single': 100 * bm / 30 / ref['single'], 'aoe': 0.0, 'cc': 0.0, 'mobility': 0.0, 'armor': 100 * ttl / ref['armor']}
        spells = {}
        for sid in kit(cls, level):
            m, sc = simulate(ch, [sid], SP, AU, heal_mode=heal); s = SP[sid]
            raw = {'single': (m - bm) / 30, 'aoe': sc / 30, 'cc': cc_value2(ch, s, AU), 'mobility': mob_value(ch, s, AU),
                   'armor': armor_ttl(ch, SP, AU, (sid,), att_level=level) - ttl}
            spells[sid] = {a: 100 * raw[a] / ref[a] for a in AX}
        res[cls] = {'base': base, 'spells': spells}
    model.TARGET.clear(); model.TARGET.update(old)
    return ref, res

def best4(values):
    return sum(sorted(values, reverse=True)[:4])

def report_pentagram(level=CAP):
    ref, P = pentagram(level)
    print(f'\n## Pentagrama al nivel {level} (equipo ×{fmt(tier2.gear_factor(level), 1)}, rango +{fmt(100 * tier2.rank_mult(level) - 100)} % del base)')
    print('Referencias del nivel ' + str(level) + ': ' + ', '.join(f'{AX_ES[a]} {fmt(ref[a], 1)}' for a in AX)
          + ' (nivel 6, rules.json: ' + ', '.join(f"{k} {v}" for k, v in PG['references'].items() if k != 'level') + ')')
    ok = True
    for cls in CLASSES:
        base = P[cls]['base']; sp = P[cls]['spells']; cv = PG['classes'][cls]
        print(f'\n### {NAMES[cls]} (base: ' + ', '.join(f'{AX_ES[a]} {fmt(base[a])}' for a in AX if base[a]) + ')')
        print('| Hechizo | Nv | ' + ' | '.join(AX_ES[a] for a in AX) + ' | Suma | Objetivo |')
        print('|---|---|' + '---|' * len(AX) + '---|---|')
        for sid, v in sp.items():
            tot = sum(v.values()); flag = ' **> 40**' if tot > PG['abilityMaxPoints'] else ''
            ok &= tot <= PG['abilityMaxPoints']
            obj = ', '.join(f'{AX_ES[a]} {n}' for a, n in TGT[sid].items()) or '—'
            mark = ' (nuevo)' if sid in NEW else ''
            print(f"| {tier2.SP0[sid]['name']}{mark} | {tier2.SP0[sid]['levelReq']} | " + ' | '.join(fmt(v[a]) for a in AX)
                  + f' | {fmt(tot)}{flag} | {obj} |')
        val = {a: base[a] + best4([v[a] for v in sp.values()]) for a in AX}
        goal = {a: TGT_BASE[cls].get(a, 0) + best4([TGT[s].get(a, 0) for s in sp]) for a in AX}
        print('| **Valor de la clase** (base + 4 mejores) | | ' + ' | '.join(f'**{fmt(val[a])}**' for a in AX) + f' | {fmt(sum(val.values()))} | |')
        print('| Objetivo de la Fase 2 | | ' + ' | '.join(fmt(goal[a]) for a in AX) + f' | {fmt(sum(goal.values()))} | |')
        print('| Pentagrama completo (nivel 15) | | ' + ' | '.join(str(cv[a]) for a in AX) + f' | {sum(cv[a] for a in AX)} | |')
        # Regla 40/75 en las combinaciones de 4
        rows = []
        for combo in itertools.combinations(sp, 4):
            tot = {a: base[a] + sum(sp[s][a] for s in combo) for a in AX}
            over = [a for a in AX if tot[a] > cv[a]]
            rows.append((sum(tot.values()), combo, tot, over))
        rows.sort(reverse=True)
        bad = [r for r in rows if r[0] > BUDGET or r[3]]
        ok &= not bad
        top = rows[0]
        print(f'\nCombinaciones de 4 ({len(rows)}): la más cargada suma {fmt(top[0])} de {fmt(BUDGET)} ('
              + ' + '.join(tier2.SP0[s]['name'] for s in top[1]) + '); la menos, ' + fmt(rows[-1][0]) + '. '
              + ('Todas cumplen la regla 40/75 y ninguna supera el valor de la clase en una punta.' if not bad else
                 f'**{len(bad)} incumplen**: ' + '; '.join(' + '.join(tier2.SP0[s]['name'] for s in r[1]) + f' ({fmt(r[0])}'
                                                          + (', ' + ', '.join(f'{AX_ES[a]} {fmt(r[2][a])} > {cv[a]}' for a in r[3]) if r[3] else '') + ')' for r in bad)))
        print('Puntas máximas de una combinación: ' + ', '.join(f'{AX_ES[a]} {fmt(max(r[2][a] for r in rows))} (de {cv[a]})' for a in AX))
    print('\nRegla 40/75 al nivel ' + str(level) + ': ' + ('se cumple en todas las clases.' if ok else '**NO se cumple** (ver arriba).'))
    return ref, P


# ---------------------------------------------------------------- rangos (ADR-027 D3)
def per_cast(ch, s, AU, mult):
    """Valor esperado de un lanzamiento sobre un objetivo (daño, cura, todo el DoT/HoT o el escudo) con el `base` × mult."""
    v = 0.0
    for e in s['effects']:
        if e['type'] in ('damage', 'heal') and 'base' in e:
            v += eff_amount(ch, s, dict(e, base=e['base'] * mult))
        if e['type'] == 'apply_aura':
            a = AU[e['auraId']]
            if a['kind'] in ('dot', 'hot', 'shield') and 'base' in a:
                per, tick, dur = aura_total(ch, dict(a, base=a['base'] * mult))
                v += per * (round(dur / tick) if tick else 1)
    return v

def real_gear(cls, level):
    """El equipo verde de items.json de ese nivel con las mismas casillas y tipos que model.REF_GEAR (HU-110). None si falta alguno."""
    out = []
    for g in model.REF_GEAR[cls]:
        it = ITEMS[g]; typ = it.get('weaponType') or it.get('armorType'); keys = set(it.get('stats') or {})
        cands = [i for i in ITEMS.values() if i.get('levelReq') == level and i.get('rarity') == 'uncommon' and i.get('slot') == it['slot']
                 and (i.get('weaponType') or i.get('armorType')) == typ]
        if not cands: return None
        out.append(max(cands, key=lambda i: len(keys & set(i.get('stats') or {})))['id'])
    return out

def rank_steps(pct):
    """Por hechizo con números, lo que sube un lanzamiento: rango 0 → 1 con el equipo del 4 (lo que se notó en la Fase 1; solo los
    hechizos de nivel ≤ 5), rango 1 → 2 con el equipo del 8 (al subir) y del 9, y lo que sube el mismo hechizo (rango 2) solo por
    pasar del equipo del 8 al del 9."""
    old = dict(model.TARGET); rows = []
    for cls in CLASSES:
        c4, c8, c9 = char(cls, 4), char(cls, 8), char(cls, 9)
        g9 = real_gear(cls, 9); c9r = Char(cls, 9, g9) if g9 else None
        for sid in kit(cls):
            s = tier2.SP0[sid]
            model.TARGET.update(ref_target(4)); v4 = [per_cast(c4, s, tier2.AU0, 1 + r * pct) for r in (0, 1)]
            model.TARGET.update(ref_target(8)); v8 = [per_cast(c8, s, tier2.AU0, 1 + r * pct) for r in (0, 1, 2)]
            model.TARGET.update(ref_target(9)); v9 = [per_cast(c9, s, tier2.AU0, 1 + r * pct) for r in (0, 1, 2)]
            vr = [per_cast(c9r, s, tier2.AU0, 1 + r * pct) for r in (1, 2)] if c9r else None
            if v8[1] <= 0: continue
            s4 = v4[1] / v4[0] - 1 if s['levelReq'] <= 5 else None
            rows.append({'cls': cls, 'sid': sid, 's4': s4, 's8': v8[2] / v8[1] - 1, 's9': v9[2] / v9[1] - 1,
                         's9r': vr[1] / vr[0] - 1 if vr else None, 'gear': v9[2] / v8[2] - 1, 'v9': v9[2]})
    model.TARGET.clear(); model.TARGET.update(old)
    return rows

def report_ranks():
    pct = PROG['spellRankBonusPct']
    noticeable = CB['varianceMax'] / CB['varianceMin'] - 1     # la tirada más baja del rango nuevo iguala la más alta del anterior
    rows = rank_steps(pct)
    print(f'\n## Rangos (ADR-027 D3): +{fmt(100 * pct)} % del base por rango, lineal; ¿se nota con el equipo del nivel 9?')
    print(f'Referencias: lo que se notó el primer rango en la Fase 1 (rango 0 → 1 con el equipo del 4), lo que sube el equipo de un nivel'
          f' (del 8 al 9) y la variación de una tirada ({fmt(100 * noticeable, 1)} % = varianceMax / varianceMin − 1: la tirada más baja'
          ' del rango nuevo iguala la más alta del anterior).\n')
    print('| Clase | Hechizo | Por lanzamiento (nv 9, rango 2) | Rango 0 → 1 (equipo del 4) | Rango 1 → 2 (equipo del 8) | Rango 1 → 2 (equipo del 9) | Ídem con el verde de nivel 9 de items.json | Equipo del 8 → 9 |')
    print('|---|---|---|---|---|---|---|---|')
    pc = lambda x: '—' if x is None else f"{fmt(100 * x, 1)} %"
    for r in rows:
        print(f"| {NAMES[r['cls']]} | {tier2.SP0[r['sid']]['name']} | {fmt(r['v9'])} | {pc(r['s4'])} | {pc(r['s8'])} | {pc(r['s9'])} | {pc(r['s9r'])} | {pc(r['gear'])} |")
    med = lambda k, rr: statistics.median(r[k] for r in rr if r[k] is not None)
    print(f"\nMedianas: primer rango en la Fase 1 {fmt(100 * med('s4', rows), 1)} %; segundo rango {fmt(100 * med('s8', rows), 1)} % con el equipo del 8"
          f" y {fmt(100 * med('s9', rows), 1)} % con el del 9 ({fmt(100 * med('s9r', rows), 1)} % con el verde de nivel 9 de items.json); un nivel de equipo (8 → 9) {fmt(100 * med('gear', rows), 1)} %."
          f" Supera la variación de una tirada en {sum(1 for r in rows if r['s9'] >= noticeable)} de {len(rows)} hechizos.")
    print('\nSensibilidad (rules.json no cambia):\n')
    print('| spellRankBonusPct | Rango 0 → 1 al 4 (mediana) | Rango 1 → 2 al 9 (mediana) | Hechizos que superan la variación al 9 | Rango 3 (nivel 12) | Hechizo que más suma (niveles 7–10) |')
    print('|---|---|---|---|---|---|')
    for p in (0.15, 0.2, 0.25, 0.3, 0.35, 0.4):
        rr = rank_steps(p)
        PROG['spellRankBonusPct'] = p                      # solo en memoria, para medir la regla 40/75 con ese rango
        top = max(((sum(v.values()), sid) for lv in range(7, CAP + 1) for P in [pentagram(lv)[1]] for c in CLASSES
                   for sid, v in P[c]['spells'].items()))
        PROG['spellRankBonusPct'] = pct
        flag = ' **> 40**' if top[0] > PG['abilityMaxPoints'] else ''
        print(f"| {fmt(p, 2)} | {fmt(100 * med('s4', rr), 1)} % | {fmt(100 * med('s9', rr), 1)} % | {sum(1 for r in rr if r['s9'] >= noticeable)} de {len(rr)}"
              f" | +{fmt(300 * p)} % | {tier2.SP0[top[1]]['name']} {fmt(top[0], 1)}{flag} |")


def report_levels():
    """Regla 40/75 en cada nivel de la Fase 2 con los hechizos ya aprendidos (los rangos y el equipo de ese nivel)."""
    print('\n## Regla 40/75 del nivel 7 al 10 (hechizos aprendidos a ese nivel)\n')
    print('| Nv | ' + ' | '.join(f'{NAMES[c]}: hechizo máx. · combinación máx. · puntas por encima' for c in CLASSES) + ' |')
    print('|---|' + '---|' * len(CLASSES))
    ok = True; base_over = []
    for lv in range(7, CAP + 1):
        _, P = pentagram(lv); cells = []
        for cls in CLASSES:
            base = P[cls]['base']; sp = P[cls]['spells']; cv = PG['classes'][cls]
            top1 = max(sum(v.values()) for v in sp.values())
            combos = [{a: base[a] + sum(sp[s][a] for s in c) for a in AX} for c in itertools.combinations(sp, min(4, len(sp)))]
            top4 = max(sum(t.values()) for t in combos)
            # si ya la base (sin hechizos) pasa del valor de la clase, no es cosa de los hechizos: se anota aparte
            over = sorted({AX_ES[a] for t in combos for a in AX if t[a] > cv[a] and t[a] > base[a] + 0.5})
            base_over += [f'{NAMES[cls]}, {AX_ES[a].lower()} {fmt(base[a], 1)} de {cv[a]} al nivel {lv}' for a in AX if base[a] > cv[a]]
            ok &= top1 <= PG['abilityMaxPoints'] and top4 <= BUDGET and not over
            cells.append(f"{fmt(top1)} · {fmt(top4)} · {', '.join(over) or 'ninguna'}")
        print(f'| {lv} | ' + ' | '.join(cells) + ' |')
    print('\n' + ('Se cumple en todos los niveles.' if ok else '**No se cumple en algún nivel.**')
          + (' La base sola ya pasa del valor de la clase en: ' + '; '.join(base_over) + '.' if base_over else ''))


# ---------------------------------------------------------------- formas (ADR-027 D4)
BODY = (CB['bodyHalfWidthTiles'], CB['bodyHeightAboveFeetTiles'], CB['bodyDepthBelowFeetTiles'])

def _box_points(px, py, n=5):
    hw, above, below = BODY
    return [(px - hw + 2 * hw * i / (n - 1), py - above + (above + below) * j / (n - 1)) for i in range(n) for j in range(n)]

def _touches(shape, px, py, d):
    """¿El cuadro del cuerpo con los pies en (px, py) toca el área? Lanzador en (−d, 0) apuntando a +x; objetivo principal en (0, 0)."""
    kind = shape['kind']
    for x, y in _box_points(px, py):
        if kind == 'cone':
            dx, dy = x + d, y; r = math.hypot(dx, dy)
            if r <= shape['r'] and (r == 0 or abs(math.degrees(math.atan2(dy, dx))) <= shape['angle'] / 2): return True
        elif kind == 'line':
            if 0 <= x + d <= shape['length'] and abs(y) <= shape['width'] / 2: return True
        elif kind == 'self':
            if math.hypot(x + d, y) <= shape['r']: return True
        elif math.hypot(x, y) <= shape['r']: return True        # círculo en el objetivo principal
    return False

def coverage(shape, d, n=4000, seed=7):
    """Fracción esperada de los 3 secundarios (uniformes en un círculo de radio 2 alrededor del principal) que el área toca."""
    rnd = random.Random(seed); hit = 0
    for _ in range(n):
        rr = 2 * math.sqrt(rnd.random()); th = rnd.random() * 2 * math.pi
        hit += _touches(shape, rr * math.cos(th), rr * math.sin(th), d)
    return hit / n

def shape_of(s):
    sh = s.get('shape', 'circle')
    if sh == 'cone': return {'kind': 'cone', 'r': s['aoeRadius'], 'angle': s['aoeAngleDeg']}
    if sh == 'line': return {'kind': 'line', 'length': s['aoeLength'], 'width': s['aoeWidth']}
    return {'kind': 'self' if s['targeting'].startswith('self_aoe') else 'ground', 'r': s['aoeRadius']}

def report_shapes():
    print('\n## Formas (ADR-027 D4): secundarios que toca cada área con el grupo de referencia (3 en un radio de 2 alrededor del principal)')
    print('El pentagrama los cuenta todos (decisión de medición 1); esta tabla dice cuántos toca de verdad cada forma.\n')
    print('| Hechizo | Forma | Casteo | Lanzador a | Secundarios tocados |')
    print('|---|---|---|---|---|')
    melee = {'rogue': 1.25, 'warrior': 1.5}
    cases = []
    for sid in ['warrior_whirlwind', 'warrior_cleave', 'rogue_throwing_blades', 'mage_flame_burst', 'mage_burning_field',
                'mage_frost_nova', 'priest_holy_pulse', 'priest_path_of_light']:
        s = tier2.SP0[sid]; d = 1.5 if s['targeting'].startswith('self_aoe') else melee.get(s['classId'], 4.0)   # a su alrededor: el monstruo se le pega
        cases.append((s['name'], shape_of(s), s['castMs'], d, s))
    for r in (3, 4):  # Cuchillas: las dos salidas de D4
        for ang in (50, 60):
            cases.append((f'Cuchillas (radio {r}, {ang}°)', {'kind': 'cone', 'r': r, 'angle': ang}, None, 1.25, None))
            cases.append((f'Cuchillas (radio {r}, {ang}°) a 2 casillas', {'kind': 'cone', 'r': r, 'angle': ang}, None, 2.0, None))
    for name, sh, cast, d, s in cases:
        desc = {'cone': lambda: f"cono {fmt(sh['r'], 1)} · {sh['angle']}°", 'line': lambda: f"línea {fmt(sh['length'])} × {fmt(sh['width'], 1)}",
                'self': lambda: f"alrededor, radio {fmt(sh['r'], 1)}", 'ground': lambda: f"suelo, radio {fmt(sh['r'], 1)}"}[sh['kind']]()
        c = coverage(sh, d)
        print(f"| {name} | {desc} | {'—' if cast is None else fmt(cast / 1000, 1) + ' s'} | {fmt(d, 2)} | {fmt(3 * c, 1)} de 3 ({fmt(100 * c)} %) |")


# ---------------------------------------------------------------- solitario con los hechizos nuevos
FARM_KITS = {  # equipos para farmear (por prioridad); cada clase farmea con el que le da más XP por hora. 'Fase 1' = tier2.SOLO_KIT
    'rogue': {'Fase 1': ['rogue_shadowstep', 'rogue_sinister_strike', 'rogue_gouge'],
              'Eviscerar': ['rogue_shadowstep', 'rogue_eviscerate', 'rogue_sinister_strike', 'rogue_gouge'],
              'Eviscerar + Cuchillas': ['rogue_shadowstep', 'rogue_eviscerate', 'rogue_sinister_strike', 'rogue_throwing_blades']},
    'mage': {'Fase 1': ['mage_flame_burst', 'mage_frostbolt', 'mage_fireball'],
             'Campo ardiente': ['mage_flame_burst', 'mage_frostbolt', 'mage_burning_field', 'mage_fireball']},
    'warrior': {'Fase 1': ['warrior_charge', 'warrior_whirlwind', 'warrior_heroic_strike'],
                'Bloqueo': ['warrior_charge', 'warrior_whirlwind', 'warrior_heroic_strike', 'warrior_shield_block'],
                'Tajo': ['warrior_charge', 'warrior_whirlwind', 'warrior_cleave', 'warrior_heroic_strike'],
                'Tajo + Bloqueo': ['warrior_charge', 'warrior_whirlwind', 'warrior_cleave', 'warrior_shield_block']},
    'priest': {'Fase 1': ['priest_holy_pulse', 'priest_smite'],
               'Renovar': ['priest_holy_pulse', 'priest_smite', 'priest_heal', 'priest_renew']}}

def _result(t, rest, mon, level):
    cyc = t + rest + tier2.WALK_SEC
    return {'t': t, 'rest': rest, 'cycle': cyc, 'xph': 3600 / cyc * tier2.xp_of(mon) * tier2.level_mod(mon['level'], level)}

def farm(cls, level, mon, kit):
    """tier2.solo con otro equipo de 4. Lo que no pega entra aparte: Bloqueo con escudo baja el daño recibido en su fracción de
    tiempo activo (menos descanso); Renovar, ver solo_priest_renew. None si el equipo usa un hechizo que aún no se aprende."""
    SP, AU = tier2.ranked(level)
    if any(SP[s]['levelReq'] > level for s in kit): return None
    old = list(tier2.SOLO_KIT[cls]); tier2.SOLO_KIT[cls] = kit; tier2._CURVES.clear()
    try:
        if 'priest_renew' in kit: return solo_priest_renew(level, mon)
        if 'warrior_shield_block' not in kit: return tier2.solo(cls, level, mon)
        s = SP['warrior_shield_block']; a = AU[s['effects'][0]['auraId']]
        red = -a['mods']['damageTakenPct'] * min(1.0, a['durationMs'] / s['cooldownMs'])
        r = tier2.fight([cls], level, mon); m = r['members'][0]; ch = m.ch
        lost = (ch.maxHp - m.hp + m.lost_after) * (1 - red)
        return _result(r['t'], (CB['hpRegenDelaySec'] + lost / ch.hpRegen) if lost > 0 else 0, mon, level)
    finally:
        tier2.SOLO_KIT[cls] = old; tier2._CURVES.clear()

def solo_priest_renew(level, mon):
    """tier2.solo del Sacerdote con Renovar sobre sí mismo al empezar y cada vez que caduca: cada uno le cuesta un GCD sin pegar;
    cura mientras pelea y, al descansar, sigue curando junto con Sanar. Su maná se suma al de la pelea y al de Sanar."""
    SP, AU = tier2.ranked(level)
    renew, heal = SP['priest_renew'], SP['priest_heal']; a = AU[renew['effects'][0]['auraId']]
    r = tier2.fight(['priest'], level, mon); m = r['members'][0]; ch = m.ch
    dur, tick, gcd = a['durationMs'] / 1000, a['tickMs'] / 1000, CB['gcdMs'] / 1000
    t = r['t'] + gcd * math.ceil(r['t'] / dur)
    n = math.ceil(t / dur)
    per = a['base'] + a.get('spCoef', 0) * ch.sp
    lost = ch.maxHp - m.hp + m.lost_after
    healed = min(lost, per * math.floor((t - gcd) / tick))
    left = max(0.0, n * dur - t)                                   # lo que le queda al último Renovar
    h = eff_amount(ch, heal, heal['effects'][0]); cast = heal['castMs'] / 1000
    rem = lost - healed
    both = h / cast + per / tick
    time = rem / both if rem / both <= left else left + (rem - left * per / tick) / (h / cast)
    casts = math.ceil(time / cast) if rem > 0 else 0
    mana = tier2.mana_used_check(ch, tier2.loadout('priest', level, 'rot', SP), t, SP) + n * renew['cost']['amount'] + casts * heal['cost']['amount']
    return _result(t, max(time, mana / ch.manaRegen), mon, level)

HP_SPREAD = (0.9, 0.95, 1.0, 1.05, 1.1)

def farm_smooth(cls, level, mon, kit):
    """farm() de media sobre la vida del monstruo ×0,9–1,1: el modelo no tiene azar y el tiempo para matar va a saltos (el golpe que
    remata cae o no antes del siguiente hechizo grande); la variación de daño y los críticos los suavizan en partida."""
    rs = [farm(cls, level, dict(mon, hp=mon['hp'] * f), kit) for f in HP_SPREAD]
    if rs[0] is None: return None
    return {k: sum(r[k] for r in rs) / len(rs) for k in ('t', 'rest', 'cycle', 'xph')}

def solo_table(levels=range(7, CAP + 1), smooth=False):
    """{nivel: {clase: {equipo: resultado}}} contra el normal de cada nivel."""
    f = farm_smooth if smooth else farm
    return {lv: {c: {k: f(c, lv, MONS[NORMAL_BY_LEVEL[lv]], kit) for k, kit in FARM_KITS[c].items()} for c in CLASSES} for lv in levels}

def best_of(d):
    return {c: max((r for r in d[c].values() if r), key=lambda r: r['xph']) for c in CLASSES}

def report_solo():
    print('\n## Solitario con los hechizos nuevos (normal de cada nivel; ciclo = pelea + descanso + 10 s; tier2.py)')
    print('Equipos para farmear: ' + ' · '.join(f"{NAMES[c]}: " + '; '.join(f"{k} ({', '.join(tier2.SP0[s]['name'] for s in kit)})"
          for k, kit in FARM_KITS[c].items()) for c in CLASSES) + '. Los hechizos que no pegan no entran en la rotación de daño.'
          ' Celdas: mata en · ciclo · XP por hora (como tier2.py, sin suavizar); en negrita, el mejor.')
    worst = {}; summary = {}
    for smooth in (False, True):
        T = solo_table(smooth=smooth); rows = []
        for lv, d in T.items():
            mon = MONS[NORMAL_BY_LEVEL[lv]]; best = best_of(d)
            if not smooth:
                print(f"\n**{mon['name']} (nv {lv}, {mon['hp']} de vida)**\n")
                for c in CLASSES:
                    cells = [(f"**{k}: {fmt(r['t'])} s · {fmt(r['cycle'])} s · {fmt(r['xph'])}**" if r is best[c] else
                              f"{k}: {fmt(r['t'])} s · {fmt(r['cycle'])} s · {fmt(r['xph'])}") for k, r in d[c].items() if r]
                    print(f'- {NAMES[c]}: ' + ' | '.join(cells))
            xs = {c: best[c]['xph'] for c in CLASSES}; f1 = {c: d[c]['Fase 1']['xph'] for c in CLASSES}
            rows.append((lv, mon, xs, 100 * (1 - min(xs.values()) / max(xs.values())), 100 * (1 - min(f1.values()) / max(f1.values()))))
        summary[smooth] = rows; worst[smooth] = max(r[3] for r in rows)
    print('\nXP por hora con el mejor equipo de cada clase. "Suavizado": media con la vida del monstruo ×0,9–1,1 (sin azar, el tiempo'
          ' para matar va a saltos).\n')
    print('| Monstruo | Nv | Cálculo | ' + ' | '.join(NAMES[c] for c in CLASSES) + ' | Dif. XP/h | Dif. con los de la Fase 1 |')
    print('|---|---|---|' + '---|' * len(CLASSES) + '---|---|')
    for i in range(len(summary[False])):
        for smooth in (False, True):
            lv, mon, xs, spread, sp1 = summary[smooth][i]
            print(f"| {mon['name']} | {lv} | {'suavizado' if smooth else 'tier2.py'} | " + ' | '.join(fmt(xs[c]) for c in CLASSES)
                  + f" | {fmt(spread)} % | {fmt(sp1)} % |")
    print(f"\nMayor diferencia de XP por hora: {fmt(worst[False])} % (tier2.py) y {fmt(worst[True])} % (suavizado); objetivo ≤ "
          f"{fmt(100 * R['balanceTargets']['soloXpPerHourSpreadPct'])} %.")
    return summary

def report_eviscerate(summary):
    """Lo que Eviscerar (el único hechizo nuevo que acelera mucho el farmeo) hace con el pentagrama y con la XP por hora."""
    print('\n## Eviscerar: aporte a mono frente a XP por hora (suavizado; las demás clases con su mejor equipo)\n')
    print('| Recarga · daño | Golpe al 7 | Mono al 10 | ' + ' | '.join(f'Dif. XP/h nv {lv}' for lv in range(7, CAP + 1)) + ' |')
    print('|---|---|---|' + '---|' * (CAP - 6))
    orig = dict(tier2.SP0['rogue_eviscerate']); others = {r[0]: r[2] for r in summary[True]}
    for cd, b, ap in ((orig['cooldownMs'], orig['effects'][0]['base'], orig['effects'][0]['apCoef']),
                      (14000, 6, 0.3), (14000, 10, 0.5), (14000, 12, 0.6), (14000, 14, 0.7), (20000, 10.5, 0.42)):
        tier2.SP0['rogue_eviscerate'] = dict(orig, cooldownMs=cd, effects=[{'type': 'damage', 'base': b, 'apCoef': ap}])
        _, P = pentagram(CAP)
        S, A = tier2.ranked(7); old = dict(model.TARGET); model.TARGET.update(ref_target(7))
        hit = eff_amount(char('rogue', 7), S['rogue_eviscerate'], S['rogue_eviscerate']['effects'][0])
        model.TARGET.clear(); model.TARGET.update(old)
        cells = []
        for lv in range(7, CAP + 1):
            mon = MONS[NORMAL_BY_LEVEL[lv]]
            rog = max((r for r in (farm_smooth('rogue', lv, mon, k) for k in FARM_KITS['rogue'].values()) if r), key=lambda r: r['xph'])
            xs = dict(others[lv], rogue=rog['xph'])
            cells.append(f"{fmt(100 * (1 - min(xs.values()) / max(xs.values())))} %")
        tag = ' (elegido)' if (cd, b, ap) == (orig['cooldownMs'], orig['effects'][0]['base'], orig['effects'][0]['apCoef']) else ''
        print(f"| {fmt(cd / 1000)} s · {fmt(b, 1)} + {fmt(ap, 2)} AP{tag} | {fmt(hit)} | {fmt(P['rogue']['spells']['rogue_eviscerate']['single'])} | " + ' | '.join(cells) + ' |')
    tier2.SP0['rogue_eviscerate'] = orig

if __name__ == '__main__':
    print(f"# HU-106: hechizos de nivel 7 y 9 (spellRankBonusPct {PROG['spellRankBonusPct']})")
    report_pentagram(CAP)
    report_levels()
    report_ranks()
    report_shapes()
    report_eviscerate(report_solo())
