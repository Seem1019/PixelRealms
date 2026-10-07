"""Equipo, botín y vendedor de los niveles 6 a 10 (HU-110): tier2.py con el equipo real y economía del Bosque. Sin azar.

Uso: python tools/balance/gear.py                    (todo)
     python tools/balance/gear.py --quick            (sin los élites)
     python tools/balance/gear.py --elite-dmg 1.3    (daño del básico de los élites ×1,3 en memoria; no toca monsters.json)
     python tools/balance/gear.py --regen-delay 5    (otro rules.combat.hpRegenDelaySec en memoria; no toca rules.json)

Supuestos (docs/design/balance-report.md §HU-110):
- Equipo real en lugar de gear_factor, con todas las casillas de afinidad alta de la clase (el Guerrero, placas y escudo):
  «esperado» = arma, pecho, piernas y cuello en verde y el resto en blanco del tramo; «completo» = todo en verde (cota superior);
  «recién llegado» = el esperado del tramo anterior (Tier 1 al 7; nivel 7 al 8 y al 9; nivel 9 al 10). El equipo de nivel 7 se
  lleva a los niveles 7 y 8 y el de nivel 9 del 9 al 11. «casillas» = solo las casillas de tier2.REF_GEAR en verde (la
  comparación directa con gear_factor).
- Poción: la de vida mayor si existe en content/ (item_greater_heal); si no, la propuesta de HU-110 (GREATER_POTION).
- Economía: monstruo normal de cada nivel (tier2.NORMAL_BY_LEVEL), ciclo medido con el equipo esperado, horas por nivel con
  rules.progression.minutesPerLevel escaladas por ciclo / killCycleSecTarget, como tier2.py. Ingresos: cobre + chatarra y
  blancos vendidos (los verdes no se cuentan: se usan o se intercambian). Precios de compra = sellPrice × vendorBuyMultiplier.
"""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
import model
from model import ITEMS, R, Lj, Char
import tier2
from tier2 import fmt, NAMES, CLASSES, MONS, PROG, NORMAL_BY_LEVEL, ZONES

ECON = R['economy']
LOOT = {t['id']: t for t in Lj('loot_tables.json')['lootTables']}
VENDORS = {v['id']: v for v in Lj('vendors.json')['vendors']}
GREATER_POTION = {'id': 'greater_healing_potion', 'heal': 100, 'cdSec': 60, 'sellPrice': 15}  # propuesta si aún no está en content/
FOOD = {'id': 'smoked_venison', 'sellPrice': 3}
if '--elite-dmg' in sys.argv:
    _f = float(sys.argv[sys.argv.index('--elite-dmg') + 1])
    for _m in MONS.values():
        if _m['type'] == 'elite': _m['damageMin'] = round(_m['damageMin'] * _f); _m['damageMax'] = round(_m['damageMax'] * _f)
if '--regen-delay' in sys.argv:
    R['combat']['hpRegenDelaySec'] = float(sys.argv[sys.argv.index('--regen-delay') + 1])

REF7 = {'rogue': ['thorn_dagger', 'tanned_leather_jerkin', 'ranger_boots', 'ranger_gloves', 'hunter_necklace'],
        'warrior': ['sentinel_sword', 'oak_round_shield', 'steel_breastplate', 'sentinel_helm', 'sentinel_legplates', 'hunter_necklace'],
        'mage': ['druid_staff', 'linen_robe', 'druid_hood'],
        'priest': ['hazel_wand', 'linen_robe', 'druid_hood', 'warden_leggings']}
REF9 = {'rogue': ['marsh_fang_dagger', 'hardened_leather_jerkin', 'lizardhide_boots', 'lizardhide_gloves', 'scale_necklace'],
        'warrior': ['drowned_knight_sword', 'tempered_steel_shield', 'tempered_steel_breastplate', 'drowned_knight_helm',
                    'drowned_knight_legplates', 'scale_necklace'],
        'mage': ['mangrove_staff', 'wool_robe', 'seer_hood'],
        'priest': ['wisp_wand', 'wool_robe', 'seer_hood', 'scale_leggings']}
