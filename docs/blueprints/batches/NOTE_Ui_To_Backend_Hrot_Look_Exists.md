<!--STATUS
state: LIVE — FYI for backend (HANDOFF_Coordinator_To_Ui_Look_Library.md asked ui to leave it when Hrot.Look exists)
updated: 2026-11-18
current-answer: the whole file
related-designs:
  - ../../designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md — §7 the look library, §7b LK-S1 as built
  - HANDOFF_Coordinator_To_Backend_Vehicle_Appearance.md — your VA-S1, which lands here
-->

# NOTE — ui → backend: `Hrot.Look` exists (CE-4671 LK-S1) — `VehicleLook` (VA-S1) goes here

FYI, nothing to build for ui's sake. The assembly your VA-S1 targets is on `ui` (merge it, or wait for it to reach the lane you build on):

| | |
|---|---|
| project | `Hrot/Engine/Hrot.Look/Hrot.Look.csproj` — in `HROT.sln`; refs `Fdp.Toolkits` + `Hrot.Core` ONLY |
| ⛔ the fence | no Raylib, Stride, ImGui, `Hrot.UI`, `Hrot.Presentation` — the rail `Hrot.Look.Tests.EffectRulesTests.LK1_TheLookLibrary_PullsInNoPresentationAssembly` walks every assembly it pulls in |
| what is in it | `Hrot.Look.Effects.EffectRules` (`ForShot` / `ForDetonation` / `ForImpact` / `ForContactBreach` / `ForFrame`, appending `EffectSpec`s to a caller's list — 0 B) and the type classifier the firing-blast rule needs, moved down WITH ITS NAMESPACE (`Hrot.UI.Common.Map3D.VisualFamily` / `VisualFamilies`, `WeaponKind`, `FiringBlast`, `FiringBlasts`, `AirMissiles`) |
| for `VehicleLook` | ⭐ `VisualFamilies.Of(template)` is here — a vehicle's family without the presentation; put `VehicleLook` under `Hrot.Look` (e.g. `Hrot/Engine/Hrot.Look/Vehicles/`) and its rails in `Hrot.Look.Tests` (no presentation reference there either) |
| the map's side | `Hrot.Presentation` references `Hrot.Look`; the map converts a spec / a look into what it draws (`EffectSpawnSystem.SpawnAll`) |

⭐ **Update `2026-11-18` — LK-S2 is in too:** `Hrot.Look.People.PersonLook` and the outfit rules; colours are `Hrot.Look.Rgba` (no renderer type — put
`VehicleLook`'s colours in the same struct). ⚠ `KitFromTkb` (a vehicle's turret geometry) was NOT moved: its kit types (`ShapeKit.cs`'s head) import
`Hrot.MuscleCharacter.Animation` and call `BlockFigure` / `ShapeKits` — it is `VehicleLook`'s material, so the split is yours with VA-S1 (design §7c).
