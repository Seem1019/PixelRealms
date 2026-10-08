"""Jefes: Capataz Grask (HU-083) y Árbol Podrido (HU-117). Valores esperados, sin azar.

Uso: python tools/balance/boss.py                  (los dos jefes)
     python tools/balance/boss.py 1400             (el Capataz con otra vida)
     python tools/balance/boss.py --arbol          (solo el Árbol Podrido)
     python tools/balance/boss.py --arbol --hp 3200 --dmg 1.2 --spores 15 --sapling-hp 120
                                                   (el Árbol con otra vida, su básico ×1,2, otro daño por pulso de las Esporas u
                                                   otra vida de los retoños, en memoria; no toca content/)

Árbol Podrido (docs/design/balance-report.md §HU-117). Supuestos:
- Grupo de referencia de rules.boss: 3 jugadores de nivel B − 2 = 9 con el equipo «esperado» de gear.py (HU-110: arma, pecho,
  piernas y cuello en verde y el resto en blanco del nivel 9); «recién llegado» y «completo» como sensibilidad. Rangos de hechizo
  como el servidor y los hechizos de nivel 7 y 9 (HU-106) en la barra; sin las mejoras de HU-107 (conservador).
- Barras (4 hechizos, por prioridad): las de ARBOL_KITS. El Guerrero tanquea con Bloqueo con escudo: su reducción media
  (−50 % durante 5 s cada 12 s) baja el daño del básico que recibe.
- El jefe pega a su objetivo (el tanque: el Guerrero si lo hay, si no el primer cuerpo a cuerpo) y no pega mientras castea.
  Raíces y Esporas van a quien no es su objetivo (`random_not_top_threat`): en el modelo, por turnos entre los vivos.
  Bajo el 50 % castea Retoños (2, tope rules.limits.maxSummonsPerCaster) y los retoños van a por quien menos amenaza tiene
  (daño hecho + 0,5 · curado; casi siempre el sanador): tardan en llegar 6 casillas a su velocidad.
- «Esquivando»: nadie se queda en una marca; esquivar le cuesta al cuerpo a cuerpo el casteo + 0,5 s sin pegar y al que pega a
  distancia 0,5 s. «Sin esquivar»: Raíces hace su daño y enraíza 3 s; de las Esporas se sale al primer pulso (o al soltarse de la
  raíz), y salir cuesta 1 s sin pegar al cuerpo a cuerpo y 0,5 s al que pega a distancia.
- Retoños: «a por ellos» = todos menos el sanador mientras cura pegan a los retoños hasta matarlos; «ignorados» = nadie les pega.
- Sanador: el Sacerdote cura (Escudo + Sanar, su capacidad medida en 60 s, tier2.heal_cap) a quien baja del 60 % hasta el 80 %,
  sin pegar mientras cura, y sus curas gastan maná. Pociones mayores de vida (100, cada 60 s) bajo el 40 %.
"""
import sys,os; sys.path.insert(0,os.path.dirname(__file__))
import model
from model import *
SP,AU=load_content()
BOSS={'level':6,'armor':90}; HP=2000
UNL={1:0,2:1,3:2,5:3}
KITS={'warrior':['warrior_whirlwind','warrior_heroic_strike'],'mage':['mage_flame_burst','mage_frostbolt','mage_fireball'],
      'rogue':['rogue_shadowstep','rogue_sinister_strike'],'priest':['priest_smite']}
def lvl_ok(sid,l): return SP[sid]['levelReq']<=l
def gear(cls,l): return [g for g in model.REF_GEAR[cls] if ITEMS[g]['levelReq']<=l]
def dps(cls,l,T=80):
    model.TARGET.update(BOSS); model.AOE_N=0
    ch=Char(cls,l,gear(cls,l)); lo=[s for s in KITS[cls] if lvl_ok(s,l)]
    m,_=simulate(ch,lo,SP,AU,T=T); return m/T, ch
def hps(l,T=80):
    ch=Char('priest',l,gear('priest',l)); lo=[s for s in ['priest_power_shield','priest_heal'] if lvl_ok(s,l)]
    m,_=simulate(ch,lo,SP,AU,T=T,heal_mode=True); return m/T
def boss_on(ch):
    return 22/2.4*(1-CB['physicalMissBase'])*(1-ch.dodge)*(1-mitig(ch.armor,6))*1.12  # 1.12 = ¡A trabajar! la mitad del combate
