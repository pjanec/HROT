<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-15
current-answer: the whole file
related-designs:
  - ../../designs/vehicle-tactics/DESIGN_Motorcycle.md — ⭐ THE design (MC-1, PL-1…PL-3; §6 M-1v, M-2p)
  - ../../UX/DESIGN_Colour_Icons.md — the house style the mockup follows (R-4139)
-->

# HANDOFF — coordinator → `ui`: the motorcycle's look and the player on it (CE-4621 M-1v, M-2p) — AUTONOMOUS

**For:** the `ui` lane. **From:** coordinator. **Dispatched at `8f6a635e6`**; ⛔ scope FROZEN there.

🔒 User, `2026-11-15`: *"the UI might take the visual part of it"* · *"the motorcycle also needs to be possessable and player can also embark it as a
passenger (the one that can shoot while motorcycle drives)"* (R-4263).

## 0. Start
`git fetch origin coordinator ui backend`; on `ui`, merge `origin/coordinator`; push `chore: started motorcycle visuals at <sha>`. Status:
`STATUS_Ui_Motorcycle_Visuals.md`. ⭐ `backend` builds the type in its M-1 (CE-4621) at the same time — until it lands, build against a test
template with the class you agree in your notes; when backend's note names the type id and seat ids, bind to them (the radar SAM precedent).

## 1. The items
| # | builds | done when |
|---|---|---|
| **U-1** | ⭐ **a mockup FIRST** (house style, `scripts/mockup_kit.py`, R-4143): the bike in 2-D and 3-D, a rider and a pillion seated, the pillion's fire arc when selected | committed as SVG beside the design |
| **U-2** | the `Motorcycle` visual family + `ShapeKit` (TWO wheels — `CarKits` draws four; the riders seated, the pillion turned to his arc while he fires), the 2-D symbol, the picker glyph and the thumbnail | drawn in 2-D and 3-D; a frame rail under `xvfb-run -a` (report counts — a skip is not a pass); the mockup and the frame side by side in the report (⛔ not in git) |
| **U-3** | **PL-2 in first person**: a possessed man who *Boards* the pillion — the camera at the seat's eye, free look, fire inside the arc; outside it the HUD shows *out of arc* (backend's refusal reason) and nothing fires; ⚠ backend makes the sim side work (its M-2p) — you own the camera, the input and the HUD | a rail: the HUD cue appears outside the arc; a live check: board, fire, see the cue |
| **U-4** | **PL-1 driving** — the bike under the player's *Drive* through the existing first-person driving; the lean in a turn is visual only | a live check: drive, slowed off-road |

## 2. Gates and report
T-1 first (the 3-D model rails, the possession / first-person suites); display rails under `xvfb-run -a`; each new rail red-proved; 0 B per
steady frame (R-2084). Report: `STATUS_Ui_Motorcycle_Visuals.md` — per item, the fidelity side-by-side, the gate-report contract (rule 8), ids.
