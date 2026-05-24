# BATCH-26: Phase 3 — Migrate Placement Tools to IStatefulGizmo

**Batch Number:** BATCH-26
**Phase:** Phase 3 — Migrating Creation & Authoring Tools
**Priority:** HIGH
**Dependencies:** BATCH-25 (Phase 2) — complete and committed.

---

## Onboarding & Workflow

### Required Reading (IN ORDER)

1. `.dev/gizmos-1/old-stuff-erradication.md` — Phase 3 section ("Migrating Creation & Authoring Tools") and the Phase 3 pass conditions at the bottom.
2. `.dev/gizmos-1/reviews/BATCH-25-REVIEW.md` — last batch review.
3. `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/Interaction/IStatefulGizmo.cs` — interface being implemented.
4. `FDP/ExtDeps/GizmoMap/GizmoMap.Example/Gizmos/EntityRotatorGizmo.cs` — reference implementation of `IStatefulGizmo`.
5. `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/GizmoFocusInputBridge.cs` — existing canvas bridge pattern.
6. `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/CreationTool.cs` — tool being replaced.
7. `Hrot/Subsystems/Hrot.Editor/Tools/ObstaclePlacementTool.cs` — tool being replaced.
8. `Hrot/Subsystems/Hrot.Editor/Adapters/EditorSpawnAdapter.cs` — first call site to update.
9. `Hrot/Subsystems/Hrot.Editor/Adapters/EditorZoneAdapter.cs` — second call site to update.
10. `Hrot/Subsystems/Hrot.IG/Systems/MapCommandController.cs` — third call site to update.

### Source Code Locations