FULL7 = {'rogue': ['thorn_dagger', 'ranger_cap', 'ranger_jerkin', 'ranger_gloves', 'ranger_leggings', 'ranger_boots', 'hunter_necklace', 'hunter_ring'],
         'warrior': ['sentinel_sword', 'sentinel_shield', 'sentinel_helm', 'sentinel_breastplate', 'sentinel_gauntlets', 'sentinel_legplates',
                     'sentinel_sabatons', 'hunter_necklace', 'hunter_ring'],
         'mage': ['druid_staff', 'druid_hood', 'druid_robe', 'druid_gloves', 'druid_leggings', 'druid_sandals', 'acorn_amulet', 'ivy_ring'],
         'priest': ['hazel_wand', 'druid_hood', 'druid_robe', 'druid_gloves', 'warden_leggings', 'druid_sandals', 'acorn_amulet', 'ivy_ring']}
FULL9 = {'rogue': ['marsh_fang_dagger', 'lizardhide_cap', 'lizardhide_jerkin', 'lizardhide_gloves', 'lizardhide_leggings', 'lizardhide_boots',
                   'scale_necklace', 'sunken_signet'],
         'warrior': ['drowned_knight_sword', 'drowned_knight_shield', 'drowned_knight_helm', 'drowned_knight_breastplate', 'drowned_knight_gauntlets',
                     'drowned_knight_legplates', 'drowned_knight_sabatons', 'scale_necklace', 'sunken_signet'],
         'mage': ['mangrove_staff', 'seer_hood', 'seer_robe', 'seer_gloves', 'seer_leggings', 'seer_sandals', 'wisp_amulet', 'glowmoss_ring'],
         'priest': ['wisp_wand', 'seer_hood', 'seer_robe', 'seer_gloves', 'scale_leggings', 'seer_sandals', 'wisp_amulet', 'glowmoss_ring']}
WHITE = {}  # poco común de nivel 7 o 9 -> común de la misma casilla, tipo y nivel
for _u in ITEMS.values():
    if _u.get('rarity') != 'uncommon' or _u.get('levelReq') not in (7, 9) or _u['type'] not in ('weapon', 'armor'): continue
    for _c in ITEMS.values():
        if (_c.get('rarity') == 'common' and _c.get('levelReq') == _u['levelReq'] and _c.get('slot') == _u['slot']
                and model.itype(_c) == model.itype(_u)):
            WHITE[_u['id']] = _c['id']

def mixed(ids):
    """La mitad de las casillas en verde (arma, pecho, piernas y cuello) y el resto en blanco del mismo tramo."""
    return [i if ITEMS[i]['slot'] in ('main_hand', 'chest', 'legs', 'neck') else WHITE.get(i, i) for i in ids]

SCENARIOS = {  # nivel del jugador -> juego
    'casillas': lambda c, lv: model.REF_GEAR[c] if lv < 7 else (REF7[c] if lv < 9 else REF9[c]),
    'llegando': lambda c, lv: model.REF_GEAR[c] if lv < 8 else mixed(FULL7[c] if lv < 10 else FULL9[c]),
    'esperado': lambda c, lv: model.REF_GEAR[c] if lv < 7 else mixed(FULL7[c] if lv < 9 else FULL9[c]),
    'completo': lambda c, lv: model.REF_GEAR[c] if lv < 7 else (FULL7[c] if lv < 9 else FULL9[c]),
}
LABEL = {'aprox': 'aproximación (gear_factor)', 'casillas': 'casillas de REF_GEAR en verde', 'llegando': 'recién llegado',
         'esperado': 'esperado (mitad verde, mitad blanco)', 'completo': 'completo (todo verde)'}
_APPROX_CHAR = tier2.char


def use(scenario):
    """Cambia el equipo que usa tier2.py (tier2.char) y vacía su caché de curvas."""
    tier2._CURVES.clear()
    if scenario == 'aprox': tier2.char = _APPROX_CHAR
    else: tier2.char = lambda cls, level: Char(cls, level, SCENARIOS[scenario](cls, level))

def set_potion():
    sp = tier2.SP0.get('item_greater_heal')
    if sp:
        tier2.POTION['heal'] = sp['effects'][0]['base']; tier2.POTION['cdSec'] = ITEMS['greater_healing_potion']['useCooldownMs'] / 1000
        return 'la poción de vida mayor de content/'
    tier2.POTION['heal'] = GREATER_POTION['heal']; tier2.POTION['cdSec'] = GREATER_POTION['cdSec']
    return f"la poción de vida mayor propuesta ({GREATER_POTION['heal']} cada {GREATER_POTION['cdSec']} s; aún no está en content/)"

