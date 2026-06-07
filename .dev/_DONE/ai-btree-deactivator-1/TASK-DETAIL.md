# Task Detail — EQS Sensor Lifecycle / BTree Hybrid Lifecycle Hook

**Design reference:** [DESIGN.md](./DESIGN.md)

---

## Phase 1 — FastBTree Library (Fbt.Kernel, isolated)

---

### TASK-EQL-001 — NodeDeactivatorDelegate and BTreeDeactivatorAttribute

**Design reference:** DESIGN.md §1.1, §1.2

**Scope**

- Add `NodeDeactivatorDelegate<TBlackboard, TContext>` to `Fbt.Kernel`.
- Add `BTreeDeactivatorAttribute` to `Fbt.Kernel` (Attributes folder, same as `BTreeActionAttribute`).
- No other files change.

**Not in scope**

- ActionRegistry changes, Interpreter changes, tests — covered by later tasks.

**Constraints**

- `NodeDeactivatorDelegate` must live in the `Fbt` namespace (same as `NodeLogicDelegate`).
- `BTreeDeactivatorAttribute` must live in the `Fbt` namespace (same as `BTreeActionAttribute`).
- Attribute must use constructor argument for `TargetAction` (not a property set), to match existing attribute conventions (`BTreeActionAttribute` uses no arguments; `BTreeDeactivatorAttribute` must accept a single `string targetAction` constructor argument).
- Do not add a reference to any package outside `Fbt.Kernel`.

**Success conditions**

- T1: `typeof(NodeDeactivatorDelegate<,>).Namespace == "Fbt"` is true at runtime.
- T2: `typeof(BTreeDeactivatorAttribute).Namespace == "Fbt"` is true at runtime.
- T3: Constructing `new BTreeDeactivatorAttribute("Foo.Bar")` returns an attribute whose `TargetAction == "Foo.Bar"`.
- T4: `NodeDeactivatorDelegate<TestBlackboard, MockContext>` can be assigned a lambda `(ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) => { }` without a cast.
- T5: `Fbt.Kernel` project builds without errors.

---

### TASK-EQL-002 — ActionRegistry deactivator support

**Design reference:** DESIGN.md §1.3

**Scope**

- Add `RegisterDeactivator` and `TryGetDeactivator` to `ActionRegistry<TBlackboard, TContext>`.
- Storage: a new `Dictionary<string, NodeDeactivatorDelegate<TBlackboard, TContext>>` field, parallel to the existing `_actions` field.

**Not in scope**

- Interpreter changes.

**Constraints**

- Registration key must be the same string used in `Register` for the corresponding action.
- `TryGetDeactivator` must return `false` and a null output when no deactivator is registered for the key (mirrors existing `TryGetAction` semantics).
- No allocation beyond dictionary insertion during registration.

**Success conditions**

- T1: After `registry.RegisterDeactivator("Foo", deleg)`, `registry.TryGetDeactivator("Foo", out var d)` returns `true` and `d` is the same delegate instance.
- T2: `registry.TryGetDeactivator("Missing", out _)` returns `false`.
- T3: Calling `RegisterDeactivator` with a null key throws `ArgumentNullException`.
- T4: Calling `RegisterDeactivator` with a null delegate throws `ArgumentNullException`.
- T5: Registering the same key twice overwrites the first entry (last-write-wins, consistent with `Register`).

---

### TASK-EQL-003 — Interpreter deactivator array and delta tracking

**Design reference:** DESIGN.md §1.4, §3.5

**Scope**

- Add `_deactivatorDelegates` array to `Interpreter<TBlackboard, TContext>`.
- Populate it in the constructor by iterating `blob.MethodNames` and calling `registry.TryGetDeactivator`.
- Snapshot the full active path into `Span<ushort> oldPath = stackalloc ushort[9]` before
  any structural bounds-check and before `ExecuteNode` in `Tick`.
