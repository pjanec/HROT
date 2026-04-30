# BATCH-10 Report: Phase 5 Visual App + Hot Reload Demo (FBT-043, FBT-045)

**Batch:** BATCH-10
**Tasks:** FBT-043, FBT-045
**Date:** 2026-04-30
**Status:** Complete

---

## Q1: GUID assigned to `Fbt.Examples.FluentBTree.Trees` in `FastBTree.sln`

`{EE74D0B1-B6DC-4BA3-90E3-F88AB9629C14}`

Generated via `[System.Guid]::NewGuid().ToString("B").ToUpper()` in PowerShell.

---

## Q2: Did `interpreter.Blob` exist in `Interpreter.cs`?

Yes. The exact property signature is:

```csharp
public BehaviorTreeBlob Blob => _blob;
```

Found at line 18 of `src/Fbt.Kernel/Runtime/Interpreter.cs`. Added in BATCH-08.

---

## Q3: `BTreeHotReloadManager.TryReload` signature and `SpanResetAction` delegate

Exact method signature:
```csharp
public ReloadResult TryReload<TState>(
    string treeName,
    BehaviorTreeBlob? newBlob,
    Span<TState> liveInstances,
    SpanResetAction<TState>? hardResetAction)
    where TState : unmanaged
```

`SpanResetAction<TState>` is defined in `BTreeHotReloadManager.cs`:
```csharp
public delegate void SpanResetAction<TState>(Span<TState> span, int index);
```

In `Program.cs`, the call passes a single-element array and uses `.AsSpan()` to get the span, then updates `state` from `stateArr[0]` after the call (since `Span<T>` cannot be stored in a closure or lambda field when `TState` is unmanaged).

---

## Q4: Did the visual app compile without errors?

Yes, `Fbt.Examples.FluentBTree.csproj` compiled with:
```
Build succeeded.
    0 Error(s)
```

No warnings were produced in the output.

---

## Q5: Did the 160 existing tests continue to pass?

Yes:
```
Passed!  - Failed: 0, Passed: 160, Skipped: 0, Total: 160
```

No regressions.

---

## Q6: Deviations from instructions and root cause

1. **Missing `using Fbt.Runtime;` in Program.cs** - The instructions' reference skeleton did not include `Fbt.Runtime` in the using directives, but `ActionRegistry<,>` and `Interpreter<,>` live in that namespace. Added `using Fbt.Runtime;` to fix the compilation error (CS0246).

2. **`Fbt.Examples.FluentBTree.csproj` Trees reference path** - The instructions initially showed a placeholder path. The actual correct relative path (from `examples/Fbt.Examples.FluentBTree/`) is `../Fbt.Examples.FluentBTree.Trees/Fbt.Examples.FluentBTree.Trees.csproj`, which matches the note in the "Common Pitfalls" section. Used this path.

3. **Hot reload `OnReloadCompleted` callback and state array** - `Span<T>` cannot be captured in a lambda or stored as a field. The implementation uses a single-element `BehaviorTreeState[]` array created in the callback, calls `TryReload` with `.AsSpan()`, then reads back `stateArr[0]` (only if `HardReset`). This matches the intent of the instructions without violating C# ref-struct restrictions.

---

## Files Created

- `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree.Trees/Fbt.Examples.FluentBTree.Trees.csproj` (new)
- `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree.Trees/CombatBlackboard.cs` (moved from FluentBTree)
- `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree.Trees/CombatActions.cs` (moved from FluentBTree)
- `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree.Trees/AmbushTree.cs` (moved from FluentBTree)

## Files Modified

- `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/Fbt.Examples.FluentBTree.csproj` - added ImGui/Raylib/rlImgui packages, Trees project reference; removed direct source file references
- `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/Program.cs` - replaced console demo with Raylib/ImGui visual app + hot reload
- `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj` - added reference to `Fbt.Examples.FluentBTree.Trees`
- `FDP/ExtDeps/FastBTree/FastBTree.sln` - added `Fbt.Examples.FluentBTree.Trees` project entry, config platforms, and nested project assignment

## Files Deleted (moved to Trees)

- `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/CombatBlackboard.cs`
- `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/CombatActions.cs`
- `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/AmbushTree.cs`
