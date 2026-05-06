# BATCH-15 Implementation Instructions

**Tasks:** GZ039  
**Agent:** Claude Sonnet 4.6  
**Task details:** `.dev/gizmos-1/TASK-DETAIL.md` (section TASK-GZ039)  
**Tracker:** `.dev/gizmos-1/TASK-TRACKER.md`

---

## MANDATORY READING BEFORE STARTING

1. Read `.dev/gizmos-1/TASK-DETAIL.md` section for TASK-GZ039 in full.
2. Read `AGENTS.md` at workspace root for coding standards.
3. Read `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs` — understand current constructor and commit handling.
4. Read `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IStatefulGizmo.cs` — understand the interface to extend.
5. Read `FDP/Engine/Fdp.Core/Abstractions/IEntityCommandBuffer.cs` — understand the interface GizmoUndoRecord.Undo/Redo receive.
6. Read `Hrot/Subsystems/Hrot.IG/IgApplication.cs` lines 1280–1310 (Update method context) and lines 1686–1750 (HandleCameraInput pattern for keyboard input).
7. Read `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmosSystemTests.cs` — test patterns.

---

## Pre-existing Failures (Do NOT count against your work)

- ~26 tests in `Fdp.Toolkits.Tests` (AimAndFire, MissionDirector, etc.)
- ~4 tests in `Hrot.IG.Tests` (CS011_ EntityInfoTranslator)
- ~3 tests in `Fdp.Presentation.Tests` (EntityInspectorPanelTests)
- ~20 tests in `Hrot.SimHost.Tests`

---

## GZ039 — Undo/Redo Stack for Gizmo Interactions

### Step 1 — IGizmoUndoRecord interface

**File to create:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/UndoRedo/IGizmoUndoRecord.cs`

```csharp
using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics.Gizmos.UndoRedo
{
    /// <summary>
    /// Encapsulates a reversible gizmo interaction. Implementations are created by
    /// stateful gizmos via <see cref="IStatefulGizmo.CreateUndoRecord"/>.
    /// </summary>
    public interface IGizmoUndoRecord
    {
        /// <summary>Human-readable label for status bar (e.g. "Move entity 42").</summary>
        string Description { get; }

        /// <summary>
        /// Re-applies the committed change. Called when the user triggers Redo.
        /// Must be idempotent.
        /// </summary>
        void Redo(IEntityCommandBuffer cmd);

        /// <summary>
        /// Reverts the committed change. Called when the user triggers Undo.
        /// Must be idempotent.
        /// </summary>
        void Undo(IEntityCommandBuffer cmd);
    }
}
```

### Step 2 — GizmoUndoStack

**File to create:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/UndoRedo/GizmoUndoStack.cs`

```csharp
using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics.Gizmos.UndoRedo
{
    /// <summary>
    /// Manages undo/redo history for gizmo interactions.
    /// Not thread-safe — call only from the ECS/render thread.
    /// </summary>
    public sealed class GizmoUndoStack
    {
        private readonly Stack<IGizmoUndoRecord> _undoStack = new();
        private readonly Stack<IGizmoUndoRecord> _redoStack = new();

        public int MaxDepth { get; init; } = 50;

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;
        public string UndoDescription => CanUndo ? _undoStack.Peek().Description : string.Empty;
        public string RedoDescription => CanRedo ? _redoStack.Peek().Description : string.Empty;

        /// <summary>
        /// Records a new committed action. Clears the redo stack (new branch).
        /// Drops the oldest entry if depth would exceed <see cref="MaxDepth"/>.
        /// </summary>
        public void Push(IGizmoUndoRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            // If at capacity, rebuild stack without the oldest (bottom) entry.
            if (_undoStack.Count >= MaxDepth)
            {
                var items = _undoStack.ToArray(); // index 0 = top (newest)
                _undoStack.Clear();
                // Re-push all except the last (oldest, index Count-1), then the new record.
                for (int i = items.Length - 2; i >= 0; i--)
                    _undoStack.Push(items[i]);
            }

            _undoStack.Push(record);
            _redoStack.Clear();
        }

        /// <summary>
        /// Performs the undo operation. No-op if <see cref="CanUndo"/> is false.
        /// </summary>
        public void Undo(IEntityCommandBuffer cmd)
        {
            if (!CanUndo) return;
            var record = _undoStack.Pop();
            record.Undo(cmd);
            _redoStack.Push(record);
        }

        /// <summary>
        /// Performs the redo operation. No-op if <see cref="CanRedo"/> is false.
        /// </summary>
        public void Redo(IEntityCommandBuffer cmd)
        {
            if (!CanRedo) return;
            var record = _redoStack.Pop();
            record.Redo(cmd);
            _undoStack.Push(record);
        }

        /// <summary>
        /// Clears both undo and redo history. Call on world/scenario reset.
        /// </summary>
        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }
}
```

