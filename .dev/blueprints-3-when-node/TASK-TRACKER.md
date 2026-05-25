# TASK-TRACKER — When-Node Reactivity Iteration

Progress checklist for the When-node iteration.

**References:**
- [TASK-DETAIL.md](./TASK-DETAIL.md) — per-task scope, constraints, success conditions.
- [When_Reactivity_Iteration_Design_v2_2.md](./When_Reactivity_Iteration_Design_v2_2.md) —
  full design (lowering templates, validator diagnostics, drawer specs, test plan).
- [../eqs-2/TASK-DETAIL.md](../eqs-2/TASK-DETAIL.md) — EQS schema deliverables this iteration
  depends on (specifically `TASK-EQS-033` for `LastUpdateTimeSeconds` and `TASK-EQS-037`
  for `EqsSensorHandle`).
- [../../docs/Predicate-Infrastructure-Capabilities.md](../../docs/Predicate-Infrastructure-Capabilities.md)
  — `IPredicateCompiler` substrate consumed by Condition Met mode.

Milestone dependency graph: see [Design §16](./When_Reactivity_Iteration_Design_v2_2.md)
mermaid chart.

---

## Phase M0 — Engine-side coordination

**Goal:** EQS team has the work scheduled, API confirmations are in, `EqsSensorHandle` is
declared (or scheduled) in `FDP.Eqs`.

