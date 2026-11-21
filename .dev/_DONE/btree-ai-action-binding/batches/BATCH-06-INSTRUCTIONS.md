# BATCH-06: Slice 2 — Partitioned per-node WorkingState + synchronous provisioning
**Tasks:** S2-1, S2-2   **Phase:** Slice 2 runtime core   **Est:** ~18h
**Dependencies:** Slice 1 complete (S1-3 baked-offset thunk path; BATCH-02/03/05).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/_DONE/btree-ai-action-binding/SLICE2-DESIGN.md` §2, §3, §6.2, §9, §10 (Flaw 1) — the spec. **§10 is authoritative where it conflicts with §9.**
3. `.dev/_DONE/btree-ai-action-binding/TASK-DETAIL.md` §S2-1, §S2-2 — the exact named tests + assertions. **Implement those tests with those assertions; do not invent acceptance criteria.**
4. Codebase-memory MCP first (`list_projects` → `get_architecture` → `search_graph`/`get_code_snippet`), per `.claude/CLAUDE.md`.

## Critical scoping note (dev-lead decision — do NOT deviate)
The BTree-node→blueprint-`TickCore` *reference-resolution* feature **does not exist yet** (the BTree persistence emitter has no AiPrimitive/WorkingState/TickCore code). **Do NOT build that compositional path.** Instead, **prove the architect-mandated mechanism** (per-node WorkingState in a `BlueprintBlackboard*` partition slot, keyed by `FNV-1a(BehaviorAssetId, NodeVisualId)`) by **extending the existing Slice-1 baked-offset thunk path** with a new **stateful demo primitive**. The "calls TickCore" of the design = "calls the bound method" for this demo primitive. The full blueprint-AiPrimitive composition is deferred (record as debt DEBT-AIB-025).

## Key current-code facts (verified by dev-lead — exact paths/lines)
- **Slice-1 baked-offset thunk** emitted in `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeBridgeEmitCore.cs` (~lines 231-282): per `(method,variable)` it registers `"{MethodFqn}@{offset}"` → a `static (ref BrainBlackboard bb, ref BehaviorTreeState st, ref BTreeContext ctx, int pi)` lambda that projects the DTO via `Unsafe.As<byte,Dto>(ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)offset))` and calls the method. `Register(...)` signature at line ~128 takes `ActionRegistry<BrainBlackboard,BTreeContext> actionRegistry`.
- **`BTreeContext` exposes `.Self` (Entity) and `.World`** — confirmed by `AiPrimitiveEmitter` thunks using `ctx.World.GetComponentRW<...>(ctx.Self)` and `ctx.World.SimulationTime`.
- **Partition API** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintBlackboardPartitions.cs`: `Initialize(byte* memory,int totalSize,byte maxSlots)`; `bool TryAttach(byte* memory,int blueprintId,int requestedSize,ulong structureHash,out int payloadOffset)`; `bool TryGetSlotOffset(byte* memory,int blueprintId,out int payloadOffset)`; `bool TryDetach(byte* memory,int blueprintId)`; `void CopyToLargerTier(byte* src,int srcSize,byte* dst,int dstSize,byte dstMaxSlots)`; `ResetSlot`, `GetSlotCount`, `GetSlot`. Slot key is a 32-bit `int`. `BlueprintSlotEntry` = 16 B.
- **Tier components** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard{1024,4096,16384}.cs` — consts `TotalSize`, `MaxSlots` (4/8/16), `PayloadSize` (928/3936/16096), `HeaderSize`(32), `fixed byte Memory[N]`.
- **`BehaviorIngressSystem`** `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BehaviorIngressSystem.cs` — `[UpdateInPhase(SystemPhase.Input)]`; reads `repo.Bus.ReadManaged<AssignBehaviorEvent>()`; commits transition at lines ~119-144; today only does `repo.AddComponent(evt.Entity, new Blackboard1024())` when `def.HeavyDtoType != null` (lines ~124-129). `repo.AddComponent`/`RemoveComponent`/`HasComponent`/`IsComponentTypeRegistered<T>` available; **synchronous structural mutation is safe in the Input phase** (outside the parallel Simulation lock).
- **Tier upgrade today** runs in `BlueprintMaintenanceSystem` (`[UpdateInPhase(SystemPhase.BeforeSync)]`) via `CopyToLargerTier` — **too late** for a same-frame Simulation tick. S2-2 moves the upgrade into the Input-phase assignment path.
- **FNV-1a helper exists**: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Determinism/FnvHasher.cs` — `uint Hash32(ReadOnlySpan<byte>)`. **Reuse it for any runtime hashing.** For the *compile-time* slot-key computation in the generator/emitter (which cannot reference that internal class), replicate the identical FNV-1a-32 algorithm (offset basis `2166136261u`, prime `16777619u`) and mask to `0x7FFFFFFF` so the key is a positive `int` — it MUST byte-for-byte match between emit-time and any runtime recomputation.
- **Runtime test harness (mechanism-level)**: `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/MultiActionBoundTests.cs` shows building an `ActionRegistry<BrainBlackboard,BTreeContext>`, registering thunks, building a blob, `new Interpreter<...>(blob,reg)`, `interpreter.Tick(ref bb, ref state, ref ctx)`, reading bytes back via `Unsafe.As`. **You must additionally build a real ECS world/entity + a `BlueprintBlackboard1024` component so the thunk can fetch the partition component via `ctx.World`/`ctx.Self`** — mirror how the behavior runtime tests construct a world (search `Fdp.Toolkits.Tests/Behavior` for an existing world/`EntityRepository` + `BTreeContext{Self,World}` setup; `BehaviorIngressSystemTests.cs` publishes `AssignBehaviorEvent` and builds a world).

