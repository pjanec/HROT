# Measures, from SOURCE (no build): every [ComponentId] type — struct or managed, its DataPolicy, and whether it holds a raw Entity field
# (directly or through a nested struct). Used by docs/UX/DESIGN_Review_Perspective.md §6 (M1 / M2). Usage: python3 scripts/probe-recorded-components.py out.json
# ⚠ a source parse, not reflection: "recorded" = no NoReplay / Transient policy; a type registered without [ComponentId] is missed.
import os,re,json,sys
roots=['FDP','Hrot','Stride']
decl=re.compile(r'((?:\[[^\]\n]*\]\s*)+)(?:public|internal)\s+(?:sealed\s+|readonly\s+|partial\s+|unsafe\s+|ref\s+)*(record\s+struct|record\s+class|record|struct|class)\s+(\w+)')
anytype=re.compile(r'(?:public|internal|private)?\s*(?:sealed\s+|readonly\s+|partial\s+|unsafe\s+)*(record\s+struct|struct|record|class)\s+(\w+)[^{;]*\{')
def body(src,i):
    j=src.find('{',i)
    if j<0: return ''
    d=0
    for k in range(j,len(src)):
        c=src[k]
        if c=='{': d+=1
        elif c=='}':
            d-=1
            if d==0: return src[j:k+1]
    return src[j:]
fieldre=re.compile(r'^\s*(?:public|internal|private|protected)?\s*(?:readonly\s+|required\s+|static\s+|fixed\s+)*([\w\.<>\[\]\?,\s]+?)\s+(\w+)\s*(?:[;={]|\[)',re.M)
comps={}; types={}
for r in roots:
  for dp,dn,fn in os.walk(r):
    if any(x in dp for x in ('/bin','/obj','Test','test','Examples')): continue
    for f in fn:
      if not f.endswith('.cs'): continue
      p=os.path.join(dp,f)
      try: s=open(p,encoding='utf-8',errors='ignore').read()
      except: continue
      for m in anytype.finditer(s):
          b=body(s,m.start())
          flds=[]
          for fm in fieldre.finditer(b):
              t=fm.group(1).strip()
              if t in ('return','new','var','if','else','case','throw','await','using','get','set','=>') : continue
              flds.append(t)
          types.setdefault(m.group(2),(m.group(1),flds,p))
      for m in re.finditer(r'\[ComponentId\(([^)]*)\)\]',s):
          seg=s[m.start():m.start()+2500]
          dm=re.search(r'(?:public|internal)\s+(?:sealed\s+|readonly\s+|partial\s+|unsafe\s+|ref\s+|abstract\s+)*(record\s+struct|record\s+class|record|struct|class)\s+(\w+)',seg)
          if not dm: continue
          attrs=seg[:dm.start()]
          pol=re.findall(r'DataPolicy\(([^)]*)\)',attrs)
          comps[dm.group(2)]=dict(id=m.group(1),kind=dm.group(1),policy=pol,file=p)
ENT=re.compile(r'(^|[^\w])Entity(\?|\[\])?$')
def has_entity(tname,seen=()):
    t=tname.replace('?','').replace('[]','').strip()
    t=re.sub(r'.*<(.+)>.*',r'\1',t).split(',')[0].strip().split('.')[-1]
    if t=='Entity': return [tname]
    if t in seen or t not in types: return []
    out=[]
    for ft in types[t][1]:
        if ENT.search(ft.strip()) or ft.strip().split('.')[-1] in ('Entity','Entity?','Entity[]') or re.search(r'\bEntity\b',ft) and 'EntityRef' not in ft and 'EntityLink' not in ft and 'EntityRepository' not in ft:
            out.append(f'{t}.{ft}')
        else:
            sub=has_entity(ft,seen+(t,))
            if sub: out.append(f'{t}→'+sub[0])
    return out
res={'components':len(comps),'managed':[],'entity_fields':[]}
for n,c in sorted(comps.items(),key=lambda x:x[0]):
    managed = c['kind'] in ('class','record','record class')
    e=has_entity(n)
    row=dict(name=n,id=c['id'],kind=c['kind'],policy=c['policy'],file=c['file'],entity=e[:3])
    if managed: res['managed'].append(row)
    if e: res['entity_fields'].append(row)
json.dump(res,open(sys.argv[1],'w'),indent=1)
print('components',len(comps),'managed',len(res['managed']),'with Entity fields',len(res['entity_fields']))
