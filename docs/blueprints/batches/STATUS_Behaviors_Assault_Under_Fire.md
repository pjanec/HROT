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
| R-2119 live #18 (`0b261b9cd`) | ✅ **TAKEN** — Blue held Objective 1 (≥ 4 men, no enemy, 30 s) from 875 s; PL 1 BY a report at 606 s; Red's three platoons wiped out; ended by "Red below 30 % with Objective 1 held" | ✅ Set 113.1 s | ✅ 122.9 s (after the Set) | — | ⭐ **every required arrow ✓** (① SPOT 4.1 → withdraws 23.4 s · ② · ③ 16 bursts, 2 m · ⑤ · ⑥ · PL · ⑧ · AU-6); only the authored PL deadlines (420 / 720 s) ⛔; moved > 50 m 61 / 27 % · 109.1 contacts/min |
| R-2119 live #19 | 🟡 **Objective 1 HELD from 608 s** — but the platoon never sent its PhaseLine report; Red stayed above 30 % (5 + 8 left) ⇒ ended at 15 min | ✅ Set 113.5 s | ✅ 124.7 s | — | ①②③⑤⑥⑧ + AU-6 ✓; PL by report ⛔; the PL deadlines ⛔; moved > 50 m 61 / 30 / 18 % · 113.2 contacts/min |
| R-2118 live #16 | 🟡 Red broken (< 30 %) at **260 s** with Blue 8 / 12 / 12 — the 81 mm on the office roof (16 bursts, 1 m from the request) broke the defence; the END (Red < 30 %) stopped the clock before the assault reached the objective ⇒ not seen TAKEN ⇒ **R-2119** | ✅ Set 115.3 s | ✅ 123.3 s | — | ①②③⑤⑥⑧ ✓; PL ⛔ (the run ended first); ⚠ the roof bursts' lethality to verify |
| R-2118 live #17 | 🟡 the same — Red broken at **317 s**, Blue 11 / 12 / 12 | ✅ Set 113.1 s | ✅ 123.5 s | — | ①②③⑤⑥⑧ ✓; PL ⛔ (the run ended first) |
| R-2112…R-2117 live #13 | ⛔ not taken — foothold + 1 room, the assault platoon down to 1 man; **the mortars never fired** ("no round left the tubes in 30 s — the point is masked": the request was the centre of the reported posts, INSIDE the office) ⇒ **R-2118** | ✅ Set 113.4 s | ✅ 122.3 s | — | ②③⑤⑥ ✓ (③ only by an 85 m burst — the check's tolerance); ① ⛔ (the outpost dead at the first read); PL ⛔; ⑧ ⛔; moved > 50 m 62 / 35 / 18 % · 122.6 contacts/min |
| R-2112…R-2117 live #14 | 🟡 PL 1 BY a report at 599 s — but 2 rooms and no `BuildingClear` ⇒ not counted as TAKEN; Red < 30 % at 815 s | ✅ Set 113.5 s | ✅ 118.1 s | — | **①②③⑤⑥ + PL + ⑧ all ✓** (16 mortar bursts); moved > 50 m 53 / 36 % · 118.5 contacts/min |
| R-2112…R-2117 live #15 | ⛔ not taken — foothold + 1 room, the assault platoon down to 2; **the mortars never fired** (the same masked refusal, the request inside the office) | ✅ Set 113.6 s | ✅ 121.3 s | — | ②③⑤⑥ ✓; ① ⛔; PL ⛔; ⑧ ⛔; moved > 50 m 62 / 32 / 23 % · 99.1 contacts/min |
| R-2114 + R-2115 live #11 (`747806e29`) | 🟡 **PL 1 reached BY a report (752 s)** — but by AT-7's "blocked building ⇒ the next objective": S1's failed approach was re-ordered (R-2114, live at 422 s), S2 closed by fire and movement and began `ClearBuilding` at 689 s with 2 men, reported `RoomBlocked` (survey timeout / no building / the drill could not begin — not logged), and the platoon went on toward Objective 2; no `BuildingClear` ⇒ ⛔ not counted as TAKEN | ✅ Set 113.4 s | ✅ 115.2 s | — | ①②③④⑤⑦ ✓ + PL by report ✓; ⑥ ⛔ **a CHECK artefact** (reserve stamped 134.0 s at the poll's start vs request 134.8 s — fixed: a task change is stamped when noticed); ⑧ ⛔; moved > 50 m: 53 / 28 / 24 % · 117.6 contacts/min |
| R-2114 live #10 (`9ed1d9ffd`) | ⛔ not taken — **further than #8/#9**: the approach went through (bounding), S1 `ClearBuilding` at 464 s (`Foothold`, 1 `RoomClear`), S2 took over at ~508 s; then BOTH assault squads sat in `ReactToContact` **inside the building for 5–6 min** (their own SOP's reaction replacing the platoon's order: the platoon below `IsInTheFight`'s line ⇒ `UnderCommand` false) ⇒ **R-2115**; AT-10 broke off at 867 s; ended by time | ✅ Set 113.3 s | ✅ 113.6 s | — | ①②③⑤⑥⑦ ✓ (① SPOT 4.0 → withdraws 19.0 s); ⑧ ⛔; PL ⛔; moved > 50 m: 55 / 28 / 15 % · 118.0 contacts/min |
| R-2113 live #9 (`ab600a2f7`) | ⛔ not taken — Red broken (< 30 % at 889.7 s) but each assault squad in turn (1st Platoon S1 → S2 by R-2112 → S3) stopped 47 / 34 / 8 m short **with no task**: its fire and movement FAILED (a half gone) while the squad still stood, its order "stood" and the approach waits for a Set ⇒ **R-2114**. ⚠ The check then sat 30 min on the stopped clock (fixed `c81b6f366`) and its end-of-run reads are void | ✅ Set 114.7 s | ✅ 115.3 s | — | ①②③⑤⑥⑦⑧ ✓ (① SPOT 4.0 → withdraws 19.0 s); PL ⛔; dynamism not measured (void end read) |
| R-2113 live #8 (`d13bc36b2`) | ⛔ not taken — the assault platoon was shot down to **1 man** (Blue 1 / 5 / 5 left, Red's line 1 / 0 / 3 + the battalion's 8); the Blue reserve attacked at 348 s and the attack stalled (moved > 50 m 12 % then 4 %); no `ClearBuilding`, ended by time ⇒ #7's success is not yet repeatable — #9 runs with the squad probe | ✅ Set 113.4 s | ✅ 118.6 s | — | ✅ **① PASS** (SPOT 4.0 s → withdraws 20.7 s; the outpost lived to 368 s) ②③④⑤⑥⑦ ✓; ⑧ ⛔; PL ⛔; moved > 50 m: 57 / 12 / 4 % · 114.0 contacts/min |
| R-2112 live #7 (`d5a7ba0ff`) | ✅ **TAKEN** — the assault entered at ~490 s, `ClearBuilding` ran ~490–700 s (5 `RoomClear`, `Foothold`, `BuildingClear`, 2 `ObjectiveSecure`), then all three squads `SquadDefend` (consolidation); **PL 1 by report 710.6 s, PL 2 (company) 782 s**; ended by Red < 30 % at 797 s ⇒ run #6's clearing was SLOW, not stuck | ✅ Set 113.7 s | ✅ 131.5 s | — | ②③④⑤⑥⑦⑧ ✓; ① ⛔ (the outpost died at 22.2 s — the same in #5/#6/#7, before the attack starts ⇒ **R-2113**); the PL deadlines 420 / 720 s ⛔ (reached later); moved > 50 m: 47 / 38 % · 113.6 contacts/min |
| R-2112 live #6 (`b2d0acc59`) | ⛔ not taken — **no abort**: the assault closed by fire and movement (0.55 m/s over 200 m), entered the office at 560 s and was STILL `ClearBuilding` at 900 s (men 7–13 m from the centre, Red's line there 0 men); never Set ⇒ no consolidation | ✅ Set 115.8 s | ✅ 136.5 s | — | ②③④⑤⑥⑦ ✓; ① ⛔ (the outpost died at 22 s, before withdrawing); PL 1 ⛔; ⑧ ⛔; moved > 50 m: 55 / 28 / 16 % · 109.7 contacts/min |
| AU-1..5, AU-7 live #5 (`f28d19813`) | ⛔ not taken — **PL 1 reached (first time)**; the assault squad reached 45 m and began `ClearBuilding`, went Black, the reserve was out on a casevac ⇒ AT-10 aborted beside an intact base of fire (probe) ⇒ **R-2112** | ✅ Set 117.7 s | ✅ 119.5 s | — | ②③④⑤⑥⑦ ✓; ① ⛔ (the outpost sheltered from Blue's PARKED drones at t = 0 ⇒ `FlightGround`); ⑧ ⛔; moved > 50 m: 61 / 43 / 11 % · 107.8 contacts/min; drone team live: recon orbit ✓, both bombers flown |
| (live #4) | — run killed by the job's 60-min wall limit before the verdict (78 men + drones run slower) | | | | |
| AU-1..4 live #3 (`e9738509f`) | ⛔ not taken — Red's line nearly destroyed (Red platoons 1 / 0 / 2 men left, Blue 5 / 5 / 6) yet PL 1 failed at 420 s; 439 `TaskFailed` reports | ✅ Set 113.2 s (established) | ✅ 130.2 s — after the Set | — | ②③④⑤⑥⑦ ✓; ① ⛔ (outpost: no withdrawal seen); PL ⛔; ⑧ ⛔; moved > 50 m: 51 / 35 / 11 % · 99.6 contacts/min |
| AU-1..3 live #2 | ⛔ — the check's wall limit at 612 s sim | the base of fire paused by its SOP's hold (probe: 31 → 47 s) | 593.7 s | — | not judged — the defect fixed in AU-4's commit |
| AU-1 + AU-2 live #1 | ⛔ not taken (assault platoon 2 / 12 left) | support Set 116.6 s | 131.2 s | — | ②–⑦ ✓; ① ⛔ (the withdrawal not seen); PL ⛔; ⑧ ⛔ (Red 13 left) |

### 0a. The editor host — deterministic replay *(measured `2026-11-15`; user: "the editor is way lighter", "it has 'run to'")*

`utility-demo-check.py --launch --host editor` (`5664df62a`, `cd0702e16`): an EDIT load, the preview entered PAUSED (zero steps, CE-4570),
advanced by run-to's FIXED steps in 5 s chunks. ✅ **Two identical runs of `cc-battalion` are IDENTICAL** over the whole 345 s — every task change,
every arrow time to the microsecond (Set 112.50000335276127 s, request 132.16667060554028 s), the report counts (1352), the survivors; world hash
`18485A372C15ED3A` at tick 8471 (run 2). ⚠ **Not a stand-in for the cluster on this scenario:** at step 1/6 (the only step faster than the cluster:
0.6× real time; 1/60 ≈ 0.18×) Red fell below 30 % at **165 s** (the cluster: ~800 s or never) — the fire is far deadlier. ⇒ the editor is for
REPLAYING a failure; ⭐ behaviour iteration moves to FOCUSED scenarios (one platoon against one building, ~24 men vs 75); `cc-battalion` stays the
final integration check.

### 0b. The focused loop — `pc-assault` *(user, `2026-11-15`: "iterations taking more than 1 hour seem too long")*

`utility-demo-check.py --launch --host editor --step 1/60 pc-assault` — `pc-defend-house`'s cast (Blue Platoon 3 × 4 vs Red Platoon's 12 in
three houses, 24 men) run to its OUTCOME: **600 s of battle in ~11 min**. Three runs, all ⛔ (no `BuildingClear`): the base of fire up, the
approach, the clearing squad entering and clearing rooms one every ~40–60 s, then **stuck in Entering for good** — runs A (310 → 600 s) and
the first (275 → 600 s). Root cause: CB-55's `HostileInRoom` counted a defender LAST SEEN in the room with no freshness ⇒ **R-2116**.
⚠ The editor run does NOT repeat on this scenario (CE-2438) — each run is a sample, the check logs the drill per chunk.

