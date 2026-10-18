<!--STATUS
state: LIVE — behaviors → backend: a FRAME (you design the details as step 1, then build).
updated: 2026-10-18
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Building_Clearing.md — OWNS this: the doctrine, the diagrams (§3 RoomGraph box, §4, §5), CB-1 and CB-6. This frame only points at it.
  - docs/DESIGN_Building_Interiors.md — (yours) the building model, panels, doors, the navmesh through doorways; its planned room/portal graph (sound v2, W-7v2 blast) and slice B-8 "room entry".
  - docs/DESIGN_Civilians_And_Crowd_Control.md — stun today is read only by the civilian code; BR-2 makes it hold every class.
-->

# HANDOFF — behaviors → backend: building clearing *(BR-1, BR-2)*

**Dispatched at:** the commit that adds this file, on `behaviors`. ⭐ Your scope is FROZEN at that commit; documents that change after
it are FYI only — if one invalidates an item, stop that item and say so (R-106), the other goes on.

**What it is.** The user asked for a squad that clears a house: armed insurgents in the rooms are eliminated, civilians are left alone.
The drill is behaviors' ([DESIGN_Building_Clearing](../../designs/group-maneuvers/DESIGN_Building_Clearing.md), `CE-2257`…`CE-2261`,
built on US Battle Drill 6 "Enter and Clear a Room"). It needs two things from the engine. ⭐ Build against that design's §3 `RoomGraph`
box and §5 module graph; fold any deviation back into it (as-built rule). ⚠ The leans are still waiting for the user's review (§9); BR-1 and
BR-2 do not depend on the open questions.

**Fences.** ⛔ No rooms authored per building template (a second copy of the walls). ⛔ No grid flood-fill in behaviors. ⭐ One producer
for the three readers: room clearing, sound v2, blast W-7v2.

| # | item | design basis | ⭐ lean | acceptance |
|---|---|---|---|---|
| **BR-1** | **the room graph** + one CQB test house | Clearing §6 CB-1, §3; Interiors' planned room/portal graph, B-8 | derived at load from the wall panels, per storey: rooms as closed polygons between panels; doors and gaps as **portals**, each with its two rooms and its width; each room's **corners** and floor area; `RoomOf(point)`; stairs are a portal between storeys. ⭐ A read-only query seam the brains can call (like the door queries). Test house in `bt-range`: one storey, a hallway, 4 rooms (one under 12 m² for the 2-man entry), a closet, one exterior door per side | rails: the test house gives 6 rooms + the hallway, every door a portal between the right two rooms, `RoomOf` correct at each room's centre and a doorway's two sides; `house-2f` gives its rooms on both storeys and the stair portal |
| **BR-2** | **a stunned person of any class cannot fire or move** for the stun's seconds | Clearing §6 CB-6 | the firing gate holds with `HoldStunned`; the mover stops; it clears when the effect ends. Today only the civilian code reads `Effects.IsStunned` | rail: a stunned soldier with a target in sight does not fire and does not move until the stun ends, then fires |

**Ids.** You allocate the tracker rows from your block (rule 3); please name them in your report so behaviors can cite them from
`CE-2257` (it waits on BR-1) and `CE-2260` (it waits on BR-2).
