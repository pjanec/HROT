<!--STATUS
state: LIVE
updated: 2026-11-15
current-answer: §0 the base variant · §1 decisions · §2 slices · §3 gates · §4 thresholds and ids
stale-below: nothing
related-designs:
  - HANDOFF_Coordinator_To_Behaviors_Assault_Under_Fire.md — the batch (dispatched at 8d5169c5f)
  - ../../designs/group-maneuvers/DESIGN_Assault_Under_Fire.md — the design (A-1…A-10, slices AU-1…AU-7)
  - STATUS_Behaviors_Command_Chain_Overnight.md — the previous night: the evidence behind CE-2436
-->

# STATUS — `behaviors`, the assault under fire (CE-2436)

Scope frozen at `8d5169c5f`. Started at `a7359633e` (merged `coordinator@a768da341`).

## 0. The base variant (`cc-battalion`)

| run | Objective 1 | suppression established | the assault moved | SITREP colour over time | chain arrows |
|---|---|---|---|---|---|
| AU-1..3 live #2 | ⛔ — the check's wall limit at 612 s sim | the base of fire paused by its SOP's hold (probe: 31 → 47 s) | 593.7 s | — | not judged — the defect fixed in AU-4's commit |
| AU-1 + AU-2 live #1 | ⛔ not taken (assault platoon 2 / 12 left) | support Set 116.6 s | 131.2 s | — | ②–⑦ ✓; ① ⛔ (the withdrawal not seen); PL ⛔; ⑧ ⛔ (Red 13 left) |

## 1. Decisions taken (lane decisions — for the user's review)

| id | decision | would change it |
|---|---|---|
| R-2111 | AU-5: a company subordinate whose roster holds a drone is its DRONE TEAM (role `Drones`, never a platoon role): its recon drone circles the objective on the team's order, its operator flies the armed drones at the posts the base of fire was ordered on; at the consolidation it only watches; the drone strike is NOT a gate on the assault | a live run where the recon drone's reports never reach the company, or the bombers strike our own men |
| R-2110 | AU-7: a round at a target in the air gets an elevation error too and σ × (1 + speed / 10 m/s); the anti-drone shotgun is 12 pellets over 15 mils, spent past 60 m, one a squad (types 212 / 213); a drone may be fired at by a shotgun in its range, an MG or autocannon, an air-defence weapon, and any small arm inside 20 m; the drone gunners (shotgun, MG) stand and engage, the rest take cover | a live run where a drone is never downed (the speed factor too wide) or always (too narrow) |
| R-2109 | AU-4: a company's vehicles take the `Vehicle` role (never assault), stand 35 m beside the base of fire's point and run the exposure cycle with Scoot, one exposure a position; the SOPs spare a base of fire as a defence; drones unjammable by one switch (`DroneJamming.DronesUnjammable`) | a live run where 35 m leaves the IFVs in the open; a base of fire that should break off on contact |
| R-2108 | AU-3: a sustained mission is 8 rounds a gun on the centre of the reported posts (elements within 40 m of the objective); the screen 50 m from the objective toward the start; the shift beyond = a sustained request on the next objective when one is taken; the company's base-of-fire squads 20 m apart | a live run where 8 rounds end before the assault arrives, or the screen misses the assault's route |
| R-2107 | AU-2: suppression ESTABLISHED = firing ≥ 15 s AND ≥ 3 rounds a man spent since firing began (no counted weapon: the time alone); "in contact" = the objective's posts are reported elements; the crossing smoke is one grenade per fire-and-movement run, 15 m toward the threat | a live run where the Set comes too late for the deadline, or one grenade does not cover the crossing |
| R-2106 | AU-1's numbers: `SupportByFire` arrives within 12 m (or fires after 60 s on the way); a reported element within 25 m of the objective is one of its posts (≤ 4; none ⇒ the objective); the SHIFT goes 30 m beyond and 25 m to each flank; man i fires on post i mod n | a live run showing the posts off the windows, or a covered support position the 12 m arrival rejects |

## 2. The slices

