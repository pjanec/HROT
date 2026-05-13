# BATCH-03 Instructions

**Sprint:** Gizmos-2 Headless  
**Tasks:** DEBT-002, GZH-014, GZH-016  
**Design ref:** `.dev/gizmos-2-headless/DESIGN.md` §10, §11  
**Task details:** `.dev/gizmos-2-headless/TASK-DETAILS.md` — GZH-014, GZH-016 sections  
**Tracker:** `.dev/gizmos-2-headless/TASK-TRACKER.md`  
**Debt tracker:** `.dev/gizmos-2-headless/DEBT-TRACKER.md`

> Read the design and task-detail references before implementing. They contain invariants and
> rationale that must be preserved. This file only provides the minimum additional context needed
> to orient you in the actual codebase.

---

## 0. Prerequisites / Context

### Already implemented (do NOT re-implement)

- `GizmoExecutionController` — `AddListener()` / `RemoveListener()` / `ListenerCount`  
  File: `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/GizmoExecutionController.cs`
- `GizmoUiStateHub` — `AddEndpoint` / `RemoveEndpoint` / `Publish`  
  File: `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Hub/GizmoUiStateHub.cs`
- `StructInspectorProjector<T>` — `EmitAndSync` / `ApplyUpdate`  
  File: `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/UI/StructInspectorProjector.cs`
- `LayerControlGizmo` refactored with dynamic `SchemaHash` and optional `IGizmoUiStatePublisher?`  
  File: `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/LayerControlGizmo.cs`
- All four composition roots have `GizmoExecutionController`
- `IInputProvider.IsMouseCaptured` / `IsKeyboardCaptured` already implemented  
  File: `FDP/Engine/Fdp.Presentation/Vis2D/Abstractions/IInputProvider.cs`  
  Impl: `FDP/Engine/Fdp.Presentation/Vis2D/Defaults/RaylibInputProvider.cs`

### Key existing properties on subsystems

All four subsystem classes already expose their controller as an `internal` property:
- `SimHostApp.GizmoController` → `_gizmoController!`
- `IgApplication.GizmoController` → `_gizmoController!`
- `EditorSubsystem.GizmoController` → `_gizmoController!`
- `CgfSubsystem.CgfGizmoController` → `_cgfGizmoController!`  
  *(Note: CGF uses a different name — keep it consistent with existing CGF naming in that file.)*

### SubsystemOrchestrator facts

File: `FDP/Toolkits/Fdp.Toolkits/Runner/SubsystemOrchestrator.cs`
- `private ISubsystem? _activeMapOwner` — set during `Initialize()`, updated by `SwitchMapOwner`
- `public void SwitchMapOwner(string subsystemName)` — already calls `MapCameraView` sync  
  Does NOT yet call `GizmoExecutionController` methods — that is GZH-014's job.
- `private bool IsMapOwner(ISubsystem subsystem)` — private; used only inside `DrawWorldAll()`

File: `FDP/Toolkits/Fdp.Toolkits/Runner/SubsystemConfig.cs`  
- Plain class with DomainId, Headless, OwnWindow, SubsystemName, NodeId, Deterministic, FixedDeltaSeconds

---

## 1. DEBT-002 — Wire `GizmoUiStateHub` to `LayerControlGizmo` in composition roots

**Status:** Deferred from BATCH-02 because hub was not yet stored in these roots.

**Affected files:**
- `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

**Change:**

In both files:
1. Add a `private readonly GizmoUiStateHub _gizmoUiHub = new GizmoUiStateHub();` field (near the other gizmo fields).
2. Pass `_gizmoUiHub` as the 4th (`uiPublisher`) argument when constructing `LayerControlGizmo`.

The field needs no explicit disposal — `GizmoUiStateHub` holds no unmanaged resources and is discarded with the subsystem.

**SimHostApp example** (line ~555):
```csharp
// BEFORE:
var layerControlGizmo = new Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo(
    layerControlId,
    _interactionBus,
    new StructEdit.Reflection.ComponentEditServiceBuilder().Build());

// AFTER:
var layerControlGizmo = new Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo(
    layerControlId,
    _interactionBus,
    new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
    _gizmoUiHub);
