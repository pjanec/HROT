<!--STATUS
state: LIVE — dispatched, not started — ⚠ AFTER terrain's CE-4692 (BL-S1) lands
updated: 2026-11-25
current-answer: the whole file
related-designs:
  - ../../designs/engineering/DESIGN_Bridge_Laying.md — ⭐ THE design (rev 2, leans APPROVED R-4299): BL-10, BL-11, §5 BL-S3 / BL-S4
  - HANDOFF_Coordinator_To_Terrain_Bridge_Laying.md — terrain's half (the walkable obstacle, its destruction) this builds on
  - ../../DESIGN_Vehicle_Breaching.md — §7.6 the bulldozer: the engineer pattern to follow
  - ../../DESIGN_Orders_Params_Entity_Classes.md — §8 / §9 the orders, the routes, the gestures
  - PROTOCOL_Autonomous_Lanes.md — how you work
-->

# HANDOFF — coordinator → `backend`: BRIDGE LAYING, the engineer (CE-4693 — BL-S3, BL-S4)

**For:** backend. **From:** coordinator. **Dispatched at the commit that adds this file**; ⛔ scope FROZEN there; a later document that
invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-25`: *"the vehicle was just a way to have someone to order to create a bridge; no problem if it lays long ponton bridge for
wider rivers"* · *"leans approved, write the hand offs"* (R-4299).

## ⭐ Priority
**Start when terrain's BL-S1 is merged** (CE-4692 ① ② ③ — a placed bridge obstacle that bakes); until then take your other rows. Status in a
new `STATUS_Backend_Bridge_Laying.md`; decisions to `DECISIONS_Backend.md`. ⚠ The bake, the world and the road step are terrain's code: call them;
a change is a NOTE to terrain.

## Read first
The design, BL-10 and BL-11 above all, and the bulldozer precedent (Breaching §7.6: `ClearObstacleBehavior` + `DozerCutSystem`).

## What you build

| # | builds (design §) | done when (rail, red-proved) |
|---|---|---|
| ① | **the real figures** (Q-6): an assault bridge and a floating bridge — span, width, deck height, build and recover time, load class — from open sources, recorded in the TKB with the source; replace terrain's placeholders | a TKB rail reads them; the doc names the sources |
| ② | **the engineer types**: an assault bridge layer and a floating-bridge engineer (TKB, DIS slot — check 1.1.*.9.3 is free, class rows, a rail like `MineClearerTkbTests`) | the types load; their class offers LayBridge / RecoverBridge |
| ③ | **`BridgeCarried`** (unmanaged: count, bridge type, state Carried / Laying / Recovering, progress) + its descriptor and translator; a renderer-facing appearance state (the look library) | a renderer reads carried / laying / laid |
| ④ | **the orders** (BL-11): `[Order]` `LayBridge {NearBank, FarBank}` and `RecoverBridge {Bridge}`; refused with a reason when the water gap along the line exceeds the span, or a deck end is not on ground; HTTP + the order menu; the two-point map pick is ui's gesture (a NOTE to ui) — HTTP and the LLM commander's seat take two points / named places now | an over-wide gap is refused with its reason, nothing laid |
| ⑤ | **the behaviours**: drive to the near bank, align, set `BridgeCarried.State` on its own vehicle (R-4212); recover from either end | the engineer reaches the bank and starts |
| ⑥ | **`BridgeWorkSystem`** (BL-10): on the node that moves the engineer's hull, never in a replay — after `BuildSeconds` spawns terrain's bridge obstacle (Constructing → Open when baked); after `RecoverSeconds` removes it and the count goes back | lay, then recover: the river is a barrier again |

## Scenarios (R-4277)

| scenario | expect rows (state and path existence, never a time) |
|---|---|
| `bl-assault-crossing` | an engineer lays an assault bridge under smoke, a platoon crosses: the bridge Open · every squad on the far bank |
| `bl-recover` | the same bridge recovered: the bridge gone · the engineer carries it again · a tank ordered across does not reach the far bank |
| `bl-too-wide` | LayBridge over a gap wider than the span: refused with its reason · no bridge |
| `bl-floating-bridge` (BL-S4) | a floating-bridge engineer bridges a wide river; a column crosses |
| ⭐ **`bl-river-crossing`** — the WHOLE feature, end to end, by OBJECTIVES | Blue must cross a river held by Red's outpost on the far bank: objective **"Bridge laid"** (an assault bridge Open at the crossing site) · objective **"Far bank seized"** (a HoldArea on the far bank, Blue only) · objective **"Bridge recovered"** (after the crossing, the bridge back on its engineer) · Red's demolition team tries to blow the bridge: objective **"Bridge held"** passes if Blue silences it before it fires, else the row judges the fall (the bridge gone, a vehicle on it destroyed, the river a barrier again). Every objective's expected end state is an expect row |

## ⛔⛔ DONE = THE SCENARIOS PASS (R-4277; user `2026-11-25`: *"do not forget the feature test scenario with objectives for testing that all, without that nothing is considered done"*)

- Every scenario above is a **feature test**: its `description` says **What** happens and how to **Judge** it, and its `checks.json` states the
  same as machine-checked `expect` rows — ⭐ where the expectation is an outcome (a bank reached, a bridge held or lost), say it as a scenario
  **OBJECTIVE** (Scenario Rules: the objective's state is what the row judges), not only a cue.
- ⛔ **The row is NOT done until `scripts/scenario-regression.sh <name>` passes for EVERY scenario listed** (deterministic editor run). Green rails
  alone are not "the feature works". A scenario that cannot pass ⇒ the row stays open, the reason in the status file.
- The report lists each scenario with its expect rows and its result.

## ⛔ Not in this
SUMO · load classes · the IG's laying animation · a re-plan when a bridge opens (design Q-8 — accepted for v1).

## Gates (CLAUDE.md rule 8)
The touched suites + the engineer precedents' suites (`ClearObstacleBehavior`, the mine-clearer rails) + the order-vocabulary rails
(`OrderVocabularyTests`, `BehaviorParamsValidationTests`) + terrain's bridge rails (run, not changed) + the scenarios; the parallel env on the
work system and the behaviours. Report the ids you allocated.
