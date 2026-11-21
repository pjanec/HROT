import os,re,sys,collections
PAT = re.compile(r"""
  BrainBlackboard | (?<!Blueprint)Blackboard1024 | BehaviorParameters
| MaxBehaviorParamByteSize | BrainBlackboardByteSize
| inline\s+tier | heavy\s+tier | heavy\s+DTO | heavy\s+component | heavy\s+blackboard
| spill\s+to\s+heavy | promote\s+to\s+heavy | MaxInlineBytes | MaxHeavyBytes
| InlineMemoryExceeded | HeavyMemoryExceeded | RequiresHeavyComponent
| 100-byte | 100\s+bytes | 128-byte | 128\s+bytes | 60-byte | 60\s+bytes
| cognitive\s+bus
| \b928\b | \b3936\b | \b16368\b | 3 936 | 16 368
| MaxSlots\s*4 | 4\s*/\s*8\s*/\s*16
| BrainHsm64 | BrainHsm128 | BrainBTreeState
| BTreeTickSystem | HsmTickSystem
| BrainBlackboardTranslator
""", re.I | re.X)
EX_FILES = {
 'docs/blueprints/DESIGN_Occurrence_Scoped_Storage.md',
 'docs/blueprints/Blueprint_Issues_Tracker.md',
 'docs/blueprints/RULINGS.md',
 'docs/blueprints/RESUME_P4_Retire_Blackboards.md',
 'docs/blueprints/RESUME_Docs_Occurrence_Storage_Sweep.md',
 # ⭐ the behaviors lane's OWN build plan and resumption for this retirement. They name every
 #   retired token on purpose — that is what they are FOR. That lane owns them; this one does not.
 'docs/blueprints/PLAN_Occurrence_Storage_Build.md',
 'docs/blueprints/RESUME_Occurrence_Storage.md',
}
rows=[]
for root,dirs,fs in os.walk('docs'):
    if 'batches' in root.split(os.sep): continue
    for f in sorted(fs):
        if not (f.endswith('.md') or f.endswith('.svg')): continue
        p=os.path.join(root,f)
        if p in EX_FILES: continue
        hits=[]
        for i,ln in enumerate(open(p,encoding='utf-8',errors='replace'),1):
            for m in PAT.finditer(ln):
                hits.append((i, m.group(0).strip(), ln.strip()[:150]))
        if hits: rows.append((len(hits),p,hits))
rows.sort(reverse=True)
tot=sum(r[0] for r in rows)
print(f'{len(rows)} files, {tot} hits')
if '-v' in sys.argv:
    for n,p,h in rows:
        print(f'\n### {n}  {p}')
        for i,tok,ln in h: print(f'  {i}: [{tok}] {ln}')
else:
    for n,p,h in rows:
        toks=collections.Counter(t.lower() for _,t,_ in h)
        print(f'{n:4d}  {p}   {dict(toks)}')
