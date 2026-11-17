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
| R-1057 | the HUD's employment zone is a plain range band (500 m … `AirArena.DefaultMaxShotRangeM`) with a 15° lock cone: the merged tree has no `InZone` / `EmploymentZone` (grep) — it arrives with S3 | S3 exposes the zone ⇒ ask it |
| R-1058 | the missile trail's AGE is the round's first-seen time on THIS host (`RoundPath.FirstSeen`); a round first seen mid-flight (a late joiner) shows its trail from then — accepted, no wire | a replicated launch time |
| R-1059 | the wingtip vortex is a straight stateless tube 40 m behind each tip (0 B), not the curved vapour of the flown path | a path-memory trail (like `RoundPath`) for aircraft |

| R-1060 | ⭐ the picker does NOT place an arena: no TKB type exists for it (the contract declares the component, not a template), so a scenario authors the `AirArena` entity (S1–S4 scenarios do) and the gizmo + form EDIT it. The handles' mouse drag (a pointer hook into the map's input chain) and a frame measure of the form's layout are the follow-up | the air lane (or ui) adds a TKB type for the arena ⇒ a picker row + the area tool's birth like the minefield's |

Ids R-1056 … R-1060 (the ledger's ui line names R-1056 next free; a parallel ui session on another machine takes R-1060+).

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

## Still waiting (after the re-bind)
S3: the air lane's employment zone (the HUD's band) and the real weapon suite on the MiG / F-16 (the rails arm a fixture suite today — then ⭐ the stores and the HUD appear with no ui change) · S3 hit tables: `VehicleBody` for the MiG (add `TwinEngineJet` to `VehicleBodyParityTests.Families` + `AircraftIds`, regenerate) · the arena's handle DRAG input and a TKB type for placing one (R-1060) · the form's frame measure.
