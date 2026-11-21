# CE-6009 / CE-6010 — generates macros/city-battle/* (rifle / militia platoon, mech / defender company). Run: python3 scripts/city_battle_macros.py
# 📄 docs/designs/group-maneuvers/DESIGN_City_Battle_Showcase.md §4a
import json, os, uuid
ROOT=os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'macros', 'city-battle')
ROT=[0,0,0,1]
def ent(pos,tkb,name,side,extra=None,binds=None):
    e={"SimTransform":{"Position":[round(pos[0],2),round(pos[1],2),0],"Rotation":ROT},"TkbIdentity":{"TkbType":tkb},
       "EntityInfo":{"Name":name,"ForceId":side}}
    if extra: e.update(extra)
    b={"EntityInfo.ForceId":"side"}
    if binds: b.update(binds)
    e["ParamBindings"]={"Bindings":b}
    return e
def write(name,desc,params,ents):
    os.makedirs(f'{ROOT}/{name}',exist_ok=True)
    doc={"$meta":{"docType":"Hrot.Scenario","schemaVersion":1},
         "header":{"subsystemType":"Hrot.Scenario","schemaVersion":"1.0","Kind":"macro","description":desc,
                   "Anchor":{"Position":[0,0,0],"Heading":0},"Params":params},
         "entities":ents}
    json.dump(doc,open(f'{ROOT}/{name}/scenario.json','w'),indent=2,ensure_ascii=False); open(f'{ROOT}/{name}/scenario.json','a').write("\n")
