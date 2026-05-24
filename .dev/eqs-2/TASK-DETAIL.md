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

**Design reference:** DESIGN.md §1.4

**Scope**

- Add `_deactivatorDelegates` array to `Interpreter<TBlackboard, TContext>`.
- Populate it in the constructor by iterating `blob.MethodNames` and calling `registry.TryGetDeactivator`.
- Snapshot `oldRunningNodeIndex` before `ExecuteNode` in `Tick`.
- After `ExecuteNode`, if the running node changed away from a non-zero node and the old node was an Action/Condition with a deactivator, invoke it.
- Handle the tree-completion case (result != Running, `state.RunningNodeIndex` is reset to 0 by existing code).

**Not in scope**

- Changes to `NodeDefinition`.
- Changes to `BehaviorTreeState`.
- Changes to `BTreeBuilder` or blob compilation.

**Constraints**

- `_deactivatorDelegates` length must equal `blob.MethodNames.Length` (same as `_actionDelegates`).
- Null entries are valid (no deactivator registered for that index); must not throw.
- The deactivator is invoked AFTER the tick completes, never during.
- The deactivator receives the same `ref TBlackboard blackboard`, `ref BehaviorTreeState state`, `ref TContext context` references as the preceding tick call, plus the payload index of the old node.
- If `blob.MethodNames` is null or empty (empty tree), `_deactivatorDelegates` must be `Array.Empty<...>()` — same as `_actionDelegates`.
- The deactivator must NOT be invoked if `oldRunningNodeIndex == 0` (tree was idle before the tick).
- The deactivator must NOT be invoked if the running node did not change (`oldRunningNodeIndex == state.RunningNodeIndex`).
- The delta check must not allocate heap memory. No `stackalloc` required; only a local `ushort`.

**Success conditions**

Setup: in all tests, register the target action + its deactivator via `ActionRegistry`.

- T1 (natural completion): Sequence with one resource-owning Action returning Success on Tick 1. `deactivationCount == 1` after Tick 1.
- T2 (branch switch): ObserverSelector (Selector semantics), two children. Child 0 = Condition, returns Failure Tick 1 / Success Tick 2. Child 1 = resource-owning Action returning Running. After Tick 1: deactivation count == 0. After Tick 2: deactivation count == 1.
- T3 (tree failure): Sequence with one resource-owning Action returning Failure. After Tick 1: deactivation count == 1.
- T4 (no deactivator registered): Tick 1000 times with a running action that has no deactivator. No exception; no GC collections.
- T5 (two resource-owning nodes, only one exits): Selector, child A (resource-owning, Running Tick 1), child B (resource-owning, Running if reached). After Tick 1, child A is running. Force tree reset (RunningNodeIndex = 0). After Tick 2, child B is reached. Assert deactivator-A fired once; deactivator-B has not fired.
- T6 (already-idle tree): RunningNodeIndex == 0 before tick. Tick returns Success immediately (empty tree). Deactivation count == 0; no exception.
- T7 (deactivator exception propagates): If the deactivator throws, the exception propagates out of `Tick` without being swallowed.

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
