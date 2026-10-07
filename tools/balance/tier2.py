"""Monstruos del Tier 2 (HU-109): solitario, XP por hora y élites de los niveles 6 a 11. Valores esperados, sin azar.

Uso: python tools/balance/tier2.py            (todo: solitario, XP por hora y élites)
     python tools/balance/tier2.py --tier1    (sin equipo nuevo: solo el verde del Tier 1)
     python tools/balance/tier2.py --quick    (sin los élites)

Supuestos (docs/design/balance-report.md §Fase 2):
- Equipo: no hay equipo de nivel 7 a 9 hasta HU-110. Aproximación: el verde de referencia del Tier 1 (model.REF_GEAR) con todo
  lo numérico (stats, armadura, daño del arma, poder de hechizo) × gear_factor(nivel) = 1 + 0,2 · (nivel − 6). Es el
  presupuesto lineal por nivel de la skill game-content, con el equipo de nivel N − 1 de media al nivel N.
- Rangos de hechizo como el servidor (+spellRankBonusPct del `base` por rango, rules.progression.spellRankLevels).
- Hechizos: los 16 de la Fase 1. Los de nivel 7 y 9 (HU-106) no entran aquí: el solitario con ellos lo mide phase2.py.
- El monstruo pega con su básico y sus hechizos (cooldown, casteo que pausa su básico, hpBelowPct). Las áreas marcadas no se
  esquivan (peor caso) y el modelo no ve el kiteo, las interrupciones ni los controles del jugador sobre el monstruo.
- El cuerpo a cuerpo tarda (attackRange − alcance del arma) / velocidad en llegar a un monstruo a distancia.
"""
import sys, os, copy, math
sys.path.insert(0, os.path.dirname(__file__))
import model
from model import *

SP0, AU0 = load_content()
MONS = {m['id']: m for m in Lj('monsters.json')['monsters']}
PROG = R['progression']
WEAPONS = R['weapons']['types']
SPEED = R['movement']['baseSpeedTilesPerSec']
TIER1_ONLY = '--tier1' in sys.argv
DT = 0.05
T_MAX = 600.0
WALK_SEC = 10.0                      # caminata entre monstruos (igual que check.py)
DODGE_RETURN_SEC = 0.5               # al esquivar un área, el cuerpo a cuerpo tarda esto en volver a pegar tras el golpe
POTION = {'heal': SP0['item_minor_heal']['effects'][0]['base'],          # Poción menor de vida
          'cdSec': ITEMS['minor_healing_potion']['useCooldownMs'] / 1000}
CLASSES = ['rogue', 'mage', 'warrior', 'priest']
NAMES = {'rogue': 'Pícaro', 'mage': 'Mago', 'warrior': 'Guerrero', 'priest': 'Sacerdote'}
SOLO_KIT = {  # rotación de check.py (hechizos de la Fase 1), por prioridad
    'rogue': ['rogue_shadowstep', 'rogue_sinister_strike', 'rogue_gouge'],
    'mage': ['mage_flame_burst', 'mage_frostbolt', 'mage_fireball'],
    'warrior': ['warrior_charge', 'warrior_whirlwind', 'warrior_heroic_strike'],
    'priest': ['priest_holy_pulse', 'priest_smite']}
HEAL_KIT = ['priest_power_shield', 'priest_heal']
ZONES = [('Linde del Bosque (6–8)', ['forest_wolf', 'bandit_woodcutter', 'weaver_spider', 'old_bear']),
         ('Pantano (8–10)', ['giant_toad', 'lizardman', 'will_o_wisp', 'swamp_witch']),
         ('Cripta de Raíces', ['root_skeleton', 'moss_spirit', 'trap_plant', 'crypt_guardian'])]
NORMAL_BY_LEVEL = {6: 'forest_wolf', 7: 'bandit_woodcutter', 8: 'giant_toad', 9: 'lizardman', 10: 'root_skeleton'}


