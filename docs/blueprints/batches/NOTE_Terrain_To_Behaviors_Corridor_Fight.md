<!--STATUS
state: LIVE — a NOTE (terrain → behaviors): the red rows of the street fight are your targets · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - HANDOFF_Coordinator_To_Behaviors_Street_Fight.md — CE-4666…CE-4669, which these rows measure
  - HANDOFF_Coordinator_To_Terrain_Street_Fight_Scenario.md + STEER_Coordinator_To_Terrain_Street_Fight_Corridor.md — CE-4672 / CE-4673 (this lane's)
  - ../../DESIGN_Building_Interiors.md — §3l.8 the cover measured along the corridor
  - ../../DESIGN_Scenario_Regression.md — §3 the `cover` / `bunched` / `dead` cues
-->
# NOTE terrain → behaviors: the street fight runs in the repo — `pc-corridor-fight`, and which rows are RED (CE-4672)

⭐ **The scenario:** `scenarios/tests/pc-corridor-fight` on the new terrain **`wide-corridor`** (the user's ground, per the STEER: a straight OPEN grass
corridor 30 m wide, y 185..215, centreline y 200, between detached enterable family houses; garden walls, fences, hedges at its edges). The report's setup:
Red Platoon `PlatoonDefend`, line squads' posts ON the centreline (x 400, x 440), `SearchRadius` 10, reserve 40 m behind (x 480); Blue Platoon `PlatoonAttack`
on (420, 200) from 160 m down the corridor under a Blue HQ. Behaviours unchanged. Run: `scripts/scenario-regression.sh pc-corridor-fight` (~6.5 min; two runs
gave the SAME verdicts at the SAME times — deterministic).

## The verdicts (two runs, identical)

| row | verdict | at |
|---|---|---|
| Red defends · Blue attacks · both fire · a man of each side is hit · no friendly hits | ✅ | Blue fires at **5.4 s** (from ~160 m), Red opens only at **61.1 s** |
| ⛔ **Red Squad 1's line in cover within its first minute** (no two men with a cover point within 3 m) | **RED** | 60 s |
| ⛔ **Red Squad 2's line in cover within its first minute** | **RED** | 60 s |
| ⛔ **Blue Squad 2 never wholly in the open after 90 s** | **RED** | 90 s |
| ✅ Blue Squad 1 / 3 — never wholly in the open | green | |
| ⛔ **Blue Squad 2 never bunched** (every man within 2 m of the squad's centre) after 30 s | **RED** | 30 s |
| ✅ every other squad never bunched | green | |
| ✅ a dead man runs no behaviour (`BehaviorState.ActiveBehaviorHash` != 0 on Health ≤ 0) | green — men ARE hit (not vacuous); ⚠ if `BehaviorState` is removed on death, this row cannot see CE-4668's case: say which component a dead man still runs from and I re-point it | |

## What terrain measured that splits your CE-4666 question ("no cover found" vs "found but not used")

⭐ **It is the SEARCH, not the data** (CE-4673, `CorridorCoverTests`, DESIGN_Building_Interiors §3l.8): from a post ON the centreline there are **0** cover points
within 10 m and 40+ within 25 m; the nearest is **13.5–16 m** away (the corridor's edges). A 30 m corridor puts its cover 15 m from the centre — `SearchRadius` 10
cannot reach it. ⇒ your step 1 ("rerun with `SearchRadius` 20–25") is a one-line scenario edit: if Red's rows go green with 25, the cover is found and used; if
they stay red, it is found and NOT used. ⚠ Cover is still material-blind (§3l C9): a fence or a hedge counts as cover like a brick wall.

## Not expressible yet (said, not faked)

- *"nobody suppressing with no ammo or no live target"* (CE-4667) — needs the ammo / target state's component path in the entity dump; name it and I add the row
  (a `field` cue with `dead`-style filter, or a new cue).

## CE-4665 (from the STEER)
⛔ **Not reproduced:** the scenario loaded LIVE four times in one editor, 30 s each: **9 · 9 · 9 · 7** of 24 infantry moved (`FailedBlocked` 1–3 on every load,
the first too) — no reload leak. Recorded in the CE-4665 row.