- After `ExecuteNode`, build `newPath` the same way; sweep `oldPath` for entries not present
  in `newPath`; for each exited node call `InvokeDeactivatorIfRegistered`.
- `InvokeDeactivatorIfRegistered` must guard on `node.Type is NodeType.Action or NodeType.Condition`
  before using `node.PayloadIndex` to index into `_deactivatorDelegates`.
- When a `NodeType.Parallel` node exits the active path, for each child whose completion bit
  in `LocalRegisters` is NOT set, sweep the child's **entire definition block**: iterate every
  node index in `[childIndex, childIndex + childNode.SubtreeOffset)` and call
  `InvokeDeactivatorIfRegistered` on each. This range sweep is required because
  `ExecuteParallel` overwrites `RunningNodeIndex` with its own index, erasing any inner leaf
  from the `NodeIndexStack`.
- Handle the hot-reload structural bounds-check case using a `pathWasReset` flag: if
  `RunningNodeIndex >= blob.NodeCount` after snapshotting `oldPath`, sweep `oldPath`
  deactivators immediately (do NOT return early), reset `RunningNodeIndex` and
  `StackPointer`, then continue to `ExecuteNode` on the same frame. Set `pathWasReset =
  true` and skip the post-tick path sweep to avoid double-firing deactivators.
- Handle the tree-completion case (result != Running, path clears to all-zero).

**Not in scope**

- Changes to `NodeDefinition`.
- Changes to `BehaviorTreeState`.
- Changes to `BTreeBuilder` or blob compilation.

**Constraints**

- `_deactivatorDelegates` length must equal `blob.MethodNames.Length` (same as `_actionDelegates`).
- Null entries are valid (no deactivator registered for that index); must not throw.
- The deactivator is invoked AFTER the tick completes for the normal path. In the hot-reload
  path it is invoked BEFORE the structural reset, and the post-tick sweep is then skipped via
  `pathWasReset` to prevent double-firing (see §3.5 ordering).
- The deactivator receives the same `ref TBlackboard blackboard`, `ref BehaviorTreeState state`,
  `ref TContext context` references as the preceding tick call, plus the payload index of the node.
- If `blob.MethodNames` is null or empty (empty tree), `_deactivatorDelegates` must be
  `Array.Empty<...>()` — same as `_actionDelegates`.
- The delta check must not allocate heap memory. Both `oldPath` and `newPath` must be
  `stackalloc ushort[9]` (call-stack only). No `List<T>` or array allocation.
- `InvokeDeactivatorIfRegistered` MUST check `node.Type is NodeType.Action or NodeType.Condition`
  before indexing `_deactivatorDelegates` with `node.PayloadIndex`. Composite and decorator nodes
  use `PayloadIndex` for a different payload table; skipping this guard causes incorrect array
  access.
- Entries with value 0 in `oldPath` must never trigger deactivator calls (idle slots).

**Success conditions**

Setup: in all tests, register the target action + its deactivator via `ActionRegistry`.

- T1 (natural completion): Sequence with one resource-owning Action returning Success on Tick 1. `deactivationCount == 1` after Tick 1.
- T2 (branch switch — leaf in RunningNodeIndex): ObserverSelector, two children. Child 0 = Condition, returns Failure Tick 1 / Success Tick 2. Child 1 = resource-owning Action returning Running. After Tick 1: deactivation count == 0. After Tick 2: deactivation count == 1.
- T3 (tree failure): Sequence with one resource-owning Action returning Failure. After Tick 1: deactivation count == 1.
- T4 (no deactivator registered): Tick 1000 times with a running action that has no deactivator. No exception; no GC collections.
- T5 (two resource-owning nodes, only one exits): Selector, child A (resource-owning, Running Tick 1), child B (resource-owning, Running if reached). After Tick 1, child A is running. Force path reset (clear `RunningNodeIndex` and `NodeIndexStack`). After Tick 2, child B is reached. Assert deactivator-A fired once; deactivator-B has not fired.
- T6 (already-idle tree): All path entries zero before tick. Tick returns Success immediately (empty tree). Deactivation count == 0; no exception.
- T7 (deactivator exception propagates): If the deactivator throws, the exception propagates out of `Tick` without being swallowed.
- T8 (subtree abort before leaf sets RunningNodeIndex): ObserverSelector whose low-priority child
  is a Sequence with a resource-owning Action. After Tick 1 the Action is Running and present in
  `oldPath`. On Tick 2 the high-priority condition succeeds and aborts the Sequence. Assert
  deactivator fires exactly once on Tick 2.
