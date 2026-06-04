# Blueprint Integration Finalization -- Task Tracker

One line per batch. Check the box when the batch is implemented, verified green, and committed.
Full descriptions, scope, and key files are in [TASK-DETAIL.md](./TASK-DETAIL.md).
Per-batch instructions live in `batches/`, reports in `reports/`.

Status legend: `[ ]` open / `[~]` in progress / `[x]` done (verified + committed). Do not delete rows.

---

## Phase 0 -- Hardening (P1 + observability)

- [x] **BATCH-01** -- DEBT-MVE-003 P1: multi-blueprint quick-reload safety (registry merge-commit + per-asset ALC) -> commit `2d06f741` -> [details](./TASK-DETAIL.md#batch-01----debt-mve-003-multi-blueprint-quick-reload-safety)
- [x] **BATCH-04** -- DEBT-MVE-002: emit `StateFields` in Instance codegen (durable observe by field name) -> committed -> [details](./TASK-DETAIL.md#batch-04----debt-mve-002-emit-statefields-in-codegen)

## Phase 1 -- Authoring: node value pins

- [x] **BATCH-02** -- Task 3: node value pins for all node kinds (ReadRankedResult / CallCustomEvent / CallPeerBlueprint return) -> committed -> [details](./TASK-DETAIL.md#batch-02----task-3-node-value-pins-for-all-node-kinds)

## Phase 2 -- Tasks 1+2: in-blueprint function-graph calls (Option B, full feature)

- [x] **BATCH-03A** -- Compiler core: `FunctionCallNode.TargetGraphId` + `IrOp_GraphCall` + Entry-input binding + emit-as-method + BP1650 latent-forbidden + e2e test -> committed -> [details](./TASK-DETAIL.md#batch-03a----compiler-core-in-blueprint-function-graph-calls)
- [x] **BATCH-03B** -- Compiler: validation hardening (BP1651 target, BP1652 arg-count, BP1653 arg-type, BP1654 recursion/cycle) + negative tests -> committed -> [details](./TASK-DETAIL.md#batch-03b----compiler-validation-hardening)
- [ ] **BATCH-03C** -- Editor projection: Entry/Return value pins from `Graph.Inputs/Outputs` + FunctionCall mirrors in-blueprint target-graph signature -> [details](./TASK-DETAIL.md#batch-03c----editor-projection-entryreturn-value-pins--functioncall-mirrors-graph-signature)
- [ ] **BATCH-03C2** -- Editor: `CallPeerBlueprintNode` arg pins via extended `BlueprintSignature` (per-function Inputs/Outputs) + sibling-signature registry threaded to projection (BATCH-02 deferral) -> [details](./TASK-DETAIL.md#batch-03c2----callpeerblueprint-arg-pins-via-extended-blueprintsignature)
- [ ] **BATCH-03D** -- Editor UI: FunctionCall Details/picker panel (CLR method OR in-blueprint graph) + graph-signature editing panel (`Graph.Inputs/Outputs`) -> [details](./TASK-DETAIL.md#batch-03d----editor-ui-functioncall-picker--graph-signature-editing-panel)

## Phase 3 -- Demonstrable authoring

- [ ] **BATCH-05** -- Task 6: hand-authored `.bp.json` that increments a blackboard `Count` and visibly counts up in the running editor inspector -> [details](./TASK-DETAIL.md#batch-05----task-6-canvas-authorable-counting-demo)

## Phase 4 -- Canvas polish backlog (Task 4, lower priority)

- [ ] **BATCH-06** -- ChannelCommand param enrichment (DEBT-BCP-006) -> [details](./TASK-DETAIL.md#batch-06----channelcommand-param-enrichment-debt-bcp-006)
- [ ] **BATCH-07** -- Inline mini-editors for node value pins -> [details](./TASK-DETAIL.md#batch-07----inline-mini-editors)
- [ ] **BATCH-08** -- Fonts: engine multi-size atlas (NodeEdit S05 / font handling) -> [details](./TASK-DETAIL.md#batch-08----fonts-multi-size-atlas)
- [ ] **BATCH-09** -- Comments / reroutes / containers (NodeEdit S06 / S26 / S27 / S35) -> [details](./TASK-DETAIL.md#batch-09----comments--reroutes--containers)

---

## Pre-existing test failures (NOT regressions -- do not chase)
After BATCH-04 the green baseline for `Hrot.Blueprints.Tests` is **7 failures**, all pre-existing DEBT-006:
AiPrimitiveEmitGolden (×2 cases), LibraryEmitGolden, LibraryMath snapshot, MoveToAndFire snapshot,
ConditionSummary, AllocationFree. Plus the flaky sub-150ns WhenNode perf test under load (DEBT-014; passes
in isolation). Every batch must keep the failing set a SUBSET of these (0 new) unless it intentionally
re-baselines a golden (BATCH-04 resolved the 3 Instance goldens).

## Done-definition for this thread
Multi-blueprint editor use is safe (BATCH-01); compiled blueprints observable by field name (BATCH-04);
every node kind exposes its value pins (BATCH-02 + BATCH-03C); FunctionCall is configurable and can call a
CLR method OR an in-blueprint function graph (BATCH-03A/B/C/D); graphs have editable signatures with
Entry/Return value pins (BATCH-03C/D); and a hand-authored `.bp.json` visibly counts up (BATCH-05).
Canvas polish (BATCH-06..09) is best-effort.