- **New gizmo files:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/`
- **ObstaclePlacementGizmo:** `Hrot/Subsystems/Hrot.Editor/Gizmos/`
- **Existing tests (Hrot.Presentation.Tests):** `Hrot/Engine/Hrot.Presentation.Tests/`
- **Existing tests (Hrot.Editor.Tests):** `Hrot/Subsystems/Hrot.Editor.Tests/`
- **Existing tests (Hrot.IG.Tests):** `Hrot/Subsystems/Hrot.IG.Tests/`

### Build & Test Commands

```
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln -c Debug --nologo -v q
dotnet test Hrot/Engine/Hrot.Presentation.Tests/ --no-build -v q
dotnet test Hrot/Subsystems/Hrot.IG.Tests/ --no-build -v q
dotnet test Hrot/Subsystems/Hrot.Editor.Tests/ --no-build -v q
```

### Report Submission

Submit your report to: `.dev/gizmos-1/reports/BATCH-26-REPORT.md`

### DO NOT STOP

Do NOT ask for permission to run tests, build, or delete files. Implement everything, fix all errors, ensure all tests pass, then write the report. No stopping halfway.

---

## Context

Phase 3 deletes the four remaining legacy placement tools (`CreationTool`, `AreaPlacementTool`, `RoutePlacementTool`, `ObstaclePlacementTool`) and replaces them with `IStatefulGizmo` implementations managed via a thin canvas bridge.

Key insight: `AreaPlacementTool` and `RoutePlacementTool` are dead stubs — they have no call sites in production `.cs` files. Just delete them. `CreationTool` and `ObstaclePlacementTool` ARE actively used and need real replacements.

The canvas tool stack (`MapCanvas.PushTool`) is still used in Phase 3 as the input bridge (Phase 6 removes it). The new `PlacementCanvasBridge : IMapTool` serves as a thin translation layer between canvas events and `IStatefulGizmo` method calls.

---

## Architecture

### EntityPlacementGizmo (replaces CreationTool)

- Namespace: `Hrot.ScenarioEditor.Gizmos`
- Implements: `IStatefulGizmo`
- `RequiresExclusiveFocus => true`
- Constructor: `(Action<SpawnEntityCommand> onEntityCreated, long tkbType, string? initialPropertiesJson, bool autoPopOnPlace, Func<string>? nameResolver, Action onRemove)`
- `UpdateAndDraw`: draws ghost sphere at `_cursorWorld` + TKB type text label (same as `CreationTool.Draw`)
- `OnDragUpdate(Vector3 worldPos)`: updates `_cursorWorld`
- `OnMouseEvent(Left, isPressed=false, pos)`: builds `SpawnEntityCommand`, calls `_onEntityCreated(cmd)`, fires `OnCommandPublished`, if `_autoPopOnPlace` calls `_onRemove()`
- `OnMouseEvent(Right, isPressed=true, pos)`: calls `_onRemove()`
- `OnKeyEvent(Escape, pressed)`: calls `_onRemove()`
- `event Action<SpawnEntityCommand>? OnCommandPublished` — retained for test/integration observability
- `event Action? Exited` — fires inside `_onRemove` invocation, retained for `MapCommandController` compatibility
- `SpawnEntityCommand` building: identical to `CreationTool.BuildAndPublishSpawnCommand` — copy the exact logic including `RequestId = Guid.NewGuid()`, `InitialTransform`, `InitialAttributesJson`
- Ghost color / label: copy `CreationTool.GetAffiliationColor` and `ParseAffiliationFromJson` private helpers
- Constants (GhostAlpha, GhostRadiusPx, GhostLabelOffsetY, DefaultTkbType): copy from `CreationToolConstants` directly into `EntityPlacementGizmo` as private `const` fields (no separate constants file)
- Unused `IStatefulGizmo` methods: `OnInteractionStarted`, `OnCommit`, `OnCancel`, `OnMenuAction` → empty body
- `Dispose()`: empty body (no resources)

### ObstaclePlacementGizmo (replaces ObstaclePlacementTool)

- Namespace: `Hrot.Editor.Gizmos`
- File: `Hrot/Subsystems/Hrot.Editor/Gizmos/ObstaclePlacementGizmo.cs`
- Implements: `IStatefulGizmo`
- `RequiresExclusiveFocus => true`
- Constructor: `(float radius, Action<Vector2> onObstaclePlaced, Action onRemove)`
- `UpdateAndDraw`: draws a red sphere at `_cursorWorld` with `_radius` (same as `ObstaclePlacementTool.Draw`)
- `OnDragUpdate(Vector3 worldPos)`: updates `_cursorWorld`
- `OnMouseEvent(Left, isPressed=false, pos)`: calls `_onObstaclePlaced(new Vector2(pos.X, pos.Y))`, then `_onRemove()`
- `OnMouseEvent(Right, isPressed=true, pos)`: calls `_onRemove()`
- `OnKeyEvent(Escape, pressed)`: calls `_onRemove()`
- Unused methods: empty body
- `Dispose()`: empty body

### PlacementCanvasBridge (thin canvas adapter)

- Namespace: `Hrot.ScenarioEditor.Gizmos`
- File: `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/PlacementCanvasBridge.cs`
- Implements: `IMapTool`
- Constructor: `(IStatefulGizmo gizmo)`
- Fields: `readonly IStatefulGizmo _gizmo; MapCanvas? _canvas;`
- `Name => "PlacementBridge"`
- `OnEnter(MapCanvas canvas)`: `_canvas = canvas; _gizmo.SetFocus(true);`
- `OnExit()`: `_gizmo.SetFocus(false); _gizmo.Dispose(); _canvas = null;`
- `Update(float dt)`: empty
- `Draw(RenderContext ctx)`: `if (ctx.DrawBuilder != null) _gizmo.UpdateAndDraw(0f, ctx.DrawBuilder);`
- `HandleHover(Vector2 worldPos)`: `_gizmo.OnDragUpdate(new Vector3(worldPos.X, worldPos.Y, 0f)); return true;`
- `HandleDrag(Vector2 worldPos, Vector2 delta)`: same as HandleHover, return true
- `HandleClick(Vector2 worldPos, MapMouseButton button)`:
  ```csharp
  var pos = new Vector3(worldPos.X, worldPos.Y, 0f);
  // Left = released (commit), Right = pressed (cancel) — matches canvas semantics
  bool isPressed = button != MapMouseButton.Left;
  _gizmo.OnMouseEvent((GizmoMouseButton)(int)button, isPressed, pos);
  return true;
  ```
  Use `using GizmoMouseButton = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton;` aliased using, same pattern as `GizmoFocusInputBridge.cs`.
- `HandleKeyPressed(MapKeyboardKey key)`: `_gizmo.OnKeyEvent((GizmoKeyboardKey)(int)key, isPressed: true); return true;`
  Use `using GizmoKeyboardKey = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey;`
- `public void RequestPop() => _canvas?.PopTool();` — called by gizmo's onRemove delegate

**IMPORTANT**: The gizmo's `_onRemove` action captures a reference to the bridge and calls `bridge.RequestPop()`. This pops the bridge from the canvas, which triggers `OnExit()` which disposes the gizmo.

---

## Tasks

### Task 1: Delete five legacy tool files

Delete the following files:

1. `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/CreationTool.cs`
2. `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Tools/CreationToolConstants.cs`
3. `Hrot/Subsystems/Hrot.Editor/Tools/AreaPlacementTool.cs`
4. `Hrot/Subsystems/Hrot.Editor/Tools/RoutePlacementTool.cs`
5. `Hrot/Subsystems/Hrot.Editor/Tools/ObstaclePlacementTool.cs`

Also delete these test files (replaced by gizmo tests):
6. `Hrot/Subsystems/Hrot.IG.Tests/CreationToolTests.cs`

---

### Task 2: Create `EntityPlacementGizmo`

**File:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/EntityPlacementGizmo.cs` (NEW)

