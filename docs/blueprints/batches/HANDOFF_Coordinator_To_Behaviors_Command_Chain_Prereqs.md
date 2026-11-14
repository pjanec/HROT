<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-14
current-answer: the whole file
related-designs:
  - ../../designs/aggregate-units/DESIGN_Aggregate_Squad.md — ⭐ §6 the real-squad defects, §17 the review and its rulings (R-4251) — the design basis of most items
  - ../../designs/group-maneuvers/DESIGN_Command_Chain_Example.md — §8e the battalion's reserve (CE-4589)
  - ../../designs/group-maneuvers/DESIGN_City_Battle_Showcase.md — §11 the showcase rows, §12 the test ladder these prerequisites serve
  - ../../DESIGN_Scenario_Rules.md — §10.2 the objective pattern CE-4614 follows
-->

# HANDOFF — coordinator → `behaviors`: the command-chain showcase's prerequisites

**For:** the `behaviors` lane (ids `CE-2000`…, `R-2000`…). **From:** coordinator. **Dispatched at `e891fda6a`**; ⛔ scope FROZEN there — later
documents are FYI; one that invalidates an item ⇒ STOP that item and report it, do the rest (R-106). ⭐ The showcase-brains lane is retired
(user: only behaviors, backend, ui, coordinator and terrain remain) — its open rows taken here are cross-lane, said.

🔒 User, `2026-11-14`: *"before we continue i need to quickly give some work to behav lane - whatever prerequisites we need for the showcase,
already known tasks, what is suitable?"*

**Why these:** the showcase must prove the command chain (battalion → company → platoon → squad). Every item below is either a defect in the
squads that chain rests on, a gate that must work before anything is proven, or a piece the chain's test scenario needs — and each is approved
or designed already. ⛔ NOT here: the FusionSquad itself (A-2…A-5) and the order routing step ⑥ — they come in a later batch.

## 0. Start

`git fetch origin coordinator behaviors`; on `behaviors`, `git merge origin/coordinator`; restore the test projects you will run; push
`chore: started command-chain prerequisites at <sha>`. Read the design sections each item cites before touching it (R-129).

## 1. The items — in this order *(re-ordered under rule 1a, unstarted — user: "i need something that is not a bug fix, can i start from point 8 right away?": the FEATURES first)*

| # | row | what | design basis |
|---|---|---|---|
| 1 | **CE-4612** | orders as TYPED state: inventory → `CurrentOrder` → order ids → readers migrated one by one → `OrderKind` extended (steps ①–⑤; ⛔ not the routing). Closes **CE-4609** by construction | §17.2 R-D11 A′ |
| 2 | **CE-4613** | `UnitStrength` (Present, Authorized) for real units at every echelon; every reader through it; IDENTICAL results for existing squads | §17.2 R-D4 |
| 3 | **CE-4614** | the objective kinds `Strength` and `QuietArea` | tracker row; Scenario Rules §10.2 |
| 4 | **CE-4589** | the battalion's reserve: `SupportCommand.Reserve`, the order by echelon (`CompanyDefend` for a company) | Command Chain §8e |
| 5 | **CE-4610** | the reports commanders read but nobody sends (FallingBack, PhaseLine, ObjectiveSecure, FireLifted, LACE, ammo colour): per report, PRODUCE it where the design names a sender, or DROP the reader with the reason; and reconcile Building Clearing CB-3 vs CB-51 | §6; `DESIGN_Platoon_Attack_Defend_Clear.md:163, :199, :203` |
| 6 | **CE-7035** | the drill fixture registers the brain phase's declared reads, so `HROT_READ_CHECK=1` runs on the drill suite (83 false reds today); the civilian fixture too | `STATUS_Showcase_Brains.md` §2–§3 |
| 7 | **CE-7037** | the stepped gate on `city-battle` runs a different number of steps per run — find what advances the clock outside `/sim/step`; restore or replace the gate's missing default scenario | tracker row |
| 8 | **CE-4605** | ⭐ the six drills split the squad by roster SLOT, dead men included ⇒ suppress-and-manoeuvre reports Success with nobody having assaulted: split the LIVE men by rank in ONE shared helper for all six (`ComputePartitionInputs` callers) | Aggregate Squad §6, §17 V-11 / R-D12 |
| 9 | **CE-4606** | a two-man squad with one down livelocks in casualty care (`MedicFor` skips the leader) — fall back to the leader | §6 |
| 10 | **CE-4607** | a convoy never finishes when a squad's only man is down — fix the ARRIVAL test only (`LiveMen` counting the down man is deliberate, for seats) | §6, §17 (refined) |
| 11 | **CE-4608** | a relieved assault squad keeps clearing — give it `SquadDefend` at the foothold (Building Clearing §6a) | §6 |
| 12 | **CE-7038** | a battery strike that can never fire runs silently — refuse it with the reason | tracker row |

⭐ A decision a design leaves open is yours: decide by the claim table (code + design columns), record it as an `R-2xxx` ruling marked
*"lane decision — for the user's review"*, and fold the as-built into the owning design (obligation ⑤).

## 2. Gates

⚠ Until CE-7035 (now item 6) lands, `HROT_READ_CHECK=1` runs on the NEW rails only, not the whole drill suite (its fixture gives 83 false reds); until CE-7037 (item 7) lands, the stepped hash runs on the small scenarios only. T-1 first (the feature's own suites: `SquadManeuverTests`, the SOP suites, `CompanyBehaviors` rails, `SupportCommand` rails, the objective suites);
every behaviour rail with `HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` + `HROT_WORLD_GUARD=1`; each new rail red-proved by a
compiling mutation; `stepped-hash.py --threads 1,4,8` on the scenarios the changes touch ; 0 B per steady frame for `UnitStrength`
(R-2084). Report per the gate-report contract (CLAUDE.md rule 8): one row per gate, `--no-build` column, reds proven pre-existing at a base sha,
ids allocated, designs updated.
