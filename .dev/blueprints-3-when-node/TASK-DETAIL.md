# TASK-DETAIL — When-Node Reactivity Iteration

Per-task specifications for the When-Node iteration. All design rationale, code sketches,
test specifications, validator diagnostic codes, lowering templates, and editor mockups
live in [When_Reactivity_Iteration_Design_v2_2.md](./When_Reactivity_Iteration_Design_v2_2.md)
(referred to below as **DESIGN**). This document is intentionally thin: each task is a
pointer into the relevant DESIGN sections plus an explicit scope-in/scope-out cut, mandatory
constraints, and the named tests from DESIGN §15 that must be green for the task to be done.

**Cross-document references:**
- DESIGN §N = section N of `When_Reactivity_Iteration_Design_v2_2.md`
- EQS-DETAIL = `../eqs-2/TASK-DETAIL.md`
- PIC = `../../docs/Predicate-Infrastructure-Capabilities.md`

Task IDs are `WHEN-Mn-Tk` (milestone n, task k), matching the milestone breakdown in DESIGN §16.

---

## Phase M0 — Engine-side coordination

Goal per DESIGN §16 M0: confirm engine APIs and unblock the EQS-side deliverables this
iteration depends on.

### WHEN-M0-T1 — Confirm EQS-side schema deliverables are scheduled

**Design reference:** DESIGN §11 (entire section), §1.10 notes 2, 4, 6; §1.4 row "EQS".

**Scope (IN):**
- Verify the EQS-2 corrective phase has scheduled both:
  - `TASK-EQS-033` — `EqsCognitiveBuffer.LastUpdateTimeSeconds` field (see EQS-DETAIL).
  - `TASK-EQS-037` — `EqsSensorHandle` wrapper struct in `FDP.Eqs` (see EQS-DETAIL).
- Confirm the data ownership choice (both deliverables landed in EQS-2 per their TASK-TRACKER).
- Confirm `view.IsAlive(Entity)` exists exactly as named (architect-confirmed; spot-check
  via codebase grep).
- Confirm `EqsResult` field naming: `EntityId` (long), `PositionX`/`PositionY` (float)
  (architect-confirmed; spot-check via DESIGN §1.10 note 6 + the `EqsResult` struct in
  `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs`).
- Confirm `IEntityCommandBuffer.CreateEntity()` + `AddComponent<T>(Entity, T)` exist
  (engine convention).

**Scope (OUT):** Implementing those EQS items — they belong to EQS-2.

**Success conditions:**
1. Reference matrix in DESIGN §1.4 reconciled against EQS-2 task tracker (no orphaned
   "needs scheduling" rows).