Implement as described in the Architecture section above. Key implementation notes:

- Copy the `BuildAndPublishSpawnCommand` logic from `CreationTool` verbatim (including the `_ = _nameResolver;` comment retention placeholder, `Guid.NewGuid()`, `InitialTransform`, `InitialAttributesJson`).
- Copy `ParseAffiliationFromJson` and `GetAffiliationColor` helpers from `CreationTool` verbatim.
- `GhostAlpha`, `GhostRadiusPx`, `GhostLabelOffsetY` constants: copy values from `CreationToolConstants.cs` before deleting it.
- `DefaultTkbType`: copy value from `CreationToolConstants.DefaultTkbType`.
- The `Exited` event fires BEFORE calling `_onRemove()` in the `Remove()` helper method. (So `OnCreationToolExited` in `MapCommandController` runs before the canvas pop.)
- No `IMapTool` reference — this is a pure `IStatefulGizmo`.

---

### Task 3: Create `ObstaclePlacementGizmo`

**File:** `Hrot/Subsystems/Hrot.Editor/Gizmos/ObstaclePlacementGizmo.cs` (NEW)

Implement as described in the Architecture section.

---

### Task 4: Create `PlacementCanvasBridge`

**File:** `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/PlacementCanvasBridge.cs` (NEW)

Implement as described in the Architecture section.

Use aliased usings for the two `MapMouseButton` / `MapKeyboardKey` name collisions, following the same pattern as `GizmoFocusInputBridge.cs`:
```csharp
using GizmoMouseButton = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton;
using GizmoKeyboardKey = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey;
```

---

### Task 5: Update `EditorSpawnAdapter`

**File:** `Hrot/Subsystems/Hrot.Editor/Adapters/EditorSpawnAdapter.cs` (MODIFY)

Replace the `CreationTool` usage in `StartPlacementMode`:

```csharp
// BEFORE:
var tool = new CreationTool(
    onEntityCreated: cmd => { ... },
    tkbType:               tkbType,
    initialPropertiesJson: initialPropertiesJson,
    autoPopOnPlace:        true);
_canvas.PushTool(tool);

// AFTER:
PlacementCanvasBridge? bridge = null;
var gizmo = new EntityPlacementGizmo(
    onEntityCreated:       cmd => { ... },  // same body as before
    tkbType:               tkbType,
    initialPropertiesJson: initialPropertiesJson,
    autoPopOnPlace:        true,
    onRemove:              () => bridge?.RequestPop());
bridge = new PlacementCanvasBridge(gizmo);
_canvas.PushTool(bridge);
```

Remove `using Hrot.ScenarioEditor.Tools;` and add `using Hrot.ScenarioEditor.Gizmos;`.
`AreaPlacementTool` and `RoutePlacementTool` are NOT used here (the area/route methods use `PointSequenceTool` directly) — no changes needed for those methods.

---

### Task 6: Update `EditorZoneAdapter`

**File:** `Hrot/Subsystems/Hrot.Editor/Adapters/EditorZoneAdapter.cs` (MODIFY)

Replace `ObstaclePlacementTool` usage in `StartObstaclePlacementMode`:

