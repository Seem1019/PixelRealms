import sys,os,copy; sys.path.insert(0,os.path.dirname(__file__))
import model
from model import *
from pentagram import FASE1,AX
SP,AU=load_content()
rog=Char('rogue',6); war=Char('warrior',6)
REF={'single':(basic_hit(rog)/rog.swing)/0.30}; REF['aoe']=4*REF['single']*0.6; REF['cc']=40.0; REF['mobility']=120.0; REF['armor']=armor_ttl(war,SP,AU,())/0.70
def contrib(cls,sid,lvl=6):
    ch=Char(cls,lvl); heal=cls=='priest'
    bm,_=simulate(ch,[],SP,AU,heal_mode=heal); m,sc=simulate(ch,[sid],SP,AU,heal_mode=heal); s=SP[sid]
    v={'single':(m-bm)/30,'aoe':sc/30,'cc':cc_value2(ch,s,AU),'mobility':mob_value(ch,s,AU),'armor':armor_ttl(ch,SP,AU,(sid,))-armor_ttl(ch,SP,AU,())}
    return {a:100*v[a]/REF[a] for a in AX}
PG=R['balanceTargets']['pentagram']['references']
print('referencias medidas:',{k:round(v,1) for k,v in REF.items()},'| rules.json:',{k:PG[k] for k in('singleDps','aoeDps','ccSecPerMin','mobilityTilesPerMin','armorTtlSec')})
print('## Pentagrama Fase 1 (nivel 6)')
PROF={}
for cls,lo in FASE1.items():
    ch=Char(cls,6); heal=cls=='priest'; bm,_=simulate(ch,[],SP,AU,heal_mode=heal)
    tot={'single':100*bm/30/REF['single'],'aoe':0,'cc':0,'mobility':0,'armor':100*armor_ttl(ch,SP,AU,())/REF['armor']}
    rows=[]
    for sid in lo:
        c=contrib(cls,sid); rows.append((sid,c,sum(c.values())))
        for a in AX: tot[a]+=c[a]
    PROF[cls]=tot
    print(cls,' '.join(f"{a}={tot[a]:.0f}" for a in AX),'total',round(sum(tot.values())))
    for sid,c,sm in rows: print('   ',sid,' '.join(f"{a[:3]}={c[a]:.0f}" for a in AX),'suma',round(sm))
# ---------- solo: nivel 5 contra kóbold minero (normal nv 5)
model.AOE_N=0
KOB={'level':5,'armor':30}
MONS={m['id']:m for m in Lj('monsters.json')['monsters']}; KM=MONS['kobold_miner']
def solo(cls,lo,mob_hp=KM['hp'],mob_dmg=(KM['damageMin']+KM['damageMax'])/2,mob_speed=KM.get('attackSpeedMs',2200)/1000,lvl=5):
    ch=Char(cls,lvl,[g for g in model.REF_GEAR[cls] if ITEMS[g]['levelReq']<=lvl])
    old=model.TARGET.copy(); model.TARGET.update(KOB)
    lo_d=[s for s in lo if any(e['type']=='damage' for e in SP[s]['effects'])]
    lo_run=[s for s in lo if s in lo_d]
    t=1.0
    while True:
        m,_=simulate(ch,lo_run,SP,AU,T=t)
        if m>=mob_hp or t>200: break
        t+=0.5
    model.TARGET.update(old)
    # daño recibido (sin contar controles ni kiting: conservador)
    inc=mob_dmg/mob_speed*(1-CB['physicalMissBase'])*(1-ch.dodge)*(1-mitig(ch.armor,5))
    lost=inc*t
    # descanso: vida (el Sacerdote se cura con Sanar) y maná
    rest=0
    if cls=='priest':
        h=eff_amount(ch,SP['priest_heal'],SP['priest_heal']['effects'][0]); casts=math.ceil(lost/h); heal_t=casts*1.5
        mana_used=casts*SP['priest_heal']['cost']['amount']
        mana_fight=_mana_used(ch,lo_run,t)
        rest=heal_t+max(0,(mana_used+mana_fight)/ (ch.manaRegen) - heal_t)
    else:
        rest=(CB['hpRegenDelaySec']+lost/ch.hpRegen) if lost>0 else 0
        if ch.resource=='mana':
            rest=max(rest,CB['hpRegenDelaySec']+_mana_used(ch,lo_run,t)/ch.manaRegen)
    return t,lost,ch.maxHp,rest
def _mana_used(ch,lo,t):
    # aproximación: coste de los hechizos lanzados en t segundos menos lo recuperado por golpe
    used=0
    for sid in lo:
        s=SP[sid]; c=s.get('cost',{}).get('amount',0)
        period=max(s['cooldownMs']/1000, s['castMs']/1000, 1.0)
        used+=c*max(1,int(t/period)) if s['cooldownMs']>0 else c*(t/ max(s['castMs']/1000,1.0))*0.5
    gained=t/ch.swing*ch.maxMana*CB['manaPerBasicHitPctPerSec']*ch.swing*0.5
    return max(0,used-gained)
print('\n## Solo: nivel 5 contra Kóbold minero (160 vida, armadura 30), con 10 s de caminata entre monstruos')
SOLO={'rogue':['rogue_shadowstep','rogue_sinister_strike','rogue_gouge'],'mage':['mage_flame_burst','mage_frostbolt','mage_fireball'],
      'warrior':['warrior_charge','warrior_whirlwind','warrior_heroic_strike'],'priest':['priest_holy_pulse','priest_smite']}
res={}
for cls,lo in SOLO.items():
    ttk,lost,hp,rest=solo(cls,lo); cyc=ttk+rest+10; res[cls]=cyc
    print(f"{cls:8s} mata en {ttk:5.1f} s | pierde {lost:5.1f} de {hp:.0f} ({100*lost/hp:3.0f} %) | descansa {rest:5.1f} s | ciclo {cyc:5.1f} s | XP/h {3600/cyc*26:6.0f}")
best=max(3600/c for c in res.values())
print('diferencia de XP/h respecto a la mejor clase:',{c:f"{100*(1-(3600/v)/best):.0f} %" for c,v in res.items()})
basic={}
for cls in SOLO:
    ttk,lost,hp,rest=solo(cls,[]); basic[cls]=(ttk,100*lost/hp)
print('solo con básicos:',{c:f"{v[0]:.0f} s, {v[1]:.0f} % vida" for c,v in basic.items()})