```

**EditorSubsystem example** (line ~610):
```csharp
// BEFORE:
var layerControlGizmo = new Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo(layerControlId, interactionBus, new StructEdit.Reflection.ComponentEditServiceBuilder().Build());

// AFTER:
var layerControlGizmo = new Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo(
    layerControlId,
    interactionBus,
    new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
    _gizmoUiHub);
```

**Also expose the hub** with an `internal GizmoUiStateHub GizmoUiHub => _gizmoUiHub;` property on both classes (near the `GizmoController` property) — needed for future module installation (BATCH-04).

**Tests:** Add one test for each composition root:
- Test `DEBT002_SimHost`: instantiate the SimHost composition root minimally (or use the existing
  `SimHostApp` test helpers), verify `GizmoUiHub` is not null.  
  File: `Hrot/Subsystems/Hrot.SimHost.Tests/Gizmos/LayerControlGizmoTests.cs` (extend existing).
- Test `DEBT002_Editor`: same for EditorSubsystem.  
  File: `Hrot/Subsystems/Hrot.SimHost.Tests/Gizmos/LayerControlGizmoTests.cs` or the nearest editor test file.

> Note: if the test environment cannot easily construct the full composition root, a lightweight
> integration-style test that just verifies the hub property is non-null after `Initialize()` is
> acceptable. Alternatively, add the tests next to `GZH011_1` and `GZH011_2` in
> `Hrot/Subsystems/Hrot.SimHost.Tests/Gizmos/LayerControlGizmoTests.cs`.

---

## 2. GZH-014 — Perspective-Aware `GizmoExecutionController` Switching

**Design ref:** DESIGN.md §10 / TASK-DETAILS.md GZH-014

### 2.1 New interface `IGizmoControllable`

**File (NEW):** `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/IGizmoControllable.cs`

```csharp
namespace Hrot.Common.Diagnostics.Gizmos;

/// <summary>
/// Exposes the <see cref="GizmoExecutionController"/> of a subsystem so that the
/// <c>PerspectiveCoordinatorSystem</c> can transfer the listener count when the
/// active perspective changes.
/// </summary>
public interface IGizmoControllable
{
    /// <summary>Returns the gizmo execution controller for this subsystem, or <c>null</c> if not applicable.</summary>
    Fdp.Toolkit.Diagnostics.Gizmos.GizmoExecutionController? GizmoController { get; }
}
```

`Hrot.Common` already references `Fdp.Toolkits`, so this compiles without any new project reference.

### 2.2 Implement `IGizmoControllable` on all four subsystems

For each of the four subsystems, do two things:

1. Add `: IGizmoControllable` to the class declaration (or its partial class, whichever is appropriate).
2. Make the existing `GizmoController` property `public` (change from `internal`) to satisfy the interface.

Files to modify:
- `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` — class `SimHostSubsystem` (or the inner `App` class — check the actual class name)
- `Hrot/Subsystems/Hrot.IG/IgApplication.cs` — class `IgApplication` (or equivalent)
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — class `EditorSubsystem`
- `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` — class `CgfSubsystem`

For CGF, the existing property is called `CgfGizmoController`. Add a **separate** explicit interface
implementation:
```csharp
Fdp.Toolkit.Diagnostics.Gizmos.GizmoExecutionController? IGizmoControllable.GizmoController
    => _cgfGizmoController;
```
This avoids renaming the existing property and breaking existing callers.

For the other three (SimHost, IG, Editor), the existing property is already named `GizmoController`,
so just change `internal` to `public` and add the interface to the class declaration.

### 2.3 Update `PerspectiveCoordinatorSystem`

**File:** `Hrot/Runner/Hrot.ClusterRunner/Systems/PerspectiveCoordinatorSystem.cs`

**Change 1 — Add a `_gizmoControllables` dictionary to the constructor:**

```csharp
private readonly IReadOnlyDictionary<string, IGizmoControllable> _gizmoControllables;

