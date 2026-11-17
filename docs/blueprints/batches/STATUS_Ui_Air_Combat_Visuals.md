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

## Contract read (not merged into `ui` yet)
`air-wip@183af725e` ("air: the contract") carries `NOTE_Air_To_Ui_Air_Combat_Contract.md`. Read from git (the worktree untouched): the names and numbers my stand-ins use match it — `TwinEngineFighter` (`1.2.*.1.200`), type 409, munitions 6060–6065, `FighterPilotState.{Throttle, GLoad}` (component 278), `AirArena{Centre, RadiusM, FloorAglM, CeilingAglM, MaxShotRangeM}` (160), `MissileDto.BurnSeconds`, `WeaponCategory.AirToAir = 13`. **Re-bind checklist (mechanical):** `VisualFamilies.AirClassStandIn` → `FamilyOfClass` · `JetStores.IsIr/IsRadar` → the munition constants (or `WeaponCategory.AirToAir`) · `ProjectileLayer.BurnOf` → the round's `MissileDto.BurnSeconds` · `FighterStateStandIn.Read` → `FighterPilotState` · `AirArenaGizmo` → a `[GizmoProjector(SimTransform, AirArena)]` class + the edit command + the Details form · `FighterHud`'s zone → the air lane's · replace the placeholder look rows 6060–6065 · the stand-in templates in the rails (`MigStandIn`, `RegisterAamStandIn`) → the real type 409 / munitions.

## Lane decisions (for the user's review)

| id | decision | would change it |
|---|---|---|
| R-1056 | the MiG's class is bound BY NAME in `VisualFamilies.AirClassStandIn`, not in `FamilyOfClass`: CE-2138's rail demands every `FamilyOfClass` name exist in the class table, and the class is the air lane's | the contract is merged ⇒ move the line |
| R-1057 | no stand-in COMPONENTS (ui's id block is exhausted, and the ids are the air lane's): the afterburner / vortex / HUD g read ONE seam `FighterStateStandIn`, the arena is drawn from `ArenaShape`; ⭐ and the HUD's employment zone is a plain range band (500 m … 6 km) with a 15° lock cone until the air lane's zone exists | the contract is merged ⇒ read the components |
| R-1058 | the missile trail's AGE is the round's first-seen time on THIS host (`RoundPath.FirstSeen`); a round first seen mid-flight (a late joiner) shows its trail from then — accepted, no wire | a replicated launch time |
| R-1059 | the wingtip vortex is a straight stateless tube 40 m behind each tip (0 B), not the curved vapour of the flown path | a path-memory trail (like `RoundPath`) for aircraft |

Ids R-1056 … R-1059 (the ledger's ui line names R-1056 next free; a parallel ui session on another machine takes R-1060+).

## Gates
none yet.
