# BATCH-HS-06 REPORT — Validation surfacing (Diagnostics + node state + region-conflict overlay)

**Date:** 2026-06-13  
**Branch:** `blueprint-integ-1`  
**Task:** TASK-HS-06  
**Status:** ✅ DONE — Failed: 0, 0 build errors, 432 passed (+9 new)

---

## Part (a) — Register HsmAssetValidator

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`  
**Lines:** 1961–1964 (added `validators:` argument to `_hsmRegistrar` constructor)

```csharp
_hsmRegistrar = new PerspectiveWorkspaceRegistrar(
    "HSM", _hsmSelectionStore, catalog, refactorService, debugRegistry,
    validators: new Hrot.Editor.AiShared.Validation.IAssetValidator[]    // ← NEW
    {
        new Hrot.Hsm.Editor.Validation.HsmAssetValidator(sharedSchemaExporter),  // ← NEW
    },                                                                           // ← NEW
    breakpointManager:             _bpManager,
    ...
```

- Uses the same `sharedSchemaExporter` (`ActionSchemaExporter`) variable as the BTree registrar (line 1956)
- Fully qualified type name `Hrot.Hsm.Editor.Validation.HsmAssetValidator` — no extra using added
- Mirror of the BTree registrar pattern at line 1944–1958

---

## Part (b) — StateNode node-state from diagnostics

### StateNode fields and getters

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs`  

Added ephemeral diagnostic fields to `StateNode` (lines after existing `IsBreakpoint` field):
```csharp
public NodeState? DiagnosticState;    // nullable, editor-only, not persisted
public string?    DiagnosticTooltip;
```

Updated getters:
```csharp
public NodeState State => DiagnosticState ?? (IsBreakpoint ? NodeState.Warning : NodeState.Normal);
public string?   StatusTooltip => DiagnosticTooltip;
```

- `DiagnosticState` is nullable and defaults `null` — preserves breakpoint fallback behavior
- When a state has no diagnostic, `DiagnosticState` remains `null`, so `State` falls through to the breakpoint check

### HsmGraphModel.BuildCaches projection

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmGraphModel.cs`

Added usings: `Hrot.Editor.AiShared.Blackboard`, `Hrot.Hsm.Editor.Validation`.

New `BuildCaches()` (lines 38–79):
1. Runs `new HsmValidator().Validate(_asset, _asset as IBlackboardManagedAsset)` — includes blackboard for region-conflict checks
2. Maps `StableId` → worst severity (Error-wins) + message via `Dictionary<Guid, (NodeState, string)>`
3. Projects onto states: sets `DiagnosticState`/`DiagnosticTooltip` when diagnostic exists, resets to `null` when absent (preserves breakpoint fallback)
4. Builds link cache for transitions (preserved from original)
5. Sets `LastDiagnostics` and fires `DiagnosticsRecomputed`

New public API:
```csharp
public IReadOnlyList<HsmDiagnostic> LastDiagnostics { get; private set; } = Array.Empty<HsmDiagnostic>();
public event Action<IReadOnlyList<HsmDiagnostic>>? DiagnosticsRecomputed;
```

---

## Part (c) — Feed HsmRegionConflictsRenderer

### Test-visible getter

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmRegionConflictsRenderer.cs`

Added after `SetDiagnostics`:
```csharp
internal IReadOnlyList<HsmDiagnostic>? CurrentDiagnostics => _diagnostics;
```

### Wiring in HsmDocumentFactory

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmDocumentFactory.cs`

- `BuildRenderers` signature changed to include `out HsmRegionConflictsRenderer regionConflictsRenderer`
- The renderer instance is captured as a local (`regionConflictsRenderer = new HsmRegionConflictsRenderer(hsmAsset)`) and added to the list
- In `Build()`, after both `graphModel` and `renderers` are constructed (lines 95–100):
  ```csharp
  graphModel.DiagnosticsRecomputed += regionConflicts.SetDiagnostics;
  regionConflicts.SetDiagnostics(graphModel.LastDiagnostics); // initial push
  ```
- `HsmGraphModel` has no reference to the renderer type — wiring is in the factory (decoupled)

---

## Tests

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmDiagnosticSurfaceTests.cs` (new, 9 tests)

| # | Test | Assertions |
|---|------|-----------|
| 1 | `Composite_without_initial_child_sets_state_to_Error` | `State == NodeState.Error`, `StatusTooltip` non-null, contains "no child marked as initial" |
| 2 | `Valid_simple_state_has_Normal_state` | `State == NodeState.Normal`, `StatusTooltip == null` |
| 3 | `Breakpoint_preserved_when_no_diagnostic` | `DiagnosticState == null`, `State == NodeState.Warning` (breakpoint fallback), `StatusTooltip == null` |
| 4 | `LastDiagnostics_contains_diagnostics_for_broken_machine` | `LastDiagnostics` non-empty, contains `CompositeWithoutInitialChild` Error |
| 5 | `DiagnosticsRecomputed_fires_on_rebuild` | Subscribes, triggers `MarkDirty()`, verifies event fired with empty diagnostics (valid machine) |
| 6 | `Renderer_wiring_pushes_diagnostics` | `SetDiagnostics(list)` → `CurrentDiagnostics.Should().BeSameAs(list)` |
| 7 | `Parallel_state_with_conflicting_lanes_produces_OutputLaneConflict` | `LastDiagnostics` contains `OutputLaneConflict` Warning |
| 8 | `HsmAssetValidator_supported_kind_is_Hsm` | `SupportedKind == AssetKind.Hsm` |
| 9 | `HsmAssetValidator_produces_diagnostics_for_broken_asset` | `Validate()` returns ≥1 `AssetDiagnostic` with Code `CompositeWithoutInitialChild`, severity `Error` |

Tests use direct-asset-construction pattern (`MakeAsset` helper, same as existing `HsmValidationTests`).

---

## Build & Test Results

```
dotnet build Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj     → 0 errors ✅
dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj → 0 errors, 0 warnings ✅
dotnet test  Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests           → Failed: 0, Passed: 432 ✅
```

- **Before:** 423 passed (baseline)
- **After:** 432 passed (+9 new tests)
- **New failures:** 0
- **Hrot.Editor pre-existing issues:** 1 pre-existing warning BTREE0002 (unrelated — `CombatShowcase.btree.json` delegate shape)
- **Env var:** No `BLUEPRINT_REGENERATE_SNAPSHOTS` used

---

## Files Touched

1. `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — added `validators:` to HSM registrar (lines 1961–1964)
2. `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs` — `DiagnosticState`/`DiagnosticTooltip` fields + updated `State`/`StatusTooltip`
3. `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmGraphModel.cs` — validation projection in `BuildCaches`, `LastDiagnostics`, `DiagnosticsRecomputed`
4. `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmRegionConflictsRenderer.cs` — `CurrentDiagnostics` getter
5. `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmDocumentFactory.cs` — renderer wiring (out param + event subscription)
6. `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmDiagnosticSurfaceTests.cs` — new test file

No other files modified. No suppressions, commented code, weakened asserts, or excluded files.
