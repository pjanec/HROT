<!--STATUS
state: LIVE — dispatched, not started (re-stamped as BUILD-ONLY under rule 1a: the design is the coordinator's, Map 3D §6am)
updated: 2026-11-14
current-answer: the whole file
related-designs:
  - ../../DESIGN_Map_3D_Mode.md — ⭐ §6am THE design (INVENTORY, classes, one frame, who draws what, D-1…D-10, acceptance) — build it; ⚠ M13 is superseded for the default
  - ../../DESIGN_People_Display.md — §7e / §7f the card's mind-state and health rows (linked both ways)
  - ../../UX/labels-behind-terrain.svg — the mockup (scope: the cards, place labels and the Units layer rows; the 3-D scene is context)
-->

# HANDOFF — coordinator → `ui`: BUILD unit cards hidden behind terrain, one card per unit (CE-4582, Map 3D §6am)

**For:** the `ui` lane. **From:** coordinator. **Dispatched at `<sha>`**; ⛔ **scope FROZEN there** — later documents are FYI; one that
invalidates an item ⇒ STOP that item and report it (R-106). ⭐ Autonomous, no questions. 🔒 User, `2026-11-14`: *"the ui lane can take the labels
hiding behind obstacles"* · *"no the design must happen here"* — ⭐ **the design is done: [Map 3D §6am](../../DESIGN_Map_3D_Mode.md). You
BUILD it; a deviation is folded back into §6am (obligation ⑤), never decided silently.**

## 0. Start

`git fetch origin ui coordinator` · on `ui`, `git merge origin/coordinator` · restore the test projects you will run once · push
`chore: started labels behind terrain at <sha>`. Read §6am end to end and check its diagrams against the code before building (obligation ③).

## 1. The items — in this order

| # | §6am | build |
|---|---|---|
| L-1 | D-6 | the no-orphan-card rule in `EntityCards.Card` (`LabelsLayer` defined once in Contracts) |
| L-2 | D-7 | the 8 free per-unit text sites → card rows on their own layers, the row numbers of §6am's mockup B; a member's line on the member's card |
| L-3 | D-9 | the 3-D bodies honour Units ▸ Entities (not drawn, not pickable) |
| L-4 | D-1, D-2, D-4, D-5, D-8 | **step 1**: `CardOcclusion` — cards and place labels depth-tested at their anchor's depth; the selected unit on top; the Units ▸ *Hide labels behind terrain* row, ON by default |
| L-5 | D-3 | **step 2**: the occluder depth pass (ground + objects, half size, depth only) and the card shader — ⭐ **measured against the budget (≤ 1.0 ms added at §6ak.5's whole-terrain and street views)**; over budget ⇒ keep step 1 and report the numbers for the user. ⚠ Linux under Xvfb is software GL (not representative) — there it is a smoke test only; the budget needs the Windows session, so if it was not measured on Windows, leave step 2 behind a switch, default OFF, and say so |

## 2. Checks

| | |
|---|---|
| ⭐ T-1 first | the card / label / layer suites; `Map3DFrameRail` and every `…FrameRail` touched, under **`xvfb-run -a`** (they skip silently without a display) |
| ⭐ §6am acceptance 1–7 | each a rail, each red-proved by one compiling mutation |
| ⛔ R-2084 | 0 B per steady frame, proved |
| ⭐ R-4146 fidelity | render the mockup's states A–E, compare, fix until they match; the side-by-side in the report (scratchpad, ⛔ not in git) |
| ⭐ threaded editor | the rails with the threaded node on and off (it is ON by default) |
| ⭐ as-built | §6am gets an "as built" sub-section (deviations argued, prior text marked superseded); CE-4582 ticked; ids from ui's block |

Report per the gate-report contract (CLAUDE.md rule 8).
