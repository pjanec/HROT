<!--STATUS
state: LIVE
updated: 2026-10-14
current-answer: the whole file. It is the report for HANDOFF_Behaviors_To_Backend_Weapon_Systems.md (WS-1–WS-4) as steered by STEER_Behaviors_To_Backend_Weapon_Systems_Crew_Presets.md (W-I) and STEER_Behaviors_To_Backend_Team_Carry.md (T-a).
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Weapon_Systems.md — the handoff this report answers
  - docs/blueprints/batches/STEER_Behaviors_To_Backend_Weapon_Systems_Crew_Presets.md — the steer (W-I) that changed WS-1 / WS-3 / WS-4 before any item was built
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — the as-built for all four items: §9
  - docs/blueprints/batches/STEER_Behaviors_To_Backend_Team_Carry.md — the second steer (T-a, built with WS-2's code)
  - docs/DESIGN_Entity_Interactions.md — §7.5.5 the T-a as-built
-->

# Report — backend → behaviors: weapon systems (WS-1–WS-4) and the team carry (T-a)

Scope frozen at the dispatch commit `f71ce3a0b`, merged into `backend` in `85abc3d4a`; the started marker is `d453c45e4`. ⭐ **Steered** by
[`STEER_Behaviors_To_Backend_Weapon_Systems_Crew_Presets.md`](STEER_Behaviors_To_Backend_Weapon_Systems_Crew_Presets.md) (W-I: presets seat
their crew) — merged in `7d5688de4` before any item was built; the steer wins where it differs (WS-1 `StartWith` on any seat, WS-3 one more
rail, WS-4 `SeatPresets` + a gunner in 104 / 105). Branch: **`backend`**. Each item's as-built is in
[`DESIGN_Weapon_Systems.md` §9](../../designs/weapons/DESIGN_Weapon_Systems.md).

## Where each item is

| item | what | sha on `backend` | design | state |
|---|---|---|---|---|
| WS-1 | `Gen.WeaponSystem`, the station seat fields (+ W-I `StartWith` on any seat), `CarrierStations`, class `WeaponSystem` (1.1.*.50), `WeaponSystemState` + `dtWeaponSystemState`, `WeaponDeploySystem` | `5c3fe8974` | §9 WS-1 | ✅ |
| WS-2 | a weapon boards a station that accepts it; a soldier carries a portable one (the carry station); a station weapon stands at its seat | `0123eae36` | §9 WS-2 | ✅ |
| WS-3 | one controller per weapon (`WeaponSystems`); the brain's mount list grows by the controlled weapons; a controlled weapon fires as itself; ⭐ W-I: the gun falls silent when its gunner dies | `f656cb38b` | §9 WS-3 | ✅ |
| WS-4 | nine weapon types, the bare Toyota (106), `SeatPresets` (W-I), 104 / 105 = pickup + weapon + gunner; seats survive a save / load | `e49721cc0` | §9 WS-4 | ✅ partly — see "not built" |
| ⭐ **T-a** (steer: [`STEER_Behaviors_To_Backend_Team_Carry.md`](STEER_Behaviors_To_Backend_Team_Carry.md), R-2017, CE-2175) | the team carry: `CarryMode.Stretcher`, the named second man (`PassengerBuffer.Assistant`, `IsAssistingTag`), the T4 gate, the rear-handle placement and the speeds (T3), both men's fire (T5), either man down ⇒ put down (T6) | TA_SHA | [`DESIGN_Entity_Interactions.md` §7.5.5](../../DESIGN_Entity_Interactions.md) | ✅ |

## Deviations (each argued in §9)

- **`EnumerateMounts` is not widened.** `CombatTkbTranslator` counts a type's own mount children with it; a boarded weapon must never join
  that count. The weapon brain reads a sibling, `WeaponMountQuery.EnumerateFiringMounts` (own + controlled).
- **`WeaponPortability.Static`**, not `None` / `Fixed`: the IDL generator scopes every enum member of a descriptor's file together, and
  `fixed` is an IDL keyword.
- **Seat presets ride the request tier**, not a CGF behaviour: `CreateEntityRequestSystem` spawns one entity per `StartWith` beside the
  host (the child-blueprint pattern) and marks the host `SeatPresetsFilled` (saved). `SeatPresetSystem` (in the entity-creation pack,
  scheduled by all five hosts) boards each into its seat. A load whose request carries the mark fills nothing; a host loaded without it (a
  scenario saved before presets) is filled.
- ⭐ **Added, because the fences required it:**
  - `HitModel.BodyOf`: a station / carried weapon's spread and sight line are its carrier's. Without it a technical's HMG would stop counting
    the pickup's motion and the fire it is under, and ground numbers would move.
  - A save now keeps each passenger's SEAT (`PassengerBuffer` "Seats", `IsEmbarkedTag` "Seat"). Before, a load re-chose seats by role,
    so a crew-only host (a pickup) refused its gunner and its M2.
  - Every weapon type has a brain (CE-523 — Health and the deploy state are Brain-group) and the Muscle group applies to a weapon system (it
    aims the turret). Without either, a split cluster never fired the station weapon.

## Not built — two user decisions (R-106: the item stopped, the batch did not)

| item | what blocks it | lean |
|---|---|---|
| **404's door stations** | the type already has **8 seats**, the wire's cap; two door stations + two door-gunner seats need 4 of the 8 passenger seats | ⭐ **lean: per side, the forward seat becomes a door station and the next a door-gunner seat — 4 passengers + 2 gunners + 2 stations.** Changes: 404 carries 4 riders, not 8 (the air-assault demo loads a squad). Alternative: keep 8 riders and give 404 no door guns (a gunship variant, a new type id, carries them) |
| **2006 / 2007 as a soldier carrying a Javelin / NLAW** | their launcher is mount 0 of the soldier TYPE today; moving it to a carried entity makes the rifle mount 0 and the launcher a controlled mount (32), under the behaviour lane's trees and the M5 rails | ⭐ **lean: leave 2006 / 2007 as they are**, and offer a new preset for "a rifleman carrying a Javelin" once the behaviour lane's `CarryWeapon` exists. What would change it: the behaviour lane confirming nothing of theirs names the AT soldier's mount 0 |

## Wire changes (R-169)

| change | crosses the wire? |
|---|---|
| `dtWeaponSystemState` = 132, `EntityWeaponSystemState` (key `EntityId`; `Deploy`, `Until`), Brain ownership group | ✅ new topic (WS-1) |
| `SeatDto` fields (`Accepts`, `OperatedBy`, `NeedsGunner`, arc, `StartWith`) | ❌ TKB only (`CarrierStations` is a per-node type fact) |
| `SeatPresetsFilled` (382, saved), `PresetSeatIntent` (383, creating node only) | ❌ |
| scenario JSON: `PassengerBuffer.Seats`, `IsEmbarkedTag.Seat` | ❌ (scenario file; older files still load) |
| ⭐ T-a: `EntityPassengers.Assistant` (long, network id) | ✅ **APPENDED** to `EntityPassengers` (dtPassengerBuffer) |
| ⭐ T-a: `EmbarkInteractionPayload.Assistant` (long, network id) | ✅ **APPENDED** to the Embark case of `EntityInteractionRequest` |
| T-a: `EmbarkEntityCommand.Assistant`, `PassengerBuffer.Assistant`, `CarryMode.Stretcher = 3` | ❌ FDP event / component (the mode rides the existing 2-bit `Modes` — no shape change) |
| T-a: `IsAssistingTag` (384) | ❌ derived on every node, never saved |

## Gates

Base: `backend@HEAD` before WS-4 (`f656cb38b`, WS-3). Built once per project (`dotnet build <tests.csproj> --no-restore`), every suite run `--no-build`
after that build (the test project, never the production one — the stale-binary trap).

| # | gate (verbatim) | result | delta vs base | `--no-build` |
|---|---|---|---|---|
| 1 | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build` | 3348 / 0 / 1 | +5 new rails (WS-3: 5 → WS-4: `WS_TrackTarget_OnABareHost_SlewsItsStationWeaponsTurret`, `WS_ABareHost_IsArmedWithItsStationWeapon_ForTheUtilityInputs_AndItsStrength`, `WS4_ACarrierAndItsRiders_DoNotHideEachOther_AStrangerStillDoes`, `WS2_*`, `WS3_*`) · 0 red | ✅ |
| 2 | `dotnet test Hrot/Engine/Hrot.Core.Tests/Hrot.Core.Tests.csproj --no-build` | 244 / 0 / 0 | +1 (`WS4_TheWeaponSystems_AndTheBarePickup`); M9 re-pinned through the presets | ✅ |
| 3 | `dotnet test Hrot/Engine/Hrot.Presentation.Tests/Hrot.Presentation.Tests.csproj --no-build` | 598 / 0 / 12 | 12 skips pre-existing; two goldens re-pinned (below) | ✅ |
| 4 | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build` | 1310 / 0 / 3 | +3 WS-4 rails (`CreateEntityRequestSystemTests.WS4_*`) + `PassengerBufferTranslatorTests.WS4_TheSeats_SurviveASaveAndLoad` + `EntityCreationPackRails.EveryRootThatBuildsThePack_SchedulesTheSeatPresets` | ✅ |
| 5 | ⭐ **integration (rule 8)** `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/… --no-build --filter "FullyQualifiedName~WeaponSystemsAcrossNodes"` | 1 / 0 / 0 | new: `WS_ATechnicalsStationHmg_FiresOnASplitCluster` — a placed 104 on SimHost + IG + ExCon + CGF: presets boarded on CGF, the pickup marked filled, `FireAtTarget` given from SimHost; the M2's turret task crosses to SimHost (Engage, OnTarget), **the shot leaves the M2 on SimHost** (`ShotLog` shooter = the M2's net id) and the brain spends the M2's own belt on CGF (300 → 299) | ✅ |
| 6 | `python3 scripts/rulings-check.py` · `tracker-counts.py --check` · `design-digest.py --check` · `mermaid-check.mjs DESIGN_Weapon_Systems.md` | 202/202 · open 106 / done 386 · OK · 7/7 | — | n/a |

