<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-14
current-answer: the whole file
related-designs:
  - ../../DESIGN_Map_3D_Mode.md — ⭐ OWNS M13 (cards drawn on top — this REVERSES it for the default), §3.4 / §3.6 the card and its rows, §6ak the render bands (D-6 the pick records only drawn boxes, D-7 a selected unit always full)
  - ../../DESIGN_People_Display.md — §7e U20 / U20a the card's rows (the mind-state bar), §7f the health bar
  - ../../UX/DESIGN_Colour_Icons.md — the row icons and colours
-->

# HANDOFF — coordinator → `ui`: unit labels hidden behind terrain, one card per unit, no free-floating unit text (CE-4582)

**For:** the `ui` lane. **From:** coordinator. **Dispatched at `c59a92d81`**; ⛔ **scope FROZEN there** — later documents are FYI; one that
invalidates an item ⇒ STOP that item and report it (R-106). ⭐ Autonomous, no questions: the decisions are the user's (below); what is left open is
decided the closest way and named in the report.
🔒 User, `2026-11-14`: *"the ui lane can take the labels hiding behind obstacles"*. The task and every decision are in
[CE-4582](../Blueprint_Issues_Tracker.md) — ⭐ **read the row end to end; it is the frame.**

## 0. Start

`git fetch origin ui coordinator` · on `ui`, `git merge origin/coordinator` (it carries the parallel programme, the threaded editor and CE-4582's
row) · restore the test projects you will run once · push `chore: started labels behind terrain at <sha>`.

## 1. The frame — decided by the user (CE-4582)

| # | decided | from the row |
|---|---|---|
| F-1 | **(A) depth-tested cards**: after the 3-D pass each card / label is drawn at its anchor's depth, depth TEST on, depth WRITE off, against the scene's depth buffer; a card half behind a ridge is cut at the ridge | ⛔ rejected B (a ray per label — ms per frame at 1 000 labels on the UI thread), C (depth read-back — no GL interop exists) |
| F-2 | **trees are drawn AFTER the cards** — only ground and buildings hide a card | a unit in a forest keeps its card |
| F-3 | **the selected unit's card is always visible** | as its body is always Full (§6ak D-7) |
| F-4 | **a layers-panel toggle**, Units ▸ *Hide labels behind terrain*, ON by default; OFF = today's on-top cards | reverses M13 for the default |
| F-5 | **one occlusion decision per UNIT, never per piece** — the whole label stack hides together | the stress bar / BROKEN rows included |
| F-6 | **no orphan card**: an optional row (Mind state, Concealed, …) draws only while its card's Labels layer is on — the row is an optional PART of the label | today Labels off + Mind state on leaves a card holding only the bar |
| F-7 | **no free-floating per-unit text** — every per-unit text becomes a row of ITS card, keeping its own layer (Actions, Utility, UnitDetails, SquadCommand) and showing only while Labels is on | the row's list: `ActionStatusGizmo:57`, `UtilityDecisionGizmo:30`, `StanceGizmo:30`, `EntityRotationGizmo:60`, `PeekAndFireGizmo:60`, `SquadGizmo:40` (commander) + `:54` (each member's own card), `CommandPictureGizmo:46` (commander) + `:93` (the subordinate's card); `EffectStateGizmo:39`'s free fallback |
| F-8 | **text at a PLACE stays free but is depth-tested the same way** | the row's list: `PeekAndFireGizmo:54`, `HillAttackGizmo:89`, `CommandPictureGizmo:81/:99`, `MissionPresentationGizmo:63`, `EqsSensorGizmo:127`, `DetonationGizmo:62`, `ProjectilePresentationGizmo:76`, `ChargeMarker:51`, `NamedPointGizmo`, `SectorAreasGizmo`, `DangerAreaGizmo`, `SpatialGridGizmo`, `ShapeDoneMark` |
| F-9 | **the 3-D bodies honour Units ▸ Entities** — a body hidden by it is not drawn and not pickable | 📐 `EntityBodyLayer3D` reads no layer mask; the Entities row (`LayerControlGizmo.cs:106`) gates only the gizmo symbols |

## 2. Step 1 — the design (yours, before code)

Per CLAUDE.md (UML, diagram first, mockup before a UI build): **re-verify the row's line numbers** (they were measured on `exp/threaded-build`
today; grep each), then extend **`DESIGN_Map_3D_Mode.md`** with a new section — `INVENTORY` (graph + grep, the queries named), a
`classDiagram` (the card, its rows, who adds them, the occlusion pass), a `sequenceDiagram` (one frame: 3-D pass → cards depth-tested → trees),
a module diagram (who draws what, each frame), and **a mockup** over `scripts/mockup_kit.py` (a ridge cutting a card; the selected card on top;
a member's card with the moved rows; the layers-panel toggle). Mark M13 superseded for the default; link People Display §7e both ways. Set the
card's row order there (People Display / Map 3D §3.6 carry today's slots). ⛔ Commit the design before the build.

## 3. Step 2 — the build, and its checks

| | |
|---|---|
| ⭐ T-1 first | the card / label / layer suites and the 3-D frame rails — `Map3DFrameRail` and every `…FrameRail` the change touches, run under **`xvfb-run -a`** (they skip silently without a display) |
| ⭐ new rails | a card behind a ridge is cut / hidden and in front is drawn; the selected one always drawn; toggle OFF ⇒ today's on-top cards; Labels off ⇒ no orphan row; a moved row appears on the right card (the member's E/R on the member, not the commander); Entities off ⇒ no body, no pick |
| ⛔ hot path (R-2084) | the occlusion pass and the card rows allocate nothing per frame — prove it (`GC.GetAllocatedBytesForCurrentThread` across a steady frame) |
| ⭐ fidelity (R-4146) | render each mockup state, compare, fix until they match; the side-by-side in the report (scratchpad, ⛔ not in git) |
| ⭐ threaded editor | the cards are drawn from the UI world A (the threaded node is ON by default) — run the rails with it on and off |
| ⭐ as-built | into Map 3D's new section (obligation ⑤); the row ticked; ids allocated from ui's block |

Report per the gate-report contract (CLAUDE.md rule 8).
