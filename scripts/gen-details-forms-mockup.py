# Generates docs/UX/details-forms-visual.svg - an ImGui-default-dark mockup of the Details forms and their states.
# Regenerate: python3 scripts/gen-details-forms-mockup.py docs/UX/details-forms-visual.svg  (needs Pillow + fonttools; reads FDP/Data/Fonts/fa-solid-900.ttf)
import sys
import base64, io
from PIL import Image
import os
_A=Image.open(os.path.join(os.path.dirname(os.path.abspath(__file__)),"..","FDP","Data","Icons","famfamfam-silk.png")).convert("RGBA")
def _cell(c):
    r=c.rstrip("0123456789"); col=int(c[len(r):])-1
    ri=ord(r)-97 if len(r)==1 else 26+ord(r[1])-97
    b=io.BytesIO(); _A.crop((col*16,ri*16,col*16+16,ri*16+16)).save(b,"PNG"); return base64.b64encode(b.getvalue()).decode()
from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen
_FA=TTFont(os.path.join(os.path.dirname(os.path.abspath(__file__)),"..","FDP","Data","Fonts","fa-solid-900.ttf"))
_FAGS=_FA.getGlyphSet(); _FACMAP=_FA.getBestCmap(); _FAUPM=_FA["head"].unitsPerEm
def fa(cp,x,y,color,sz=14):
    """Draw the real FontAwesome 6 glyph `cp` (code point) at (x,y) top-left, sz px tall, in `color`."""
    name=_FACMAP[cp]; pen=SVGPathPen(_FAGS); _FAGS[name].draw(pen)
    k=sz/_FAUPM; asc=_FA["hhea"].ascent
    o.append(f"<path d='{pen.getCommands()}' fill='{color}' transform='translate({x},{y+asc*k}) scale({k},{-k})'/>")
_SILK={k:_cell(k) for k in ["i14","v6","a24","h1","t9","c23"]}
def silk(cell,x,y,sz=16):
    o.append(f"<image x='{x}' y='{y}' width='{sz}' height='{sz}' href='data:image/png;base64,{_SILK[cell]}' style='image-rendering:pixelated'/>")
W,H=1240,900
o=[]
def a(s): o.append(s)
# ImGui default dark (flattened over WindowBg #0F0F0F)
BG="#0F0F0F"; TITLE="#294A7A"; FRAME="#1D2F48"; FRAMEH="#2A4D7A"; BTN="#23415F"; HDR="#1C344F"
TXT="#FFFFFF"; DIS="#808080"; BORDER="#3A3A44"; SEP="#4A4A55"; TAB="#1F3A5C"; TABSEL="#3369AD"; ACC="#4296FA"
ERR="#E65555"; ERRBG="#3A1A1A"; PEND="#F2C033"; PENDBG="#3A3112"; INFO="#6AB0FF"; LOCK="#9A9AA6"
FONT="font-family='Roboto, Segoe UI, Arial, sans-serif'"
def t(x,y,s,c=TXT,sz=13,anchor="start",w="normal",st=""):
    s=s.replace("&","&amp;").replace("<","&lt;")
    a(f"<text x='{x}' y='{y}' fill='{c}' font-size='{sz}' {FONT} text-anchor='{anchor}' font-weight='{w}' {st}>{s}</text>")
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
def r(x,y,w,h,f,stroke=None,rx=0,sw=1,op=1):
    st=f" stroke='{stroke}' stroke-width='{sw}'" if stroke else ""
    a(f"<rect x='{x}' y='{y}' width='{w}' height='{h}' fill='{f}' rx='{rx}'{st} opacity='{op}'/>")
# icons (stand-ins for the FontAwesome 6 glyphs the app has merged)
def hourglass(x,y,c=PEND):
    return fa(0xf252,x,y-1,c)
def _old_hourglass(x,y,c=PEND):
    a(f"<path d='M{x} {y} h10 M{x} {y+13} h10 M{x+1} {y} L{x+9} {y+13} M{x+9} {y} L{x+1} {y+13}' stroke='{c}' stroke-width='1.6' fill='none'/>")
def xcircle(x,y,c=ERR):
    return fa(0xf057,x-1,y-1,c)
def _old_xcircle(x,y,c=ERR):
    a(f"<circle cx='{x+6}' cy='{y+6}' r='6.5' fill='{c}'/><path d='M{x+3} {y+3} L{x+9} {y+9} M{x+9} {y+3} L{x+3} {y+9}' stroke='#1A0A0A' stroke-width='1.8'/>")
