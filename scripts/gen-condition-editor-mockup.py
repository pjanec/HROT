# Generates docs/UX/condition-editor-visual.svg - an ImGui-default-dark mockup of the friendly condition editor.
# Regenerate: python3 scripts/gen-condition-editor-mockup.py docs/UX/condition-editor-visual.svg  (needs fonttools; reads FDP/Data/Fonts/fa-solid-900.ttf)
import os, sys
from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen
_F=TTFont(os.path.join(os.path.dirname(os.path.abspath(__file__)),"..","FDP","Data","Fonts","fa-solid-900.ttf"))
_G=_F.getGlyphSet(); _C=_F.getBestCmap(); _U=_F["head"].unitsPerEm; _A=_F["hhea"].ascent
o=[]
BG="#0F0F0F"; TITLE="#294A7A"; FRAME="#1D2F48"; BTN="#23415F"; HDR="#1C344F"; TXT="#FFFFFF"; DIS="#808080"; BORDER="#3A3A44"; SEP="#4A4A55"
TAB="#1F3A5C"; TABSEL="#3369AD"; ACC="#4296FA"; ERR="#E65555"; PEND="#F2C033"; INFO="#6AB0FF"; OK="#5BD06A"
TEAL,ORANGE,VIOLET,BLUE,AMBER,GREEN,RED,GREY="#3EC6B0","#F29B38","#B07CF0","#5AA9FF","#F2C033","#5BD06A","#E65555","#A0A0AA"
FONT="font-family='Roboto, Segoe UI, Arial, sans-serif'"
def fa(cp,x,y,col,sz=13,op=1):
    pen=SVGPathPen(_G); _G[_C[cp]].draw(pen); k=sz/_U
    o.append(f"<path d='{pen.getCommands()}' fill='{col}' fill-opacity='{op}' transform='translate({x},{y+_A*k}) scale({k},{-k})'/>")
def t(x,y,s,c=TXT,sz=13,anchor="start"):
    s=s.replace("&","&amp;").replace("<","&lt;")
    o.append(f"<text x='{x}' y='{y}' fill='{c}' font-size='{sz}' {FONT} text-anchor='{anchor}'>{s}</text>")
def tabbar(x,y,w_total,names,selected,TABC="#24242C",SELC="#3A3A48",icons=None,LINE="#3369AD"):
    """ImGui's tab bar: rounded top corners, flat bottom, a line under the bar in the selected colour."""
    cx=x
    for i,name in enumerate(names):
        ic=icons[i] if icons else None
        w=22+(19 if ic else 0)+sum(8.6 if ch.isupper() else (3.6 if ch==' ' else (4.0 if ch in 'il&' else 6.9)) for ch in name); c=SELC if i==selected else TABC; rr=4; h=21
        o.append(f"<path d='M{cx} {y+h} L{cx} {y+rr} Q{cx} {y} {cx+rr} {y} L{cx+w-rr} {y} Q{cx+w} {y} {cx+w} {y+rr} L{cx+w} {y+h} Z' fill='{c}'/>")
        if i==selected: o.append(f"<rect x='{cx+2}' y='{y}' width='{w-4}' height='2' fill='{LINE}'/>")
        if ic: fa(ic[0],cx+8,y+4,ic[1],12.5)
        t(cx+8+(19 if ic else 0),y+15,name); cx+=w+2
    o.append(f"<rect x='{x}' y='{y+21}' width='{w_total}' height='1.5' fill='{LINE}'/>")
def r(x,y,w,h,f,stroke=None,rx=0,sw=1,dash=None):
    st=f" stroke='{stroke}' stroke-width='{sw}'" if stroke else ""
    d=f" stroke-dasharray='{dash}'" if dash else ""
    o.append(f"<rect x='{x}' y='{y}' width='{w}' height='{h}' fill='{f}' rx='{rx}'{st}{d}/>")
def tw(s,sz=13): return len(s)*sz*0.56
# a sentence blank: a small combo-looking chip, optional coloured glyph; returns its width
def chip(x,y,label,glyph=None,gcol=None,border=None,hint=False,arrow=True):
    w=tw(label)+ (26 if glyph else 10) + (16 if arrow else 0)
    r(x,y,w,20,FRAME,stroke=border,rx=3,sw=1.3)
    tx=x+6
    if glyph: fa(glyph,x+5,y+3.5,gcol,12); tx=x+21
    t(tx,y+14.5,label,DIS if hint else TXT)
    if arrow: o.append(f"<path d='M{x+w-12} {y+8} l3.5 4 l3.5 -4 z' fill='{DIS}'/>")
    return w
