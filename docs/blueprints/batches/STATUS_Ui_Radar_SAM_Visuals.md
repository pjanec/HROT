<!--STATUS
state: LIVE
updated: 2026-11-15
current-answer: the whole file (ui lane's report for CE-4618: U-1 DONE, U-2 DONE, the rounds-left launcher DONE; U-3 optional, not started)
stale-below: nothing (the first U-1 report, written before backend's class existed, is replaced by this one; its kit numbers are SUPERSEDED here)
related-designs:
  - ../../designs/air-attack/DESIGN_Radar_SAM.md — THE design (§5 U-1 / U-2 rows carry the as-built; the 6047 → 6050 correction)
  - ../../DESIGN_Map_3D_Mode.md — §6al.1 holds the as-built model, classDiagram and the table of what was built
  - ../../DESIGN_Add_Entity_Picker.md — §2j the thumbnail atlas the truck's picture went into
  - HANDOFF_Coordinator_To_Ui_Radar_SAM_Visuals.md — the dispatch (frozen at 9b6ed8318)
  - STATUS_Backend_Radar_SAM.md — backend's as-built (type 114, class, TurretDto, missile 6050)
-->

# STATUS — ui: the radar SAM's visuals (CE-4618) — U-1 + U-2 + rounds-left DONE

Started on `d1b61e952` (backend merged). Design check: the handoff's U-1 / U-2 rows, `DESIGN_Radar_SAM.md` §2a / §5, §6al.1's classDiagram — the build matches; the deviations are the three cross-lane edits and the 6050 correction below, all folded into both designs.

## What changed
| item | as built |
|---|---|
| bind | `FamilyOfClass["AirDefenceVehicle"] = VisualFamily.AirDefenceVehicle`; the waiting rail became `TheClass_MapsToTheFamily_AndTheRealType114IsTheSamTruck`; the `PinLook` stand-in is deleted — the frame rails draw the REAL type 114 through the real catalogue |
| kit vs TKB (decision) | ⭐ **the KIT changed, not backend's `TurretDto`.** The kit is now in the TKB's own metres (box 9.1 × 2.8 × 4.2, was 9.8 × 3.0 × 4.4 scaled 0.93) with axis −1.5, trunnion −1.1, z 2.6 = backend's numbers; canisters 3.5 m long, hinged 0.7 m ahead of their rear end; mast moved to x +2.5 m. Rendered at 0 / 45 / 85° and 3/4: the launcher sits on the rear deck, clears the cab and the antenna's sweep at every elevation, the hinge is at the canisters' rear |
| rounds left | `LauncherLoad.RoundsLeft(world, e, mount)` (one reader beside `RadarActivity`, over the replicated `WeaponAmmo`; none ⇒ full; 0 B) + `KitPart.Round` (a part is drawn only while rounds > Round; one int test in `DrawParts`). Each canister: a front cover and a dark open mouth behind it. **Fire order: top-left, top-right, bottom-left, bottom-right** |
| U-2 missile | 6050 (⛔ not 6047): 3.0 × 0.17 m `Missile` shape (body, nose, wings, fins), light grey-green, smoke trail |
| U-2 launch | `Effect.Set` on 6050: new flash type **7173** (booster flash at the muzzle); `FiringBlast.Launch` → smoke type **7174** at the shot's origin; explosion / decal medium. Same `Effect.Set` + `BlastOf` mechanism as the Stinger / recoilless blast — no new system |
| 2-D | the TKB's SHGPUCDM + the class chain ⇒ ground-vehicle outline: pinned by a pure rail |
| thumbnail | `scripts/thumbnails.py --add-image … --name "Radar SAM vehicle"` ⇒ atlas page 1 slot 5/1 (78 pictures); atlas count rail 77 → 78 |
| display node | not built: kit from the replicated DIS type, pose from `TurretPose`, radar from `RadarEmission`, rounds from `WeaponAmmo` ⇒ identical by construction; no existing rail renders an entity on a separate display node for another family (nothing to add type 114 to) |
| U-3 | not started (optional) |

## ⛔ Cross-lane edits (said in the commits)
| file | change | why |
|---|---|---|
| `Hrot.Core/MapDefinitions/Tkb/NedTkbCatalog.cs` (`RadarSam`) | `MuzzleOffset (1.0, 0, 0.4)` → `(2.8, 0, 0.3)` | the round and its flash must leave the canister's MOUTH; with the hinge at −1.1 m the old offset was mid-launcher, and a 3.2 m missile cannot be 1 m ahead of the hinge and still elevate to 85° without the canister's rear going through the chassis. `NedTkbBuilderCombatTests` CE-4617 case does not pin the offset (green) |
| `Hrot.Core/Tkb/MunitionTkbCatalog.cs` | 6050's `MunitionVisualDto` (3.0 × 0.17 m, `#C4C8B8`) | U-2's tuning |
| `Hrot.Core/Tkb/EffectTkbCatalog.cs` | effect types 7173 / 7174 + the `Effect.Set` of 6050 | U-2's launch |
| `Hrot.Presentation.Tests/AddEntity/EntityTypeCatalogTests.cs` | goldens: + 114, + the DIS name `1.1.*.28`, atlas 78; ⚠ also the lines other lanes had left stale (113, 212, 213, 306, 407, 408, 8819) — that rail was already red at base | the picker goldens are the picture of the catalogue |

## Gates
| # | command | result | `--no-build` |
|---|---|---|---|
| 1 | `dotnet restore` once, then `dotnet build Hrot/Engine/Hrot.Presentation.Tests/Hrot.Presentation.Tests.csproj --no-restore` | 0 errors | builds |
| 2 | `HROT_RAIL_SHOTS=<scratchpad> xvfb-run -a dotnet test …Hrot.Presentation.Tests.csproj --no-build --filter FullyQualifiedName~AirDefenceVehicleModelRail` | **14 passed, 0 failed, 0 skipped** (the 2 frame rails ran, not skipped) | yes |
| 3 | same, `--filter "AirDefenceVehicleModelRail\|Map3DTests\|EntityTypeCatalog\|EffectTests\|ThumbnailAtlas"` after fixing the goldens | 328 total: **326 passed, 2 failed** (`CE1309_TheMindStateBar…`, `CE1086_TheFpvDrone…` — both red at the base `31000fe08`, other lanes) | yes |
| 4 | first wide run (+ `BulldozerModelRail`, `BodyScaleFrameRail`, `VehicleBodyParityTests`, `PersonBodyParityTests`, `Map3DFrameRail`, `AudioTests`) | 418 total: 405 passed, 3 skipped (audio), 10 failed = the 2 above + 3 `Map3DFrameRail` reds also red at base (`CE4388`, `CE1227`, `E2b`) + 5 caused by 114 / 7173 / 7174 / 6050 and fixed (`S1_TheIconFallback`, `CE2138_TheClassTable…`, `E1_EveryEffectType…`, `LR1_…` (backend's explosive list one short), the picker golden) | yes |
| 5 | `dotnet test …Hrot.Core.Tests.csproj --no-build --filter NedTkbBuilderCombatTests` (after restore + build) | **60 passed, 0 failed** (CE-4617 cases green) | yes |
| 6 | `python3 scripts/tracker-counts.py --check` | OK, 3259 rows | — |

Golden movement: the picker goldens (+ lines) and the atlas page 1 JPEG + index (+1 picture, the one allowed exception). New skip: none. The `Map3DFrameRail` reds were not re-run after the fix (unrelated).

## Red-proofs (each a compiling mutation: apply, build, test, revert, `touch`, rebuild — restored run 13/0/0 before the 2-D rail was added)
| mutation | rails red |
|---|---|
| `DrawParts`: the cover rule `part.Round >= 0` → `>= 1000` (covers never removed) | `TheLauncher_ShowsTheRoundsLeft_FourTwoNone_…` (1 red) |
| `EffectTkbCatalog`: the 6050 `Effect.Set` registered under type 0 | `TheLaunch_ShowsItsFlashSmokeAndMissile_OnTheFrameOfTheShot_…` (1 red) |
(The pure rails — fire order, `LauncherLoad`, class binding, kit fit — pin values against the TKB; the earlier U-1 mutations stand.)

## Renders (scratchpad only, never git)
Directory `/tmp/claude-0/-home-user-HROT/1bf325a1-da7f-51ac-b2db-4ed0c6f53d2b/scratchpad/radar-sam2/`: `sam-2-front34.png` (3/4), `sam-3-elevated45.png`, `sam-3c-elevated85.png`, `sam-launch-1.png` (the frame of the shot), `sam-load-4.png` / `-2.png` / `-0.png` (4 / 2 / 0 rounds), `sam-6-thumb.png`. Read next to the model: with 2 rounds the top row shows dark open mouths, with 0 all four; the launch frame shows the flash at the top-left canister's mouth, the smoke there and the missile on its boresight.

## Ids / files
Ids allocated: none new (CE-4618 closed; effect types 7173 / 7174 are TKB ids in the effects block). Files: `Hrot.Presentation/Map3D/{ShapeKit,VisualFamily,EntityBodyLayer3D,WeaponKits}.cs`, `Hrot.Presentation/Map3D/LauncherLoad.cs` (new), `Hrot.Presentation/Effects/EffectSystems.cs`, `Hrot.Presentation/Thumbnails/entities-1.jpg` + `entities.atlas.json`, the cross-lane files above, `AirDefenceVehicleModelRail.cs`, `EntityTypeCatalogTests.cs`; docs: `DESIGN_Map_3D_Mode.md` §6al.1, `DESIGN_Radar_SAM.md` §2a / §5, the tracker row.
