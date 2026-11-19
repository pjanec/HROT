<!--STATUS
state: LIVE — a finding handed to behaviors (CE-5040) · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - docs/DESIGN_Minefields.md — KG-7 / MR-19, the chain this blocks (terrain owns the knowledge, the solve and the sensor's area)
  - HANDOFF_Coordinator_To_Terrain_Minefield_Scenarios.md — the scenario that found it (mf-clear-lane-and-cross)
-->
# NOTE terrain → behaviors: a squad under an HQ never reacts `MinefieldAhead` (CE-5040)

📄 Row: [CE-5040](../Blueprint_Issues_Tracker.md) · found authoring `scenarios/demos/minefields/mf-clear-lane-and-cross` (red at S3).

## What happens

A Friend squad (`SquadMarch` + `BasicSquadSop`, **`CommanderGuid` = an HQ running `SupportCommand`**) marches into a minefield its side knows of
(learned from a strike). Its route sensor reports the field, but the squad never halts. It walks in and sets off a second mine.

## Measured (`2026-11-19`, terrain, a throwaway probe: two such patrols, Field D, test-town)

| step | evidence | verdict |
|---|---|---|
| the side's knowledge | `MinefieldKnowledge` record, Friend, Source Strike, suspected cells round the blast | ✅ terrain |
| the route solve (`DangerAlongRouteSolve.MinefieldAreas`) | `mf 1` on every solve from x 57 on (CE-2443 / KG-7, committed `31872471f`) | ✅ terrain |
| the sensor buffer (`DangerAreaSensorSystem`) | `wanted … Minefield`, `kept 1 k0 Minefield` — **the area is `[0]`** | ✅ |
| distance | `DistanceAlongRoute` ≤ 30 from x 93, still decreasing while the squad walks to x 127 | ✅ inside the row's 30 m |
| the SOP row `MinefieldAhead` (`BasicSquadSop`, `c4624000-…-0b40`; SOP ticked every 0.2 s by `BrainTickSystem`) | **proposes nothing**; the check `on: task, who: Patrol 2, is: MinefieldAhead` never holds | ⛔ |
| ⭐ the control: SAME probe, **only Patrol 2's `CommanderGuid` cleared** | `MinefieldAhead` at **62.75 s**; Patrol 2 never enters the field | ⭐ the commander is the difference |

⇒ ⭐ Something on the commanded-squad path suppresses the SOP row, or its reaction. Possible causes: the HQ's command state outranks an `Alert`
reaction, the SOP slot is gated for a commanded unit, or the reaction is filtered. ⛔ Not measured which one. That's yours.

Why the existing scenarios miss it: `mf-no-way-round`'s squad has **no commander**, and `mf-engineers-answer`'s squad **starts inside 30 m**.

## What it blocks

`mf-clear-lane-and-cross` (strike → mark → clear a lane → cross inside it) stops at its S3 row. Once the squad reacts, the rest of the chain
(HQ → `ReconMinefield` → `Breach` → `ClearLane` → cross) is the one `mf-engineers-answer` already passes. Then terrain tightens the lane rows
and closes CE-2443 / CE-4624.

⭐ **Acceptance:** `scripts/scenario-regression.sh mf-clear-lane-and-cross` gets past S3. A rail of yours: a commanded squad whose danger buffer
holds a `Minefield` at `[0]` within 30 m reacts `MinefieldAhead`.