public PerspectiveCoordinatorSystem(
    SubsystemOrchestrator orchestrator,
    IReadOnlyDictionary<string, string> perspectiveToSubsystemName,
    IReadOnlyDictionary<string, IGizmoControllable>? gizmoControllables = null)
{
    _orchestrator              = orchestrator;
    _perspectiveToSubsystemName = perspectiveToSubsystemName;
    _gizmoControllables = gizmoControllables
        ?? new Dictionary<string, IGizmoControllable>();
}
```

The parameter is optional (defaults to empty dictionary) so existing callers in test code and
`Program.cs` that pass only two arguments still compile.

**Change 2 — In `ProcessPendingEvents()`, add listener transfers:**

```csharp
public void ProcessPendingEvents()
{
    while (_queue.TryDequeue(out var evt))
    {
        if (_perspectiveToSubsystemName.TryGetValue(evt.NewPerspective, out var subsystemName))
        {
            // Transfer gizmo listener: outgoing loses one, incoming gains one.
            if (_gizmoControllables.TryGetValue(evt.OldPerspective, out var outgoing))
                outgoing.GizmoController?.RemoveListener();
            if (_gizmoControllables.TryGetValue(evt.NewPerspective, out var incoming))
                incoming.GizmoController?.AddListener();

            _orchestrator.SwitchMapOwner(subsystemName);
        }

        _currentPerspective = evt.NewPerspective;
    }
}
```

**Change 3 — Wire in `Program.cs`:**

In `Program.cs`, after the `PerspectiveCoordinatorSystem` is constructed (around line 243), build
the gizmo controllables map from the discovered subsystems:

```csharp
var gizmoControllables = subsystems
    .OfType<IGizmoControllable>()
    .Select(s => (ISubsystem)s)
    .Where(s => s != perspSubsystem)
    .ToDictionary(
        s => s.Name,
        s => (IGizmoControllable)s,
        StringComparer.OrdinalIgnoreCase);

var coordinator = new PerspectiveCoordinatorSystem(orchestrator, perspectiveMap, gizmoControllables);
```

The `using Hrot.Common.Diagnostics.Gizmos;` using directive will need to be added to `Program.cs`
if it is not already present.

### 2.4 Tests for GZH-014

**Test file:** `Hrot/Runner/Hrot.ClusterRunner.Tests/PerspectiveCoordinatorSystemTests.cs`  
(file already exists — add a new test class or test methods to it)

**Test `GZH014_1` — Perspective switch transfers listener count:**
```
Two mock IGizmoControllable subsystems (SubA, SubB), each with its own GizmoExecutionController
(minimal stubs — controller backed by a stub TogglablePostSimulationGroup).
Build PerspectiveCoordinatorSystem with both in the gizmoControllables map.

Simulate OpenLocalWindow: call coordinator.Enqueue(new TogglePerspectiveEvent("", "SubA")).
Call coordinator.ProcessPendingEvents().
Assert: SubA.GizmoController.ListenerCount == 1, SubB.GizmoController.ListenerCount == 0.

