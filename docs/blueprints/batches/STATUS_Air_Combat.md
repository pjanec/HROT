<!--STATUS
state: LIVE — the air lane's progress (built by `ui` as the temporary air lane, branch `air-wip`; the coordinating session merges it into `ui`)
updated: 2026-11-17
current-answer: the whole file — "Where it stands" first
related-designs:
  - ../../designs/air-attack/DESIGN_Air_Combat.md — THE design (§3 the contract, §8 the slices)
  - HANDOFF_Coordinator_To_Air_Combat.md — the dispatch (at 369177d6d; scope frozen there)
  - NOTE_Air_To_Ui_Air_Combat_Contract.md — the ids and names ui keys on
-->

# STATUS — air combat (CE-4647, the `air` lane)

## Where it stands

| slice | state | commit |
|---|---|---|
| **S0 measure + the contract** | ✅ DONE — ids declared, the four answers in the design §1a | `air: the contract —` (see `git log air-wip`) |
| **S1 the autopilot** | ✅ DONE — AC-1, AC-3 (`Steer3D`), AC-4 (GCAS in every AI fixed-wing mode), the jets' flight numbers; 5 / 5 scenarios pass | `air: S1 the autopilot —` |
| S2 missiles that can be beaten | 🟡 HALF — AC-6 the motor + the Monte-Carlo acceptance rail built; AC-9 / AC-9a (`MissileDefence`, the live threat, the last-ditch reflex), the grammar items and the six scenarios NOT yet | `air: S2 (first half) —` |
| S3 the fighters | ⏳ | — |
| S4 the show | ⏳ | — |

## Ids taken *(⛔ permanent)*

| what | value |
|---|---|
| tracker | `CE-10000` (the contract + S0) · `CE-10001` (S1) · `CE-10002` (S2) |
| rulings | `R-10000` … `R-10009` (below) |
| components | `MissileDefenceState` 128 · `AirArena` 160 · `AirCombatSteer` 277 · `FighterPilotState` 278 · `AirCombatState` 279 |
| DDS ordinals | `dtAirCombatSteer` 216 · `dtFighterPilotState` 217 · `dtAirArena` 218 · 219 spare (undeclared) |
| TKB | `Jet_MiG29` 409 · munitions 6060 AIM-9X · 6061 AIM-120 · 6062 R-73 · 6063 R-77 · 6064 M61 HEI · 6065 GSh-30-1 HEI |
| enums | `NavigationMode.AirCombat` = wire `NAV_AIR_COMBAT` = 8 · `WeaponCategory.AirToAir` = 13 · MiG DIS 1.2.222.1.200 |

## Lane decisions *(for the user's review — each says what would change it)*

