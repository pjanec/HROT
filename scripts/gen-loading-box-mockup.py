# Generates docs/UX/loading-message-box-visual.svg — CE-4400 + CE-4406 (docs/DESIGN_Cluster_Load_Phase.md §11.7 ④, the loading screen).
# Scope: ONLY the loading message box (the text + Cancel) over the dimmed screen; the editor behind it is context and unchanged.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render, text_width

W, H = 1240, 640
s = Svg(W, H, "The loading message box — one solid dark panel round the text and Cancel (CE-4400 · CE-4406)")

def scene(x, y, w, h, title, lines, cancel=True, glyph=None):
    s.rect(x, y, w, h, "#2C2F36", stroke=C.BORDER)                       # the editor behind (context)
    for i in range(7): s.rect(x + 14, y + 16 + i * 30, w * 0.42, 18, "#3A3E48", rx=2)
    s.rect(x + w * 0.55, y + 16, w * 0.4, h * 0.6, "#34383F", rx=2)
    s.rect(x, y, w, h, "#000000", op=0.65)                                # the veil (unchanged: VeilAlpha 0.65)
    bw = max(text_width(l, 22) for l in lines) + 90
    bh = 38 + 30 * len(lines) + (58 if cancel else 20)
    bx, by = x + (w - bw) / 2, y + h * 0.382 - 40
    s.rect(bx, by, bw, bh, "#14161B", stroke="#4A4F5C", rx=6, sw=1.4)      # ★ the message box: solid, darker than the veil's result, a thin border
    ty = by + 40
    for i, l in enumerate(lines):
        s.text(bx + bw / 2, ty + i * 30, l, "#FFFFFF" if i == 0 else "#C8C8C8", 22 if i == 0 else 18, "middle")
    if cancel:
        cw = 96
        s.rect(bx + (bw - cw) / 2, by + bh - 46, cw, 28, C.BTN, rx=3); s.text(bx + bw / 2, by + bh - 27, "Cancel", C.TXT, 14, "middle")
    s.text(x + 8, y + h + 16, title, C.NOTE, 11.5)

scene(20, 60, 580, 400, "a scenario load (Cancel: the host's slave cancels the load)", ["Loading — editor: Terrain"])
scene(640, 60, 580, 400, "opening a recording — from the double-click until the Replay Browser is ready", ["Opening recording…"])
s.note(20, 500, ["★ ONE box for every load: text + Cancel in one panel, solid #14161B with a 1 px border (darker than the dimmed screen under it)",
                 "★ the same box for a scenario load and a recording open (the recording used to show nothing until the browser appeared)",
                 "★ a player's waiting screen is the same box without Cancel (it does not own the load)",
                 "unchanged: the veil (0.65), the text size rule (the film subtitles'), the golden line, Cancel under the text, the modal swallowing every click"], C.SUB, 12)
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "loading-message-box-visual.svg")
s.save(out)
if "--png" in sys.argv: render(out, sys.argv[sys.argv.index("--png") + 1], W, H)
print("wrote", out)
