<!--STATUS
state: LIVE
updated: 2026-11-17
current-answer: the whole file — the numbers and names ui (and every lane) binds to
related-designs:
  - ../../designs/air-attack/DESIGN_Air_Combat.md — the design: §4a the classes, §7 the ids (this note gives the values it left to step 0)
  - HANDOFF_Coordinator_To_Air_Combat.md — the air lane's dispatch (S0 = this contract)
  - HANDOFF_Coordinator_To_Ui_Air_Combat_Visuals.md — ui's half: §1 keys on everything below
-->

# NOTE — air → ui (and everyone): the air-combat contract, in numbers

From the temporary `air` lane (branch `air-wip`, merged into `ui` by the coordinating session), the FIRST code commit of CE-4647 / CE-10000.
Every type below **compiles and is registered today**; the behaviour behind it arrives slice by slice (S1 … S4) — the names, ids and fields do not change.

## Ids — ⛔ permanent

| what | value | where |
|---|---|---|
| component `AirCombatSteer` | **277** | `GlobalComponentIds.AirCombatSteer` |
| component `FighterPilotState` | **278** | `GlobalComponentIds.FighterPilotState` |
| component `AirCombatState` | **279** | `GlobalComponentIds.AirCombatState` |
| component `MissileDefenceState` | **128** | `GlobalComponentIds.MissileDefenceState` |
| component `AirArena` | **160** | `GlobalComponentIds.AirArena` |
| DDS `dtAirCombatSteer` · `dtFighterPilotState` · `dtAirArena` | **216 · 217 · 218** (219 the air lane's spare, undeclared) | `EDescriptorType` (NED) |
| `NavigationMode.AirCombat` / wire `ENavigationMode.NAV_AIR_COMBAT` | **8** | both `MapMode` switches map it |
| TKB the MiG-29 class | **409** | `TkbEntityTypes.Jet_MiG29` |
| its DIS | **1.2.222.1.200** (a HROT subcategory, R-10002) | `NedTkbCatalog` |
| its entity class | **`TwinEngineFighter`** = `1.2.*.1.200` (under `Fighter` 1.2.*.1) | `EntityClassTable` |
| munitions AIM-9X · AIM-120 · R-73 · R-77 class · M61 20 mm HEI · GSh-30-1 30 mm HEI | **6060 · 6061 · 6062 · 6063 · 6064 · 6065** | `MunitionTkbCatalog.TkbAim9x …` |
| weapon category air-to-air | **`WeaponCategory.AirToAir = 13`** | `TurretSuiteDto.cs` |

⚠ **How the five component ids were found (R-10000):** a full scan of every `[ComponentId]`, every `*Ids` constant and every literal on coordinator,
behaviors, backend, ui, terrain, convoy and the three `exp/agg-*` branches. 128, 160, 277, 278, 279 were the ONLY ids nobody declares or uses —
**the 0–511 space is otherwise full** (the next lane that needs one needs the mask widened). 277–279 sat under a stale `NavFakeIds` note ("262–279")
whose own file declares only 262–269; the note is corrected.

## Types — `Fdp.Toolkit.Motion` (`FDP/Toolkits/Fdp.Toolkits/Motion/AirCombat/AirCombatComponents.cs`), all unmanaged and recorded

| type | fields | owner · wire |
|---|---|---|
| `AirCombatSteer` | `Manoeuvre Manoeuvre` · `PilotLapse Lapse` · `Entity Target` · `Entity Arena` · `Vector3 Point` · `float Throttle` (0…1) · `float FloorAgl` · `float MaxG` | Brain group · `dtAirCombatSteer` (ids as network ids) |
| `FighterPilotState` | `float GLoad` · `float Throttle` (0…1, 1 = full afterburner) · `byte GcasActive` · `ushort GcasCount` · `float MinAgl` · `byte Inbound` · `float ThreatBearingDeg` · `GuidanceKind ThreatSeeker` · `float ThreatClosureMps` · `float ThreatTtiS` · `SimInstant LastDitchAt` · `byte Defeated` · `float SpeedMps` | Muscle group · `dtFighterPilotState` (sent on a change of its rounded values: 0.1 g, 0.05 throttle) |
| `AirCombatState` | `Entity Target` · `Manoeuvre` · `PilotSkill Skill` · `PilotLapse Lapse` · `PilotLapse ForcedLapse` · `SimInstant LapseUntil, NextDecision, LastShotAt` · `byte Kills, Shots` · `float HighGSeconds` | brain, not replicated; `Skill` / `ForcedLapse` authored |
| `MissileDefenceState` | `MissileDefencePhase Phase` · `GuidanceKind Seeker` · `byte Decoys, Resume` · `SimInstant StartedAt, WarnedAt, LastDecoyAt` · `ushort Count` | brain, not replicated |
| `AirArena` | `Vector3 Centre` · `float RadiusM, FloorAglM, CeilingAglM, MaxShotRangeM, StalemateSeconds` · `byte LapsesOff` · `SimInstant LastHitAt` | Brain group · `dtAirArena`; saved with the scenario |

| enum | values |
|---|---|
| `Manoeuvre` | None 0 · Merge 1 · LeadPursuit 2 · PurePursuit 3 · LagPursuit 4 · Break 5 · Extend 6 · Climb 7 · Beam 8 · Drag 9 · LastDitch 10 · ReturnToArena 11 · Disengage 12 · Rejoin 13 |
| `PilotSkill` | Rookie 0 · Veteran 1 · Ace 2 |
| `PilotLapse` | None 0 · Fixation 1 · LateWarning 2 · EnergyBleed 3 · GLoc 4 |
| `MissileDefencePhase` | None 0 · Drag 1 · Beam 2 · LastDitch 3 |

## For the looks (AC-15 — the looks are ui's)

| ui reads | from |
|---|---|
| the afterburner | `FighterPilotState.Throttle` (≥ 0.9 = afterburner) |
| the wingtip vortex trails | `FighterPilotState.GLoad` (> 6 g) |
| the missile's burn | the round's AGE vs `MissileDto.BurnSeconds` (IR 5 s, radar 8 s) — no wire |
| the MiG's body | the class `TwinEngineFighter` (DIS 1.2.*.1.200); today it falls back to `Fighter`'s F-16 shape |
| the four AAM looks, the two guns' tracers | `MunitionTkbCatalog.BuiltInLooks[6060…6065]` — ⚠ PLACEHOLDER rows (plain `Missile` with a smoke trail; the guns trace); ui replaces them |

`MissileDto` gained (all 0 = the old constant-speed round): `BurnSeconds` · `MaxG` · `DragPerS` · `MinSpeedMps` · `SteerLagS`.

## What changes when

S1 the autopilot (`Steer3D`, GCAS, the F-16's flight numbers) · S2 the missiles' motor and `MissileDefence` · S3 the fighters (the MiG's flight
and weapons, the behaviour, the wire) · S4 the arena and lapses. Progress: `STATUS_Air_Combat.md`.
