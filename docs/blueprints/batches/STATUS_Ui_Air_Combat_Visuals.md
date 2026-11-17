<!--STATUS
state: LIVE — ui air combat looks (CE-4647 AC-15), unattended
updated: 2026-11-17
current-answer: the slice table
related-designs:
  - ../../designs/air-attack/DESIGN_Air_Combat.md — THE design (§4d the looks, §5a jets, §5b missiles, §7 ids)
  - HANDOFF_Coordinator_To_Ui_Air_Combat_Visuals.md — the dispatch (369177d6d)
  - NOTE_Air_To_Ui_Air_Combat_Contract.md — the air lane's contract (when it lands)
-->

# STATUS — ui: air combat looks (CE-4647 / AC-15)

Started `4fa79837f` (merged `coordinator@369177d6d+`). Engine half (`air-wip`) NOT yet merged — built against `*StandIn`s.

| step | state | evidence |
|---|---|---|
| 1 mockups | ✅ | `scripts/gen-air-combat-mockup.py` → `docs/designs/air-attack/air-combat-{body,stores,hud,arena}.svg`, linked from DESIGN_Air_Combat §4d (one-line cross-lane doc edit, said in the commit); PNGs in the scratchpad `aircombat/`. The MiG table in the script IS the kit table of step 2 |
| 2 MiG body | ✅ against the NAME | `VisualFamily.TwinEngineJet` + `ShapeKits.TwinEngineJet` (+ `KitPart.RollDegrees` for the canted fins) + `VisualFamilies.AirClassStandIn["TwinEngineFighter"]`; `AirCombatLooksRail` (kit + class + the real frame: side · top · rear, `aircombat-mig-*.png`) |
| 3 stores | ✅ | `JetStores` (pylons always; one missile per round left of the mount whose ammunition is an AAM; left rail first), cached part arrays (0 B); rail: 4 → 2 → 0 = a 6-part difference each way on the real frame |
| 4 AAM in flight · launch | ✅ mechanism · ⏳ look ROWS | the trail from the round's age vs `BurnOf` (IR 5 s, radar 8 s), then NONE; the launch flash + puff at the rail through `ApplyAirMissileEffects` + `BlastOf ⇒ Launch`. ⏳ tuning the four rows needs the contract's rows (they exist only on `air-wip`) |
| 5 afterburner · vortex · dead jet | ✅ | flames (`Throttle ≥ 0.9`, the contract's threshold), wingtip tubes (`GLoad ≥ 6`), the existing W3 wreck fire + smoke follows a falling jet (rail), its afterburner out |
| 6 arena | ✅ drawing + handles + validation · ⏳ the projector / edit / form | `AirArenaGizmo` over the seam `ArenaShape`; `AirArenaGizmoRail` 7 (2-D and 3-D real frames) |
| 7 fighter HUD | ✅ | `FighterHud` (pure `Build`, `Draw`) wired into `PossessionStation.DrawOverlay` for the FixedWing scheme; `FighterHudRail` 6 (the real frame measures the mockup's anchors) |

## ✅ RE-BOUND `2026-11-17` to the merged contract (`183af725e`)
Every stand-in is gone but the HUD's zone: `VisualFamilies.FamilyOfClass["TwinEngineFighter"]` · the real type `TkbEntityTypes.Jet_MiG29` (its `BodyGeometryDto`: gear hidden in the air) · `JetStores` classifies mounts by `MunitionTkbCatalog.TkbAim9x/R73/Aim120/R77` · `FighterState.Read` reads the real `FighterPilotState` (afterburner, vortex, HUD g) · `ProjectileLayer.BurnOf` reads `MissileDto.BurnSeconds` · `EffectTkbCatalog.AirMissileIds` = the catalogue constants · the four AAM and the two guns' look rows tuned in `MunitionTkbCatalog.BuiltInLooks` (cross-lane, said) · the arena: `AirArenaProjector` over the real `AirArena` (+ `NotAUnit`), `AirArenaOps` (one undo step each, no caches / statics), `AirArenaDetailsView` registered in `EditorSubsystem` next to the minefield's · the rails fixture-arm the real MiG with the §5a weapon suite (the air lane's row has none until S3) and write the real components · the `EntityTypeCatalog` golden line for 409 now says `family TwinEngineJet` (captured, not typed).

## Lane decisions (for the user's review)

| id | decision | would change it |
|---|---|---|
| R-1056 ✅ done | the MiG's class was bound by name in a stand-in until the contract merged; now `FamilyOfClass` | — |
| R-1057 | RESOLVED — the HUD missile cue asks the air lane's `EmploymentZone.Inside` (allocation-free) with the loaded round's `MissileDto`; rail `FighterHudRail.TheMissileCue_IsSearchWithNothingInTheCone_InZoneExactlyWhenTheAirLanesZoneSaysSo…` (both jets, IR + radar, 0 B) | — |
| R-1058 | the missile trail's AGE is the round's first-seen time on THIS host (`RoundPath.FirstSeen`); a round first seen mid-flight (a late joiner) shows its trail from then — accepted, no wire | a replicated launch time |
| R-1059 | the wingtip vortex is a straight stateless tube 40 m behind each tip (0 B), not the curved vapour of the flown path | a path-memory trail (like `RoundPath`) for aircraft |

| R-1061 | the arena's handles are armed by the existing **Edit Shape** tool on a selected arena (`ScenarioToolRegistrations` Edit: an `AirArena` ⇒ `AirArenaHandleGizmo`, else the polyline editor) — no new tool id; the handles show only while it is armed (the projector draws none), as a polygon's vertices do. A drag is RELATIVE to the press (centre by the pointer's travel, radius by the change in distance from the centre, floor / ceiling by that radial travel × 0.25), previewed locally, ONE undo step on release (`AirArenaOps.SetShape`); Esc drops it and ends the tool | a different tool, or handles always on |
| R-1060 | RESOLVED — picker row "Air Arena" (TKB 8840, point tool, `Map Graphics`, icon `_airarena`); `GameplayBirth.BornWith` gives a placed arena the default `AirArena` centred on the click; the body layer skips 8840 (map graphic). Rail `AirArenaGizmoRail.ThePickerListsTheArena…` | — |

Ids R-1056 … R-1061 (the ledger's ui line names R-1056 next free; a parallel ui session on another machine takes R-1060+).

## Gates (rule 8) — `7e890eb56` + this commit

| command | result | `--no-build` |
|---|---|---|
| `HROT_RAIL_SHOTS=<dir> xvfb-run -a dotnet test Hrot/Engine/Hrot.Presentation.Tests/Hrot.Presentation.Tests.csproj --no-build --filter "FullyQualifiedName~AirCombatLooksRail\|FullyQualifiedName~FighterHudRail\|FullyQualifiedName~AirArenaGizmoRail"` | **26 / 0 / 0** (none skipped: the frame rails ran under Xvfb), run twice | built first (`dotnet build <tests.csproj> --no-restore`) |
| the same + `PlayerHudFrameRail\|PlayerMessagesFrameRail\|ScoreHudFrameRail` (the HUD-text rails my station edit sits beside) | 29 / 0 / 0 | |
| `… --filter "Hrot.Presentation.Tests.Effects\|EntityTypeCatalog\|Hrot.Presentation.Tests.Map2D\|VehicleBodyParity\|Map3DTests\|ModelRail"` | 413 / 5 / 0 | |
| `… --filter "Map3DFrameRail"` | 51 / 2 / 0 | |
| reds, classified | `CE1309_TheMindStateBar…` and `CE1086_TheFpvDrone…` — pre-existing, CE-1955 (the minefields status). `S1_TheIconFallback…` (expected `null`, got `_tank` — the mine clearer's icon) and `CE4388_TheCachedWireSphere…` — ⭐ **red at the base `4fa79837f` in a clean worktree**. `E2b_V_ARandomTerrain_TheRoadsStandOnTheHills` (1.13 m road vertex off the ground) — terrain-geometry, no jet in it; ⚠ it SKIPPED in the base worktree, so NOT proven pre-existing. `MinefieldGizmoRail.TheCache…` and `TheBuildingDamagePlanTests…` failed only inside the 418-test run and PASS alone (state / memory pressure: the first wide run was OOM-killed) | |

**Red-proofs** (one compiling mutation per rail, each reverted; `scratchpad/redproof.sh`): the fin cant removed ⇒ the body rail RED · the ammunition mirror ignored ⇒ the stores rails (2) RED · `TrailFactor` never fading ⇒ the burn + trail rails (2) RED · `BurnerFrom` 9 ⇒ the afterburner rail RED · the floor ring not emitted ⇒ the arena drawing rail RED · the pipper not led ⇒ the pipper rail RED.

**Frames** (never committed): scratchpad `/tmp/claude-0/-home-user-HROT/1bf325a1-da7f-51ac-b2db-4ed0c6f53d2b/scratchpad/aircombat/frames/` — `aircombat-mig-{side,top,rear}.png`, `aircombat-f16-and-mig.png`, `aircombat-mig-stores-{4,2,0}.png`, `aircombat-aam-{burning,coasting}.png`, `aircombat-launch.png`, `aircombat-{afterburner,vortex,dead-jet-falling}.png`, `aircombat-hud-{missile,gun}.png`, `airarena-{2d,2d-selected,3d}.png`; the mockups' PNGs one folder up.

**Fidelity (R-4146), side by side:** the MiG matches the mockup's silhouette (two canted fins, two nozzles, the intakes under the wings, the blended body); deviations: the kit's gear hangs in the air as the F-16's does (the mockup omits it) · the missiles are visible from below the wing (the mockup draws them through it) · the 3-D arena has no floor DISC (a 2-D fill only; spokes in 3-D) · the HUD text is the HUD font's size (14 / 20 px), a little smaller than the mockup's.

Ids allocated: R-1056 … R-1059 (lane decisions above); no tracker row filed (CE-4647's row is the coordinator's).

## ✅ Follow-ups done `2026-11-17` (the coordinator's two items)
1. **Handle drag** — `AirArenaHandleGizmo` (R-1061): rail `AirArenaGizmoRail` — each handle's drag changes exactly its field by the dragged amount (centre +300 / −200 · radius +500 · floor +100 · ceiling +100), the component untouched while previewing, exactly `begin Edit air arena · end` on release; a press that moves nothing, a cancel and an unknown handle are no edit; 0 B in `UpdateAndDraw`.
2. **Form frame measure** — `AirArenaDetailsViewTests` (R-4138): seven rows in the mockup's order, ONE value column, one frame height and width, the groups apart, a steady frame under the ImGui floor (a per-frame `"##" + key` string it caught is fixed), a checkbox click = one step; the form matches the mockup's panel (deviation: Centre is two boxes, not one; Lapses off is a row the mockup now carries).
Red-proofs: the drag's commit removed ⇒ the drag rail RED · the lapses write removed ⇒ the form rail RED. Gate: Presentation filter (air rails, EntityTypeCatalog, CE2138, Minefield gizmo + frame, ToolControllerTests) 110 / 0 / 0; AiShared (`AirArenaDetailsViewTests|MinefieldDetailsView`) 5 / 0 / 0, under `xvfb-run -a`.

## ✅ Re-bound to the REAL suites `2026-11-17` (air S3 catalogue: mount 0 gun · 1 IR pair · 2 radar pair · dispenser last, BOTH jets)
Code needed no change: `JetStores` classifies mounts by the munition constants and reads the `WeaponAmmo` mirror in the real mount order (all four mounts sit inside `WeaponAmmo.MaxMounts`), `FighterHud` picks the gun by CATEGORY and the missile by its ammunition, `BlastOf` by ammunition — nothing assumes an index. Rails: `AirCombatLooksRail.ArmMig` deleted; rails find each mount by what it holds (`MountIndex`, `MirrorOf`); the F-16 now draws its four too (stores 4 → 2 → 0 for BOTH jets on the real frame, the launch `BlastOf` for both jets' IR and radar mounts, the gun none), `FighterHudRail.TheHud_WorksForBothFighters` (pipper + LockInZone for each), the not-a-fighter case is now the A-10; a dead jet drops its missiles and rails with its flame. Gate (`xvfb-run -a`, `--no-build`): air rails + EntityTypeCatalog + CE2138 + MinefieldGizmoRail **85 / 0 / 0**. Red-proof: `Carries` limited to the MiG ⇒ four rails RED (the F-16's stores, the HUD for the F-16, the afterburner / dead-jet rails that count its parts).

## Still waiting (after the re-bind)
S3: DONE — employment zone (R-1057), `TwinEngineJet` hit tables (`bodies.json` profile `1.2.*.1.200` → kit `TwinEngineJet`; `VehicleKitPart.RollDegrees` for the canted fins; regenerated `VehicleBody.Tables.g.cs`; `VehicleBodyParityTests` 2/0), arena picker (R-1060). The golden line `8840 Air Arena … folder Other` is the TKB-template snapshot (unchanged, real output); the picker folder is `Map Graphics` (CE1017 covers it).