- [ ] **WHEN-M0-T1** Confirm EQS-side schema deliverables are scheduled [details](./TASK-DETAIL.md#when-m0-t1--confirm-eqs-side-schema-deliverables-are-scheduled)

---

## Phase M1 — Schema and validator

**Goal:** All three node kinds deserialize cleanly; validator emits expected diagnostics
including dispatch restrictions.

- [ ] **WHEN-M1-T1** `EqsSensorHandle` consumed (no implementation here) [details](./TASK-DETAIL.md#when-m1-t1--eqssensorhandle-consumed-no-implementation-here)
- [ ] **WHEN-M1-T2** `WhenNode`, `ReadEqsResultNode`, `SpawnEqsSensorNode` schema classes [details](./TASK-DETAIL.md#when-m1-t2--whennode-readeqsresultnode-spawneqssensornode-schema-classes)
- [ ] **WHEN-M1-T3** `WhenNode` validator (Stage 2 diagnostics `BP20xx`) [details](./TASK-DETAIL.md#when-m1-t3--whennode-validator-stage-2-diagnostics-bp20xx)
- [ ] **WHEN-M1-T4** `ReadEqsResultNode` validator (`BP2020`, `BP2021`) [details](./TASK-DETAIL.md#when-m1-t4--readeqsresultnode-validator-bp2020-bp2021)
- [ ] **WHEN-M1-T5** `SpawnEqsSensorNode` validator (`BP2030`, `BP2031`) [details](./TASK-DETAIL.md#when-m1-t5--spawneqssensornode-validator-bp2030-bp2031)

---

## Phase M2 — `WhenNode` Value Changed and Event Fired lowering

**Goal:** Instance Blueprints with Value Changed or Event Fired `WhenNode`s compile and run.

- [ ] **WHEN-M2-T1** `WhenIrNode` IR primitive + payloads [details](./TASK-DETAIL.md#when-m2-t1--whenirnode-ir-primitive--payloads)
- [ ] **WHEN-M2-T2** Value Changed mode — Stage 6 lowering [details](./TASK-DETAIL.md#when-m2-t2--value-changed-mode--stage-6-lowering)
- [ ] **WHEN-M2-T3** Event Fired mode — Stage 6 lowering [details](./TASK-DETAIL.md#when-m2-t3--event-fired-mode--stage-6-lowering)
- [ ] **WHEN-M2-T4** Value Changed and Event Fired runtime tests [details](./TASK-DETAIL.md#when-m2-t4--value-changed-and-event-fired-runtime-tests)

---

## Phase M3 — Condition Met + predicate-compiler integration

**Goal:** Condition Met `WhenNode` compiles, runs, and survives hot-reload.

- [ ] **WHEN-M3-T1** `ConditionMetIrPayload` + Stage 6 lowering [details](./TASK-DETAIL.md#when-m3-t1--conditionmetirpayload--stage-6-lowering)
- [ ] **WHEN-M3-T2** `AiHotReloadCoordinator.DrainPendingCallbacks` extension [details](./TASK-DETAIL.md#when-m3-t2--aihotreloadcoordinatordrainpendingcallbacks-extension)
- [ ] **WHEN-M3-T3** Condition Met runtime tests + degraded-mode safety [details](./TASK-DETAIL.md#when-m3-t3--condition-met-runtime-tests--degraded-mode-safety)

---

## Phase M4 — EQS Result mode + `ReadEqsResultNode` + `SpawnEqsSensorNode`

**Goal:** All three EQS-related lowerings work against mock scenarios with child-entity
hosting and zero allocations on the hot path.

**Hard dependency:** M0 + EQS-2 `TASK-EQS-033` + `TASK-EQS-037` available in the working
branch.

- [ ] **WHEN-M4-T1** EQS Result mode — common scaffolding [details](./TASK-DETAIL.md#when-m4-t1--eqs-result-mode--common-scaffolding)
- [ ] **WHEN-M4-T2** EQS Result mode — FirstReady, TopChanged, ScoreCrossed, BecomesStale triggers [details](./TASK-DETAIL.md#when-m4-t2--eqs-result-mode--firstready-topchanged-scorecrossed-becomesstale-triggers)
- [ ] **WHEN-M4-T3** `ReadEqsResultNode` lowering [details](./TASK-DETAIL.md#when-m4-t3--readeqsresultnode-lowering)
- [ ] **WHEN-M4-T4** `SpawnEqsSensorNode` lowering [details](./TASK-DETAIL.md#when-m4-t4--spawneqssensornode-lowering)
- [ ] **WHEN-M4-T5** EQS-related runtime tests + inline-array safety [details](./TASK-DETAIL.md#when-m4-t5--eqs-related-runtime-tests--inline-array-safety)

---

## Phase M5 — Editor drawers and palette

**Goal:** Designers can create, configure, and Quick-Reload all three new node kinds.

- [ ] **WHEN-M5-T1** `WhenNodeDrawer` + `WhenNodeSession` [details](./TASK-DETAIL.md#when-m5-t1--whennodedrawer--whennodesession)
- [ ] **WHEN-M5-T2** `ReadEqsResultNodeDrawer` + `ReadEqsResultNodeSession` [details](./TASK-DETAIL.md#when-m5-t2--readeqsresultnodedrawer--readeqsresultnodesession)
- [ ] **WHEN-M5-T3** `SpawnEqsSensorNodeDrawer` + `SpawnEqsSensorNodeSession` [details](./TASK-DETAIL.md#when-m5-t3--spawneqssensornodedrawer--spawneqssensornodesession)
- [ ] **WHEN-M5-T4** Palette entries + mode-aware edge selector [details](./TASK-DETAIL.md#when-m5-t4--palette-entries--mode-aware-edge-selector)

---

## Phase M6 — Visual extensions

**Goal:** Canvas shows pills for all three nodes plus dependency badges; runtime firing
pulses work in Debug mode.

- [ ] **WHEN-M6-T1** `ConditionSummaryAttachment` + provider (`WhenNode`) [details](./TASK-DETAIL.md#when-m6-t1--conditionsummaryattachment--provider-whennode)
- [ ] **WHEN-M6-T2** `EqsTemplateAttachment` (`SpawnEqsSensorNode`) + sensor-name pill (`ReadEqsResultNode`) [details](./TASK-DETAIL.md#when-m6-t2--eqstemplateattachment-spawneqssensornode--sensor-name-pill-readeqsresultnode)
- [ ] **WHEN-M6-T3** `CrossAssetDependencyAttachment` + provider [details](./TASK-DETAIL.md#when-m6-t3--crossassetdependencyattachment--provider)
- [ ] **WHEN-M6-T4** `WhenFiringPulseRenderer` [details](./TASK-DETAIL.md#when-m6-t4--whenfiringpulserenderer)

---

## Phase M7 — Behavior Recipes + "New from Recipe…" workflow

**Goal:** All five recipes compile and tick correctly; the New-from-Recipe dialog produces
working copies.

- [ ] **WHEN-M7-T1** Author five recipe `.bp.json` files [details](./TASK-DETAIL.md#when-m7-t1--author-five-recipe-bpjson-files)
- [ ] **WHEN-M7-T2** `NewFromRecipeService` + Asset Browser submenu + dialog [details](./TASK-DETAIL.md#when-m7-t2--newfromrecipeservice--asset-browser-submenu--dialog)

---

## Phase M8 — Reactive-Guard vocabulary unification + documentation

**Goal:** Consistent "Reactive Guards" category across BTree / HSM / Blueprint editors;
cross-references resolve.

- [ ] **WHEN-M8-T1** `ReactiveGuardVocabulary` string constants + editor wirings [details](./TASK-DETAIL.md#when-m8-t1--reactiveguardvocabulary-string-constants--editor-wirings)
- [ ] **WHEN-M8-T2** `Hrot/Docs/ReactiveGuards.md` author [details](./TASK-DETAIL.md#when-m8-t2--hrotdocsreactiveguardsmd-author)

---

## Phase M9 — End-to-end demo + performance verification

**Goal:** Full pipeline (spawn → observe → read → act) runs cleanly in a real scenario;
performance budgets met.

- [ ] **WHEN-M9-T1** `CoverAwarePatrol` end-to-end integration test [details](./TASK-DETAIL.md#when-m9-t1--coverawarepatrol-end-to-end-integration-test)
- [ ] **WHEN-M9-T2** Performance test battery [details](./TASK-DETAIL.md#when-m9-t2--performance-test-battery)
- [ ] **WHEN-M9-T3** Hot-reload integration battery [details](./TASK-DETAIL.md#when-m9-t3--hot-reload-integration-battery)
