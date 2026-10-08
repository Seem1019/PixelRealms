"""Mejoras 1-de-2 de los hechizos (HU-107): cada mejora sobre el pentagrama, regla 40/75 con todas las builds y XP por hora.

Uso: python tools/balance/upgrades.py                  (todo)
     python tools/balance/upgrades.py --draft f.json   (mejoras de un borrador {spellId: [mejora, mejora]} en lugar de las de
                                                        content/spells.json; para iterar números sin tocar el contenido)
     python tools/balance/upgrades.py --quick          (sin la XP por hora)

Supuestos (docs/design/balance-report.md §HU-107), los de phase2.py (HU-106) más:
- Una mejora se aplica como SpellUpgrades.Apply: valor final = valor · mult + add; `effect` + `mult` escala base, coeficientes,
  porcentaje de arma y cantidad de los efectos de ese tipo; `aura` + `stat` cambia una copia del aura que aplica el hechizo;
  `addEffect` añade el efecto al final. Redondeo de los campos enteros como el servidor (al alejarse de cero). El rango (+30 % del
  `base` al nivel 10) se aplica después, sobre el `base` mejorado (HU-104, "rango y mejora multiplican el mismo base").
- Lo que suma una mejora en mono y área es la diferencia media con ventanas de 24 a 36 s (WINDOWS): sin azar, en 30 s exactos una
  recarga algo más corta da 0 o un lanzamiento entero más. El hechizo sin mejora conserva su valor de 30 s.
- Lo que el modelo de phase2.py no miraba porque ningún hechizo lo cambiaba, y una mejora sí puede cambiar (los hechizos sin mejora
  dan lo mismo que en phase2.py):
  * Forma del área: lo que gana o pierde una mejora de radio, apertura o largo se mide con la cobertura del grupo de referencia
    (phase2.coverage: 3 secundarios en un radio de 2) y escala la parte de los secundarios en área y en control.
  * Carga: recorre (minRange + alcance) / 2 casillas (5 con el alcance de 8, lo que contaba model.mob_value).
  * Ralentización: como mucho rules.combat.maxSlowPct (el servidor no aplica más).
  * Armadura: también las auras con `applyTo: self` de un hechizo que no es sobre uno mismo, y las curas propias (como un escudo).
- Fuera del pentagrama, para comparar las dos mejoras de un hechizo: recurso por minuto lanzándolo en cuanto está listo, valor de un
  lanzamiento (daño, cura, DoT/HoT o escudo sobre un objetivo), alcance y secundarios tocados con un grupo más disperso (3 en un
  radio de 3), que es donde se nota el radio de un área que ya cubre el grupo de referencia.
- Builds: 4 de los 6 hechizos de la clase × (sin mejora, A o B) en cada uno; el valor de la clase en una punta es el del
  pentagrama completo (phase2.py, decisión 3).
"""
import sys, os, copy, math, json, itertools
sys.path.insert(0, os.path.dirname(__file__))
import model
from model import *
import tier2
from tier2 import fmt, NAMES, CLASSES, MONS, NORMAL_BY_LEVEL
import phase2
from phase2 import AX, AX_ES, PG, BUDGET, CAP

UPGRADE_LEVEL = R['progression']['spellUpgradeLevel']
SP_CONTENT = copy.deepcopy(tier2.SP0)
AU_CONTENT = copy.deepcopy(tier2.AU0)
if '--draft' in sys.argv:
    _draft = json.load(open(sys.argv[sys.argv.index('--draft') + 1], encoding='utf-8'))
    for _sid, _ups in _draft.items(): SP_CONTENT[_sid]['upgrades'] = _ups


# ---------------------------------------------------------------- SpellUpgrades.Apply
def round_away(x):
    """Math.Round(x, MidpointRounding.AwayFromZero)."""
    return int(math.floor(abs(x) + 0.5)) * (1 if x >= 0 else -1)

