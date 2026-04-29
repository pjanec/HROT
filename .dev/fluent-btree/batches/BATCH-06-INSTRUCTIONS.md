# BATCH-06: BTreeHotReloadManager + Interpreter Hot Reload Check + Tests

**Batch Number:** BATCH-06
**Tasks:** FBT-020, FBT-021, FBT-022 (partial — FBT-023 tests in BATCH-07)
**Phase:** Phase 3 (BTreeHotReloadManager)
**Estimated Effort:** 6-8 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 through BATCH-05

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Task Details:** `.dev/fluent-btree/TASK-DETAIL.md` — FBT-020, FBT-021, FBT-022
2. **BehaviorTreeState:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/BehaviorTreeState.cs`
3. **BehaviorTreeBlob:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/BehaviorTreeBlob.cs` (has `StructureHash`, `ParamHash`)
4. **Interpreter:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs` (has HOT RELOAD STUB comment at line ~28)

### Build and Test Commands

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build FDP/ExtDeps/FastBTree/FastBTree.sln -v quiet 2>&1 | Select-String "error|Build succeeded|FAILED"
dotnet test FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 3
```

### Report Submission

`.dev/fluent-btree/reports/BATCH-06-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW

1. **FBT-020:** Create `BTreeHotReloadManager` + `ReloadResult` enum → test
2. **FBT-021:** Implement hot reload safety check in `Interpreter.Tick` → test
3. **FBT-022:** Write tests for FBT-020 and FBT-021 → all pass
4. Run all 135 existing tests — must still pass

---

## ✅ Tasks

### Task 1: BTreeHotReloadManager (FBT-020)

**New files:**
- `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/HotReload/ReloadResult.cs`
- `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/HotReload/BTreeHotReloadManager.cs`

#### ReloadResult.cs

```csharp
namespace Fbt.HotReload
{
    public enum ReloadResult
    {
        /// <summary>First time this tree name has been registered.</summary>
        NewTree,
        /// <summary>Both StructureHash and ParamHash are identical — no reload needed.</summary>
        NoChange,
        /// <summary>Only ParamHash changed (floats/ints). Entity state is preserved.</summary>
        SoftReload,
        /// <summary>StructureHash changed. Entity states reset via hardResetAction.</summary>
        HardReset,
    }
}
```

#### BTreeHotReloadManager.cs

The manager is a standalone component that:
1. Maintains a `Dictionary<string, BehaviorTreeBlob>` of registered blobs by tree name
2. On `TryReload`, compares `StructureHash` and `ParamHash` to determine the result
3. Calls `hardResetAction` on each instance for `HardReset`
4. Does NOT reference `DoctrineRegistry` (which is in Fdp.Toolkits) — the caller is responsible for patching the registry

```csharp
using System;
using System.Collections.Generic;

namespace Fbt.HotReload
{
    /// <summary>
    /// Manages hot reload of behavior tree blobs.
    /// Tracks registered blobs and computes reload results by comparing structure/param hashes.
    /// DoctrineRegistry patching is the caller's responsibility.
    /// </summary>
    public class BTreeHotReloadManager
    {
        private readonly Dictionary<string, BehaviorTreeBlob> _knownBlobs
            = new Dictionary<string, BehaviorTreeBlob>(StringComparer.Ordinal);

        /// <summary>
        /// Determines the reload result for a new blob and updates the internal registry.
        /// Calls <paramref name="hardResetAction"/> on each span element when a HardReset occurs.
        /// Never throws; guards against null newBlob.
        /// </summary>
        public ReloadResult TryReload<TState>(
            string treeName,
            BehaviorTreeBlob? newBlob,
            Span<TState> liveInstances,
            Action<TState>? hardResetAction)
            where TState : unmanaged
        {
            if (newBlob == null) return ReloadResult.NoChange;

            if (!_knownBlobs.TryGetValue(treeName, out var oldBlob))
            {
                _knownBlobs[treeName] = newBlob;
                return ReloadResult.NewTree;
            }

            if (oldBlob.StructureHash == newBlob.StructureHash &&
                oldBlob.ParamHash == newBlob.ParamHash)
            {
                return ReloadResult.NoChange;
            }

            _knownBlobs[treeName] = newBlob;

            if (oldBlob.StructureHash != newBlob.StructureHash)
            {
                // Structure changed -- reset all live instances
                if (hardResetAction != null)
                {
                    for (int i = 0; i < liveInstances.Length; i++)
                    {
                        hardResetAction(liveInstances[i]);
                    }
                }
                return ReloadResult.HardReset;
            }

            // Only params changed
            return ReloadResult.SoftReload;
        }