def lock(x,y,c=LOCK):
    return fa(0xf023,x,y-1,c)
def _old_lock(x,y,c=LOCK):
    a(f"<rect x='{x}' y='{y+5}' width='11' height='8' rx='1.5' fill='{c}'/><path d='M{x+2.5} {y+5} v-2 a3 3 0 0 1 6 0 v2' stroke='{c}' stroke-width='1.6' fill='none'/>")
def reset(x,y,c=INFO):
    return fa(0xf2ea,x-1,y-1,c)
def _old_reset(x,y,c=INFO):
    a(f"<path d='M{x+10} {y+6} a5 5 0 1 1 -2 -4' stroke='{c}' stroke-width='1.6' fill='none'/><path d='M{x+6} {y-1} l3 2 l-3 2' stroke='{c}' stroke-width='1.6' fill='none'/>")
def warn(x,y,c=PEND):
    return fa(0xf071,x-1,y-1,c)
def _old_warn(x,y,c=PEND):
    a(f"<path d='M{x+6} {y} L{x+12.5} {y+12} L{x-0.5} {y+12} Z' fill='{c}'/><rect x='{x+5.2}' y='{y+4}' width='1.6' height='4.5' fill='#1A1400'/><rect x='{x+5.2}' y='{y+9.3}' width='1.6' height='1.6' fill='#1A1400'/>")
def combo(x,y,w,txt,c=TXT,hint=False):
    r(x,y,w,19,FRAME); r(x+w-19,y,19,19,BTN)
    a(f"<path d='M{x+w-14} {y+7} l4.5 5 l4.5 -5 z' fill='{TXT}'/>")
    t(x+5,y+14,txt,DIS if hint else c)
def field(x,y,w,txt,c=TXT,border=None,hint=False):
    r(x,y,w,19,FRAME,stroke=border,sw=1.2)
    t(x+5,y+14,txt,DIS if hint else c)
def slider(x,y,w,val,label,dis=False):
    r(x,y,w,19,FRAME,op=0.55 if dis else 1)
    gx=x+2+val*(w-14); r(gx,y+2,10,15,ACC if not dis else "#3B4C63",rx=1)
    t(x+w/2,y+14,label,DIS if dis else TXT,anchor="middle")
def checkbox(x,y,on,dis=False,mixed=False):
    r(x,y,19,19,FRAME,op=0.55 if dis else 1)
    if on: a(f"<path d='M{x+4} {y+10} l4 4 l7 -9' stroke='{ACC}' stroke-width='2.2' fill='none'/>")
    if mixed: r(x+5,y+8.5,9,2.2,ACC)
def button(x,y,w,txt,c=BTN):
    r(x,y,w,19,c); t(x+w/2,y+14,txt,anchor="middle")
def callout(x1,y1,x2,y2,lines,c="#C8C8D0"):
    a(f"<path d='M{x1} {y1} L{x2-6} {y2}' stroke='#5A5A66' stroke-width='1' stroke-dasharray='3 3' fill='none'/>")
    for i,l in enumerate(lines): t(x2,y2+4+i*14,l,c,sz=11.5)
def tooltip(x,y,w,lines,edge=SEP):
    h=10+16*len(lines); r(x,y,w,h,"#141418",stroke=edge,rx=3)
    for i,l in enumerate(lines): t(x+8,y+16+i*16,l,sz=12)
# canvas
a(f"<svg xmlns='http://www.w3.org/2000/svg' width='{W}' height='{H}' viewBox='0 0 {W} {H}'>")
r(0,0,W,H,"#1B1B1F")
t(20,26,"Details forms — visual design (ImGui default dark; icons are the real FontAwesome 6 glyphs, coloured by meaning; every element is one ImGui call — design §6.2)","#E8E8EE",sz=14,w="bold")
t(20,44,"Annotations in grey are not drawn by the app.","#8A8A96",sz=11.5)
# ---------- panel A: the Unit form, every row state ----------
PX,PY,PW,PH=20,60,520,560
r(PX,PY,PW,PH,BG,stroke=BORDER)
r(PX,PY,PW,20,TITLE); t(PX+8,PY+14,"Details — Alpha 1-1 (Rifleman, Friend)")
# tabs
tabbar(PX+8,PY+25,PW-16,["Unit","Crew & cargo","Orders","Brain","Components"],0,icons=[(0xf007,"#5AA9FF"),(0xf5b6,"#F29B38"),(0xf4d7,"#5BD06A"),(0xf5dc,"#E8A0C8"),(0xf121,"#A0A0AA")])
# mode chip
r(PX+PW-92,PY+2,84,16,"#163A24",rx=8); t(PX+PW-50,PY+14,"Edit mode","#7FD69A",sz=11,anchor="middle")
LX,CX,CW,SX=PX+14,PX+150,PW-196,PX+PW-38
y=PY+62
def sep(label):
    global y
    t(LX,y+12,label,"#9FB7D8",sz=13); a(f"<line x1='{LX+8+len(label)*7.4}' y1='{y+8}' x2='{PX+PW-14}' y2='{y+8}' stroke='{SEP}'/>"); y+=24