### T-a gates (after `behaviors@ee891028d` was merged, `32264ce64`)

| # | gate (verbatim) | result | delta | `--no-build` |
|---|---|---|---|---|
| 1 | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/… --no-build` | 3352 / 0 / 1 | +4 (`PersonCarrierTests.TA_*`) | ✅ |
| 2 | `dotnet test Hrot/Engine/Hrot.Core.Tests/… --no-build` | 244 / 0 / 0 | — | ✅ |
| 3 | `dotnet test Hrot/Engine/Hrot.Presentation.Tests/… --no-build` | 598 / 0 / 12 | — | ✅ |
| 4 | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/… --no-build` | 1357 / 30 / 6 in the full run · ⚠ **the 30 are 31/31 green alone** (`--filter` on their six classes: the replay/recording four, `BlueprintStateTranslatorTests`, `ZoneEntityPersistenceTests`) — order-dependent, no file of theirs touched | +2 (`PassengerReplicationTests.TA_*`, the `InteractionTransportTests` stretcher leg) | ✅ |
| 5 | ⭐ integration `--filter "FullyQualifiedName~WeaponSystemsAcrossNodes\|FullyQualifiedName~CE1056_AConvoyIsAmbushed"` | 2 / 0 / 0 | the station HMG still fires across nodes; the convoy squad still dismounts (passenger list + interaction transport, both changed by T-a) | ✅ |
| 6 | red-proofs, each mechanism removed then restored | 6/6 red: the T4 gate · the rear-handle pass · either-man-down · the stretcher speed · `EntityPassengers.Assistant` egress · the codec's `idsOut` | — | ✅ |

