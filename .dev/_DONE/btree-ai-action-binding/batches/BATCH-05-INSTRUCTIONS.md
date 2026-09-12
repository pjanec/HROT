# BATCH-05: Slice 1 DEMO GATE (S1-G) + live inspector wiring
**Tasks:** S1-G (+ DEBT-AIB-009, DEBT-AIB-012)   **Phase:** Slice 1 (capstone)   **Est:** ~16h
**Dependencies:** BATCH-01..04 (S1-0…S1-5, S1-2b all landed). This proves the whole Slice-1 pipeline end-to-end.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `docs/blueprints/BTree_AiActionParameterBinding_Detailed_Design.md` ("AIB-DD") §5 (demo) + §3.
3. `.dev/_DONE/btree-ai-action-binding/SLICE1-DESIGN.md` §5 (demo specs).
4. `.dev/_DONE/btree-ai-action-binding/TASK-DETAIL.md` §S1-G — the gate's named proof tests + assertions. **Implement exactly; do not invent acceptance criteria.**
5. `.dev/_DONE/btree-ai-action-binding/DEBT-TRACKER.md` — DEBT-AIB-009 (hardcoded-DTO panel wiring) and DEBT-AIB-012 (inspector multi-DTO read) are folded into this batch.

Use codebase-memory MCP graph tools FIRST. `read_file` only for exact edit targets.
**Complete tasks in sequence; do NOT start the next until the current is done, its tests written, and ALL tests (incl. prior batches') pass.**

## Verified end-to-end harness (mirror this — do not invent a new one)
`Hrot.AiEditor.Generators.Tests/Bridge/BlueprintRegistrarBridgeIntegrationTests.cs` — `BTree_SampleScout_Bridge_Register_TreeIsTickable_Body` (~l.337-389) does the full pipeline: load `.btree.json` → `GenerateBTreeSources(json,name)` (topology+bridge) → `CompileMultiAndLoad` (in-mem PE into collectible ALC) → `ScanForRegistrars` → invoke `Register` with an injected `ActionRegistry<BrainBlackboard,BTreeContext>` (build it via `BTreeActionRegistryFactory.BuildFromAssembly` over the assembly carrying the `[FbtRegistrar]`, OR construct + register the demo thunks) → `def.BTreeInterpreter.Tick(ref bb, ref state, ref ctx)`. Helpers: `GenerateBTreeSources`, `CompileMultiAndLoad`, `CreateCoordinator`, `ScanForRegistrars` already in that test file — reuse them.
- Committed demo assets live in `Hrot/Subsystems/Hrot.AI.Behaviors/Assets/BTrees/Authoring/*.btree.json`. `BehaviorParameters` is the `fixed byte[100]` at offset 0 of `BrainBlackboard`; project a DTO at offset O via `Unsafe.As<byte,TDto>(ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)O))` (see `MultiActionBoundTests.cs`).
- **Defaults:** check whether managed-asset variable defaults (`DefaultValueJson`) are written into `BehaviorParameters` at assignment (search for a generated/managed `ParseParams`). If NOT, the proof test must SEED the needed values (e.g. `Threshold`) by writing the DTO at its packed offset before ticking — and you must record "managed-asset default-writing not implemented" as a new debt item (DEBT-AIB-013) in DEBT-TRACKER.

---

## Task 1: Demo nodes + assets + proof tests (S1-G — the GATE)

### 1a. Demo nodes — file: `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/DemoCounterNodes.cs` (UPDATE)
Add a **second, distinct** stateless DTO + action so the demo binds ≥2 different DTO types at distinct offsets:
- Keep `DemoCounterParams {int Counter; int Threshold}` + `Condition_CounterBelowThreshold` + `Action_IncrementCounter`.
- Add e.g. `DemoAccumParams {int Sum; int Step}` + `[BTreeAction] Action_AddStepToSum(ref DemoAccumParams, ref BehaviorTreeState, ref BTreeContext)` that does `Sum += Step; return Success;` (Step defaults seeded by test). All side-effect-free, blittable, sequential.

### 1b. Demo assets (NEW) — `Hrot/Subsystems/Hrot.AI.Behaviors/Assets/BTrees/Authoring/`
- `T10_MultiAction.btree.json` — managed blackboard with **two** variables of **different** DTO types: `counter : Hrot.AI.Behaviors.Brains.DemoCounterNodes+DemoCounterParams` and `accum : Hrot.AI.Behaviors.Brains.DemoCounterNodes+DemoAccumParams` (use the `+` nested FullName form — BATCH-03 validator normalizes it). Tree: a `Sequence` with a gating `Condition_CounterBelowThreshold` (bound to `counter`), a `Repeater`/decorator over `Action_IncrementCounter` (bound to `counter`), and `Action_AddStepToSum` (bound to `accum`). Confirm it compiles cleanly (no BTREE0002) once authored — the validator requires the variable type FQN to equal each method's param-0 type.
- `T11_Aliasing.btree.json` — two `Action_IncrementCounter` nodes both bound to the **same** `counter` variable (aliasing → both write the same bytes).
- Author these to round-trip through `BTreeJsonServices` (mirror an existing committed `T0x` asset's JSON shape; managed Blackboard block like `T09` but with the struct-DTO variable types and real bindings).

### 1c. Proof tests (NEW) — `T10_MultiAction_ProofTests` (mirror `Hrot.Blueprints.Tests/Demos/CountingDemo_ProofTests.cs` in spirit; use the end-to-end BTree harness above). Place in `Hrot.AiEditor.Generators.Tests` (or the project where the harness helpers live) so you can reuse `GenerateBTreeSources`/`CompileMultiAndLoad`.
- `MultiAction_AfterNTicks_CounterReachesThresholdThenConditionFails` — seed `Threshold=N`; tick the interpreter through the Repeater until the gate; assert `counter.Counter` climbs to `N` then the bound condition returns `Failure` (Sequence stops). Read back via `Unsafe.As` at the **packed offset** for `counter` (compute the same way the generator does, or read the emitted offset).
- `MultiAction_SecondDtoMutatesIndependently` — assert `accum.Sum` advances by `Step` per tick at ITS offset, and the `counter` DTO bytes are unaffected by the accum action (and vice-versa) — no cross-talk.
- `Aliasing_TwoNodesShareOneVariable` (T11) — after a tick, `counter.Counter` reflects BOTH increment nodes (e.g. +2 per tick); both nodes resolve to the same offset (same byte slice; zero-copy).

**Gate build:** `dotnet build-server shutdown` then clean rebuild of `Hrot.AI.Behaviors` = 0 errors; byte-identity gate (`Hrot.AiEditor.Persistence.Tests`) green; the 2 known `MigrationEquivalenceTests` excepted.

## Task 2: Live inspector wiring (DEBT-AIB-009 + DEBT-AIB-012)
Make the live Variables panel / blackboard inspector actually reflect the managed multi-DTO blackboard (so the manual gate check is meaningful).
- **DEBT-AIB-012 (multi-DTO read):** `BrainBlackboardRenderer` (editor) currently projects `BehaviorDefinition.ParamsDtoType` at offset 0 only (`RenderTypedDto`/`Marshal.PtrToStructure`). Add managed-variable metadata to `BehaviorDefinition` (e.g. `IReadOnlyList<(string Name, Type Type, int ByteOffset)> ManagedBlackboardVariables`), have the per-asset bridge/registrar populate it (the bridge already computes the packed offsets via `BTreeBlackboardPackHelper`), and extend the renderer to iterate variables and project each at its `ByteOffset`. When the list is null/empty, keep the existing offset-0 behavior (no regression).
- **DEBT-AIB-009 (hardcoded-DTO read-only reflection):** wire the live Variables-panel render path (`BlackboardAuthoringWindow.cs:375` call site) to pass the `IActionSchemaExporter` + the asset's bound action FQNs into `BuildViewModel`, so `HardcodedDtoFields` is populated in the live editor, and render those rows read-only.

**Tests:**
- `BrainBlackboardRenderer_MultiVariable_ProjectsEachAtItsOffset` (in the editor renderer test project) — given a `BehaviorDefinition` carrying two managed variables at offsets O1/O2 and a `BrainBlackboard` with distinct bytes, assert the renderer view-model/output reflects each variable's value from its own offset (headless: assert the projected values, not ImGui pixels — mirror how existing renderer tests assert).
- `VariablesPanel_LiveRenderPath_PassesExporterAndBoundFqns` — assert the render call path now supplies the exporter + bound FQNs so `HardcodedDtoFields` is non-empty for an asset binding a hardcoded action (extend the BATCH-01 VM tests to the real call site).

## Success Criteria
- [ ] S1-G demo nodes + `T10`/`T11` assets authored; both compile (no BTREE0002); blob/registrar carry distinct packed offsets for the two DTO types.
- [ ] All three proof tests pass via the end-to-end (load json → generate → compile → register → tick) harness with real cross-talk/aliasing assertions.
- [ ] Live inspector: multi-variable projection + hardcoded-DTO reflection wired with tests.
- [ ] Clean rebuild 0 errors; byte-identity green; full relevant suites 0 net-new failures (2 MigrationEquivalence excepted); report submitted.
- [ ] **Document the manual/visual check** in the report (what to open in the editor: the managed DTO fields in `BrainBlackboardRenderer`, the active node in `BTreeVisualizerRenderer`) — the lead will hand this to the user.

Run all tests and fix root causes to completion **without asking permission**. Only stop on a breaking design↔codebase contradiction (describe it in the report).

## Report Requirements (`.dev/_DONE/btree-ai-action-binding/reports/BATCH-05-REPORT.md`)
Answer: whether managed-asset defaults are auto-written or you seeded them (and DEBT-AIB-013 if applicable); how you computed/obtained the packed offsets in the proof tests; the exact T10/T11 tree structure authored; how the bridge populates `ManagedBlackboardVariables`; any renderer changes + how headless tests assert projection; weak points; edge cases; the manual visual-check steps for the user; suggested commit message. Do NOT ask comprehension questions.