def capataz(HP):
    print('## Capataz Grask (armadura 90)')
    print('vida del jefe',HP)
    for name,grp in [('3 × nv 4: Guerrero, Mago, Sacerdote',[('warrior',4),('mage',4),('priest',4)]),
                     ('3 × nv 4 sin sanador: Guerrero, Pícaro, Mago',[('warrior',4),('rogue',4),('mage',4)]),
                     ('2 × nv 6: Pícaro, Mago',[('rogue',6),('mage',6)]),
                     ('2 × nv 6: Guerrero, Sacerdote',[('warrior',6),('priest',6)])]:
        tot=0; tank=None; heal=0; parts=[]; pr=None
        for cls,l in grp:
            d,ch=dps(cls,l)
            if tank is None and cls in('warrior','rogue'): tank=ch
            if cls=='priest': pr=(d,l); continue
            tot+=d; parts.append(f"{cls} {d:.1f}")
        intake=boss_on(tank); whip=(10+24)/8; need=intake+whip*0.5
        if pr:
            cap=hps(pr[1]); f=min(1,need/cap); heal=cap*f; tot+=pr[0]*(1-f); parts.append(f"priest {pr[0]*(1-f):.1f} (cura el {100*f:.0f} % del tiempo)")
        dur=HP/tot
        pots=0 if pr else 60/60   # una poción menor de vida por minuto
        net=need-heal-pots
        surv=tank.maxHp/net if net>0 else float('inf')
        print(f"- {name}: DPS {tot:.1f} ({', '.join(parts)}) → {dur:.0f} s | el tanque ({tank.cls}, {tank.maxHp:.0f} vida) recibe {intake:.1f}/s"
              +(f", el Sacerdote cura {heal:.1f}/s" if heal else '')+(f" | aguanta {surv:.0f} s sin pociones" if surv!=float('inf') else ' | aguanta todo el combate'))


# ---------------------------------------------------------------- Árbol Podrido (HU-117)
ARBOL_KITS = {  # barras de 4 al nivel 9, por prioridad (phase2.FARM_KITS; el Guerrero con Bloqueo para tanquear)
    'warrior': ['warrior_shield_block', 'warrior_whirlwind', 'warrior_cleave', 'warrior_heroic_strike'],
    'rogue': ['rogue_shadowstep', 'rogue_eviscerate', 'rogue_sinister_strike', 'rogue_gouge'],
    'mage': ['mage_flame_burst', 'mage_frostbolt', 'mage_burning_field', 'mage_fireball'],
    'priest': ['priest_holy_pulse', 'priest_smite']}
ARBOL_DT = 0.05
ARBOL_T_MAX = 400.0
SAPLING_WALK_TILES = 6.0          # de junto al árbol hasta el sanador
DODGE_MELEE_EXTRA_SEC = 0.5       # el cuerpo a cuerpo sale de la marca durante el casteo y vuelve
DODGE_RANGED_SEC = 0.5            # quien pega a distancia sigue casteando mientras anda (×castMoveSpeedMult)
LEAVE_MELEE_SEC, LEAVE_RANGED_SEC = 1.0, 0.5   # salir de unas Esporas ya puestas


def _arg(name, default, cast=float):
    return cast(sys.argv[sys.argv.index(name) + 1]) if name in sys.argv else default