def buy_price(iid, fallback_sell):
    it = ITEMS.get(iid)
    if it: return it.get('vendorPrice') or round(it['sellPrice'] * ECON['vendorBuyMultiplier'])
    return round(fallback_sell * ECON['vendorBuyMultiplier'])


# ---------------------------------------------------------------- 0. comparador del tooltip (HU-053)
def _delta(a, b):
    """Diferencias de a sobre b como en tooltip_builder.compare (misma afinidad y haste: mismo tipo de objeto)."""
    d = {}
    for k in set(a.get('stats') or {}) | set(b.get('stats') or {}):
        v = (a.get('stats') or {}).get(k, 0) - (b.get('stats') or {}).get(k, 0)
        if v: d[k] = v
    for k in ('armor', 'spellPower'):
        v = (a.get(k) or 0) - (b.get(k) or 0)
        if v: d[k] = v
    if a.get('damageMin') and b.get('damageMin'):
        v = 500 * ((a['damageMin'] + a['damageMax']) / a['speedMs'] - (b['damageMin'] + b['damageMax']) / b['speedMs'])
        if abs(v) > 1e-9: d['dps'] = v
    return d

def comparator():
    """Cada objeto de nivel 7 y 9 contra los de nivel ≤ 6 de su casilla y tipo (rareza igual o menor) y el de nivel 9 contra el de
    nivel 7 de su casilla, tipo y rareza (en joyería, del mismo tipo de stats). «Mejor» = ninguna flecha roja y al menos una verde."""
    rank = {'common': 0, 'uncommon': 1, 'rare': 2, 'epic': 3}
    gear_items = [i for i in ITEMS.values() if i['type'] in ('weapon', 'armor')]
    new = [i for i in gear_items if i.get('levelReq') in (7, 9) and i['rarity'] in ('common', 'uncommon')]
    old = [i for i in gear_items if i.get('levelReq', 1) <= 6]
    ok = 0; bad = []; side = []
    for a in new:
        main = max(a.get('stats') or {'-': 0}, key=lambda k: (a.get('stats') or {}).get(k, 0))   # joyería: solo la del mismo tipo de stats
        pool = [b for b in old if b['slot'] == a['slot'] and model.itype(b) == model.itype(a)
                and (model.itype(a) != 'jewelry' or main in (b.get('stats') or {}))]
        pool += [b for b in new if a['levelReq'] == 9 and b['levelReq'] == 7 and b['slot'] == a['slot'] and model.itype(b) == model.itype(a)
                 and b['rarity'] == a['rarity'] and set(b.get('stats') or {}) == set(a.get('stats') or {})]
        for b in pool:
            d = _delta(a, b); worse = any(v < 0 for v in d.values())
            if rank[b['rarity']] > rank[a['rarity']]: side.append((a, b, d)); continue
            if worse or not d: bad.append((a, b, d))
            else: ok += 1
    print('\n### 0. Comparador del tooltip (HU-053)')
    print(f'{ok} comparaciones «mejor» (sin flechas rojas) de {len(new)} objetos de nivel 7 y 9 contra los del Tier 1 de su casilla y tipo'
          f' y contra los de nivel 7; {len(bad)} no lo son.')
    for a, b, d in bad: print(f"- {a['name']} contra {b['name']}: {d}")
    whites = [x for x in side if x[0]['rarity'] == 'common']
    print(f'Blancos contra los verdes del Tier 1 de su casilla y tipo: {len(whites)} comparaciones, '
          f"{sum(1 for _, _, d in whites if not any(v < 0 for v in d.values()))} sin flechas rojas (el resto, cambio de stats por más armadura o DPS).")
    print('Verdes contra los raros del Capataz (nivel 6):')
    for a, b, d in side:
        if a['rarity'] == 'common': continue
        print(f"- {a['name']} (nv {a['levelReq']}) contra {b['name']}: " + ', '.join(f'{k} {fmt(v, 2 if k == "dps" else 0)}' for k, v in d.items()))


# ---------------------------------------------------------------- 1. equipo de referencia
def gear_points(ids, cls):
    st = 0.0; arm = 0.0; sp = 0.0; wdps = 0.0
    for i in ids:
        it = ITEMS[i]; m = model.affm(cls, it)
        st += sum((it.get('stats') or {}).values()) * m; arm += (it.get('armor') or 0) * m; sp += (it.get('spellPower') or 0) * m
        if it.get('damageMin'): wdps = (it['damageMin'] + it['damageMax']) / 2 * m / (it['speedMs'] / 1000)
    return st, arm, sp, wdps