Switch perspective: coordinator.Enqueue(new TogglePerspectiveEvent("SubA", "SubB")).
Call coordinator.ProcessPendingEvents().
Assert: SubA.GizmoController.ListenerCount == 0, SubB.GizmoController.ListenerCount == 1.
```

To construct `GizmoExecutionController` in tests, you will need stub implementations of
`TogglablePostSimulationGroup`, `GlobalGizmoManager`, and `DataDrivenGizmoSystem` — or check if
the existing test helpers in `Hrot.ClusterRunner.Tests/Mocks/` already provide these. If they
don't, add minimal stubs there.

**Test `GZH014_2` — Unknown perspective is silently ignored (no exception):**
```
One gizmoControllable for "SubA" only.
Enqueue TogglePerspectiveEvent("SubA", "UnknownPersp").
ProcessPendingEvents() must not throw.
SubA.GizmoController.ListenerCount == 0 (RemoveListener was called, no AddListener fired).
```

---

## 3. GZH-016 — Subsystem Input Collision Fix

**Design ref:** DESIGN.md §11 / TASK-DETAILS.md GZH-016

The existing `IInputProvider.IsMouseCaptured` / `IsKeyboardCaptured` are already implemented.
Step 1 of GZH-016 is therefore **already done**. Only Steps 2 and 3 need work.

### 3.1 Add `IsActiveMapOwner` seam via `SubsystemConfig` (FDP submodule)

**File (MODIFY):** `FDP/Toolkits/Fdp.Toolkits/Runner/SubsystemConfig.cs`

Add one property:
```csharp
/// <summary>
/// Returns <c>true</c> when this subsystem is currently the active map owner.
/// Injected by <see cref="SubsystemOrchestrator"/> during <c>Initialize()</c>.
/// Defaults to <c>() => true</c> so standalone subsystems (non-ClusterRunner) are unaffected.
/// </summary>
public Func<bool> IsActiveMapOwner { get; set; } = () => true;
```

**File (MODIFY):** `FDP/Toolkits/Fdp.Toolkits/Runner/SubsystemOrchestrator.cs`

In `Initialize()`, inside the `foreach (var subsystem in _subsystems)` loop, after constructing
`cfg`, set the delegate:

```csharp
var captured = subsystem; // capture loop variable
cfg.IsActiveMapOwner = () => _activeMapOwner == captured;
```

Pass `captured` (not `subsystem`) to avoid the classic C# foreach closure bug.

### 3.2 Store the delegate in each relevant subsystem

The two subsystems that have canvas/gizmo input gating in their `Update()` or `DrawWorld()` are:
- `Hrot/Subsystems/Hrot.IG/IgApplication.cs`
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

In each, in the `Initialize(SubsystemConfig config)` method, store the delegate:
```csharp
_isActiveMapOwner = config.IsActiveMapOwner;
```

Add the corresponding field:
```csharp
private Func<bool> _isActiveMapOwner = () => true;
```

### 3.3 Update the input gate in `IgApplication.Update()`

**File:** `Hrot/Subsystems/Hrot.IG/IgApplication.cs`

Current code (around line 1242):
```csharp
if (!ImGui.GetIO().WantCaptureMouse)
{
    HandleCameraInput(dt);
    _measureToolGizmoAdapter?.Update();
    _canvas.Update(dt);
}
```

Replace with:
```csharp
if (_isActiveMapOwner() && !ImGui.GetIO().WantCaptureMouse)
{
    HandleCameraInput(dt);
    _measureToolGizmoAdapter?.Update();
    _canvas.Update(dt);
}
```

### 3.4 Update the input gate in `EditorSubsystem.DrawUI()`

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

Current code (around line 1068):
```csharp
if (!ImGuiNET.ImGui.GetIO().WantCaptureMouse && _canvas != null && _world != null)
{
    // hover tooltip code
}
```

If there is also a canvas.Update() call in `Update()` or `DrawWorld()` gated by `WantCaptureMouse`
in EditorSubsystem, apply the same `_isActiveMapOwner()` wrapping. Add `_isActiveMapOwner()`
as the **first** condition (short-circuit evaluation means it is checked before any Raylib state).

> Note: Only gate input processing (canvas updates, mouse event handling). Do NOT gate `DrawWorld()`,
> `DrawUI()`, or ECS simulation ticks — the orchestrator already gates `DrawWorld()` via
> `IsMapOwner()`.

### 3.5 Verify `DebugGizmoLayer.HandleInput` (Step 3 of GZH-016)

Search for `DebugGizmoLayer.HandleInput` or `_gizmoLayer.HandleInput` in the subsystem files to
check whether it also needs the gate. If it is already inside the gated block above, no change is
needed. If it sits outside, move it inside the same `_isActiveMapOwner() && !WantCaptureMouse` block.

### 3.6 Tests for GZH-016

**Test file:** Add a new class `GZH016_Tests` to  
`Hrot/Subsystems/Hrot.IG.Tests/IgApplicationPanelTests.cs`  
(or the nearest existing test file in `Hrot.IG.Tests` or `Hrot.ClusterRunner.Tests`).

**Test `GZH016_1` — Mouse captured suppresses input:**

This test uses the pattern established in the existing `InputGate_WantCaptureMouseTrue_*` tests.
Abstract the IG Update input gate as a boolean predicate test:

```csharp
bool isActiveMapOwner = true;
bool wantCaptureMouse = true;
bool handlerCalled    = false;

if (isActiveMapOwner && !wantCaptureMouse)
    handlerCalled = true;

Assert.False(handlerCalled,
    "Input must be suppressed when WantCaptureMouse is true.");