class Arbol:
    """El Árbol Podrido, sus hechizos y sus retoños de content/, con los cambios en memoria de la línea de órdenes."""
    def __init__(s):
        import tier2
        s.t2 = tier2
        s.boss = dict(tier2.MONS['rotten_tree']); s.sap = dict(tier2.MONS['rotten_sapling'])
        s.boss['hp'] = _arg('--hp', s.boss['hp'], int)
        f = _arg('--dmg', 1.0)
        s.boss['damageMin'] = round(s.boss['damageMin'] * f); s.boss['damageMax'] = round(s.boss['damageMax'] * f)
        s.sap['hp'] = _arg('--sapling-hp', s.sap['hp'], int)
        s.spells = {ms['spellId']: dict(tier2.SP0[ms['spellId']], _ms=ms) for ms in s.boss['spells']}
        s.spores = s.spells['rotten_tree_spores']
        s.spore_dmg = _arg('--spores', s.spores['effects'][0]['base'])
        s.roots = s.spells['rotten_tree_roots']
        s.root_aura = tier2.AU0[next(e['auraId'] for e in s.roots['effects'] if e['type'] == 'apply_aura')]
        s.summon = next(e for e in s.spells['rotten_tree_saplings']['effects'] if e['type'] == 'summon')

    def curve(s, cls, level):
        """Daño acumulado y maná, paso a paso, de la barra de la clase contra el jefe (model.simulate con rangos)."""
        SP_, AU_ = s.t2.ranked(level); ch = s.t2.char(cls, level)
        old = dict(model.TARGET); model.TARGET.update({'level': s.boss['level'], 'armor': s.boss['armor']}); model.AOE_N = 0
        tr = []
        simulate(ch, [x for x in ARBOL_KITS[cls] if SP_[x]['levelReq'] <= level], SP_, AU_, T=ARBOL_T_MAX + 1, trace=tr)
        model.TARGET.clear(); model.TARGET.update(old)
        return ch, [0.0] + [x[1] for x in tr], [ch.maxMana] + [x[2] for x in tr]

    def block(s, level):
        """Reducción media del daño recibido con Bloqueo con escudo (tiempo activo / recarga)."""
        SP_, AU_ = s.t2.ranked(level); sb = SP_['warrior_shield_block']; a = AU_[sb['effects'][0]['auraId']]
        return -a['mods']['damageTakenPct'] * min(1.0, a['durationMs'] / sb['cooldownMs'])

    def basic_on(s, ch, block=0.0):
        """Daño por segundo del básico del jefe sobre `ch` (tabla de impacto, crítico y mitigación), sin contar sus casteos."""
        return (s.boss['damageMin'] + s.boss['damageMax']) / 2 / (s.boss['attackSpeedMs'] / 1000) * s.t2.hit_on(ch, s.boss, 'physical') * (1 - block)

    def fight(s, members, dodge=True, saplings='kill', heal_threshold=0.6, potions=True):
        """members: [(clase, nivel)]. Devuelve la duración, si muere el jefe y, por jugador, lo más bajo que llega, el maná, etc."""
        t2 = s.t2; boss = s.boss; DT = ARBOL_DT
        mem = []
        for cls, lv in members:
            ch, dmg, mana = s.curve(cls, lv)
            mem.append({'cls': cls, 'lv': lv, 'ch': ch, 'dmg': dmg, 'mana': mana, 'hp': ch.maxHp, 'lowest': ch.maxHp, 'busy': 0.0,
                        'rooted': 0.0, 'immune': 0.0, 'potion': -999.0, 'dealt': 0.0, 'healed': 0.0, 'taken': {}, 'melee': t2.is_melee(ch)})
        def pick_tank():
            alive = [m for m in mem if m['hp'] > 0]
            return next((m for m in alive if m['cls'] == 'warrior'), next((m for m in alive if m['melee']), alive[0] if alive else mem[0]))
        tank = pick_tank()
        pri = next((m for m in mem if m['cls'] == 'priest'), None)
        if pri:
            hcap = t2.heal_cap(pri['lv'], boss); mph = t2.heal_mana_per_hp(pri['lv'])
            pmana = pri['ch'].maxMana; pregen = pri['ch'].manaRegen * CB['manaRegenCastingPenalty']
        block = {m['lv']: s.block(m['lv']) for m in mem if m['cls'] == 'warrior'}
        pulse = R['limits']['persistentAreaTickMs'] / 1000; swing_s = boss['attackSpeedMs'] / 1000
        hp = boss['hp']; t = 0.0; i = 0; swing = 0.0; casting = None; turn = 0
        cds = {sid: 0.0 for sid in s.spells}
        areas = []    # [caduca, siguiente pulso, víctima, sale a los]
        saps = []     # [vida, llega a los, swing]
        healing = None; heal_time = 0.0; oom = None; sap_time = 0.0; summoned = 0
        def take(m, kind, v):
            m['hp'] -= v; m['taken'][kind] = m['taken'].get(kind, 0.0) + v
        while hp > 0 and t < ARBOL_T_MAX:
            if tank['hp'] <= 0: tank = pick_tank()
            alive_saps = [x for x in saps if x[0] > 0]
            if alive_saps: sap_time += DT
            # --- jugadores
            for m in mem:
                if m['hp'] <= 0 or t < m['busy']: continue
                j = min(i, len(m['dmg']) - 2)
                d = m['dmg'][j + 1] - m['dmg'][j]
                if m is pri and healing is not None: d = 0.0
                if alive_saps and saplings == 'kill':
                    alive_saps[0][0] -= d
                    alive_saps = [x for x in saps if x[0] > 0]
                    continue
                hp -= d; m['dealt'] += d
            # --- curas del Sacerdote (histéresis: empieza bajo heal_threshold, para al 80 %)
            if pri:
                pmana = min(pri['ch'].maxMana, pmana + pregen * DT)
                if pri['hp'] > 0:
                    if healing is None:
                        low = [m for m in mem if 0 < m['hp'] < heal_threshold * m['ch'].maxHp]
                        if low: healing = min(low, key=lambda m: m['hp'] / m['ch'].maxHp)
                    if healing is not None:
                        h = min(hcap * DT, max(0.0, pmana) / mph)
                        if h < hcap * DT and oom is None: oom = t
                        healing['hp'] = min(healing['ch'].maxHp, healing['hp'] + h); pri['healed'] += h; pmana -= h * mph; heal_time += DT
                        if healing['hp'] >= 0.8 * healing['ch'].maxHp or healing['hp'] <= 0 or h <= 0: healing = None
            if potions:
                for m in mem:
                    if 0 < m['hp'] < 0.4 * m['ch'].maxHp and t - m['potion'] >= t2.POTION['cdSec']:
                        m['hp'] = min(m['ch'].maxHp, m['hp'] + t2.POTION['heal']); m['potion'] = t
            # --- Esporas en el suelo
            for a in areas:
                while a[1] <= a[0] + 1e-9 and a[1] <= t:
                    v = a[2]
                    if v['hp'] > 0 and a[1] < a[3]: take(v, 'Esporas', s.spore_dmg * t2.hit_on(v['ch'], boss, s.spores['school']))
                    a[1] += pulse
            areas = [a for a in areas if t < a[0]]
            # --- retoños: a por quien menos amenaza tiene
            for x in saps:
                if x[0] <= 0 or t < x[1]: continue
                x[2] += DT
                if x[2] >= s.sap['attackSpeedMs'] / 1000:
                    x[2] -= s.sap['attackSpeedMs'] / 1000
                    cands = [m for m in mem if m['hp'] > 0 and m is not tank] or [tank]
                    tg = min(cands, key=lambda m: m['dealt'] + CB['threatPerHeal'] * m['healed'])
                    take(tg, 'retoños', (s.sap['damageMin'] + s.sap['damageMax']) / 2 * t2.hit_on(tg['ch'], s.sap, 'physical'))
            # --- jefe
            others = [m for m in mem if m is not tank and m['hp'] > 0] or [tank]
            if casting:
                casting[1] -= DT
                if casting[1] <= 0:
                    sp, v = casting[0], casting[2]
                    cds[sp['id']] = sp['cooldownMs'] / 1000
                    if sp is s.spells['rotten_tree_saplings']:
                        n = min(s.summon['count'], R['limits']['maxSummonsPerCaster'] - len([x for x in saps if x[0] > 0]))
                        saps += [[s.sap['hp'], t + SAPLING_WALK_TILES / s.sap['speed'], 0.0] for _ in range(n)]; summoned += n
                    elif sp is s.roots and not dodge:
                        take(v, 'Raíces', s.roots['effects'][0]['base'] * t2.hit_on(v['ch'], boss, s.roots['school']))
                        if t >= v['immune']:
                            v['rooted'] = t + s.root_aura['durationMs'] / 1000; v['immune'] = v['rooted'] + CB['hardControlImmunitySec']
                    elif sp is s.spores and not dodge:
                        # Sale tras el primer pulso o al soltarse de la raíz (enraizado sigue pegando); andar le cuesta LEAVE_*.
                        areas.append([t + s.spores['areaDurationMs'] / 1000, t + pulse, v, max(t + pulse, v['rooted']) + pulse / 5])
                        v['busy'] = max(v['busy'], t + (LEAVE_MELEE_SEC if v['melee'] else LEAVE_RANGED_SEC))
                    casting = None
            else:
                for sid, sp in s.spells.items():
                    ms = sp['_ms']
                    if cds[sid] > 0 or (ms.get('hpBelowPct', 1.0) < 1.0 and hp / boss['hp'] > ms['hpBelowPct']): continue
                    v = tank if ms.get('target') == 'self' else others[turn % len(others)]
                    if ms.get('target') != 'self': turn += 1
                    casting = [sp, sp['castMs'] / 1000, v]
                    if dodge and sp['targeting'].startswith('ground'):
                        v['busy'] = max(v['busy'], t + (sp['castMs'] / 1000 + DODGE_MELEE_EXTRA_SEC if v['melee'] else DODGE_RANGED_SEC))
                    break
                if not casting and tank['hp'] > 0:
                    swing += DT
                    if swing >= swing_s:
                        swing -= swing_s
                        take(tank, 'básico', s.basic_on(tank['ch'], block.get(tank['lv'], 0.0) if tank['cls'] == 'warrior' else 0.0) * swing_s)
            for k in cds: cds[k] = max(0.0, cds[k] - DT)
            for m in mem: m['lowest'] = min(m['lowest'], m['hp'])
            if all(m['hp'] <= 0 for m in mem): break
            t += DT; i += 1
        for m in mem: m['mana_end'] = m['mana'][min(i, len(m['mana']) - 1)]
        return {'t': t, 'killed': hp <= 0, 'left': max(0.0, hp), 'mem': mem, 'tank': tank, 'oom': oom, 'summoned': summoned,
                'sap_time': sap_time, 'heal_pct': 100 * heal_time / max(t, DT), 'hcap': hcap if pri else None}