SPELL_DEFAULTS = {'aoeRadius': 0.0, 'aoeAngleDeg': 0.0, 'aoeLength': 0.0, 'aoeWidth': 0.0, 'maxTargets': 10}
INT_STATS = ('cooldownMs', 'castMs', 'maxTargets')

def apply(spell, up, AU):
    """Hechizo efectivo con la mejora `up` y las auras que cambia ({id nuevo: aura}). Como el servidor, una aura cambiada es una
    copia con el mismo id para el juego; aquí lleva el id `aura@mejora` para que convivan en el mismo diccionario."""
    s = copy.deepcopy(spell); s.pop('upgrades', None); s['appliedUpgrade'] = up['id']
    effects = [dict(e) for e in spell['effects']]
    over = {}                                                   # índice del efecto -> aura cambiada
    for m in up['mods']:
        mult, add = m.get('mult', 1.0), m.get('add', 0.0)
        V = lambda v: v * mult + add
        if 'addEffect' in m:
            effects.append(dict(m['addEffect']))
        elif 'aura' in m:
            for i, e in enumerate(effects):
                if e['type'] == 'apply_aura' and e['auraId'] == m['aura']:
                    a = dict(over.get(i) or AU[m['aura']])
                    if m['stat'] == 'durationMs': a['durationMs'] = round_away(V(a['durationMs']))
                    elif m['stat'] == 'pct': a['pct'] = V(a.get('pct', 0.0))
                    elif m['stat'] == 'amount':
                        a['base'] = V(a.get('base', 0.0))
                        for k in ('apCoef', 'spCoef'):
                            if k in a: a[k] = a[k] * mult
                    over[i] = a
        elif 'effect' in m:
            for i, e in enumerate(effects):
                if e['type'] == m['effect']:
                    for k in ('base', 'apCoef', 'spCoef', 'weaponPct', 'amount'):
                        if k in e: e[k] = e[k] * mult
        else:
            st = m['stat']
            if st == 'cost':
                if s.get('cost'): s['cost'] = dict(s['cost'], amount=round_away(V(s['cost']['amount'])))
            else:
                v = V(s.get(st, SPELL_DEFAULTS.get(st, 0.0)))
                s[st] = round_away(v) if st in INT_STATS else v
    new = {}
    for i, a in over.items():
        nid = f"{a['id']}@{up['id']}"; a = dict(a, id=nid); new[nid] = a; effects[i] = dict(effects[i], auraId=nid)
    s['effects'] = effects
    return s, new

def check_vectors():
    """apply() frente a shared/test-vectors/spell_upgrades.json, los casos que pasan en xUnit y en GUT: (correctos, total)."""
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'shared', 'test-vectors', 'spell_upgrades.json')
    cases = json.load(open(path, encoding='utf-8'))['cases']; good = 0
    close = lambda a, b: math.isclose(a, b, abs_tol=1e-9)
    for c in cases:
        up = next(u for u in c['spell']['upgrades'] if u['id'] == c['upgradeId'])
        s, new = apply(c['spell'], up, {a['id']: a for a in c['auras']}); e = c['expected']
        ok = all(close(s.get(k, SPELL_DEFAULTS.get(k, 0)), e[k]) for k in ('castMs', 'cooldownMs', 'range', 'aoeRadius', 'aoeAngleDeg',
                                                                          'aoeLength', 'aoeWidth', 'maxTargets'))
        ok &= close((s.get('cost') or {}).get('amount', 0), e['cost']) and len(s['effects']) == len(e['effects'])
        for got, exp in zip(s['effects'], e['effects']):
            for k, v in exp.items():
                ok &= got.get(k, '').split('@')[0] == v if k == 'auraId' else (close(got.get(k, 0), v) if isinstance(v, (int, float)) else got.get(k) == v)
        auras = {nid.split('@')[0]: a for nid, a in new.items()}
        ok &= set(auras) == set(e['auras']) and all(close(auras[a].get(k, 0.0), v) for a, ea in e['auras'].items() for k, v in ea.items())
        good += ok
    return good, len(cases)