---

## Tasks (complete strictly in sequence; do NOT start Task 2 until Task 1 is implemented, its tests written, and ALL tests — including prior batches' — pass)

### Task 1: Stateful demo primitive + partition-slot WorkingState adapter (S2-1)
**Files:**
- `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/DemoCounterNodes.cs` (UPDATE — add stateful primitive)
- `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeBridgeEmitCore.cs` (UPDATE — emit working-state adapter thunk + per-asset stateful-slot manifest)
- the generator that drives it (`Hrot/Subsystems/AI/Hrot.AiEditor.Generators/BTreeJsonGenerator*` — read to find where node bindings are enumerated; compute the FNV-1a slot key from `(asset GUID bytes ++ node VisualId GUID bytes)` there or in the emit core)
- `BehaviorDefinition` (find it — likely `FDP/Toolkits/Fdp.Toolkits/Behavior/.../BehaviorDefinition.cs`) (UPDATE — add a stateful-slot manifest field, see below)
- the BTree authoring asset model / node payload (`Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs`) only if a node needs a new field to mark a working-state binding (prefer reusing existing binding fields; if you add a field, keep it optional + `Managed==false` assets unaffected).

**Scope:**
1. **Stateful demo primitive** in `DemoCounterNodes`: add a params struct `DemoCursorParams { int Limit; }` and a working-state struct `DemoCursorState { int Cursor; }` (both `[StructLayout(Sequential)]`, blittable). Add a method
   `public static NodeStatus Action_AdvanceCursor(ref DemoCursorParams p, ref DemoCursorState ws, ref BehaviorTreeState state, ref BTreeContext ctx)` that does `ws.Cursor++; return ws.Cursor < p.Limit ? NodeStatus.Running : NodeStatus.Success;`. Attribute it so it is discoverable as a stateful BTree action (introduce/extend an attribute or `DelegateShape` that marks the 4-param-with-working-state shape — name it `ThreeParamReusableStateful` or add `WorkingStateType` to the existing binding; pick the smallest change and document it).
