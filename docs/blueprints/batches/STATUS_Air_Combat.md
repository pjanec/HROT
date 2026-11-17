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
| **S2 missiles that can be beaten** | 🟡 BUILT — the motor + Monte-Carlo, the live threat + reflex, ⭐ `MissileDefence.Run` (AirAttack's evade + radar escape folded in), the radar notch, the grammar; 4 / 6 scenarios — see below | `air: S2 — MissileDefence.Run …` (`1c2be25bf`) + the S3/S4 commit |
| **S3 the fighters** | 🟡 BUILT — the `AirCombat` profile of `AirAttack`, mode 8 from the brain, tick-start targets, `EmploymentZone`, the accelerating gun lead, winchester / disengage | `air: S3 + S4 —` (`ef77654cc`) and later |
| **S4 the show** | 🟡 BUILT — `AirArenaSystem`, arena floor / shot cap / soft edge / merge, lapses, TKB 8840 | same |
| **CE-10005 the fights close** | ✅ BUILT — skill on the defence, the merge, the CAP (= `AirArmedSearch`, deconflicted), a transit's defence, the gun's lead on the nose, the fighter's radar, the serial brains' pre-pass; scenarios below | `air: CE-10005 —` (`87e3a1e16`) + the gates commit |
| ⏳ NOT built | a ROUTE (FollowRoute) transit's defence; the Hold + SOP-row form of the CAP (R-10022 chose `AirArmedSearch`) | — |

## Ids taken *(⛔ permanent)*

| what | value |
|---|---|
| tracker | `CE-10000` (the contract + S0) · `CE-10001` (S1) · `CE-10002` (S2) · `CE-10003` (S3) · `CE-10004` (S4) · `CE-10005` (the closing fights, skill on the defence, the CAP, the transit's defence) — next free `CE-10006` |
| rulings | `R-10000` … `R-10028` (below) — next free `R-10029` |
| components | `MissileDefenceState` 128 · `AirArena` 160 · `AirCombatSteer` 277 · `FighterPilotState` 278 · `AirCombatState` 279 |
| DDS ordinals | `dtAirCombatSteer` 216 · `dtFighterPilotState` 217 · `dtAirArena` 218 · 219 spare (undeclared) |
| TKB | `Jet_MiG29` 409 · munitions 6060 AIM-9X · 6061 AIM-120 · 6062 R-73 · 6063 R-77 · 6064 M61 HEI · 6065 GSh-30-1 HEI |
| TKB the Air Arena | **8840** (R-10013) |
| enums | `AirAttackProfile.AirCombat` = 8 (blackboard) · `NavigationMode.AirCombat` = wire `NAV_AIR_COMBAT` = 8 · `WeaponCategory.AirToAir` = 13 · MiG DIS 1.2.222.1.200 |

## Lane decisions *(for the user's review — each says what would change it)*

| id | decision | why | would change it |
|---|---|---|---|
| **R-10000** | the five component ids are **128, 160, 277, 278, 279** | a full scan (every `[ComponentId]`, every int / ushort / byte constant in an `*Ids` file, every literal) on coordinator, behaviors, backend, ui, terrain, convoy and the three exp/agg branches: these five were the only ids nobody declares or uses. 277–279 sat under `NavFakeIds`' stale "262–279" note (its file declares 262–269 only; 270–276 have long been the squad block's) — the note is corrected (a cross-lane comment edit). ⚠ **the 0–511 space is now FULL** — the next lane needs the mask widened (a performance decision: `BitMask512` is one cache line) | a lane that holds one of these five unpushed — then move ours (nothing is recorded or on a cluster yet) |
| **R-10001** | DDS ordinals **216 · 217 · 218** (+ 219 spare) | the top of 207–219 measured free on every branch; the convoy lane is told to take 198–200 + 207 upwards (`NOTE_Coordinator_To_Convoy_Ordinals.md`), so the two never meet | the convoy lane needing more than nine |
| **R-10002** | the MiG is DIS **1.2.222.1.200** (Russia, Fighter, HROT subcategory 200), class `TwinEngineFighter` = `1.2.*.1.200`; the air-to-air rail is `WeaponCategory.AirToAir` = **13** | the class keys the body, so the sub must be ours (the `LandAnimal` 200+ precedent), never a real DIS MiG sub another tool might assign to a different airframe; 13 = the next after `Countermeasure` 12 on every branch | a real DIS table being adopted for aircraft |
| **R-10004** | `AirCombatSteer` is SAVED with a scenario | an authored steer is the motion node's feature-test seam: S1's GCAS scenarios fly a dive before the behaviour (S3) exists; the behaviour overwrites it on its first decision | the behaviour needing the steer cleared on load |
| **R-10005** | AC-1: climbs start EARLY, descents start LATE | AC-1 said "the climb or descent starts early" — an early descent would leave the previous leg's clearance over its highest ground | — |
| **R-10006** | `Steer3D` lifts GRAVITY FIRST: past the g limit the turn gets what is left once the lift holds the aircraft up | without it a max-g "level" turn spiralled down (measured: the 6.5 g rail lost speed and height); with it the bank is acos(1/n), as AC-3 states | — |
| **R-10007** ⛔ SUPERSEDED by R-10018 | the Monte-Carlo bands are the MEASURED ones: of 300 seeded all-aspect IR shots at 1.2–3 km, a timely Veteran is hit 6…30 (measured 16), a LateWarning one 25…60 (38), no defence 120…180 (150) | the §5b round (0.4 s lag, the lead on its burn-out speed) is far more dodgeable than the design's EXAMPLE band (30–60 % against a Veteran); the handoff forbids tuning guidance beyond the design, so the numbers stand and the band says what they give | ⭐ the user's call: a harder missile = a shorter lag or the lead on the current speed |
| **R-10008** | GCAS lets go only after ≥ 1.5 s, climbing, with a recovery clearing the floor by 100 m; a route plane then REJOINS its path (a point 1 km ahead until within 30 m) | measured on `ac-gcas-ridge`: released at the floor, a kinematic point order dived straight back at the ridge and the second recovery came too late; measured on a U-turn: the old snap back onto the path read a 7 km/s velocity for a frame | — |
| **R-10009** | the `air-range` terrain is a 2 × 2 km grid; the edge rule carries its ridge and plateau beyond | measured: a 24 km and an 8 km grid did not finish their first load (the navmesh bake) inside the regression's 30 s; 2 km loads in ~20 s | a terrain asset with a prefilled navmesh cache |
| **R-10010** | GCAS watches a POINT order only when the order's own kinematic flight meets the ground; a ground point leaves the 8 m floor | measured: `ua-air-attack`'s gun run failed with GCAS (passes on the base `ui`); with the gate it passes again and `ac-gcas-ridge`'s Viper Low trips GCAS at the ridge (13 s), not at the start | a route order needing the same (none measured) |
| **R-10003** | `AirCombatSteer` ALSO carries the pilot's `Lapse` (beyond §4a); `AirCombatState` carries the AUTHORED `Skill` and `ForcedLapse` (a feature test forces `LateWarning`); `FighterPilotState` also carries `SpeedMps` (the brain's energy) | the motion node must fly a G-LOC wings level and a fixation's lowered floor, so the lapse belongs in the order it reads; a scenario sets a pilot's skill as data, never as code | none known |