def word(x,y,s,c="#C8C8D0"): t(x,y+14.5,s,c); return tw(s)+2
def row(x,y,parts,live=None,measured=None,bad=False,icon=None):
    # parts: list of ("chip",label,glyph,gcol,opts) or ("word",text)
    if bad: r(x-6,y-3,700,26,"#2A1416")
    cx=x
    for p in parts:
        if p[0]=="word": cx+=word(cx,y,p[1])+4
        else:
            opts=p[4] if len(p)>4 else {}
            cx+=chip(cx,y,p[1],p[2],p[3],**opts)+6
    if live is not None:
        fa(0xf00c if live else 0xf00d, x+612, y+3.5, OK if live else ERR, 13)
        if measured: t(x+630,y+14.5,measured,DIS,sz=12)
    fa(0xf142,x+694,y+3.5,DIS,13)   # ellipsis-vertical: the row menu
W,H=1240,826
o.append(f"<svg xmlns='http://www.w3.org/2000/svg' width='{W}' height='{H}' viewBox='0 0 {W} {H}'>")
r(0,0,W,H,"#1B1B1F")
t(20,26,"Condition editor — the friendly view (ImGui default dark; real FontAwesome glyphs; every element is one ImGui call — design §4)","#E8E8EE",sz=14)
t(20,44,"Annotations in grey are not drawn by the app.","#8A8A96",sz=11.5)
# ---- the Details window, rule "Phase 2: assault", When tab
PX,PY,PW,PH=20,60,760,540
r(PX,PY,PW,PH,BG,stroke=BORDER)
r(PX,PY,PW,20,TITLE); t(PX+8,PY+14,"Details — Rule \"Phase 2: assault\"")
tabbar(PX+8,PY+25,PW-16,["When","Do","Settings","Results"],0,icons=[(0xf0b0,"#5AA9FF"),(0xf04b,"#5BD06A"),(0xf013,"#A0A0AA"),(0xf11e,"#F2C033")])
# expert switch + live chip
r(PX+PW-200,PY+27,84,18,"#163A24",rx=9); fa(0xf111,PX+PW-192,PY+30,GREEN,11); t(PX+PW-176,PY+40,"Live",  "#7FD69A",sz=11.5)
r(PX+PW-108,PY+27,100,18,FRAME,rx=3); t(PX+PW-100,PY+40,"Expert view",DIS,sz=11.5); r(PX+PW-24,PY+31,10,10,"#2A2A33",stroke=SEP)
X=PX+16; y=PY+64
t(X,y+14.5,"When"); cw=chip(X+40,y,"all"); t(X+40+cw+6,y+14.5,"of these are true:")
y+=34
row(X,y,[("chip","Alpha 1-1",0xf007,BLUE),("chip","is inside",None,None),("chip","OBJ BLUE",0xf05b,GREEN)],live=True,icon=(0xf3c5,GREEN))
yA=y; y+=30
row(X,y,[("chip","at least 3",None,None),("word","of"),("chip","Red side",0xf024,RED),("chip","are destroyed",0xf54c,RED)],live=False,measured="1 of 3",icon=(0xf54c,RED))
yB=y; y+=30
row(X,y,[("chip","Objective",0xf140,GREEN),("chip","Seize the bridge",None,None),("chip","is",None,None),("chip","achieved",0xf00c,GREEN)],live=True,icon=(0xf140,GREEN))
yC=y; y+=30
row(X,y,[("chip","Alpha 1-1",0xf007,BLUE),("chip","strength",0xf21e,RED),("chip","is below",None,None),("chip","50 %",None,None,{"arrow":False})],live=False,measured="62 %",icon=(0xf21e,RED))
yD=y; y+=30
row(X,y,[("chip","Alpha 1-1",0xf007,BLUE),("chip","is not",None,None),("chip","under fire",0xf06d,ORANGE)],live=True,icon=(0xf06d,ORANGE))
yE=y; y+=30
row(X,y,[("chip","Bravo 1",0xf0c0,BLUE),("chip","is inside",None,None),("chip","pick an area…",0xf05b,GREEN,{"border":ERR,"hint":True})],bad=True,icon=(0xf3c5,GREEN))
t(X+20,y+40,"Choose an area — the rule cannot be applied until every blank is filled",ERR,sz=11.5)
yF=y; y+=58
# nested group (one level)
r(X+2,y-4,690,96,"#141A24",stroke="#2A3550",rx=4)
t(X+12,y+14.5,"Any"); cw=chip(X+42,y,"of",arrow=True); t(X+42+cw+6,y+14.5,"these:")
y+=30
row(X+12,y,[("chip","Time",0xf017,BLUE),("chip","is after",None,None),("chip","00:12:00",None,None,{"arrow":False})],live=True,icon=(0xf017,BLUE))
y+=30
row(X+12,y,[("chip","Rule",0xf0e7,AMBER),("chip","Ambush at the ford",None,None),("chip","has fired",0xf0e7,AMBER)],live=False,icon=(0xf0e7,AMBER))
yG=y; y+=46
# custom (expert) row
r(X-2,y-3,690,24,"#1A1A22",stroke=SEP,rx=3,dash="4 3"); fa(0xf121,X+4,y+2,GREY,13)
t(X+24,y+13.5,"Custom condition (Expert) — Blueprint variable of \"Sniper team\"  ·  Open in Expert view",DIS,sz=12.5)
yH=y; y+=34
# add buttons
r(X,y,128,22,BTN,rx=3); fa(0xf067,X+8,y+4.5,GREEN,12); t(X+24,y+15.5,"Add condition ▾",sz=12.5)
r(X+136,y,104,22,BTN,rx=3); fa(0xf247,X+144,y+4.5,BLUE,12); t(X+160,y+15.5,"Add group",sz=12.5)
r(X+248,y,118,22,BTN,rx=3); fa(0xf0d0,X+256,y+4.5,VIOLET,12); t(X+272,y+15.5,"From preset ▾",sz=12.5)
yAdd=y; y+=40
# timing line (the trigger's holdFor / delay / repeat)
o.append(f"<line x1='{X}' y1='{y-8}' x2='{PX+PW-16}' y2='{y-8}' stroke='{SEP}'/>")
cx=X; cx+=word(cx,y,"…and it stays true for")+4; cx+=chip(cx,y,"30 s",arrow=False)+8; cx+=word(cx,y,", then wait")+4; cx+=chip(cx,y,"0 s",arrow=False)+8; cx+=word(cx,y,".  Fire")+4; chip(cx,y,"once")
yT=y
# ---- callouts
CXT=PX+PW+18
def callout(yy,lines,ax=PX+PW-6):
    o.append(f"<path d='M{ax} {yy+10} L{CXT-6} {yy+8}' stroke='#5A5A66' stroke-dasharray='3 3' fill='none'/>")
    for i,l in enumerate(lines): t(CXT,yy+12+i*14,l,"#C8C8D0",sz=11.5)