# ---------------------------------------------------------------- reglas de rules.json
def xp_of(mon):
    base = PROG['monsterXpPerLevel'] * mon['level'] + PROG['monsterXpBase']
    return round(base * PROG['monsterTypeMultiplier'][mon['type']]) * PROG['xpRate']

def level_mod(mon_level, ref_level):
    diff = mon_level - ref_level
    if diff <= PROG['levelDiffGreyAt']: return 0.0
    c = PROG['levelDiffClamp']
    return 1 + PROG['levelDiffModPerLevel'] * max(-c, min(c, diff))

def rank_mult(level):
    return 1 + sum(1 for l in PROG['spellRankLevels'] if level >= l) * PROG['spellRankBonusPct']

def ranked(level):
    """Copia de hechizos y auras con los rangos que el servidor aplica al `base` de los hechizos de clase."""
    m = rank_mult(level); SP = copy.deepcopy(SP0); AU = copy.deepcopy(AU0); done = set()
    for s in SP.values():
        if s['source'] != 'class': continue
        for e in s['effects']:
            if e['type'] in ('damage', 'heal') and 'base' in e: e['base'] *= m
            if e['type'] == 'apply_aura' and e['auraId'] not in done and 'base' in AU[e['auraId']]:
                AU[e['auraId']]['base'] *= m; done.add(e['auraId'])
    return SP, AU


# ---------------------------------------------------------------- personajes
def gear_factor(level):
    return 1.0 if TIER1_ONLY else 1 + 0.2 * max(0, level - 6)

def char(cls, level):
    f = gear_factor(level); ids = []
    for g in model.REF_GEAR[cls]:
        it = ITEMS[g]
        if it['levelReq'] > level: continue
        nid = f'{g}@{f:.2f}'
        if nid not in model.ITEMS:
            s = copy.deepcopy(it)
            for k in ('damageMin', 'damageMax', 'armor', 'spellPower'):
                if s.get(k): s[k] = s[k] * f
            if s.get('stats'): s['stats'] = {k: v * f for k, v in s['stats'].items()}
            model.ITEMS[nid] = s
        ids.append(nid)
    return Char(cls, level, ids)

def loadout(cls, level, kit, SP):
    """Hechizos de la rotación, como check.py: en solitario solo los que hacen daño; 'heal' = Escudo + Sanar (boss.py)."""
    if kit == 'heal': return [s for s in HEAL_KIT if SP[s]['levelReq'] <= level]
    if kit == 'rot': return [s for s in SOLO_KIT[cls] if SP[s]['levelReq'] <= level and any(e['type'] == 'damage' for e in SP[s]['effects'])]
    return []

_CURVES = {}
def curve(cls, level, mon, kit, ranks=True):
    """Daño (o cura) acumulado del jugador contra el monstruo, paso a paso (lista indexada por DT). kit: 'rot' | 'basic' | 'heal'."""
    key = (cls, level, mon['level'], mon['armor'], kit, ranks, TIER1_ONLY)
    if key in _CURVES: return _CURVES[key]
    SP, AU = ranked(level) if ranks else (SP0, AU0)
    ch = char(cls, level)
    old = dict(model.TARGET); model.TARGET.update({'level': mon['level'], 'armor': mon['armor']}); model.AOE_N = 0
    tr = []
    simulate(ch, loadout(cls, level, kit, SP), SP, AU, T=T_MAX + 1, heal_mode=(kit == 'heal'), trace=tr)
    model.TARGET.clear(); model.TARGET.update(old)
    _CURVES[key] = ([0.0] + [x[1] for x in tr], [ch.maxMana] + [x[2] for x in tr])
    return _CURVES[key]

def heal_cap(level, mon, ranks=True, window=60.0):
    """Cura por segundo del Sacerdote lanzando Escudo + Sanar sin parar durante `window` s (maná incluido), como boss.py."""
    h, _ = curve('priest', level, mon, 'heal', ranks)
    return h[int(window / DT)] / window