def variants(SP=None):
    """{(spellId, upgradeId | None): (hechizo, auras nuevas)} de los hechizos de clase con mejoras."""
    SP = SP or SP_CONTENT; out = {}
    for sid, s in SP.items():
        if s['source'] != 'class': continue
        out[(sid, None)] = (s, {})
        for up in s.get('upgrades', []): out[(sid, up['id'])] = apply(s, up, AU_CONTENT)
    return out

def content_with(choice):
    """Copia de hechizos y auras del contenido con las mejoras elegidas ({spellId: upgradeId}) puestas en su sitio."""
    SP = copy.deepcopy(SP_CONTENT); AU = copy.deepcopy(AU_CONTENT)
    for sid, uid in choice.items():
        if uid is None: continue
        up = next(u for u in SP_CONTENT[sid]['upgrades'] if u['id'] == uid)
        SP[sid], new = apply(SP_CONTENT[sid], up, AU_CONTENT); AU.update(new)
    return SP, AU

def ranked(SP0, AU0, level):
    """tier2.ranked con otro contenido: +spellRankBonusPct del `base` por rango en los efectos y auras de los hechizos de clase."""
    old = tier2.SP0, tier2.AU0
    tier2.SP0, tier2.AU0 = SP0, AU0
    try: return tier2.ranked(level)
    finally: tier2.SP0, tier2.AU0 = old


# ---------------------------------------------------------------- medidas (las de phase2.pentagram, más la forma)
MELEE_D = {'rogue': 1.25, 'warrior': 1.5}
# Sin azar, en 30 s exactos una recarga o un casteo algo más corto da 0 o un lanzamiento entero más (Eviscerar con 4 s menos de
# recarga cabe 3 veces, igual que sin mejora). Lo que suma una mejora en mono y área es la diferencia media con estas ventanas;
# el hechizo sin mejora se queda con su valor de 30 s (el de phase2.py).
WINDOWS = (24.0, 27.0, 30.0, 33.0, 36.0)

def caster_distance(s):
    return 1.5 if s['targeting'].startswith('self_aoe') else MELEE_D.get(s.get('classId'), 4.0)

def is_area(s):
    return s['targeting'] in ('self_aoe_enemies', 'ground_aoe_enemies', 'ground_aoe_all', 'self_aoe_allies', 'ground_aoe_allies')

_COV = {}
def cov(s, spread=2.0):
    """Fracción esperada de 3 secundarios (en un radio `spread` del principal) que toca el área (phase2.coverage)."""
    sh = phase2.shape_of(s); key = (json.dumps(sh, sort_keys=True), caster_distance(s), spread)
    if key not in _COV:
        if spread == 2.0: _COV[key] = phase2.coverage(sh, caster_distance(s))
        else:
            import random
            rnd = random.Random(7); hit = 0
            for _ in range(4000):
                rr = spread * math.sqrt(rnd.random()); th = rnd.random() * 2 * math.pi
                hit += phase2._touches(sh, rr * math.cos(th), rr * math.sin(th), caster_distance(s))
            _COV[key] = hit / 4000
    return _COV[key]

def capped(AU):
    """Las ralentizaciones por encima de rules.combat.maxSlowPct cuentan como el tope (el servidor no aplica más)."""
    out = {}
    for k, a in AU.items():
        out[k] = dict(a, pct=min(a['pct'], CB['maxSlowPct'])) if a.get('kind') == 'slow' and a.get('pct', 0) > CB['maxSlowPct'] else a
    return out

def mob(ch, s, AU):
    """model.mob_value con la Carga según su alcance: (minRange + alcance) / 2 casillas (5 con el alcance de 8)."""
    v = mob_value(ch, s, AU)
    for e in s['effects']:
        if e['type'] == 'dash':
            v += ((e.get('minRange', 0) + s['range']) / 2 - 5.0) * 60 / max(s['cooldownMs'] / 1000, 1.0)
    return v

