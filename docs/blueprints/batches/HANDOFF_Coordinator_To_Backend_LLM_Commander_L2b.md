<!--STATUS
state: LIVE — dispatched, not started — ⚠ AFTER HANDOFF_Coordinator_To_Backend_LLM_Commander_L2.md (CE-4679)
updated: 2026-11-22
current-answer: the whole file
related-designs:
  - ../../designs/llm-commander/DESIGN_LLM_Commander.md — ⭐ THE design: §5b (B-1 … B-6) is this slice; §3 the feed, §5a L-1 as built
  - HANDOFF_Coordinator_To_Backend_LLM_Commander_L2.md — the slice before this one (the routes, ExternalMind, the clock pause)
  - ../../designs/group-maneuvers/DESIGN_Platoon_Attack_Defend_Clear.md — the platoon plays run_play hands to (behaviors' code)
  - ../../DESIGN_Scenario_Rules.md — objectives and VisibleSides
  - PROTOCOL_Autonomous_Lanes.md — how you work
-->

# HANDOFF — coordinator → `backend`: THE LLM COMMANDER, slice L-2b (CE-4680)

**For:** backend (ids `CE-3000`…, `R-3000`…). **From:** coordinator. **Dispatched at the commit that adds this file**; ⛔ scope FROZEN there;
a later document that invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-22`: *"what our game ai platoon commander now do? can his LLM replacement bring something interesting? what info are we going to
share with the LLM?"* → the coordinator's answer and lean (design §5b) → *"Yes"*.

## ⭐ Priority
**After L-2 (CE-4679) is done** — it builds on L-2's routes and the Haiku worker. Status: continue `STATUS_Backend_LLM_Commander.md` (a new §,
L-2b); decisions to `DECISIONS_Backend.md`. ⚠ The plays and the command chain are behaviors' code: CALL them, ⛔ do not change them; a change they
need is a NOTE to behaviors.

## Read first
Design §5b end to end, and why: the game-AI platoon commander EXECUTES a given play well; the LLM's value is DECIDING. L-1 gave him squad orders
only, so he could not use the plays. L-2b gives him the plays as tools and the information a decision needs.

## What L-2b builds (design §5b)

| # | builds | done when (rail, red-proved) |
|---|---|---|
| B-1 | ⭐ **`run_play(play, objective…)` / `stop_play()`**: the commander's own task is one of `PlatoonAttack` · `PlatoonDefend` · `PlatoonBound` · `PlatoonHold`, with the objective / position from the gazetteer; the play orders the squads. ⭐ A direct squad order while a play runs ENDS the play first (its squad orders released) and says so in an `order-state` item. Added to `/tools`. ⚠ **Measure first** (claim table in your status file): how a play is given to a body-less commander as its TASK (the order path the platoon's superior uses), and that ending it releases its squad orders — a premise that fails ⇒ that item STOPS, the rest goes on | the scripted mind runs `PlatoonAttack` on the house: the support squad reports SET BEFORE the assault squad moves (the play's choreography, not the mind's); a direct order mid-play ends it and the squad takes the direct order |
| B-2 | **the mission brief** in the prefix — Task · Purpose · Constraints · Time — an authoring-only managed `CommanderBrief` on the seat (`[DataPolicy(NoReplay)]`), set in the scenario | the briefing's prefix carries it; a scenario round-trip keeps it |
| B-3 | **the objectives line** in every snapshot: each objective whose `VisibleSides` includes the seat's side — name, state, owner, progress | an objective hidden from Blue never appears (rail) |
| B-4 | **each squad's task and progress** in the snapshot: its current order (kind, where), latest progress (Set, Foothold, FallingBack…), strength / ammo colour, the age of its last report (`CommandPicture.Subordinates` + `SubordinateOrders`) | a squad given Attack then reporting Set reads so in the next snapshot |
| B-5 | **the enemy as reported**: size, activity, equipment, the report's age, seen or only heard — in the snapshot and the report radio lines | a heard-only contact says "heard"; an old report says its age |
| B-6 | **one-line terrain notes on named places**: an optional `Note` on a named Point / tactical area (scenario field), listed with the places in the prefix; `llm-platoon-attack` gets notes on its places | the prefix lists each place with its note |

⭐ **Keep the briefing short** (an 8 B model's context, L-3): report the briefing's size in characters before and after L-2b for
`llm-platoon-attack`. ⛔ No ground truth: every new line comes from a report to the commander or from his scenario.

## The proof
1. `llm-platoon-attack` with the scripted mind, changed to use `run_play(PlatoonAttack, house)`, passes its checks.
2. ⭐ **The Haiku worker plays it again** (as in L-2, routes only) with the brief, the objectives and the plays; the report quotes 5–10 lines of
   the radio log and its answers, and says whether it used `run_play`, direct orders, or both — and whether its reasons cite the mission.

## Gates (CLAUDE.md rule 8)
The L-1 / L-2 rails + the new ones (each red-proved) + the plays' own suites that `run_play` exercises (name them — behaviors' code, run not
changed) + `ScenarioChecksTests` + the scenario. Fold any deviation into design §5b (obligation ⑤). Report the ids you allocated.
