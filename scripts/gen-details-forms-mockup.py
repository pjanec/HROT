# Generates docs/UX/details-forms-visual.svg - an ImGui-default-dark mockup of the Details forms and their states.
# Regenerate: python3 scripts/gen-details-forms-mockup.py docs/UX/details-forms-visual.svg  (needs fonttools; draws through scripts/mockup_kit.py)
import sys
from mockup_kit import Svg, C, FA
W,H=1240,900
_s=Svg(W,H,title=None)
o=_s.o
def a(x): o.append(x)
BG=C.BG; TITLE=C.TITLE; FRAME=C.FRAME; BTN=C.BTN; HDR=C.HDR; TXT=C.TXT; DIS=C.DIS; BORDER=C.BORDER; SEP=C.SEP; ACC=C.ACC
ERR=C.ERR; ERRBG=C.ERRROW; PEND=C.PEND; PENDBG=C.PENDROW; INFO=C.INFO; LOCK="#9A9AA6"
def t(x,y,s,c=TXT,sz=13,anchor="start",w="normal",st=""): _s.text(x,y,s,c,sz,anchor,w)
def r(x,y,w,h,f,stroke=None,rx=0,sw=1,op=1): _s.rect(x,y,w,h,f,stroke=stroke,rx=rx,sw=sw,op=op)
def fa(cp,x,y,color,sz=14): _s.glyph(cp,x,y,color,sz)
def tabbar(x,y,w_total,names,selected,icons=None): _s.tabs(x,y,w_total,[(n,(icons[i][0] if icons else None),(icons[i][1] if icons else None)) for i,n in enumerate(names)],selected)
def hourglass(x,y,c=PEND): fa(FA.HOURGLASS,x,y-1,c)
def xcircle(x,y,c=ERR): fa(FA.CIRCLE_XMARK,x-1,y-1,c)
def lock(x,y,c=LOCK): fa(FA.LOCK,x,y-1,c)
def reset(x,y,c=INFO): fa(FA.ROTATE_LEFT,x-1,y-1,c)
def warn(x,y,c=PEND): fa(FA.WARNING,x-1,y-1,c)
def combo(x,y,w,txt,c=TXT,hint=False): _s.combo(x,y,w,txt,hint=hint)
def field(x,y,w,txt,c=TXT,border=None,hint=False): _s.field(x,y,w,txt,c=c,border=border,hint=hint)
def slider(x,y,w,val,label,dis=False): _s.slider(x,y,w,val,label,dis)
def checkbox(x,y,on,dis=False,mixed=False): _s.checkbox(x,y,on,dis,mixed)
def button(x,y,w,txt,c=BTN): _s.button(x,y,w,txt,fill=c)
def callout(x1,y1,x2,y2,lines,c="#C8C8D0"): _s.callout(x1,y1,x2,y2,lines)
def tooltip(x,y,w,lines,edge=SEP): _s.tooltip(x,y,w,lines,edge)
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

_s.save(sys.argv[1])