2. **Slot key**: per stateful node binding, `SlotKey = FNV-1a-32(assetGuid.ToByteArray() ++ nodeVisualId.ToByteArray()) & 0x7FFFFFFF` (positive int). Computed at emit time; baked as a literal into that node's thunk.
3. **Adapter thunk** (extend the Slice-1 emission): for a stateful binding emit a thunk that (a) projects `Params` over `bb.BehaviorParameters[0]` at the baked param offset (Slice-1 pattern), (b) fetches the entity's active `BlueprintBlackboard*` tier component via `ctx.World`/`ctx.Self` (**dispatch across 16384/4096/1024 — whichever the entity has; do NOT hardcode 1024**), inside a `fixed` block calls `BlueprintBlackboardPartitions.TryGetSlotOffset(memory, SLOTKEY, out int wsOff)`, projects `WorkingState` over `memory + wsOff`, then (c) calls the method `(ref p, ref ws, ref st, ref ctx)`. If the slot is missing (`TryGetSlotOffset` false) the thunk must **fail loud** (return `NodeStatus.Failure` AND `Debug.Assert`/throw in DEBUG) — never silently project over offset 0 or garbage.
4. **Stateful-slot manifest** on `BehaviorDefinition`: add `public StatefulSlotInfo[]? StatefulWorkingSlots;` where `StatefulSlotInfo { int SlotKey; int PayloadSize; uint StructureHash; }`. The per-asset registrar (emitted in `BTreeBridgeEmitCore.Register`) populates it with one entry per **reachable** stateful node instance (all stateful bindings in the asset; dedupe by SlotKey). `PayloadSize` = managed size of the WorkingState struct (reuse the Slice-1 `StructSizeResolver`); `StructureHash` = FNV-1a-32 of the WorkingState type's field layout (or reuse an existing struct-hash if one exists — search). This manifest is what S2-2 consumes; keep `Managed==false` assets emitting `null`.

**Tests required** (`FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/` — runtime, real world+partition component):
- `StatefulPrimitive_WorkingStatePersistsAcrossTicks` — build a world, an entity with a `BlueprintBlackboard1024`, `Initialize` + `TryAttach` one slot for the demo node's SlotKey; register the adapter thunk **as emitted by the actual emitter is preferable, but at minimum mirror it exactly**; tick N times; assert `DemoCursorState.Cursor` increments and **persists across ticks** by reading it back from the partition slot offset (NOT from `Blackboard1024 Memory+8`).
- `SameStatefulPrimitive_TwoNodes_IndependentSlots` — two nodes (two distinct VisualIds → two distinct FNV-1a keys) with two attached slots; advance one; assert the other's slot bytes are unchanged (independent state, no cross-talk).
- **Emitter-exercising test** (do NOT skip — Slice-1 lesson): a generator/emit test (`Hrot.AiEditor.Generators.Tests` or `Hrot.AiEditor.Persistence.Tests`) that runs the real emitter on a fixture asset with a stateful binding and asserts the emitted thunk source (a) contains the baked SlotKey literal equal to the independently-computed FNV-1a value, (b) calls `TryGetSlotOffset`, (c) projects WorkingState at the returned offset; and that the emitted registrar populates `StatefulWorkingSlots` with the correct `{SlotKey,PayloadSize}`.
- Verify the SlotKey computation has a **unit test** asserting a known `(assetGuid,nodeGuid)` → known int (lock the algorithm).

