# BATCH-09: Slice 2 DEMO GATE (S2-G) — multiple stateful primitives, end-to-end
**Tasks:** S2-G   **Phase:** Slice 2 capstone   **Est:** ~12h
**Dependencies:** S2-1, S2-2, S2-3 (BATCH-06/08 committed); S2-4 (BATCH-07).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your contract.
2. `.dev/_DONE/btree-ai-action-binding/TASK-DETAIL.md` §S2-G — the gate + named proof tests + exact assertions.
3. `.dev/_DONE/btree-ai-action-binding/SLICE2-DESIGN.md` §5 (demos).
4. **Existing harness to mirror:** `Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Demos/T10_MultiAction_ProofTests.cs` — it loads a committed `.btree.json`, runs the REAL `BTreeJsonGenerator`, Roslyn-compiles the generated sources, invokes the registrar, and ticks. THIS is the path that compiles the emitted code.
5. **World+partition+provisioning harness to combine with it:** `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/StatefulPrimitiveTests.cs` and `BehaviorIngressStatefulTests.cs` (BATCH-06) — show building a world, registering `BlueprintBlackboard*` tiers, `BTreeContext{Self,World}`, and provisioning via `BehaviorIngressSystem.Execute` on a published `AssignBehaviorEvent`.
6. Codebase-memory MCP first.

## Why this batch matters (closes DEBT-AIB-026)
The S2-1 emitted stateful thunk (`BTreeBridgeEmitCore.EmitStatefulActionThunks`) has NEVER been compiled — no asset uses `ThreeParamReusableStateful`. This gate authors the first such asset, so the **normal `Hrot.AI.Behaviors` codegen build compiles it**, and the proof test ticks the actually-emitted+compiled code through the real provisioning path. **If the emitted thunk has compile errors, fixing the emitter (`BTreeBridgeEmitCore`) is in scope.**

## Tasks (sequence)

### Task 1: Author the T20 demo asset
**File (NEW):** `Hrot/Subsystems/Hrot.AI.Behaviors/Assets/BTrees/Authoring/T20_MultiStateful.btree.json`
**Scope:** a `Managed==true` BTree that uses the **same stateful primitive at two nodes** + ≥1 stateless action. Model on `T10_MultiAction.btree.json`. Concretely:
- Managed blackboard variables: one `DemoCursorParams` variable for node A's params, one for node B's params (or share — your call, but the WORKING state must be independent via distinct node VisualIds), plus one stateless `DemoCounterParams` variable.
- Two `Action` nodes both binding `Hrot.AI.Behaviors.Brains.DemoCounterNodes.Action_AdvanceCursor` with `"DelegateShape": "ThreeParamReusableStateful"`, `"WorkingStateTypeId": "Hrot.AI.Behaviors.Brains.DemoCounterNodes+DemoCursorState"`, distinct `VisualId`s, and `ExpressionTargetField` → their `DemoCursorParams` variable. **Distinct VisualIds ⇒ distinct FNV-1a slot keys ⇒ independent WorkingState slots.**
- One stateless `Action` node binding `Action_IncrementCounter` (`ThreeParamReusable`) → the `DemoCounterParams` variable (proves mixed stateless+stateful).
- Topology: a `Sequence[ AdvanceCursor_A(LimitA), AdvanceCursor_B(LimitB), IncrementCounter ]`. Note `Action_AdvanceCursor` returns `Running` while `cursor < Limit` (incrementing first) and `Success` once reached — so with distinct limits the two cursors advance on independent schedules and diverge; the stateless `IncrementCounter` runs only when both precede it with Success. Set `DefaultValueJson` for the limits (e.g. LimitA=3, LimitB=5) and counter Threshold high. Pick limits so the proof-test arithmetic is clean and document the expected per-tick values in the asset comment.
- Verify the asset round-trips: `Hrot.AiEditor.Persistence.Tests` byte-identity gate stays green for existing assets (T20 is new — it should NOT alter any existing golden).

### Task 2: Make it compile in the normal build (DEBT-AIB-026 closure)
`dotnet build-server shutdown` then clean-rebuild `Hrot/Subsystems/Hrot.AI.Behaviors`. The `BTreeJsonGenerator` will now emit the stateful bridge for T20. **It MUST compile with 0 errors.** If the emitted stateful thunk (or manifest) has C#/type errors, fix `BTreeBridgeEmitCore` (root cause) — do NOT hack the asset to dodge the emitter. Re-run until the project builds clean.

