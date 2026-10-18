<!--STATUS
state: LIVE — behaviors → backend: a user RULING (R-2037) that changes two unstarted frames, plus one new frame item.
updated: 2026-10-18
current-answer: the whole note.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — §13b OWNS this: R-2037, the WorldSurvey sensor (DA-25), the inventory of brain-side geometry reads
  - docs/DESIGN_World_Query_Seam.md — (yours) IWorldQuery; R-2037 decides WHO may call it: the muscle, never the brain
  - docs/designs/eqs-2/EQS_Design_v1.3_final.md — (yours) the child-sensor road WorldSurvey reuses
-->

# NOTE — behaviors → backend: the brain never reads world geometry *(R-2037)*

🔒 **User, `2026-10-18`:** *"The line of sight must always be a sensor or raycast query which goes to the muscle and returns back as a
result. It is nothing that is calculatable immediately on the brain It always needs to go to muscle and back."* — because smoke,
destruction and doors change the world, and *"only the muscle knows the current state of the environment"*. *"The final decisions, of
course, can stay on brain."*

⭐ **This is a ruling, not a request to start work now.** It changes three things in frames behaviors already sent you. ⚠ Your
`git log` shows none of them started, so they are amended by THIS note rather than re-dispatched (rule 1c's form: the original files
are not edited).

| frame item | was | ⭐ now (R-2037) |
|---|---|---|
| **BR-1** the room graph ([Building Clearing frame](HANDOFF_Behaviors_To_Backend_Building_Clearing.md)) | *"a read-only query seam the brains can call (like the door queries)"* | built and kept **on the muscle**, updated when walls or doors change; the brain receives rooms, portals and corners only as a **BuildingSurvey** answer (BR-12) |
| **BR-10** `BuildingOf(point)` ([Defend / attack frame](HANDOFF_Behaviors_To_Backend_Defend_And_Attack.md)) | a read-only query | a **PointSurvey** answer (BR-12) |
| 🆕 **BR-12** the `WorldSurvey` sensor | — | ⭐ lean (DA-25): a child sensor in the EQS family (`EqsChildSensor` lifecycle, epoch, config / answer translators), solved on the muscle against the live world (`Trace` with the sight purpose: walls, openings, doors, clouds, damage), its answer stamped with the muscle's time, applied into a cognitive buffer on the owner's node, announced by `SensorChange.SurveyReady`. Shapes: **VisibilityFan** (≤ 12 origins × an arc × ≤ 24 rays → free distance + what stopped each ray) · **BuildingSurvey** (an area or building → storeys, faces, window / door openings, rooms) · **PointSurvey** (points → building, storey, room, indoors, ground and surface heights). Rails: a fan through an open window sees the street and, after a smoke grenade bursts in front of it, does not; after a wall is destroyed, the BuildingSurvey shows the opening; the answer reaches a brain on another node |

**Your code that the same measurement found reading geometry on the brain** *(a text sweep — the list is what the pattern matched)*:

| where | reads | why it matters under R-2037 |
|---|---|---|
| `DangerAreaSensorSystem.Rate` (brain half, `CE-3072`) | `TerrainWorld.SegmentBlocked` from a remembered contact to the area | sight on the brain |
| `AimAndFireExecutor` in CGF's Brain executor set (`CgfLogicPack.cs:164-167`) → `LineOfFire.MuzzleMasked`, `AimPoint.Middle` (`AimAndFireExecutor.cs:268-275`) | the muzzle masked by a wall, the visible part of the body | geometry and sight in the fire gate on the brain |
| `FireSolution.For` / `ClearsDensely` (`FireSolution.cs:82, 137`), called by behaviors' indirect fire | does the arc clear the terrain | geometry on the brain |
| `TerrainWorldQuery.SightBlocked` (`:69-76`) as served on CGF | sight with mirrored clouds and doors, no destruction | the mirror R-2037 forbids |

⭐ Behaviors' own sites are `CE-2286`. Please allocate your rows from your block and name them in your report, so `CE-2286`
(waits on BR-12), `CE-2283` (PointSurvey) and `CE-2257` (BuildingSurvey) can cite them.
