"""Modelo de balance de la Fase 1 (nivel 6) para PixelRealms. Valores esperados, sin azar."""
import json, os, math, copy
ROOT=os.path.join(os.path.dirname(os.path.abspath(__file__)),'..','..','content')
Lj=lambda f: json.load(open(os.path.join(ROOT,f),encoding='utf-8'))
R=Lj('rules.json'); CB=R['combat']; CL={c['id']:c for c in Lj('classes.json')['classes']}
ITEMS={i['id']:i for i in Lj('items.json')['items']}
def load_content():
    return {s['id']:s for s in Lj('spells.json')['spells']}, {a['id']:a for a in Lj('auras.json')['auras']}
AFF=R['affinity']
REF_GEAR={ # "equipo verde de su nivel" (nv 6): mejores uncommon/common de afinidad alta disponibles en el Tier 1
 'rogue':['wolf_fang_dagger','leather_vest','boar_hide_boots','bandit_gloves','wolf_tooth_necklace'],
 'warrior':['iron_sword','wooden_shield','recruit_mail_shirt','iron_helm','miner_leggings','wolf_tooth_necklace'],
 'mage':['oak_staff','novice_robe','apprentice_hood'],
 'priest':['willow_wand','novice_robe','apprentice_hood','miner_leggings'],
}
def itype(i): return i.get('weaponType') or i.get('armorType')
def affm(cls,i): return AFF['multipliers'][AFF['byClass'][cls].get(itype(i),'baja')]
class Char:
    def __init__(s,cls,level,gear=None):
        c=CL[cls]; sc=R['classScaling'][cls]; s.cls=cls; s.level=level
        st={k:c['baseStats'][k]+c['statsPerLevel'][k]*(level-1) for k in c['baseStats']}
        s.gear=[ITEMS[g] for g in (gear if gear is not None else REF_GEAR[cls])]
        armor=0; spi=0
        for it in s.gear:
            m=affm(cls,it)
            for k,v in (it.get('stats') or {}).items(): st[k]+=v*m
            armor+=(it.get('armor') or 0)*m*sc['armorMult']; spi+=(it.get('spellPower') or 0)*m
        s.st=st
        s.maxHp=c['baseHp']+st['sta']*sc['hpPerSta']; s.maxMana=(c.get('baseMana',0)+st['int']*sc['manaPerInt']) if c['resource']=='mana' else 0
        s.ap=sum(st[k]*sc['ap'][k] for k in sc['ap']); s.sp=sum(st[k]*sc['sp'][k] for k in sc['sp'])+spi
        s.armor=armor+st['agi']*CB['armorPerAgi']; s.haste=sc['haste']; s.resource=c['resource']
        s.critP=min(CB['critCap'],CB['critBase']+st['agi']*CB['critPerAgi']); s.critM=min(CB['critCap'],CB['critBase']+st['int']*CB['critPerInt'])
        s.dodge=min(CB['dodgeCap'],CB['dodgeBase']+st['agi']*CB['dodgePerAgi'])
        w=[g for g in s.gear if g.get('weaponType')][0]; s.weapon=w; s.waff=affm(cls,w)
        s.school='magic' if AFF['weaponScaling'][w['weaponType']]=='int' else 'physical'
        s.swing=w['speedMs']/1000/s.haste; s.wavg=(w['damageMin']+w['damageMax'])/2
        s.manaRegen=(st['spi']*CB['manaRegenPerSpiPer5s']+st['int']*CB['manaRegenPerIntPer5s'])/5
        s.hpRegen=st['spi']*CB['hpRegenPerSpi']+st['sta']*CB['hpRegenPerSta']