| slice | state | rails | design folded |
|---|---|---|---|
| hygiene | ✅ R-2093…R-2105 in `RULINGS.md` with probes (674 / 674 verify) | — | — |
| AU-1 | ✅ `883f9d6df` — A-2 the M249 / PKM in every squad; A-1 `SupportByFire`; A-3 the SHIFT at the assault, the LIFT at the foothold (AT-6 superseded) | `CE2436_SupportByFire_…` (new), `CE2277_AnOpenObjective_…` (shift), `CE2277_AnAreaWithABuilding…` (lift), 3 updated; six mutants red | Assault Under Fire §9.1 · Defend & Attack AT-6 marked superseded |
| AU-2 | ✅ A-7 fire and movement in contact (bounding before), the crossing smoke (BreakContact's rule); A-8 SET = suppression established (time + rounds spent) — ⚠ the drone half of A-8 waits for AU-5 | `CE2436_SupportByFire_SuppressionIsEstablished_…`, `CE2436_FireAndMovement_OneCoveringManThrowsCrossingSmoke…`, `CE2436_PlatoonAttack_BeforeContact…`; CE2277 ×3, CE2346, CE2284 updated; five mutants red | Assault Under Fire §9.2 |
| AU-3 | ✅ A-5 sustained mission (Count → Rounds), the screen on the crossing, the shift beyond; A-1 at the company (`PlatoonSupportByFire`) · ⛔ **A-9 not built** (covered position — next) | `CE2436_CompanyAttack_Sustained…`, `CE2436_ASustainedRequest…`, `CE2436_PlatoonSupportByFire…`; CE7023 ×2, CE7005 updated; six mutants red | Assault Under Fire §9.3 |
| AU-4 | ✅ `VehicleSupportByFire`; the company's `Vehicle` role; IFVs under the company · 🔴 **defect fixed**: the SOPs paused a base of fire (`Supporting` in both `NotDefending` guards) · ⚠ no blind cannon fire on reported points | `CE2436_CompanyAttack_AVehicleSupportsByFire…`, `CE2436_TheSops_DoNotPauseABaseOfFire_OnAContact` | Assault Under Fire §9.4 |
| AU-5 | ⏳ (built, gates below) A-6 the company's DRONE TEAM (`Drones` role → `DroneTeam`: the recon drone circles the objective, the operator flies the bombers at the posts); the unjammable rule (AU-4's commit); a Blue drone team in `cc-battalion` | `CE2436_AU5_…` (two), `CE7014_…` | §9.6 |
| AU-7 | ⏳ (built, gates below) A-10a the SHOTGUN (`Pellets` on the mount: N ordinary rounds over a 2-D pattern, spent past the range; types 212 / 213, one a squad); A-10 fire at the air spread in height and widened by speed (R-2110); A-10b `CanEngageDrone` + the shelter's gate; A-10c the drone gunners engage | `CE2436_AU7_…` ×3 (`AirDefenceNodesTests`), `CE2436_ARifleman_…` (`ShelterFromAirNodesTests`), `CE2436_AShotgun_…` (`FireProcessingSystemTests`), `CE2436_APellet_…` (`BallisticsSystemTests`), `CE2436_AU7_UnderADrone_…` | §9.5 |
| AU-6 | | | |

## 3. Gates

| command | `--no-build` | result |
|---|---|---|
| AU-1: `dotnet test Hrot.SimHost.Tests --filter SquadManeuverTests\|SuppressNodesTests\|ScenarioRulesTests` plain · `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` | ✓ | 406 / 406 · 406 / 406 |
| AU-1: `--filter SopParamsRoundTrip\|ShippedTreesAreCSharp\|BehaviourHostingSnapshot` | ✓ | 35 / 36 → 36 / 36 after re-recording the hosting golden (`HROT_RECORD_HOSTING=1`): ⚠ it had drifted since `06ee9318b` (2026-11-08, other behaviours' sizes) — this slice's lines are `PlatoonAttack` 208 → 272 and `SupportByFire` 104 |
| AU-2: the same suites plain · PAR + READ_CHECK + WORLD_GUARD | ✓ | 409 / 409 · 409 / 409 |
| AU-2: registry suites | ✓ | 35 / 36 → 36 / 36 re-recorded; moved lines exactly `FireAndMovement` 1424 → 1440, `ReactToContact` 1624 → 1640 (embeds it), `SupportByFire` 104 → 120 |
| AU-2 red-proofs | — | rounds check dropped · time check dropped · contact forced · rush dropped · crossing smoke dropped ⇒ each RED; restored 29 / 29 |
| AU-3: the suites plain · PAR + READ_CHECK + WORLD_GUARD | ✓ | 412 / 412 · 412 / 412 |
| AU-3: registry suites | ✓ | 36 / 36 after re-recording: moved lines `CompanyAttack` 144 → 192, new `PlatoonSupportByFire` 120 |
| AU-3 red-proofs | — | rounds not passed · sustained count dropped · screen on the objective · shift beyond dropped · platoon Set on the first squad · no spread ⇒ each RED; restored 39 / 39 |
| AU-4: `--filter SquadManeuverTests\|SuppressNodesTests\|ScenarioRulesTests\|AirAttackNodesTests\|VehicleFightFromCover\|BasicInfantrySop` plain · PAR + READ_CHECK + WORLD_GUARD | ✓ | 497 / 497 · 493 / 497 — the four reds are the `BasicInfantrySopTests` CE3306 / CE4035 / CE2332 / CE1269, recorded PRE-EXISTING under PAR in `STATUS_Behaviors_Command_Chain_Overnight.md` §3 (row "after CE-4610") |
| AU-4: registry suites | ✓ | 36 / 36 after re-recording: one new line, `VehicleSupportByFire` 328 |
| AU-4 red-proofs | — | platoon guard dropped · squad guard dropped · vehicle check dropped · `AllSet` waits on the vehicle · drones jammable again ⇒ each RED; restored 43 / 43 |
| AU-1 red-proofs (six compiling mutations, `scratchpad/mutate.py`) | — | arrival dropped · man i on post 0 · SET dropped · shift dropped · lift dropped · AT-6's lift back ⇒ each RED; restored 16 / 16 |

## 4. Dynamism thresholds and ids

Not locked yet. Ids allocated: none yet.
