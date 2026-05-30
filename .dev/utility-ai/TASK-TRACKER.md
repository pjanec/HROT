# Utility AI — Task Tracker

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

**Design references:**
- [Architecture v1.2](./Utility_AI_Design_v1_1.md)
- [Source Generator v1.1](./Utility_AI_SourceGenerator_Design_v1_1.md)
- [Editor v1.2](./Utility_AI_Editor_Design_v1_2.md)
- [Starter Pack v1.2](./Utility_AI_StarterPack_Examples_v1_1.md)
- [Tuning Console & Overlays v1.0](./Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md)
- [Curve Editor in StructEdit v1.1](./Curve_Editor_in_StructEdit_Guide_v1_1.md)
- [Build Order v1.0](./Build_Order_UtilityAI_Tuning_Overlays_v1_0.md)
- [Phase-0 Bundle](./PREREQ_Phase0_Bundle.md)

**Debt:** [DEBT-TRACKER.md](./DEBT-TRACKER.md)

---

## Phase 0 — Prerequisite bundle

**Goal:** Land six codebase prerequisites so Phase-1 code compiles against real APIs.

- [x] **TASK-UAI-P0-01** `WeaponState.MaxAmmo` cache [details](./TASK-DETAIL.md#task-uai-p0-01-weaponstatemaxammo-cache) — BATCH-01
- [x] **TASK-UAI-P0-02** Multi-mount weapon entities [details](./TASK-DETAIL.md#task-uai-p0-02-multi-mount-weapon-entities) — BATCH-01
- [x] **TASK-UAI-P0-03** Raise `MaxTrackedTargets` to 16 [details](./TASK-DETAIL.md#task-uai-p0-03-raise-maxtrackedtargets-to-16) — BATCH-01
- [x] **TASK-UAI-P0-04** `UnitRoster.Add` / `IndexOf` helpers [details](./TASK-DETAIL.md#task-uai-p0-04-unitrosteradd--indexof-helpers) — BATCH-01
- [x] **TASK-UAI-P0-05** `Blackboard1024.Project<T>` helper [details](./TASK-DETAIL.md#task-uai-p0-05-blackboard1024projectt-helper) — BATCH-01
- [x] **TASK-UAI-P0-06** `UtilityTestWorld` helper [details](./TASK-DETAIL.md#task-uai-p0-06-utilitytestworld-helper) — BATCH-01
- [x] **TASK-UAI-P0-07** Phase-0 integration test (gate) [details](./TASK-DETAIL.md#task-uai-p0-07-phase-0-integration-test-gate) — BATCH-01

---

## Phase 1 — Runtime core + trace buffer

**Goal:** Scoring core, curve evaluation, aggregator, trace buffer, four starter-pack decisions — headless.

- [x] **TASK-UAI-P1-01** Scoring core data structures [details](./TASK-DETAIL.md#task-uai-p1-01-scoring-core-data-structures) — BATCH-02
- [x] **TASK-UAI-P1-02** Curve evaluation [details](./TASK-DETAIL.md#task-uai-p1-02-curve-evaluation-curveevaluate) — BATCH-02
- [x] **TASK-UAI-P1-03** Aggregator (product-with-compensation + sum) [details](./TASK-DETAIL.md#task-uai-p1-03-aggregator-product-with-compensation--sum) — BATCH-02
- [x] **TASK-UAI-P1-04** `UtilityResultBuffer` + trace buffer [details](./TASK-DETAIL.md#task-uai-p1-04-utilityresultbuffer-and-trace-buffer) — BATCH-03
- [x] **TASK-UAI-P1-05** `UtilityScorer` core tick path [details](./TASK-DETAIL.md#task-uai-p1-05-utilityscorer-core-tick-path) — BATCH-03
- [x] **TASK-UAI-P1-06** Standard input readers catalog [details](./TASK-DETAIL.md#task-uai-p1-06-standard-input-readers-catalog) — BATCH-04
- [x] **TASK-UAI-P1-07** `ThreatMatrixAssignmentSystem` [details](./TASK-DETAIL.md#task-uai-p1-07-threatmatrixassignmentsystem-squad-greedy-assignment) — BATCH-05 (corrective in BATCH-06)
- [x] **TASK-UAI-P1-08** Starter-pack decisions + integration tests [details](./TASK-DETAIL.md#task-uai-p1-08-starter-pack-decisions--integration-tests) — BATCH-05 (corrective in BATCH-06)
- [x] **TASK-UAI-P1-09** Integration nodes (BTree / HSM / Blueprint) [details](./TASK-DETAIL.md#task-uai-p1-09-integration-nodes-btree--hsm--blueprint) — BATCH-06 (1-A ✅ 1-B ✅ 1-C ✅) + BATCH-07 (1-C steps 4-7)

---

## Phase 2 — Source generator + analyzer

**Goal:** `In.*` accessors, registrars, and the `UT####` diagnostics.

- [x] **TASK-UAI-P2-01** `UtilityInputGenerator` [details](./TASK-DETAIL.md#task-uai-p2-01-utilityinputgenerator) — BATCH-08
- [x] **TASK-UAI-P2-02** `UtilityDecisionGenerator` [details](./TASK-DETAIL.md#task-uai-p2-02-utilitydecisiongenerator) — BATCH-09
- [x] **TASK-UAI-P2-03** `UtilityAuthoringAnalyzer` [details](./TASK-DETAIL.md#task-uai-p2-03-utilityauthoringanalyzer) — BATCH-10
- [x] **TASK-UAI-P2-04** Startup handshake [details](./TASK-DETAIL.md#task-uai-p2-04-startup-handshake) — BATCH-08

---

## Phase 3 — Standalone curve widget

**Goal:** One curve widget, host-agnostic, for both the editor and (Phase-6) the tuning console.

- [x] **TASK-UAI-P3-01** `CurveWidget.Draw` host-agnostic widget [details](./TASK-DETAIL.md#task-uai-p3-01-curvewidgetdraw-host-agnostic-widget) — BATCH-11 (corrective BATCH-12)

---

## Phase 4 — AI overlays + tuning console Slice 1

**Goal:** Observe→tune loop online with scalar tuning before the visual editor exists.

- [x] **TASK-UAI-P4-01** `AiOverlayFlags` + per-entity gating [details](./TASK-DETAIL.md#task-uai-p4-01-aioverlayflags--per-entity-gating) — BATCH-12
- [x] **TASK-UAI-P4-02** Five overlay sources [details](./TASK-DETAIL.md#task-uai-p4-02-five-overlay-sources) — BATCH-12
- [x] **TASK-UAI-P4-03** `TuningRegistry` + `TuningConsoleGizmo` Slice 1 [details](./TASK-DETAIL.md#task-uai-p4-03-tuningregistry--tuningconsolegizmo-slice-1-scalars) — BATCH-13

---

## Phase 5 — Utility editor (card-table)

**Goal:** Visual authoring with lossless C# round-trip.

- [x] **TASK-UAI-P5-01** `UtilityDecisionAsset` model + `ManagedWindow` host [details](./TASK-DETAIL.md#task-uai-p5-01-utilitydecisionasset-model--managedwindow-host) — BATCH-14
- [ ] **TASK-UAI-P5-02** Input catalog browser + curve inspector [details](./TASK-DETAIL.md#task-uai-p5-02-input-catalog-browser--curve-inspector-calls-task-uai-p3-01)
- [ ] **TASK-UAI-P5-03** Live preview + in-editor debug [details](./TASK-DETAIL.md#task-uai-p5-03-live-preview--in-editor-debug-reads-phase-1-trace-throttled-10-hz)
- [x] **TASK-UAI-P5-04** `UtilityFluentEmitter` [details](./TASK-DETAIL.md#task-uai-p5-04-utilityfluentemitter-lossless-round-trip) — BATCH-15
- [x] **TASK-UAI-P5-05** Comparison integration [details](./TASK-DETAIL.md#task-uai-p5-05-comparison-integration-sanitizer--tuning-diff-fast-lane) — BATCH-16
- [x] **TASK-UAI-P5-06** Shared-infra extensions [details](./TASK-DETAIL.md#task-uai-p5-06-shared-infra-extensions-4-small-touches) — BATCH-14

---

## Phase 6 — Tuning console Slice 2 + bridge + polish

**Goal:** Visual curve editing in-world + editor↔console bridge + snapshot/restore.

- [ ] **TASK-UAI-P6-01** `UtilityCurveFieldEditor` + `UtilityCurveFieldDrawer` [details](./TASK-DETAIL.md#task-uai-p6-01-utilitycurvefieldeditor--utilitycurvefielddrawer)
- [ ] **TASK-UAI-P6-02** Piecewise translate-on-apply [details](./TASK-DETAIL.md#task-uai-p6-02-piecewise-translate-on-apply)
- [ ] **TASK-UAI-P6-03** Editor ↔ console bridge [details](./TASK-DETAIL.md#task-uai-p6-03-editor--console-bridge)
- [ ] **TASK-UAI-P6-04** Snapshot / restore [details](./TASK-DETAIL.md#task-uai-p6-04-snapshot--restore-revert-group--revert-all)