def mitig(armor,att_level): return min(CB['mitigationCap'],armor/(armor+CB['mitigationPerLevel']*att_level+CB['mitigationConstant']))
TARGET={'level':6,'armor':35}   # objetivo de referencia nv 6 (entre kóbold 30 y gólem)
def hitfac(ch,school,target=TARGET):
    if school=='physical':
        miss=CB['physicalMissBase']+CB['physicalMissPerTargetLevel']*max(0,target['level']-ch.level)
        return (1-miss)*(1-CB['dodgeBase'])*(1+ch.critP*(CB['critMultiplier']-1))*(1-mitig(target['armor'],ch.level))
    return (1-CB['magicMissBase'])*(1+ch.critM*(CB['critMultiplier']-1))
def basic_hit(ch,target=TARGET):
    power=ch.sp if ch.school=='magic' else ch.ap
    raw=ch.wavg*ch.waff+power/CB['basicAttackPowerDivisor']*ch.swing
    return raw*hitfac(ch,ch.school,target)
def eff_amount(ch,spell,e,target=TARGET):
    if e['type']=='damage':
        raw=e['base']+e.get('apCoef',0)*ch.ap+e.get('spCoef',0)*ch.sp+e.get('weaponPct',0)*ch.wavg*ch.waff
        return raw*hitfac(ch,spell['school'],target)
    if e['type']=='heal':
        raw=(e['base']+e.get('spCoef',0)*ch.sp)*(1+ch.critM*(CB['critMultiplier']-1))
        if e.get('bonusBelowHpPct'): raw*= (0.5*1+0.5*e['bonusMult'])  # mitad de las veces por debajo del umbral
        return raw
    return 0
def aura_total(ch,a,target=TARGET):
    if a['kind'] in('dot','hot'):
        per=a['base']+a.get('apCoef',0)*ch.ap+a.get('spCoef',0)*ch.sp
        if a['kind']=='dot' and a.get('school')=='physical': per*=(1-mitig(target['armor'],ch.level))
        return per, a['tickMs']/1000, a['durationMs']/1000
    if a['kind']=='shield': return a['base']+a.get('spCoef',0)*ch.sp, None, a['durationMs']/1000
    return None