- T9 (Parallel child abort — subtree sweep): `Parallel` node with two children, each a
  `Sequence` containing a resource-owning `Action` with a distinct deactivator. On Tick 1
  both leaf Actions are Running (only the `Parallel` appears in `NodeIndexStack`). On Tick 2
  the `Parallel` exits the active path. Assert both deactivators fired exactly once, proving
  the range sweep reaches leaf Actions nested inside `Sequence` children.
- T10 (hot-reload bounds-check — no frame skip): Resource-owning Action is Running. Replace blob
  with a shorter one where old `RunningNodeIndex` is out of bounds. Assert: (a) deactivator
  fires BEFORE `RunningNodeIndex` is reset; (b) `RunningNodeIndex` is 0 after; (c) `Tick`
  continues to call `ExecuteNode` on the same invocation (no early return); (d) deactivators
  are NOT fired a second time by the post-tick sweep (`pathWasReset` flag prevents it).

---

## Phase 2 — Roslyn Generator Extension (Fdp.Toolkits.Analyzers)

---

### TASK-EQL-004 — BTreeActionGenerator deactivator detection and emission

**Design reference:** DESIGN.md §2.1–2.5

**Scope**

- Extend `BTreeMethodInfo` with `IsDeactivator` and `TargetAction` fields.
- Extend `GetMethodInfo` to detect `[BTreeDeactivatorAttribute]`.
- Extend `Execute` to collect deactivators and assign them to `GroupEntry.Deactivators`.
- Extend `GenerateRegistrar` to emit `registry.RegisterDeactivator(...)` calls.
- Handle both 4-param (direct) and 3-param (bridge `@0`) target key forms.
- Emit diagnostics `BHU-016` (missing TargetAction) and `BHU-017` (unknown TargetAction).

**Not in scope**

- Changes to `BTreeDefinitionGenerator` or `HsmActionGenerator`.
- Tests for the generated output beyond what existing generator tests cover.

**Constraints**

- Deactivator detection must use attribute name string comparison (`"BTreeDeactivatorAttribute"`)
  consistent with the pattern used for `BTreeActionAttribute`.
- The target key for a 3-param bridge deactivator must use the `"{fullMethodName}@0"` form.
  The generator must detect whether the target method (identified by `TargetAction`) is a
  bridge method by checking the `IsReusable` flag of the matched `BTreeMethodInfo` in the
  same compilation.
- A deactivator method must have `void` return type; emit `BHU-016` and skip emission if not.
- Generator must remain incremental (no `ForceFullRegeneration`).

**Success conditions**

- T1: A 4-param action `Foo.Bar.Action_X` with a companion `[BTreeDeactivator("Foo.Bar.Action_X")]` method causes `registry.RegisterDeactivator("Foo.Bar.Action_X", global::Foo.Bar.Deactivate_X)` to appear in the generated `FbtActionRegistrar.g.cs`.
- T2: A 3-param bridge action `Foo.Bar.Action_Y@0` with a companion `[BTreeDeactivator("Foo.Bar.Action_Y@0")]` causes the correct compound key to be emitted.
- T3: A `[BTreeDeactivator("")]` (empty string) causes diagnostic `BHU-016` and no emission for that method.
- T4: A `[BTreeDeactivator("Foo.Unknown")]` where `"Foo.Unknown"` matches no `[BTreeAction]` or `[BTreeCondition]` in the compilation causes diagnostic `BHU-017`.
- T5: An existing compilation with no `[BTreeDeactivator]` methods generates the same `FbtActionRegistrar.g.cs` as before (regression: no new lines emitted for zero deactivators).