2. Brief written confirmation (one paragraph in this iteration's batch log) that all five
   API points hold against the current `main`.

---

## Phase M1 — Schema and validator

Goal per DESIGN §16 M1: three new node kinds deserialize cleanly and the validator emits
the expected diagnostics. Backed by DESIGN §2 (schema) and §4 (validator).

### WHEN-M1-T1 — `EqsSensorHandle` consumed (no implementation here)

**Design reference:** DESIGN §2.1, §11.2.

**Scope (IN):** Add the using/imports referencing `FDP.Eqs.EqsSensorHandle` in the Blueprint
namespaces that will produce/consume it (`WhenNode`, `ReadEqsResultNode`,
`SpawnEqsSensorNode` payload types and the variable-type registry). No struct definition
here — see EQS-DETAIL `TASK-EQS-037`.

**Scope (OUT):** Editor variable-picker filtering on the type — see WHEN-M5-T2.

**Success conditions:**
1. The three node classes (T2 below) reference `FDP.Eqs.EqsSensorHandle` from `using` directives.
2. The variable-type registry includes `EqsSensorHandle` as a permitted Blueprint variable
   type. Add a test `EqsSensorHandle_IsPermittedVariableType` asserting this.

### WHEN-M1-T2 — `WhenNode`, `ReadEqsResultNode`, `SpawnEqsSensorNode` schema classes

**Design reference:** DESIGN §2.2, §2.3, §2.4, §2.5, §2.6, §2.7, §2.8, §2.9.

**Scope (IN):**
- Add the three concrete `Node` subclasses with exact field shapes per DESIGN §2.3, §2.4,
  §2.5 (`WhenMode`, `WhenEdge`, all four payload classes for the modes, `EventTargetFilter`,
  `PayloadCondition`, `EqsTriggerKind`, `EqsSensorVariableRef`, etc.).
- Add the three `[JsonDerivedType]` entries to the polymorphic `Node` base per
  DESIGN §2.2.
- Update `BlueprintJsonServices` (or equivalent) so the new types round-trip through the
  asset JSON pipeline.
- Update the "where new nodes are allowed" matrix (DESIGN §2.6).
- Author the identity/editor metadata (DESIGN §2.9 — `NodeKindRegistry` entries with
  display names + palette categories).

**Scope (OUT):** Validator rules (T3, T4, T5); IR / lowering (M2 onwards); drawer (M5).

**Constraints:**
- Field names, casing, and JSON discriminator strings must match DESIGN §2.2 exactly.
  These cross the asset persistence boundary; renaming after merge is a destructive
  schema migration.
- `WhenNode` mode-radio + per-mode payload class shape is non-negotiable (DESIGN §1.5
  rationale). Do not collapse the per-mode payload classes into a single bag.

**Success conditions:**
1. All three classes deserialize and re-serialize via the existing
   `BlueprintJsonServices` round-trip tests.
2. The discriminator strings (`"When"`, `"ReadEqsResult"`, `"SpawnEqsSensor"`) survive a
   round-trip.
3. Compile: solution builds.

### WHEN-M1-T3 — `WhenNode` validator (Stage 2 diagnostics `BP20xx`)

**Design reference:** DESIGN §4.1, §4.4.

**Scope (IN):** Implement every `BP20xx` diagnostic enumerated in DESIGN §4.1. Each
diagnostic id, message text, and triggering condition is fully specified there.

**Success conditions:**
1. `Hrot.Blueprints.Tests/Compiler/WhenNodeValidatorTests.cs` per DESIGN §15.2 passes
   (same coverage as v2, plus the dispatch test for AiPrimitive hosting).

### WHEN-M1-T4 — `ReadEqsResultNode` validator (`BP2020`, `BP2021`)

**Design reference:** DESIGN §4.2.

**Success conditions:**
1. `Hrot.Blueprints.Tests/Compiler/ReadEqsResultValidatorTests.cs` per DESIGN §15.2 passes.

### WHEN-M1-T5 — `SpawnEqsSensorNode` validator (`BP2030`, `BP2031`)

**Design reference:** DESIGN §4.3.

**Success conditions:**
1. `Hrot.Blueprints.Tests/Compiler/SpawnEqsSensorValidatorTests.cs` per DESIGN §15.2 passes
   (the two named tests: `Validate_UnsupportedDispatch_BP2030`,
   `Validate_TemplateNotFound_BP2031`).

---

## Phase M2 — `WhenNode` Value Changed and Event Fired lowering

Goal per DESIGN §16 M2: Instance Blueprints with Value Changed or Event Fired WhenNodes
compile and run.

### WHEN-M2-T1 — `WhenIrNode` IR primitive + payloads

**Design reference:** DESIGN §5.2 (table row "WhenIrNode"), §5.4 (synthesized previous-state
field), §5.5 (StructureHash contribution).

**Scope (IN):**
- Add `WhenIrNode` to the IR class hierarchy with the per-mode payload variants enumerated
  in DESIGN §5.2.
- Update the validator → IR mapping (Stage 4 of the compiler pipeline) for `WhenNode`.
- Implement the synthesized-field placement rule (DESIGN §5.4): one `_when_<id>_prev`
  field per WhenNode in the Instance's `State` struct, with shape determined by mode.
- Update StructureHash computation to include synthesized fields (DESIGN §5.5).

**Scope (OUT):** The actual code-emit (T2, T3); modes other than Value Changed and Event
Fired (M3 and M4).

**Constraints:**
- Synthesized-field shapes per-mode are listed in DESIGN §5.4 and §6.9. Honour the
  cumulative-budget table (§6.9) — the placement algorithm must guarantee deterministic
  ordering across WhenNode IDs so StructureHash is stable.

**Success conditions:**
1. Unit test in `WhenNodeLoweringTests.cs`: `Lower_StructureHashIncludesSynthesizedFields`
   per DESIGN §15.1.

### WHEN-M2-T2 — Value Changed mode — Stage 6 lowering

**Design reference:** DESIGN §3.2 (UX), §5.6, §7.1.

**Scope (IN):**
- Stage 6 emission per DESIGN §7.1 (full lowered-code listing there).
- Handle all three `ValueChangedSource` variants (`SelfComponent`,
  `PeerBlueprintVariable`, `WorkingStateField`) per DESIGN §2.3 + §7.1.
- Scalar vs Vector2 epsilon comparison emission per DESIGN §7.1 (`LengthSquared` +
  `epsilon-squared` constant for Vector2).

**Success conditions (named tests from DESIGN §15.1 `WhenNodeLoweringTests.cs`):**
1. `Lower_ValueChanged_Scalar_EmitsInlineComparison`.
2. `Lower_ValueChanged_Vector2_EmitsLengthSquaredComparison`.
3. `Lower_ValueChanged_PeerVariable_EmitsSlotLookup`.

### WHEN-M2-T3 — Event Fired mode — Stage 6 lowering

**Design reference:** DESIGN §3.3, §5.6, §7.2.

**Scope (IN):**
- Stage 6 emission per DESIGN §7.2 (full lowered-code listing there).
- `Self` target filter and optional `PayloadCondition` emission per DESIGN §2.3.
- `bus.HasEvent<T>()` fast-path when no filters present.

**Success conditions (named tests from DESIGN §15.1):**
1. `Lower_EventFired_WithSelf_EmitsTargetCheck`.
2. `Lower_EventFired_WithPayloadCondition_EmitsValueParse`.
3. `Lower_EventFired_NoFilters_EmitsHasEventFastPath`.
4. `Lower_EventFired_NoSynthesizedField` (state struct has no `_when_<id>_prev` for this
   mode).

### WHEN-M2-T4 — Value Changed and Event Fired runtime tests

**Design reference:** DESIGN §15.3 `WhenNodeRuntimeTests.cs`.

**Scope (IN):** All v2 Value Changed and Event Fired runtime tests (per DESIGN §15.3 list).

**Scope (OUT):** EQS-related runtime tests (M4); Condition Met runtime tests (M3).

**Success conditions:** Named tests for Value Changed and Event Fired pass (the rising-edge,
falling-edge, both-edge, peer-variable, working-state, target-filter, and
payload-condition cases).

---

## Phase M3 — Condition Met + predicate-compiler integration

Goal per DESIGN §16 M3: Condition Met `WhenNode` compiles, runs, and survives hot-reload.

### WHEN-M3-T1 — `ConditionMetIrPayload` + Stage 6 lowering

**Design reference:** DESIGN §3.4, §7.3.

**Scope (IN):**
- `ConditionMetIrPayload` per DESIGN §5.2.
- Stage 6 emission per DESIGN §7.3 (full lowered-code listing).
- The static `_whenCondPred_<id>` field + `InitializePredicates` method emission per
  DESIGN §7.3.
- Both rising-edge and falling-edge branches when `Edges = RisingEdge | FallingEdge`.

**Constraints:**
- The bridge to `IPredicateCompiler` is the architectural Open-Closed boundary documented
  in PIC §3. Do **not** duplicate or re-implement predicate compilation; consume the
  existing `IPredicateCompiler.CompileComponentPredicate(SearchPredicateDto)` API
  unchanged (PIC §3.1).

**Success conditions (named tests from DESIGN §15.1):**
1. `Lower_ConditionMet_EmitsStaticDelegateField`.
2. `Lower_ConditionMet_RisingFallingBoth_BothBranchesEmitted`.

### WHEN-M3-T2 — `AiHotReloadCoordinator.DrainPendingCallbacks` extension

**Design reference:** DESIGN §7.4 (Registrar wiring for Condition Met), §10.

**Scope (IN):**
- Extend `AiHotReloadCoordinator.DrainPendingCallbacks` (or equivalent ALC-swap callback
  surface) to also re-bind `IPredicateCompiler` + `ISearchPredicateRegistry` for the
  freshly-loaded assembly, per DESIGN §7.4.
- On hot-reload, all `_whenCondPred_<id>` static fields in the new ALC must be re-bound
  via `InitializePredicates`.

**Constraints:**
- Hot-reload semantics follow DESIGN §10: pure code edits → Soft Reload; structure
  changes → Hard Reload. The `ConditionMet` predicate edit is a Soft-Reload case (new
  delegate on next tick).

**Success conditions:**
1. Unit test: pump a Condition Met `WhenNode`; edit only the predicate DTO; trigger Soft
   Reload; assert the delegate is replaced and the new predicate evaluates correctly
   on the next tick.
2. `Hrot.Blueprints.Tests/HotReload/WhenNodeHotReloadTests.cs` →
   `EditWhenNodePredicate_SoftReload_DelegateRecompiled` per DESIGN §15.7.

### WHEN-M3-T3 — Condition Met runtime tests + degraded-mode safety

**Design reference:** DESIGN §15.3 (Condition Met runtime tests), §15.7
(`BadPredicateAfterReload_DegradedMode_NoCrash`).

**Success conditions:**
1. Condition Met named runtime tests pass.
2. `BadPredicateAfterReload_DegradedMode_NoCrash` (DESIGN §15.7) passes — invalid
   predicate after reload yields `null` delegate; the WhenNode no-ops; no crash.

---

## Phase M4 — EQS Result mode + `ReadEqsResultNode` + `SpawnEqsSensorNode`

Goal per DESIGN §16 M4: all three EQS-related lowerings work against mock scenarios with
child-entity hosting and zero allocations on the hot path.

**Hard dependency: M0 and the corresponding EQS-2 corrective tasks (EQS-DETAIL TASK-EQS-033,
TASK-EQS-037) must be available in the working branch before M4 can compile.** The
runtime guards (DESIGN §6.1, §6.2) reference these directly.

### WHEN-M4-T1 — EQS Result mode — common scaffolding

**Design reference:** DESIGN §6.1 (non-negotiable buffer-access pattern), §6.2 (child-entity
read pattern), §6.3 (epoch-gating rule), §6.4 (trigger semantics), §6.9 (per-trigger state
struct sizes), §6.10 (diagnostic annotations).

**Scope (IN):**
- `EqsResultIrPayload` per DESIGN §5.2.
- The common pre-flow used by every EQS trigger: read `EqsSensorHandle` from the variable
  slot → `view.IsAlive(handle.ChildId)` guard → `view.GetComponentRO<EqsCognitiveBuffer>(
  handle.ChildId)` → `buffer.GetSpanRO()` access pattern (DESIGN §6.1, §6.2).
- Per-trigger state-struct skeleton selection per DESIGN §6.9.
- Diagnostic annotations per DESIGN §6.10 (compiler emits `// trigger: …` and
  `// child-entity read` comments in generated code; useful for debugging).

**Constraints (non-negotiable, restated from DESIGN §6.1):**
- **NEVER** index directly into `buffer.Results[i]` — always go through `GetSpanRO()` to
  avoid the C# 12 `[InlineArray]` `ldobj` defensive-copy trap.
- **ALWAYS** guard `view.IsAlive(handle.ChildId)` before any read of the child's
  components. The child can be destroyed by `SubEntityCleanupSystem` at any
  `PostSimulation` boundary if its parent dies.

**Success conditions (DESIGN §15.1 `WhenNodeLoweringTests.cs`):**
1. `Lower_EqsResult_UsesChildEntityRead`.
2. `Lower_EqsResult_LivenessGuardPrecedesReads`.
3. `Lower_EqsResult_TopChanged_UsesGetSpanRO` (trigger-specific but exercises the common
   read pattern).

### WHEN-M4-T2 — EQS Result mode — FirstReady, TopChanged, ScoreCrossed, BecomesStale triggers

**Design reference:** DESIGN §6.4 (trigger semantics), §6.5 (canonical TopChanged
lowering), §6.6 (FirstReady), §6.7 (ScoreCrossed), §6.8 (BecomesStale).

**Scope (IN):** Stage 6 emission for each of the four triggers, using the lowered-code
templates in DESIGN §6.5–6.8. Each trigger's state-struct shape per DESIGN §6.9.

**Constraints:**
- TopChanged: emit the positional-vs-entity hash branch per DESIGN §6.5 / §1.10 note 6 —
  `top.EntityId != 0L ? top.EntityId : HashCode.Combine(top.PositionX, top.PositionY)`.
- TopChanged: first check is the epoch comparison (DESIGN §6.3, §6.5).
- BecomesStale: time basis is `time - buffer.LastUpdateTimeSeconds`, NOT
  `currentTick - LastUpdateTick` (DESIGN §6.8, §1.10 note 4). BecomesStale is the only
  trigger that is NOT epoch-gated (DESIGN §6.8).
- ScoreCrossed: threshold is emitted as `const float _whenScoreThreshold_<id>`
  (DESIGN §6.7).

**Success conditions (DESIGN §15.1):**
1. `Lower_EqsResult_TopChanged_EpochGated`.
2. `Lower_EqsResult_PositionalHash_OnTheFly`.
3. `Lower_EqsResult_FirstReady_DistinctStateStruct` (smaller 4-byte struct).
4. `Lower_EqsResult_ScoreCrossed_EmitsConstThreshold`.
5. `Lower_EqsResult_BecomesStale_UsesSimTime`.
6. `Lower_EqsResult_BecomesStale_NotEpochGated`.

### WHEN-M4-T3 — `ReadEqsResultNode` lowering

**Design reference:** DESIGN §2.4, §3.6, §7 (the §7.x for ReadEqsResultNode — see TOC for
exact subsection; full lowered-code listing there).

**Scope (IN):**
- `ReadEqsResultIrNode` per DESIGN §5.2.
- Helper-method emission (`ReadEqsResult_<id>` static method + `EqsResultRead_<id>`
  struct) per DESIGN §15.1 `Lower_EmitsHelperMethod`.
- Result-caching when multiple consumers in the same graph read the same sensor + index
  (per `Lower_SharedReadCaching` in DESIGN §15.1).
- Index clamping per `Lower_ClampsIndex` (`Math.Clamp(resultIndex, 0, results.Length - 1)`).
- Liveness guard precedes the buffer read.
- **Failure-path return shape (non-negotiable).** If the liveness guard
  `view.IsAlive(handle.ChildId)` fails — or if the child exists but
  `view.HasComponent<EqsCognitiveBuffer>(handle.ChildId)` returns false — the emitted
  helper method must return a `default(EqsResultRead_<id>)` shaped struct with
  `IsReady = false`, `ResultCount = 0`, and every other field (`Entity`, `Position`,
  `Score`) zero/default. The helper must **never** throw or propagate a "missing
  component" exception to its caller — callers downstream of `ReadEqsResultNode`
  branch on `IsReady` and rely on the safe-zero contract.

**Constraints:**
- Zero contribution to StructureHash (`Lower_ZeroStateContribution`) — `ReadEqsResultNode`
  is a pure read; it has no synthesized state field.
- The failure-path branch must short-circuit *before* any component read; do not call
  `GetComponentRO<EqsCognitiveBuffer>` after a failed liveness check.

**Success conditions (DESIGN §15.1 `ReadEqsResultLoweringTests.cs`):**
1. `Lower_EmitsHelperMethod`.
2. `Lower_ClampsIndex`.
3. `Lower_LivenessGuard`.
4. `Lower_SharedReadCaching`.
5. `Lower_ZeroStateContribution`.
6. **(new)** `Lower_LivenessGuardFails_ReturnsSafeDefault` — emit a graph where the
   sensor variable holds an `EqsSensorHandle` pointing at an already-destroyed child
   entity; assert the helper returns `IsReady == false`, `ResultCount == 0`, and all
   other fields zeroed; assert no exception propagates.
7. **(new)** `Lower_BufferComponentMissing_ReturnsSafeDefault` — child entity exists but
   has no `EqsCognitiveBuffer` (e.g., spawned but ECB playback for the buffer attachment
   hasn't run yet); same assertion shape as #6.

### WHEN-M4-T4 — `SpawnEqsSensorNode` lowering

**Design reference:** DESIGN §1.8, §2.5, §2.8 (pin layout), §3.7, §6.11 (interaction with
EQS Result mode), §7.8 (lowering subsection — confirm exact §).

**Scope (IN):**
- `SpawnEqsSensorIrNode` per DESIGN §5.2.
- Stage 6 emission: `ecb.CreateEntity()` → `ecb.AddComponent<PartMetadata>(...)` →
  `ecb.AddComponent<EqsSensor>(...)` → `ecb.AddComponent<EqsCognitiveBuffer>(...)` →
  `EqsSensorHandle` constructor with the new child entity (output pin).
- Attachment order is non-negotiable: `PartMetadata` BEFORE `EqsSensor` and
  `EqsCognitiveBuffer` (per `Lower_AttachmentOrder` in DESIGN §15.1).
- Pin handling: wired pin emits the upstream expression; unconnected pin emits the
  editor literal default (DESIGN §15.1 `Lower_WiredPin_EmitsUpstreamExpression`,
  `Lower_UnconnectedPin_EmitsLiteralDefault`).
- All five universal `EqsSensor` parameter fields assigned per `Lower_AllFiveFieldsAssigned`
  (SearchRadius, FactionFilter, ThreatThreshold, PublishPolicy, Priority).
- `BlueprintId` derived from the chosen template's registered hash per
  `Lower_TemplateBlueprintId_FromTemplateAssetId`.
- **`EqsSensor.Epoch = 1` initialization (non-negotiable).** The lowering must emit
  `Epoch = 1` inside the `EqsSensor` initializer. Default-zero `Epoch` would (a) cause
  the EQS solver's epoch comparison logic to misbehave on the first evaluation and
  (b) make every subsequent `Action_MaintainEqsSensor`-style parameter mutation
  indistinguishable from "freshly spawned never-evaluated" state. `Epoch = 1` is the
  conventional initial value used by `EqsLifecycleNodes.Action_MaintainEqsSensor` (see
  the BTree counterpart in `EqsLifecycleNodes.cs`) and must be matched here.
- **Deterministic `PartMetadata.InstanceId` (non-negotiable).** The lowering must emit
  a stable, per-spawn-node integer for `InstanceId`, NOT leave it at default `0`. Per
  EQS-DETAIL `TASK-EQS-038`, `PartMetadata.InstanceId` is the `LocalChildIndex` that
  forms half of the DDS replication key `(ParentNetworkId, LocalChildIndex)`. Two
  `SpawnEqsSensorNode`s on the same agent with `InstanceId = 0` would share the same
  DDS topic key — the second spawn would silently overwrite the first sensor's
  configuration on the wire. Recommended derivation:
  `(int)node.Id.GetHashCode()` (the node id is stable across Soft Reloads per the
  Blueprint asset model; if `node.Id` is a `Guid`, hashing it yields a 32-bit value
  with vanishingly small collision risk across the spawn nodes in a single Blueprint
  asset). The compiler emits the literal:
  ```csharp
  ecb.AddComponent(child, new PartMetadata {
      ParentEntity      = self,
      InstanceId        = <baked-int-from-node-id-hash>,
      DescriptorOrdinal = 0,
  });
  ```
  Different `SpawnEqsSensorNode` instances within the same Blueprint MUST produce
  different `InstanceId` literals (validator should reject hash collisions on Stage 2
  with a new diagnostic, e.g. `BP2032 SpawnEqsSensor_InstanceIdCollision`, in the
  rare-but-not-impossible birthday case).

**Constraints:**
- **All entity creation and component attachment must go through `IEntityCommandBuffer`**
  (`ecb.CreateEntity` / `ecb.AddComponent<T>`) — never direct repo mutation. Blueprint
  Tick graphs run during `SystemPhase.Simulation`; direct structural mutation corrupts
  chunk arrays. Same constraint as EQS-DETAIL TASK-EQS-039.
- The lowering targets the **fixed seven-field** `EqsSensor` shape committed by
  DESIGN §1.10 note 9. **Note:** the EQS-2 corrective phase (TASK-EQS-034, TASK-EQS-035)
  schedules additions of `ScoreDeltaThreshold` and three context-slot Entity handles to
  this struct. When those land, this task gains additional pins; coordinate via the
  cross-iteration coordination note in EQS-DETAIL Phase 10's header. Land this task
  against the current 7-field shape; expand in a follow-up corrective task once
  EQS schema additions ship.
- `EqsSensor.Epoch = 1` (see Scope above) and `PartMetadata.InstanceId =
  <baked-node-hash>` (see Scope above) are mandatory and must be present in the
  emitted code regardless of pin connections — they are compiler-determined, not
  user-authored.
- Zero StructureHash contribution (`Lower_ZeroStateContribution`).

**Success conditions (DESIGN §15.1 `SpawnEqsSensorLoweringTests.cs`):**
1. All eleven named tests from DESIGN §15.1's `SpawnEqsSensorLoweringTests.cs` table pass
   (`Lower_EmitsCreateEntity`, `Lower_EmitsPartMetadataAttach`,
   `Lower_EmitsEqsSensorAttach`, `Lower_EmitsCognitiveBufferAttach`,
   `Lower_EmitsHandleOutput`, `Lower_AttachmentOrder`,
   `Lower_WiredPin_EmitsUpstreamExpression`, `Lower_UnconnectedPin_EmitsLiteralDefault`,
   `Lower_AllFiveFieldsAssigned`, `Lower_TemplateBlueprintId_FromTemplateAssetId`,
   `Lower_ZeroStateContribution`).
2. **(new)** `Lower_EmitsEqsSensorAttach_WithEpochOne` — golden-output test: assert the
   `EqsSensor` initializer literal contains `Epoch = 1`.
3. **(new)** `Lower_PartMetadataInstanceId_IsDeterministicAndNonZero` — compile the same
   `SpawnEqsSensorNode` twice; assert both runs emit the same literal `InstanceId`;
   assert the literal is non-zero.
4. **(new)** `Lower_TwoSpawnNodes_ProduceDistinctInstanceIds` — graph with two
   `SpawnEqsSensorNode`s; assert their emitted `InstanceId` literals differ.
5. **(new)** `Validate_SpawnEqsSensor_InstanceIdCollision_BP2032` — synthesise (via
   crafted node ids) a hash collision between two spawn nodes; assert validator emits
   the new `BP2032` diagnostic and refuses to compile.

### WHEN-M4-T5 — EQS-related runtime tests + inline-array safety

**Design reference:** DESIGN §15.3 (`WhenNodeRuntimeTests.cs` EQS-specific rows,
`ReadEqsResultNodeRuntimeTests.cs`, `SpawnEqsSensorRuntimeTests.cs`), §15.4
(`WhenNodeEqsInlineArrayTests.cs`).

**Success conditions (named tests, DESIGN §15.3 + §15.4):**
1. `EqsResult_FirstReady_FiresOnceOnChildEntity`.
2. `EqsResult_TopChanged_PositionalQueries_HashesPosition`.
3. `EqsResult_BecomesStale_UsesSimTimeNotTicks`.
4. `EqsResult_ChildEntityDestroyed_NoFire_NoCrash`.
5. All v2 ReadEqsResultNode runtime tests (DESIGN §15.3 row "Same coverage as v2 §15.4").
6. All `SpawnEqsSensorRuntimeTests.cs` named tests (DESIGN §15.3 table): nine tests
   covering CreateEntity emission, parent attachment, template id propagation, buffer
   init, handle output, literal & wired parameter binding, multiple-invocation
   distinctness, zero-allocation hot path.
7. Inline-array safety per `WhenNodeEqsInlineArrayTests.cs` (same coverage as v2 §15.5).

---

## Phase M5 — Editor drawers and palette

Goal per DESIGN §16 M5: designers can create, configure, and Quick-Reload all three new
node kinds.

### WHEN-M5-T1 — `WhenNodeDrawer` + `WhenNodeSession`

**Design reference:** DESIGN §3.1, §3.2, §3.3, §3.4, §3.5 (per-mode forms), §3.8 (preview
pills), §8 (full drawer section — confirm exact subsections), §15.5
(`WhenNodeDrawerTests.cs`).

**Scope (IN):**
- Drawer entry-point + `Handles(WhenNode)` predicate.
- Mode-radio top control (Value Changed / Event Fired / Condition Met / EQS Result).
- Per-mode sub-form rendering per DESIGN §3.2–3.5.
- Edge selector (Rising / Falling / Both) per DESIGN §3.1.
- Inline dispatch guard for non-Instance assets (red badge per DESIGN §8 / §14).
- Inline preview pill per DESIGN §3.8.

**Success conditions:** Same tests as v2 §15.6 for `WhenNodeDrawerTests.cs` pass.

### WHEN-M5-T2 — `ReadEqsResultNodeDrawer` + `ReadEqsResultNodeSession`

**Design reference:** DESIGN §3.6, §8 (drawer subsection for ReadEqsResultNode).

**Scope (IN):**
- Drawer entry-point + `Handles(ReadEqsResultNode)`.
- Filtered variable picker — only `EqsSensorHandle`-typed variables (per DESIGN §2.1
  rationale: "Blueprint editor dropdowns filter the asset's variable list to only those
  whose declared type is `FDP.Eqs.EqsSensorHandle`").
- `ResultIndex` input with clamp hint.
- Output pin badges (`IsReady`, `ResultCount`, `Entity`, `Position`, `Score`).

**Success conditions:** Same tests as v2 §15.6 for `ReadEqsResultNodeDrawerTests.cs` pass.

### WHEN-M5-T3 — `SpawnEqsSensorNodeDrawer` + `SpawnEqsSensorNodeSession`

**Design reference:** DESIGN §3.7, §1.10 note 9 (fixed pin layout), §15.5
(`SpawnEqsSensorNodeDrawerTests.cs`).

**Scope (IN):**
- Drawer entry-point + `Handles(SpawnEqsSensorNode)`.
- Template picker (combo populated from `EqsTemplateRegistry`).
- Fixed five-pin layout (SearchRadius, FactionFilter, ThreatThreshold, PublishPolicy,
  Priority) — identical across all templates per DESIGN §1.10 note 9.
- Template switching updates `TemplateAssetId` only; pin set does NOT rebuild
  (`Drawer_TemplateSwitch_UpdatesAssetIdOnly`).
- Pin connections preserved across template switches (`Drawer_PreservesPinConnectionsAcrossTemplateSwitch`).
- Inline dispatch guard for non-Instance assets.

**Constraints:**
- Target size per DESIGN §16 M5 estimate: ~60 lines. Do **not** add dynamic-binding /
  per-template parameter-struct reflection — that was explicitly cut in v2.2 (see DESIGN
  v2.2 status note at the top of the design doc).

**Success conditions (DESIGN §15.5):**
1. All five `SpawnEqsSensorNodeDrawerTests.cs` named tests pass
   (`Drawer_HandlesSpawnEqsSensor`, `Drawer_TemplatePicker_PopulatesFromRegistry`,
   `Drawer_TemplateSwitch_UpdatesAssetIdOnly`,
   `Drawer_PreservesPinConnectionsAcrossTemplateSwitch`,
   `Drawer_DispatchGuard_ShowsForNonInstance`).

### WHEN-M5-T4 — Palette entries + mode-aware edge selector

**Design reference:** DESIGN §2.9 (palette categories), §14.2 (palette category alignment),
§3.1 (edge selector).

**Scope (IN):**
- Palette entry for `WhenNode` under "Reactive Guards" category.
- Palette entries for `ReadEqsResultNode` and `SpawnEqsSensorNode` under "EQS" category.
- Tooltips wired via `ReactiveGuardVocabulary` constants (forward-references M8;
  acceptable to land stub tooltips here and replace text in M8).

**Success conditions:**
1. Boot editor; palette shows the three entries under the correct categories.
2. Drawer/palette tests in the same project assert category strings match
   `ReactiveGuardVocabulary.CategoryName` (or pass against literal strings until M8).

---

## Phase M6 — Visual extensions (NodeAttachments + CustomCanvasRenderer)

Goal per DESIGN §16 M6: canvas shows pills for all three nodes plus dependency badges;
runtime firing pulses work in Debug mode.

### WHEN-M6-T1 — `ConditionSummaryAttachment` + provider (WhenNode)

**Design reference:** DESIGN §9 (full visual section — confirm §9.1 or §9.2 for this
specific attachment).

**Scope (IN):** Inline attachment on `WhenNode` showing the active mode's compact summary
(e.g., "Health < 10 ↑").

**Success conditions:** Visual smoke test in `Hrot.Blueprints.Tests/Editor/` confirms
the attachment registers for `WhenNode` and renders non-empty content for each mode.

### WHEN-M6-T2 — `EqsTemplateAttachment` (SpawnEqsSensorNode) + sensor-name pill (ReadEqsResultNode)

**Design reference:** DESIGN §9.

**Scope (IN):**
- `EqsTemplateAttachment` for `SpawnEqsSensorNode` showing the chosen template name.
- Sensor-name pill on `ReadEqsResultNode` showing the variable name it reads from.

**Success conditions:** Visual smoke tests assert each attachment renders for its
respective node type with correct text.

### WHEN-M6-T3 — `CrossAssetDependencyAttachment` + provider

**Design reference:** DESIGN §9.

**Scope (IN):** Cross-Blueprint dependency badge — when a `WhenNode`'s
`ValueChangedSource = PeerBlueprintVariable`, draw a badge on the node pointing to the
peer asset.

**Success conditions:** Visual smoke test for the badge.

### WHEN-M6-T4 — `WhenFiringPulseRenderer`

**Design reference:** DESIGN §9.

**Scope (IN):** `CustomCanvasRenderer` that overlays a brief visual pulse on a `WhenNode`
when it fires at runtime in Debug mode.

**Constraints:** Renderer must be no-op in Release / non-Debug mode; zero allocations in
the steady state when no fires are occurring.

**Success conditions:** Smoke test: fire a `WhenNode` via the runtime test fixture;
assert the renderer's draw call is invoked once per fire in Debug mode and zero times in
Release mode.

---

## Phase M7 — Behavior Recipes + "New from Recipe…" workflow

Goal per DESIGN §16 M7: all five recipes compile and tick correctly; the New-from-Recipe
dialog produces working copies.

### WHEN-M7-T1 — Author five recipe `.bp.json` files

**Design reference:** DESIGN §12 (full recipe section — confirm the five recipes
enumerated).

**Scope (IN):** Five recipe `.bp.json` files under
`Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Recipes/` per DESIGN §12. Recipe 1
("Cover-aware Patrol") MUST use all three new node kinds wired end-to-end with the
first-tick pattern (DESIGN §12.2).

**Constraints:**
- Each recipe must include `EditorMetadata.Recipe` metadata (Description,
  ConceptsTaught ≥ 2) per `AllRecipes_HaveDescriptionsAndConcepts` (DESIGN §15.6).
- Stable AssetIds — running the build twice must produce identical IDs
  (`AllRecipes_HaveStableAssetIds`).
- Recipe 5 ("SquadAwareEngagement", or whichever recipe DESIGN §12 nominates) must
  cross-reference `SquadState` for the `AllRecipes_CrossReferencesResolve` test.

**Success conditions:** All `RecipeIntegrityTests.cs` named tests pass (DESIGN §15.6:
parse, recipe metadata, validate-only compile, full compile to valid C#, cross-references
resolve, stable AssetIds, descriptions and concepts present,
`CoverAwarePatrol_UsesAllThreeNewNodes`).

### WHEN-M7-T2 — `NewFromRecipeService` + Asset Browser submenu + dialog

**Design reference:** DESIGN §13.

**Scope (IN):**
- `NewFromRecipeService` per DESIGN §13.
- Asset Browser "+ New" submenu entry.
- New-from-Recipe dialog with "(★ recommended for learning)" hint on Recipe 1.

**Success conditions:** Manual smoke: open Asset Browser, "+ New" → "From Recipe…";
choose Recipe 1; assert a new asset is created with the expected node graph and
recipe-metadata stripped.

---

## Phase M8 — Reactive-Guard vocabulary unification + documentation

Goal per DESIGN §16 M8: consistent "Reactive Guards" category across editors; cross-
references resolve.

### WHEN-M8-T1 — `ReactiveGuardVocabulary` string constants + editor wirings

**Design reference:** DESIGN §14.1, §14.2, §14.4.

**Scope (IN):**
- New file `Hrot/Editor/Hrot.Editor.AiShared/ReactiveGuardVocabulary.cs` with the exact
  string constants from DESIGN §14.1 (~40 lines).
- BTree editor: palette label change + two tooltip wiring lines.
- HSM editor: palette label change + two tooltip wiring lines.
- Blueprint editor: palette label change + two tooltip wiring lines (alongside §8.4
  hookup from M5).

**Success conditions:**
1. Compile.
2. Manual smoke: open each editor, hover the reactive-guard palette entry, confirm the
   tooltip matches the corresponding `ReactiveGuardVocabulary` constant.

### WHEN-M8-T2 — `Hrot/Docs/ReactiveGuards.md` author

**Design reference:** DESIGN §14.3.

**Scope (IN):** ~80-line Markdown reference per DESIGN §14.3. Must include the note that
`SpawnEqsSensorNode` and `ReadEqsResultNode` are subsystem-specific (paired with
`WhenNode` but not reactive guards themselves).

**Success conditions:** Document committed; cross-link from the three editor tooltips
(via `ReactiveGuardVocabulary.CrossSubsystemHint*` constants) resolves to the doc.

---

## Phase M9 — End-to-end demo + performance verification

Goal per DESIGN §16 M9: the full pipeline runs cleanly in a real scenario; performance
budgets met.

### WHEN-M9-T1 — `CoverAwarePatrol` end-to-end integration test

**Design reference:** DESIGN §15.9.

**Success conditions (DESIGN §15.9 named tests):**
1. `CoverAwarePatrol_FullScenario` passes.
2. `CoverAwarePatrol_ParentDeath_AutoCleanup` passes.
3. `CoverAwarePatrol_HotReload_SoftReload_PreservesSensor` passes.

### WHEN-M9-T2 — Performance test battery

**Design reference:** DESIGN §15.8.

**Success conditions (DESIGN §15.8 targets):**
1. `WhenNode_ValueChanged_Under100ns_perTick` — < 100 ns avg.
2. `WhenNode_EventFired_Under500ns_perTick` — < 500 ns avg.
3. `WhenNode_ConditionMet_Under200ns_perTick` — < 200 ns avg.
4. `WhenNode_EqsResult_Under150ns_perTick` — < 150 ns avg (epoch unchanged common case).
5. `WhenNode_ZeroAllocOnHotPath` — zero allocations.
6. `ReadEqsResultNode_Under80ns_perInvocation` — < 80 ns avg.
7. `SpawnEqsSensorNode_Under5us_perInvocation` — < 5 µs avg (one-time cost).

### WHEN-M9-T3 — Hot-reload integration battery

**Design reference:** DESIGN §15.7.

**Scope (IN):** Run every named test in `WhenNodeHotReloadTests.cs` end-to-end against
the real `AiHotReloadCoordinator` (not a unit-test mock).

**Success conditions:** All named tests from DESIGN §15.7 pass:
- `AddWhenNode_TriggersHardReload`
- `RemoveWhenNode_TriggersHardReload`
- `EditWhenNodePredicate_SoftReload_DelegateRecompiled`
- `EditWhenNodeMode_HardReload`
- `ValueChangedFieldType_Soft_PreservesPrev`
- `BadPredicateAfterReload_DegradedMode_NoCrash`
- `EqsTriggerChange_HardReload`
- `AddReadEqsResultNode_SoftReload`
- `AddSpawnEqsSensorNode_SoftReload`
- `EditSpawnTemplate_SoftReload_PreservesHandle`

---

## Deferred / out-of-scope

Items explicitly out of scope for this iteration (DESIGN §1.3, §17 Resolutions Summary):
- Push-notification reactivity (architect preferred polling).
- Per-slot version fields on every blackboard slot.
- Cross-entity reactivity beyond the EQS sensor child-entity case.
- EQS template authoring inside the visual graph (kept in hand-written C# with
  `[EqsTemplate]`).
- `WhenNode` hosting in `AiPrimitive` Condition or Action bodies (BTrees and HSMs keep
  their existing reactive primitives).
- Array-output multi-result reads (`ReadEqsResultNode` ships with indexed scalar-output
  shape only).
- BTree-side spawn helper for child sensors (handled separately under EQS-DETAIL
  TASK-EQS-039).
