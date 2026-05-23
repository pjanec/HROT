# BATCH-29 — HS-S1-13 through HS-S1-17

## Tasks
- TASK-HS-S1-13: `hsm.transition_labels` custom renderer (AfterWires)
- TASK-HS-S1-14: Internal-transition rendering (dashed loop label placement)
- TASK-HS-S1-15: `hsm.initial_state_arrows` custom renderer (AfterNodes)
- TASK-HS-S1-16: Events table window (`hsm_events`)
- TASK-HS-S1-17: Global transitions strip

## Non-negotiable rules
1. No Unicode characters in comments or string literals (ASCII only).
2. Build must succeed with 0 errors and 0 warnings.
3. All 51 existing tests must continue passing.
4. Do not modify any existing file unless required by these tasks.
5. Preserve existing comments exactly.

---

## Overview

Create 4 new source files and their unit tests. These are all structural implementations:
two custom canvas renderers (primarily ImGui-based, so the Render() method is a
structural stub) plus two window/strip stubs.

### File locations

```
Hrot/Subsystems/AI/Hrot.Hsm.Editor/
  Renderers/
    HsmTransitionLabelRenderer.cs   <-- CREATE
    HsmInitialArrowRenderer.cs      <-- CREATE
  Windows/
    HsmEventsWindow.cs              <-- CREATE
    HsmGlobalsStrip.cs              <-- CREATE

Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/
  HsmTransitionLabelRendererTests.cs  <-- CREATE
  HsmRendererRegistrationTests.cs     <-- CREATE
```

---

## Step 1 — Understand existing code

### 1.1 Read HsmAsset.cs to understand TransitionNode fields

File: `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs`

Important fields on `TransitionNode`:
```
public string? EventName;      // symbolicated event name; null means no event
public string? GuardFunction;  // FQN or short name; null means no guard
public string? ActionFunction; // FQN or short name; null means no action
public byte Priority;          // default = 128
public TransitionKind Kind;    // External, Internal, Local
public ushort SyncGroupId;     // 0 means no sync group
```

Important fields on `HsmAsset`:
- `GlobalTransitions` - List<GlobalTransitionNode>
- `Events` - List<EventDefinition>
- `AllStates` - IReadOnlyList<StateNode>

### 1.2 Check that Hrot.Hsm.Editor.csproj references NodeEditor.Core

Run: `dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj`

Check the csproj for existing package/project refs so you know what namespaces are available.

### 1.3 Read ObserverGuardBadgeRenderer.cs as pattern for ICustomCanvasRenderer