        /// <summary>
        /// Returns the currently registered blob for the given tree name, or null if not known.
        /// </summary>
        public BehaviorTreeBlob? GetKnownBlob(string treeName)
        {
            _knownBlobs.TryGetValue(treeName, out var blob);
            return blob;
        }
    }
}
```

**Note:** `BehaviorTreeState.Reset()` does not exist as a method. The `hardResetAction` delegate is how callers reset state (e.g., HROT's integration layer passes `state => { state.RunningNodeIndex = 0; state.TreeVersion++; }`). The manager calls `hardResetAction(instance)` for each instance — but since `TState` is `unmanaged` and the action takes `TState` by value, mutations won't be visible through the span. Consider using an `ActionRef<TState>` pattern or have `hardResetAction` take a `ref TState` — but `Action<ref T>` is not valid in C#.

**Resolution:** Change `TState` to use index-based reset:
```csharp
public ReloadResult TryReload<TState>(
    string treeName,
    BehaviorTreeBlob? newBlob,
    Span<TState> liveInstances,
    SpanAction<TState>? hardResetAction)   // Can't use Action<ref TState>
    where TState : unmanaged
```
Actually `SpanAction<T>` doesn't exist. Use a simpler pattern: pass the `Span<TState>` and an index-based reset action `Action<Span<TState>, int>`:

```csharp
public ReloadResult TryReload<TState>(
    string treeName,
    BehaviorTreeBlob? newBlob,
    Span<TState> liveInstances,
    Action<Span<TState>, int>? hardResetAction)
    where TState : unmanaged
{
    ...
    for (int i = 0; i < liveInstances.Length; i++)
    {
        hardResetAction?.Invoke(liveInstances, i);
    }
    ...
}
```

The caller would write: `(span, i) => { span[i].SomeField = 0; }` — but since `TState` is `unmanaged`, `span[i]` returns a ref, so mutations ARE visible. This works correctly.

OR even simpler: since tests use `BehaviorTreeState` directly, just test with `Action<Span<BehaviorTreeState>, int>` or use a `bool[] resetCalled` array to verify the action was invoked. **Pick the simplest design that allows verifying the reset was called AND that mutations are visible.**

**Recommended: Use this signature:**
```csharp
public ReloadResult TryReload<TState>(
    string treeName,
    BehaviorTreeBlob? newBlob,
    Span<TState> liveInstances,
    Action<Span<TState>, int>? hardResetAction)
    where TState : unmanaged
```
This allows `(span, i) => span[i] = default` for a simple reset.

---

### Task 2: Interpreter Hot Reload Safety Check (FBT-021)

**File to modify:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs`

Replace the `// === HOT RELOAD CHECK (Stub for now) ===` comment with:

```csharp
// === HOT RELOAD CHECK ===
// Safety net: if the state's running node index is out of bounds for THIS blob
// (can happen if a new Interpreter is created with a structurally different blob
// while an entity is mid-execution), reset state to prevent out-of-bounds access.
if (state.RunningNodeIndex > 0 && state.RunningNodeIndex >= _blob.Nodes.Length)
{
    state.RunningNodeIndex = 0;
    state.StackPointer = 0;
    unchecked { state.TreeVersion++; }
}
```

This is the correct safety net: if `RunningNodeIndex` is out of range for the new blob (because the tree structure changed and the new tree has fewer nodes), reset state. This prevents array out-of-bounds panics on hot reload.

Also add to the `Interpreter` constructor a private field:
```csharp
// Used for diagnostics/debugging — not currently used in tick but available for hot reload introspection.
private readonly int _blobStructureHash;
```

And in constructor: `_blobStructureHash = blob.StructureHash;`

**This is a minimal change** — do not rewrite the Interpreter. Only replace the stub comment with the bounds check.

---

### Task 3: Tests for FBT-020 and FBT-021 (FBT-022 partial)

**New test file:** `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/HotReloadTests.cs`

**Tests (minimum 8):**

**FBT-020 tests:**

```
TryReload_NewTree_ReturnsNewTree
    Setup: empty manager, call TryReload with any blob
    Assert: result == ReloadResult.NewTree

TryReload_NoChange_WhenHashesIdentical
    Setup: register blob B1; call TryReload with blob B2 having same StructureHash and ParamHash
    Assert: result == ReloadResult.NoChange

TryReload_SoftReload_WhenOnlyParamHashDiffers
    Setup: register blob B1 (StructureHash=X, ParamHash=Y); call TryReload with B2 (StructureHash=X, ParamHash=Z)
    Assert: result == ReloadResult.SoftReload

TryReload_HardReset_WhenStructureHashDiffers
    Setup: register blob B1 (StructureHash=X); call TryReload with B2 (StructureHash=Y)
    Assert: result == ReloadResult.HardReset

TryReload_HardReset_CallsHardResetAction_OnAllInstances
    Setup: register blob B1; create Span<SomeState> with 3 elements; provide hardResetAction that zeros each element
    Action: TryReload with different StructureHash blob
    Assert: all 3 instances were reset (verified by checking a sentinel field)

TryReload_NullBlob_ReturnsNoChange
    Setup: call TryReload with null newBlob
    Assert: result == ReloadResult.NoChange, no exception

TryReload_SoftReload_DoesNotCallHardResetAction
    Setup: register blob B1; call TryReload with B2 (same structure, different params); provide action that sets a flag
    Assert: flag is NOT set (action not called for SoftReload)

TryReload_EmptySpan_HardReset_DoesNotThrow
    Setup: register blob B1; call TryReload with different structure blob; pass empty Span<int>
    Assert: returns HardReset, no exception
```

