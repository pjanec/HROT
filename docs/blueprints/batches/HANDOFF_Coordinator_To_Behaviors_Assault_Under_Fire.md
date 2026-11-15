<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-15
current-answer: the whole file
related-designs:
  - ../../designs/group-maneuvers/DESIGN_Assault_Under_Fire.md — ⭐ THE design (measured causes §2, the modern doctrine §3, diagrams §4, decisions A-1…A-10 APPROVED R-4254, slices §6, acceptance §7)
  - ../../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — the attack as built (AT-4 / AT-5 / AT-6 this revises; DA-11, DA-12)
  - ../../designs/group-maneuvers/DESIGN_Command_Chain_Battalion_Test.md — `cc-battalion`, whose base variant is the test (AU-6)
  - STATUS_Behaviors_Command_Chain_Overnight.md — last night's report: the evidence behind CE-2436
-->

# HANDOFF — coordinator → `behaviors`: the assault under fire, built and proved — AUTONOMOUS, OVERNIGHT

**For:** the `behaviors` lane (ids `CE-2000`…, `R-2000`…). **From:** coordinator. **Dispatched at `<sha>`**; ⛔ scope FROZEN there — later
documents are FYI; one that invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-15`: *"design CE-2436. You are the military expert here … take into consideration the modern weapons. So stuff like suicide drones
and effective anti-tank missiles. Don't be stuck in the last century"* · *"let's consider drones be unjammable"* · *"the infantry has to shoot at the
drones … Maybe one of them could have a shotgun"* · *"Approved"* (R-4254) · *"yes, write the overnight handoff"*.

## 0. Start

`git fetch origin coordinator behaviors`; on `behaviors`, `git merge origin/coordinator`; restore the test projects you will run; push
`chore: started assault under fire at <sha>`. Open `STATUS_Behaviors_Assault_Under_Fire.md`, update it at every slice's end (a later session or a
compaction resumes from it). Read the design END TO END first, then the sections it cites (R-129).

⭐ **Hygiene first (10 minutes):** last batch's lane decisions R-2097…R-2105 live only in the status file and the design — add each to
`docs/blueprints/RULINGS.md` (your block) with its probe, so `rulings-check.py` covers them.

## 1. How you work — AUTONOMOUS

| | |
|---|---|
| ⭐⭐⭐ **decide, never wait** | every number the design leaves open (pellet count and pattern, the anti-drone ranges, N seconds of established suppression, the relocation distance, the mission's rounds and duration, the screen's offset) is YOURS: measure, decide by the claim table (code + design columns), BUILD, record an `R-2xxx` *"lane decision — for the user's review"* |
| ⭐⭐ **the design's measured causes are the targets** | §2 of the design lists them with file:line — each slice removes some; ⛔ never "tune the scenario until it passes" |
| ⭐⭐ **a defect is fixed where it lives** | in the owning behaviour, with a rail in THAT behaviour's suite (T-1), its design folded (obligation ⑤) |
| ⭐ **a failed premise stops ONE slice** | report it, do the next whose prerequisites hold |
| ⭐ **cross-lane edits allowed and said** | the shotgun type in the TKB catalogue, `DroneJamming`, the ballistics' pellet path — name the lane and the file in the commit |
| ⭐ **Sonnet delegation** | per CLAUDE.md: one warm worker for well-specified steps; you keep design, root-causing and review |
| ⛔ **out of scope** | the FusionSquad, order routing (CE-4612 step ⑥), stochastic units, a mounted approach to a dismount point (A-4's later step) |

## 2. The order — the design's slices

| # | slice | the decisions | done when |
|---|---|---|---|
| 1 | **AU-1** | A-2 real automatic weapons (the generator and the squad templates use the existing AR / MG types where the role says so) · A-1 the squad play `SupportByFire` (sustained `FireAtPoint` at the REPORTED posts, seen or not; automatic weapons first; ammunition-paced) · A-3 the SHIFT (at the assault's PhaseLine report: depth and flanks; LIFT only at the Foothold) replacing AT-6 | design §7 item 1 (`SupportByFire` rails) |
| 2 | **AU-2** | A-7 in contact the approach is fire and movement (rushes between cover), bounding only before contact; smoke grenades at an open crossing (DA-12's open half) · A-8 the assault goes when suppression is ESTABLISHED (rounds landing on the reported posts for N s, the drone strike in) | §7 item 2 |
| 3 | **AU-3** | A-5 a SUSTAINED mission on the reported posts for the approach, a smoke SCREEN on the crossing (between the objective and the assault's route, never on the objective), the shift beyond at the PhaseLine · A-9 the company's base of fire on a COVERED position within weapon range | the mission and screen rails |
| 4 | **AU-4** | A-4 the vehicle play `VehicleSupportByFire`: covered position at stand-off, cannon on the reported posts, RELOCATE after each engagement, never lead across the open; the company hands its vehicles this play | §7 item 1 (the vehicle rail) |
| 5 | **AU-5** | A-6 a recon drone over the objective (its reports are the target list) and bomber / FPV strikes on the reported posts (the drone half of CE-7003); drones UNJAMMABLE (`DroneJamming` no longer downs them) | the drone rails |
| 6 | **AU-7** | A-10 counter-drone: a SHOTGUN type (N pellets through the existing ballistics; high chance inside ~40 m, useless past ~60 m) carried by one man a squad; a "can engage a DRONE" rule beside "can engage aircraft" (shotgun, MG, rifles at short range); the squad's air-threat row (CE-7029): the anti-drone man and the MG engage, the rest take cover, everyone fires inside ~20 m | the counter-drone rails (a drone downed by a shotgun at 30 m; not at 80 m) |
| 7 | **AU-6** | the test: `cc-battalion` base — three runs out of three take Objective 1 with the chain arrows AND "suppression established before the assault moved"; then set and LOCK the dynamism thresholds X / Y from those three clean runs (CE-4615 §5) | design §7 items 2–3; CE-4615's acceptance met for all three variants |

## 3. Gates — every slice

T-1 first (the feature's own suites: `SquadManeuverTests`, the attack / company / SupportCommand / indirect-fire / drone / air-defence suites);
every behaviour rail with `HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` + `HROT_WORLD_GUARD=1`; each new rail red-proved by a compiling mutation;
`stepped-hash.py --threads 1,4,8` on `cc-battalion`; 0 B per steady frame (R-2084); invariant number output (R-4221). Build the affected project in
a fix loop, never the solution; the live checks in the background (CLAUDE.md T3), three runs each.

## 4. The morning report — `STATUS_Behaviors_Assault_Under_Fire.md`

| # | section |
|---|---|
| 1 | ⭐ **the base variant**: each run — Objective 1 taken or not, when suppression was established, when the assault moved, the SITREP colour over time, the chain arrows |
| 2 | ⭐ **decisions taken** (`R-2xxx`), each with what would change it |
| 3 | per slice: built / stopped (the failed premise), the rails, the design folded |
| 4 | the gate-report contract (CLAUDE.md rule 8): one row per gate, `--no-build` column, reds proven pre-existing at a base sha |
| 5 | the dynamism thresholds locked (or why not); ids allocated |
