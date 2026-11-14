<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-14
current-answer: the whole file
related-designs:
  - ../../designs/group-maneuvers/DESIGN_Command_Chain_Battalion_Test.md — ⭐ THE design of the scenario (forces, the chain the checks look for, variants, acceptance)
  - HANDOFF_Coordinator_To_Behaviors_Command_Chain_Prereqs.md — the prerequisites batch in progress: its OPEN items come first here, as written there
  - ../../designs/group-maneuvers/DESIGN_City_Battle_Showcase.md — §6a the company as built, §12 the test ladder
  - ../../designs/group-maneuvers/DESIGN_Command_Chain_Example.md — §8e the battalion's reserve
-->

# HANDOFF — coordinator → `behaviors`: the command chain, developed and proved — AUTONOMOUS, OVERNIGHT

**For:** the `behaviors` lane (ids `CE-2000`…, `R-2000`…). **From:** coordinator. **Dispatched at `<sha>`**; ⛔ scope FROZEN there — later
documents are FYI; one that invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-14`: *"do you have enough to write an autonomous overnight batch for the commander chain development and testing, specified
enough so no questions need to be asked?"* · *"ok let's leave the fusionSquad for later"* (R-4253 — ⛔ nothing of the FusionSquad here).

## 0. Start

`git fetch origin coordinator behaviors`; on `behaviors`, `git merge origin/coordinator`; restore the test projects you will run; push
`chore: started command-chain overnight at <sha>`. Keep `STATUS_Behaviors_Command_Chain_Prereqs.md` for part A and open
`STATUS_Behaviors_Command_Chain_Overnight.md` for parts B–D; update both at every item's end so a later session (or a compaction) resumes from them.

## 1. How you work — AUTONOMOUS

| | |
|---|---|
| ⭐⭐⭐ **decide, never wait** | the user is asleep. A choice a design leaves open (a coordinate, a tunable, the order of two fixes, how to judge a check) is YOURS: decide by the claim table (code + design columns) and the proven solution, BUILD, and record it as an `R-2xxx` ruling marked *"lane decision — for the user's review"* |
| ⭐⭐ **a defect the scenario finds is fixed where it lives** | in the owning behaviour, with a rail in THAT behaviour's suite (T-1), and its design folded (obligation ⑤) — ⛔ never worked around in the scenario or the check |
| ⭐ **a failed premise stops ONE item** | report it, go on with the next whose prerequisites hold |
| ⭐ **cross-lane edits allowed and said** | a fix in backend's or ui's files: name the lane and the file in the commit |
| ⭐ **Sonnet delegation** | per CLAUDE.md: one warm worker for well-specified steps; you keep design, root-causing and review |
| ⭐ **stay in scope** | ⛔ no FusionSquad, no order routing (CE-4612 step ⑥), no stochastic units |

## 2. The order

**A — finish the prerequisites batch's OPEN items, as written there:** CE-4614 (`Strength`, `QuietArea`) → CE-4589 (the battalion's reserve)
→ CE-4610 (the reports nobody sends — per report, produce it where a design names the sender, else drop the reader) → CE-7037 (the stepped gate on
a big scenario) → CE-4605 (the live-men split, all six plays) → CE-4606 → CE-4607 → CE-4608 → CE-7038 → CE-7035's civilian half.

**B — the dynamism measure, CE-7033:** read from a run (live over HTTP during the check, or from its recording): the share of men moved > 50 m in
each 5 min (weighted by `UnitStrength` so a unit counts as its men), contacts per minute, each phase line reached by its deadline; a red row when the
battle goes static.

**C — the scenario, CE-4615** — build [DESIGN_Command_Chain_Battalion_Test.md](../../designs/group-maneuvers/DESIGN_Command_Chain_Battalion_Test.md)
exactly: `scripts/gen-chain-scenarios.py` (the `gen-morale-scenarios.py` precedent) writes `cc-battalion`, `cc-battalion-stall`,
`cc-battalion-collapse` under `scenarios/demos/platoons-and-vehicles/`, each with its `description`; the positions, the deadline and the tunables of
§4 are yours (measure demo-city's blocks, record the choice); `run_cc_battalion` in `scripts/utility-demo-check.py` (the `run_morale_barrage`
precedent) judges §3.2's chain IN ORDER and CAUSED, per variant.

**D — run, triage, fix, repeat:** `python3 scripts/utility-demo-check.py --launch cc-battalion` (and the two variants), three runs each, in the
BACKGROUND. Every red: root-cause it (the chain or the check?), fix it where it lives (§1), re-run. Set the dynamism thresholds X / Y from the first
three clean base runs, then lock them in the check. ⭐ The deliverable is the chain WORKING, not the check passing.

## 3. Gates — every item

T-1 first (the feature's own suites: `SquadManeuverTests`, the company / SupportCommand / objective / SOP suites); every behaviour rail with
`HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` + `HROT_WORLD_GUARD=1`; each new rail red-proved by a compiling mutation; `stepped-hash.py --threads
1,4,8` on `cc-battalion` once CE-7037 lands; 0 B per steady frame for anything per-frame (R-2084); invariant number output (R-4221). Build the
affected project, never the solution, inside a fix loop; the live checks and any full suite in the background (CLAUDE.md T3).

## 4. The morning report — `STATUS_Behaviors_Command_Chain_Overnight.md`

| # | section |
|---|---|
| 1 | ⭐ **the chain**: each §3.2 arrow per variant — seen / not seen, its sim time, the evidence (a report or journal line) |
| 2 | ⭐ **decisions taken** (`R-2xxx`), each with what would change it |
| 3 | defects found and fixed (row, root cause, the rail that pins it, the design folded) · items stopped (the failed premise) |
| 4 | the gate-report contract (CLAUDE.md rule 8): one row per gate, `--no-build` column, reds proven pre-existing at a base sha |
| 5 | the dynamism thresholds chosen and why; ids allocated; designs updated |