| id | decision | why | would change it |
|---|---|---|---|
| **R-10000** | the five component ids are **128, 160, 277, 278, 279** | a full scan (every `[ComponentId]`, every int / ushort / byte constant in an `*Ids` file, every literal) on coordinator, behaviors, backend, ui, terrain, convoy and the three exp/agg branches: these five were the only ids nobody declares or uses. 277–279 sat under `NavFakeIds`' stale "262–279" note (its file declares 262–269 only; 270–276 have long been the squad block's) — the note is corrected (a cross-lane comment edit). ⚠ **the 0–511 space is now FULL** — the next lane needs the mask widened (a performance decision: `BitMask512` is one cache line) | a lane that holds one of these five unpushed — then move ours (nothing is recorded or on a cluster yet) |
| **R-10001** | DDS ordinals **216 · 217 · 218** (+ 219 spare) | the top of 207–219 measured free on every branch; the convoy lane is told to take 198–200 + 207 upwards (`NOTE_Coordinator_To_Convoy_Ordinals.md`), so the two never meet | the convoy lane needing more than nine |
| **R-10002** | the MiG is DIS **1.2.222.1.200** (Russia, Fighter, HROT subcategory 200), class `TwinEngineFighter` = `1.2.*.1.200`; the air-to-air rail is `WeaponCategory.AirToAir` = **13** | the class keys the body, so the sub must be ours (the `LandAnimal` 200+ precedent), never a real DIS MiG sub another tool might assign to a different airframe; 13 = the next after `Countermeasure` 12 on every branch | a real DIS table being adopted for aircraft |
| **R-10004** | `AirCombatSteer` is SAVED with a scenario | an authored steer is the motion node's feature-test seam: S1's GCAS scenarios fly a dive before the behaviour (S3) exists; the behaviour overwrites it on its first decision | the behaviour needing the steer cleared on load |
| **R-10005** | AC-1: climbs start EARLY, descents start LATE | AC-1 said "the climb or descent starts early" — an early descent would leave the previous leg's clearance over its highest ground | — |
| **R-10006** | `Steer3D` lifts GRAVITY FIRST: past the g limit the turn gets what is left once the lift holds the aircraft up | without it a max-g "level" turn spiralled down (measured: the 6.5 g rail lost speed and height); with it the bank is acos(1/n), as AC-3 states | — |
| **R-10007** | the Monte-Carlo bands are the MEASURED ones: of 300 seeded all-aspect IR shots at 1.2–3 km, a timely Veteran is hit 6…30 (measured 16), a LateWarning one 25…60 (38), no defence 120…180 (150) | the §5b round (0.4 s lag, the lead on its burn-out speed) is far more dodgeable than the design's EXAMPLE band (30–60 % against a Veteran); the handoff forbids tuning guidance beyond the design, so the numbers stand and the band says what they give | ⭐ the user's call: a harder missile = a shorter lag or the lead on the current speed |
| **R-10008** | GCAS lets go only after ≥ 1.5 s, climbing, with a recovery clearing the floor by 100 m; a route plane then REJOINS its path (a point 1 km ahead until within 30 m) | measured on `ac-gcas-ridge`: released at the floor, a kinematic point order dived straight back at the ridge and the second recovery came too late; measured on a U-turn: the old snap back onto the path read a 7 km/s velocity for a frame | — |
| **R-10009** | the `air-range` terrain is a 2 × 2 km grid; the edge rule carries its ridge and plateau beyond | measured: a 24 km and an 8 km grid did not finish their first load (the navmesh bake) inside the regression's 30 s; 2 km loads in ~20 s | a terrain asset with a prefilled navmesh cache |
| **R-10010** | GCAS watches a POINT order only when the order's own kinematic flight meets the ground; a ground point leaves the 8 m floor | measured: `ua-air-attack`'s gun run failed with GCAS (passes on the base `ui`); with the gate it passes again and `ac-gcas-ridge`'s Viper Low trips GCAS at the ridge (13 s), not at the start | a route order needing the same (none measured) |
| **R-10003** | `AirCombatSteer` ALSO carries the pilot's `Lapse` (beyond §4a); `AirCombatState` carries the AUTHORED `Skill` and `ForcedLapse` (a feature test forces `LateWarning`); `FighterPilotState` also carries `SpeedMps` (the brain's energy) | the motion node must fly a G-LOC wings level and a fixation's lowered floor, so the lapse belongs in the order it reads; a scenario sets a pilot's skill as data, never as code | none known |

## Scenarios *(R-4277 — DONE = `scripts/scenario-regression.sh <name>` passes)*

| slice | scenario | verdict |
|---|---|---|
| S1 | `ac-cruise-altitude` (3 legs: 550 · 426 · 625 m held ±15) · `ac-gcas-ridge` (GCAS 0.25 s / 10.5 s, both alive, ≥ 8 m) · `ac-gcas-inverted` (GCAS at 5 s) · `ac-gcas-steep-ridge` (GCAS, turns away, alive) · `ac-map-edge` (beyond the edge at 32 s, no GCAS) | ✅ 5 / 5 PASS (`scripts/scenario-regression.sh`), goldens captured — see the gate table |
| S2 | `ac-ir-defeated` · `ac-ir-hits-late` · `ac-radar-chaff-beam` · `ac-two-missiles` · `ac-sam-dodge` · `ac-attack-evades` | ⏳ |
| S3 | `ac-guns-drone` · `ac-guns-turning` · `ac-head-on-shot` · `ac-dogfight-1v1` · `ac-winchester` | ⏳ |
| S4 | `ac-dogfight-2v2` · `ac-skill-decides` · `ac-stalemate-lapse` | ⏳ |

## Gates *(CLAUDE.md rule 8 — the contract commit; base = `4fa79837f`)*