File: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Renderers/ObserverGuardBadgeRenderer.cs`

Note the imports and `ICustomCanvasRenderer` usage pattern.

---

## Step 2 — Create HsmTransitionLabelRenderer.cs

### Location
`Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmTransitionLabelRenderer.cs`

### Requirements
- `public sealed class HsmTransitionLabelRenderer : ICustomCanvasRenderer`
- Constructor: `public HsmTransitionLabelRenderer(HsmAsset asset)`
- `public string Id => "hsm.transition_labels";`
- `public CanvasRenderPass Pass => CanvasRenderPass.AfterWires;`
- `public bool IsActive { get; set; } = true;`
- Stub `Render(ICanvasRenderContext ctx)` method (see below)
- **`public static string FormatLabel(TransitionNode t)`** — pure, testable (see below)

### FormatLabel logic
Returns the label string for a transition in the format:
`"EventName[GuardShort]/ActionShort"`
with parts omitted when null.

Rules:
1. EventPart = `t.EventName` when non-null, else empty string.
2. GuardPart = when `t.GuardFunction` is non-null:
   - Take last segment after the last `.` (if no `.`, use full string).
   - Wrap in square brackets: `"[GuardShort]"`.
   - Else empty string.
3. ActionPart = when `t.ActionFunction` is non-null:
   - Take last segment after the last `.` (if no `.`, use full string).
   - Prefix with `/`: `"/ActionShort"`.
   - Else empty string.
4. SyncBadge = `t.SyncGroupId != 0` → append `" [SG:" + t.SyncGroupId + "]"` at end.
5. PriorityBadge = `t.Priority != 128` → append `" (P:" + t.Priority + ")"` at end.
6. Combine: `EventPart + GuardPart + ActionPart + SyncBadge + PriorityBadge`
7. If result is empty string (no event, no guard, no action), return `"<unnamed>"`.

### Render method stub
```csharp
public void Render(ICanvasRenderContext ctx)
{
    // TODO: draw Event[Guard]/Action label at each transition midpoint
    // Runs in AfterWires pass; uses ctx.VisibleLinks + ctx.Graph
}
```

### Imports needed
```csharp
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
```

---

## Step 3 — Create HsmInitialArrowRenderer.cs

### Location
`Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmInitialArrowRenderer.cs`

### Requirements
- `public sealed class HsmInitialArrowRenderer : ICustomCanvasRenderer`
- Constructor: `public HsmInitialArrowRenderer(HsmAsset asset)`
- `public string Id => "hsm.initial_state_arrows";`
- `public CanvasRenderPass Pass => CanvasRenderPass.AfterNodes;`
- `public bool IsActive { get; set; } = true;`
- Stub `Render(ICanvasRenderContext ctx)` that does nothing for now:
  ```csharp
  public void Render(ICanvasRenderContext ctx)
  {
      // TODO: draw filled circle + arrow to initial child for each composite state
      // Runs in AfterNodes pass
  }
  ```

---

## Step 4 — Create HsmEventsWindow.cs

### Location
`Hrot/Subsystems/AI/Hrot.Hsm.Editor/Windows/HsmEventsWindow.cs`

### Requirements
- `public sealed class HsmEventsWindow`
- `public const string WindowId = "hsm_events";`
- Constructor: `public HsmEventsWindow(HsmAsset asset)`
- Store asset in `private readonly HsmAsset _asset;`
- `public void Render()` stub:
  ```csharp
  public void Render()
  {
      // TODO: render ImGui window showing events from _asset.Events
      // Columns: ID, Name, Payload, Flags, Priority, Global
  }
  ```

### Imports
```csharp
using Hrot.Hsm.Editor.Model;
```

---

## Step 5 — Create HsmGlobalsStrip.cs

### Location
`Hrot/Subsystems/AI/Hrot.Hsm.Editor/Windows/HsmGlobalsStrip.cs`

### Requirements
- `public sealed class HsmGlobalsStrip`
- Constructor: `public HsmGlobalsStrip(HsmAsset asset)`
- Store asset in `private readonly HsmAsset _asset;`
- `public void Render()` stub:
  ```csharp
  public void Render()
  {
      // TODO: render the globals strip (window chrome, not canvas content)
      // Shows chips for each GlobalTransitionNode in _asset.GlobalTransitions
  }
  ```

### Imports
```csharp
using Hrot.Hsm.Editor.Model;
```

---

## Step 6 — Create test: HsmTransitionLabelRendererTests.cs

### Location
`Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmTransitionLabelRendererTests.cs`

### Requirements
- `using Hrot.Hsm.Editor.Model;`
- `using Hrot.Hsm.Editor.Renderers;`
- `using FluentAssertions;`
- `using Xunit;`
- Namespace: `Hrot.Hsm.Editor.Tests`
- Class: `public sealed class HsmTransitionLabelRendererTests`

### Helper: create a minimal TransitionNode
```csharp
private static TransitionNode MakeTransition(
    string? eventName = null,
    string? guardFqn = null,
    string? actionFqn = null,
    byte priority = 128,
    ushort syncGroupId = 0,
    TransitionKind kind = TransitionKind.External)
{
    // Build a minimal StateNode for Source
    var asset = new HsmAsset(Guid.NewGuid(), "Test", "Test.cs", new HsmEditorLayout());
    // Need Source and Target states; use the asset's RootState's add mechanism
    // OR just create TransitionNode directly using its public fields
    var t = new TransitionNode
    {
        VisualId = Guid.NewGuid(),
        EventName = eventName,
        GuardFunction = guardFqn,
        ActionFunction = actionFqn,
        Priority = priority,
        SyncGroupId = syncGroupId,
        Kind = kind,
    };
    // Source and Target are required but not used by FormatLabel; set to any valid StateNode
    var src = new StateNode("Src", Guid.NewGuid(), null!, 0, StateFlags.None);
    t.Source = src;
    t.Target = src;
    return t;
}
```

IMPORTANT: Look at the actual constructors of `HsmAsset`, `HsmEditorLayout`, and `StateNode`
before writing tests. The constructors may differ from the examples above.
Read `HsmAsset.cs` fully first.

### Tests to write (7 tests):

1. **`FormatLabel_event_only_returns_event_name`**
   - Input: `EventName = "OnSight"`, guard/action null, priority 128, sync 0
   - Expected: `"OnSight"`

2. **`FormatLabel_event_and_action_returns_event_slash_action`**
   - Input: `EventName = "Fire"`, `ActionFunction = "MyNs.MyClass.Reload"`, guard null
   - Expected: `"Fire/Reload"`

3. **`FormatLabel_event_and_guard_returns_event_brackets_guard`**
   - Input: `EventName = "OnSight"`, `GuardFunction = "GuardNs.Checks.AmmoOk"`, action null
   - Expected: `"OnSight[AmmoOk]"`

4. **`FormatLabel_full_all_parts_combined`**
   - Input: `EventName = "OnFire"`, `GuardFunction = "G.AmmoOk"`, `ActionFunction = "A.StashWeapon"`
   - Expected: `"OnFire[AmmoOk]/StashWeapon"`

5. **`FormatLabel_no_event_no_guard_no_action_returns_unnamed`**
   - Input: all null, priority 128, sync 0
   - Expected: `"<unnamed>"`

6. **`FormatLabel_nondefault_priority_appends_badge`**
   - Input: `EventName = "Hit"`, priority = 200, sync 0
   - Expected: `"Hit (P:200)"`

7. **`FormatLabel_sync_group_appends_badge`**
   - Input: `EventName = "Hit"`, syncGroupId = 3, priority 128
   - Expected: `"Hit [SG:3]"`

---

## Step 7 — Create test: HsmRendererRegistrationTests.cs

### Location
`Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmRendererRegistrationTests.cs`

### Requirements
- Tests for `HsmTransitionLabelRenderer` and `HsmInitialArrowRenderer` structural properties.
- `using NodeEditor.Core.Canvas;`
- `using NodeEditor.Core.Interfaces;`

### Tests (4 tests):

1. **`TransitionLabelRenderer_Id_equals_expected`**
   - Create `HsmTransitionLabelRenderer` with a dummy `HsmAsset`
   - `renderer.Id.Should().Be("hsm.transition_labels")`

2. **`TransitionLabelRenderer_Pass_is_AfterWires`**
   - `renderer.Pass.Should().Be(CanvasRenderPass.AfterWires)`

3. **`InitialArrowRenderer_Id_equals_expected`**
   - Create `HsmInitialArrowRenderer` with a dummy `HsmAsset`
   - `renderer.Id.Should().Be("hsm.initial_state_arrows")`

4. **`InitialArrowRenderer_Pass_is_AfterNodes`**
   - `renderer.Pass.Should().Be(CanvasRenderPass.AfterNodes)`

For "dummy HsmAsset", look at how existing tests construct one (e.g., `HsmGraphModelTests.cs`
or `HsmFluentEmitterTests.cs`) and use the same pattern.

---

## Step 8 — Build and test

```
dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj
dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj
dotnet test  Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj
```

Expected: 0 errors, 0 warnings, 62 tests passing (51 existing + 11 new).

---

## Completion checklist

- [ ] `Renderers/HsmTransitionLabelRenderer.cs` created with `FormatLabel()` + `ICustomCanvasRenderer`
- [ ] `Renderers/HsmInitialArrowRenderer.cs` created
- [ ] `Windows/HsmEventsWindow.cs` created
- [ ] `Windows/HsmGlobalsStrip.cs` created
- [ ] `HsmTransitionLabelRendererTests.cs` (7 tests)
- [ ] `HsmRendererRegistrationTests.cs` (4 tests)
- [ ] Build: 0 errors, 0 warnings
- [ ] Tests: all 62 pass
- [ ] `git add -A && git commit -m "BATCH-29: HS-S1-13..17 - transition label renderer, initial arrow renderer, events window, globals strip (62 tests)"`
