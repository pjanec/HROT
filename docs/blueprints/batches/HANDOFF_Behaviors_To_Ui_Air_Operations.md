<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-14); scope frozen there.
updated: 2026-10-14
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/air-attack/DESIGN_Air_Operations.md — THE design (§5 the aircraft route, §6 flight models and the station, §11 slices)
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Air_Operations.md — the backend half (the route fields on the wire, the flight models)
-->

# HANDOFF — ui: air operations (the route editor with heights; later the pilot station)

**For:** the ui lane (`ui`). **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at the commit that adds this file**: your scope
is FROZEN there; documents that change after it are FYI. If a later document invalidates an item, **STOP that item and report it**; do
every item that is not blocked (R-106).

## 0. Why

🔒 User, `2026-10-14` — air operations *"Approved"* (R-2015): aircraft follow a waypoint route the scenario author draws, with a height per
waypoint and Ground waypoints where a helicopter lands. Thank you for the doors-off Black Hawk and its riders (CE-1081…1084) — the air-assault
scenarios use them. ⭐ **Build against [DESIGN_Air_Operations.md](../../designs/air-attack/DESIGN_Air_Operations.md)** §5 and §6.

## 1. Before anything

1. Merge `behaviors` at the dispatch commit or later. Push a `chore: started …` marker (rule 1b).
2. Ids from your block (`CE-1xxx`); progress on our rows (CE-2172, CE-2173) as appended dated lines.

## 2. The items

| # | item | design | acceptance |
|---|---|---|---|
| **U1** | **CE-2172 — the route gizmo is Z-up**: `RouteWaypointGizmo` reads and writes `Position.X / Y` (it treats `Z` as north today, `RouteWaypointGizmo.cs:19-21`), so a drawn route (`ScenarioSpawnAdapter.cs:414` writes `(x, y, 0)`) shows and edits where it was drawn | R-182 | a rail in the gizmo's suite: draw a route, drag a point, its `Z` is unchanged and `X / Y` follow the drag (red before) |
| **U2** | **per-waypoint height, kind and hold** in the route editor: `HeightAgl` (m above the ground; 0 = the stored Z), `Kind` (Pass / Ground), `Hold` (None / Seconds / Unload / Pickup) + `HoldSeconds` + `HoldUnit` (a unit pick). ⚠ The fields are backend's E2 — build U2 after `backend` lands E2 (merge it), or STOP and report if it has not | §5 | a rail: the edited fields reach the `RoutePlan` and survive save / load; a 3-D map frame showing a route's heights |
| **U3** ⏸ | **the pilot station** — `ControlScheme.FixedWing`; the camera follows the body's attitude (bank and pitch) for both flyers (today yaw only, `PossessionStation.cs:262-269`); the HUD per scheme (speed, height, heading; later the bomb cue). ⛔ **Start only after backend's E5** (the plane's Manual model) | §6 | `PossessionStation` rails; a frame under Xvfb with a banking plane |

## 3. Fences

- ⛔ No wire or ECS change of your own: the waypoint fields and the intent's route reference are backend's (E2).
- ⛔ The behaviour lane owns the air behaviours, the squad rows and the scenarios — a finding there is a REPORT.
- ⚠ `PossessionStation.EyeOf`: at our merge `e09d8f96c` we kept the rotorcraft eye AHEAD of the vehicle branch — `IsVehicle` is false for a
  rotorcraft, so your branch's form put a helicopter's camera at a person's head height. Please keep that order.

## 4. Report back — the gate report contract (CLAUDE.md rule 8)

One row per gate: verbatim command · pass / fail / skip · delta vs base · `--no-build` column · every red confirmed pre-existing against the
base sha · tree clean after each suite · `tracker-counts.py --check` · every id allocated · screenshots for U2 / U3. The design folded to the
as-built. Then the sha of each item on `ui`.