| gate | command | result | vs base |
|---|---|---|---|
| T-1 the contract's rails | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --no-build --filter "AirCombatContractTests\|TheDescriptorMapIsWiredTests\|BladeCutWireTests\|HrotOwnershipGroups"` | 23 / 0 / 0 | +4 new (`AirCombatContractTests`), the two descriptor pins extended |
| NED suite | `dotnet test Hrot/Network/Hrot.Network.NED.Tests --no-build` | 198 / 0 after the protocol rows (it was 197 / 1: `TheEntityKeyedSetIsExactlyTheProtocolsEntityKeyedRows` — the three new keyed topics needed their protocol rows) | `RoleGroupOwnershipStrategyTests` pins extended |
| Core suite | `dotnet test Hrot/Engine/Hrot.Core.Tests --no-build` | 377 / 0 / 0 | — |
| the catalogue goldens | `dotnet test Hrot/Engine/Hrot.Presentation.Tests --no-build --filter EntityTypeCatalogTests` | 44 / 0 | golden moved by ONE added line (`409 MiG-29 Fulcrum \| dis 1.2.222.1.200.0.0 \| family Jet \| folder Platform/Air/Russia/Fighter/Air Defense \| icon -`), nothing removed |
| `Map3DTests` | same project, `--filter Map3DTests` | 3 red: `S1_TheIconFallback…`, `CE1309_TheMindStateBar…`, `CE1086_TheFpvDrone…` | ✅ PRE-EXISTING: the same three red at the base `4fa79837f` (a clean worktree, 212 / 3) — not ours |
| protocol | `python3 scripts/protocol-doc-check.py` | OK (162 topics, 105 ordinals) | +3 topics, +3 ordinals, mode 8 documented |
| build | `--no-build` above after `dotnet build <test project> --no-restore` on each | 0 errors | — |

## Gates — S1 + S2 half *(base = `ui` at `e0814c90b` for the scenario comparison)*

| gate | command | result | vs base |
|---|---|---|---|
| T-1 motion + guided suites | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build --filter "GuidedRoundSystemTests\|FixedWingModelTests\|RotorcraftModelTests\|Countermeasure\|AirRoute"` | 74 / 0 / 0 | +10 new rails (7 S1, 3 S2); GCAS rails red-proven (GCAS off ⇒ both red) |
| S1 scenarios | `scripts/scenario-regression.sh ac-cruise-altitude,ac-gcas-ridge,ac-gcas-inverted,ac-gcas-steep-ridge,ac-map-edge` | 5 / 5 PASS; goldens captured, re-verified "same" in fresh processes | new |
| air scenarios that move | the same script on `sam-site, ua-air-defence, ua-air-attack, ua-eject, ua-shootdown-eject, ua-air-strike` | `ua-air-defence` FAILS ("the Apache receives a missile warning") — ✅ PRE-EXISTING: same red on the base worktree; all others PASS; `ua-eject` / `ua-shootdown-eject` goldens re-recorded (described in commit `a91139374`); `sam-site` passes (no golden; its chaff band NOT re-measured — S2's second half) | `ua-air-attack` was red with GCAS and is green again after R-10010 |
| ⛔ not run | `HROT_PARALLEL_SIM` / read-check / world guard, `stepped-hash.py`, the cluster run | — | no behaviour code yet (S3); the motion node is serial |

## Open findings

- ⚠ **GCAS predicts STRAIGHT AHEAD** (as the real Auto-GCAS does): a planned turn short of terrain can trip it (measured on a U-turn 100 m from the wall). Accepted; scenarios keep routes clear of it.
- ⚠ **the §5b missile is very dodgeable** (R-10007) — the user's call whether to make it harder.
- ⏳ **not built** (this session ran out): S2's `MissileDefence` / live threat / last-ditch reflex / grammar items / scenarios; S3 (the behaviour, mode 8's tick-start target cache, guns, AAM rails, radar, deconfliction, the employment zone); S4 (arena system, lapses); the `--mode all` cluster run; `stepped-hash.py` on `ac-dogfight-2v2`.

- ⚠ the component-id space is exhausted (R-10000) — a decision for the user before the next lane needs an id.
- the `AirArena` entity needs a TKB type to be authored (S4): a free entity-type id outside the air blocks — measured then.