def row(label, draw, slot=None, bg=None, lab=TXT, h=25):
    global y
    if bg: r(PX+6,y-3,PW-12,h,bg)
    t(LX,y+14,label,lab); draw(y)
    if slot: slot(SX,y+3)
    y+=h
sep("Identity")
row("Name", lambda yy: field(CX,yy,CW,"Alpha 1-1"))
row("Side", lambda yy: combo(CX,yy,CW,"Hostile"), slot=hourglass, bg=None)
yPend=y-25
row("Type", lambda yy: field(CX,yy,CW,"Rifleman (US)",c=DIS), slot=None)
sep("State")
row("Posture", lambda yy: combo(CX,yy,CW,"Prone"))
yErr=y
row("Strength", lambda yy: slider(CX,yy,CW,0.75,"75 %"), slot=xcircle, bg=ERRBG)
yInv=y
def inv(yy):
    field(CX,yy,CW,"abc",border=ERR); t(CX,yy+33,"Enter a whole number 0 – 210","#F08080",sz=11.5)
row("Rounds, M4", inv, h=42)
row("Weapon", lambda yy: (combo(CX,yy,110,"Aimed"), t(CX+118,yy+14,"at",DIS), (button(CX+136,yy,CW-136,"     Pick on map…"), fa(0xf05b,CX+162,yy+3,"#7FD69A",sz=13))))
row("In hand", lambda yy: combo(CX,yy,CW,"Weapon"))
row("Playable", lambda yy: (checkbox(CX,yy,True), t(CX+26,yy+14,"slot",DIS), field(CX+52,yy,CW-52,"Alpha lead")))
sep("Character")
yReset=y
row("Bravery", lambda yy: slider(CX,yy,CW,0.8,"0.80  (type 0.50)"), slot=reset)
yLock=y
row("Skill", lambda yy: slider(CX,yy,CW,0.55,"Regular",dis=True), slot=lock, lab=DIS)
# expert
r(PX+6,y,PW-12,20,HDR); a(f"<path d='M{LX} {y+6} l6 4 l-6 4 z' fill='{TXT}'/>"); t(LX+14,y+14,"Details for experts"); y+=28
t(LX,y+12,"Components (debugging) — last tab, raw fields",DIS,sz=11.5)
# callouts for panel A
CXT=PX+PW+14
callout(SX+12,yPend+10,CXT,yPend+6,["PENDING (> 1 s): hourglass, amber; the new value shows","at once; tooltip: \"Waiting for node 3 (Brain)…\""])
callout(SX+12,yErr+10,CXT,yErr+8,["REFUSED / LOST: red ✗ in the status slot + faint red row;","value back to the real one; tooltip = the reason;","click ✗ to clear; + a sticky notification (panel D)"])
callout(CX+CW,yInv+10,CXT,yInv+30,["INVALID INPUT (local, before any send): red frame +","the reason under the field, visible while typing;","nothing is committed until it is valid"])
callout(SX+12,yReset+10,CXT,yReset+10,["AUTHORED ≠ TYPE: blue reset arrow — click resets","to the type's value; tooltip: \"Type 0.50 — reset\""])
callout(SX+12,yLock+10,CXT,yLock+28,["NOT ALLOWED FOR THIS SEAT: row disabled + lock;","tooltip on the disabled row (AllowWhenDisabled):","\"Your seat (Observer) cannot change Skill\""])
t(CXT,PY+30,"ROW ANATOMY: label | control (fills) | status slot (18 px, fixed)","#C8C8D0",sz=11.5,w="bold")
t(CXT,PY+45,"the slot is always reserved, so a mark never moves the layout;","#C8C8D0",sz=11.5)
t(CXT,PY+59,"one mark at a time: error > pending > lock > reset (coloured FA glyphs)","#C8C8D0",sz=11.5)
# tooltip example near strength row
t(LX,y+40,"hovering the ✗ on Strength shows:",DIS,sz=11.5)
tooltip(LX,y+48,262,["Refused by node 3 (Brain):","Alpha 1-1 is a player's unit"],edge=ERR)
# ---------- panel B: multi-select ----------
BX,BY,BW,BH=20,640,390,240
r(BX,BY,BW,BH,BG,stroke=BORDER); r(BX,BY,BW,20,TITLE); t(BX+8,BY+14,"Details — 3 units selected")
r(BX+8,BY+28,BW-16,20,HDR,rx=2); t(BX+16,BY+42,"Unit — fields common to all 3; a tick sets one value for all","#C8C8D0",sz=11.5)
yy=BY+60
for lab,on,txt,mixed in [("Side",True,"Friend",False),("Posture",False,"— mixed (2 values) —",True),("Strength",False,"— mixed (3 values) —",True),("Playable",True,"",False)]:
    checkbox(BX+12,yy,on); t(BX+38,yy+14,lab,TXT if on else DIS)
    if lab=="Playable": checkbox(BX+140,yy,False,mixed=True); t(BX+166,yy+14,"some are, some are not",DIS,sz=11.5)
    elif mixed: field(BX+140,yy,BW-160,txt,hint=True)
    else: combo(BX+140,yy,BW-160,txt)
    yy+=27