def table_gear():
    print('\n### 1. Equipo de referencia: puntos de stats · armadura · poder de hechizo · DPS del arma (con afinidad)')
    print('| Clase | Tier 1 (nv 6) | ×1,4 (aprox. nv 8) | Nv 7 (sus casillas) | Nv 7 completo | ×1,8 (aprox. nv 10) | Nv 9 (sus casillas) | Nv 9 completo |')
    print('|---|---|---|---|---|---|---|---|')
    for c in CLASSES:
        base = gear_points(model.REF_GEAR[c], c)
        cell = lambda p: f'{fmt(p[0])} · {fmt(p[1])} · {fmt(p[2])} · {fmt(p[3], 1)}'
        sc = lambda f: tuple(x * f for x in base)
        print(f'| {NAMES[c]} | {cell(base)} | {cell(sc(1.4))} | {cell(gear_points(REF7[c], c))} | {cell(gear_points(FULL7[c], c))} | '
              f'{cell(sc(1.8))} | {cell(gear_points(REF9[c], c))} | {cell(gear_points(FULL9[c], c))} |')

def table_stats(scenarios):
    print('\n### 1b. Stats derivados: vida · AP/SP · armadura (rangos incluidos)')
    print('| Nv | ' + ' | '.join(f'{NAMES[c]}' for c in CLASSES) + ' |')
    print('|---|' + '---|' * len(CLASSES))
    for lv in range(6, 11):
        for s in scenarios:
            use(s); cells = []
            for c in CLASSES:
                ch = tier2.char(c, lv); cells.append(f'{ch.maxHp:.0f} · {ch.ap:.0f}/{ch.sp:.0f} · {ch.armor:.0f}')
            print(f'| {lv} {LABEL[s]} | ' + ' | '.join(cells) + ' |')


# ---------------------------------------------------------------- 2. solitario y XP por hora
def solo_summary(scenarios):
    print('\n### 2. Solitario contra los monstruos de campamento de su nivel (Linde, Pantano y Cripta)')
    print('Rotación: tiempo y vida perdida · solo básicos: vida perdida (máxima del tramo) · ciclo máximo · diferencia de XP por hora entre clases.\n')
    print('| Equipo | ' + ' | '.join(f'{NAMES[c]}: rotación / básicos' for c in CLASSES) + ' | Ciclo máx. | Dif. XP/h máx. |')
    print('|---|' + '---|' * len(CLASSES) + '---|---|')
    res = {}
    for s in scenarios:
        use(s); rows = {c: [] for c in CLASSES}; worst = 0.0; slowest = 0.0; per_mon = []
        for _, ids in ZONES:
            for mid in ids:
                mon = MONS[mid]
                if mon['type'] == 'elite': continue
                r = {c: tier2.solo(c, mon['level'], mon) for c in CLASSES}
                b = {c: tier2.solo(c, mon['level'], mon, kit='basic') for c in CLASSES}
                best = max(x['xph'] for x in r.values()); spread = 100 * (1 - min(x['xph'] for x in r.values()) / best)
                worst = max(worst, spread); slowest = max(slowest, max(x['cycle'] for x in r.values()))
                for c in CLASSES: rows[c].append((r[c], b[c]))
                per_mon.append((mid, r, spread))
        cells = []
        for c in CLASSES:
            ts = [x[0]['t'] for x in rows[c]]; lost = [x[0]['pct'] for x in rows[c]]; bl = [x[1]['pct'] for x in rows[c]]
            cells.append(f"{fmt(min(ts))}–{fmt(max(ts))} s · ≤ {fmt(max(lost))} % / ≤ {tier2.star(max(bl))}")
        print(f'| {LABEL[s]} | ' + ' | '.join(cells) + f' | {fmt(slowest)} s | {fmt(worst)} % |')
        res[s] = per_mon
    return res

def table_cycle(per_mon, scenario):
    print(f'\n#### Ciclo y XP por hora, {LABEL[scenario]}')
    print('| Monstruo | Nv | ' + ' | '.join(NAMES[c] for c in CLASSES) + ' | Dif. |')
    print('|---|---|' + '---|' * len(CLASSES) + '---|')
    for mid, r, spread in per_mon:
        mon = MONS[mid]
        print(f"| {mon['name']} | {mon['level']} | " + ' | '.join(f"{fmt(r[c]['cycle'])} s · {fmt(r[c]['xph'])}" for c in CLASSES) + f' | {fmt(spread)} % |')