**FBT-021 tests:**

```
Interpreter_HotReloadCheck_ResetsState_WhenRunningIndexOutOfBounds
    Setup: Compile a 1-node tree (blob A). Manually set state.RunningNodeIndex = 5 (out of bounds).
    Action: Tick with the interpreter.
    Assert: Tick completes without exception. After tick, state.RunningNodeIndex != 5 (was reset).

Interpreter_HotReloadCheck_DoesNotResetState_WhenRunningIndexValid
    Setup: Compile a tree with >=5 nodes (e.g. Sequence with 4 actions). Set state.RunningNodeIndex = 2.
    Action: Tick with the interpreter.
    Assert: Tick completes without exception. State was not force-reset at index 0 by the bounds check.
    (Note: tick may still reset state if it completes — verify check specifically)
```

**How to create two blobs with known different hashes in tests:**
- `BTreeBuilder.Compile("Tree1")` produces a blob with a certain StructureHash
- `BTreeBuilder.Compile("Tree2")` with a different structure produces a different StructureHash
- To create blobs with same StructureHash but different ParamHash: compile two trees with same node structure but different float parameters (e.g., `Wait(1.0f)` vs `Wait(2.0f)` — same structure, different float param → different ParamHash)
- To create blobs with same StructureHash and ParamHash: compile the same tree twice

Verify by checking `blob.StructureHash` and `blob.ParamHash` values in tests — add `Assert.Equal` to confirm assumptions.

**Test struct for HardReset testing:**
```csharp
private struct SimpleState
{
    public int Value;
}
```

---

## ⚠️ Quality Standards

- `BTreeHotReloadManager` must not reference any FDP/HROT-specific types
- `Action<Span<TState>, int>` allows in-place mutation of span elements
- Zero warnings in `Fbt.Tests` (TreatWarningsAsErrors)
- Interpreter change must be minimal — only the stub comment is replaced

---

## 📊 Report Requirements

Create `.dev/fluent-btree/reports/BATCH-06-REPORT.md`:

```markdown
# BATCH-06 Report

## Summary

## Tasks Completed
- [ ] FBT-020: BTreeHotReloadManager + ReloadResult enum
- [ ] FBT-021: Interpreter.Tick hot reload safety check
- [ ] FBT-022 (partial): Tests for FBT-020 and FBT-021

## Test Results
Total passing: XX / XX

## Developer Insights

**Q1:** Issues encountered (especially around generic Span+Action patterns)?

**Q2:** Design decisions?

**Q3:** Weak points?

**Suggested commit message:**
```
```

---

## 🎯 Success Criteria

- [ ] `Fbt.Kernel/HotReload/ReloadResult.cs` and `BTreeHotReloadManager.cs` exist
- [ ] `BTreeHotReloadManager.TryReload<TState>` returns all 4 ReloadResult values in appropriate scenarios
- [ ] `Interpreter.Tick` has bounds check replacing the stub comment
- [ ] `HotReloadTests.cs` with at least 10 tests (8 manager + 2 interpreter)
- [ ] All 135 existing tests still pass
- [ ] Zero build errors or warnings

---

## ⚠️ Common Pitfalls

- `BehaviorTreeState` uses `[StructLayout(LayoutKind.Explicit, Size = 64)]` — do NOT add new fields directly to it. If you need to track version in state, use `TreeVersion` which already exists.
- `Action<ref TState>` is not valid C# — use `Action<Span<TState>, int>` for index-based in-place mutation
- When comparing blob hashes in tests: be careful about `Wait(float)` duration — verify what affects `StructureHash` vs `ParamHash` by reading `TreeCompiler.CalculateStructureHash` and `CalculateParamHash`
- `Interpreter.Tick` uses `unsafe` internally — the bounds check must not introduce new unsafe code. `state.RunningNodeIndex` is a `ushort` and `_blob.Nodes.Length` is an `int`. Cast appropriately: `(int)state.RunningNodeIndex >= _blob.Nodes.Length`

---

## 📚 Reference Materials

- **FBT-020 Task Detail:** `.dev/fluent-btree/TASK-DETAIL.md` → TASK-FBT-020
- **FBT-021 Task Detail:** `.dev/fluent-btree/TASK-DETAIL.md` → TASK-FBT-021
- **BehaviorTreeState:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/BehaviorTreeState.cs`
- **Interpreter:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs`
- **TreeCompiler hash methods:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Serialization/TreeCompiler.cs`