def armor(ch, s, AU, level):
    """model.armor_ttl con las auras propias de cualquier hechizo (applyTo self) y las curas propias, como un escudo."""
    phys = 10.0 * PHYS_SHARE * (1 - CB['physicalMissBase']) * (1 - ch.dodge) * (1 - mitig(ch.armor, level))
    mag = 10.0 * (1 - PHYS_SHARE) * (1 - CB['magicMissBase'])
    inc = phys + mag; red = 0.0; rate = 0.0; cd = max(1.0, s['cooldownMs'] / 1000)
    own = s['targeting'] == 'self'
    for e in s['effects']:
        if not (own or e.get('applyTo') == 'self'): continue
        if e['type'] == 'apply_aura':
            a = AU[e['auraId']]; up = min(1.0, (a['durationMs'] / 1000) / cd)
            dt = a.get('mods', {}).get('damageTakenPct', 0)
            if dt < 0: red += -dt * up
            if a['kind'] == 'shield': rate += (a['base'] + a.get('spCoef', 0) * ch.sp) / cd
        if e['type'] == 'heal':
            rate += eff_amount(ch, s, e) / cd
    return ch.maxHp / max(inc * (1 - red) - rate, 0.1)

def per_cast(ch, s, AU):
    """Valor de un lanzamiento sobre un objetivo: daño o cura, todo el DoT/HoT y el escudo (rangos ya aplicados)."""
    v = 0.0
    for e in s['effects']:
        if e['type'] in ('damage', 'heal') and 'base' in e: v += eff_amount(ch, s, e)
        if e['type'] == 'apply_aura':
            a = AU[e['auraId']]
            if a['kind'] in ('dot', 'hot', 'shield') and 'base' in a:
                per, tick, dur = aura_total(ch, a)
                v += per * (round(dur / tick) if tick else 1)
    return v

def uses_per_min(s):
    gcd = CB['gcdMs'] / 1000 if s.get('triggersGcd', True) else 0
    return 60 / max(s['cooldownMs'] / 1000, s['castMs'] / 1000, gcd, 0.25)

def references(level):
    SP, AU = ranked(SP_CONTENT, AU_CONTENT, level)
    old = dict(model.TARGET); model.TARGET.update(phase2.ref_target(level))
    rog, war = tier2.char('rogue', level), tier2.char('warrior', level)
    ref = {'single': basic_hit(rog) / rog.swing / 0.30}
    ref['aoe'] = 4 * 0.6 * ref['single']
    ref['cc'] = PG['references']['ccSecPerMin']; ref['mobility'] = PG['references']['mobilityTilesPerMin']
    ref['armor'] = armor_ttl(war, SP, AU, (), att_level=level) / 0.70
    model.TARGET.clear(); model.TARGET.update(old)
    return ref