def heal_mana_per_hp(level, ranks=True):
    """Maná por punto curado con Escudo + Sanar (un Escudo y los Sanar que caben en su cooldown)."""
    SP, AU = ranked(level) if ranks else (SP0, AU0); ch = char('priest', level)
    heal, shield = SP['priest_heal'], SP['priest_power_shield']
    h = eff_amount(ch, heal, heal['effects'][0]); a = AU[shield['effects'][0]['auraId']]
    absorb = a['base'] + a.get('spCoef', 0) * ch.sp
    n = math.floor((shield['cooldownMs'] - CB['gcdMs']) / heal['castMs'])
    return (shield['cost']['amount'] + n * heal['cost']['amount']) / (absorb + n * h)


# ---------------------------------------------------------------- el monstruo contra el jugador
def hit_on(ch, mon, school):
    """Daño esperado por punto de daño bruto del monstruo sobre el jugador (tabla de impacto, crítico y mitigación)."""
    crit = 1 + CB['critBase'] * (CB['critMultiplier'] - 1)
    if school == 'physical':
        miss = CB['physicalMissBase'] + CB['physicalMissPerTargetLevel'] * max(0, ch.level - mon['level'])
        return (1 - miss) * (1 - ch.dodge) * crit * (1 - mitig(ch.armor, mon['level']))
    return (1 - CB['magicMissBase']) * crit

def is_melee(ch): return WEAPONS[ch.weapon['weaponType']]['rangeTiles'] <= 2

class Member:
    def __init__(s, cls, level, mon, kit='rot', ranks=True):
        s.cls = cls; s.ch = char(cls, level); s.mon = mon; s.kit = kit; s.ranks = ranks
        s.rot, s.mana = curve(cls, level, mon, kit, ranks)
        s.basic, _ = curve(cls, level, mon, 'basic', ranks)
        s.hp = s.ch.maxHp; s.lost_after = 0.0
        s.silenced = s.stunned = s.immune = s.busy = 0.0
        s.dots = []           # [expira, siguiente tick, por tick, cada]
        s.potion_at = -999.0
        s.approach = max(0.0, mon['attackRange'] - WEAPONS[s.ch.weapon['weaponType']]['rangeTiles']) / SPEED if is_melee(s.ch) else 0.0
        s.dealt = 0.0; s.healing = 0.0; s.lowest = s.hp
    def caster(s): return s.ch.resource == 'mana'