---

## Phase 3 — Engine Integration

---

### TASK-EQL-005 — WeaponChannel deactivator for InsurgentNodes.Action_AimAndFire

**Design reference:** DESIGN.md §3.1

**Scope**

- Add `Deactivate_AimAndFire` static method to `InsurgentNodes` in
  `Fdp.Examples.UrbanCombat.Brains`.
- Annotate with `[BTreeDeactivator("Fdp.Examples.UrbanCombat.Brains.InsurgentNodes.Action_AimAndFire")]`.
- Body: if entity has `WeaponChannel` and `ActiveAction == CombatConstants.ActionIdAimAndFire`,
  set `ActiveAction = 0` and increment `ActionInstanceId` (unchecked).

**Not in scope**

- Removing the existing `Action_AimAndFire` method body.
- Changes to `AiBehaviorFactory`.
- Writing scenario tests.

**Constraints**

- The deactivator must guard against the entity not having `WeaponChannel` (component may be
  absent if the entity was partially constructed).
- Only clear the channel if `ActiveAction` equals the specific action ID; do not clear channels
  belonging to other actions.
- Do not clear `WeaponChannel` if `ActiveAction == 0` (already clear; skip the `ActionInstanceId`
  increment to avoid spurious re-dispatch signals).

**Success conditions**

- T1: A unit test constructs an entity with `WeaponChannel.ActiveAction = CombatConstants.ActionIdAimAndFire`.
  Invoke `Deactivate_AimAndFire` directly. Assert `ActiveAction == 0` and `ActionInstanceId` incremented.
- T2: Invoke `Deactivate_AimAndFire` on an entity without `WeaponChannel`. No exception.
- T3: Invoke with `ActiveAction == 0` (already cleared). Assert `ActionInstanceId` is NOT incremented.
- T4: Invoke with `ActiveAction` set to a different action ID. Assert `WeaponChannel` is unchanged.

---

### TASK-EQL-006 — LocomotionChannel deactivator for HillAttackTankNodes.Action_CreepToAndBeyondSlot

**Design reference:** DESIGN.md §3.2

**Scope**

- Add `Deactivate_CreepToAndBeyondSlot` to `HillAttackTankNodes` in `Hrot.AI.Behaviors.Brains`.
- Target key is the 3-param bridge compound key
  `"Hrot.AI.Behaviors.Brains.HillAttackTankNodes.Action_CreepToAndBeyondSlot@0"`.
- Body: if entity has `LocomotionChannel` and `ActiveAction == NavigationConstants.ActionIdMoveTo`,
  set `ActiveAction = 0`, increment `ActionInstanceId` (unchecked).

**Not in scope**

- Removing the existing explicit `loco.ActiveAction = 0` lines inside
  `Action_CreepToAndBeyondSlot` for the `Failure` path — those are defensive and may stay
  (belt-and-suspenders). Removing them is deferred technical debt.

**Constraints**

- Same guard-against-absent-component rule as TASK-EQL-005.
- Only clear if `ActiveAction == NavigationConstants.ActionIdMoveTo`.
- The deactivator is NOT responsible for publishing `ClearBehaviorEvent`; that is only done
  by `Action_ReverseToBaseline` on deliberate end-of-behavior.

**Success conditions**

- T1: Entity with `LocomotionChannel.ActiveAction = NavigationConstants.ActionIdMoveTo`. Invoke
  deactivator. Assert `ActiveAction == 0` and `ActionInstanceId` incremented.
- T2: Entity without `LocomotionChannel`. No exception.
- T3: `ActiveAction` set to a different action. Assert unchanged.

