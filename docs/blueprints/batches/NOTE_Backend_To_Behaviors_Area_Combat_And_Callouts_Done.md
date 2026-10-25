<!--STATUS
state: LIVE
updated: 2026-10-25
current-answer: the whole note
stale-below: nothing
related-designs:
  - HANDOFF_Behaviors_To_Backend_Area_Combat_And_Callouts.md — the handoff this answers (dispatched at 9c481363c)
  - ../Architect_Question_104_Area_And_Building_Combat.md — §7.2 / §7.3 the as-built
  - ../../DESIGN_Thermal_And_Acoustic_Sensing.md — §8.8 the sound items
  - ../../designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md — §13 LN-9 the lean's depth
-->

# NOTE — backend → behaviors: your AQ104 / AQ105 handoff, all five items built

**Re:** [HANDOFF_Behaviors_To_Backend_Area_Combat_And_Callouts](HANDOFF_Behaviors_To_Backend_Area_Combat_And_Callouts.md). Started at `9c481363c` (merged `behaviors@018df41e5`).

| # | id | as built — what YOU call | design | rails (each red-proved) |
|---|---|---|---|---|
| 1 | `CE-3319` | a person is not `Arrived` on another level than his destination (`Levels.Same`, the world query's stacked surfaces); the planner's half-metre two-point move only on one level, so a re-sent move from under the point plans the stairs. ⭐ **Your move node must RE-SEND** when it sees "not arrived, not moving" — the mover stands still | [AQ104 §7.2](../Architect_Question_104_Area_And_Building_Combat.md) | `CarKinematicsSystemTests.AQ104I_*`, `TerrainHeightTests.AQ104I_*` |
| 2 | `CE-3320` | a wall or slab HALVES a high sound's carry (`min(dB, 6.02) × band`); Low ×0.2, Mid ×0.6. Nothing to call | [Sensing §8.8](../../DESIGN_Thermal_And_Acoustic_Sensing.md) | `AcousticHearingTests` |
| 3 | `CE-3320` | a door's state change is heard at its doorway: lock 5 m, open / shut 15 m, breach 40 m — as a man's sound (Movement, Footsteps), of the side of the man standing at it | Sensing §8.8 | `SoundEmissionSystemTests.AQ104E_*` |
| 4 | `CE-3320` | a SHOUT: while `Utterance` is any kind but `Talk` (`Utterances.IsShouted`), an enemy within 40 m hears him — ⭐ your squad calls (`CE-2372`) are shouted the moment you append them; tell me any that must be quiet | Sensing §8.8 | `AcousticHearingTests.AQ105D_*` |
| 5 | `CE-3321` | `Leans.SetDepth(repo, unit, metres)` (0.01 … 0.6 m; unset = today's 0.4 m), keeps his side; `Leans.ShiftOf(view, unit)` reads it. The eye, the seen points, the hit boxes and the aim follow it | [hit location §13 LN-9](../../designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md) | `LeanTests.AQ104G_*`, `BodyPartReplicationTests.CE3257_Lean_*` |

| ⚠ for you | |
|---|---|
| ⭐ **the brain reads no terrain** (user, `2026-10-25`: *"brain should use map only for rendering, no direct access"*; *"map = terrain"*) | AQ104 I's belt in `PostureNodes` (compare `LevelAt` of self and objective) must get the levels from a SURVEY answer, never `BuildingLayout` / `IWorldQuery` itself — [Node Roles §3.0a](../../DESIGN_Node_Roles_And_Policies.md) |
| ⭐ hearing is Perception-only (user, same day) | a shout, a door, steps reach your brain only as the anonymous `SoundContact` — never assume the sensor code runs on the brain |
| ⚠ a voice and a door are heard as **Footsteps** | the answer's kind and class bits are full (as jets are `Rotor`); "a man there" either way |
| ⏳ not built | sound round corners / through the open door (WQ-E v2); the TKB's per-signature band; the hearing gizmo |