def fight(classes, level, mon, potions=False, heal_threshold=None, kit='rot', dodge=False, ranks=True):
    """Combate de un grupo contra un monstruo. Tanque = Guerrero, si no el primer cuerpo a cuerpo, si no el primero.
    El Sacerdote cura (Escudo + Sanar, heal_cap) a quien baje de heal_threshold hasta el 80 %; mientras cura no pega. Sus curas
    gastan maná (heal_mana_per_hp) de una reserva que se regenera con la penalización de castear; el maná de su rotación de
    daño va aparte (optimista para el Sacerdote)."""
    mem = [Member(c, level, mon, kit, ranks) for c in classes]
    tank = next((m for m in mem if m.cls == 'warrior'), next((m for m in mem if is_melee(m.ch)), mem[0]))
    others = [m for m in mem if m is not tank] or [tank]
    pri = next((m for m in mem if m.cls == 'priest'), None)
    hcap = heal_cap(level, mon, ranks) if (pri and heal_threshold is not None) else None
    if hcap:
        pmana = pri.ch.maxMana; mph = heal_mana_per_hp(level, ranks); pregen = pri.ch.manaRegen * CB['manaRegenCastingPenalty']
    oom_at = None
    healing_target = None
    mhp = mon['hp']; shield = 0.0; shield_until = 0.0; buff = (0.0, 0.0)
    cds = {s['spellId']: 0.0 for s in mon.get('spells', [])}
    casting = None; swing = 0.0; t = 0.0; i = 0
    swing_ms = mon['attackSpeedMs'] / 1000
    while mhp > 0 and t < T_MAX:
        if tank.hp <= 0:  # el tanque cae: el monstruo pasa al siguiente vivo
            tank = next((m for m in mem if m.hp > 0 and is_melee(m.ch)), next((m for m in mem if m.hp > 0), tank))
            others = [m for m in mem if m is not tank] or [tank]
        # --- jugadores
        for m in mem:
            if m.hp <= 0: continue
            if t < m.approach or t < m.stunned or t < m.busy: continue
            jj = min(max(0, int((t - m.approach) / DT)), len(m.rot) - 2)
            src = m.basic if (m.caster() and t < m.silenced) else m.rot
            d = src[jj + 1] - src[jj]
            if m is pri and healing_target is not None: d = 0.0
            if t < shield_until and shield > 0:
                a = min(shield, d); shield -= a; d -= a
            mhp -= d; m.dealt += d
        # --- curas del Sacerdote (histéresis: empieza bajo heal_threshold, para al 80 %)
        if hcap: pmana = min(pri.ch.maxMana, pmana + pregen * DT)
        if hcap and pri.hp > 0 and t >= pri.stunned and t >= pri.silenced:
            if healing_target is None:
                low = [m for m in mem if m.hp > 0 and m.hp < heal_threshold * m.ch.maxHp]
                if low: healing_target = min(low, key=lambda m: m.hp / m.ch.maxHp)
            if healing_target is not None:
                h = min(hcap * DT, max(0.0, pmana) / mph)
                if h < hcap * DT and oom_at is None: oom_at = t
                healing_target.hp = min(healing_target.ch.maxHp, healing_target.hp + h); pri.healing += h; pmana -= h * mph
                if healing_target.hp >= 0.8 * healing_target.ch.maxHp or healing_target.hp <= 0 or h <= 0: healing_target = None
        # --- pociones
        if potions:
            for m in mem:
                if 0 < m.hp < 0.4 * m.ch.maxHp and t - m.potion_at >= POTION['cdSec']:
                    m.hp = min(m.ch.maxHp, m.hp + POTION['heal']); m.potion_at = t
        # --- daño en el tiempo sobre los jugadores
        for m in mem:
            for d in m.dots:
                while d[1] <= d[0] + 1e-9 and d[1] <= t:
                    m.hp -= d[2]; d[1] += d[3]
            m.dots = [d for d in m.dots if d[1] <= d[0] + 1e-9]
        # --- monstruo
        mult = 1 + (buff[1] if t < buff[0] else 0)
        if casting:
            casting[1] -= DT
            if casting[1] <= 0:
                sp, tgt = casting[0], casting[2]
                cds[sp['id']] = sp['cooldownMs'] / 1000
                for victim, w in resolve_targets(sp, tgt, tank, others, mem, dodge, mon):
                    apply_effects(sp, victim, w, mon, mult, t)
                if tgt == 'self':
                    for e in sp['effects']:
                        if e['type'] == 'heal' and sp['targeting'] in ('self', 'self_aoe_allies'): mhp = min(mon['hp'], mhp + e['base'])
                        if e['type'] == 'apply_aura':
                            a = AU0[e['auraId']]
                            if a['kind'] == 'stat_mod' and a.get('mods', {}).get('damageDonePct'): buff = (t + a['durationMs'] / 1000, a['mods']['damageDonePct'])
                            if a['kind'] == 'shield': shield = a['base']; shield_until = t + a['durationMs'] / 1000
                casting = None
        else:
            for ms in mon.get('spells', []):
                sp = SP0[ms['spellId']]
                if cds[sp['id']] > 0: continue
                if ms.get('hpBelowPct', 1.0) < 1.0 and mhp / mon['hp'] > ms['hpBelowPct']: continue
                casting = [sp, sp['castMs'] / 1000, ms.get('target', 'current')]
                if sp['castMs'] == 0: casting[1] = 0
                if dodge:  # esquivar cuesta: el cuerpo a cuerpo sale del área y vuelve, sin pegar mientras tanto
                    for victim, _ in resolve_targets(sp, casting[2], tank, others, mem, False, mon):
                        if is_melee(victim.ch) and (sp['targeting'].startswith('ground_aoe') or sp['targeting'].startswith('self_aoe')):
                            victim.busy = t + sp['castMs'] / 1000 + DODGE_RETURN_SEC
                break
            if not casting and tank.hp > 0:
                swing += DT
                if swing >= swing_ms:
                    swing -= swing_ms
                    tank.hp -= (mon['damageMin'] + mon['damageMax']) / 2 * hit_on(tank.ch, mon, mon.get('school', 'physical')) * mult
        for k in cds: cds[k] = max(0.0, cds[k] - DT)
        for m in mem: m.lowest = min(m.lowest, m.hp)
        if all(m.hp <= 0 for m in mem): break
        t += DT; i += 1
    for m in mem:  # lo que queda de los DoT después de morir el monstruo
        for d in m.dots:
            n = max(0, math.floor((d[0] - d[1]) / d[3] + 1e-9) + 1)
            m.lost_after += n * d[2]
    return {'t': t, 'killed': mhp <= 0, 'members': mem, 'tank': tank, 'mhp': mhp, 'oom_at': oom_at}