- **Red-proofs** (each inverse edit made the new rail red, then reverted): `TrackTarget` without the platform routing ⇒ `WS_TrackTarget_…` red · the
  occluder without `RideGroups` ⇒ `WS4_ACarrierAndItsRiders_…` red · (WS-3's five, unchanged).
- **Golden movement** (diff shape): `EntityTypeCatalogTests` built-in snapshot +10 lines, 0 removed (106 and 600–608: `family Unknown` — the ui lane's
  WS-5 draws them; they are hidden from the picker meanwhile); DIS names golden +1 line (`1.1.*.50 Weapon System (HROT)`); `EffectTests.LR1` explosive
  set +1 (the LAW rocket — first mount to load the existing munition, no number moved). Nothing removed anywhere.
- **Reds seen on the way, not this batch's** — ⚠ flaky, passing in isolation, no code of theirs touched: SimHost replay/recording classes
  (`ReplayLoadClusterOpHandlerTests`, `EcsRecordReplayControllerTests`, `NodeBootstrapperReplayTests`, `FullBranchPipelineTests`) went red 16× in one
  full run and 17/17 green alone; `SquadCoordinationSystemTests.Execute_DoesNotAllocate_OnTheSteadyStatePath` likewise. ⚠ The SimHost total
  flip-flops between runs (1313 / 1325 / 1389 seen) — test discovery, not this batch.
- Tree clean after every suite run (no golden rewritten by a test). Quarantine counts unchanged (no new skip).

### ⭐ Found while gating — three defects the split-cluster rail exposed (each fixed, railed and folded into §9)

1. **`TrackTarget` ignored controlled weapons** (WS-3 routed `AimAndFire`, not the tracker): the technical's tree tracks before it fires, the tracker
   looked for a mount on the bare pickup and failed every frame.
2. **"Is this unit armed?" read the unit's own `WeaponState`** — the utility inputs and `ThreatDanger` (own strength, and the danger it poses to
   others): a bare pickup scored 0 on engage and read as an unarmed contact. One answer now: `WeaponSystems.WeaponOf`.
3. ⚠ **Older than WS-4 — a carrier and its open-cabin riders blinded each other** (sight, fragments, blast shadow): the gunner at the pickup's own
   position blocked the pickup's line of sight. Measured at one spot: 101 and 106 saw the target, the crewed 104 did not.

⚠ **Report to behaviors** (not built — your files): `PeekAndFireNodes.ReloadingOrEmpty`, `VehicleFightFromCoverNodes.Reloading` and
`HillAttackTankNodes` read the unit's own `WeaponState`; a bare host reads unarmed there (`WeaponOf` is the call). And an **Attack order on a technical
on open ground never fires**: it runs `VehicleFightFromCover`, which fires only from a firing point its cover search finds — the rail uses
`FireAtTarget` for that reason.


## Ids allocated

None. No tracker or ruling id was allocated (progress went to CE-2174 and CE-2175 as dated lines). Component ids 379–384 (`GlobalComponentIds`; 384 = `IsAssistingTag`),
descriptor ordinal 132, TKB types 106 and 600–608.
