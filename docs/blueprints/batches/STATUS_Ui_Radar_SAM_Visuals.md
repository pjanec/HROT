<!--STATUS
state: LIVE
updated: 2026-11-15
current-answer: the whole file (ui lane's report for CE-4618 U-1; U-2 / U-3 not started)
related-designs:
  - ../../designs/air-attack/DESIGN_Radar_SAM.md — THE design (§5 U-1 row carries the as-built)
  - ../../DESIGN_Map_3D_Mode.md — §6al.1 holds the as-built model, classDiagram and the pending list
  - HANDOFF_Coordinator_To_Ui_Radar_SAM_Visuals.md — the dispatch (frozen at 9b6ed8318)
-->

# STATUS — ui: the radar SAM's visuals (CE-4618) — U-1 BUILT, U-2 / U-3 pending backend

Started at `1344f276e` (coordinator merged, marker `31000fe08`). Design check: the handoff's U-1 row, `DESIGN_Radar_SAM.md` §2 / §2a / §5, the bulldozer precedent §6al — the build matches them; the only deviation is the one the handoff itself allows (below).

## What was built
`VisualFamily.AirDefenceVehicle` + `ShapeKits.AirDefenceVehicle` (43 parts, 9.8 × 3.0 × 4.4 m): 8 × 8 carrier (tyres with spokes), cab, a launcher of four canisters on the turret-0 turntable hinged at the rear (turns with `TurretAzimuth`, elevates 0…85° with `GunElevation`), a hull radar mast with a flat search antenna = new `PartRole.RadarAntenna` + `MotionPose.Antenna`, spooled and spun in `MotionTracker` (0.6 rev/s, 3 s spool, frame clock x sim pace like a rotor), wheels rolling on a 0.6 m radius. ONE read of "is the radar on": `RadarActivity.IsOn` (new, `Hrot.Presentation/Map3D/RadarActivity.cs`) — false until backend's replicated `RadarEmission` exists (TODO CE-4618 in the file), `RadarActivity.Source` is the plug the rail uses.

## Choices
| choice | what / why |
|---|---|
| `FamilyOfClass` timing | family + kit + size + colour + picker glyph now; the `["AirDefenceVehicle"]` row is NOT added — `CE2138_EveryClassAFamilyOrAvatarNames_IsInTheTable` would fail on a class the table lacks, and the handoff forbids declaring backend's class. Instead `TheFamilyRow_WaitsForTheClass_AndTurnsRedTheMomentItLands` goes red when backend declares the class without the mapping ⇒ then a one-line commit. The frame rail pins the look (`EntityBodyLayer3D.PinLook`, internal) for a stand-in type |
| mast placement | on the HULL between cab and launcher (not on the turret): it does not swing with the launcher and never meets the elevated canisters; a turret-mounted mast would also be re-centred by `KitFromTkb` |
| canisters = boxes | `KitFromTkb` re-sizes any `Gun` cylinder along x to the TKB muzzle ("the barrel") and re-centres every `Turret` part on the TKB pivot — a rail pins boxes + on-axis turret parts. Backend's turret 0 should use pivot x ≈ −2.4 m, trunnion x ≈ −3.0 m, z 3.0 m (also in the design row) |
| 2-D | the symbol is class / SymbolCode keyed (backend); ui added the picker glyph (`IconName` `_wheeled`) only |
| display node | kit from the replicated DIS type, pose from `TurretPose`, flag from `RadarEmission` ⇒ same by construction; no real entity can exist before type 114 ⇒ no cluster rail yet (pending) |
| thumbnail / U-2 | skipped: the type (114) and munition 6047 do not exist on `ui` or `origin/backend` |

## Gates
| # | command (verbatim) | result | `--no-build` |
|---|---|---|---|
| 1 | `dotnet build Hrot/Engine/Hrot.Presentation.Tests/Hrot.Presentation.Tests.csproj --no-restore` | 0 errors | builds |
| 2 | `HROT_RAIL_SHOTS=<scratchpad> xvfb-run -a dotnet test …Hrot.Presentation.Tests.csproj --no-build --filter FullyQualifiedName~AirDefenceVehicleModelRail` | **9 passed, 0 failed, 0 skipped** (8 pure + 1 frame, ran, not skipped) | yes |
| 3 | `xvfb-run -a dotnet test … --no-build --filter "FullyQualifiedName~Map3DTests\|…~BulldozerModelRail\|…~BodyScaleFrameRail\|…~VehicleBodyParityTests\|…~PersonBodyParityTests\|…~Map3DFrameRail\|…~EntityTypeCatalog\|…~AirDefenceVehicleModelRail"` (every class exercising the changed seams: `MotionPose`, `MotionTracker`, `ShapeKits.For`, `VisualFamily`) | 333 total: **327 passed, 6 failed, 0 skipped** | yes |
| 4 | the 6 reds re-run at the base `31000fe08` (my changes stashed, test project rebuilt) | **the same 6 fail**: `CE2138_EveryBuiltInType_KeepsIts3dFamily_PickerFolder_AndIcon` (a new catalog line `113 HMMWV (drone jamm…`), `CE1309_TheMindStateBar_…`, `CE1086_TheFpvDrone_…` (0.35 vs 0.6 size), `CE4388_TheCachedWireSphere_…` (timing), `CE1227_V_DemoCity_…`, `E2b_V_ARandomTerrain_…` | pre-existing |
| 5 | `python3 scripts/tracker-counts.py --check` | OK, 3256 rows | — |

Golden movement: none (no golden file touched). New skip: none. Working tree after the suites: only my files (7 modified / 2 new, listed below).

## Red-proofs (each a compiling mutation: apply, build, test, revert, `touch`)
| mutation | rails red |
|---|---|
| `BodyTransform` ignores the antenna spin (`when motion.Antenna == 1234.5f`; the first attempt `when false` did not compile — unreachable-code error — and was redone) | `TheAntenna_SpinsAboutTheMast_…` + the frame rail (2 red) |
| `MotionTracker` never writes the antenna angle | `TheMotion_TheAntennaStandsWhileTheRadarIsOff_Spools…` + the frame rail (2 red) |
| `ShapeKits.For(AirDefenceVehicle)` ⇒ `Unknown` | `TheSamTruck_HasItsOwnFamily_…` + the frame rail (2 red) |
After each revert the full rail was 9/0/0 again.

## Renders (scratchpad only, never git)
Directory `/tmp/claude-0/-home-user-HROT/1bf325a1-da7f-51ac-b2db-4ed0c6f53d2b/scratchpad/radar-sam/` — `sam-1-side.png`, `sam-2-front34.png`, `sam-2b-rear34.png`, `sam-3-elevated45.png`, `sam-3b-elevated45-side.png`, `sam-3c-elevated85.png`, `sam-4-mast-off.png`, `sam-5-mast-on-1.png` / `-2.png` (two spin angles), `sam-6-thumb.png`. Read next to the model: a wheeled missile truck with a radar, not a tank or a bulldozer; the frame rail asserts the Off frames are identical whatever the clock and the On frames differ only in the top third of the truck.

## Ids / files
Ids allocated: none (CE-4618 updated in place). Files: `Hrot.Presentation/Map3D/{VisualFamily,ShapeKit,MotionTracker,EntityBodyLayer3D}.cs` (modified), `RadarActivity.cs` (new), `Hrot.Presentation.Tests/Map3D/AirDefenceVehicleModelRail.cs` (new), `docs/DESIGN_Map_3D_Mode.md` §6al.1, `docs/designs/air-attack/DESIGN_Radar_SAM.md` §5, `docs/blueprints/Blueprint_Issues_Tracker.md` (CE-4618 row).

## Waits for backend (CE-4617)
Class `AirDefenceVehicle` (DIS `1.1.*.28`) + type 114 ⇒ add the `FamilyOfClass` row (a rail turns red then), bind `RadarActivity.IsOn` to `RadarEmission.On`, 2-D symbol, thumbnail, display-node rail. Munition 6047 ⇒ U-2 (missile look, launch flash). U-3 optional, not started.