def resolve_targets(sp, tgt, tank, others, mem, dodge, mon):
    """(víctima, peso) de un hechizo del monstruo. Áreas en el objetivo: solo él; áreas centradas en el monstruo: el cuerpo a
    cuerpo y, si el monstruo es cuerpo a cuerpo, su objetivo (lo tiene pegado); random_not_top_threat: reparte el valor
    esperado entre los que no son el tanque."""
    if sp['targeting'] in ('self', 'self_aoe_allies'): return []
    ground = sp['targeting'].startswith('ground_aoe') or sp['targeting'].startswith('self_aoe')
    if ground and dodge: return []
    if tgt == 'self':
        near = lambda m: is_melee(m.ch) or (m is tank and mon['attackRange'] <= 2)
        return [(m, 1.0) for m in mem if m.hp > 0 and near(m)]
    if tgt == 'random_not_top_threat':
        live = [m for m in others if m.hp > 0] or [tank]
        return [(m, 1.0 / len(live)) for m in live]
    return [(tank, 1.0)]

def p_hit(ch, mon, school):
    """Probabilidad de que el golpe entre (las auras de un hechizo que falla no se aplican, combat.md §Tabla de impacto)."""
    if school == 'physical':
        miss = CB['physicalMissBase'] + CB['physicalMissPerTargetLevel'] * max(0, ch.level - mon['level'])
        return (1 - miss) * (1 - ch.dodge)
    return 1 - CB['magicMissBase']

def apply_effects(sp, m, w, mon, mult, t):
    ph = p_hit(m.ch, mon, sp['school'])
    for e in sp['effects']:
        if e['type'] == 'damage':
            m.hp -= e['base'] * hit_on(m.ch, mon, sp['school']) * mult * w
        if e['type'] == 'apply_aura':
            a = AU0[e['auraId']]; dur = a['durationMs'] / 1000
            if a['kind'] == 'dot':
                per = a['base'] * w * ph * ((1 - mitig(m.ch.armor, mon['level'])) if a.get('school') == 'physical' else 1)
                old = next((d for d in m.dots if d[4] == a['id']), None)
                if old: old[0] = t + dur; old[2] = per
                else: m.dots.append([t + dur, t + a['tickMs'] / 1000, per, a['tickMs'] / 1000, a['id']])
            if a['kind'] in CB['hardControlKinds'] and w >= 0.5 and t >= m.immune:
                end = t + dur
                if a['kind'] == 'stun': m.stunned = max(m.stunned, end)
                if a['kind'] == 'silence': m.silenced = max(m.silenced, end)
                m.immune = end + CB['hardControlImmunitySec']


