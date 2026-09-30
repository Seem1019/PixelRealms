import sys,os; sys.path.insert(0,os.path.dirname(__file__))
from model import *
FASE1={'rogue':['rogue_sinister_strike','rogue_gouge','rogue_shadowstep','rogue_sprint'],
       'mage':['mage_fireball','mage_frostbolt','mage_frost_nova','mage_flame_burst'],
       'warrior':['warrior_heroic_strike','warrior_taunt','warrior_charge','warrior_whirlwind'],
       'priest':['priest_heal','priest_smite','priest_power_shield','priest_holy_pulse']}
TGT_BASE={'rogue':{'single':30,'armor':25},'mage':{'single':15,'armor':15},'warrior':{'single':15,'armor':70},'priest':{'single':0,'armor':30}}
TGT={ # aportes objetivo (class-kits.md) de los hechizos de la Fase 1
 'rogue_sinister_strike':{'single':20},'rogue_gouge':{'cc':15,'single':5},'rogue_shadowstep':{'mobility':25,'single':5,'cc':5},'rogue_sprint':{'mobility':25},
 'mage_fireball':{'single':20},'mage_frostbolt':{'cc':15,'single':5},'mage_frost_nova':{'cc':30,'aoe':5},'mage_flame_burst':{'aoe':30,'single':5},
 'warrior_heroic_strike':{'single':15},'warrior_taunt':{'cc':15},'warrior_charge':{'mobility':15,'cc':15},'warrior_whirlwind':{'aoe':20,'single':5},
 'priest_heal':{'single':30},'priest_smite':{'cc':5},'priest_power_shield':{'single':20,'mobility':10,'armor':5},'priest_holy_pulse':{'aoe':30,'cc':10}}
AX=['single','aoe','cc','mobility','armor']
def raw_metrics(SP,AU,level=6):
    out={}
    for cls,lo in FASE1.items():
        ch=Char(cls,level); heal=(cls=='priest')
        b_main,b_sec=simulate(ch,[],SP,AU,heal_mode=heal)
        base={'single':b_main/30,'aoe':0,'cc':0,'mobility':0,'armor':armor_ttl(ch,SP,AU,())}
        out[cls]={'ch':ch,'base':base,'spells':{}}
        for sid in lo:
            m,sc=simulate(ch,[sid],SP,AU,heal_mode=heal)
            s=SP[sid]
            out[cls]['spells'][sid]={'single':m/30-base['single'],'aoe':sc/30,'cc':cc_value2(ch,s,AU),'mobility':mob_value(ch,s,AU),
                                   'armor':armor_ttl(ch,SP,AU,(sid,))-base['armor']}
    return out
def calibrate(M):
    ref={}
    ref['single']=M['rogue']['base']['single']/0.30
    ref['armor']=M['warrior']['base']['armor']/0.70
    ms=M['mage']['spells']; rs=M['rogue']['spells']
    ref['aoe']=(ms['mage_flame_burst']['aoe']+ms['mage_frost_nova']['aoe'])/0.35
    ref['cc']=(ms['mage_frostbolt']['cc']+ms['mage_frost_nova']['cc'])/0.45
    ref['mobility']=(rs['rogue_shadowstep']['mobility']+rs['rogue_sprint']['mobility'])/0.50
    return ref
def points(M,ref):
    P={}
    for cls,d in M.items():
        P[cls]={'base':{a:100*d['base'][a]/ref[a] for a in AX},'spells':{sid:{a:100*v[a]/ref[a] for a in AX} for sid,v in d['spells'].items()}}
    return P
def report(SP,AU,ref=None,show=True):
    M=raw_metrics(SP,AU); ref=ref or calibrate(M); P=points(M,ref)
    rows=[]
    for cls in FASE1:
        tot={a:P[cls]['base'][a] for a in AX}
        if show: print(f"\n{cls}: base "+' '.join(f"{a}={P[cls]['base'][a]:.0f}(obj {TGT_BASE[cls].get(a,0)})" for a in AX if a in('single','armor')))
        for sid,v in P[cls]['spells'].items():
            for a in AX: tot[a]+=v[a]
            if show: print(f"  {sid:24s} "+' '.join(f"{a[:3]}={v[a]:5.1f}/{TGT[sid].get(a,0):>2}" for a in AX))
        if show: print('  TOTAL fase1: '+' '.join(f"{a[:3]}={tot[a]:.0f}" for a in AX))
    return M,ref,P
if __name__=='__main__':
    SP,AU=load_content(); M,ref,P=report(SP,AU)
    print('\nref',{k:round(v,2) for k,v in ref.items()})
    for cls in FASE1:
        ch=M[cls]['ch']; print(cls,'hp',round(ch.maxHp),'mana',round(ch.maxMana),'AP',round(ch.ap,1),'SP',round(ch.sp,1),'armor',round(ch.armor),'swing',round(ch.swing,2),'basicDPS',round(basic_hit(ch)/ch.swing,2),'TTL',round(M[cls]['base']['armor'],1))
