<!--STATUS
state: LIVE · updated: 2026-11-18 · current-answer: §1
related-designs:
  - HANDOFF_Coordinator_To_Backend_Bug_Queue.md — the queue (22 rows) and its order
  - PROTOCOL_Autonomous_Lanes.md — how: push per row, the loop, decisions skipped and written
-->
# STATUS — backend, the queue

Handoff: [`HANDOFF_Coordinator_To_Backend_Bug_Queue.md`](HANDOFF_Coordinator_To_Backend_Bug_Queue.md). Started at `2e274c5fd` (`origin/coordinator` merged).
Self-wake-up: ✅ the scheduled task `backend self-wake-up` (hourly, :58), created `2026-11-18 08:58 UTC`; its message is the §1a text verbatim (updated 12:04). ⛔ NEVER deleted (R-4292): on an empty queue it takes new HANDOFF/STEER/NOTE files addressed to backend and DECIDED answers in `DECISIONS_Backend.md` (a DECIDED answer is new work), else writes "idle — waiting for a handoff".

## 1. Now
NEXT: **CE-4661** (TD-S5, `Health.StateSince` appended to `EntityDamage`).

## 2. The queue (in order)
| row | state | sha | rail | scenario |
|---|---|---|---|---|
| CE-3458 P1 unreachable move never fails | ✅ DONE | (this commit) | `MoveToLocationRoadUseTests.CE3458_*` 2/2, red-proved (no OwnMoveFailed / no OwnRouteFailed ⇒ red); neighbours 79/79 | 46 movement scenarios: 40 PASS; 6 RED AT THE BASE TOO (same checks): pc-ambush-bypass, pc-attack-dry, pc-urban-ambush, uc-reinforce-dry, uc-request-chain-dry, uc-urban-battle — behaviors' scenarios, open rows CE-2284 / CE-2352 / CE-2277 / CE-4042 / CE-2310 / CE-9023 |
| CE-3452 P1 bike from a standstill reports FailedBlocked | ✅ DONE | (this commit) | `CarKinematicsSystemTests.CE3452_*` 3 + `DriveByBehaviorTests.CE3452_*`, red-proved; CarKinematics 171 / 0, kinematics + obstacle suites 197 / 0, DriveBy 5 / 0 | own: `ua-moto-parked-bomb`, `ua-moto-drive-by-grenade`, `ua-technical-scoot` PASS · WIDE RUN (41, with CE-3473): 35 PASS; 6 RED AT THE BASE TOO (same rows): bt-window-duel, ia-area-defend, ia-house-hunt, rf-car-squads, rf-house-obstacle, ts-aggression |
| CE-3473 the ramming car never breaks the barrels (DECIDED: lean A + the barrel's fill) | ✅ DONE | (this commit) | `CarKinematicsSystemTests.CE3473_*` 4 (red-proved: no ram row ⇒ 1 red; no nose trace ⇒ 3 red), `CE4594_WhatAHullBreaks_*` drum rows; Presentation obstacle snapshots 74 / 0 | `cp-ram-barrier` PASS (in the wide run) |
| CE-9023 P1 disabled vehicle runs its route forever (the locomotion half) | ✅ DONE (locomotion half) | (this commit) | `MoveToLocationRoadUseTests.CE9023_AnImmobilisedVehicle_StopsRunningItsRoute` — the REAL dispatcher: CanMove lost (the IED) ⇒ the channel Failure (CE-3091) ⇒ the FollowRoute node Failure every tick, never re-issued (CE-3458); red-proved (no OwnRouteFailed ⇒ red); the class 8/8 | `uc-urban-battle` stays red AT THE BASE TOO — the play half (back to CMove in `runFor` 200) is behaviors' (NOTE_Convoy_To_Behaviors_UrbanBattle_MovesOn) |
| CE-3457 P5 vehicles never yield to animals | ✅ DONE | (this commit) | `CarKinematicsSystemTests.CE3457_*` (red-proved: no cap ⇒ the car runs into the cow); kinematics + NavigationExecution + Animal suites 194 / 0 | own: NEW `ua-animal-horse-blocks-car` 5 / 5 + the animal set (`ua-animal-herd-road`, `farm-under-fire`, `-guard-dog`, `-village-fight`, `-flock-flush`) 5 / 5, all under the parallel checks |
| CE-4657 TH TD-S1 thermal drivers: components, ops, perception re-based ([handoff](HANDOFF_Coordinator_To_Backend_Thermal_Drivers.md)) | ✅ DONE (the sensor re-base + `ThermalHeatSystem` retirement MOVED to CE-4658 — a re-based sensor before its writer reads everything cold; design §4d) | (this commit) | `EntityThermalDriverTranslatorTests` 4 (ids; both drivers byte-for-byte to a late joiner; slot reuse) · `ThermalDriversOpsTests` 6 (idle/off/running/body/hottest/barrel/0 B); SimHost filter 83 / 0, Toolkits thermal 16 / 0; ModuleHost `RPos`/`RVel` 487/488 → 500/501 | no own scenario (TD-S1: no behaviour changes); the sensing set (`ld-sentry-surprise`, `-360`, `ua-night-sensing`, `ua-weather-regions`, `ua-weather-sensing`) 5 / 5 PASS under `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` |
| CE-4658 TH TD-S2 HeatSourceSystem (hysteresis, possessed ignition) | ✅ DONE — + every body's default thermal data (`ThermalDefaults`: NO shipped type had any), the sensor re-based, `Locomotions.SpeedOf` (one rule), the thermal observer (TKB **215**), `FireState.TryAdd` fixed (CE-4657 reused a not-yet-lit slot); ⏳ ignition key (ui) and engine orders (none exist) — design §4e | (this commit) | `ThermalHeatSystemTests.CE4658_*` 5 (body edges, crewed hysteresis, authored Off holds + Dead, the stand → ground mark, 0 B) red-proved per its summary; `EqsModuleTests.S7_*` on the new drivers; Toolkits thermal 38 / 0, SimHost 52 / 0 | own: NEW `th-idling-tank` PASS under the parallel checks; red-proved BOTH checks (parked authored Idle ⇒ a second contact; idling authored Off ⇒ no contact); three drafts put a tank off the 600 m map — caught by the red-proof · the sensing set (`ld-sentry-surprise`, `-360`, `ua-night-sensing`, `ua-weather-regions`, `ua-weather-sensing`) 5 / 5 under the parallel checks |
| CE-4659 TH TD-S3 burst barrel heat on WeaponAmmo | ✅ DONE — per mount `BurstRounds` · `BurstStartedAt` · `LastShotAt` · `HeatAtLastShot` + ⚠ `CoolPerSecond` (a reader has no WeaponState); `Heat0..3` retired (0 on the wire); ONE rule `Combat.BarrelHeat.At`; HUD (ui file, NOTE_Backend_To_Ui_Barrel_Heat_Computed) and sensor read the computed heat; `ThermalHeatSystem` now only a STATION weapon — design §4f | (this commit) | `WeaponAmmoMirrorSystemTests.CE4659_*`, `BodyPartReplicationTests.CE4659_*`, `ThermalHeatSystemTests.CE1358_AHotStationWeapon_*`, `PossessionStationTests.CE1358_*` (+ the line 4 s later); Toolkits 34 / 0, SimHost 75 / 0, Presentation 100 / 0 | the weapons set (`ua-m2-engage`, `ua-weapon-choice`, `ua-weapon-systems`, `ua-tank-ammo`, `rf-car-duel`) under the full parallel checks: `ua-weapon-choice`, `rf-car-duel` PASS; `ua-m2-engage`, `ua-weapon-systems`, `ua-tank-ammo` ABORT on the read check (`DeployWeapon` → `WeaponSystemState`) — IDENTICAL at base `d7d0ef41d` ⇒ pre-existing, folded into **CE-3470**; with the read check off (parallel sim + world guard) all three PASS |
| CE-4660 TH TD-S4 fire: ignition, BurnSystem, wreck reads FireState | ✅ DONE — `BurnSystem` (HealthApplication, Health owner): a type with `FireSeconds` lit ONCE at its death crossing (`DeathCrossings`, now shared with `WreckObstacleSystem`), one spot hull half-length + 1.5 m; a body inside loses 10 % of max per whole second; `ThermalDefaults.Of(template)` the ONE thermal-data rule (+ `VehicleClass`, + a moving hull ⇒ car); ui's W3 `WreckSpawnSystem` READS `FireState` (cross-lane, NOTE_Backend_To_Ui_Wreck_Reads_Fire); ⏳ incendiary ignition (CE-4635/4637), building / terrain-cell holders — design §4g | (this commit) | `BurnSystemTests.CE4660_*` (2), `WreckObstacleTests`, ui `EffectTests` + `Map3DFrameRail.W_DestroyedEntities` + `AirCombatLooksRail.ADeadJet` (xvfb, 59 / 0); Toolkits 23 / 0 | `th-burning-wreck` PASS 6 / 6 under the full parallel checks (T-72 dead t 32 s, wreck lit t 32 s, Bystander burns t 32 s, dies t 41 s, Onlooker untouched, M1 survives); 🔴 red-proof: Bystander moved 9 m out ⇒ RPROOF; `ua-tank-ammo` aborts on the read check (`CarrierStations`, CE-3470, pre-existing) — UATANK with the read check off |
| CE-4661 TH TD-S5 Health.StateSince | TODO | | | |
| CE-4664 TH TD-S8 ground heat marks | ➡ MOVED to terrain (STEER_Coordinator_To_Backend_Rows_Moved_And_Scenarios) | | | `th-vehicle-left-warm-spot` |
| CE-4663 TH TD-S7 DIS mapping table (doc) | TODO | | | |
| CE-4670 VA-S1 partial damage exposed + the kill kind (`VehicleLook` in ui's `Hrot.Look`; `Health.Destruction`) ([handoff](HANDOFF_Coordinator_To_Backend_Vehicle_Appearance.md)) | TODO — ⚠ needs ui's `Hrot.Look` (LK-S1); if absent, VA-S2 first | | | NEW scenario: an MBT track hit → part Destroyed + mobility kill; an ammunition hit → Catastrophic |
| CE-4670 VA-S2 hatches, doors, ramps (`VehicleOpenings` 290, `dtVehicleOpenings` 243) | TODO | | | NEW scenario: an IFV dismount opens / closes its ramp |
| CE-4670 VA-S3 lights (`VehicleLights` 291, `dtVehicleLights` 244) | TODO | | | NEW scenario: a convoy blacked out at night; a helicopter's anti-collision light |
| CE-3468 T2 Points / Best blind under the parallel phase | ➡ MOVED to ui (STEER_Coordinator_To_Backend_Rows_Moved_And_Scenarios) | | | |
| CE-3469 T2 six SimHost rails red under the parallel env | TODO | | | |
| CE-2440 T2 cc-battalion not thread-count independent | TODO | | | |
| CE-3445 T2 rare mode-3 stepped divergence | TODO | | | |
| CE-2438 T2 paused-preview + run-to does not repeat | ➡ MOVED to ui (STEER_Coordinator_To_Backend_Rows_Moved_And_Scenarios) | | | |
| CE-2448 T2 cp-vbied golden does not repeat | TODO | | | |
| CE-3449 T2 five goldens differ from t = 0 | TODO | | | |
| CE-3424 T2 scale-1: 1 of 12 movers moved | ➡ MOVED to terrain (STEER_Coordinator_To_Backend_Rows_Moved_And_Scenarios) | | | |
| CE-3425 T2 scale-1 frame cost | TODO | | | |
| CE-4395 T2 the brain's cost at 1 000 men | TODO | | | |
| CE-3460 T2 the root objective never aggregates | TODO | | | |
| CE-3471 T2 a pig walked through its pen wall | ➡ MOVED to terrain (STEER_Coordinator_To_Backend_Rows_Moved_And_Scenarios) | | | |
| CE-1038 T2 SimHost run counts differ | ➡ MOVED to ui (STEER_Coordinator_To_Backend_Rows_Moved_And_Scenarios) | | | |
| CE-1046 T2 AfterBranch_SimGroupsReEnabled under load | ➡ MOVED to ui (STEER_Coordinator_To_Backend_Rows_Moved_And_Scenarios) | | | |
| CE-1473 T2 ClusterRunner.Tests does not build | ➡ MOVED to ui (STEER_Coordinator_To_Backend_Rows_Moved_And_Scenarios) | | | |
| CE-3451 T2 six ClusterRunner.Tests rails drifted | ➡ MOVED to ui (STEER_Coordinator_To_Backend_Rows_Moved_And_Scenarios) | | | |
| CE-1475 T2 SumoNetPlacementTests on a Czech Windows | ➡ MOVED to ui (STEER_Coordinator_To_Backend_Rows_Moved_And_Scenarios) | | | |
| CE-167 T2 re-check, close if stale | TODO | | | |

## 3. Taken from new handoffs
- ✅ `STEER_Coordinator_To_Backend_Rows_Moved_And_Scenarios.md` (`2026-11-18`) — ten rows moved (terrain: CE-3424, CE-3471, CE-4664; ui: CE-3468, CE-2438, CE-1473, CE-3451, CE-1475, CE-1038, CE-1046); from now §6a: per row its own scenarios + the touched feature's set, the wide family once per several rows. The 41-scenario run going for CE-3452 + CE-3473 counts as this batch's wide run.
- ✅ `NOTE_Terrain_To_Backend_Ground_Heat_Marks.md` (origin/terrain merged `2026-11-18`) — CE-4664 built by terrain: `GroundHeatMarkRules.LeaveStand` is CE-4658's call when a stand ends; CE-4657 uses terrain's `ThermalPowerClass` / `HeatMarkEnd` for `ThermalSignatureDto` (one enum each, ruling 9) and adds `Signature` / `BarrelHeat` in its OWN file of the `ThermalDriversOps` partial class. Agreed.
- ✅ `NOTE_Terrain_To_Backend_Thermal_Field_Names.md` — FYI for CE-4657 (smoke `AreaEffectCloud.BlocksInfrared`, weather `WeatherConditions.Since`, `TerrainMaterial.ThermalClass`, `SurfaceThermalClass`); names accepted, nothing to answer.
- ✅ `HANDOFF_Coordinator_To_Backend_Vehicle_Appearance.md` (coordinator merged `2026-11-18`, `5a774253c`) — CE-4670 VA-S1…VA-S3 queued after the thermal rows, before T2 (its priority).
- ✅ `HANDOFF_Coordinator_To_Backend_Thermal_Drivers.md` (coordinator merged `2026-11-18`) — CE-4657…CE-4661, CE-4663, CE-4664 queued in §2 after the P1/P5 rows, before T2 (the handoff's priority).

## 4. Notes sent / received
- ⬅ `NOTE_Behaviors_To_Backend_NearMiss_And_LyingCap.md` ② (my `CE4649` lying-cap allocation rail red under the strict guards) — ✅ DONE: the process-wide AccessTrace growing inside the one window; three windows, cheapest 0 B; guarded 4/4, red-proved. ➡ answered in `NOTE_Backend_To_Behaviors_LyingCapRail.md`.

## 5. Gates (rule 8)
(per row, in §2)