# ---------------------------------------------------------------- descanso, ciclo y XP por hora (como check.py)
def mana_used_check(ch, lo, t, SP):
    """El maná gastado en la pelea con la aproximación de check.py (_mana_used): la escala de la Fase 1. Supone que el caster
    alterna básicos; el maná exacto de la rotación más codiciosa (model.simulate) sale más alto (balance-report.md §Fase 2)."""
    used = 0
    for sid in lo:
        s = SP[sid]; c = s.get('cost', {}).get('amount', 0)
        period = max(s['cooldownMs'] / 1000, s['castMs'] / 1000, 1.0)
        used += c * max(1, int(t / period)) if s['cooldownMs'] > 0 else c * (t / max(s['castMs'] / 1000, 1.0)) * 0.5
    gained = t / ch.swing * ch.maxMana * CB['manaPerBasicHitPctPerSec'] * ch.swing * 0.5
    return max(0, used - gained)

def rest_after(m, fight_t):
    ch = m.ch; lost = ch.maxHp - m.hp + m.lost_after
    mana_used = 0.0
    SP, _ = ranked(ch.level) if m.ranks else (SP0, AU0)
    if ch.resource == 'mana':
        mana_used = mana_used_check(ch, loadout(m.cls, ch.level, m.kit, SP), fight_t, SP)
    if m.cls == 'priest':
        h = eff_amount(ch, SP['priest_heal'], SP['priest_heal']['effects'][0]); casts = math.ceil(max(0, lost) / h)
        heal_t = casts * SP['priest_heal']['castMs'] / 1000
        return lost, heal_t + max(0, (casts * SP['priest_heal']['cost']['amount'] + mana_used) / ch.manaRegen - heal_t)
    rest = (CB['hpRegenDelaySec'] + lost / ch.hpRegen) if lost > 0 else 0
    if ch.resource == 'mana' and mana_used > 0: rest = max(rest, CB['hpRegenDelaySec'] + mana_used / ch.manaRegen)
    return lost, rest

def solo(cls, level, mon, kit='rot', dodge=False, ranks=True):
    r = fight([cls], level, mon, kit=kit, dodge=dodge, ranks=ranks); m = r['members'][0]
    lost, rest = rest_after(m, r['t'])
    cyc = r['t'] + rest + WALK_SEC
    xph = 3600 / cyc * xp_of(mon) * level_mod(mon['level'], level)
    return {'t': r['t'], 'lost': lost, 'pct': 100 * lost / m.ch.maxHp, 'rest': rest, 'cycle': cyc, 'xph': xph, 'killed': r['killed']}


# ---------------------------------------------------------------- informes
def fmt(x, d=0): return f'{x:.{d}f}'.replace('.', ',')

def table_stats():
    print('\n### Stats por clase y nivel (equipo ' + ('del Tier 1' if TIER1_ONLY else 'aproximado, gear_factor') + ', rangos incluidos)')
    print('| Nv | Equipo ×| ' + ' | '.join(f'{NAMES[c]}: vida · AP/SP · armadura' for c in CLASSES) + ' |')
    print('|---|---|' + '---|' * len(CLASSES))
    for lv in range(6, 12):
        cells = []
        for c in CLASSES:
            ch = char(c, lv); cells.append(f'{ch.maxHp:.0f} · {ch.ap:.0f}/{ch.sp:.0f} · {ch.armor:.0f}')
        print(f'| {lv} | {fmt(gear_factor(lv), 1)} | ' + ' | '.join(cells) + ' |')

def star(p, limit=50): return f'**{fmt(p)} %**' if p > limit else f'{fmt(p)} %'