SIDE={"Name":"side","TypeId":"ForceId","Default":"Friend"}
KEY={"Name":"key","TypeId":"string","Default":"Platoon"}   # ⭐ CE-6010 — the platoon's RefKey, so a scenario's trigger can Order THIS platoon ("@A1")
PKEYS=[{"Name":f"p{i}","TypeId":"string","Default":f"Platoon{i}"} for i in (1,2,3)]   # the company's three platoon keys
def platoon(prefix,kit,label):
    man=kit  # TKB type of the men
    E={}
    def g(n): return f'{prefix}-0000-4000-8000-{n:012d}'
    E[g(1)]=ent((0,0),305,label,"Friend",{"RefKey":{"Value":"Platoon"}},{"EntityInfo.Name":"name","RefKey.Value":"key"})
    n=2
    for s in range(6):
        sx=((s%3)-1)*15.0; sy=-(s//3)*12.0
        sq=g(n); n+=1
        E[sq]=ent((sx,sy),302,f"Squad {s+1}","Friend",{"UnitSubordinate":{"CommanderGuid":g(1),"Designation":"Wingman"}})
        roles=[("Leader","SquadLeader",man,(0,1.5)),("MG","MachineGunner",man,(-2,0)),("Grenadier","Wingman",2004 if man==2002 else man,(2,0)),
               ("Rifleman 1","Wingman",man,(-4,-1.5)),("Rifleman 2","Wingman",man,(0,-1.5)),("Rifleman 3","Wingman",man,(4,-1.5))]
        for r,des,t,(dx,dy) in roles:
            E[g(n)]=ent((sx+dx,sy+dy),t,f"Squad {s+1} {r}","Friend",{"Health":{"Current":100,"Max":100},"TargetMemory":{"Entries":[]},
                     "UnitSubordinate":{"CommanderGuid":sq,"Designation":des}}); n+=1
    return E
write("rifle-platoon",
 "What: CE-6009 (city battle M-1, docs/designs/group-maneuvers/DESIGN_City_Battle_Showcase.md §4, §11 M-1) - a RIFLE PLATOON, the attacker's kit: a body-less platoon (type 305, key @Platoon) commanding SIX squads (302; CommandPicture.MaxSubordinates = 6) of six soldiers (2002; one grenadier 2004): leader, MG, grenadier, three riflemen, two rows of three 15 m apart round the anchor, idle until ordered. Params: side, name (the platoon's name), key (its RefKey, default Platoon - CE-6010). Judge: inserted, 1 platoon + 6 squads + 36 men appear round the anchor, of side 'side', each man under his squad, each squad under the platoon.",
 [SIDE,{"Name":"name","TypeId":"string","Default":"Rifle Platoon"},KEY], platoon("c6009a00","2002" and 2002,"Rifle Platoon"))
write("militia-platoon",
 "What: CE-6009 (city battle M-1) - a MILITIA PLATOON, the defender's kit: the rifle platoon's shape (platoon 305 @Platoon, six squads of six) of insurgent fighters (2003). Params: side (default Hostile), name, key (its RefKey).",
 [{"Name":"side","TypeId":"ForceId","Default":"Hostile"},{"Name":"name","TypeId":"string","Default":"Militia Platoon"},KEY], platoon("c6009b00",2003,"Militia Platoon"))
def inst(key,macro,pos,args_binds):
    return {"SimTransform":{"Position":[pos[0],pos[1],0],"Rotation":ROT},"TkbIdentity":{"TkbType":8813},"EntityInfo":{"Name":macro},
            "MacroInstance":{"Macro":macro,"Args":{}},"ParamBindings":{"Bindings":args_binds}}
def company(prefix,platoon_macro,names,support):
    E={}
    def g(n): return f'{prefix}-0000-4000-8000-{n:012d}'
    for i,nm in enumerate(names):
        e=inst(g(i+1),f"city-battle/{platoon_macro}",((i-1)*60.0,0),{"MacroInstance.Args.side":"side","MacroInstance.Args.key":f"p{i+1}"})
        e["MacroInstance"]["Args"]={"name":nm}
        E[g(i+1)]=e
    n=10
    for (pos,tkb,name,extra) in support:
        E[g(n)]=ent(pos,tkb,name,"Friend",extra); n+=1
    return E,g
E,g=company("c6009c00","rifle-platoon",["1st Platoon","2nd Platoon","3rd Platoon"],[])
sup=[]
tp=g(20)
E[tp]=ent((0,-40),301,"Tank Section","Friend")
for i in range(2): E[g(21+i)]=ent((-10+20*i,-40),100,f"Tank {i+1}","Friend",{"Health":{"Current":3000,"Max":3000},"TargetMemory":{"Entries":[]},"UnitSubordinate":{"CommanderGuid":tp,"Designation":"Wingman" if i else "SquadLeader"}})
for i in range(4): E[g(30+i)]=ent((-75+50*i,-25),101,f"IFV {i+1}","Friend",{"Health":{"Current":500,"Max":500},"TargetMemory":{"Entries":[]}})
write("mech-company",
 "What: CE-6009 (city battle M-1, §4) - a MECHANISED COMPANY (attacker): three rifle-platoon macros (nested instances, 60 m apart, named 1st / 2nd / 3rd Platoon) and the company SUPPORT ELEMENT - a tank section (unit 301, 2 M1) and 4 Bradley IFVs, 25-40 m behind. ⚠ NO company commander: the slot stays empty until the brains lane's CE-7002 (CompanyCommander) - the platoons are top-level until then. Params: side, p1 / p2 / p3 (the platoons' RefKeys, default Platoon1-3 - CE-6010).",
 [SIDE]+PKEYS,E)
E,g=company("c6009d00","militia-platoon",["1st Platoon","2nd Platoon","3rd Platoon"],[])
for i in range(2): E[g(40+i)]=ent((-30+60*i,-30),104,f"Technical {i+1}","Hostile",{"Health":{"Current":500,"Max":500},"TargetMemory":{"Entries":[]}})
write("defender-company",
 "What: CE-6009 (city battle M-1, §4) - a DEFENDER COMPANY: three militia-platoon macros (nested, 60 m apart) and two technicals (104) as its support element. ⚠ NO company commander until CE-7002. Params: side (default Hostile), p1 / p2 / p3 (the platoons' RefKeys).",
 [{"Name":"side","TypeId":"ForceId","Default":"Hostile"}]+PKEYS,E)
print(os.listdir(ROOT))

# ⭐ CE-6011 — a ROADBLOCK as one macro, so a variant keeps or drops a whole crossing's roadblock with ONE Presence roll (Scenario Macros §17):
#   two concrete blocks + two cars across the road west of the anchor (the ring-road crossing), an MG nest behind sandbags each side, an AT team.
def obstacle(pos,tkb,name,yaw,l,w,h):
    import math
    a=math.radians(yaw)/2
    return {"SimTransform":{"Position":[pos[0],pos[1],0],"Rotation":[0,0,round(math.sin(a),7),round(math.cos(a),7)]},"TkbIdentity":{"TkbType":tkb},
            "EntityInfo":{"Name":name},"ObstacleShape":{"Length":l,"Width":w,"Height":h}}
FACE_W=[0,0,0.7071068,0.7071068]
def g(n): return f'c6011a00-0000-4000-8000-{n:012d}'
E={g(1):obstacle((-14,-2.2),8808,"Roadblock block 1",90,6,1,1), g(2):obstacle((-16,2.6),8808,"Roadblock block 2",90,6,1,1),
   g(3):obstacle((-24,-1.5),8806,"Roadblock car 1",30,4.5,1.9,1.8), g(4):obstacle((-27,2.5),8806,"Roadblock car 2",-25,4.5,1.9,1.8)}
n=5
for dy,tag in ((-14,"S"),(14,"N")):
    E[g(n)]=obstacle((4,dy),8807,f"MG Nest {tag} sandbags",90,4,0.8,1.2); n+=1
    m=ent((6,dy),2009,f"MG Nest {tag}","Hostile",{"Health":{"Current":100,"Max":100},"TargetMemory":{"Entries":[]}}); m["SimTransform"]["Rotation"]=FACE_W
    E[g(n)]=m; n+=1
for j in range(2):
    m=ent((10,(j-0.5)*4),2006,f"AT Team Gunner {j+1}","Hostile",{"Health":{"Current":100,"Max":100},"TargetMemory":{"Entries":[]}}); m["SimTransform"]["Rotation"]=FACE_W
    E[g(n)]=m; n+=1
write("roadblock",
 "What: CE-6011 (city battle V-A, §4 prepared positions, D-3) - a ROADBLOCK at a road crossing (the anchor): two concrete blocks (8808) and two cars (8806) across an east-west road 14-27 m WEST of the anchor, an MG nest (an automatic rifleman 2009 behind sandbags 8807) 14 m each side of the road, an AT team (2 x 2006) behind. One macro so a variant keeps or drops a whole roadblock with one Presence roll. Params: side (default Hostile).",
 [{"Name":"side","TypeId":"ForceId","Default":"Hostile"}],E)