def measure(level, V=None):
    """Base de cada clase y, por cada variante (hechizo, mejora) aprendida a ese nivel, su aporte en puntos y lo de fuera."""
    V = V or variants(); ref = references(level)
    SP = copy.deepcopy(SP_CONTENT); AU = copy.deepcopy(AU_CONTENT)
    keys = {}
    for (sid, uid), (s, new) in V.items():
        if s['levelReq'] > level or (uid and level < UPGRADE_LEVEL): continue
        k = sid if uid is None else f'{sid}#{uid}'
        SP[k] = dict(s, id=k); AU.update(new); keys[(sid, uid)] = k
    SPr, AUr = ranked(SP, AU, level); AUr = capped(AUr)
    old = dict(model.TARGET); model.TARGET.update(phase2.ref_target(level)); model.AOE_N = 3
    res = {}
    for cls in CLASSES:
        ch = tier2.char(cls, level); heal = cls == 'priest'
        bm, _ = simulate(ch, [], SPr, AUr, heal_mode=heal)
        ttl = armor_ttl(ch, SPr, AUr, (), att_level=level)
        base = {'single': 100 * bm / 30 / ref['single'], 'aoe': 0.0, 'cc': 0.0, 'mobility': 0.0, 'armor': 100 * ttl / ref['armor']}
        sims = {}
        def sim(k, T, heal_mode):
            if (k, T, heal_mode) not in sims: sims[(k, T, heal_mode)] = simulate(ch, [k], SPr, AUr, T=T, heal_mode=heal_mode)
            return sims[(k, T, heal_mode)]
        out = {}
        for (sid, uid), k in keys.items():
            s = SPr[k]
            if s.get('classId') != cls: continue
            b = SPr[sid]                                       # el mismo hechizo sin mejora (forma de referencia)
            r = 1.0
            if is_area(s) and is_area(b) and phase2.shape_of(s) != phase2.shape_of(b):
                r = cov(s) / max(cov(b), 1e-9)
            nsec = min(3, s.get('maxTargets', 10) - 1) if is_area(s) else 0
            f = (1 + nsec * r) / (1 + nsec) if nsec else 1.0
            m, sc = sim(sid, 30.0, heal)
            single, aoe = (m - bm) / 30, sc / 30
            if uid is not None:                                # la mejora: diferencia media con varias ventanas (WINDOWS)
                single += sum((sim(k, T, heal)[0] - sim(sid, T, heal)[0]) / T for T in WINDOWS) / len(WINDOWS)
                aoe += sum((sim(k, T, heal)[1] * f - sim(sid, T, heal)[1]) / T for T in WINDOWS) / len(WINDOWS)
            cc = cc_value2(ch, s, AUr)
            if r != 1.0 and is_area(s):
                n = min(3, s.get('maxTargets', 10)); cc = cc / (1 + 0.5 * (n - 1)) * (1 + 0.5 * (n - 1) * r) if n > 1 else cc
            raw = {'single': single, 'aoe': aoe, 'cc': cc, 'mobility': mob(ch, s, AUr), 'armor': armor(ch, s, AUr, level) - ttl}
            pts = {a: 100 * raw[a] / ref[a] for a in AX}
            side = {'cost': uses_per_min(s) * (s.get('cost') or {}).get('amount', 0), 'cast': per_cast(ch, s, AUr),
                    'range': s['range'], 'spread': 3 * cov(s, 3.0) if is_area(s) and s.get('maxTargets', 10) > 1 else None,
                    'dmg': None}
            if heal:                                           # el daño del Sacerdote no está en su pentagrama (lo vigila la XP por hora)
                side['dmg'] = sum((sum(sim(k, T, False)) - simulate(ch, [], SPr, AUr, T=T)[0]) / T for T in WINDOWS) / len(WINDOWS)
            out[(sid, uid)] = {'pts': pts, 'side': side}
        res[cls] = {'base': base, 'spells': out}
    model.TARGET.clear(); model.TARGET.update(old)
    return ref, res


# ---------------------------------------------------------------- informes
def sgn(x, d=1): return f'{x:+.{d}f}'.replace('.', ',')

def up_name(sid, uid):
    if uid is None: return 'sin mejora'
    return next(u['name'] for u in SP_CONTENT[sid]['upgrades'] if u['id'] == uid)

EPS = 0.5        # empate en el pentagrama: menos de medio punto (las tablas redondean a puntos enteros)
EPS_SIDE = 0.02  # empate fuera del pentagrama: menos de un 2 %
SIDE = (('cost', -1), ('cast', 1), ('range', 1), ('spread', 1), ('dmg', 1))   # signo: el coste, cuanto menos mejor

def dominates(a, b, side=False):
    """¿a es al menos igual que b en todo y mejor en algo? side: también fuera del pentagrama."""
    pairs = [(a['pts'][x], b['pts'][x], EPS) for x in AX]
    if side:
        for k, sign in SIDE:
            if a['side'][k] is None or b['side'][k] is None: continue
            x, y = sign * a['side'][k], sign * b['side'][k]
            pairs.append((x, y, EPS_SIDE * max(abs(x), abs(y), 1e-9)))
    return all(x >= y - e for x, y, e in pairs) and any(x > y + e for x, y, e in pairs)

