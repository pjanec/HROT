<!--STATUS
state: LIVE — dispatched · updated: 2026-11-24 · current-answer: the whole file
related-designs:
  - HANDOFF_Coordinator_To_Behaviors_Street_Fight.md — the street-fight rows CE-4666…CE-4669 (the same complaint, first reported on the source terrain)
  - STEER_Coordinator_To_Behaviors_Critical_Reload_And_Street_Fight.md — their priority
  - ../../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — OWNS PlatoonDefend / PlatoonAttack, the post's cover search, the base of fire
  - ../../designs/group-maneuvers/DESIGN_Squad_Tactics.md — OWNS SupportByFire / Suppress and the squad drills
-->
# HANDOFF — user → `behaviors`: the street fight on the REAL town geometry (`village-street`)

🔒 User, `2026-11-24`: the fight on the real-data town went *"like 200 years ago, with a group of soldiers standing and shooting at another group"*.
*"Maybe that changed already as we have newer fixes, but this is up to them to find out."*

## Why this, now
CE-4666…CE-4669 were reported on a real-data town and then worked on `wide-corridor` (`scenarios/tests/pc-corridor-fight`): a straight, open, 30 m
grass corridor between detached houses, built to isolate the problem. That scenario now passes every row (`5c0cb5edf`). The real street has never been
available in the repository; now it is: the same fight on the same streets, house spacing, gardens and trees as the original report. The question is
whether the fight there now looks like a modern infantry fight, and where it does not, why.

## What is new (on this branch, `street-fight-extract`)
| what | where | note |
|---|---|---|
| the terrain | `assets/terrain-input/village-street/` (input set, 2.9 MB) | an anonymized 800 × 500 m extract of the real-data town: arbitrary map frame, heights shifted, ids renumbered; the geometry is as in the source. Imports on first use (≈ 9 s incl. the navmesh): 276 enterable buildings, 9 373 doors / windows, 205 road corridors, ≈ 870 trees, no walls or fences |
| the scenario | `scenarios/tests/village-street-fight/scenario.json` | the original report's fight: `pc-defend-house`'s structure moved onto the town's straight main street |

**The ground** — the main street runs west → east along **y ≈ 255** (straight from x ≈ 300 to x ≈ 680), a two-carriageway road ≈ 18 m between
the kerbs, with side streets every 60 – 100 m, detached houses (one to five storeys, mostly two and three) 10 – 30 m back from the road, gardens, single trees and small
groves. Gentle slope (23 m of relief over the box).

**The forces** (positions in the scenario's frame):
| side | unit | behaviour | where |
|---|---|---|---|
| Red (Hostile) | Red Platoon: 3 squads + Red Reserve | `PlatoonDefend` of (550, 255), arc facing west, MaxRange 250, squad posts on the street, SearchRadius 25 | x 550 – 590 |
| Red | T-72, ZU-23 technical, radar SAM | placed, no orders | x 575 – 680 |
| Blue (Friend) | Blue Platoon: 3 squads, under Blue HQ (`SupportCommand`) | `PlatoonAttack` on (550, 255), SearchRadius 25 | from x ≈ 365 |
| Blue | M1, Bradley | `FollowRoute` up the street | x 330 – 350 |
| Blue | Apache | under Blue HQ | x 140, 60 m up |

Behaviours are as in the original run; nothing is scripted. (The original also had an A-10 orbiting at 700 m radius; it is left out, as the orbit does
not fit the box.)

**What the user saw on the source terrain** (the original report, before the CE-4666…CE-4669 work): the squads fight bunched in the open street, the
base of fire leaves the house line for the street centre, suppression goes on with no ammunition and at dead targets, dead men keep running behaviours.
A 90 s run of this scenario at `coordinator@c5f8c1971` (before this branch): both platoons engage, Blue squads `AdvanceTo` / `Suppress` up the street,
Red's line `AdvanceTo` toward its posts. Not judged further — that is your job.

## The ask (a frame — you design the details)
1. **Run it at your head** (editor over HTTP, `POST /scenario/load/live {"name":"village-street-fight"}`, then `POST /sim/play {}`), watch the
   first 3 – 5 minutes, and judge it by the realism rows you already use for `pc-corridor-fight` (`checks.json` there: Red's line in cover within its
   first minute; no Blue squad wholly in the open after 90 s; no squad bunched after 30 s; dead men run nothing). Add the rows you find missing.
2. **Report what still looks like "200 years ago"** and the cause of each, in your status — which of CE-4666…CE-4669 hold on real streets, which do not,
   and anything new that only real geometry shows (houses set back from the street, gardens, side streets, trees, the slope, the vehicles on the street).
3. **Fix in your lane's normal way** (rows, rails, red-proof). Give the scenario a `checks.json` like `pc-corridor-fight`'s when the rows are settled.

⛔ Fences: the terrain input set is source data — do not edit it to make the fight pass; if the ground is wrong for a fight, say so in your status (a
NOTE to terrain). Behaviour code follows CLAUDE.md's parallel-phase rules.

## Done when
- your status names, row by row, how the fight on `village-street` behaves at your head and why;
- what you fixed has its rail; what you did not fix is a tracker row with its cause;
- `village-street-fight` has its `checks.json` (green rows, or red rows each pointing at a tracker row).