**Note on `Push` overflow logic:** `Stack<T>.ToArray()` returns items with index 0 = top (most recently pushed). Index `Length-1` is the oldest. We drop index `Length-1` (oldest) and push items from index `Length-2` down to 0 (bottom-to-top order, so we push in reverse), then push the new record.

Verify the logic by tracing: if `MaxDepth=2` and stack has [B(top), A(bottom)]:
- `ToArray()` → `[B, A]` (index 0=top=B, index 1=bottom=A)
- Loop from `items.Length-2 = 0` down to `0`: push `items[0]` = B
- Stack now: `[B]`
- Then `_undoStack.Push(record)` → `[record(top), B]`
- Old top B is kept, old bottom A is dropped. ✓

### Step 3 — Extend IStatefulGizmo

**File to modify:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IStatefulGizmo.cs`

First, read the current file to understand the interface structure. Then add:

```csharp
// Add using at top:
using Fdp.Toolkit.Diagnostics.Gizmos.UndoRedo;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;

// Add to IStatefulGizmo interface body:
/// <summary>
/// Returns an undo record for the most recent committed interaction, or
/// <c>null</c> if this gizmo does not support undo.
/// Default implementation returns <c>null</c> (opt-out).
/// Called by <see cref="DataDrivenGizmoSystem"/> after processing
/// <see cref="GizmoInteractionCommitEvent"/>.
/// </summary>
virtual IGizmoUndoRecord? CreateUndoRecord(GizmoInteractionCommitEvent commit) => null;
```

C# 8+ supports default interface methods. Verify `IStatefulGizmo` is an `interface` (not abstract class) before adding the default implementation.

### Step 4 — DataDrivenGizmoSystem Integration

**File to modify:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs`

1. **Add field:**
   ```csharp
   private readonly GizmoUndoStack? _undoStack;
   ```

2. **Update constructor(s)** to accept optional `GizmoUndoStack?`:
   ```csharp
   public DataDrivenGizmoSystem(
       GizmoRegistry registry,
       DebugPrimitiveBuffer buffer,
       Func<ISimulationView, Entity, bool>? isSelectedPredicate = null,
       GizmoUndoStack? undoStack = null)
   {
       // existing assignments...
       _undoStack = undoStack;
   }
   ```
   Add `using Fdp.Toolkit.Diagnostics.Gizmos.UndoRedo;` at top.

3. **After each `GizmoInteractionCommitEvent` is handled,** push the undo record:
   ```csharp
   // After the gizmo's commit handling (wherever GizmoInteractionCommitEvent is drained):
   var record = gizmoInstance.CreateUndoRecord(commitEvent);
   if (record != null)
       _undoStack?.Push(record);
   ```

   To find the exact location: search `DataDrivenGizmoSystem` for where it reads/drains
   `GizmoInteractionCommitEvent` and calls the gizmo's commit handler. Add the push AFTER
   the gizmo has processed the event (so the gizmo has already captured its before/after state).

### Step 5 — IgApplication Keyboard Shortcuts

**File to modify:** `Hrot/Subsystems/Hrot.IG/IgApplication.cs`

1. **Add field** (near the other gizmo fields, around line 237):
   ```csharp
   private GizmoUndoStack? _gizmoUndoStack;
   ```
   Add using: `using Fdp.Toolkit.Diagnostics.Gizmos.UndoRedo;`

2. **Initialize** the undo stack where `DataDrivenGizmoSystem` was previously constructed (around where the gizmo buffer is initialized, ~line 1126):
   ```csharp
   _gizmoUndoStack = new GizmoUndoStack();
   ```

