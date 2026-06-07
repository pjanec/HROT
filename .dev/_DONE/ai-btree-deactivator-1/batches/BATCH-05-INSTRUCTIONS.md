# BATCH-05 — Interpreter Cleanup + Editor Integration (EQL-012)

**Design reference:** [DESIGN.md §5.4](../DESIGN.md)
**Task detail:** [TASK-DETAIL.md — TASK-EQL-012](../TASK-DETAIL.md)
**Tracker:** [TASK-TRACKER.md](../TASK-TRACKER.md)

---

## Objective

Remove the `_deactivatorDelegates` pre-built array from `Interpreter` (replace with a stored
`_registry` reference). Wire the `isResourceOwning` delegate through the Roslyn generator and
`AiBehaviorFactory`. Add a `[R]` visual indicator to `BTreeVisualizerRenderer.DrawNode`.

---

## Scope (one task: EQL-012)

### 1. `Interpreter.cs` — Strip `_deactivatorDelegates`, add `_registry`

**File:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs`

**Changes:**

a) Replace the `_deactivatorDelegates` field with `_registry`:
   ```csharp
   // BEFORE:
   private readonly NodeDeactivatorDelegate<TBlackboard, TContext>?[] _deactivatorDelegates;
   // AFTER:
   private readonly ActionRegistry<TBlackboard, TContext> _registry;
   ```

b) In the constructor, remove `BindDeactivators` call and store `_registry` instead:
   ```csharp
   // BEFORE:
   _deactivatorDelegates = BindDeactivators(blob, registry);
   // AFTER:
   _registry = registry;
   ```

c) Update the V1 fallback loop in the constructor. It currently checks
   `_deactivatorDelegates[pi] != null`. Replace with a registry lookup:
   ```csharp
   // BEFORE:
   if (_deactivatorDelegates.Length > pi && _deactivatorDelegates[pi] != null)
       node.SetResourceOwning();
   // AFTER:
   if (_registry.TryGetDeactivator(_blob.MethodNames[pi], out _))
       node.SetResourceOwning();
   ```

d) Update `SweepExitedNode`. It currently indexes `_deactivatorDelegates[pi]`.
   Replace with a registry lookup on the method name:
   ```csharp
   // BEFORE (inside the if (node.IsResourceOwning) block):
   int pi = node.PayloadIndex;
   if ((uint)pi < (uint)_deactivatorDelegates.Length)
   {
       var deactivator = _deactivatorDelegates[pi];
       deactivator?.Invoke(ref blackboard, ref state, ref context, pi);
   }
   // AFTER:
   int pi = node.PayloadIndex;
   if ((uint)pi < (uint)_blob.MethodNames.Length)
   {
       if (_registry.TryGetDeactivator(_blob.MethodNames[pi], out var deactivator))
           deactivator.Invoke(ref blackboard, ref state, ref context, pi);
   }
   ```

e) Delete the entire `BindDeactivators` method (lines ~717–733 in the current file).
   It is no longer needed.

**The `BindActions` method and `_actionDelegates` field are unchanged.**

---

### 2. `BTreeDefinitionGenerator.cs` — Add `isResourceOwning` parameter to generated `Get*` methods

**File:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/BTreeDefinitionGenerator.cs`

**Change:** Update `GenerateCatalog` so that every generated `Get*` method:
- Accepts `global::System.Func<string, bool>? isResourceOwning = null` as a parameter
- Passes it to `.Compile(treeName, isResourceOwning)` (builder-returning overload)
- For blob-returning methods (raw `BehaviorTreeBlob`), no change to the call — the parameter
  is accepted but not used (no `Compile` call exists)

Current generated code pattern (builder-returning):
```csharp
public static global::Fbt.BehaviorTreeBlob GetMoveToLocation()
    => global::Hrot.AI.Behaviors.Brains.HrotBrainBehaviors.MoveToLocation().Compile("MoveToLocation");
```

New generated code pattern (builder-returning):
```csharp
public static global::Fbt.BehaviorTreeBlob GetMoveToLocation(global::System.Func<string, bool>? isResourceOwning = null)
    => global::Hrot.AI.Behaviors.Brains.HrotBrainBehaviors.MoveToLocation().Compile("MoveToLocation", isResourceOwning);
```

For blob-returning methods (direct method returning `BehaviorTreeBlob`):
```csharp
public static global::Fbt.BehaviorTreeBlob GetSomeName(global::System.Func<string, bool>? isResourceOwning = null)
    => global::Some.Type.SomeMethod();
```

The `isResourceOwning` parameter is accepted but ignored for blob-returning variants (the blob
is already compiled by the annotated method itself). This keeps the API uniform.

**In the `GenerateCatalog` method, update both string-builder branches:**