def report_upgrades(level=CAP):
    ref, P = measure(level)
    print(f'\n## Cada mejora sobre el pentagrama al nivel {level} (aporte con la mejora − aporte sin ella; puntos)')
    print('Fuera del pentagrama: recurso por minuto lanzándolo en cuanto está listo, valor de un lanzamiento, alcance, secundarios'
          ' tocados con un grupo disperso (3 en un radio de 3) y, en el Sacerdote, el daño por segundo que no cuenta en su pentagrama.\n')
    print('| Clase | Hechizo | Mejora | ' + ' | '.join(AX_ES[a] for a in AX) + ' | Suma (total) | Recurso/min | Por lanzamiento | Alcance | Disperso | Daño/s |')
    print('|---|---|---|' + '---|' * len(AX) + '---|---|---|---|---|---|')
    flags5, flags = [], []
    for cls in CLASSES:
        sp = P[cls]['spells']
        for sid in phase2.kit(cls, level):
            if (sid, None) not in sp: continue
            b = sp[(sid, None)]; ups = [u['id'] for u in SP_CONTENT[sid].get('upgrades', [])]
            btot = sum(b['pts'].values())
            for uid in [None] + ups:
                v = sp[(sid, uid)]; d = {a: v['pts'][a] - b['pts'][a] for a in AX}
                tot = sum(v['pts'].values()); flag = ' **> 40**' if tot > PG['abilityMaxPoints'] else ''
                cells = [fmt(v['pts'][a]) if uid is None else (sgn(d[a]) if abs(d[a]) >= 0.05 else '·') for a in AX]
                total = fmt(tot) if uid is None else f'{sgn(tot - btot)} ({fmt(tot)})'
                sd = v['side']
                print(f"| {NAMES[cls] if uid is None and sid == phase2.kit(cls, level)[0] else ''} | {tier2.SP0[sid]['name'] if uid is None else ''} | "
                      f"{up_name(sid, uid)} | " + ' | '.join(cells) + f" | {total}{flag}"
                      + f" | {fmt(sd['cost'])} | {fmt(sd['cast'])} | {fmt(sd['range'], 1)} | {'—' if sd['spread'] is None else fmt(sd['spread'], 1)}"
                      + f" | {'—' if sd['dmg'] is None else fmt(sd['dmg'], 1)} |")
            if len(ups) == 2:
                A, B = sp[(sid, ups[0])], sp[(sid, ups[1])]
                for x, y, nx, ny in ((A, B, ups[0], ups[1]), (B, A, ups[1], ups[0])):
                    if dominates(x, y): flags5.append((sid, nx, ny))
                    if dominates(x, y, side=True): flags.append((sid, nx, ny))
    print('\nParejas en las que una mejora domina a la otra en el pentagrama (igual o mejor en las 5 puntas y mejor en alguna): '
          + ('ninguna.' if not flags5 else '; '.join(f"{tier2.SP0[s]['name']}: {up_name(s, x)} sobre {up_name(s, y)}" for s, x, y in flags5) + '.'))
    print('Contando también lo de fuera del pentagrama: '
          + ('ninguna.' if not flags else '; '.join(f"{tier2.SP0[s]['name']}: {up_name(s, x)} sobre {up_name(s, y)}" for s, x, y in flags) + '.'))
    return P, flags5, flags

def builds(P, cls, level):
    """Todas las builds de 4 hechizos × (sin mejora, A, B): (total, [(sid, uid)], puntas)."""
    base = P[cls]['base']; sp = P[cls]['spells']
    sids = [s for s in phase2.kit(cls, level) if (s, None) in sp]
    out = []
    for combo in itertools.combinations(sids, min(4, len(sids))):
        opts = [[(s, u) for (s2, u) in sp if s2 == s] for s in combo]
        for pick in itertools.product(*opts):
            tot = {a: base[a] + sum(sp[k]['pts'][a] for k in pick) for a in AX}
            out.append((sum(tot.values()), pick, tot))
    return out

def label(pick):
    return ' + '.join(tier2.SP0[s]['name'] + (f' ({up_name(s, u)})' if u else '') for s, u in pick)