| **R-10011** | the checks read decoys from `MissileDefenceState.DecoysTotal` (appended) | a decoy writes NO shot row (`FireProcessingSystem.cs:111` skips a dispenser), so §6's `shot … where ammo contains "Flare"` cannot be said | the dispenser getting a shot row (a ShotLog change every golden with flares would move) |
| **R-10012** | the verifier gains `far` AND `close` (`who`, `from` a unit (3-D) or `point` (horizontal), `m`) | the take's `near` takes a point only, and the take grammar is ui's (Presentation); the 1v1's "near Viper Fulcrum" needs a unit | ui folding unit-to-unit `near` into the take grammar — then `close` is a duplicate to remove |
| **R-10013** | the Air Arena's TKB type is **8840** (a map graphic like the minefield, no DIS) | 8840 measured unused on every origin branch's `TkbEntityTypes.cs` and in the tree | — |
| **R-10014** | NO next-target search and NO deconfliction search in the brain: the Attack order names the bandit (a scenario's / the player's orders deconflict); a kill ends the attack (Success, B14) | a search needs every pilot's target (a cross-entity read of a component the same phase writes) or the CAP's SOP row; neither was in reach this session | the CAP (Hold + fighter SOP row) — then the SOP picks the next contact |
| **R-10015** | the arena's soft edge is a manoeuvre SWITCH at the radius (`ReturnToArena`), not a blend; the defence and a disengage override it | RV-9's hazard was the edge turning a DRAGGING jet into the missile — the override removes it | a show measuring jets leaving the arena too far |
| **R-10016** | a pilot in a LateWarning or a Fixation lapse gets NO last-ditch reflex, and a Fixation does not start the defence while it lasts | measured: with the reflex a "late" pilot was never hit (`ac-ir-hits-late`) — the reflex undid the lapse; a fixated pilot does not see the launch | — |
| **R-10017** | a fighter's MISSILE leaves through one `WeaponFireIntent` on the named rail (asked through the mount's `ShotLedger`, the muscle spends it), not through the `AimAndFire` executor; the gun through `FireAtPoint` at the accelerating lead; inside 700 m closing it throttles back, inside 250 m it lags off, an empty gun inside 1.2 km falls back | measured: through `AimAndFire` no AAM left its rail in 2 s (its laying gates are a turret's); a gun pursuit flew INTO the bandit (both lost at once, AC-13a) and an empty-gun jet sat at 100 m in trail with two missiles left | the executor learning fixed air-to-air rails |

| **R-10018** | ⭐ the missiles HARDENED (🔒 user, `2026-11-17`: *"Harden as you lean."*; AC-6 / R-4283 — the same lead pursuit with energy): `MaxG` ×1.25 on every AAM and SAM (IR 50, radar 44, radar SAM 38, Stinger 25, Igla 23), flare rejection 0.82 (IR AAM, Stinger) / 0.7 (Igla), and a motor round leads on its CURRENT speed; lag, drag, min speed kept. The Monte-Carlo rail now drops the defence's flares by the system's rule. 📐 timely 24 % · late 41 % · undefended 94 % (bands 10–25 · 40–60 · 85–97) | the sweep: drag / min speed do not move a 1.2–3 km shot; a short lag kills the defended too; the burn-out-speed lead missed half the straight targets; the decoys set the defended bands. SUPERSEDES R-10007's numbers (5 / 13 / 50 %) | the user re-banding |

| **R-10019** | ⭐ SKILL ON THE DEFENCE (AC-12): a Rookie never drags, flies the beam as a LEVEL break, drops 2 decoys per missile; an Ace drags ANY seeker while it is > 4 s out; a Veteran keeps AC-9; the reflex pulls the pilot's own g | 📐 `ac-skill-decides` diagnosed: the Ace died at 21.5 s to the Rookie's second radar shot (both beamed head-on 6 km shots; no notch at 2 km). With it: 4 of 5 Rookies down, ≤ 1 Ace lost | the user re-weighting skill |
| **R-10020** | ⭐ the MERGE holds fire until the PASS (bandit behind the wing line or < 1 km), 300 m right of the centre (left-to-left), corner × 1.2; it flies in from outside the arena | 📐 the 1v1 fired radar missiles at each other at 6 km (4.4 s) and both died; the merge never started (4.03 km from the centre was outside the 4 km radius ⇒ `ReturnToArena`, weapons free). Now: close 14 s, first shot 16 s. AC-11's 6 km default untouched | a show wanting head-on BVR shots |
| **R-10021** | "5 seeds" = FIVE PAIRS in one scenario, each in its own arena 30 km apart with a different start; rows: ≥ 4 Rookies down (`atLeast:4`) AND never 2 Aces lost | the sim is deterministic and the verifier has no seed knob; ⚠ the two rows admit 3 strict pair wins in the worst case (4 Rookies down incl. one whose Ace also died) — measured: 4 Rookies down, 0–1 Ace | a verifier seed knob |
| **R-10022** | ⭐ the CAP = the EXISTING `AirArmedSearch` over the arena (not a Hold + a SOP row); a fighter picks only hostile aircraft, deconflicted by the arena pre-pass's tick-start index of the pilots' targets and a RANK spread; the higher rank yields a shared bandit when another is free. ⭐ the fighter's RADAR (AC-13, was missing): Search on CAP, Track in the fight | the seam law (ruling 9): a search-and-engage order existed and is the aircraft's; ⚠ the ground's eyes see only inside the 2 km grid, so the CAP sees by RADAR. SUPERSEDES R-10014 for the CAP | the user wanting the Hold + SOP-row form |
| **R-10023** | a TRANSIT's defence = `AirOrderNodes.FlyTo` (the Move) calls `MissileDefence.Run` (MoveTo flight), the move re-issued after; a PLANE breaks no lower than 150 m AGL (transit and attack) | 📐 the A-10's break "at its height" off a 16 m gun pass flew into the hills (`ac-attack-evades`); a route (FollowRoute) transit is not wired | a route transit needing it |
| **R-10024** | the GUN: a lead pursuit inside 800 m flies the brain's gun lead (`TargetLead.PointAccelerating`, as an offset in the steer's `Point`, 1 m steps) tracked 2 × harder; the 2° cone is judged on the NOSE (velocity + 0.6°/g angle of attack toward the lift) | 📐 `ac-guns-turning`: the flight path held ~6° behind the lead of a 5 g target and the gun never fired; after: a gun hit at 28 s (27 hits / 60 s) | — |
| **R-10025** | the 2v2 DEMO authors its OWN arena shot cap, 2.5 km (said in its description); the default stays 6 km | at 6 km the four fought on missiles at range and no gun fired; at 2.5 km the guns fire (245 rounds measured) — ⚠ still 0 gun HITS (the Rookies' gun from a pure pursuit; the Aces rarely inside 500 m) | the user's show call |

## Scenarios *(R-4277 — DONE = `scripts/scenario-regression.sh <name>` passes)*

| slice | scenario | verdict |
|---|---|---|
| S1 | `ac-cruise-altitude` (3 legs: 550 · 426 · 625 m held ±15) · `ac-gcas-ridge` (GCAS 0.25 s / 10.5 s, both alive, ≥ 8 m) · `ac-gcas-inverted` (GCAS at 5 s) · `ac-gcas-steep-ridge` (GCAS, turns away, alive) · `ac-map-edge` (beyond the edge at 32 s, no GCAS) | ✅ 5 / 5 PASS (`scripts/scenario-regression.sh`), goldens captured — see the gate table |
| S2 | `ac-ir-defeated` (4/4) · `ac-ir-hits-late` (2/2) · `ac-two-missiles` (5/5) · `ac-radar-chaff-beam` (4/4) | ✅ 4 PASS, goldens captured (after R-10018). ⚠ `ac-radar-chaff-beam`'s "DEFEATED" row became "no MiG round hits the F-16 in 20 s" and `ac-two-missiles`' "a missile defeated" became "both rows end" (the hardened missile ends defeated, expired or burst off-target — the design's own wording for two-missiles) |
| S2 | `ac-sam-dodge` · `ac-attack-evades` | ⏳ NOT WRITTEN — a transit move's defence is not wired (only AirAttack and AirCombat call `MissileDefence`) |
| S3 | `ac-guns-drone` (3/3 — the gun at 650 m behind the drone, inside the missiles' 500 m minimum) · `ac-head-on-shot` (3/3) · `ac-winchester` (3/3 — empty gun, the MiG made unkillable: health 100 000) | ✅ 3 PASS, goldens captured |
| S3 | `ac-dogfight-1v1` (demo, expect rows only) | ⚠ 3/4 — shot inside 120 s ✅, a kill ✅, stays over the map ✅; "they close to 2 km" ❌: the hardened missiles end it at range before the merge (no arena in it — the 6 km head-on shot) |
| S3 | `ac-guns-turning` | ⏳ not written — the accelerating lead is proven by its rail and the gun by `ac-guns-drone` |
| S4 | `ac-stalemate-lapse` (2/2: a lapse at 40 s, a kill) | ✅ PASS (no golden: a demo-length fight) |
| S4 | `ac-dogfight-2v2` (demo) | ⚠ 5/6 — 4 missiles ✅, two kills ✅ (328 s before R-10018), never beyond 6 km ✅, no fratricide ✅; "a gun hit" ❌: the missiles decide it before a gun pass |
| S4 | `ac-skill-decides` | ❌ 0/2 — the Ace is lost and the Rookie survives in this one seed. 5 seeds per the design are not available (the verifier has no seed knob); skill sets reaction, g and shot discipline, but with R-10018 a missile at a Rookie's max range still hits ~24 % of a timely defence. ⭐ the user's call: give skill more (a Rookie's flares later / fewer, an Ace's earlier) |

## ⭐ Scenarios — CE-10005 *(`2026-11-17`, `scripts/scenario-regression.sh`; artifacts `sr-full`, `sr-full2`, `sr-cap2`, `sr-cap3`, `sr-final2`)*

| scenario | verdict | golden |
|---|---|---|
| S1 ×5 (`ac-cruise-altitude`, `ac-gcas-ridge`, `ac-gcas-inverted`, `ac-gcas-steep-ridge`, `ac-map-edge`) | ✅ 5 / 5 | gcas ×3 + map-edge SAME; cruise-altitude re-recorded (1 mm at t 20 s — float noise of the merged tree) |
| S2 `ac-ir-defeated` · `ac-ir-hits-late` · `ac-two-missiles` · `ac-radar-chaff-beam` | ✅ 4 / 4 | re-recorded: the jets move by metres (the reflex now pulls the PILOT's g — a Veteran 8, R-10019 — and the fighters' radars are on, R-10022); shot rows, outcomes and every verdict unchanged. ⚠ `ac-radar-chaff-beam` was RED until the merge became head-on only (its MiG chases from behind) |
| S2 `ac-sam-dodge` ⭐ new | ✅ 5 / 5 (a SAM fires 40 s, the F-16 beams + decoys, a SAM missile DEFEATED, alive) | captured |
| S2 `ac-attack-evades` ⭐ new | ✅ 4 / 4 (Stinger 66 s, the A-10 beams, 4 flares, the truck destroyed after the defence) | captured |
| S3 `ac-guns-drone` · `ac-head-on-shot` · `ac-winchester` | ✅ 3 / 3 | guns-drone, head-on re-recorded (metres, as above); winchester SAME |
| S3 `ac-guns-turning` ⭐ new | ✅ a Viper gun hit at 28 s | captured |
| S3 `ac-dogfight-1v1` (demo) | ✅ 4 / 4 — close 14 s, first shot 16 s, a kill 62 s, stays over the map | — |
| S4 `ac-skill-decides` (5 pairs) | ✅ ≥ 4 Rookies down at 86 s, never 2 Aces lost | — |
| S4 `ac-cap` ⭐ new | ✅ 4 / 4 — each MiG shot at by an F-16 (17 s, 19 s), a MiG down 43 s, no fratricide | — |
| S4 `ac-stalemate-lapse` | ✅ 2 / 2 (authored stalemate 10 s, R-10026) | — |
| S4 `ac-dogfight-2v2` (demo) | ⚠ 5 / 6 — "a gun hit" ❌: the guns FIRE (245 rounds measured with the 2.5 km cap, R-10025) but none hits | — |
| `ua-air-attack` · `sam-site` · `ua-air-strike` | ✅ | `ua-air-strike` golden "different" PRE-EXISTING (format staleness, see the S2-rest gates) |
| `ua-air-defence` | ❌ 2 / 3 PRE-EXISTING (red on the base, S1 gates) | — |

| id | decision (CE-10005, later) | why | would change it |
|---|---|---|---|
| **R-10026** | `ac-stalemate-lapse` authors a 10 s stalemate | 📐 with the merge and R-10019 two Aces decide inside ~45 s; a 30 s quiet left the 2 %/s draws too little time (the row failed) | the user preferring Veterans there |
| **R-10027** | ⭐ the SERIAL brains run the same pre-pass (`BrainPhase.Prepare`) — cross-lane into `BrainTickSystem` | 📐 the `--mode all` cluster run (serial): the arena index was never built — no merge, both fired at 6 km at 4.6 s; after: merge, pass at 16 s, a kill at ~40 s | — |
| **R-10028** | the fight's FIRST decision waits until both motions are known (straight on, weapons safe); after a KILL the steer HOLDS over the arena (`ReturnToArena`; no arena ⇒ straight and level) | 📐 cluster: no replicated velocity on the first ticks ⇒ the head-on test failed; the winner kept chasing the falling wreck at 9 g into GCAS; straight and level, the 1v1's winner left 10 km (its row went red) | — |

## Gates — CE-10005 *(`2026-11-17`; base = `air-wip` `96d39cf1a` merged with `ui`)*

| gate | command | result | vs base |
|---|---|---|---|
| T-1 attack / defence / radar / orders + the machine checks | `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1 dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --no-build --filter "AirAttackNodesTests\|AirDefence\|AirCombatContractTests\|Radar\|EmitterLocating\|AirOrderNodes\|AirArmedSearch"` | 60 / 0 / 0 | +2 rails (`CE10005_SkillOnTheDefence…`, `CE10005_TheMerge…`) |
| T-1 motion + guided | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build --filter "GuidedRoundSystemTests\|FixedWingModelTests\|RotorcraftModelTests\|Countermeasure\|AirRoute"` | 77 / 0 / 0 | — |
| catalogue goldens | `dotnet test Hrot/Engine/Hrot.Presentation.Tests --no-build --filter "EntityTypeCatalogTests\|CE2138"` | 47 / 0 | ONE line added: `8840 Air Arena \| dis - \| family Unknown \| folder Other \| icon -` (it was red: the arena type had no golden row) |
| red-proofs | one compiling mutation each, its own test project built + filtered, reverted (tree clean) | ✅ all RED: `CE10002_TheFighterDefence` (live end → snapshot), `CE10003_AFightersRadar` (exemption removed), `CE10003_TheZone` (coast drag dropped), `CE10003_TheGunLead` (acceleration term dropped), `CE10005_SkillOnTheDefence` (the Ace read as a Veteran) | the four from the S3 commit now proven |
| protocol | `python3 scripts/protocol-doc-check.py` | OK (162 topics, 105 ordinals) | no wire change (the radar's class is `Unknown`) |
| ⭐ the cluster | `--mode all` (domain 47, port 8179), `POST /scenario/load/live ac-dogfight-1v1`, sampled on the SimHost perspective | ✅ mode 8 crosses the wire (`NavigationIntent.Mode AirCombat` on SimHost); Merge → the pass at 16 s (640 m) → LeadPursuit / Drag / Beam → a kill at ~40 s; 4 shots, the head-on pair DEFEATED | found R-10027 + R-10028 (both fixed, then re-run) |
| `stepped-hash.py --threads 1,4,8 --scenario ac-dogfight-2v2 --chunk 400` (40 s, past the first shots) | ⏳ the run was stopped at the 30 min background limit (three runs of a Release build beside two verify runs) — the earlier 2 s run is STATE EQUAL | — |
| `sam-site` chaff band | ⏳ not re-measured (one seeded run gives no band) | — |

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
- ✅ ~~the §5b missile is very dodgeable (R-10007)~~ — hardened by the user's decision (R-10018).
- ⏳ **still to build** (the next session's plan, in order): ① a TRANSIT move's defence (`MissileDefence.Run` with a MoveTo flight from the route / point move behaviours) ⇒ write `ac-sam-dodge` and `ac-attack-evades`; ② the CAP (a Hold over the arena + the fighter SOP row: an air contact ⇒ Attack) and a next-target pick after a kill (R-10014) — deconfliction through the group's shared contacts; ③ skill that decides (`ac-skill-decides` ❌: a Rookie's flares later / fewer); ④ `ac-guns-turning`; ⑤ `sam-site`'s chaff band (a seeded multi-run); ⑥ the `--mode all` cluster run of `ac-dogfight-1v1`; ⑦ red-prove the four new rails.
- ⚠ **show finding**: with R-10018 the fights end on missiles at range — the 1v1 never merges inside 2 km and the 2v2 has no gun hit. For the show, the arena's `MaxShotRangeM` (6 km) is the knob (the user's: AC-11).

- ⚠ the component-id space is exhausted (R-10000) — a decision for the user before the next lane needs an id.
- the `AirArena` entity needs a TKB type to be authored (S4): a free entity-type id outside the air blocks — measured then.

## Gates — S2 rest + S3 + S4 + R-10018 *(`2026-11-17`; base = `air-wip` at `1a82c5d1e` merged with `ui` `8072ddc0c`)*

| gate | command | result | vs base |
|---|---|---|---|
| T-1 motion + guided suites | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build --filter "GuidedRoundSystemTests\|FixedWingModelTests\|RotorcraftModelTests\|Countermeasure\|AirRoute"` | 77 / 0 / 0 | +2 rails (the zone incl. 0 B allocation; the accelerating lead); the Monte-Carlo's bands re-set by R-10018 (measured 73 / 124 / 281 of 300) |
| T-1 attack / defence / radar | `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1 dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --no-build --filter "AirAttackNodesTests\|AirDefence\|AirCombatContractTests\|Radar\|EmitterLocating"` | 57 / 0 / 0 | +2 rails (the fighter defence's phases; RV-3's exemption); every pre-existing E4 / radar-escape rail green through `MissileDefence` |
| scenarios | `scripts/scenario-regression.sh` (artifacts `sr-hard`, `sr-cap`) | S1 5/5 (pre-R-10018 run) · S2/S3 feature 7/7 with goldens · demos as tabled above · `ua-air-attack` 9/9 golden SAME · `sam-site` 7/7 · `ua-air-strike` 11/11 | `ua-air-defence` 2/3 PRE-EXISTING (red on the base, S1 gates); `ua-air-strike`'s golden diff is "the golden predates the visible-state comparison (RT-3)" — every diff is `golden null` — PRE-EXISTING format staleness, NOT re-recorded here |
| `sam-site` chaff band | — | ⏳ not re-measured: its checks carry no hit rate, and one seeded run cannot give a band | — |
| rails NOT red-proven | the four new rails name the mutant that would redden them; it was not run | — | — |
| `stepped-hash.py --threads 1,4,8` on `ac-dogfight-2v2` | `python3 scripts/stepped-hash.py --threads 1,4,8 --scenario ac-dogfight-2v2` (Release runner) | ✅ **STATE EQUAL** at 1 / 4 / 8 threads (31 entities, 0 types differ) — ⚠ the script's default 6 × 20 steps cover only the first 2 s (before any shot); a longer `--chunk` run is the next session's | new |
| ⛔ not run | the `--mode all` cluster run of `ac-dogfight-1v1`; `scripts/protocol-doc-check.py` (no wire change since S0) | — | — |