```csharp
// BEFORE:
var tool = new ObstaclePlacementTool(
    radius:   radius,
    onPlaced: worldPos => { _bus.PublishManaged(new SpawnZoneObstacleCommand { ... }); });
_canvas.PushTool(tool);

// AFTER:
PlacementCanvasBridge? bridge = null;
var gizmo = new ObstaclePlacementGizmo(
    radius:            radius,
    onObstaclePlaced:  worldPos =>
    {
        _bus.PublishManaged(new SpawnZoneObstacleCommand
        {
            ZoneName = zoneName,
            Position = new Vector2(worldPos.X, worldPos.Y),
            Radius   = zoneRadius,
        });
    },
    onRemove: () => bridge?.RequestPop());
bridge = new PlacementCanvasBridge(gizmo);
_canvas.PushTool(bridge);
```

Add `using Hrot.Editor.Gizmos;` and `using Hrot.ScenarioEditor.Gizmos;`. Remove `using Hrot.Editor.Tools;`.

---

### Task 7: Update `MapCommandController`

**File:** `Hrot/Subsystems/Hrot.IG/Systems/MapCommandController.cs` (MODIFY)

Changes:
1. Replace field `private CreationTool? _activeCreationTool;` with `private PlacementCanvasBridge? _activePlacementBridge;`
2. Remove `using Hrot.ScenarioEditor.Tools;`; add `using Hrot.ScenarioEditor.Gizmos;`
3. In `ActivatePlacementCommand`:
   - Change `if (_canvas.ActiveTool is CreationTool)` to `if (_canvas.ActiveTool is PlacementCanvasBridge)`
   - Replace the `new CreationTool(...)` + `tool.Exited +=` block with:
     ```csharp
     PlacementCanvasBridge? bridge = null;
     var gizmo = new EntityPlacementGizmo(
         onEntityCreated:       OnEntityCreatedByTool,
         tkbType:               tkbType,
         initialPropertiesJson: initialPropertiesJson,
         autoPopOnPlace:        true,
         nameResolver:          _nameGenerator,
         onRemove:              () =>
         {
             bridge?.RequestPop();
             OnCreationToolExited();
         });
     bridge = new PlacementCanvasBridge(gizmo);
     _activePlacementBridge = bridge;
     _canvas.PushTool(bridge);
     ```
   - Remove the `tool.Exited += OnCreationToolExited;` line (it's now inline in `onRemove`).
   - Remove `_activeCreationTool = tool;` and replace with `_activePlacementBridge = bridge;`
4. In `ClearSession`: change `_activeCreationTool = null;` to `_activePlacementBridge = null;`
5. Update the class XML `<summary>` doc and any inline comments that mention `CreationTool` to say `EntityPlacementGizmo` / `PlacementCanvasBridge`.

Note: `OnCreationToolExited` method itself does NOT need renaming (it implements the same session logic).

---

### Task 8: Update `ToolPresenceTests`

**File:** `Hrot/Engine/Hrot.Presentation.Tests/ToolPresenceTests.cs` (MODIFY)

In the test that checks `ScenarioEditor_Assembly_ContainsAllToolTypes`, add two `Assert.Null` assertions confirming `CreationTool` and `CreationToolConstants` are gone:

```csharp
// Phase 3 erasures (BATCH-26)
Assert.Null(asm.GetType("Hrot.ScenarioEditor.Tools.CreationTool"));
Assert.Null(asm.GetType("Hrot.ScenarioEditor.Tools.CreationToolConstants"));
```

And add two `Assert.NotNull` assertions confirming the new gizmos ARE present:
```csharp
Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Gizmos.EntityPlacementGizmo"));
Assert.NotNull(asm.GetType("Hrot.ScenarioEditor.Gizmos.PlacementCanvasBridge"));
```

---

### Task 9: Update `ToolInteractionIntegrationTests`

**File:** `Hrot/Subsystems/Hrot.IG.Tests/ToolInteractionIntegrationTests.cs` (MODIFY)

The file has two sections:
- **Section 1 (lines ~85-115): "Tests: DDS payload from CreationTool"** — these two tests reference `CreationTool` directly and must be replaced.
- **Section 2 (lines ~117+): "Tests: StandardInteractionTool selection"** — keep unchanged.

Replace Section 1 with equivalent tests using `EntityPlacementGizmo`:

```csharp
// Tests: EntityPlacementGizmo spawn command
[Fact]
public void EntityPlacementGizmo_LeftClick_WritesExactlyOneCommand()
{
    var captured = new List<SpawnEntityCommand>();
    PlacementCanvasBridge? bridge = null;
    var gizmo = new EntityPlacementGizmo(
        onEntityCreated: cmd => captured.Add(cmd),
        tkbType:         TestTkbType,
        onRemove:        () => bridge?.RequestPop());
    bridge = new PlacementCanvasBridge(gizmo);

    bridge.HandleClick(new Vector2(SpawnX, SpawnY), MapMouseButton.Left);

    Assert.Single(captured);
}

[Fact]
public void EntityPlacementGizmo_LeftClick_CommandCarriesTkbTypeAndPosition()
{
    var captured = new List<SpawnEntityCommand>();
    PlacementCanvasBridge? bridge = null;
    var gizmo = new EntityPlacementGizmo(
        onEntityCreated: cmd => captured.Add(cmd),
        tkbType:         TestTkbType,
        onRemove:        () => bridge?.RequestPop());
    bridge = new PlacementCanvasBridge(gizmo);

    bridge.HandleClick(new Vector2(SpawnX, SpawnY), MapMouseButton.Left);

    Assert.Equal(TestTkbType, captured[0].TkbType);
    Assert.True(captured[0].InitialTransform.HasValue);
    Assert.Equal(SpawnX, captured[0].InitialTransform!.Value.Position.X, precision: 2);
    Assert.Equal(SpawnY, captured[0].InitialTransform!.Value.Position.Y, precision: 2);
}
```

Update `using Hrot.ScenarioEditor.Tools;` → `using Hrot.ScenarioEditor.Gizmos;`.

---

### Task 10: Update `AdapterTests` (ObstaclePlacement and SpawnAdapter tests)

**File:** `Hrot/Subsystems/Hrot.Editor.Tests/Adapters/AdapterTests.cs` (MODIFY)

**A) EditorSpawnAdapterTests:**

