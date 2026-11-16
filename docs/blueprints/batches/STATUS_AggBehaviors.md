<!--STATUS
state: LIVE
updated: 2026-11-16
current-answer: §1 slices · §2 decisions · §3 gates · §4 ids
stale-below: nothing
related-designs:
  - HANDOFF_Coordinator_To_AggBehaviors.md — the batch (dispatched at 3f33c0ee3)
  - ../../designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md — the design; §23.5 is this lane's ST-0 ⑦ design
-->

# STATUS — `exp/agg-behaviors`, the stochastic unit's behaviours and the chain (CE-4616)

Started at `b5a884ba7` (from `coordinator@d94af8e2f`). Unattended run. agg-backend started at `992d8d0c8` (no contract NOTE yet); agg-ui not started.

## 1. Slices

| slice | state | sha | rails | design folded |
|---|---|---|---|---|
| ST-0 ⑦ design | ✅ §23.5 — inventory (23 sites), R-8300…R-8304, UML | — | — | §23.5 |
| ST-0 ⑦ increment 1 (neutral DTOs Defend / BreakContact, the contract rail, the routed record) | ✅ built — no site flipped (R-8304) | `dbdc6b9de` | `BehaviorParamsValidationTests.R8300_…` (all 7 kinds / 26 plays) · `SquadManeuverTests.R8301_…` · red-proofs: a play DTO field removed ⇒ R8300 red; `Order` recording "" ⇒ R8301 red; `Release` not unnaming ⇒ R8301 + `CE2141_AMemberThatIsAUnit…` red | §23.5 (as-built table; D-1 `Left`/`Right` are `Bearing`) |
| ST-0 ⑦ the flip (sites → `Route`, R-8305) + ST-0 ③ `StochasticUnitDefend` (R-8306) | ✅ built (agg-backend merged at `d767196c9`) | see §3 | `ST0_Acceptance_…` (SquadManeuverTests.Aggregate.cs) · the physical plays' suites green · red-proofs: `Route` never routing ⇒ acceptance red; the staff's stochastic walk dropped ⇒ red; the Prepared time dropped ⇒ red | §23.5 (D-2) |
| ST-0 ① the four readers' slot-table branch (`AggregateUnits`) | ✅ built | see §3 | `SquadManeuverTests.ST0_*` ×3 · `ScenarioRulesTests.ST0_TheCensus_…` · red-proofs: each reader's branch / the roster-sum arm / the census add / the StrengthOf guard / an allocation in the slot walk ⇒ the matching rail red | §23.6 |
| ST-0 (the whole slice) | ✅ done — except ⛔ the SITREP ammo colour (finding F-1) | | the acceptance rail, plain and `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` | |
| ST-1 (`StochasticUnitMove`, Defend's approach, R-8307/8308) | ✅ built | see git | `ST1_*` ×6 (acceptance: arrival within ±10 % of `PredictSeconds`) | §23.5 |
| ST-2 (`StochasticUnitBreakContact`, `StochasticUnitSop`, `StochasticUnitHold`, the ingress seam; R-8309…8311) | ✅ built | see git | `ST2_*` ×5 (acceptance: under fire it breaks contact) | §23.5 |
| ST-3 (`StochasticUnitAttack`, the two-payload Route + the flipped sites, `AttackOrder` Position, AmmoOf, SPOT; R-8312…8318) | ✅ built | see git | `ST3_*` ×16 (acceptance: approaches, PhaseLine, fights, outcome agrees with the slots; HoldBack; not secured while an enemy stands) · red-proofs: HoldBack ignored ⇒ 1 red · success ignoring the defender ⇒ 1 red · SOP not sparing Attack ⇒ 2 red · Route sending the neutral json to a physical member ⇒ 1 red | §23.5 |
| ST-4 | ✅ (the scenarios, §23.6b) | | | |
| ST-5 A policy (`ResolutionPolicy` 119, `SetResolutionPolicy`, `BandsOf` wired in `CgfLogicPack`; R-8326) | ✅ built | see git | `ResolutionPolicyRails` ×5 · `ScenarioRulesTests.SetResolutionPolicy_…` · `F5_TheBrainPack_…` | §23.5c |
| ST-5 B F-5 (`UnitSopResolutionSystem`; R-8327) | ✅ built | see git | `SquadManeuverTests.F5_*` ×4 | §23.5c |
| ST-5 C D-P3 plan / assign split | ⛔ STOPPED (R-8330: the plan is bound to a commander's EQS sensors + live men) | | | §23.5c |
| **Feature-test scenarios ST-0 … ST-2** (R-4277, §23.6 / §23.6a; `scenarios/demos/aggregates/`, `scripts/gen-agg-scenarios.py`) — merged agg-backend `7f3824e69` (F-9 spawn-time fill, CE-8016) | ✅ 8 of 9 green, 🔴 1 red | see git | `scripts/scenario-regression.sh <name>` (editor tier): `agg-platoon-under-company` ✅ 5/5 · `agg-refused-order` ✅ 3/3 · `agg-recorded` ✅ 2/2 + `--verify-review-at 30` SAME · `agg-march-open` ✅ 3/3 (arrived 1590.25 s vs predicted 1588.9 s; no golden) · `agg-march-mounted` ✅ 4/4 (no golden, F-13) · `agg-route-dropped` ✅ 7/7 · `agg-detect-by-range` ✅ 3/3 · `agg-break-contact` ✅ 6/6 · `agg-sees-physical` 🔴 1/3 (F-10: N-3 not built, filed to agg-backend). Goldens captured for all but `agg-march-open` (1760 s), `agg-march-mounted` (F-13) and the red one; the seven goldened ones re-ran SAME in a fresh process except `agg-march-mounted` (once DIFFERENT, then 3 / 3 SAME). Grammar: field-cue `*` path + `elements` + `where` (R-8320, `ScenarioChecksTests.R8320_…`, red-proved twice) |
| **Feature-test scenarios ST-3 / ST-4 / mixed** (R-4277, §23.6b; R-8321…R-8325) | ✅ 6 of 6 green | see git | `agg-platoon-duel` ✅ 6/6 · `agg-shelled-unit` ✅ 5/5 · `agg-prepared-defence` ✅ 3/3 · `agg-out-of-ammo` ✅ 5/5 · `agg-battalion-fight` ✅ 20/20 · `agg-mixed-company-attack` ✅ 9/9. Goldens captured and SAME on a fresh run for all but the mixed one (26 entities with physical squads, 9 min wall — not captured). Previews committed. Stepped-hash on the battalion: STATE EQUAL at 1, 4, 8 threads (window ~2 s of sim, before contact) |
| **Scenario re-run after the ST-5 merge (E)** | 11 of 15 pass; 🔴 `agg-sees-physical` (F-10 still: no contact / SPOT on a physical squad) · 🔴 `agg-out-of-ammo` (slots not all empty in 60 s, SITREP not Black) · 🔴 `agg-battalion-fight` (Red company 3 Green / company 1 slots) · 🔴 `agg-detect-by-range` (700 m contact misses the window) — the last three are engine-side timing / lethality moves of the merge (CE-8019 lethality, sensing), NOT baseline-verified; not touched | see git | 8 passing goldens re-captured (platoon-duel, shelled-unit, prepared-defence, break-contact, platoon-under-company, recorded, refused-order, route-dropped) · march-mounted / march-open / mixed have none | |
| **The four reds root-caused and re-authored (§23.6c, R-8331…R-8334)** — bisected in a scratch worktree (agg-backend commits reverted one at a time on `a6ba7cf86`, after a fresh restore + build ruled out a stale binary) | ✅ 4 of 4 green, goldens re-captured and SAME on a fresh run | see git | `agg-detect-by-range` ✅ 5/5 (moved by `5d8a30150` R-8006, the eye capped at 150 m: near 100 m by eye, mid 2100 m through OPTICS, 8 steps, reported 7.25 s) · `agg-battalion-fight` ✅ 24/24 (moved by `5d8a30150` R-8005: Red co 1 and 2 now ≥ 10 down ⇒ Amber; rows judged by colour BAND; the HQ roster lists the companies 3, 2, 1) · `agg-out-of-ammo` ✅ 5/5 (moved by `6d9634b56` CE-8021: the order-less target, now with a pool, broke contact under our SOP; it now DEFENDS) · `agg-sees-physical` ✅ 5/5 (N-3 works: OPTICS at 700 m on flat berlin — reported 9.25 s, SPOT 12.25 s; findings F-19 / F-20 block the eye-only basic-desert version) | §23.6c |
| **Feature-test scenarios ST-5** (R-4277, §23.6d; R-8335…R-8337) | ✅ 4 of 4 green, goldens captured and SAME on a fresh run, previews | see git | `agg-split-on-contact` ✅ 66/66 · `agg-squad-holds-house` ✅ 42/42 (as built: fallback line, D-P3 stopped) · `agg-merge-back` ✅ 63/63 · `agg-forced-handover` ✅ 6/6. Grammar: a `field` cue's `who` may be a network id (R-8335, `ScenarioChecksTests.R8335_…`, red-proved; `ScenarioChecksTests` 14/0) | §23.6d |

## 2. Lane decisions (for the user's review)

| id | decision | would change it |
|---|---|---|
| R-8300…R-8311 | see the design §23.5 (R-8307…R-8311: ST-1 / ST-2) | — |

## 3. Gates

| command | `--no-build` | result |
|---|---|---|
| `dotnet test Hrot.SimHost.Tests --filter BehaviorParamsValidationTests\|SquadManeuverTests\|CommanderNodesTests\|UnitCommander\|TacticalIntentResolutionSystemTests\|BehaviorParamsSweepTests\|CgfBehaviorSetupTests` | yes (built the test project) | 328 / 0 — plain AND `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` |
| `dotnet test Fdp.Toolkits.Tests --filter Behavior\|SubordinateOrders` | yes | plain 640 / 0 · parallel env 639 / 1 — `BTreeHostsBTreeTests.U0_R5_…` ("Component size 4096 exceeds maximum 1024", deterministic, BTree host, not touched here; ⚠ not re-proved at the base) |
| `dotnet test Hrot.Editor.Tests --filter Order\|ABehaviourAdvertises` | yes | 49 / 1 — `TheTaskPickerTests.H5_ARifleman…` ⛔ PRE-EXISTING: fails identically with this batch's `OrderDtos.cs` reverted (Combat 14→16, Casualties 6→7) |
| `dotnet test Hrot.Editor.AiShared.Tests --filter TheOrdersTabTests` | yes | 4 / 0 |
| `dotnet test Hrot.Presentation.Tests --filter Menu\|Shortcut\|Keymap\|Order\|RuleEditorR5b` | yes | 175 / 0 after the goldens regenerated (A1/A2 `TheMenusAreGoldenTests`: 2 files, 8 lines each, action ids only; K2 letters D / B declared), plain and parallel env |
| `python3 scripts/tracker-counts.py --check` | — | OK |
| **ST-0 ① / ⑦ flip / ③** — `dotnet test Hrot.SimHost.Tests --filter SquadManeuverTests\|ScenarioRulesTests\|Behavior\|Commander\|TacticalIntent\|CgfBehaviorSetup\|Aggregate\|Staff\|Objective\|Census\|Order\|R830\|ST0_` | yes (SimHost.Tests built) | plain 626 / 1 (`LoadPhaseChainTests.CE3345`, flaky — green alone); parallel env 623 / 4 (the four pre-existing, F-4) — every `ST0_*`, `R830*` and the physical plays' suites green in both |
| `dotnet test Fdp.Toolkits.Tests` (whole) | yes | plain 5168 / 7 · parallel 5162 / 13 — all pre-existing (F-4); the S0 seconds rail no longer lists `DefendOrder` (allow-listed with its plays' DTOs) |
| `dotnet test Hrot.Core.Tests` routing / fit / order / params | yes | 33 / 0 |
| `dotnet test Hrot.Editor.Tests` order / behaviour / picker | yes | 97 / 2 — `H5_…` pre-existing, `CE4350…` golden stale (F-3) |
| `dotnet test Hrot.Presentation.Tests` menu / order / task | yes | 166 / 0 |

| **ST-3** — `dotnet test Hrot.SimHost.Tests --filter SquadManeuverTests\|BehaviorParamsValidationTests\|Aggregate\|ResolutionRouting\|StochasticUnit\|TacticalIntent\|SopParamsRoundTrip\|CommanderNodes\|Staff\|BehaviorParamsSweep\|CgfBehaviorSetup` | yes (SimHost.Tests built) | 384 / 0 plain AND `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` |
| ST-3 whole `Hrot.SimHost.Tests` | yes | 2876 / 22 plain: ours = `SopParamsRoundTripTests.CE2079` (fixed, green); `BehaviourHostingSnapshotTests.CE1431` ⛔ NOT regenerated — the diff holds `AnimalHsm` (CE-4623, another lane) besides our 6 StochasticUnit* rows; the rest pre-existing (OneBootstrap, EpisodeLoad ×5 / ReferencePreview ×11 / LoadPhaseChain pass alone, CE4208, UtmEverywhere) |
| ST-3 `Hrot.Editor.Tests` | yes | 938 / 6, none ours: CE4350 golden (F-3, + `AirSense`), `LiveBlackboardValueProviderTests` CE1412 / CE1430, `E2_…Terrain`, `TheOneSpawnAdapterConstruction…`, `H5_…` — all fail on messages that do not name the Attack order; `OrderVocabularyTests` green |
| ST-3 `Hrot.Core.Tests` | yes | 380 / 0 |
| ST-3 `Fdp.Toolkits.Tests` | yes | 5191 / 8 — the S0 rails (bare Vector3 / NodeId / Seconds members of other types, none `AttackOrder`), `PredicateCompilerLevel…`, `MissionDirector…` — pre-existing (F-4); not re-proved at the base sha |
| ST-3 `Hrot.Presentation.Tests` | yes | 1994 / 3 — `EntityTypeCatalogTests.CE4271` atlas, `Map3DTests` CE1309 / CE1086, unrelated to orders; menu goldens unchanged |
| **ST-5 scenarios / post-merge reds** — `dotnet test Hrot.Editor.Tests --filter ScenarioChecksTests` (the R-8335 grammar) | yes (Hrot.Editor.Tests built) | 14 / 0; red-proof: the id branch dropped ⇒ `R8335_…` red. Scenarios: `scripts/scenario-regression.sh` the 8 touched / new, each pass + golden SAME on a second fresh process (runner rebuilt after `dotnet restore`) |

## 4. Ids allocated

R-8300…R-8318 · R-8326…R-8330 (ST-5) · R-8331…R-8334 (the four re-authored scenarios, §23.6c) · R-8335…R-8337 (ST-5 scenarios, §23.6d) · CE-8300 (policy) · CE-8301 (F-5) · R-8320 · R-8321…R-8325 (the six scenarios' lane decisions, §23.6b) (a `field` cue's `*` path with `elements` / `where`) (R-8312 assault 100 m; R-8313 search radius 75 m; R-8314 the ammo share; R-8315 two-payload Route; R-8316 AttackOrder fields; R-8317 the SOP spares an attack; R-8318 success judged at the objective; R-8307 arrival radius / line depth; R-8308 retries; R-8309 break point; R-8310 the SOP rows; R-8311 the SOP seam; R-8305: `CommandOrders.Route` / `KeyFor`; R-8306: the Prepared times, Hasty 0 / Prepared 600 s / Fortified 3600 s).

## 5. Findings

| # | finding | for |
|---|---|---|
| F-1 | ⛔ `UnitSlot` has no ammo capacity (`Ammo` is a count; `UnitHoldings.Ammo` a pool) ⇒ a stochastic unit's SITREP ammo colour is UNKNOWN (`AmmoOf` adds nothing for it) | agg-backend: a capacity field or a per-echelon basic-load constant |
| F-2 | the staff composer served only rostered units: a roster-less stochastic unit never flushed its outbox (no SITREP / Set / the refusal's TaskFailed). Fixed here (`StaffComposerSystem._stochastic`) — agg-backend's `CE8002` rail asserted the QUEUE, never the delivery | agg-backend (aware; no action unless it prefers an empty `UnitRoster` on every stochastic unit) |
| F-3 | `TheOfferedBehavioursFitTheEntityTests.CE4350…` golden is STALE from other lanes (26 `ClearObstacle` rows, 21 new TKB types, …) before this batch; a regenerate would also take `StochasticUnitDefend` (7 rows). Not regenerated here | whoever regenerates it |
| F-4 | pre-existing reds, not touched: `TheTaskPickerTests.H5_…`, the `S0*RailTests` (Terrain / Orchestration / ReplayBrowser members), `BTreeHostsBTreeTests.U0_R5_…` + `MissionDirector…` + gizmo alloc rails (Toolkits); under the parallel env `HoldTheLine…CE2241`, `TacticsTrees…CE3170`, `StationWeapon…CE2238` ×2; `LoadPhaseChainTests.CE3345` flaky (green alone) | their lanes |
| F-5 | ✅ RESOLVED in ST-5 (R-8327, `UnitSopResolutionSystem`). Was: ST-2: the SOP is substituted when the template SOP is ASSIGNED; a unit that becomes stochastic LATER (a merge, ST-5) keeps its physical SOP until something re-assigns it | ST-5 (the merge must publish the table's SOP) |
| F-6 | `March` / `Dash` resolve to `StochasticUnitMove` but carry `SquadMarch`-shaped JSON, which the strict `MoveOrder` parse refuses (R-8303 increment 2: a neutral March DTO) | agg-behaviors increment 2 |
| F-8 | agg-backend's phase A fires every live system at every reported contact in range, moving or not — no base-of-fire share input; HoldBack changes only the movement | agg-backend, if a share is wanted |
| F-1, F-6 | RESOLVED in ST-3 (R-8314 ammo; R-8315 the March `MoveOrder`) | — |
| F-7 | the staff also reports Set / TaskFailed when a commander's TASK ends, so a Move play's own Set duplicates it (same kind, newest wins) — harmless; the play reports explicitly so it holds without a staff | — |
| F-9 | (agg-backend's spawn-time fill, CE-8016) merged and used by every scenario | resolved |
| F-10 | ✅ RESOLVED (agg-backend CE-8018 built N-3; `agg-sees-physical` green with optics at 700 m, §23.6c). Was: ⏳ N-3 is not built: a stochastic observer never targets a PHYSICAL unit, so `agg-sees-physical` is red (rows 1 and 2) | agg-backend — [NOTE_AggBehaviors_To_AggBackend](NOTE_AggBehaviors_To_AggBackend.md) |
| F-11 | ✅ RESOLVED (CE-8021: the fill provisions the pool; detect-by-range / sees-physical author none now). Was: nothing provisions `SquadCognitiveState` on a memberless stochastic unit, so N-2's contact pool is never written in a scenario run; three scenarios author it | agg-backend — same note |
| F-12 | a scenario `Order` verb checks the PHYSICAL class table first: a rifle unit cannot be ordered Strike from a scenario (the refused-order scenario uses an artillery battery) | the regression / macros lane, if a stochastic-table order from a scenario is wanted |
| F-13 | a captured golden of `agg-march-mounted` differed once in five fresh-process runs: the unit's position was one step (16.7 m/s × 1/60 s) off from t = 15 s (`AggregateUnit`, `SimTransform`, `FireAllocation`, `SopState` hashes) — the Move play / the follower started a frame later. Not reproduced in 3 more runs; the other six goldened scenarios repeat | agg-backend, if they see it (the fill → first brain tick → first follower step order under the threaded node) |
| F-14 | an authored `Blue Stochastic Squad` (and a natural one) under a physical company running `CompanyDefend` never fired at a contact 300 m away in 200 s, while the same squad under `CompanyAttack` fired at 1.25 s — not investigated (a hold-fire ROE or the defend position out of range?); the out-of-ammo scenario uses CompanyAttack | whoever owns CompanyDefend's orders to a stochastic unit |
| F-15 | `agg-battalion-fight`: the fight is one-sided and short — the 90-man assault company is destroyed within 7 s of contact (~335 s) while the Red company in front of it loses 5 and its stress jumps to the break level on 60 slots at the first contact; the base-of-fire company (Set at ~290 s) and the reserve never engage, so no side is broken and nothing is secured. Same shape as the duel (the first second's stress). Balance for the C-8 band | agg-backend (CE-8009) |
| F-19 | ⛔ a stochastic unit keeps its authored z (0): N-3's line from z + 2 m runs into basic-desert's ground (LOS route: blocked by terrain at 31.6 m) | agg-backend — NOTE |
| F-20 | ⚠ the naked eye (150 m, R-8006) is inside the split band (600 m / forced 300 m): an unpinned eye-only observer is split before it can see a body | agg-backend — NOTE (design question) |
| F-21 | ⚠ the eye reach is one number per process (the first-filled unit's men, max VisionRange — an AT man's launcher sight counts) | agg-backend — NOTE |
| F-22 | ⚠ a split's children carry no name (`UnitResolution.cs:348`): checks, the golden and the UI cannot name them | agg-backend — NOTE |
| F-23 | ⛔ merge distance (band + margin, 500 m) is inside the split trigger (band + lead, 600 m): a unit flip-flops every ~63 s with an enemy at 500–600 m (measured) | agg-backend — NOTE |
| F-16 | an authored `UnitSlotTable` is not filled, so the personal-weapon map (`KillerVictimTkb.PersonalSoldiers`) is taught only by ANOTHER, filled unit in the world; a scenario with only authored tables fires nothing | agg-backend, if authored tables are meant to stand alone |