### Task 3: End-to-end proof tests
**File (NEW):** `Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Demos/T20_MultiStateful_ProofTests.cs`
**Scope:** mirror `T10_MultiAction_ProofTests`'s load→generate→compile→register pipeline (this compiles + loads the emitted stateful bridge), THEN — because the stateful thunk projects WorkingState from a partition slot via `ctx.World`/`ctx.Self` — drive the REAL provisioning + tick:
1. Build a world (`TestWorldFactory.Create()`), register `BlueprintBlackboard1024/4096/16384`.
2. Register the compiled T20 `BehaviorDefinition` (which carries the emitted `StatefulWorkingSlots` manifest) into a `BehaviorRegistry`; create a `BehaviorIngressSystem(registry)`.
3. Create an entity with `BehaviorState` + `BrainBlackboard` + `BrainBTreeState`; publish `AssignBehaviorEvent{Entity, BehaviorName="T20_MultiStateful", JsonParams=""}`; `Bus.SwapBuffers()`; `ingress.Execute(world, dt)` — this provisions the two stateful slots (S2-2) from the manifest. Assert both slots are attached (`TryGetSlotOffset`).
4. Tick the registered interpreter with `ctx = new BTreeContext{Self=entity, World=world}` for N ticks (seed limits/threshold manually if `ParseParams` defaults aren't applied in-harness, mirroring T10's note).
5. **Named proof tests (exact assertions):**
   - `TwoStatefulInstances_MaintainIndependentState` — after N ticks, read each node's `DemoCursorState.Cursor` **from its own partition slot** (distinct keys) and assert they advanced on their independent schedules and are NOT equal (no cross-talk — neither slot's bytes affected the other). Assert the values match the per-tick arithmetic of the chosen limits.
   - `MixedStatelessAndStateful_Coexist` — assert the stateless `DemoCounterParams.Counter` (read from `BrainBlackboard.BehaviorParameters` at its packed offset) evolves correctly AND the stateful cursors (read from partition slots) evolve correctly in the same run — disjoint memory, no interference (e.g. cursors advancing does not perturb the counter DTO bytes and vice-versa).
6. ALC unload + `AwaitAlcCollection` as T10 does.

**If the stateful thunk's `ctx.World` access can't be satisfied by the chosen `BTreeContext` construction in the test (e.g. World type mismatch), investigate and resolve — this is the real integration. Only STOP if there's a genuine design contradiction.**

## Global rules
- `dotnet build-server shutdown` before codegen verification.
- Validate with the `Behavior` filter where relevant; the new proof tests live in `Hrot.AiEditor.Generators.Tests` (run that suite — known non-regressions: 2 MigrationEquivalence). Byte-identity gate `Hrot.AiEditor.Persistence.Tests` 129/0 (T20 must not perturb existing goldens). Full Fdp.Toolkits behavior tests via `--filter Behavior` (DEBT-AIB-030: full unfiltered suite is flaky — do not chase those).
- Never weaken a test. Fail loud. Fix emitter root causes. Work autonomously; only stop on a genuine design contradiction (write it at the top of the report).

## Success Criteria
- [ ] T20 asset authored; `Hrot.AI.Behaviors` clean-rebuilds 0 errors (emitted stateful thunk compiles — DEBT-AIB-026 closed).
- [ ] `T20_MultiStateful_ProofTests` (both named tests) pass — driving real generator→compile→register→provision→tick, asserting independent partition-slot state + mixed stateless/stateful coexistence.
- [ ] Byte-identity 129/0; `Hrot.AiEditor.Generators.Tests` green (minus 2 known); behavior tests green under filter.
- [ ] Report at `.dev/_DONE/btree-ai-action-binding/reports/BATCH-09-REPORT.md`.

## Report Requirements
Answer: whether the emitted stateful thunk compiled first try or what you fixed in `BTreeBridgeEmitCore`; the T20 topology + the exact per-tick expected cursor/counter values and why; how the proof test provisions slots (real ingress vs manual) and ticks; the live-inspector status (note if only deferred); any deviation; weak points; suggested commit message. Do NOT ask comprehension questions.