def _fmt(x, d=0): return f'{x:.{d}f}'.replace('.', ',')


def arbol():
    import tier2, gear as hu110
    names = tier2.NAMES; a = Arbol(); b = a.boss; pot = hu110.set_potion()
    rb = R['boss']; lv = b['level'] + rb['referenceLevelOffset']; lo, hi = rb['targetDurationSec']
    print(f"\n## Árbol Podrido (nivel {b['level']}, {b['hp']} de vida, armadura {b['armor']}; básico {b['damageMin']}–{b['damageMax']} cada "
          f"{_fmt(b['attackSpeedMs'] / 1000, 1)} s a {_fmt(b['attackRange'])} casillas; Esporas {_fmt(a.spore_dmg)} por pulso; retoños de "
          f"{a.sap['hp']} de vida y {a.sap['damageMin']}–{a.sap['damageMax']} cada {_fmt(a.sap['attackSpeedMs'] / 1000, 1)} s)")
    print(f'Pociones: {pot}. Objetivo (rules.boss): {rb["referencePartySize"]} de nivel {lv} en {lo}–{hi} s; nadie lo mata solo.')
    hu110.use('esperado')
    w9, r11 = tier2.char('warrior', lv), tier2.char('rogue', b['level'])
    print(f"\nBásico sin curas ni casteos: el Guerrero de nivel {lv} ({w9.maxHp:.0f} de vida) cae en {_fmt(w9.maxHp / a.basic_on(w9))} s "
          f"({_fmt(w9.maxHp / a.basic_on(w9, a.block(lv)))} s con Bloqueo con escudo); un Pícaro de nivel {b['level']} ({r11.maxHp:.0f}) en "
          f"{_fmt(r11.maxHp / a.basic_on(r11))} s.")
    trios = [('warrior', 'mage', 'priest'), ('warrior', 'rogue', 'priest'), ('warrior', 'rogue', 'mage'), ('rogue', 'mage', 'priest')]
    LABEL = {'llegando': 'recién llegado', 'esperado': 'esperado', 'completo': 'completo'}
    def row(r):
        parts = []
        for m in r['mem']:
            p = f"{names[m['cls']]} {_fmt(100 * max(0, m['lowest']) / m['ch'].maxHp)} %" + (' (muere)' if m['hp'] <= 0 else '')
            if m['ch'].maxMana: p += f", maná {_fmt(m['mana_end'])}/{_fmt(m['ch'].maxMana)}"
            parts.append(p)
        res = 'lo matan' if r['killed'] and all(m['hp'] > 0 for m in r['mem']) else ('lo matan con baja' if r['killed'] else
              f"no lo matan (le queda el {_fmt(100 * r['left'] / b['hp'])} %)")
        extra = f"{_fmt(r['heal_pct'])} %" if r['hcap'] else '—'
        return f"{_fmt(r['t'])} s | {'; '.join(parts)} | {extra} | {r['summoned']} · {_fmt(r['sap_time'], 1)} s | {res}"
    print(f'\n### Grupos de 3 de nivel {lv} (retoños a por ellos)')
    print('Vida más baja de cada uno y maná al final · % del combate que el Sacerdote cura · retoños invocados · segundos con retoños vivos.\n')
    print('| Equipo | Grupo | Marcas | Duración | Vida más baja (maná al final) | Cura | Retoños | Resultado |')
    print('|---|---|---|---|---|---|---|---|')
    summary = {}
    for sc in ('esperado', 'llegando', 'completo'):
        hu110.use(sc)
        for g in trios:
            for dodge in (True, False):
                r = a.fight([(c, lv) for c in g], dodge=dodge)
                summary.setdefault(sc, []).append(r['t'])
                print(f"| {LABEL[sc]} | {' + '.join(names[c] for c in g)} | {'esquivando' if dodge else 'sin esquivar'} | {row(r)} |")
    for sc, ts in summary.items():
        print(f"\n{LABEL[sc].capitalize()}: {_fmt(min(ts))}–{_fmt(max(ts))} s (objetivo {lo}–{hi} s).", end='')
    print()
    hu110.use('esperado')
    print(f'\n### Retoños ignorados (nivel {lv}, equipo esperado, esquivando las marcas)\n')
    print('| Grupo | Duración | Vida más baja (maná al final) | Cura | Retoños | Resultado |')
    print('|---|---|---|---|---|---|')
    for g in trios:
        r = a.fight([(c, lv) for c in g], saplings='ignore')
        print(f"| {' + '.join(names[c] for c in g)} | {row(r)} |")
    print(f'\n### Quedarse en las Esporas\nCada pulso ({_fmt(R["limits"]["persistentAreaTickMs"])} ms) hace {_fmt(a.spore_dmg)} mágico; el área dura '
          f'{_fmt(a.spores["areaDurationMs"] / 1000)} s: quedarse entera son {_fmt(a.spore_dmg * a.spores["areaDurationMs"] / R["limits"]["persistentAreaTickMs"])} '
          f'de daño (' + ', '.join(f"{names[c]} {_fmt(100 * a.spore_dmg * a.spores['areaDurationMs'] / R['limits']['persistentAreaTickMs'] * tier2.hit_on(tier2.char(c, lv), b, 'magic') / tier2.char(c, lv).maxHp)} %"
                                   for c in tier2.CLASSES) + f' de la vida al nivel {lv}).')
    cap = R['progression']['levelCapByPhase'][1]
    print(f'\n### Solo (nivel {cap}, tope de la Fase 2, con pociones; el Sacerdote se cura bajo el 50 %)\n')
    print('| Equipo | ' + ' | '.join(names[c] for c in tier2.CLASSES) + ' |')
    print('|---|' + '---|' * len(tier2.CLASSES))
    for sc in ('esperado', 'completo'):
        hu110.use(sc); cells = []
        for c in tier2.CLASSES:
            out = []
            for dodge in (True, False):
                r = a.fight([(c, cap)], dodge=dodge, heal_threshold=0.5); m = r['mem'][0]
                out.append(('**lo mata**' if r['killed'] and m['hp'] > 0 else ('muere' if m['hp'] <= 0 else 'no lo mata'))
                           + f" ({_fmt(r['t'])} s, le queda el {_fmt(100 * r['left'] / b['hp'])} %)")
            cells.append(' / '.join(out))
        print(f'| {LABEL[sc]} (esquivando / sin esquivar) | ' + ' | '.join(cells) + ' |')
    print(f"\n### Fase 3, orientativo (sin los hechizos de nivel 11; equipo esperado del nivel 9)\n")
    print('| Grupo | Marcas | Duración | Vida más baja (maná al final) | Cura | Retoños | Resultado |')
    print('|---|---|---|---|---|---|---|')
    hu110.use('esperado')
    B = b['level']
    for grp in ([('warrior', B), ('priest', B)], [('warrior', B), ('mage', B)], [('rogue', B), ('mage', B)], [('rogue', B), ('priest', B)],
                [('warrior', B + 1), ('priest', B - 1)], [('rogue', B + 1), ('mage', B - 1)]):
        r = a.fight(grp, dodge=True)
        print(f"| {' + '.join(f'{names[c]} {l}' for c, l in grp)} | esquivando | {row(r)} |")
    hu110.use('aprox')


if __name__ == '__main__':
    if '--arbol' not in sys.argv:
        num = [x for x in sys.argv[1:] if x.isdigit()]
        capataz(int(num[0]) if num else next(m['hp'] for m in Lj('monsters.json')['monsters'] if m['id']=='foreman_grask'))
    if '--capataz' not in sys.argv:
        arbol()