---

### TASK-EQL-007 — WeaponChannel deactivator for HillAttackTankNodes.Action_AimAndFireSpecific

**Design reference:** DESIGN.md §3.3

**Scope**

- Add `Deactivate_AimAndFireSpecific` to `HillAttackTankNodes`.
- Target key: `"Hrot.AI.Behaviors.Brains.HillAttackTankNodes.Action_AimAndFireSpecific@0"`.
- Body: if entity has `WeaponChannel` and `ActiveAction == CombatConstants.ActionIdAimAndFire`,
  set `ActiveAction = 0`, increment `ActionInstanceId` (unchecked).

**Not in scope**

- Removing the `ClearWeaponActionIfActive` call in `Action_AimAndFireSpecific` for the
  `MaxRounds` path — that path calls it explicitly before returning Success; keep it.
  The deactivator covers the branch-abort path that `ClearWeaponActionIfActive` does not reach.

**Constraints**

- Same guard and conditional-clear constraints as TASK-EQL-005.

**Success conditions**

- T1–T4: Same structure as TASK-EQL-005 tests, scoped to `WeaponChannel` and
  `CombatConstants.ActionIdAimAndFire`.

---

### TASK-EQL-008 — EqsRequestId deactivator for HillAttackCommanderNodes.Action_RequestAreaQuery

**Design reference:** DESIGN.md §3.4

**Scope**

- Add `Deactivate_RequestAreaQuery` to `HillAttackCommanderNodes` in `Hrot.AI.Behaviors.Brains`.
- Target key: `"Hrot.AI.Behaviors.Brains.HillAttackCommanderNodes.Action_RequestAreaQuery@0"`.
- Body: obtain `Blackboard1024` heavy component via `GetComponentRW`, `Unsafe.As` to
  `HillAttackMutableState`, set `CachedEqsRequestId = -1`.

**Not in scope**

- Cancelling the in-flight area query at the solver level — the pool slot will remain occupied
  until the solver's TTL evicts it. Slot reclamation is tracked as separate technical debt.
- Clearing `CachedTargetGroupHandle` — that field is reset by `Action_CalculateSegments` at
  behavior re-entry, which is the correct place.

**Constraints**

- Guard: if entity does not have `Blackboard1024`, return without action.
- Only reset `CachedEqsRequestId` to `-1`; do not modify other `HillAttackMutableState` fields.
- The `Unsafe.As` cast is identical to the pattern used throughout `HillAttackCommanderNodes`.

**Success conditions**

- T1: Entity with `Blackboard1024` containing `HillAttackMutableState { CachedEqsRequestId = 42 }`.
  Invoke deactivator. Assert `CachedEqsRequestId == -1`.
- T2: Entity without `Blackboard1024`. No exception.
- T3: Entity with `CachedEqsRequestId == -1` already. Assert no exception and value remains -1.

---

## Phase 5 — AOT Bit-Flag Optimization

---

### TASK-EQL-009 — NodeDefinition bit-flag layout and temporary Interpreter patching

**Design reference:** DESIGN.md §5.1

**Scope**

- Rename `public int PayloadIndex` to `RawPayloadIndex` in `NodeDefinition`.
- Add `PayloadIndex` (get-only, masks bit 31), `IsResourceOwning` (tests bit 31), and
  `SetResourceOwning()` to `NodeDefinition`.
- Fix all compilation errors caused by the rename: update `TreeCompiler`, `BinaryTreeSerializer`,
  and any other direct field writers to use `RawPayloadIndex`.
- Add temporary in-memory patching loop to `Interpreter` constructor: iterate all nodes; for
  each `Action`/`Condition` node whose method name is in the deactivator registry, call
  `node.SetResourceOwning()`. Mark with `// TODO: Remove in Phase 5.2`.
- Replace `InvokeDeactivatorIfRegistered` in `Interpreter` with `SweepExitedNode` that checks
  `node.IsResourceOwning` instead of the `NodeType` guard.

