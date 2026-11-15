<!--STATUS
state: LIVE — CE-4617 B-1…B-6 built (backend), 2026-11-15
updated: 2026-11-15
current-answer: the whole file
related-designs:
  - HANDOFF_Coordinator_To_Backend_Radar_SAM.md — the frame (dispatched d287b3380)
  - NOTE_Coordinator_To_Backend_Radar_SAM_Ids.md — the fixed ids (type 114, DIS 1.1.*.28.1, class AirDefenceVehicle, missile 6047) — adopted, ⚠ EXCEPT the missile: 6047 is taken (⑤)
  - ../../designs/air-attack/DESIGN_Radar_SAM.md — the owning design; the as-built folds into it
-->

# STATUS — backend: the radar SAM (CE-4617)

Started at `ba3f54ee6` (merged `coordinator@de3be19be`, `behaviors@5da420f19`). Behaviors had marked it started and released it (`5da420f19`): nothing built there.

## ① `sam-site` — the three runs

`scenarios/demos/air/sam-site` on **city-battle** (1.5 km, relief 2–40 m; the SAM on the hill at (1404, 736), the design's "hill AA"): an Apache attacking
a truck from inside the radar's coverage, a bomber drone, an A-10 from **5 km beyond the terrain's west edge**, an FPV loitering near (tracked,
never fired at — it keeps the radar on past 30 s), Blue HQ (SupportCommand) the top commander. Run as `--verify sam-site` (headless editor)
with `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1`, three fresh processes:

| named check | run 1 | run 2 | run 3 |
|---|---|---|---|
| cued (its eyes), the radar comes on | ✅ 0.25 s | ✅ 0.25 s | ✅ 0.25 s |
| an aircraft warned SEARCHED | ✅ 0.25 s | ✅ | ✅ |
| warned TRACKED before the first launch | ✅ 0.5 s | ✅ | ✅ |
| a salvo (2+ missiles) | ✅ 37.4 s | ✅ 37.4 s | ✅ 37.4 s |
| radar OFF after the launch (relocating, or destroyed) | ✅ 51.75 s | ✅ | ✅ |
| the FPV never fired at (lock floor) | ✅ | ✅ | ✅ |
| the SPOT in Blue HQ's picture (AirDefence) | ✅ 30.25 s | ✅ 30.25 s | ✅ 30.25 s |

⭐ Observed in the live probe (HTTP, run 6): the SPOT landed **70 m** off the SAM at radius 175 m, and `SupportCommand.Sead` sent the HQ's free
armed drone onto it — the SAM was **destroyed at 51.75 s** (that is what switches its radar off in these runs; relocation is covered by the node
rails). Whether a missile hits is not checked (fixed seeds; the Apache was downed in the probe).
⭐ **Deterministic:** `python3 scripts/stepped-hash.py --threads 1,4,8 --scenario sam-site --chunk 100` (10 s, past the first launch, 1873
entities) ⇒ **STATE EQUAL** at 1 / 4 / 8 threads.
⭐ **The thermal §8.7 trap, measured:** no existing type gained a sensor child (the radar is listed on type 114 only; aircraft gained only a
`RadarSignature`). `--verify ua-air-defence` gives the SAME verdicts at the SAME times on the pre-radar base `ba3f54ee6` and on this branch
(Stinger 87.77 s ✅, ZU-23 40.67 s ✅, ⛔ "the Apache receives a missile warning" FAILS on BOTH — pre-existing, not CE-4617's).

## ③ Slices

| slice | state |
|---|---|
| **B-1** the radar sensor | ✅ built — `RadarEmission` (497, replicated as `dtRadarEmission` 188, brain-owned) · `RadarSignature` (498, stamped from `SignaturesDto.Radar`) · `RadarPerception` (aircraft-only generator, `RadarSignatureFilter`, the host's sight from the mast, no eye dwell) · the one-list carriage (`SimCombatDef.RadarRange`) · the RCS table · ⏳ the thermal §8.7 trap is measured on the carrier in B-6 (`sam-site` with / without the radar child — the carrier type is B-2) |
| **B-2** the missile, the launcher, the vehicle | ✅ built — type **114** `AirDefence_RadarSam` (DIS 1.1.222.28.1, class `AirDefenceVehicle` = `1.1.*.28`) · ONE mount: 4 missiles on turret 0 (0–85°), no reload, `RoundsInFlight` 2 / `SalvoIntervalS` 2 (new `WeaponMountDto` fields, honoured in `AimAndFireExecutor.TurretPath`) · missile **6050** (radar, 60°, 800 m/s, 18 s, 0.5 km min, chaff rejection 0.9, the library's new `"Radar SAM warhead"` proximity-fused) + a placeholder look row · chaff's END-GAME gate (`CountermeasureSystem.InChaffEndGame`) and the aircraft holding its chaff for it (`AirAttackNodes.ChaffTooEarly`, behaviors' file, said) · rails: two launches 2 s apart then the flight · a UH-60 at 4 km and an F-16 crossing at 250 m/s 3 km out hit (a 20° cone loses the jet) · an aircraft 2 km beyond the terrain hurt as much as over it · four chaff in the end-game defeat 20–40 % (measured 1 − 0.9⁴, seeded) |
| **B-3** the fire-control rule and the behaviour | ✅ built — `FireControlSight` (`Sees` per mount, `SeesForAnyMount` before the pick) at the executor's two sites and `AirSense.FireAt`; `SightNow` untouched, `SightNow.Holds` added · `RadarSamEngage` (`RadarSamNodes`, behaviors' assembly, said) from a NEW `BasicVehicleSop` row ahead of the air row · salvo by type 2 / 1 / 0 · Off while relocating, deactivator Off · `CanEngageAir` reads ammunition (`AirDefenceWeapons.HasRounds`) · the FPV refused by the missile's lock floor (`MissileDto.MinTargetRcsM2`, the one round gate `AmmoChoice.InDomain`) · rails: radar-only contact seen by the SAM's launcher, never by an M2 or an IR launcher; empty ⇒ neither row; the node's on → track → salvo of 2 → down → off → relocate, and the quiet path |
| **B-4** the radar warning and the escape | ✅ built — `RadarWarning` (499, unmanaged, NOT replicated: derived) written on the aircraft's brain node by `RadarWarningReceiverSystem` (the CGF pack's simulation list beside `MissileWarningSensingSystem`, gated on brain authority) from every emitter's replicated `RadarEmission` — searched (another side's radar on, in its range and line of sight) / tracked (its `TrackedTarget`) · the attack profiles' radar escape in `AirAttackNodes` (behaviors' file, said): tracked ⇒ weapons stop, off the bearing, down to 10 m AGL; clears 8 s after the track; the pass NOT counted (`ComeRound(countPass: false)`, the missile break's tail shared) · transit and drones do not react — **CE-4619** (the coordinator's row) · rails: searched → tracked → off; none out of range / own side; 0 B per tick; the Apache's escape, uncounted pass, then its rockets |
| **B-5** the SPOT and SEAD | ✅ built — `EmitterLocatingSystem` (`Fdp.Toolkit.Squad.Command`, the CGF pack before the staff, gated on the TOP commander's brain authority): a radar ON for 30 s without a break ⇒ a synthetic `SquadReportNotification` SPOT (AirDefence + Vehicle, `Radius` 200 m refining 2.5 m/s to 50 m, placed off the truth by half the radius in a direction seeded per emission) from the commander's first subordinate to EVERY other side's top commander (no superior), every 10 s while on · `SupportCommand.Sead` (behaviors' file, said): a fresh AirDefence element ⇒ the first free strike plane, else battery (fire for effect), else armed drone (DIS 50) — without a request; one SEAD order per 5 s (`LastSeadAt`) · rails: located at 30 s (not before), ≤ 100 m off, refined to 50 m, struck by the plane; a radar bursting 20 s on / 5 s off never located; the HQ's own radar never reported; SEAD with no request — plane, then battery |
| **B-6** `sam-site` | ✅ built and run (① above). ⭐ Running it under the machine checks found FOUR defects, all fixed: undeclared cross reads — `RadarSignature` (the brain phase, `BrainReads`), `WeaponSystems.Controlled`'s seat / station types (the squad phase, `SquadCoordinationSystem`), the terrain's live state through a bomb cue's `WorldQuery.Of` (the dispatcher phase, `WeaponDispatcherSystem`) — and a SAM destroyed while tracking that kept radiating (`RadarEmissions.Radiates`: on AND in the fight, read by every consumer; the node releases out of the fight) |

## ② Lane decisions *(each: what would change it)*

| # | decision | what would change it |
|---|---|---|
| L-1 | radar cross-sections (m², frontal, open-source orders of magnitude): UH-60 4 · AH-64 3 · F-16 3 · A-10 10 · C-130 40 · FPV 0.01 · recon drone 0.05 · bomber drone 0.3 ⇒ an 8 km radar sees a UH-60 at ~6.4 km, an FPV at ~1.4 km | a sourced figure for a type, or an exercise that needs a type seen sooner / later |
| L-2 | an aircraft with no `RadarSignature` reflects the 10 m² reference | a type that should be stealthy — give it a TKB value |
| L-3 | the radar acquires with NO acquisition dwell (CE-3260's dwell models an eye finding a target: 24.5 s at 5 km) — only the track hysteresis; `StrategySightTest(acquisitionDelay: false)` | a radar model with a lock-on time — then a radar-own delay, not the eye's |
| L-4 | the mast IS the carrier's sight height (`SimCombatDef.EyeHeight` → `SensorMount`, RV-17): its eyes, its radar and its muzzle share it — one sight rule | a launcher whose height matters for the missile's first metres |
| L-5 | the generator gathers to 1.5 × the stated range (a C-130 at 40 m² reaches 1.41 ×); the filter decides per target | a type above ~50 m² |
| L-7 | the missile (an SA-8 / Tor class, rounded): 800 m/s, 18 s, 0.5–8 km, a 60° seeker cone, chaff rejection 0.9, the shared 4 m proximity fuze; its warhead a new library row `"Radar SAM warhead"` (frag, 15 kg, 4.5 kg explosive) | a sourced figure; a target the 4 m fuze misses |
| L-8 | chaff's end-game = the round within 1.5 km of the cloud OR 3 s from it at its own speed; the aircraft holds chaff until the warning's time to go (`TimeToImpactS − elapsed`) is inside 3 s — dropped at the launch it would always be wasted | a measured defeat rate outside 20–40 % on `sam-site` |
| L-9 | the vehicle: a 6×6 truck (17.5 t, 16 m/s), splinter protection (20 / 10 / 10 / 5 mm, 400 health), the mast 6 m (its sight height), the launcher's lock 2 s, 60 °/s traverse; side 2 (Russia) by default — a scenario's faction overrides | a tracked chassis (one catalogue line), a sourced figure |
| L-10 | the salvo's NEXT one waits for the last round's estimated flight (the guided profile's rule), the two of one salvo 2 s apart | — |
| L-11 | an FPV is refused by a SEEKER LOCK FLOOR on the missile (`MinTargetRcsM2` 0.03 m²: FPV 0.01 refused, recon 0.05 taken) in the one round gate — not a type list | a drone class that must be refused / taken against its cross-section |
| L-12 | drone vs manned = SISO DIS air category 50 (unmanned) ⇒ 1 missile, else 2 | a type DIS-coded outside 50 |
| L-13 | relocation: 350 m straight away from the last aircraft (its heading when none) at 8 m/s, given up after 90 s; ⏸ no cover search yet | a site where straight away is into the enemy — then the EQS cover query |
| L-14 | the salvo's outcome = the target out of the fight, or 18 s (one flight) after the salvo left | a longer-flying round |
| L-15 | the cue is the SOP row's fresh air sense (any sense); the radar's own contacts exist only while it radiates, and it is off between engagements (relocation outlasts the 3 s freshness) — so its own radar does not re-cue it | a re-cue measured on `sam-site` |
| L-16 | the warning receiver hears a radar within the emitter's STATED range (no cross-section scaling — a receiver hears radiation farther than a radar sees a return), line of sight from the emitter's antenna (its sight height) | a receiver range model |
| L-17 | the escape is the same for every attack profile, rotor and fixed wing: half beam, half away from the radar, 10 m above the target's ground; a fresh leg while still tracked; no decoys for a track alone (chaff answers a missile, L-8) | a jet that must not go to 10 m — then a profile-own floor |
| L-18 | direction finding is the SIDE's, not a unit's: derived from the replicated emission on the node owning each top commander's brain (no sensor, no line of sight); 30 s continuous, error 200 → 50 m at 2.5 m/s, re-sent every 10 s | an ESM sensor model (then it becomes a sensor kind, RS-10's "any later passive receiver") |
| L-19 | SEAD: plane → battery → armed drone, regardless of a request; one order per 5 s; a still-fresh element is struck again once an asset is free (the radar keeps refreshing it while it radiates) | a doctrine of one strike per element |
| L-6 | the clutter floor reads the ground through the same `IWorldQuery` the sight reads (`TerrainWorldLosStrategy.LiveWorldQuery`), once per tick per thread — as the sight's own `BeginBatch` (one query object a tick, the existing pattern) | — |

## ④ The gate contract

| gate | command | result | vs base |
|---|---|---|---|
| Toolkits, full | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build` | 5072 passed · 8 failed · 1 skipped (at B-3) | ⛔ 7 S0 type rails + `S0WalkerTests.Resolve_AtLoad…` red on base `bd2d97999` too (clean worktree); `MissionDirectorSystemTests.…HealthAboveThreshold` green alone (order-dependent). The S0 "bare double" rail was red on base through B-1's `OnSince` — fixed (SimInstant), so it is green now; the other S0 counts unchanged (9 / 1 / 7 / 2) |
| Toolkits, touched (B-6) | `--filter CE4617\|Squad\|WeaponDispatcher\|Parallel\|ReadCheck` | 208 / 0 / 1 | — |
| Core, full | `dotnet test Hrot/Engine/Hrot.Core.Tests --no-build` | 371 / 1 | ⛔ `HrotEnvironmentTests.CreateGeoTransform_HasNoDefaultOrigin…` red on base too |
| SimHost air + staff + packs | `--filter AirDefence\|AirAttackNodes\|CE4617\|SquadManeuverTests\|Staff\|SquadReport\|CgfLogicPack\|BehaviourHosting\|ShippedTrees` | 297 + 47 + 93 (overlapping runs), 0 failed | — |
| Editor golden | `--filter TheOfferedBehavioursFitTheEntity` | 9 / 0 | — |
| Presentation | `--filter EntityTypeCatalogTests\|EffectTests\|Map3DTests` | 306 / 0 | — |
| Red-proofs | 15 mutants (`scratchpad/mutate.py`) | every one red | ⚠ the script now TOUCHES the restored file: restoring with the old mtime let an incremental build keep the mutant (measured, B-4) |
| `sam-site` ×3, parallel + read check + world guard | ① | 3 / 3 pass | — |
| determinism | `stepped-hash.py --threads 1,4,8 --scenario sam-site` | STATE EQUAL | — |
| tree / hosting snapshots | re-recorded | purely additive (+6 rows SOP, +1 behaviour, SupportCommand block 20 → 32 B) | — |
| `tracker-counts.py` / ids | — | no new tracker rows (CE-4619 is the coordinator's) | — |
| ⏸ not run | `run-system-tests.sh`, the full SimHost suite | — | T3, async |

## ⑤ Ids

`GlobalComponentIds` 497 `RadarEmission`, 498 `RadarSignature` · descriptor `dtRadarEmission` 188 — each measured free on backend, ui, behaviors, coordinator `2026-11-15`.
Type **114** `AirDefence_RadarSam`, DIS **1.1.222.28.1**, class **`AirDefenceVehicle`** (`1.1.*.28`) — as the ids note.
⚠ Missile **6050** `TkbRadarSamMissile`, NOT the note's 6047: 6047–6049 are the mortar SMOKE bombs (`Tkb60mmMortarSmoke` …, behaviors `CE-7025`) on every lane — **ui: key the missile look on 6050** (the as-built). Its DIS 2.1.222.1.0.0.0.
`GlobalComponentIds` **499** `RadarWarning` (free on every lane). Ruling ids: none (lane decisions are ② here, not canon).
⚠ For ui: `MunitionTkbCatalog.IsBuiltIn` covers 6001–6035 only, so 6036+ (6050 too) are not "a round" to a host with no TKB (the replay browser) — pre-existing, not changed here.