3. **Add private method** `HandleGizmoUndoInput()`:
   ```csharp
   private void HandleGizmoUndoInput()
   {
       if (_gizmoUndoStack == null) return;
       if (ImGui.GetIO().WantCaptureKeyboard) return;

       bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl)
                || Raylib.IsKeyDown(KeyboardKey.RightControl);
       if (!ctrl) return;

       bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift)
                 || Raylib.IsKeyDown(KeyboardKey.RightShift);

       if (Raylib.IsKeyPressed(KeyboardKey.Z) && !shift)
       {
           if (_gizmoUndoStack.CanUndo)
           {
               var cmd = ((ISimulationView)_world).GetCommandBuffer();
               _gizmoUndoStack.Undo(cmd);
               cmd.Playback(_world);
           }
           return;
       }

       if (Raylib.IsKeyPressed(KeyboardKey.Y) || (Raylib.IsKeyPressed(KeyboardKey.Z) && shift))
       {
           if (_gizmoUndoStack.CanRedo)
           {
               var cmd = ((ISimulationView)_world).GetCommandBuffer();
               _gizmoUndoStack.Redo(cmd);
               cmd.Playback(_world);
           }
       }
   }
   ```
   
   **Check:** Verify `cmd.Playback(_world)` is the correct call signature by looking at  
   the existing usage at line ~2316.

4. **Call `HandleGizmoUndoInput()`** from within the `Update` method, in the block that guards  
   against `ImGui.GetIO().WantCaptureMouse` or nearby:
   ```csharp
   HandleGizmoUndoInput();
   ```
   Place it near the existing `HandleCameraInput(dt)` call (around line 1285). Place it OUTSIDE  
   the `WantCaptureMouse` guard (keyboard capture and mouse capture are independent).

5. **Clear undo stack on world reset:** In the `Update` method (or wherever `WorldResetEvent` is  
   handled), add:
   ```csharp
   foreach (var _ in _world.Bus.ReadManaged<WorldResetEvent>())
       _gizmoUndoStack?.Clear();
   ```
   Check the current `Update` method for an existing `WorldResetEvent` subscription pattern  
   to place this correctly. If `ReadManaged<WorldResetEvent>()` doesn't exist on the bus  
   directly, use `view.ReadManagedEvents<WorldResetEvent>()`.

   **Note:** `WorldResetEvent` is in `Hrot.Presentation` (namespace `Hrot.Presentation` or similar).  
   Add the appropriate using. If the type isn't directly accessible, check which project  
   `Hrot.IG` already references by looking at `Hrot.IG.csproj`.

---

## Tests for GZ039

**File to create:** `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmoUndoStackTests.cs`

Test helpers:

```csharp
internal sealed class MockUndoRecord : IGizmoUndoRecord
{
    public string Description => "Mock";
    public int UndoCallCount { get; private set; }
    public int RedoCallCount { get; private set; }
    public void Undo(IEntityCommandBuffer cmd) => UndoCallCount++;
    public void Redo(IEntityCommandBuffer cmd) => RedoCallCount++;
}
```

**SC-GZ039-1**: Push then Undo calls Undo and moves to redo stack:
```csharp
[Fact]
public void SC_GZ039_1_Push_Then_Undo_CallsUndoAndMovesRecord()
{
    var stack = new GizmoUndoStack();
    var record = new MockUndoRecord();
    stack.Push(record);

    stack.Undo(null!); // cmd is null — MockUndoRecord ignores it

    Assert.Equal(1, record.UndoCallCount);
    Assert.False(stack.CanUndo);
    Assert.True(stack.CanRedo);
}
```

**SC-GZ039-2**: Undo then Redo calls Redo and moves record back:
```csharp
[Fact]
public void SC_GZ039_2_Undo_Then_Redo_CallsRedoAndMovesBack()
{
    var stack = new GizmoUndoStack();
    var record = new MockUndoRecord();
    stack.Push(record);
    stack.Undo(null!);

    stack.Redo(null!);

    Assert.Equal(1, record.RedoCallCount);
    Assert.True(stack.CanUndo);
    Assert.False(stack.CanRedo);
}
```

**SC-GZ039-3**: Push beyond MaxDepth drops oldest entry:
```csharp
[Fact]
public void SC_GZ039_3_Push_BeyondMaxDepth_DropsOldest()
{
    var stack = new GizmoUndoStack { MaxDepth = 3 };
    var r1 = new MockUndoRecord();
    var r2 = new MockUndoRecord();
    var r3 = new MockUndoRecord();
    var r4 = new MockUndoRecord();

    stack.Push(r1); stack.Push(r2); stack.Push(r3);
    stack.Push(r4); // this should drop r1

    Assert.Equal(3, /* stack depth approximation — check CanUndo */ 3);
    // Verify: r1 is gone. Undo 3 times: expect r4, r3, r2 — NOT r1.
    // Use UndoCallCount to trace.
    stack.Undo(null!); // r4
    stack.Undo(null!); // r3
    stack.Undo(null!); // r2
    Assert.False(stack.CanUndo); // r1 was evicted
    Assert.Equal(1, r2.UndoCallCount);
    Assert.Equal(0, r1.UndoCallCount); // r1 was dropped
}
```

