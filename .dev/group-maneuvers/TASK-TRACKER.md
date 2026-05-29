# Squad Coordination — Task Tracker

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

**Design references:**
- [Squad Coordination v1.1](./Squad_Coordination_Design_v1_1.md) — architecture
- [Step 1.5 — TargetMemory 3D reconciliation](./Step_1_5_TargetMemory_3D_Reconciliation.md) — pre-step gate
- [Utility AI v1.2](../utility-ai/Utility_AI_Design_v1_1.md) — the scoring core role/fire/slot/maneuver assignment reuses
- [Utility Editor v1.2](../utility-ai/Utility_AI_Editor_Design_v1_2.md) — `ManeuverSelect` authoring
- [Tuning Console & Overlays v1.0](../utility-ai/Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md) — overlay extension point
- [Curve Editor in StructEdit v1.1](../utility-ai/Curve_Editor_in_StructEdit_Guide_v1_1.md) — used by the editor for `ManeuverSelect` curves

**Assumed-completed dependencies** (must be merged green before this workstream's Phase 0 begins):
- Utility AI Phases 0–6 (P3–P6 finish before squad starts; see [`../utility-ai/TASK-TRACKER.md`](../utility-ai/TASK-TRACKER.md)).
- 3D Cognitive Spatial Awareness Promotion (`TargetMemory`, `EqsResult`, EQS cover query are 3D-native; `EqsResult` is 32 B with `PositionZ`).
- [Step_1_5_TargetMemory_3D_Reconciliation.md](./Step_1_5_TargetMemory_3D_Reconciliation.md) — Utility readers consume the 3D `TargetMemory`.

**Debt:** [DEBT-TRACKER.md](./DEBT-TRACKER.md)

---

## Phase 0 — Prerequisites & state layout

**Goal:** Land the upfront layout reconciliation, the `ManeuverSelect` decision-kind extension, and the fake-provider scaffolding so Phase 1 compiles against real APIs.

- [ ] **TASK-SQD-P0-01** Shrink `AssignmentSlot` (64→16 B) + migrate `ThreatMatrixAssignmentSystem` [details](./TASK-DETAIL.md#task-sqd-p0-01-shrink-assignmentslot-and-migrate-threatmatrixassignmentsystem)
- [ ] **TASK-SQD-P0-02** `SquadCognitiveState` — single contiguous projection [details](./TASK-DETAIL.md#task-sqd-p0-02-squadcognitivestate--single-contiguous-projection)
- [ ] **TASK-SQD-P0-03** Add `ManeuverSelect` to `DecisionKind` + source-gen support [details](./TASK-DETAIL.md#task-sqd-p0-03-add-maneuverselect-to-decisionkind--source-gen-support)
- [ ] **TASK-SQD-P0-04** `FakeDangerAreaProvider` scaffolding [details](./TASK-DETAIL.md#task-sqd-p0-04-fakedangerareaprovider-scaffolding)
- [ ] **TASK-SQD-P0-05** Phase-0 integration gate test [details](./TASK-DETAIL.md#task-sqd-p0-05-phase-0-integration-gate-test)

---

## Phase 1 — Primitives library

**Goal:** The five Brain-resident primitives (§2) as a clean library. Three reuse the Utility allocation matrix; two are new.

- [ ] **TASK-SQD-P1-01** Element partition primitive (with hysteresis §4.1) [details](./TASK-DETAIL.md#task-sqd-p1-01-element-partition-primitive-with-hysteresis)
- [ ] **TASK-SQD-P1-02** Tactical-feature reference handles [details](./TASK-DETAIL.md#task-sqd-p1-02-tactical-feature-reference-handles)
- [ ] **TASK-SQD-P1-03** Role / slot assignment primitive (matrix reuse) [details](./TASK-DETAIL.md#task-sqd-p1-03-role--slot-assignment-primitive-allocation-matrix-reuse)
- [ ] **TASK-SQD-P1-04** Phase sequencer with turn-taking (squad-HSM substrate) [details](./TASK-DETAIL.md#task-sqd-p1-04-phase-sequencer-with-turn-taking-squad-hsm-substrate)
- [ ] **TASK-SQD-P1-05** Exposed-slot rotation with burn/reuse [details](./TASK-DETAIL.md#task-sqd-p1-05-exposed-slot-rotation-with-burnreuse)

---

## Phase 2 — Shared awareness + danger-area sensor

**Goal:** The §4 perception merge + the §5 sensor lifecycle.

- [ ] **TASK-SQD-P2-01** `SquadPerceptionMergeSystem` (10 Hz + event-driven) [details](./TASK-DETAIL.md#task-sqd-p2-01-squadperceptionmergesystem-10-hz--event-driven)
- [ ] **TASK-SQD-P2-02** `SquadKnowsContact` Utility input reader [details](./TASK-DETAIL.md#task-sqd-p2-02-squadknowscontact-utility-input-reader)
- [ ] **TASK-SQD-P2-03** `DangerAreaSensor` + `DangerAreaCognitiveBuffer` components [details](./TASK-DETAIL.md#task-sqd-p2-03-dangerareasensor--dangerareacognitivebuffer-components)
- [ ] **TASK-SQD-P2-04** Phase-2 integration test [details](./TASK-DETAIL.md#task-sqd-p2-04-phase-2-integration-test-perception-merge--sensor)

---

## Phase 3 — Maneuver selection (commander-tier Utility)

**Goal:** `ManeuverSelect` as a real `[UtilityDecision]` on the commander; mission orders can force a maneuver via the existing tactical-intent rail.

- [ ] **TASK-SQD-P3-01** Commander-tier Utility scorer pipeline [details](./TASK-DETAIL.md#task-sqd-p3-01-commander-tier-utility-scorer-pipeline)
- [ ] **TASK-SQD-P3-02** Squad-level commander Utility considerations [details](./TASK-DETAIL.md#task-sqd-p3-02-squad-level-commander-utility-considerations)
- [ ] **TASK-SQD-P3-03** Mission-override mapper (force a maneuver) [details](./TASK-DETAIL.md#task-sqd-p3-03-mission-override-mapper-force-a-maneuver)
- [ ] **TASK-SQD-P3-04** `ManeuverSelectDecision` starter-pack (worked example) [details](./TASK-DETAIL.md#task-sqd-p3-04-maneuverselectdecision-starter-pack-worked-example)

---

## Phase 4 — Authority, rotation engine, movement mode

**Goal:** The two-level-by-weight authority model wired through member Utility, the event-driven rotation engine, and the `MovementMode` posture bit.

- [ ] **TASK-SQD-P4-01** `AssignedRole` / `AssignedSlot` member considerations [details](./TASK-DETAIL.md#task-sqd-p4-01-assignedrole--assignedslot-member-considerations)
- [ ] **TASK-SQD-P4-02** Veto detection + broken-rotation recovery [details](./TASK-DETAIL.md#task-sqd-p4-02-veto-detection--broken-rotation-recovery)
- [ ] **TASK-SQD-P4-03** Event-driven rotation engine (hybrid event/timer) [details](./TASK-DETAIL.md#task-sqd-p4-03-event-driven-rotation-engine-hybrid-eventtimer)
- [ ] **TASK-SQD-P4-04** `MovementMode` intent (squad posture bit → Muscle) [details](./TASK-DETAIL.md#task-sqd-p4-04-movementmode-intent-squad-posture-bit--muscle)

---

## Phase 5 — Maneuver catalog (infantry-first, integration tests)

**Goal:** Each catalog entry is a configuration of the five primitives; each doubles as an integration test. Per §11 sequencing: infantry first (8.1, 8.2, 8.3), then 8.4 hill-crest parity, then 8.6 briefer.

- [ ] **TASK-SQD-P5-01** 8.1 Danger-area crossing (canonical infantry case) [details](./TASK-DETAIL.md#task-sqd-p5-01-81-danger-area-crossing-canonical-infantry-case)
- [ ] **TASK-SQD-P5-02** 8.2 Bounding overwatch (open-field + urban variants) [details](./TASK-DETAIL.md#task-sqd-p5-02-82-bounding-overwatch-open-field--urban-variants)
- [ ] **TASK-SQD-P5-03** 8.3 Suppress-and-maneuver [details](./TASK-DETAIL.md#task-sqd-p5-03-83-suppress-and-maneuver)
- [ ] **TASK-SQD-P5-04** 8.4 Hill-crest hull-down rotation (cross-unit parity proof) [details](./TASK-DETAIL.md#task-sqd-p5-04-84-hill-crest-hull-down-rotation-cross-unit-parity-proof)
- [ ] **TASK-SQD-P5-05** 8.6 Briefer entries (stack-and-room-entry + travelling overwatch) [details](./TASK-DETAIL.md#task-sqd-p5-05-86-briefer-catalog-entries-stack-and-room-entry--travelling-overwatch)

---

## Phase 6 — Three-way authoring shells

**Goal:** Squad HSM (preferred default) + Blueprint + dedicated-script parity (§7).

- [ ] **TASK-SQD-P6-01** Squad HSM authoring shell (FastHSM) [details](./TASK-DETAIL.md#task-sqd-p6-01-squad-hsm-authoring-shell-fasthsm)
- [ ] **TASK-SQD-P6-02** Blueprint host for squad logic [details](./TASK-DETAIL.md#task-sqd-p6-02-blueprint-host-for-squad-logic)
- [ ] **TASK-SQD-P6-03** Dedicated-script path (parity with `PlatoonHillAttack`) [details](./TASK-DETAIL.md#task-sqd-p6-03-dedicated-script-path-parity-with-platoonhillattack)

---

## Phase 7 — Debug & overlays

**Goal:** The squad coordination overlay extends `SquadAssignmentOverlaySource` and surfaces the new maneuver state (§10). Depends on Utility AI Phase-4 overlays.

- [ ] **TASK-SQD-P7-01** `SquadCoordinationOverlaySource` (element coloring, role/slot, OBB+Z danger area) [details](./TASK-DETAIL.md#task-sqd-p7-01-squadcoordinationoverlaysource-extends-squadassignmentoverlaysource)
- [ ] **TASK-SQD-P7-02** Assignment-vs-actual divergence lines + veto labels [details](./TASK-DETAIL.md#task-sqd-p7-02-assignment-vs-actual-divergence-lines--veto-labels)
- [ ] **TASK-SQD-P7-03** Phase + dwell timer + merged contact-pool markers [details](./TASK-DETAIL.md#task-sqd-p7-03-squad-hsm-phase--dwell-timer--merged-contact-pool-markers)