`StartPlacementMode_PushesCreationTool` → rename and update assertion:
```csharp
[Fact]
public void StartPlacementMode_PushesPlacementCanvasBridge()
{
    var adapter = new EditorSpawnAdapter(_canvas, _bus);
    adapter.StartPlacementMode(2001L, null);

    Assert.IsType<PlacementCanvasBridge>(_canvas.ActiveTool);
}
```

**B) EditorZoneAdapterTests (ObstaclePlacement tests):**

`StartObstaclePlacementMode_PushesObstaclePlacementTool` → update:
```csharp
[Fact]
public void StartObstaclePlacementMode_PushesPlacementCanvasBridge()
{
    var adapter = new EditorZoneAdapter(_canvas, _bus);
    adapter.StartObstaclePlacementMode("zone_alpha", 10f);

    Assert.IsType<PlacementCanvasBridge>(_canvas.ActiveTool);
}
```

`StartObstaclePlacementMode_OnClick_PublishesSpawnZoneObstacleCommand` → update:
```csharp
[Fact]
public void StartObstaclePlacementMode_OnClick_PublishesSpawnZoneObstacleCommand()
{
    var adapter = new EditorZoneAdapter(_canvas, _bus);
    adapter.StartObstaclePlacementMode("zone_beta", 5f);

    var bridge = Assert.IsType<PlacementCanvasBridge>(_canvas.ActiveTool);
    bridge.HandleClick(new Vector2(100f, 200f), MapMouseButton.Left);

    _bus.SwapBuffers();
    var events = _bus.ReadManaged<SpawnZoneObstacleCommand>();
    Assert.Single(events);
    Assert.Equal("zone_beta", events[0].ZoneName);
    Assert.Equal(100f, events[0].Position.X, precision: 2);
    Assert.Equal(200f, events[0].Position.Y, precision: 2);
    Assert.Equal(5f, events[0].Radius, precision: 2);
}
```

Add `using Hrot.ScenarioEditor.Gizmos;` to the file. Remove `using Hrot.Editor.Tools;` if no longer needed.

---

### Task 11: Create `EntityPlacementGizmoTests`

**File:** `Hrot/Engine/Hrot.Presentation.Tests/EntityPlacementGizmoTests.cs` (NEW)

Write tests **EPG-001 through EPG-006** covering the following scenarios.  All tests use `PlacementCanvasBridge` as the facade (since the gizmo is normally exercised through the bridge):