**Not in scope**

- Changes to `TreeCompiler` compilation logic (AOT baking — Phase 5.2).
- Changes to `BinaryTreeSerializer` version or read/write semantics (Phase 5.3).
- Removing `_deactivatorDelegates` array (Phase 5.4).

**Constraints**

- `NodeDefinition` must remain exactly 8 bytes (`StructLayout(Sequential, Pack = 1)` unchanged).
- `PayloadIndex` must return bits 0–30 of `RawPayloadIndex` (bit 31 stripped).
- `IsResourceOwning` must test bit 31 only, with no branching.
- `SetResourceOwning()` must be a pure bitwise OR with no conditional.
- The patching loop must only call `SetResourceOwning()` on `Action`/`Condition` nodes; it must
  never set the bit on composite or decorator nodes.
- The `NodeType` guard (`node.Type is NodeType.Action or NodeType.Condition`) is removed from
  `SweepExitedNode`; the bit flag provides equivalent safety for all node types.

**Success conditions**

- T1: `new NodeDefinition { RawPayloadIndex = 5 }.PayloadIndex == 5`.
- T2: `new NodeDefinition { RawPayloadIndex = 5 }.IsResourceOwning == false`.
- T3: After `d.SetResourceOwning()`, `d.PayloadIndex == 5` (bits 0–30 unchanged).
- T4: After `d.SetResourceOwning()`, `d.IsResourceOwning == true`.
- T5: `new NodeDefinition { RawPayloadIndex = unchecked((int)0x80000005) }.PayloadIndex == 5`
  (MSB masked out by the property).
- T6: `sizeof(NodeDefinition) == 8`.
- T7 (patching correct): Construct an `Interpreter` with a registered deactivator for action A.
  Assert `blob.Nodes[actionANodeIndex].IsResourceOwning == true` immediately after construction.
- T8 (no contamination): Action B has no deactivator. Assert
  `blob.Nodes[actionBNodeIndex].IsResourceOwning == false` after construction.
- T9 (no contamination on composites): A `Sequence` node whose `PayloadIndex` happens to
  collide with a deactivator method index must NOT have `IsResourceOwning == true` after
  construction (the patch loop is type-gated).
- T10 (regression): All L-01 through L-08 tests in `HybridLifecycleTests` pass without
  modification.
- T11: `Fbt.Kernel` and `Fbt.Tests` build without errors or warnings.

---

### TASK-EQL-010 — AOT compilation pipeline

**Design reference:** DESIGN.md §5.2

**Scope**

- Add `public bool IsResourceOwning { get; set; }` to `BuilderNode`.
- Update `TreeCompiler.FlattenToBlob` and `FlattenToBlobCore` to accept
  `Func<string, bool>? isResourceOwning = null`.
- In `FlattenRecursive`, evaluate and set the bit for `Action`/`Condition` nodes.
- Update `BTreeBuilder.Compile` to pass
  `methodName => _registry.TryGetDeactivator(methodName, out _)` as the delegate.
- Delete the `// TODO: Remove in Phase 5.2` patching loop from `Interpreter` constructor.

**Not in scope**

- `BinaryTreeSerializer` version bump (Phase 5.3).
- Removing `_deactivatorDelegates` array (Phase 5.4).

**Constraints**

- The `isResourceOwning` parameter is optional (default null) so existing callers of
  `TreeCompiler.FlattenToBlob` that do not pass the delegate continue to compile without error.
- The bit must be set when either `BuilderNode.IsResourceOwning == true` OR the delegate
  returns `true` for the node's method name. Both conditions independently set the bit.
- The bit must NOT be set for `Selector`, `Sequence`, `Parallel`, or decorator node types,
  regardless of the delegate's return value for any incidentally matching string.
- After this task, the `Interpreter` constructor must NOT contain the patching loop.

**Success conditions**