### Task 2: Synchronous Input-phase provisioning + tier upgrade (S2-2)
**Files:**
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BehaviorIngressSystem.cs` (UPDATE)
- a small provisioning helper if it keeps the system readable (your call; keep it in the same assembly).

**Scope:** in the `AssignBehaviorEvent` handler (Input phase), after committing the transition: if `def.StatefulWorkingSlots` is non-null/non-empty:
1. Compute aggregate required payload size = Σ `PayloadSize` (+ per-slot 16 B `BlueprintSlotEntry` overhead) and slot count.
2. Select the smallest tier whose `PayloadSize`/`MaxSlots` fit (1024→4096→16384).
3. **Synchronously** ensure the entity carries the selected tier component:
   - none present → `repo.AddComponent(entity, new BlueprintBlackboard{N}())` then `Initialize`.
   - a **smaller** tier present with existing slots → `AddComponent` the larger, `CopyToLargerTier(src,srcSize,dst,dstSize,dstMaxSlots)`, `RemoveComponent` the smaller — all inline in Input (mirror `BlueprintMaintenanceSystem` but synchronous). Preserve existing slots/offsets.
   - the correct-or-larger tier already present → leave it.
4. Eager-allocate every manifest slot: `TryAttach(memory, SlotKey, PayloadSize, StructureHash, out _)` for each (skip if already attached). This guarantees the slot exists **before** the same frame's Simulation BTree tick.
5. On behavior change, `TryDetach` the previous behavior's stateful slots before attaching new ones (no leak). (Track previous slot keys however is cleanest — e.g. the prior `def.StatefulWorkingSlots`; if you can't get the prior def, document the limitation and cover what you can.)

**Tests required** (`FDP/Toolkits/Fdp.Toolkits.Tests` — SimHost/behavior systems; mirror `BehaviorIngressSystemTests.cs`):
- `Assign_UpgradesTierSynchronously_BeforeFirstTick` — entity pre-carrying `BlueprintBlackboard1024` (with an existing slot occupying most of it) assigned a behavior whose manifest needs more than 1024 → after the Input phase (before any Simulation tick) the entity has the larger tier, the old slot's bytes survived the copy, and all new slots are attached (`TryGetSlotOffset` true for each). Assert no exception on a subsequent tick.
- `Assign_ProvisionsWorstCaseReachableStatefulNodes` — provisioned/attached slot count == number of distinct stateful node instances in the manifest (not the executed subset). Build a manifest with ≥3 slots; assert all 3 attached after assignment.

## Global rules
- `dotnet build-server shutdown` before any codegen verification.
- All emit changes guarded behind `Managed==true`; `Managed==false` assets emit byte-identical output — **byte-identity gate `Hrot.AiEditor.Persistence.Tests` must stay green** (CombatShowcase/SampleScout).
- Pre-existing NON-regression failures to ignore: the 2 `MigrationEquivalenceTests` JSON byte-stability cases (`Hrot.AiEditor.Generators.Tests`); ~24 unrelated `Fdp.Toolkits.Tests` failures NOT matching `~Behavior`. Run behavior tests with a `Behavior` filter to see your signal; also run the full touched-project suites and confirm 0 **net-new** failures.
- Never weaken/soften a test to make it pass. Fail loud over silent fallback. Fix root causes to completion; do not stop for permission. **Only stop on a breaking design flaw** — if you hit one, STOP and write it at the top of the report rather than guessing.

## Success Criteria
- [ ] S2-1: stateful primitive + partition-slot adapter + slot manifest; both runtime tests + the emitter-exercising test + the SlotKey unit test pass.
- [ ] S2-2: synchronous Input-phase provisioning + tier upgrade; both runtime tests pass.
- [ ] Clean rebuild of `Hrot.AI.Behaviors` 0 errors; byte-identity gate green; 0 net-new failures in touched projects.
- [ ] Report written to `.dev/_DONE/btree-ai-action-binding/reports/BATCH-06-REPORT.md`.

## Report Requirements (`reports/BATCH-06-REPORT.md`)
Answer: exact files changed; the new attribute/DelegateShape you chose for the stateful shape and why; how the thunk dispatches across tiers; how you computed the WorkingState `StructureHash`; how behavior-change detach handles the previous slot keys; any place you had to deviate from this spec and why; weak points / edge cases discovered; performance notes (per-tick lookup cost); suggested commit message. Do NOT ask comprehension questions.
