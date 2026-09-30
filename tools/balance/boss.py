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
print('## Capataz Grask (armadura 90)')
import sys
HP=int(sys.argv[1]) if len(sys.argv)>1 else next(m['hp'] for m in Lj('monsters.json')['monsters'] if m['id']=='foreman_grask')
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