def hours_6_10(scenario):
    use(scenario); out = {}
    for c in CLASSES:
        out[c] = sum(PROG['minutesPerLevel'][lv - 1] / 60 * tier2.solo(c, lv, MONS[NORMAL_BY_LEVEL[lv]])['cycle'] / PROG['killCycleSecTarget']
                     for lv in range(6, 10))
    return out


# ---------------------------------------------------------------- 4. élites
def elites(scenarios):
    print('\n### 4. Élites con el equipo real (' + set_potion() + ')')
    print('Solo a su nivel con pociones (bajo el 40 %) y, el Sacerdote, curándose; y lo mismo esquivando todas las áreas. Grupos de su nivel con pociones y curas.\n')
    print('| Élite | Equipo | ' + ' | '.join(NAMES[c] for c in CLASSES) + ' | Parejas | Tríos |')
    print('|---|---|' + '---|' * len(CLASSES) + '---|---|')
    pairs = [('warrior', 'rogue'), ('warrior', 'mage'), ('warrior', 'priest'), ('rogue', 'mage'), ('rogue', 'priest'), ('mage', 'priest')]
    trios = [('warrior', 'mage', 'priest'), ('warrior', 'rogue', 'priest'), ('warrior', 'rogue', 'mage'), ('rogue', 'mage', 'priest')]
    for _, ids in ZONES:
        for mid in ids:
            mon = MONS[mid]
            if mon['type'] != 'elite': continue
            lv = mon['level']
            for s in scenarios:
                use(s); cells = []
                for c in CLASSES:
                    out = []
                    for dodge in (False, True):
                        r = tier2.fight([c], lv, mon, potions=True, heal_threshold=0.5, dodge=dodge); m = r['members'][0]
                        if r['killed'] and m.hp > 0: out.append(f"**lo mata** ({fmt(r['t'])} s)")
                        elif m.hp > 0: out.append(f"no lo mata ({fmt(100 * max(0, r['mhp']) / mon['hp'])} %)")
                        else: out.append(f"muere ({fmt(100 * max(0, r['mhp']) / mon['hp'])} %)")
                    cells.append(' / '.join(out))
                def grp(gs):
                    ts = []; lost = 0
                    for g in gs:
                        r = tier2.fight(list(g), lv, mon, potions=True, heal_threshold=0.6)
                        dead = sum(1 for m in r['members'] if m.hp <= 0)
                        ts.append(r['t']); lost += (not r['killed']) or dead > 0
                    return f"{fmt(min(ts))}–{fmt(max(ts))} s" + (f', {lost} con baja o sin matarlo' if lost else '')
                print(f"| {mon['name']} (nv {lv}) | {LABEL[s]} | " + ' | '.join(cells) + f' | {grp(pairs)} | {grp(trios)} |')


# ---------------------------------------------------------------- 3. economía
def loot_value(tid):
    """Cobre esperado por kill: oro + chatarra + blancos vendidos; aparte, verdes y raros esperados por kill."""
    t = LOOT[tid]; gold = (t['gold']['min'] + t['gold']['max']) / 2
    junk = 0.0; whites = 0.0; greens = 0.0; rares = 0.0; potions = 0.0
    for e in t.get('entries', []):
        it = ITEMS[e['itemId']]; q = (e.get('min', 1) + e.get('max', 1)) / 2 * e['chance']
        if it['rarity'] == 'uncommon': greens += q
        elif it['rarity'] == 'rare': rares += q
        elif it['type'] == 'consumable': potions += q
        elif it['rarity'] == 'common' and it['type'] in ('weapon', 'armor'): whites += q * it['sellPrice']
        else: junk += q * it['sellPrice']
    for g in t.get('groups', []):
        tot = sum(e['weight'] for e in g['entries'])
        for e in g['entries']:
            it = ITEMS[e['itemId']]; q = g['rolls'] * e['weight'] / tot
            if it['rarity'] == 'uncommon': greens += q
            elif it['rarity'] == 'rare': rares += q
    return {'gold': gold, 'junk': junk, 'whites': whites, 'greens': greens, 'rares': rares, 'potions': potions}

