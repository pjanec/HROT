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

- [x] **WHEN-M0-T1** Confirm EQS-side schema deliverables are scheduled [details](./TASK-DETAIL.md#when-m0-t1--confirm-eqs-side-schema-deliverables-are-scheduled)

---

## Phase M1 — Schema and validator

**Goal:** All three node kinds deserialize cleanly; validator emits expected diagnostics
including dispatch restrictions.

- [x] **WHEN-M1-T1** `EqsSensorHandle` consumed (no implementation here) [details](./TASK-DETAIL.md#when-m1-t1--eqssensorhandle-consumed-no-implementation-here)
- [x] **WHEN-M1-T2** `WhenNode`, `ReadEqsResultNode`, `SpawnEqsSensorNode` schema classes [details](./TASK-DETAIL.md#when-m1-t2--whennode-readeqsresultnode-spawneqssensornode-schema-classes)
- [x] **WHEN-M1-T3** `WhenNode` validator (Stage 2 diagnostics `BP20xx`) [details](./TASK-DETAIL.md#when-m1-t3--whennode-validator-stage-2-diagnostics-bp20xx)
- [x] **WHEN-M1-T4** `ReadEqsResultNode` validator (`BP2020`, `BP2021`) [details](./TASK-DETAIL.md#when-m1-t4--readeqsresultnode-validator-bp2020-bp2021)
- [x] **WHEN-M1-T5** `SpawnEqsSensorNode` validator (`BP2030`, `BP2031`) [details](./TASK-DETAIL.md#when-m1-t5--spawneqssensornode-validator-bp2030-bp2031)

---

## Phase M2 — `WhenNode` Value Changed and Event Fired lowering

**Goal:** Instance Blueprints with Value Changed or Event Fired `WhenNode`s compile and run.

- [x] **WHEN-M2-T1** `WhenIrNode` IR primitive + payloads [details](./TASK-DETAIL.md#when-m2-t1--whenirnode-ir-primitive--payloads)
- [x] **WHEN-M2-T2** Value Changed mode — Stage 6 lowering [details](./TASK-DETAIL.md#when-m2-t2--value-changed-mode--stage-6-lowering)
- [x] **WHEN-M2-T3** Event Fired mode — Stage 6 lowering [details](./TASK-DETAIL.md#when-m2-t3--event-fired-mode--stage-6-lowering)
- [x] **WHEN-M2-T4** Value Changed and Event Fired runtime tests [details](./TASK-DETAIL.md#when-m2-t4--value-changed-and-event-fired-runtime-tests)

---

## Phase M3 — Condition Met + predicate-compiler integration

**Goal:** Condition Met `WhenNode` compiles, runs, and survives hot-reload.

- [x] **WHEN-M3-T1** `ConditionMetIrPayload` + Stage 6 lowering [details](./TASK-DETAIL.md#when-m3-t1--conditionmetirpayload--stage-6-lowering)
- [x] **WHEN-M3-T2** `AiHotReloadCoordinator.DrainPendingCallbacks` extension [details](./TASK-DETAIL.md#when-m3-t2--aihotreloadcoordinatordrainpendingcallbacks-extension)
- [x] **WHEN-M3-T3** Condition Met runtime tests + degraded-mode safety [details](./TASK-DETAIL.md#when-m3-t3--condition-met-runtime-tests--degraded-mode-safety)

---

## Phase M4 — EQS Result mode + `ReadEqsResultNode` + `SpawnEqsSensorNode`

**Goal:** All three EQS-related lowerings work against mock scenarios with child-entity
hosting and zero allocations on the hot path.

**Hard dependency:** M0 + EQS-2 `TASK-EQS-033` + `TASK-EQS-037` available in the working
branch.

- [x] **WHEN-M4-T1** EQS Result mode — common scaffolding [details](./TASK-DETAIL.md#when-m4-t1--eqs-result-mode--common-scaffolding)
- [x] **WHEN-M4-T2** EQS Result mode — FirstReady, TopChanged, ScoreCrossed, BecomesStale triggers [details](./TASK-DETAIL.md#when-m4-t2--eqs-result-mode--firstready-topchanged-scorecrossed-becomesstale-triggers)
- [x] **WHEN-M4-T3** `ReadEqsResultNode` lowering [details](./TASK-DETAIL.md#when-m4-t3--readeqsresultnode-lowering)
- [x] **WHEN-M4-T4** `SpawnEqsSensorNode` lowering [details](./TASK-DETAIL.md#when-m4-t4--spawneqssensornode-lowering)
- [x] **WHEN-M4-T5** EQS-related runtime tests + inline-array safety [details](./TASK-DETAIL.md#when-m4-t5--eqs-related-runtime-tests--inline-array-safety)

---

## Phase M5 — Editor drawers and palette

**Goal:** Designers can create, configure, and Quick-Reload all three new node kinds.

- [x] **WHEN-M5-T1** `WhenNodeDrawer` + `WhenNodeSession` [details](./TASK-DETAIL.md#when-m5-t1--whennodedrawer--whennodesession)
- [x] **WHEN-M5-T2** `ReadEqsResultNodeDrawer` + `ReadEqsResultNodeSession` [details](./TASK-DETAIL.md#when-m5-t2--readeqsresultnodedrawer--readeqsresultnodesession)
- [x] **WHEN-M5-T3** `SpawnEqsSensorNodeDrawer` + `SpawnEqsSensorNodeSession` [details](./TASK-DETAIL.md#when-m5-t3--spawneqssensornodedrawer--spawneqssensornodesession)
- [x] **WHEN-M5-T4** Palette entries + mode-aware edge selector [details](./TASK-DETAIL.md#when-m5-t4--palette-entries--mode-aware-edge-selector)

---

## Phase M6 — Visual extensions

**Goal:** Canvas shows pills for all three nodes plus dependency badges; runtime firing
pulses work in Debug mode.

- [x] **WHEN-M6-T1** `ConditionSummaryAttachment` + provider (`WhenNode`) [details](./TASK-DETAIL.md#when-m6-t1--conditionsummaryattachment--provider-whennode)
- [x] **WHEN-M6-T2** `EqsTemplateAttachment` (`SpawnEqsSensorNode`) + sensor-name pill (`ReadEqsResultNode`) [details](./TASK-DETAIL.md#when-m6-t2--eqstemplateattachment-spawneqssensornode--sensor-name-pill-readeqsresultnode)
- [x] **WHEN-M6-T3** `CrossAssetDependencyAttachment` + provider [details](./TASK-DETAIL.md#when-m6-t3--crossassetdependencyattachment--provider)
- [x] **WHEN-M6-T4** `WhenFiringPulseRenderer` [details](./TASK-DETAIL.md#when-m6-t4--whenfiringpulserenderer)

---

## Phase M7 — Behavior Recipes + "New from Recipe…" workflow

**Goal:** All five recipes compile and tick correctly; the New-from-Recipe dialog produces
working copies.

- [x] **WHEN-M7-T1** Author five recipe `.bp.json` files [details](./TASK-DETAIL.md#when-m7-t1--author-five-recipe-bpjson-files)
- [x] **WHEN-M7-T2** `NewFromRecipeService` + Asset Browser submenu + dialog [details](./TASK-DETAIL.md#when-m7-t2--newfromrecipeservice--asset-browser-submenu--dialog)

---

## Phase M8 — Reactive-Guard vocabulary unification + documentation

**Goal:** Consistent "Reactive Guards" category across BTree / HSM / Blueprint editors;
cross-references resolve.

- [x] **WHEN-M8-T1** `ReactiveGuardVocabulary` string constants + editor wirings [details](./TASK-DETAIL.md#when-m8-t1--reactiveguardvocabulary-string-constants--editor-wirings)
- [x] **WHEN-M8-T2** `Hrot/Docs/ReactiveGuards.md` author [details](./TASK-DETAIL.md#when-m8-t2--hrotdocsreactiveguardsmd-author)

---

## Phase M9 — End-to-end demo + performance verification

**Goal:** Full pipeline (spawn → observe → read → act) runs cleanly in a real scenario;
performance budgets met.

- [x] **WHEN-M9-T1** `CoverAwarePatrol` end-to-end integration test [details](./TASK-DETAIL.md#when-m9-t1--coverawarepatrol-end-to-end-integration-test)
- [x] **WHEN-M9-T2** Performance test battery [details](./TASK-DETAIL.md#when-m9-t2--performance-test-battery)
- [x] **WHEN-M9-T3** Hot-reload integration battery [details](./TASK-DETAIL.md#when-m9-t3--hot-reload-integration-battery)

---

## Phase M10 — Corrective: library defects

**Goal:** Fix four code defects from independent review + three test-coverage holes from
the post-implementation walk-through. Each supersedes a constraint in an earlier task
where indicated.

- [x] **WHEN-M10-T1** Deterministic `PartMetadata.InstanceId` via `BlueprintIdHash.Compute()` *(supersedes WHEN-M4-T4 recommendation)* [details](./TASK-DETAIL.md#when-m10-t1--deterministic-partmetadatainstanceid-via-blueprintidhashcompute)
- [x] **WHEN-M10-T2** `HasComponent<EqsCognitiveBuffer>` guard + safe-default contract in `ReadEqsResult` helper *(supersedes WHEN-M4-T3 failure-path)* [details](./TASK-DETAIL.md#when-m10-t2--hascomponenteqscognitivebuffer-guard--safe-default-contract-in-readeqsresult-helper)
- [x] **WHEN-M10-T3** Vector-aware epsilon comparison in Value Changed lowering [details](./TASK-DETAIL.md#when-m10-t3--vector-aware-epsilon-comparison-in-value-changed-lowering)
- [x] **WHEN-M10-T4** `BP2014` epsilon warning must check the resolved property type [details](./TASK-DETAIL.md#when-m10-t4--bp2014-epsilon-warning-must-check-the-resolved-property-type)
- [x] **WHEN-M10-T5** `SpawnEqsSensorNode` pin-binding test coverage (or formal closure) [details](./TASK-DETAIL.md#when-m10-t5--spawneqssensornode-pin-binding-test-coverage)
- [⚠️] **WHEN-M10-T6** Strengthen `CoverAwarePatrol_HotReload_SoftReload_*` to assert sensor preservation [details](./TASK-DETAIL.md#when-m10-t6--strengthen-coverawarepatrol_hotreload_softreload_-to-assert-sensor-preservation) — corrective in BATCH-17 (recipe Links empty, sensor never spawns)

---

## Phase M11 — Corrective: Production wiring

**Goal:** Wire the implemented library into the running Blueprint editor. Identical
gap-pattern to EQS-2 and Universal-Breakpoints corrective phases — every editor-side
class added by this iteration currently has zero inbound callers from production code.

**Hard dependency:** M10 lands first (correctness fixes before production exposure).

- [x] **WHEN-M11-T1** Register the three drawers with the editor's `DrawerRegistry` [details](./TASK-DETAIL.md#when-m11-t1--register-the-three-drawers-with-the-editors-drawerregistry)
- [x] **WHEN-M11-T2** Register `WhenNodePaletteEntries` in the palette host [details](./TASK-DETAIL.md#when-m11-t2--register-whennodepaletteentries-in-the-palette-host)
- [x] **WHEN-M11-T3** Register the three visual attachment providers with the canvas [details](./TASK-DETAIL.md#when-m11-t3--register-the-three-visual-attachment-providers-with-the-canvas)
- [x] **WHEN-M11-T4** Move recipes to production location + wire Asset Browser discovery [details](./TASK-DETAIL.md#when-m11-t4--move-recipes-to-production-location--wire-asset-browser-discovery)
- [x] **WHEN-M11-T5** Consolidate the two `ReactiveGuardVocabulary` declarations [details](./TASK-DETAIL.md#when-m11-t5--consolidate-the-two-reactiveguardvocabulary-declarations)
- [x] **WHEN-M11-T6** End-to-end "wired" smoke test in the running editor [details](./TASK-DETAIL.md#when-m11-t6--end-to-end-wired-smoke-test-in-the-running-editor)
