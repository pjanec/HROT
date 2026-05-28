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

- [ ] **TASK-UAI-P0-01** `WeaponState.MaxAmmo` cache [details](./TASK-DETAIL.md#task-uai-p0-01-weaponstatemaxammo-cache)
- [ ] **TASK-UAI-P0-02** Multi-mount weapon entities [details](./TASK-DETAIL.md#task-uai-p0-02-multi-mount-weapon-entities)
- [ ] **TASK-UAI-P0-03** Raise `MaxTrackedTargets` to 16 [details](./TASK-DETAIL.md#task-uai-p0-03-raise-maxtrackedtargets-to-16)
- [ ] **TASK-UAI-P0-04** `UnitRoster.Add` / `IndexOf` helpers [details](./TASK-DETAIL.md#task-uai-p0-04-unitrosteradd--indexof-helpers)
- [ ] **TASK-UAI-P0-05** `Blackboard1024.Project<T>` helper [details](./TASK-DETAIL.md#task-uai-p0-05-blackboard1024projectt-helper)
- [ ] **TASK-UAI-P0-06** `UtilityTestWorld` helper [details](./TASK-DETAIL.md#task-uai-p0-06-utilitytestworld-helper)
- [ ] **TASK-UAI-P0-07** Phase-0 integration test (gate) [details](./TASK-DETAIL.md#task-uai-p0-07-phase-0-integration-test-gate)

---

## Phase 1 — Runtime core + trace buffer

**Goal:** Scoring core, curve evaluation, aggregator, trace buffer, four starter-pack decisions — headless.

- [ ] **TASK-UAI-P1-01** Scoring core data structures [details](./TASK-DETAIL.md#task-uai-p1-01-scoring-core-data-structures)
- [ ] **TASK-UAI-P1-02** Curve evaluation [details](./TASK-DETAIL.md#task-uai-p1-02-curve-evaluation-curveevaluate)
- [ ] **TASK-UAI-P1-03** Aggregator (product-with-compensation + sum) [details](./TASK-DETAIL.md#task-uai-p1-03-aggregator-product-with-compensation--sum)
- [ ] **TASK-UAI-P1-04** `UtilityResultBuffer` + trace buffer [details](./TASK-DETAIL.md#task-uai-p1-04-utilityresultbuffer-and-trace-buffer)
- [ ] **TASK-UAI-P1-05** `UtilityScorer` core tick path [details](./TASK-DETAIL.md#task-uai-p1-05-utilityscorer-core-tick-path)
- [ ] **TASK-UAI-P1-06** Standard input readers catalog [details](./TASK-DETAIL.md#task-uai-p1-06-standard-input-readers-catalog)
- [ ] **TASK-UAI-P1-07** `ThreatMatrixAssignmentSystem` [details](./TASK-DETAIL.md#task-uai-p1-07-threatmatrixassignmentsystem-squad-greedy-assignment)
- [ ] **TASK-UAI-P1-08** Starter-pack decisions + integration tests [details](./TASK-DETAIL.md#task-uai-p1-08-starter-pack-decisions--integration-tests)
- [ ] **TASK-UAI-P1-09** Integration nodes (BTree / HSM / Blueprint) [details](./TASK-DETAIL.md#task-uai-p1-09-integration-nodes-btree--hsm--blueprint)

---

## Phase 2 — Source generator + analyzer

**Goal:** `In.*` accessors, registrars, and the `UT####` diagnostics.

- [ ] **TASK-UAI-P2-01** `UtilityInputGenerator` [details](./TASK-DETAIL.md#task-uai-p2-01-utilityinputgenerator)
- [ ] **TASK-UAI-P2-02** `UtilityDecisionGenerator` [details](./TASK-DETAIL.md#task-uai-p2-02-utilitydecisiongenerator)
- [ ] **TASK-UAI-P2-03** `UtilityAuthoringAnalyzer` [details](./TASK-DETAIL.md#task-uai-p2-03-utilityauthoringanalyzer)
- [ ] **TASK-UAI-P2-04** Startup handshake [details](./TASK-DETAIL.md#task-uai-p2-04-startup-handshake)

---

## Phase 3 — Standalone curve widget

**Goal:** One curve widget, host-agnostic, for both the editor and (Phase-6) the tuning console.

- [ ] **TASK-UAI-P3-01** `CurveWidget.Draw` host-agnostic widget [details](./TASK-DETAIL.md#task-uai-p3-01-curvewidgetdraw-host-agnostic-widget)

---

## Phase 4 — AI overlays + tuning console Slice 1

**Goal:** Observe→tune loop online with scalar tuning before the visual editor exists.

- [ ] **TASK-UAI-P4-01** `AiOverlayFlags` + per-entity gating [details](./TASK-DETAIL.md#task-uai-p4-01-aioverlayflags--per-entity-gating)
- [ ] **TASK-UAI-P4-02** Five overlay sources [details](./TASK-DETAIL.md#task-uai-p4-02-five-overlay-sources)
- [ ] **TASK-UAI-P4-03** `TuningRegistry` + `TuningConsoleGizmo` Slice 1 [details](./TASK-DETAIL.md#task-uai-p4-03-tuningregistry--tuningconsolegizmo-slice-1-scalars)

---

## Phase 5 — Utility editor (card-table)

**Goal:** Visual authoring with lossless C# round-trip.

- [ ] **TASK-UAI-P5-01** `UtilityDecisionAsset` model + `ManagedWindow` host [details](./TASK-DETAIL.md#task-uai-p5-01-utilitydecisionasset-model--managedwindow-host)
- [ ] **TASK-UAI-P5-02** Input catalog browser + curve inspector [details](./TASK-DETAIL.md#task-uai-p5-02-input-catalog-browser--curve-inspector-calls-task-uai-p3-01)
- [ ] **TASK-UAI-P5-03** Live preview + in-editor debug [details](./TASK-DETAIL.md#task-uai-p5-03-live-preview--in-editor-debug-reads-phase-1-trace-throttled-10-hz)
- [ ] **TASK-UAI-P5-04** `UtilityFluentEmitter` [details](./TASK-DETAIL.md#task-uai-p5-04-utilityfluentemitter-lossless-round-trip)
- [ ] **TASK-UAI-P5-05** Comparison integration [details](./TASK-DETAIL.md#task-uai-p5-05-comparison-integration-sanitizer--tuning-diff-fast-lane)
- [ ] **TASK-UAI-P5-06** Shared-infra extensions [details](./TASK-DETAIL.md#task-uai-p5-06-shared-infra-extensions-4-small-touches)

---

## Phase 6 — Tuning console Slice 2 + bridge + polish

**Goal:** Visual curve editing in-world + editor↔console bridge + snapshot/restore.

- [ ] **TASK-UAI-P6-01** `UtilityCurveFieldEditor` + `UtilityCurveFieldDrawer` [details](./TASK-DETAIL.md#task-uai-p6-01-utilitycurvefieldeditor--utilitycurvefielddrawer)
- [ ] **TASK-UAI-P6-02** Piecewise translate-on-apply [details](./TASK-DETAIL.md#task-uai-p6-02-piecewise-translate-on-apply)
- [ ] **TASK-UAI-P6-03** Editor ↔ console bridge [details](./TASK-DETAIL.md#task-uai-p6-03-editor--console-bridge)
- [ ] **TASK-UAI-P6-04** Snapshot / restore [details](./TASK-DETAIL.md#task-uai-p6-04-snapshot--restore-revert-group--revert-all)