AOE_N=3
PHYS_SHARE=1.0  # los monstruos del Tier 1 pegan físico  # objetivos secundarios agrupados
def simulate(ch,loadout,SP,AU,T=30.0,dt=0.05,count_basic=True,heal_mode=False,trace=None):
    """Devuelve (main, secondary) daño o cura total en T s. loadout: ids por prioridad.
    trace (lista, opcional): recibe (t, main+secondary acumulado, maná) en cada paso; lo usa tier2.py."""
    t=0; swing_t=0; cast=None; gcd=0; lock=0; cds={i:0 for i in loadout}
    energy=100.0; rage=0.0; mana=ch.maxMana; last_spend=-99
    main=0; sec=0; dots={}; buff=[0.0,0.0]  # [hasta, pct]  # (auraId,target)->[expiry,nexttick,per,tick,stacks]
    rage_taken_rate=CB['ragePerHitTaken']/2.2
    def cost_ok(s):
        a=s.get('cost',{}).get('amount',0)
        return {'energy':energy,'rage':rage,'mana':mana}[ch.resource]>=a
    def pay(s):
        nonlocal energy,rage,mana,last_spend
        a=s.get('cost',{}).get('amount',0)
        if ch.resource=='energy': energy-=a
        elif ch.resource=='rage': rage-=a
        else: mana-=a; last_spend=t if a>0 else last_spend
    def resolve(s):
        nonlocal main,sec,rage
        tgt=s['targeting']; nsec=0
        if tgt in('self_aoe_enemies','ground_aoe_enemies','ground_aoe_all','self_aoe_allies','ground_aoe_allies'):
            nsec=min(AOE_N,s.get('maxTargets',10)-1)
        dealt=False
        mult=1+(buff[1] if t<buff[0] else 0)
        for e in s['effects']:
            if e.get('applyTo')=='self':
                if e['type']=='apply_aura':
                    a=AU[e['auraId']]; p=a.get('mods',{}).get('damageDonePct',0)
                    if p>0: buff[0]=t+a['durationMs']/1000; buff[1]=p
                continue
            if e['type']=='damage' and not heal_mode:
                v=eff_amount(ch,s,e)*mult; dealt=True
                if nsec: sec+=v*(nsec+1)   # hechizo de área: todo su daño cuenta en Área
                else: main+=v
            if e['type']=='heal' and heal_mode:
                v=eff_amount(ch,s,e)
                if nsec: sec+=v*(nsec+1)
                else: main+=v
            if e['type']=='apply_aura':
                a=AU[e['auraId']]; at=aura_total(ch,a)
                if not at: continue
                per,tick,dur=at
                if (a['kind']=='dot' and not heal_mode) or (a['kind']=='hot' and heal_mode):
                    for tg in ['main']+['sec']*nsec:
                        key=(a['id'],tg if tg=='main' else 'sec')
                        d=dots.get(key)
                        st=min((d[4]+1) if d and t<d[0] else 1, a.get('maxStacks',1))
                        nxt=d[1] if d and t<d[0] else t+tick
                        dots[key]=[t+dur,nxt,per,tick,st,nsec if tg=='sec' else 0]
                if a['kind']=='shield' and heal_mode:
                    main+=per
        if dealt and ch.resource=='rage': rage=min(CB['resourceCap'],rage+CB['ragePerHitDealt'])
    while t<T:
        # recursos
        if ch.resource=='energy': energy=min(CB['resourceCap'],energy+CB['energyPerSec']*dt)
        if ch.resource=='rage': rage=min(CB['resourceCap'],rage+rage_taken_rate*dt)
        if ch.resource=='mana':
            mana=min(ch.maxMana,mana+ch.manaRegen*dt*(CB['manaRegenCastingPenalty'] if t-last_spend<5 else 1))
        # auras en el tiempo
        for k,d in list(dots.items()):
            while d[1]<=d[0]+1e-9 and d[1]<=t:
                v=d[2]*d[4]
                if k[1]=='main': main+=v
                else: sec+=v*d[5]
                d[1]+=d[3]
            if t>d[0]: del dots[k]
        gcd=max(0,gcd-dt); lock=max(0,lock-dt)
        for i in cds: cds[i]=max(0,cds[i]-dt)
        if cast:
            cast[1]-=dt
            if cast[1]<=0:
                s=SP[cast[0]]
                if cost_ok(s): pay(s); resolve(s); cds[s['id']]=s['cooldownMs']/1000
                cast=None
        else:
            for sid in loadout:
                s=SP[sid]
                if cds[sid]>0 or lock>0 or (gcd>0 and s.get('triggersGcd',True)) or not cost_ok(s): continue
                if s.get('triggersGcd',True): gcd=CB['gcdMs']/1000
                if s['castMs']>0: cast=[sid,s['castMs']/1000]
                else: pay(s); resolve(s); cds[sid]=s['cooldownMs']/1000; lock=CB['abilityLockMs']/1000
                break
        if not cast:
            swing_t+=dt
            if swing_t>=ch.swing and lock<=0:
                swing_t-=ch.swing
                if count_basic and not heal_mode: main+=basic_hit(ch)*(1+(buff[1] if t<buff[0] else 0))
                if ch.resource=='rage': rage=min(CB['resourceCap'],rage+CB['ragePerHitDealt'])
                if ch.resource=='mana': mana=min(ch.maxMana,mana+ch.maxMana*CB['manaPerBasicHitPctPerSec']*ch.swing)
        t+=dt
        if trace is not None: trace.append((t,main+sec,mana))
    return main,sec