| `pc-assault` run | with | outcome | why (the drill dump) |
|---|---|---|---|
| R-2116 #1 | R-2116 | ⛔ 2 rooms, then Entering 345 → 600 s | — (before the stuck dump) |
| R-2116 #2 | R-2116 | ⛔ Entering the FIRST room 240 → 600 s, all 4 at the threshold | — |
| R-2116 #3 | R-2116 | ⛔ still clearing at 600 s (3 rooms) | slow, not stuck |
| R-2116 #4 | R-2116 + dump | ⛔ Entering 345 → 600 s | the leader 2.1 m and a rifleman 3.0 m short of their points; the only in-room contact DEAD ⇒ **R-2117** |
| R-2116 #5 | R-2116 + dump | ✅ **PASS — `BuildingClear`**, the first | stuck 30 s once (the leader 5.6 m off a point upstairs), then through |
| R-2117 #1 | R-2116 + R-2117 | ⛔ AT-10 break-off at 452 s — Blue 2 / 0 / 1 left, Red 6 | a lost fight, not a stall |
| R-2117 #2 | R-2116 + R-2117 | ⛔ AT-10 break-off — every Blue man down | a lost fight, not a stall |
| R-2117 #3 | R-2116 + R-2117 | ✅ **`BuildingClear`** | one 30 s stuck Entering (men 5 m off points upstairs) — through on the bound |
| R-2117 #4 | R-2116 + R-2117 | ✅ **`BuildingClear` × 2** (OBJ, then the next building) | — |