- T1: Compile a tree via `BTreeBuilder` with a deactivator registered for action A. Before
  constructing an `Interpreter`, assert
  `blob.Nodes[actionANodeIndex].IsResourceOwning == true`.
- T2: Action B has no deactivator. Assert `blob.Nodes[actionBNodeIndex].IsResourceOwning == false`
  on the raw blob.
- T3: `BuilderNode { IsResourceOwning = true }` with no registry match: the compiled blob
  still has the bit set (explicit `BuilderNode` flag is honored).
- T4: A `Sequence` node in the same tree: `blob.Nodes[sequenceIndex].IsResourceOwning == false`.
- T5: Call `TreeCompiler.FlattenToBlob(root, "test")` without the delegate parameter.
  Assert it compiles without error and produces a blob with no `IsResourceOwning` bits set.
- T6 (regression, no patch loop): All L-01 through L-08 tests pass. The `Interpreter`
  constructor no longer contains the patch loop (verify by inspection or by asserting that
  construction time does not scale with the number of `Action` nodes).
- T7: `Fbt.Kernel`, `Fbt.Compiler`, and `Fbt.Tests` build without errors.

---

### TASK-EQL-011 — Binary serialization versioning and V1 legacy fallback

**Design reference:** DESIGN.md §5.3

**Scope**

- Bump `BinaryTreeSerializer.CurrentVersion` from `1` to `2`.
- Update `BinaryTreeSerializer.Save` to write `node.RawPayloadIndex`.
- Update `BinaryTreeSerializer.Load` to read into `RawPayloadIndex`.
- Change `BehaviorTreeBlob.Version` default to `2`.
- Stamp `blob.Version = 2` inside `TreeCompiler.FlattenToBlob`.
- Add V1 legacy fallback in `Interpreter` constructor: if `_blob.Version < 2`, apply the
  patching loop (same logic as the Phase 5.1 temporary loop, now behind the version gate).

**Not in scope**

- Removing `_deactivatorDelegates` array (Phase 5.4).
- Migrating any existing `.fbt` files on disk — the V1 path handles them at load time.

**Constraints**

- The `Load` method must accept both `Version == 1` and `Version == 2` blobs without throwing.
  A version outside `[1, 2]` must throw `InvalidDataException`.
- V1 blobs read from disk have their `RawPayloadIndex` stored without bit 31, so
  `IsResourceOwning` will be false for all nodes until the V1 patch loop in `Interpreter` runs.
- The V1 patch loop in `Interpreter` must be structurally identical to the Phase 5.1 temporary
  loop: type-gated to `Action`/`Condition` nodes, driven by the deactivator registry.
- V2 blobs must NOT trigger the patch loop.

**Success conditions**

- T1: A blob produced by `TreeCompiler.FlattenToBlob` has `blob.Version == 2`.
- T2 (V2 round-trip): Compile a tree with a resource-owning action. Save via
  `BinaryTreeSerializer.Save`. Load via `Load`. Assert: (a) loaded `blob.Version == 2`;
  (b) `blob.Nodes[actionIndex].IsResourceOwning == true` before constructing an `Interpreter`
  (bit survived the round-trip).
- T3 (V1 round-trip): Manually construct a V1 blob (`Version = 1`, `RawPayloadIndex` without
  bit 31 set for a resource-owning action). Load it. Assert `blob.Version == 1` and
  `blob.Nodes[actionIndex].IsResourceOwning == false` before constructing an `Interpreter`.
  Construct an `Interpreter` with the deactivator registry. Assert
  `blob.Nodes[actionIndex].IsResourceOwning == true` (V1 patch applied by constructor).
- T4 (V2 skips patch): Construct an `Interpreter` from a V2 blob. Verify the V1 patch loop
  body did not execute (instrument with a counter initialized to 0; assert counter == 0
  after construction).
- T5 (regression): All L-01 through L-08 tests pass with blobs produced by `BTreeBuilder`
  (which now stamps Version 2).