# ---- control y movilidad (analítico, por minuto)
def cc_value(ch,s,AU):
    uses=60/max(s['cooldownMs']/1000, s['castMs']/1000+ (CB['gcdMs']/1000 if s.get('triggersGcd',True) else 0), 1.0)
    per=0
    for e in s['effects']:
        if e.get('applyTo')=='self': continue
        if e['type']=='interrupt': per+=0.5
        if e['type']=='taunt': per+=0.5*e['durationMs']/1000
        if e['type']=='apply_aura':
            a=AU[e['auraId']]; d=a['durationMs']/1000
            w={'stun':1.0,'root':0.6,'silence':0.6,'slow':0.3*(a.get('pct',0)/0.4)}.get(a['kind'])
            if w: per+=w*d
    n=1
    if s['targeting'] in('self_aoe_enemies','ground_aoe_enemies','ground_aoe_all'): n=min(3,s.get('maxTargets',10))
    mult=1+0.5*(n-1)
    # un solo objetivo no puede estar controlado más de 60 s por minuto
    return min(per*uses,60*max(per/ max(1e-9,sum(1 for _ in [0])) ,0))*mult if per else 0
def cc_value2(ch,s,AU):
    uses=60/max(s['cooldownMs']/1000, s['castMs']/1000+(CB['gcdMs']/1000 if s.get('triggersGcd',True) else 0), 1.0)
    per=0; cap_w=0
    for e in s['effects']:
        if e.get('applyTo')=='self': continue
        if e['type']=='interrupt': per+=0.5
        if e['type']=='taunt': per+=0.5*e['durationMs']/1000; cap_w=max(cap_w,0.5)
        if e['type']=='apply_aura':
            a=AU[e['auraId']]; d=a['durationMs']/1000
            w={'stun':1.0,'root':0.6,'silence':0.6,'slow':0.3*(a.get('pct',0)/0.4)}.get(a['kind'])
            if w: per+=w*d; cap_w=max(cap_w,w)
    n=1
    if s['targeting'] in('self_aoe_enemies','ground_aoe_enemies','ground_aoe_all'): n=min(3,s.get('maxTargets',10))
    val=per*uses
    val=min(val,60*cap_w+0.5*uses)   # tope por tiempo real controlado
    return val*(1+0.5*(n-1))
def mob_value(ch,s,AU,base_speed=4.0):
    uses=60/max(s['cooldownMs']/1000,1.0); per=0
    # el lanzador también recibe lo beneficioso de un área a su alrededor o de una línea que sale de él (HU-102)
    own=s['targeting'] in('self','ally','self_aoe_allies') or (s['targeting'] in('ground_aoe_allies','ground_aoe_all') and s.get('shape')=='line')
    for e in s['effects']:
        if e['type']=='leap': per+=e['maxRange']
        if e['type']=='dash': per+=5.0
        if e['type']=='apply_aura':
            a=AU[e['auraId']]
            if a['kind']=='stat_mod' and a.get('mods',{}).get('speedPct',0)>0 and not a.get('isDebuff') and (e.get('applyTo')=='self' or own):
                per+=a['mods']['speedPct']*base_speed*a['durationMs']/1000
            if a.get('removesKinds') and 'root' in a['removesKinds']: per+=2*base_speed  # ~2 s de control quitado
    return per*uses
def armor_ttl(ch,SP,AU,loadout=(),ref_dps=10.0,att_level=6):
    phys=ref_dps*PHYS_SHARE*(1-CB['physicalMissBase'])*(1-ch.dodge)*(1-mitig(ch.armor,att_level)); mag=ref_dps*(1-PHYS_SHARE)*(1-CB['magicMissBase'])
    inc=phys+mag; red=0; shield_rate=0
    for sid in loadout:
        s=SP[sid]
        if s['targeting']!='self': continue
        for e in s['effects']:
            if e['type']=='apply_aura':
                a=AU[e['auraId']]; up=min(1,(a['durationMs']/1000)/max(1,s['cooldownMs']/1000))
                dt=a.get('mods',{}).get('damageTakenPct',0)
                if dt<0: red+=-dt*up
                if a['kind']=='shield': shield_rate+=(a['base']+a.get('spCoef',0)*ch.sp)/max(1,s['cooldownMs']/1000)
    inc=inc*(1-red)-shield_rate
    return ch.maxHp/max(inc,0.1)
