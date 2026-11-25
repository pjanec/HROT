<!--STATUS
state: LIVE · updated: 2026-11-25 · current-answer: §1 (the doors), §3 (a second stall, same cause)
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Building_Clearing.md — the room drill that stalls at these doors (§7.1a measured table: an earlier "wedged at the jamb" case on bt-range)
  - REPORT_Behaviors_Village_Street_Fight.md — the scenario run that found it
-->

# NOTE — behaviors (exp/agg-behaviors) → terrain: 283 of village-street's 628 ground-floor doors are not walkable through

**From:** behaviors, `exp/agg-behaviors` · **Tracker:** `CE-8324` (ours, for the follow-up) · **For:** the terrain lane (the bake / the generated buildings) · **Not a STOP** — FYI with a measurement.

## 1. The measurement

`village-street-mg-flank`: the assault squad stacked at the gun house's south door (`f666eab30807`, door at 527.82, 221.54, heading 170), the drill
released the point man to the threshold 1 m inside, and he stood 1.4 m short of it for 55 s — `NavigationStatus` Idle, `RouteHandle` 0, no replan,
no failure reported. The house was never entered.

A probe on the real terrain (`TerrainFolderWorld.ReadWorld` of `assets/terrain/village-street/terrain.json` → `new RecastNavmeshFactory().Build(world)`,
infantry layer, no door states ⇒ every door Closed, which infantry passes):

| point at the gun house's south door | walkable | path from 1 m outside |
|---|---|---|
| 1 m outside (527.75, 220.5) | ✅ z 176.0 | ✅ |
| the door centre (527.82, 221.54) | ✅ z 176.2 | ⛔ none |
| 1 m inside / the room / the gunner | ✅ z 176.2 | ⛔ none |

Every ground-floor door of `buildings.geojson` (1.2 m out → 1.2 m in along its heading): **passable 345 · blocked 283 · a side off the mesh 0.**
First blocked ones: `2d8671537056` (770.23, 199.5) · `464bd9890962` (53.54, 201.3), (69.73, 215.09) · `6c3ea2f5e61f` (107.14, 311.55) ·
`8986ea52bbc8` (199.71, 173.63), (203.81, 164.18) · `b6b3a0ce6037` (384.45, 128.1), (399.93, 115.35) · `cae334fce792` ×4.

⚠ **Cause not measured.** Both sides bake walkable; the doorway's polygon is the interior's (z 176.2), the outside ground 0.2 m lower — within the
0.4 m climb, so a plain step does not explain it. Suspects, unranked: the floor slab's edge in the doorway, the fine/coarse tile seam, the doorway
volume (`NavDoorways`, 0.4 m reach) vs a 0.3 m wall. ⛔ We did not edit the terrain input or the bake (the handoff's fence).

## 2. What behaviors does meanwhile

`CE-8324`: the room drill no longer waits for ever at a door it cannot pass — an entry man who makes no progress toward the threshold gives the room
up as **blocked** (the CB-53 path), and the squad tries the building's next way in. It does not make these doors passable.

## 3. A second stall, the same cause (`2026-11-25`)

`village-street-mg-flank`, Blue Squad 3 (the reserve) bounding: Rifleman 2 was ordered to a cover point **inside house `f9ec73982a36`**
(452.85, 157.89) and stood at (429, 176) from 130 s to 180 s — `NavigationStatus` Idle, `Result` InProgress, no route, no replan, **no failure
reported**, exactly as the gun-house point man.

The same probe (`TerrainFolderWorld.ReadWorld` → `RecastNavmeshFactory().Build`, infantry layer):

| | walkable | snapped z | path |
|---|---|---|---|
| from (428.94, 176.08) — outside | ✅ | 177.95 | — |
| to (452.85, 157.89) — inside `f9ec73982a36` | ✅ | 178.20 | ⛔ none from outside |
| the leg's midpoint (outside) | ✅ | — | ✅ from the start · ⛔ to the house |

⇒ one more house whose interior is walkable and unreachable — consistent with §1's 283 doors, nothing new in the mechanism.

## 4. Two asks, in order of value

| # | ask | why |
|---|---|---|
| ① | **the doorways** — why 283 of 628 bake with no path through (§1) | every house assault and every cover point inside a house depends on it |
| ② | ⭐ **a move with no route should FAIL, not idle** — the mover reports `InProgress` for ever when the planner finds no path | the failure was silent both times; behaviors had to add its own no-progress timers (CE-8324 room drill, CE-8321 bound clamp) to stop waiting. A reported failure would let every caller react at once. ⚠ We have not checked whether this is the navigation lane's rather than yours — if it is not yours, please pass it on |

⭐ **Behaviors side, already done, needs nothing from you:** the room drill gives a room up as blocked after 15 s without progress (`CE-8324`);
a bound never takes a man more than 50 m from his overwatch, so one stalled teammate no longer strands the other (`CE-8321`).
⚠ **Open on our side:** the cover search offered a point inside a house the man cannot reach — we will look at whether the cover ask should
skip unreachable points once ① is answered (if the doors are fixed, the question disappears).
