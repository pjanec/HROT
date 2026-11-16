<!--STATUS
state: LIVE — dispatched, not started — EXPERIMENTAL, UNATTENDED
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md — ⭐ THE design: §23 YOUR ids, the contract and the slices per lane; §10–§19 the decisions (the §21 / §17.1 rulings override the rows they name), §22 the UML
  - ../../designs/aggregate-units/DESIGN_Aggregate_Squad.md — the unit state, order routing and child keys the FusionSquad put there
  - ../../designs/aggregate-units/MEASURED_Aggregate_Squad_Seams.md — W-1…W-11, the checklist; the calibration numbers
-->

# HANDOFF — coordinator → `exp/agg-backend`: the stochastic unit — the ENGINE (CE-4616)

**For:** a NEW experimental lane on branch **`exp/agg-backend`** (create it from `origin/coordinator`). **From:** coordinator. **Dispatched at `3f33c0ee3`**;
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
`git fetch origin coordinator exp/agg-backend exp/agg-behaviors exp/agg-ui`; create `exp/agg-backend` from `origin/coordinator` (or check it out if
it exists); restore the test projects you run; push `chore: started exp/agg-backend at <sha>`. Status: `STATUS_AggBackend.md`. Read RULINGS.md, then the design END TO
END (R-129) — §23 first, then §10–§22.

## 1. ⭐ FIRST commit after the start marker — the contract
Declare the four components at their fixed ids (281 `AggregateUnit`, 282 `UnitSlotTable`, 283 `UnitHoldings`, 284 `FireAllocation` — the
constants already exist in `GlobalComponentIds.cs`) with EVERY recorded field of §14–§19 (SV-12), the descriptors `dtAggregateUnit` 220 /
`dtUnitSlotTable` 221 / `dtUnitHoldings` 222 (reserved in `AllDescriptors.cs`), and publish `NOTE_AggBackend_To_Agg_Contract.md`: the layouts,
the slot capacity per echelon, the enum values, the routing-table API. Compiling stubs in the same commit, so the other two lanes can merge.

## 2. Your slices — §23.3, column agg-backend, in the §20 order
- **ST-0** ② the components · ③ their translators in the shared pack (every node role) + a recording round trip · ④ the (class, resolution, order)
  table incl. the SOP (B-1, SV-2 — a table, never a concatenated name) · ⑤ a refused order ⇒ `TaskFailed` (B-8, `TacticalIntentResolutionSystem`) ·
  ⑥ the run seed in `SimRng` (C-8).
- **ST-1** M-1…M-4: plan with the EXISTING planner from the holdings (mounted ⇒ vehicle layer + roads; dismounted ⇒ infantry), the speed model
  (M-2), a deterministic path follower for the unit's point (M-4 — ⛔ not the body kinematics). Done when: arrival within ± 10 % of M-2.
- **ST-2** N-1…N-5: the spatial grid, LOS between footprint samples through the terrain query on the Muscle, Pd, the accumulating levels, the
  write into the SAME squad contact pool (N-2). Done when: Pd against range matches N-1's curve.
- **ST-3** ⚠ **only after minefield MS-T1 has merged into `coordinator`** (it adds the armour BOTTOM facing the table must include): the killer–victim
  table GENERATOR from `ArmorModel` / `HitModel` (RV-1, RV-2, RV-15), `StochasticFirePhase` (A) and `StochasticLossPhase` (B) as per-entity
  phases in `CgfLogicPack` beside the stress phase, the `AreaEffectSystem` sink (serial, after B). Done when: the C-8 ladder passes. Until MS-T1 is
  in: build the generator HARNESS against the four facings and leave the bottom row as a named TODO.
- **ST-4** determinism of a battalion-vs-battalion run at 1 / 4 / 8 threads (agg-behaviors owns the scenario).
- **ST-5** the resolution manager, de-aggregation (children from the TKB composite, stable `<unit>/<slot>` keys — X-3), re-aggregation, the Held
  participant, C-7's forced handover. Done when: a split then a merge keeps every identity; a physical weapon in range forces the handover.

## Gates — every slice
T-1 first (each touched feature's own suites); every per-entity step with `HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` + `HROT_WORLD_GUARD=1`; every new type read across units declared (`BrainReads` / `IStableReads`); each new rail red-proved by a compiling mutation; `stepped-hash.py --threads 1,4,8`; 0 B per steady frame (R-2084, no LINQ on the hot path); invariant output (R-4221); recorded components unmanaged, ≤ 1024 B (a size rail). Report per rule 8 (the gate-report contract).

## The report — `STATUS_AggBackend.md`
per slice built / stopped and the design folded (as-built into §22 / §23 — obligation ⑤) · every lane decision with what would change it · the
gate-report contract · ids allocated from your block.