def table_solo(ids, title, ranks=True):
    print(f'\n### {title}: rotación / solo básicos (tiempo en matarlo · vida perdida; en negrita, por encima del 50 %)')
    print('| Monstruo | Nv | Tipo | XP | ' + ' | '.join(NAMES[c] for c in CLASSES) + ' |')
    print('|---|---|---|---|' + '---|' * len(CLASSES))
    for mid in ids:
        mon = MONS[mid]; cells = []
        for c in CLASSES:
            r = solo(c, mon['level'], mon, ranks=ranks); b = solo(c, mon['level'], mon, kit='basic', ranks=ranks)
            cells.append(f"{fmt(r['t'])} s · {fmt(r['pct'])} % / {fmt(b['t'])} s · {star(b['pct'])}")
        print(f"| {mon['name']} | {mon['level']} | {mon['type']} | {xp_of(mon):.0f} | " + ' | '.join(cells) + ' |')

def table_cycle(ids, title, ranks=True):
    print(f'\n### {title}: ciclo por monstruo (pelea + descanso + {WALK_SEC:.0f} s) · XP por hora en solitario')
    print('| Monstruo | Nv | ' + ' | '.join(NAMES[c] for c in CLASSES) + ' | Dif. XP/h | Ciclo máx. |')
    print('|---|---|' + '---|' * len(CLASSES) + '---|---|')
    worst = 0.0; slowest = 0.0
    for mid in ids:
        mon = MONS[mid]; res = {c: solo(c, mon['level'], mon, ranks=ranks) for c in CLASSES}
        best = max(r['xph'] for r in res.values()); spread = 100 * (1 - min(r['xph'] for r in res.values()) / best)
        cmax = max(r['cycle'] for r in res.values())
        worst = max(worst, spread); slowest = max(slowest, cmax)
        print(f"| {mon['name']} | {mon['level']} | " + ' | '.join(f"{fmt(res[c]['cycle'])} s · {fmt(res[c]['xph'])}" for c in CLASSES)
              + f" | {fmt(spread)} % | {fmt(cmax)} s |")
    return worst, slowest

def elite_report(mid):
    mon = MONS[mid]; lv = mon['level']
    print(f"\n#### {mon['name']} (nv {lv}, {mon['hp']} de vida, armadura {mon['armor']}, XP {xp_of(mon):.0f})")
    print(f'Cura del Sacerdote de nivel {lv} (Escudo + Sanar, 60 s): {fmt(heal_cap(lv, mon), 1)} por segundo.\n')
    print('| Solo a nivel ' + str(lv) + ' | Como la Fase 1 (rotación, sin curas ni pociones) | Con pociones y, el Sacerdote, curándose | Lo mismo esquivando todas las áreas marcadas |')
    print('|---|---|---|---|')
    def outcome(r, m):
        left = f"le queda el {fmt(100 * max(0, r['mhp']) / mon['hp'])} %"
        if r['killed'] and m.hp > 0: res = f"**lo mata** en {fmt(r['t'])} s"
        elif not r['killed'] and m.hp > 0: res = f"no lo mata en {fmt(T_MAX)} s ({left})"
        else: res = f"muere a los {fmt(r['t'])} s ({left})"
        if r['oom_at'] is not None: res += f", sin maná para curarse a los {fmt(r['oom_at'])} s"
        return res
    for c in CLASSES:
        r = fight([c], lv, mon); m = r['members'][0]
        rp = fight([c], lv, mon, potions=True, heal_threshold=0.5); mp = rp['members'][0]
        rd = fight([c], lv, mon, potions=True, heal_threshold=0.5, dodge=True); md = rd['members'][0]
        lost = 100 * (m.ch.maxHp - m.hp) / m.ch.maxHp
        print(f"| {NAMES[c]} | {fmt(r['t'])} s · {star(lost, 100)} | {outcome(rp, mp)} | {outcome(rd, md)} |")
    print('\n| Grupo de nivel ' + str(lv) + ' | Duración | Lo más bajo que llega cada uno (vida perdida) | Resultado |')
    print('|---|---|---|---|')
    pairs = [('warrior', 'rogue'), ('warrior', 'mage'), ('warrior', 'priest'), ('rogue', 'mage'), ('rogue', 'priest'), ('mage', 'priest')]
    trios = [('warrior', 'mage', 'priest'), ('warrior', 'rogue', 'priest'), ('warrior', 'rogue', 'mage'), ('rogue', 'mage', 'priest')]
    for grp, glv in [(g, lv) for g in pairs + trios] + [(t, lv - 1) for t in trios[:2]]:
        r = fight(list(grp), glv, mon, potions=True, heal_threshold=0.6)
        parts = [f"{NAMES[m.cls]} {fmt(100 * (m.ch.maxHp - m.lowest) / m.ch.maxHp)} %" + (' (muere)' if m.hp <= 0 else '') for m in r['members']]
        dead = sum(1 for m in r['members'] if m.hp <= 0)
        res = 'lo matan' if r['killed'] and not dead else ('lo matan con una baja' if r['killed'] else 'no lo matan')
        label = ' + '.join(NAMES[c] for c in grp) + (f' (nv {glv})' if glv != lv else '')
        print(f"| {label} | {fmt(r['t'])} s | {', '.join(parts)} | {res} |")