- T6: Invalid version (e.g., `Version = 99`) in a loaded stream causes `InvalidDataException`.
- T7: `Fbt.Kernel` and `Fbt.Tests` build without errors.

---

### TASK-EQL-012 — Interpreter cleanup and editor integration

**Design reference:** DESIGN.md §5.4

**Scope**

- Delete `_deactivatorDelegates` field from `Interpreter`. Add
  `private readonly ActionRegistry<TBlackboard, TContext> _registry`.
- Update `Interpreter` constructor to store `_registry`.
- Update `SweepExitedNode` to perform a targeted `_registry.TryGetDeactivator` lookup when
  `node.IsResourceOwning` is true, replacing the array index.
- Update `Interpreter.BindActions` or related initialization to no longer populate a
  `_deactivatorDelegates` array (the array no longer exists).
- Update `BTreeDefinitionGenerator` so generated `FbtTreeCatalog.Get*` methods accept
  `Func<string, bool>? isResourceOwning = null` and forward it to `Compile`.
- Update `AiBehaviorFactory.BuildRegistrationAction` to construct the `isResourceOwning`
  delegate and pass it to all `FbtTreeCatalog.Get*` calls.
- Add the `[R]` visual indicator to `BTreeVisualizerRenderer.DrawNode`.

**Not in scope**

- Changes to `ActionRegistry` itself.
- Adding new deactivators beyond those in Phases 1–3.

**Constraints**

- `_deactivatorDelegates` must be completely absent from `Interpreter` after this task (no
  nullable field, no fallback array). If the field is absent the constraint is met by definition.
- `SweepExitedNode` must still invoke the correct deactivator delegate and pass the correct
  `PayloadIndex` as the `paramIndex` argument.
- The `isResourceOwning` parameter on generated `Get*` methods must default to `null` so that
  existing call sites without the argument continue to compile.
- The `[R]` tooltip text must exactly match:
  `"Resource Owning Node: Manages standing ECS resources via OnDeactivate."`

**Success conditions**

- T1 (regression): All L-01 through L-08 tests pass with no `_deactivatorDelegates` array.
- T2 (array absence): `typeof(Interpreter<,>).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)`
  contains no field of type `NodeDeactivatorDelegate<,>[]`.
- T3 (no allocation on construction): Construct an `Interpreter` for a 500-node tree with
  zero resource-owning nodes. Assert `GC.CollectionCount(0)` unchanged before and after
  construction.
- T4 (correct deactivator called): Construct an `Interpreter` for a tree with action A
  resource-owning and action B not. Let action A run. Trigger a branch switch. Assert
  deactivator-A fired exactly once and deactivator-B never fired.
- T5 (generated catalog signature): In the generated `FbtTreeCatalog.g.cs`, every `Get*`
  method has a parameter `global::System.Func<string, bool>? isResourceOwning = null`.
- T6 (factory wiring): Load the `UrbanCombat` scenario. Verify that the `Insurgent` behavior
  blob has `blob.Nodes[Action_AimAndFireIndex].IsResourceOwning == true` (the factory passed
  the correct delegate at compile time).
- T7 (hot-reload end-to-end): While the `UrbanCombat` scenario is running, trigger a
  hot-reload. After the ALC swap, force a branch switch away from `Action_AimAndFire`. Assert
  `WeaponChannel.ActiveAction == 0` on the affected entity (deactivator fired correctly through
  the `_registry` dynamic lookup path, not an array).
- T8 (editor indicator): Pin the BTree Visualizer to an Insurgent. The `Action_AimAndFire`
  node displays `[R]` in purple. Hovering shows
  `"Resource Owning Node: Manages standing ECS resources via OnDeactivate."`.
- T9: `Fdp.Toolkits.Analyzers`, `Hrot.AI.Behaviors`, and `Hrot.BTree.Editor` build without
  errors.