⭐ **No stall left on the focused loop:** with R-2114…R-2117 every run ends in a decision — the building cleared (2 / 4) or the attack broken off on
losses (2 / 4). ⚠ 12 attackers against 12 defenders in prepared houses is below the doctrinal 3 : 1 — losing half is a fight, not a defect.

## 1. Decisions taken (lane decisions — for the user's review)

| id | decision | would change it |
|---|---|---|
| R-2119 | AU-6 (the scenario's end): Red broken ends the exercise only once Blue HOLDS Objective 1 (≥ 4 men in the 35 m block, no enemy, 30 s); Objective 2 held or 15 min end it as before; the check keys on the scenario's own "The end" and judges AU-6 by that hold | a run where Red survivors scattered in the block keep the end away for good (then: maxEnemy 1, or the block radius) |
| R-2118 | AU-6 (Indirect Fire IF-1, cross-lane): an indirect round aimed at a point under a roof is aimed at the ROOF (the top surface over the point, `FireSolution.OnTop`) — it bursts there; the arc is no longer refused as masked by the target's own building. Points in the open are unchanged | a live run where roof bursts do no harm to the defenders inside (then: the building's damage / the burst through the roof) |
| R-2117 | AU-6 (Building Clearing CB-55): Entering gets a bound — 20 s after every entry man is released, a man short of his point covers from where he stands; the room then clears on the 3 s quiet as before | a live run where the bound clears a room with a defender hidden where the short man would have looked |
| R-2116 | AU-6 (Building Clearing CB-55 as designed): a hostile in the squad's pool holds a room uncleared only while FRESH (seen within 3 s); the entry men at their points not seeing him for 3 s refute him — the as-built "no freshness filter, the pool forgets" kept men in an empty room for 290 s | a live run where a hidden defender (behind cover in the room) survives the 3 s look and shoots the next man in |
| R-2115 | AU-6: a squad whose commander is a UNIT still counts as under command while it runs that commander's order, even when the unit is below `IsInTheFight`'s line (AQ97 A: "a live commander above him" — his own master once his unit is wiped out); no order of its ⇒ his own rows as before | a live run where a shattered platoon's squads wait on orders that never come (then: the commander's running play as the test, read from the tick-start view) |
| R-2114 | AU-6: an assault whose APPROACH failed (`TaskFailed` — fire and movement lost a half) while ≥ 2 of its men stand is ordered forward again (the new order re-splits the survivors); with fewer it is lost and R-2112's relief takes over | a live run where the re-ordered squad fails again at once in a loop (then: a cooldown, or Bound instead of Rush) |
| R-2113 | AU-6 (scenario rule, extends R-2101): the outpost ALSO withdraws on its FIRST CASUALTY (a `Strength` objective on the outpost, threshold 0.99) — Blue's IFVs on the start line kill it at ~22 s, before the attack (t = 30 s) and before Blue closes (~142 s), measured in runs #5/#6/#7, so the close rule never fired | a run where the first casualty comes before the outpost's SPOT (the withdrawal then precedes the report), or the OP play (CE-2435) is built |
| R-2112 | AU-6: a lost assault with no free reserve is relieved by the platoon's BASE OF FIRE when it is not Black (the spent assault holds as the reserve); AT-10 breaks off only when no squad is fit | a live run where the base of fire's move forward leaves the assault without covering fire and both are lost |
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
| AU-5 | ✅ A-6 the company's DRONE TEAM (`Drones` role → `DroneTeam`: the recon drone circles the objective, the operator flies the bombers at the posts); the unjammable rule (AU-4's commit); a Blue drone team in `cc-battalion` | `CE2436_AU5_…` (two), `CE7014_…` | §9.6 |
| AU-7 | ✅ A-10a the SHOTGUN (`Pellets` on the mount: N ordinary rounds over a 2-D pattern, spent past the range; types 212 / 213, one a squad); A-10 fire at the air spread in height and widened by speed (R-2110); A-10b `CanEngageDrone` + the shelter's gate; A-10c the drone gunners engage | `CE2436_AU7_…` ×3 (`AirDefenceNodesTests`), `CE2436_ARifleman_…` (`ShelterFromAirNodesTests`), `CE2436_AShotgun_…` (`FireProcessingSystemTests`), `CE2436_APellet_…` (`BallisticsSystemTests`), `CE2436_AU7_UnderADrone_…` | §9.5 |
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
| AU-7 + AU-5: `dotnet test Fdp.Toolkits.Tests --filter Combat\|Utility` | ✓ | 721 / 721 |
| AU-7 + AU-5: SimHost `--filter SquadManeuverTests\|SuppressNodes\|ScenarioRules\|AirAttackNodes\|VehicleFightFromCover\|BasicInfantrySop\|AirDefenceNodes\|ShelterFromAirNodes\|BasicVehicleSop` plain · PAR + READ_CHECK + WORLD_GUARD | ✓ | 517 / 517 · 513 / 517 — the same four pre-existing `BasicInfantrySopTests` reds as AU-4 |
| AU-7 + AU-5: registry suites | ✓ | one moved line after re-recording: new `DroneTeam` 28 |
| AU-7: `Hrot.Core.Tests` (the catalogue, types 212 / 213) | built | 370 / 371 — ⚠ the red `HrotEnvironmentTests.CreateGeoTransform_HasNoDefaultOrigin_…` is geo-frame code this batch does not touch (`LocalOnlyFrame.ToGeodetic` throws by design since CE-4396, 2026-11-10); NOT proven at a base sha |
| AU-7 + AU-5 red-proofs (`scratchpad/mutate6.py`, `mutate7.py`) | — | pellets not spawned · pattern dropped · range limit not set · range expiry dropped · rifle's 20 m dropped · shotgun range ignored · MG not counted · pellets not a drone gunner · air 2-D estimate dropped · pellet range estimate dropped · drone-gunner choice dropped · strike kept at the consolidation · operator ordered with no strike ⇒ each RED (13); then (non-folding conditions — a literal `if (false)` or `world == null` does not compile here) air elevation dropped · the shelter's gate dropped · the team not recognised · the recon drone sent to strike · the operator not ordered ⇒ each RED: **18 / 18**; restored 31 / 31 + 56 / 56 |
| R-2118: `FireSolutionTests.CE2436_APointInsideARoofedHouse_…` · FDP `Combat` · SimHost indirect fire (`IndirectFire\|SupportCommand\|Artillery\|FireMission\|Mortar`) plain + PAR | ✓ | 548 / 548 · 6 / 6 · 6 / 6 |
| R-2118 red-proof (`mutate14.py`) | — | `OnTop` never applied ⇒ 1 / 1 RED; restored, the tree clean |
| R-2117: `CE2257_BC1_ASmallRoom_…` (a man 3 m short holds the room within the 3 s, not after the 20 s bound) · SimHost `SquadManeuverTests` + `Building` + `Room` plain · the same PAR + checks | ✓ | 298 / 298 · 298 / 298 |
| R-2117 red-proof (`mutate13.py`) | — | the bound never applies ⇒ 1 / 1 RED; restored, the tree clean |
| R-2116: `CE2257_BC1_ClearRoom_…` (a fresh hostile holds the room, a stale one does not) · SimHost `SquadManeuverTests` + `Building` + `Room` + `Clear` plain · the same PAR + checks | ✓ | 338 / 338 · 337 / 338 (`HillAttackIntegrationTests.CE485…` — an undeclared `NavigationStatus` read, **CE-2439**, not this batch's files) |
| R-2116 red-proof (`mutate12.py`) | — | the freshness skip never applies ⇒ 1 / 1 RED; restored, the tree clean |
| R-2115: the rail `CE2436_AUnitBelowItsLine…` · SimHost `BasicInfantrySopTests` + `SquadManeuverTests` + `GrenadeReflexTests` plain · the same PAR + checks · FDP `Behavior\|Squad` | ✓ | 298 / 298 · 291 / 298 (the four pre-existing `BasicInfantrySopTests` + three `GrenadeReflexTests` — a round born this tick, **CE-2437**, not in this batch's files, not proven at a base sha) · 813 / 814 (1 skipped) |
| R-2115 red-proof (`mutate11.py`) | — | `RunsHisOrder` never true ⇒ 1 / 1 RED; restored, the tree clean |
| R-2114: the rail `CE2436_AFailedApproach…` (4 and 2 standing) · SimHost whole suite plain · `SquadManeuverTests` + `BasicInfantrySopTests` PAR + read check + world guard | ✓ | 2 / 2 · 2812 / 2822 (6 skipped; 4 red outside this lane's files, NOT proven at a base sha: `OneBootstrapImplementationRails.NoHost_ReferencesAnotherHost…`, `StrictNetworkSeparationTests.CE4208…`, `UtmEverywhereTests.TheEightCommittedTerrains…` (zone 32 ≠ 33 — backend CE-4396), `LoadPhaseChainTests.CE3345…` (green alone: order-dependent)) · 289 / 293 (the four pre-existing `BasicInfantrySopTests`) |
| R-2114 red-proof (`mutate10.py`) | — | the re-order guard never dirties ⇒ 2 / 2 RED; restored, the tree clean |
| R-2112 + `FlightGround`: FDP `--filter Motion\|Perception\|Combat` · SimHost touched suites plain · PAR + checks | ✓ | 807 / 807 · 518 / 518 · 514 / 518 (the four pre-existing `BasicInfantrySopTests`) |
| R-2112 + `FlightGround` red-proofs (`mutate8.py`, `mutate9.py`) | — | the base of fire not considered · the reserve judged on colour alone (the rail-found defect, `SlotLost`) · the ground test dropped from `IsAir` ⇒ each RED; restored 40 / 40 + 57 / 57 |
| AU-1 red-proofs (six compiling mutations, `scratchpad/mutate.py`) | — | arrival dropped · man i on post 0 · SET dropped · shift dropped · lift dropped · AT-6's lift back ⇒ each RED; restored 16 / 16 |

## 4. Dynamism thresholds and ids

Not locked yet. Ids allocated: none yet.