For `ReturnsBuilder == true`:
```csharp
sb.AppendLine("        public static global::Fbt.BehaviorTreeBlob Get" + safeName +
    "(global::System.Func<string, bool>? isResourceOwning = null)");
sb.AppendLine("            => global::" + m.FullyQualifiedTypeName + "." + m.MethodName +
    "().Compile(\"" + m.TreeName + "\", isResourceOwning);");
```

For `ReturnsBuilder == false`:
```csharp
sb.AppendLine("        public static global::Fbt.BehaviorTreeBlob Get" + safeName +
    "(global::System.Func<string, bool>? isResourceOwning = null)");
sb.AppendLine("            => global::" + m.FullyQualifiedTypeName + "." + m.MethodName + "();");
```

---

### 3. `AiBehaviorFactory.cs` — Wire `isResourceOwning` delegate

**File:** `Hrot/Subsystems/Hrot.AI.Behaviors/AiBehaviorFactory.cs`

After `FbtActionRegistrar.RegisterAll(actionRegistry);` and before the first `FbtTreeCatalog.Get*()` call, add:
```csharp
Func<string, bool> isResourceOwning = name => actionRegistry.TryGetDeactivator(name, out _);
```

Then pass it to every `FbtTreeCatalog.Get*()` call:
```csharp
// BEFORE:
var moveToBlob        = FbtTreeCatalog.GetMoveToLocation();
var followRouteBlob   = FbtTreeCatalog.GetFollowRoute();
var joinFormationBlob = FbtTreeCatalog.GetJoinFormation();
var wanderBlob        = FbtTreeCatalog.GetWanderMilitary();
var fireAtTargetBlob  = FbtTreeCatalog.GetFireAtTarget();
var hullDownBlob      = FbtTreeCatalog.GetHullDownAttackRun();
var platoonHillBlob   = FbtTreeCatalog.GetPlatoonHillAttack();

// AFTER:
var moveToBlob        = FbtTreeCatalog.GetMoveToLocation(isResourceOwning);
var followRouteBlob   = FbtTreeCatalog.GetFollowRoute(isResourceOwning);
var joinFormationBlob = FbtTreeCatalog.GetJoinFormation(isResourceOwning);
var wanderBlob        = FbtTreeCatalog.GetWanderMilitary(isResourceOwning);
var fireAtTargetBlob  = FbtTreeCatalog.GetFireAtTarget(isResourceOwning);
var hullDownBlob      = FbtTreeCatalog.GetHullDownAttackRun(isResourceOwning);
var platoonHillBlob   = FbtTreeCatalog.GetPlatoonHillAttack(isResourceOwning);
```

---

### 4. `BTreeVisualizerRenderer.cs` — Add `[R]` indicator

**File:** `Hrot/Engine/Hrot.Presentation/Renderers/BTreeVisualizerRenderer.cs`

Add a static color field at the top of the class (alongside existing ColorGreen, ColorYellow, ColorGray):
```csharp
private static readonly Vector4 ColorPurple = new Vector4(0.8f, 0.4f, 0.8f, 1.0f);
```

In `DrawNode`, after the `bool open = ImGui.TreeNodeEx(...)` call and before the `if (popColors > 0) ImGui.PopStyleColor(popColors);` line, add:
```csharp
// [R] indicator for resource-owning nodes
if (node.IsResourceOwning)
{
    ImGui.SameLine();
    ImGui.PushStyleColor(ImGuiCol.Text, ColorPurple);
    ImGui.Text("[R]");
    ImGui.PopStyleColor();
    if (ImGui.IsItemHovered())
        ImGui.SetTooltip("Resource Owning Node: Manages standing ECS resources via OnDeactivate.");
}
```

---

## Tests to Write

Write tests in:
- **`FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/InterpreterCleanupTests.cs`** (new file)

All tests use the existing `MockContext` / `TestBlackboard` pattern from
`tests/Fbt.Tests/TestFixtures/MockContext.cs`. Use `BTreeBuilder` + `ActionRegistry`
as per the compile-after-register pattern used in `HybridLifecycleTests.cs`.

### Test list

**T1 (regression — L-01 through L-08 style):** Verify EQL-003 scenarios still work after
the `_deactivatorDelegates` array removal.

Write one test: `Deactivator_FiresOnBranchSwitch_AfterRegistryCleanup`
- Build a Selector with two Actions: actionA (returns Failure), actionB (returns Running).
- Register actionA, actionB, and a deactivator for actionA.
- Compile AFTER register (BTreeBuilder.Compile with isResourceOwning delegate).
- Tick 1: actionA is evaluated, returns Failure. Selector moves to actionB. actionA has exited
  the active path. Assert deactivatorA fired exactly once.

