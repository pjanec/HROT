<!--STATUS
state: LIVE — for backend (CE-4658 HeatSourceSystem, CE-4657 the calculator) · updated: 2026-11-18 · current-answer: the whole file
related-designs:
  - ../../designs/thermal/DESIGN_Thermal_Drivers.md — §4b the marks, §4c as built (CE-4664)
  - HANDOFF_Coordinator_To_Terrain_Moved_Rows.md — CE-4664 moved to terrain
-->

# NOTE — terrain → backend: the ground heat marks are built (CE-4664) — call this when a stand ends

| what | where | for you |
|---|---|---|
| ⭐⭐ **the writer entry point** | `Fdp.Toolkit.Terrain.GroundHeatMarkRules.LeaveStand(EntityRepository repo, in HeatMark mark)` | ⭐ `HeatSourceSystem` (CE-4658) calls it ONCE when a vehicle LEAVES a stand, on the node that moves it. It ignores a stand < 60 s or with `EngineOnSeconds` = 0 (returns false), so you may call it for every stand |
| the mark | `HeatMark(Kind, At, Heading, Length, Width, HotEnd, StandSince, LeftAt, EngineOnSeconds, PowerClass)` | `Kind = HeatMarkKind.VehicleStand`; `At` the hull's centre where it stood (terrain x, y), `Heading` radians, `Length` × `Width` the hull; `HotEnd` front / rear from the TKB; `LeftAt` = now |
| the power class | `Fdp.Toolkit.Terrain.ThermalPowerClass` (Car 1 · Truck 2 · Tank 3 · Aircraft 4) | ⚠ please use THIS enum for `ThermalSignatureDto.PowerClass` (one enum, ruling 9) — or tell me the one you chose and I switch |
| the hot end | `Fdp.Toolkit.Terrain.HeatMarkEnd` (NoHotEnd · Front · Rear) | the same for `ThermalSignatureDto.HotEnd` |
| the writer | `GroundHeatMarkSystem` in `DamageAssessmentModule` (SimHost's core logic pack — where the design puts the movers) | ⚠ if `HeatSourceSystem` runs on a node WITHOUT that module, the local event is never read — tell me and I register it there too |
| the warmth | `Fdp.Toolkit.Perception.Signatures.ThermalDriversOps.MarkWarmth(in HeatMark, double now, in WeatherConditions)` | ⭐ `ThermalDriversOps` is a `static partial class` — add your `Signature` / `BarrelHeat` in your OWN file of it (`ThermalDriversOps.cs`), no merge conflict |
| ids | component **493** `GroundHeatMarks`, descriptor **242** `dtGroundHeatMarks`, TKB type **7209**, event id **5110** | the toolkit test struct on 493 (`KeyframeTestPos`) moved to 502 |

⭐ The scenario **`th-vehicle-left-warm-spot`** is yours with CE-4658: it needs your `HeatSourceSystem` to end the stand. Its expect rows can read the record through
`GroundHeatMarks` (count, `PowerClass`, `HotEnd`); a rail of mine already proves a mark crosses to a late joiner (`EntityGroundDamageTranslatorTests.CE4664_*`).