def report_builds(P, level=CAP):
    print(f'\n## Builds al nivel {level}: 4 hechizos × (sin mejora, A o B); regla 40/75 y valor de la clase por punta')
    ok = True
    print('\n| Clase | Builds | Máx. total (de ' + fmt(BUDGET, 1) + ') | Mín. total | ' + ' | '.join(f'Máx. {AX_ES[a].lower()}' for a in AX) + ' | Incumplen |')
    print('|---|---|---|---|' + '---|' * len(AX) + '---|')
    tops = {}
    for cls in CLASSES:
        cv = PG['classes'][cls]; B = builds(P, cls, level); B.sort(key=lambda r: r[0])
        bad = [r for r in B if r[0] > BUDGET or any(r[2][a] > cv[a] + 1e-9 for a in AX)]
        one = [(k, v) for k, v in P[cls]['spells'].items() if sum(v['pts'].values()) > PG['abilityMaxPoints']]
        ok &= not bad and not one
        full = [r for r in B if all(u for _, u in r[1])]        # con una mejora elegida en cada hechizo
        tops[cls] = (B[-1], B[0], bad, one, full[0])
        print(f"| {NAMES[cls]} | {len(B)} | {fmt(B[-1][0], 1)} | {fmt(B[0][0], 1)} | "
              + ' | '.join(f"{fmt(max(r[2][a] for r in B))} (de {cv[a]})" for a in AX) + f" | {len(bad) + len(one)} |")
    for cls in CLASSES:
        top, low, bad, one, lowf = tops[cls]
        print(f"\n- **{NAMES[cls]}**, la más fuerte ({fmt(top[0], 1)}): {label(top[1])} · " + ' / '.join(fmt(top[2][a]) for a in AX))
        print(f"  la más débil ({fmt(low[0], 1)}): {label(low[1])} · " + ' / '.join(fmt(low[2][a]) for a in AX))
        print(f"  la más débil con una mejora en cada hechizo ({fmt(lowf[0], 1)}): {label(lowf[1])} · " + ' / '.join(fmt(lowf[2][a]) for a in AX))
        for r in bad[:5]:
            over = [f"{AX_ES[a]} {fmt(r[2][a], 1)} > {PG['classes'][cls][a]}" for a in AX if r[2][a] > PG['classes'][cls][a] + 1e-9]
            print(f"  **incumple** ({fmt(r[0], 1)}{', ' + ', '.join(over) if over else ''}): {label(r[1])}")
        for k, v in one: print(f"  **hechizo de más de 40**: {label([k])} ({fmt(sum(v['pts'].values()), 1)})")
    print('\n' + (f'Regla 40/75 al nivel {level}: se cumple en todas las builds.' if ok else f'**Regla 40/75 al nivel {level}: NO se cumple** (arriba).'))
    return ok, tops

def report_levels():
    print('\n## Regla 40/75 con mejoras en los niveles en que se eligen (8 a 10; hechizos aprendidos a ese nivel)\n')
    print('| Nv | ' + ' | '.join(f'{NAMES[c]}: hechizo máx. · build máx. · puntas por encima' for c in CLASSES) + ' |')
    print('|---|' + '---|' * len(CLASSES))
    ok = True
    for lv in range(UPGRADE_LEVEL, CAP + 1):
        _, P = measure(lv); cells = []
        for cls in CLASSES:
            cv = PG['classes'][cls]; base = P[cls]['base']
            k1 = max(P[cls]['spells'], key=lambda k: sum(P[cls]['spells'][k]['pts'].values()))
            top1 = sum(P[cls]['spells'][k1]['pts'].values())
            B = builds(P, cls, lv); top4 = max(r[0] for r in B)
            mx = {a: max(r[2][a] for r in B) for a in AX}
            over = [f'{AX_ES[a]} {fmt(mx[a], 1)} > {cv[a]}' for a in AX if mx[a] > cv[a] + 1e-9 and mx[a] > base[a] + 0.5]
            ok &= top1 <= PG['abilityMaxPoints'] and top4 <= BUDGET and not over
            cells.append(f"{fmt(top1, 1)} ({label([k1])}) · {fmt(top4, 1)} · {', '.join(over) or 'ninguna'}")
        print(f'| {lv} | ' + ' | '.join(cells) + ' |')
    print('\n' + ('Se cumple en los tres niveles.' if ok else '**No se cumple en algún nivel.**'))
    return ok