def economy():
    print('\n### 3. Economía del nivel 6 al 10 (equipo esperado)')
    pot = buy_price(GREATER_POTION['id'], GREATER_POTION['sellPrice']); food = buy_price(FOOD['id'], FOOD['sellPrice'])
    mana = buy_price('minor_mana_potion', 5)
    print(f"Vendedor del Linde ({VENDORS['forest_camp']['name']}) hoy: {', '.join(ITEMS[i]['name'] for i in VENDORS['forest_camp']['items'])}."
          f' Precios del tier: poción de vida mayor {pot} cobres, comida {food}, poción menor de maná {mana}.\n')
    print('| Nv | Monstruo | Cobre + chatarra + blancos por kill | Verdes por 100 kills | ' + ' | '.join(f'{NAMES[c]}: kills/h · platas/h' for c in CLASSES) + ' |')
    print('|---|---|---|---|' + '---|' * len(CLASSES))
    use('esperado'); totals = {c: {'copper': 0.0, 'kills': 0.0, 'hours': 0.0, 'fight': 0.0} for c in CLASSES}
    for lv in range(6, 11):
        mon = MONS[NORMAL_BY_LEVEL[lv]]; v = loot_value(mon['lootTableId']); per = v['gold'] + v['junk'] + v['whites']
        cells = []
        for c in CLASSES:
            r = tier2.solo(c, lv, mon); kph = 3600 / r['cycle']
            cells.append(f"{fmt(kph)} · {fmt(kph * per / 100, 1)}")
            if lv < 10:
                h = PROG['minutesPerLevel'][lv - 1] / 60 * r['cycle'] / PROG['killCycleSecTarget']
                totals[c]['copper'] += h * kph * per; totals[c]['kills'] += h * kph; totals[c]['hours'] += h; totals[c]['fight'] += h * kph * r['t']
        print(f"| {lv} | {mon['name']} | {fmt(per, 1)} ({fmt(v['gold'])} + {fmt(v['junk'], 1)} + {fmt(v['whites'], 1)}) | {fmt(100 * v['greens'], 1)} | " + ' | '.join(cells) + ' |')
    print('\nDel nivel 6 al 10 (los 4 niveles, contra el normal de cada nivel; sin contar élites ni verdes vendidos):\n')
    print('| Clase | Horas | Kills | Ingresos | Pociones: 1 cada 10 kills | Pociones en cada recarga en combate (peor caso) | Comida en cada kill | Ingresos − (peor caso + comida) |')
    print('|---|---|---|---|---|---|---|---|')
    for c in CLASSES:
        t = totals[c]; normal = t['kills'] / 10 * pot; worst = t['fight'] / GREATER_POTION['cdSec'] * pot; eat = t['kills'] * food
        oro = lambda x: f'{fmt(x / 100)} platas'
        print(f"| {NAMES[c]} | {fmt(t['hours'], 1)} h | {fmt(t['kills'])} | {oro(t['copper'])} | {oro(normal)} ({fmt(100 * normal / t['copper'])} %) | "
              f"{oro(worst)} ({fmt(100 * worst / t['copper'])} %) | {oro(eat)} ({fmt(100 * eat / t['copper'])} %) | {oro(t['copper'] - worst - eat)} |")
    print('\nÉlites (botín por kill, a repartir en el grupo):\n')
    print('| Élite | Cobre + chatarra | Verdes | Raros |')
    print('|---|---|---|---|')
    for mid in ('old_bear', 'swamp_witch', 'crypt_guardian'):
        v = loot_value(MONS[mid]['lootTableId'])
        print(f"| {MONS[mid]['name']} | {fmt(v['gold'] + v['junk'], 1)} | {fmt(v['greens'], 2)} | {fmt(v['rares'], 2)} |")


if __name__ == '__main__':
    quick = '--quick' in sys.argv
    print('# HU-110: equipo real de los niveles 7 y 9, botín y economía')
    comparator()
    table_gear()
    table_stats(['aprox', 'esperado'])
    scen = ['aprox', 'llegando', 'esperado', 'completo']
    per = solo_summary(scen)
    table_cycle(per['esperado'], 'esperado')
    print('\nHoras del nivel 6 al 10 contra el normal de cada nivel (la curva da '
          f"{fmt(sum(PROG['minutesPerLevel'][lv - 1] for lv in range(6, 10)) / 60, 1)} h a {PROG['killCycleSecTarget']} s):")
    for s in scen:
        h = hours_6_10(s); print(f'- {LABEL[s]}: ' + ', '.join(f'{NAMES[c]} {fmt(v, 1)} h' for c, v in h.items()))
    economy()
    if not quick: elites(['aprox', 'esperado', 'completo'])
    use('aprox')