callout(yA,["A ROW = WHO · IS / HAS · VALUE, as a sentence; an icon only","INSIDE the chip it describes (skull on destroyed …)."])
callout(yB,["WHO can be a group: any / at least N / all / none","of a side or of a unit's men  ⇒ CountMatching."])
callout(yD,["Friendly facts, not component paths: strength,","ammunition, under fire … (registered readers)."])
callout(yE,["NOT is a choice of the verb: is / is not —","no separate operator to place."])
callout(yF,["An unfilled blank: red frame + the reason;","Apply stays disabled until the rule is complete."])
callout(yG-30,["One nesting level: a group says All / Any / None,","drawn as a tinted box."])
callout(yH,["Anything the friendly view cannot draw is ONE","dashed row — kept, never rewritten (Expert view)."])
callout(yAdd,["Add condition opens the menu on the right;","From preset inserts pre-filled rows."])
callout(yT,["Timing belongs to the rule (holdFor · delay ·","repeat), written as the end of the sentence."])
t(CXT,PY+46,"LIVE: ✓ / ✗ per row and the number behind it","#C8C8D0",sz=11.5)
t(CXT,PY+60,"(the existing Live strip, matched by the same sentence)","#C8C8D0",sz=11.5)
# ---- the Add condition menu
MX,MY=20,616
r(MX,MY,1200,196,"#141418",stroke=SEP,rx=3)
t(MX+10,MY+18,"Add condition — grouped, filtered to what fits a rule; type to search","#C8C8D0",sz=12)
cols=[("A unit",0xf007,BLUE,["is inside an area","is alive / destroyed","strength is above / below","ammunition is above / below","is under fire","sees / knows about a unit","was hit (an event)"]),
      ("A group of units",0xf0c0,BLUE,["any of a side is inside …","at least N of … are destroyed","all of a unit's men are …","none of … is …"]),
      ("Objectives & rules",0xf140,GREEN,["objective is achieved / failed","objective is active","rule has fired (n times)","phase has begun (E11)"]),
      ("Time & presets",0xf017,BLUE,["time is after / before / between","PRESET  Take and hold an area","PRESET  Reach an area","PRESET  Side has lost N men","PRESET  Strength below …"])]
cx=MX+10
for title,g,gc,items in cols:
    fa(g,cx,MY+30,gc,13); t(cx+18,MY+42,title,TXT,sz=12.5)
    for i,it in enumerate(items): t(cx+18,MY+62+i*17,it,"#C8C8D0" if not it.startswith("PRESET") else VIOLET,sz=12)
    cx+=290
t(MX+10,MY+188,"Debugging kinds (trace buffer, ring entry, hit tag) appear only in the replay search and breakpoints — a kind's Contexts decides.",DIS,sz=11.5)
o.append("</svg>")
open(sys.argv[1],"w",encoding="utf-8").write("\n".join(o))