# ---------------------------------------------------------------- XP por hora con las mejoras que más farmean
def farm_with(choice, cls, level, kit):
    SP, AU = content_with(choice)
    old = tier2.SP0, tier2.AU0
    tier2.SP0, tier2.AU0 = SP, AU; tier2._CURVES.clear()
    try: return phase2.farm_smooth(cls, level, MONS[NORMAL_BY_LEVEL[level]], kit)
    finally: tier2.SP0, tier2.AU0 = old; tier2._CURVES.clear()

def best_farm(cls, level):
    """El mejor equipo de phase2.FARM_KITS con la mejor mejora de cada hechizo (una a una y luego juntas) frente al de HU-106."""
    best = None
    for name, kit in phase2.FARM_KITS[cls].items():
        r0 = farm_with({}, cls, level, kit)
        if r0 is None: continue
        pick = {}
        for sid in kit:
            opts = [None] + [u['id'] for u in SP_CONTENT[sid].get('upgrades', [])]
            scored = [(farm_with({sid: u}, cls, level, kit)['xph'], u) for u in opts]
            pick[sid] = max(scored, key=lambda t: t[0])[1]
        r = farm_with(pick, cls, level, kit)
        if best is None or r['xph'] > best[1]['xph']: best = (name, r, pick, r0)
    return best

def report_farm():
    print('\n## XP por hora en solitario con la mejor mejora para farmear (phase2.py suavizado: normal de cada nivel)\n')
    print('| Nv | Monstruo | ' + ' | '.join(NAMES[c] for c in CLASSES) + ' | Dif. con mejoras | Dif. sin mejoras (HU-106) |')
    print('|---|---|' + '---|' * len(CLASSES) + '---|---|')
    worst = 0.0; picks = {}
    for lv in range(UPGRADE_LEVEL, CAP + 1):
        res = {c: best_farm(c, lv) for c in CLASSES}
        x = {c: res[c][1]['xph'] for c in CLASSES}
        x0 = {c: max(farm_with({}, c, lv, k)['xph'] for k in phase2.FARM_KITS[c].values() if farm_with({}, c, lv, k)) for c in CLASSES}
        sp = 100 * (1 - min(x.values()) / max(x.values())); sp0 = 100 * (1 - min(x0.values()) / max(x0.values()))
        worst = max(worst, sp)
        cells = [f"{fmt(x[c])} ({'+' if x[c] >= x0[c] else ''}{fmt(100 * (x[c] / x0[c] - 1), 1)} %)" for c in CLASSES]
        print(f"| {lv} | {MONS[NORMAL_BY_LEVEL[lv]]['name']} | " + ' | '.join(cells) + f" | {fmt(sp)} % | {fmt(sp0)} % |")
        picks[lv] = res
    print('\nMejoras elegidas para farmear (nivel 10): ' + ' · '.join(
        f"{NAMES[c]} ({picks[CAP][c][0]}): " + ', '.join(f"{tier2.SP0[s]['name']} {up_name(s, u)}" for s, u in picks[CAP][c][2].items() if u)
        for c in CLASSES))
    return worst


if __name__ == '__main__':
    print(f"# HU-107: mejoras de los hechizos (spellRankBonusPct {R['progression']['spellRankBonusPct']}, nivel de mejora {UPGRADE_LEVEL})")
    good, total = check_vectors()
    print(f'apply() reproduce SpellUpgrades.Apply en {good} de {total} casos de shared/test-vectors/spell_upgrades.json.')
    if good != total: sys.exit(1)
    P, f5, f = report_upgrades(CAP)
    report_builds(P, CAP)
    report_levels()
    if '--quick' not in sys.argv: report_farm()
