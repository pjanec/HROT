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
| S1 the autopilot | ⏳ next | — |
| S2 missiles that can be beaten | ⏳ | — |
| S3 the fighters | ⏳ | — |
| S4 the show | ⏳ | — |

## Ids taken *(⛔ permanent)*

| what | value |
|---|---|
| tracker | `CE-10000` (the contract + S0) |
| rulings | `R-10000` … `R-10003` (below) |
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
| **R-10003** | `AirCombatSteer` ALSO carries the pilot's `Lapse` (beyond §4a); `AirCombatState` carries the AUTHORED `Skill` and `ForcedLapse` (a feature test forces `LateWarning`); `FighterPilotState` also carries `SpeedMps` (the brain's energy) | the motion node must fly a G-LOC wings level and a fixation's lowered floor, so the lapse belongs in the order it reads; a scenario sets a pilot's skill as data, never as code | none known |

## Scenarios *(R-4277 — DONE = `scripts/scenario-regression.sh <name>` passes)*

| slice | scenario | verdict |
|---|---|---|
| S1 | `ac-cruise-altitude` · `ac-gcas-ridge` · `ac-gcas-inverted` · `ac-gcas-steep-ridge` · `ac-map-edge` | ⏳ |
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

## Open findings

- ⚠ the component-id space is exhausted (R-10000) — a decision for the user before the next lane needs an id.
- the `AirArena` entity needs a TKB type to be authored (S4): a free entity-type id outside the air blocks — measured then.