```

**Test `GZH016_2` — Inactive map owner suppresses input:**

```csharp
bool isActiveMapOwner = false;
bool wantCaptureMouse = false;
bool handlerCalled    = false;

if (isActiveMapOwner && !wantCaptureMouse)
    handlerCalled = true;

Assert.False(handlerCalled,
    "Input must be suppressed when the subsystem is not the active map owner.");
```

**Test `GZH016_3` — Active owner + mouse free allows input:**

```csharp
bool isActiveMapOwner = true;
bool wantCaptureMouse = false;
bool handlerCalled    = false;

if (isActiveMapOwner && !wantCaptureMouse)
    handlerCalled = true;

Assert.True(handlerCalled,
    "Input must be processed when active map owner and mouse is not captured.");
```

> These tests mirror the style of the existing `InputGate_WantCaptureMouse*` tests in
> `IgApplicationPanelTests.cs`, which test the gate logic as a plain boolean expression rather
> than calling the real IG. This approach avoids Raylib dependencies and is consistent with the
> established test pattern in this file.

---

## 4. Build and Test Instructions

All changes span two repository boundaries:

### FDP submodule (`d:\Work\IOS-IG-SimHost-FDP-2\FDP`)

Changes:
- `FDP/Toolkits/Fdp.Toolkits/Runner/SubsystemConfig.cs` (GZH-016)
- `FDP/Toolkits/Fdp.Toolkits/Runner/SubsystemOrchestrator.cs` (GZH-016)

Build check:
```powershell
dotnet build "FDP\Toolkits\Fdp.Toolkits\Fdp.Toolkits.csproj"
```

Test check:
```powershell
dotnet test "FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj" --filter "FullyQualifiedName~Diagnostics.Gizmos"
```
Expected: 187 / 187 pass (no regressions; GZH-016 adds no tests in FDP).

### Hrot (parent repo)

Changes:
- `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/IGizmoControllable.cs` (NEW, GZH-014)
- `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` (DEBT-002 + GZH-014)
- `Hrot/Subsystems/Hrot.IG/IgApplication.cs` (GZH-014 + GZH-016)
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (DEBT-002 + GZH-014 + GZH-016)
- `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` (GZH-014)
- `Hrot/Runner/Hrot.ClusterRunner/Systems/PerspectiveCoordinatorSystem.cs` (GZH-014)
- `Hrot/Runner/Hrot.ClusterRunner/Program.cs` (GZH-014 wiring)

Build check:
```powershell
dotnet build "Hrot\Engine\Hrot.Common\Hrot.Common.csproj"
dotnet build "Hrot\Runner\Hrot.ClusterRunner\Hrot.ClusterRunner.csproj"
```

Test checks:
```powershell
dotnet test "Hrot\Subsystems\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj" --filter "FullyQualifiedName~GZH011|FullyQualifiedName~DEBT002"
dotnet test "Hrot\Runner\Hrot.ClusterRunner.Tests\Hrot.ClusterRunner.Tests.csproj" --filter "FullyQualifiedName~GZH014|FullyQualifiedName~GZH016"
```

Regression suite for PerspectiveCoordinatorSystem:
```powershell
dotnet test "Hrot\Runner\Hrot.ClusterRunner.Tests\Hrot.ClusterRunner.Tests.csproj" --filter "FullyQualifiedName~PerspectiveCoordinator"
```
All existing PerspectiveCoordinatorSystem tests must still pass.

---

## 5. Batch Report

Create `d:\Work\IOS-IG-SimHost-FDP-2\.dev\gizmos-2-headless\reports\BATCH-03-REPORT.md` with:

```markdown
# BATCH-03 Report

## Tasks completed
- [ ] DEBT-002 — GizmoUiStateHub wired to LayerControlGizmo in SimHostApp + EditorSubsystem
- [ ] GZH-014 — IGizmoControllable interface + PerspectiveCoordinatorSystem listener transfer
- [ ] GZH-016 — IsActiveMapOwner input gate (SubsystemConfig delegate + IgApplication + Editor)

## Files modified
(list each file)

## Files created
(list each file)

## Test results
(paste dotnet test output summaries)

## Issues / deviations
(any deviations from the instructions, with rationale)
```
