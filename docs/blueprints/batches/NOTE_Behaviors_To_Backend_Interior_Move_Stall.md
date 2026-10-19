<!--STATUS
state: LIVE — behaviors → backend: a measured locomotion stall inside a house, for you to own.
updated: 2026-10-19
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — §8c D-2d / D-2e: the posts the men walk to, and the behaviour half already fixed
  - docs/DESIGN_Building_Interiors.md — OWNS the house the men move inside (house-3f, its stair and doors)
  - docs/designs/group-maneuvers/DESIGN_Platoon_Attack_Defend_Clear.md — §12.2 stage B: sq-defend-house is its demo
-->

# NOTE — behaviors → backend: seven men inside one house stop moving, and the move never says so

**Not orders.** On `behaviors`. Row: [`CE-2304`](../Blueprint_Issues_Tracker.md) (posted defenders stall inside OBJ; their moves stay `InProgress` for good).

| | |
|---|---|
| ⭐ **what we see** | `sq-defend-house` (cqb-village, OBJ = `house-3f`): the squad's seven men spawn inside OBJ and are each sent to a post 2–7 m away (some upstairs, by the stair). Within 20 s **four of seven never arrive**: each sits 4–6 m from his post, his SimHost `NavigationStatus` reads **`InProgress / Following`** for 20 s and more, and the brain's channel reads Running, so nothing re-issues |
| 📐 **measured** *(probe over HTTP, SimHost perspective, 2026-10-19, behaviors@`73812700a`)* | Rifleman 2 (1003): a **14.75 m** path to his upstairs post `(117, 153.9, 3)`, `ProgressS` 1.3 m at 6.8 s, 2.04 m at 10.3 s — then flat. Rifleman 1 (1002): his ground-floor post is **4 m** away; he ends at **(119.8, 152.0)**, i.e. **1 m outside OBJ's south wall** (the footprint is y 153–161 in world terms: origin (125, 161), rotation 180). Walk speed in the ground floor ≈ **0.4 m/s** on a 2 m/s order |
| ❓ **what we cannot tell from the brain** | whether this is agent-to-agent avoidance in two small rooms (seven men, a 1 m hall door) pushing men off the mesh and through a wall, or path-following itself. ⚠ The hall door is open (no `initial` ⇒ Open, `TerrainBuildingExpander.ParseDoorState`) — not the cause |
| ⭐ **the two asks** | **①** a man inside a house must not be pushed through its walls (1002). **②** a move that makes **no progress** for some seconds should end — `Failure`/blocked — so the brain re-plans (today `InProgress / Following` is held indefinitely: the brain has no way to know) |
| ⭐ **repro** | `python3 scripts/utility-demo-check.py sq-defend-house --launch` → the check "PROGRESS Set (every man at his post)" fails, every other check passes. For the numbers: `POST /perspective {name:"SimHost"}`, then `GET /entities/{id}` on the Red riflemen → `NavigationStatus`, `NavState`, `SimTransform` every few seconds |
| ✅ **what behaviors already fixed on its side** | D-2d: a posted man's attack moves stay within 4 m of his post · D-2e: he walks to his post first, whatever the fight decides (before, the first contact stopped him on the stairs). With both, three men reach their posts; the four left are this note |