| ID | Test | What to verify |
|----|------|----------------|
| EPG-001 | `LeftClick_WritesExactlyOneCommand` | `HandleClick(Left)` on bridge triggers delegate exactly once |
| EPG-002 | `LeftClick_CommandHasCorrectTkbType` | `SpawnEntityCommand.TkbType` matches construction arg |
| EPG-003 | `LeftClick_CommandHasInitialTransformMatchingClickPosition` | `InitialTransform.Position.X/Y` match click X/Y |
| EPG-004 | `LeftClick_CommandHasNonEmptyRequestId` | `SpawnEntityCommand.RequestId != Guid.Empty` |
| EPG-005 | `RightClick_DoesNotPublish` | `HandleClick(Right)` fires no delegate |
| EPG-006 | `InitialAttributesJson_ForwardedVerbatim` | `SpawnEntityCommand.InitialAttributesJson` == the `initialPropertiesJson` arg |

For EPG-001 through EPG-006, construct the bridge+gizmo pair:
```csharp
private static (List<SpawnEntityCommand> captured, PlacementCanvasBridge bridge)
    CreateBridge(long tkbType = 202L, string? initialPropertiesJson = null)
{
    var captured = new List<SpawnEntityCommand>();
    PlacementCanvasBridge? bridge = null;
    var gizmo = new EntityPlacementGizmo(
        onEntityCreated:       cmd => captured.Add(cmd),
        tkbType:               tkbType,
        initialPropertiesJson: initialPropertiesJson,
        onRemove:              () => bridge?.RequestPop());
    bridge = new PlacementCanvasBridge(gizmo);
    return (captured, bridge);
}
```

Verify actual values (position X/Y as floats with precision:2, exact TkbType, RequestId not empty, InitialAttributesJson exact string equality).

---

### Task 12: Build and Verify

1. `dotnet build IOS-IG-SimHost.sln -c Debug --nologo -v q` — must be **0 errors**.
2. `dotnet test Hrot/Engine/Hrot.Presentation.Tests/ --no-build -v q` — all pass.
3. `dotnet test Hrot/Subsystems/Hrot.IG.Tests/ --no-build -v q` — **0 new failures** vs the pre-existing 68 baseline.
4. `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/ --no-build -v q` — all pass.

If tests fail, diagnose and fix root cause before moving to the report.

---

## Pass Conditions

| Condition | Status |
|-----------|--------|
| `CreationTool.cs` physically deleted | |
| `CreationToolConstants.cs` physically deleted | |
| `AreaPlacementTool.cs` physically deleted | |
| `RoutePlacementTool.cs` physically deleted | |
| `ObstaclePlacementTool.cs` physically deleted | |
| `CreationToolTests.cs` physically deleted | |
| `EntityPlacementGizmo` implements `IStatefulGizmo`, `RequiresExclusiveFocus = true` | |
| `ObstaclePlacementGizmo` implements `IStatefulGizmo`, `RequiresExclusiveFocus = true` | |
| `PlacementCanvasBridge` implements `IMapTool`, forwards events to gizmo | |
| `EditorSpawnAdapter`, `EditorZoneAdapter`, `MapCommandController` use bridge+gizmo | |
| `ToolPresenceTests` asserts `CreationTool` is absent | |
| Solution builds 0 errors | |
| `Hrot.Presentation.Tests`: all pass (incl. EPG-001..006) | |
| `Hrot.IG.Tests`: no new failures vs 68-failure baseline | |
| `Hrot.Editor.Tests`: all pass | |

---

## Notes on Type Conflicts

`MapMouseButton` exists in both `Fdp.Toolkit.Vis2D.Abstractions` (IMapTool world) and `Fdp.Toolkit.Diagnostics.Gizmos.Interaction` (gizmo world). In `PlacementCanvasBridge.cs`, use aliased usings:

```csharp
using GizmoMouseButton = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton;
using GizmoKeyboardKey = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey;
```

Follow `GizmoFocusInputBridge.cs` as a reference (it already uses this pattern at the top of the file).

---

## Report Requirements

Submit `.dev/gizmos-1/reports/BATCH-26-REPORT.md` containing:

- Pass condition table (filled in)
- Test result counts for each test project
- Files created / modified / deleted
- Issues encountered and how resolved
- Any design decisions made beyond the spec