if __name__ == '__main__':
    quick = '--quick' in sys.argv
    print('# Tier 2 (HU-109): equipo', 'del Tier 1 (sin equipo nuevo)' if TIER1_ONLY else 'aproximado (gear_factor = 1 + 0,2 · (nivel − 6))')
    table_stats()
    print('\n## Referencia: Kóbold minero (nv 5) con los supuestos de check.py (sin rangos)')
    table_solo(['kobold_miner'], 'Kóbold minero', ranks=False); table_cycle(['kobold_miner'], 'Kóbold minero', ranks=False)
    worst = 0.0; slowest = 0.0
    for name, ids in ZONES:
        camp = [i for i in ids if MONS[i]['type'] != 'elite']
        table_solo(camp, name); w, s = table_cycle(camp, name); worst = max(worst, w); slowest = max(slowest, s)
    print(f"\nMayor diferencia de XP por hora entre clases: {fmt(worst)} % (objetivo ≤ {fmt(100 * R['balanceTargets']['soloXpPerHourSpreadPct'])} %)."
          f" Ciclo más lento: {fmt(slowest)} s (killCycleSecTarget = {PROG['killCycleSecTarget']} s).")
    # Horas del 6 al 10: los minutos de la curva escalados por el ciclo medido contra el normal de cada nivel (GDD: 7,2 h a 36 s).
    hours = {}
    for c in CLASSES:
        hours[c] = sum(PROG['minutesPerLevel'][lv - 1] / 60 * solo(c, lv, MONS[NORMAL_BY_LEVEL[lv]])['cycle'] / PROG['killCycleSecTarget']
                       for lv in range(6, 10))
    ref = sum(PROG['minutesPerLevel'][lv - 1] for lv in range(6, 10)) / 60
    print(f"Horas del nivel 6 al 10 contra el normal de cada nivel (la curva da {fmt(ref, 1)} h a {PROG['killCycleSecTarget']} s): "
          + ', '.join(f"{NAMES[c]} {fmt(h, 1)} h" for c, h in hours.items()) + '.')
    if quick: sys.exit(0)
    print('\n## Élites: solo y en grupo de su nivel (pociones de 60 cada 60 s bajo el 40 %; el Sacerdote cura bajo el 50 % solo y bajo el 60 % en grupo)')
    for name, ids in ZONES:
        for i in ids:
            if MONS[i]['type'] == 'elite': elite_report(i)