**SC-GZ039-4**: Push clears redo stack:
```csharp
[Fact]
public void SC_GZ039_4_Push_ClearsRedoStack()
{
    var stack = new GizmoUndoStack();
    var r1 = new MockUndoRecord();
    var r2 = new MockUndoRecord();
    stack.Push(r1);
    stack.Undo(null!); // r1 moves to redo
    Assert.True(stack.CanRedo);

    stack.Push(r2); // new action invalidates redo

    Assert.False(stack.CanRedo);
    Assert.True(stack.CanUndo);
}
```

**SC-GZ039-5**: Undo when CanUndo==false is no-op:
```csharp
[Fact]
public void SC_GZ039_5_Undo_WhenEmpty_NoOp()
{
    var stack = new GizmoUndoStack();
    stack.Undo(null!); // must not throw
    Assert.False(stack.CanUndo);
}
```

**SC-GZ039-6**: Redo when CanRedo==false is no-op:
```csharp
[Fact]
public void SC_GZ039_6_Redo_WhenEmpty_NoOp()
{
    var stack = new GizmoUndoStack();
    stack.Redo(null!); // must not throw
    Assert.False(stack.CanRedo);
}
```

**SC-GZ039-7**: DataDrivenGizmoSystem pushes returned record after commit:
```csharp
[Fact]
public void SC_GZ039_7_DataDrivenGizmoSystem_PushesRecord_AfterCommit()
{
    // This test verifies integration, not just the stack.
    // Create a minimal gizmo with a stub CreateUndoRecord that returns a mock record.
    // Register it in DataDrivenGizmoSystem with a GizmoUndoStack.
    // Fire a GizmoInteractionCommitEvent for the entity.
    // Assert: undoStack.CanUndo == true.
    //
    // Use the test scaffolding from GizmosSystemTests.cs as a pattern:
    // - Create EntityRepository, register gizmo, create entity with the right component
    // - Publish GizmoInteractionCommitEvent → SwapBuffers → Execute DataDrivenGizmoSystem
    // - Assert undoStack.CanUndo == true

    // IMPLEMENTATION GUIDANCE:
    // If DataDrivenGizmoSystem's commit handling is complex to test due to component
    // requirements, create the simplest possible mock gizmo definition that always
    // returns CreateUndoRecord = MockUndoRecord.
    // Look at GizmosSystemTests.cs to understand how to bootstrap a gizmo system with
    // a minimal gizmo that activates.
}
```

**SC-GZ039-8**: Gizmo returning null from CreateUndoRecord does not push anything:
```csharp
[Fact]
public void SC_GZ039_8_Null_CreateUndoRecord_DoesNotPush()
{
    // Similar to SC-GZ039-7 but with a gizmo whose CreateUndoRecord returns null.
    // Assert: undoStack.CanUndo == false after commit event.
}
```

For SC-GZ039-7 and SC-GZ039-8, if they are difficult to implement in isolation, you may skip  
them in favor of SC-GZ039-1 through SC-GZ039-6 which directly verify `GizmoUndoStack`.  
Document any skipped tests in the report with reason.

---

## Build & Test Validation

```
dotnet build d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln --no-incremental -clp:ErrorsOnly
```
→ **Must show 0 errors.**

```
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --no-build --filter "SC_GZ039" --logger "console;verbosity=normal"
```
→ SC-GZ039-1 through SC-GZ039-6 (minimum) pass.

---

## Commit Instructions

**Step 1 — FDP submodule:**
```
cd d:\Work\IOS-IG-SimHost-FDP-2\FDP
git add -A
git commit -m "GZ039: IGizmoUndoRecord, GizmoUndoStack, DataDrivenGizmoSystem integration"
```

**Step 2 — Root repo:**
```
cd d:\Work\IOS-IG-SimHost-FDP-2
git add -A
git commit -m "GZ039: Gizmo undo/redo keyboard shortcuts in IgApplication"
```

---

## Batch Report

Create `.dev/gizmos-1/reports/BATCH-15-REPORT.md` documenting:
- Files created/modified
- Test counts
- Build result
- Any deviations (e.g., SC-GZ039-7/8 skipped due to complexity)
- Notes on `WorldResetEvent` subscription approach

Update `.dev/gizmos-1/TASK-TRACKER.md`: mark GZ039 as `[x]` done.