t(BX+12,yy+12,"Ticked ⇒ editable; Enter writes all 3, ONE undo step.",DIS,sz=11.5)
t(BX+12,yy+27,"A differing checkbox shows ▬ (drawn by hand).",DIS,sz=11.5)
# ---------- panel C: read-only + conflict ----------
CX0,CY0,CW0,CH0=426,640,400,240
r(CX0,CY0,CW0,CH0,BG,stroke=BORDER); r(CX0,CY0,CW0,20,TITLE); t(CX0+8,CY0+14,"Details — Alpha 1-1")
r(CX0+8,CY0+28,CW0-16,22,"#22283A",rx=2); fa(0xf05a,CX0+16,CY0+32,INFO); t(CX0+36,CY0+43,"Review — read-only · values at 00:12:34",INFO,sz=12)
yy=CY0+60
for lab,txt in [("Name","Alpha 1-1"),("Side","Friend")]:
    t(CX0+14,yy+14,lab,DIS); r(CX0+110,yy,CW0-130,19,FRAME,op=0.5); t(CX0+115,yy+14,txt,DIS); yy+=25
t(CX0+14,yy+10,"whole form inside BeginDisabled; no marks, no pickers",DIS,sz=11.5); yy+=24
# conflict bar
r(CX0+8,yy,CW0-16,46,PENDBG,stroke=PEND,rx=2); warn(CX0+16,yy+7)
t(CX0+36,yy+18,"Orders changed on node 2 (Instructor) while you edited",PEND,sz=12)
button(CX0+36,yy+24,80,"Reload"); button(CX0+122,yy+24,90,"Overwrite"); t(CX0+222,yy+38,"(precondition edits only)",DIS,sz=11)
yy+=56
t(CX0+14,yy+10,"CONFLICT: an amber bar at the top of the committing form",DIS,sz=11.5)
# ---------- panel D: sticky notifications ----------
DX,DY=840,640
t(DX,DY+12,"Sticky notifications — top-right of the main window,","#C8C8D0",sz=11.5,w="bold")
t(DX,DY+26,"newest on top, stay until dismissed (R-4127)","#C8C8D0",sz=11.5,w="bold")
def note(y,col,icon,title,body,btns):
    r(DX,y,380,62,"#18181C",stroke=BORDER,rx=3); r(DX,y,4,62,col)
    icon(DX+14,y+10); t(DX+34,y+20,title,TXT,sz=13); t(DX+34,y+37,body,"#C8C8D0",sz=12)
    bx=DX+34
    for b in btns: w=12+len(b)*7; button(bx,y+42-1,w,b); bx+=w+6
    t(DX+366,y+18,"×",DIS,sz=16,anchor="middle")
note(DY+40,ERR,xcircle,"Strength not changed","Alpha 1-1 is a player's unit (node 3)",["Show"])
note(DY+110,PEND,warn,"Orders changed on node 2","while you were editing Bravo's plan",["Show","Reload","Overwrite"])
t(DX,DY+192,"+ 2 more…  (stack shows 3; the Messages panel keeps the history)",DIS,sz=11.5)
a("</svg>")
open(sys.argv[1],"w",encoding="utf-8").write("\n".join(o))
