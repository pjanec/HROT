<!--STATUS
state: LIVE — dispatched, not started — EXPERIMENTAL, UNATTENDED
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md — ⭐ THE design: §23 YOUR ids, the contract and the slices per lane; §10–§19 the decisions (the §21 / §17.1 rulings override the rows they name), §22 the UML
  - ../../designs/aggregate-units/DESIGN_Aggregate_Squad.md — the unit state, order routing and child keys the FusionSquad put there
  - ../../designs/aggregate-units/MEASURED_Aggregate_Squad_Seams.md — W-1…W-11, the checklist; the calibration numbers
-->

# HANDOFF — coordinator → `exp/agg-behaviors`: the stochastic unit — the BEHAVIOURS and the chain (CE-4616)

**For:** a NEW experimental lane on branch **`exp/agg-behaviors`** (create it from `origin/coordinator`). **From:** coordinator. **Dispatched at `3f33c0ee3`**;
⛔ scope FROZEN there — later documents are FYI; one that invalidates a slice ⇒ STOP that slice, do the rest (R-106).

🔒 User, `2026-11-16`: *"i would like to work in parallel on the aggregated entity stuff, experimental branch, different behavior/backend/ui
lane"* · *"approved, write the handoffs for the three exp lanes"* — R-4276; the design's decisions R-4248…R-4257.

## ⭐⭐⭐ UNATTENDED — read this before anything else
| the rule | |
|---|---|
| ⭐⭐⭐ **NOBODY IS THERE TO ANSWER** — never ask, never wait, never end a turn on a question | every open choice: measure it, decide by the claim table (code + design), build it, record an `R-` lane decision *"for the user's review"* (your block) with what would change it |
| ⭐⭐⭐ **DO NOT STOP UNTIL EVERY SLICE YOU CAN BUILD IS BUILT, GATED AND REPORTED** | a slice finishing is not the end — go straight to the next |
| ⭐⭐ **a failed premise stops ONE slice, never the batch** (R-106) | write why in the status file, do every slice that does not depend on it |
| ⭐⭐ **the other two agg lanes may not have landed** — build against STAND-INS with the §23.2 names and ids, re-bind when their work merges; `git fetch` them at each slice's end and merge them when they carry what you need | ⛔ never build another lane's half |
| ⭐⭐⭐ **EXPERIMENTAL: you merge `coordinator` INTO your branch (at start and before each slice), ⛔ NEVER the reverse**, and you never push to `coordinator`, `backend`, `behaviors`, `ui` or `terrain` | the user decides when the experiment comes in |
| ⭐⭐ **the animals and minefield lanes are running beside you** — their files change under you; a cross-lane edit is allowed and SAID (commit + status), kept minimal | ⛔ do not touch `DoorStates`, the path request's side, the minefield or animal components |
| ⭐ **commit, push and update the status file at every slice's end** | a restart or a compaction resumes from it |
| ⛔ **you may NOT** reverse a user ruling, change an id or a name fixed in §23, or take an id outside your block (§23.1) | file it as a finding instead |

## 0. Start
`git fetch origin coordinator exp/agg-backend exp/agg-behaviors exp/agg-ui`; create `exp/agg-behaviors` from `origin/coordinator` (or check it out if
it exists); restore the test projects you run; push `chore: started exp/agg-behaviors at <sha>`. Status: `STATUS_AggBehaviors.md`. Read RULINGS.md, then the design END TO
END (R-129) — §23 first, then §10–§22.

## 1. Bind
⭐ The ENGINE is `exp/agg-backend`'s: merge it as soon as `NOTE_AggBackend_To_Agg_Contract.md` lands; until then stand-ins with the §23.2 names.

## 2. Your slices — §23.3, column agg-behaviors, in the §20 order
- **ST-0** ① `UnitStrength.Of`, `CombatEffective.IsInTheFight` / `IsStanding`, the staff's `AmmoOf` and `ObjectiveCensus` answer an
  `AggregateUnit` from its slot table (RV-5) · ⑦ CE-4612 step ⑥ — the commanders' orders ROUTED: re-inventory the `Assign` sites, decide ONE
  neutral params DTO per kind FIRST (write it into §23 before building). Done when: a stochastic platoon under a physical company reads Green in the
  SITREP and counts in an objective; a Defend order resolves to its play.
- **ST-1** `StochasticUnitMove` (B-2): orders → the engine's path follower; a dropped route re-requested.
- **ST-2** `StochasticUnitSop`: under fire the unit breaks contact.
- **ST-3** (after agg-backend's phases) Defend / Attack / BreakContact plays, B-8, the reports (SPOT / CONTACT / SITREP through the existing chain).
- **ST-4** the battalion-vs-battalion scenario with named checks: it runs to an end; the census and the SITREPs agree with the slot tables.
- **ST-5** the policy (§16b — who decides the resolution, when, to what level) and the planners' plan / assign split (D-P3).

## Gates — every slice
T-1 first (each touched feature's own suites); every per-entity step with `HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` + `HROT_WORLD_GUARD=1`; every new type read across units declared (`BrainReads` / `IStableReads`); each new rail red-proved by a compiling mutation; `stepped-hash.py --threads 1,4,8`; 0 B per steady frame (R-2084, no LINQ on the hot path); invariant output (R-4221); recorded components unmanaged, ≤ 1024 B (a size rail). Report per rule 8 (the gate-report contract).

## The report — `STATUS_AggBehaviors.md`
per slice built / stopped and the design folded (as-built into §22 / §23 — obligation ⑤) · every lane decision with what would change it · the
gate-report contract · ids allocated from your block.