**T2 (array absence):** `Interpreter_HasNo_DeactivatorDelegatesField`
```csharp
var fields = typeof(Interpreter<TestBlackboard, MockContext>)
    .GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
bool hasArray = fields.Any(f => f.FieldType.IsArray
    && f.FieldType.GetElementType() is { } et
    && et.IsGenericType
    && et.GetGenericTypeDefinition() == typeof(NodeDeactivatorDelegate<,>));
Assert.False(hasArray);
```

**T3 (no GC on construction):** `Constructor_ZeroResourceOwningNodes_NoGcPressure`
- Build a Selector with 500 Action children, no deactivators registered.
- Record `GC.CollectionCount(0)` before and after construction.
- Assert the count did not increase.
- Note: Use `Enumerable.Range(0, 500)` to add 500 actions. Each action must be registered in
  the registry (returns Running).

**T4 (correct deactivator):** `Deactivator_CorrectDelegateInvoked_NotOtherAction`
- Build a Selector with actionA (resource-owning, Running) and actionB (not resource-owning).
- Register both actions, register deactivator only for actionA.
- Tick 1: actionA starts Running. Manually clear state (simulate branch switch by resetting
  `state.RunningNodeIndex = 0`). Build a new tree with only actionB. Swap the blob (using
  hot-reload path: replace interpreter). Tick the new interpreter.
- Simpler alternative: Use two-level Selector where condition forces switch. See T4 example in
  HybridLifecycleTests for reference pattern.

---

## Build Verification

After all changes, build the following solutions / projects:

```
dotnet build FDP/ExtDeps/FastBTree/FastBTree.sln
dotnet build FDP/FDP.sln
```

Run the Fbt.Tests suite:
```
dotnet test FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj --no-build
```

Expected: 203+ passing, 9 pre-existing failures (generator-related), no new failures.
The 4 new `InterpreterCleanupTests` should all pass.

---

## Success Conditions Summary

| # | Condition | How to verify |
|---|-----------|---------------|
| T1 | Branch-switch deactivator fires (regression) | InterpreterCleanupTests.Deactivator_FiresOnBranchSwitch_AfterRegistryCleanup |
| T2 | No `_deactivatorDelegates` field in Interpreter | Reflection test |
| T3 | No GC pressure on construction | GC.CollectionCount assertion |
| T4 | Correct delegate called, not other action | Targeted deactivator test |
| T5 | Generated `Get*` signatures have `isResourceOwning` param | Check BTreeDefinitionGenerator output |
| T9 | All modified projects build without errors | `dotnet build` passes |

---

## Files Summary

| File | Action |
|------|--------|
| `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs` | Modify: remove `_deactivatorDelegates`, add `_registry`, update V1 fallback, update `SweepExitedNode`, delete `BindDeactivators` |
| `FDP/Toolkits/Fdp.Toolkits.Analyzers/BTreeDefinitionGenerator.cs` | Modify: `GenerateCatalog` adds `isResourceOwning` parameter to all `Get*` signatures |
| `Hrot/Subsystems/Hrot.AI.Behaviors/AiBehaviorFactory.cs` | Modify: add `isResourceOwning` delegate, pass to all `FbtTreeCatalog.Get*()` calls |
| `Hrot/Engine/Hrot.Presentation/Renderers/BTreeVisualizerRenderer.cs` | Modify: add `ColorPurple`, add `[R]` indicator after `TreeNodeEx` |
| `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/InterpreterCleanupTests.cs` | New: T1–T4 tests |

---

## Notes

- `Interpreter.BindActions` and `_actionDelegates` are NOT changed. Only the deactivator side is cleaned up.
- The `_registry` field stores the same `registry` reference that was previously only used during construction. It must be declared `private readonly`.
- The V1 fallback loop now uses `_registry.TryGetDeactivator(_blob.MethodNames[pi], out _)` instead of checking the array. The semantics are identical: it patches in-memory `IsResourceOwning` bits for V1 blobs where deactivators are registered.
- `FbtTreeCatalog` is generated by `BTreeDefinitionGenerator.cs`. The source generator produces a new `.g.cs` file each time the assembly recompiles. The parameter change takes effect automatically on the next build.
- `AiBehaviorFactory` uses `FbtTreeCatalog` directly. After the generator update, the `Get*` methods accept the delegate. The factory must pass it so that compiled V2 blobs carry the `IsResourceOwning` bits.
- T6 (factory wiring) and T7 (hot-reload end-to-end) from TASK-DETAIL are integration tests requiring a running simulation; skip them in the automated test suite and note them as manual verification items in the batch report.
- T8 (editor indicator) is a visual test; verify by reading the code change and noting in the report that it matches the spec.
