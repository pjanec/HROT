# Morning Fix Report — FIX-A + FIX-B

**Date:** 2026-06-06  
**Branch:** `blueprint-integ-1`  
**Agent:** claude-sonnet-4-6

---

## FIX-A Investigation — Was the Dispatcher Wired? Where Was the Bridge Missing?

### Root cause (two independent gaps)

1. **`SetFacetDispatcher` never called** for BTree or HSM perspectives.  
   `InspectorWindow.GetCurrentFacet()` guards on `_facetDispatcher != null`. The `ActiveChanged`
   handler in `EditorSubsystem` called `SetFacetEditService` for BTree/HSM but left
   `_facetDispatcher = null`, so the facet table never rendered regardless of what was selected.

2. **`ActiveSubSelection` never published** from the BTree/HSM canvas.  
   `InspectorWindow` additionally guards on `_store.ActiveSubSelection is {}`. Blueprint already
   installed an `AfterDraw` delegate (via `BlueprintSelectionBridgeHelper`) that publishes a
   `BlueprintNodeSelection` each frame from the canvas `SelectionState`. BTree and HSM had no
   equivalent — `AfterDraw` was `null`, so `ActiveSubSelection` stayed `null` indefinitely.

Both conditions must be true simultaneously for the facet table to appear. Both were broken.

### Additionally: `AiCanvasContext.AssetRef` not set in BTree/HSM factories

The blueprint factory already set `AssetRef = bpAsset`. BTree/HSM document factories returned a
context with `AssetRef = null`, making the per-frame delegate unable to retrieve the typed asset
from `ctx.AssetRef`.

---

## What Was Wired (Files Changed)

### New files

| File | Purpose |
|------|---------|
| `Hrot\Subsystems\AI\Hrot.BTree.Editor\Host\BTreeSelectionBridgeHelper.cs` | Pure static bridge: `MapSelection(SelectionState, BehaviorTreeAsset?)→BTreeNodeSelection?`, `BuildAfterDrawAction(AiSelectionStore)`, `BuildFacetDispatcher(BehaviorTreeAsset?)`. Canvas `NodeId.Value == BTreeEditorNode.VisualId` (direct, no asset walk). |
| `Hrot\Subsystems\AI\Hrot.Hsm.Editor\Host\HsmSelectionBridgeHelper.cs` | Same pattern for HSM: `MapSelection→HsmStateSelection?` (includes stale-id guard via `FindStateByStableId`), `BuildAfterDrawAction`, `BuildFacetDispatcher`. |

### Modified: `BTreeDocumentFactory.cs` and `HsmDocumentFactory.cs`

Added `AssetRef = btAsset` / `AssetRef = hsmAsset` to the returned `AiCanvasContext`.  
Comment added: "Store the asset in AssetRef so the composition root can wire the selection→Inspector bridge."

### Modified: `EditorSubsystem.cs` (two edit locations)

**Location 1 — after blueprint's `AfterDraw` assignment (~line 2285):**

```csharp
// FIX-A: wire per-frame canvas selection→Inspector bridges for BTree and HSM.
btreeCanvasWindow.AfterDraw =
    BTreeSelectionBridgeHelper.BuildAfterDrawAction(_btreeSelectionStore);
hsmCanvasWindow.AfterDraw =
    HsmSelectionBridgeHelper.BuildAfterDrawAction(_hsmSelectionStore);
```

**Location 2 — `ActiveChanged` handler, inside the BTree/HSM/else branches:**

```csharp
// BTree branch:
_btreeRegistrar?.Inspector.SetFacetDispatcher(
    BTreeSelectionBridgeHelper.BuildFacetDispatcher(btreeAsset));

// HSM branch:
_hsmRegistrar?.Inspector.SetFacetDispatcher(
    HsmSelectionBridgeHelper.BuildFacetDispatcher(hsmAsset));

// else branch (no BTree/HSM document active):
_btreeRegistrar?.Inspector.SetFacetDispatcher(null);
_hsmRegistrar?.Inspector.SetFacetDispatcher(null);
```

No new `using` directives were required — both `Hrot.BTree.Editor.Host` and
`Hrot.Hsm.Editor.Host` were already imported in EditorSubsystem.

---

## HSM Sub-Selection Coverage (which kinds wired vs noted)

| Selection kind | Status | Notes |
|----------------|--------|-------|
| **State node click** | **Wired** | Canvas `NodeId.Value == StateNode.StableId`. Stale-id guard: `FindStateByStableId` returns null → `ActiveSubSelection` stays null. |
| **Transition click** | **Not wired** | HSM transitions are `ILinkModel`, not `INodeModel`. They appear in `selection.Links`, not `selection.Nodes`. `HsmGlobalsStrip` already handles `HsmGlobalTransitionSelection` via its own mechanism. Regular transitions do not have a facet editor path in the current Inspector design. |

---

## FIX-B Format Change

**File:** `Hrot\Subsystems\Blueprints\Hrot.Blueprints.Editor\Host\BlueprintPinModel.cs`  
**Class:** `BlueprintPinDefaultValue` — `FormatValue` and `ParseValue`

### Before (culture-dependent)

`System.Numerics.Vector3` fell through to `value.ToString()`, which produces `"<0  4.5  0>"` on
invariant culture but `"<0  4,5  0>"` on locales using comma as decimal separator.

### After (invariant bracket format)

`FormatValue` now emits:
- `Vector2`: `[x, y]`
- `Vector3`: `[x, y, z]`
- `Vector4`: `[x, y, z, w]`
- `Quaternion`: `[x, y, z, w]`

All components formatted with `InvariantCulture` via `.ToString(inv)`.

`ParseValue` now handles these types in both the null/empty (zero-value) branch and the rawValue
branch, using private helpers `SplitFloats` / `ParseVector2` / `ParseVector3` / `ParseVector4` /
`ParseQuaternion`.

`SplitFloats` strips `[`, `]`, `<`, `>` delimiters and splits on `,`, ` `, `\t` — tolerates both
the old `<x  y  z>` format (backward compat) and the new `[x, y, z]` format.

---

## Tests

### New test files

| File | Tests | Result |
|------|-------|--------|
| `Hrot\Subsystems\AI\Hrot.BTree.Editor.Tests\Host\BTreeSelectionBridgeHelperTests.cs` | 8 | 8/8 pass |
| `Hrot\Subsystems\AI\Hrot.Hsm.Editor.Tests\Host\HsmSelectionBridgeHelperTests.cs` | 9 | 9/9 pass |

BTree tests cover: null-asset guard, empty/multi/link-selection guards, single-action-node happy
path, root-node happy path, end-to-end `GetCurrentFacet` integration, and null-dispatcher guard.

HSM tests cover: null-asset, empty/multi/link guards, unknown-id stale-id guard, two state happy
paths (Idle/Active), end-to-end `GetCurrentFacet` integration asserting `StateFacet` with correct
`Name`, and null-dispatcher guard.

### FIX-B tests added to existing file

`Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Host\BlueprintPinDefaultValueTests.cs` — 9
new tests covering Vector2/3/4/Quaternion format and parse round-trips.

---

## Build / Test Results

| Project | Build | Tests |
|---------|-------|-------|
| `Hrot.BTree.Editor` | 0 errors | — |
| `Hrot.Hsm.Editor` | 0 errors | — |
| `Hrot.Editor` (EditorSubsystem) | 0 errors | — |
| `Hrot.BTree.Editor.Tests` | 0 errors | 399/399 pass |
| `Hrot.Hsm.Editor.Tests` | 0 errors | 348/348 pass |
| `Hrot.Blueprints.Tests` | 0 errors | 1563/1575 pass, 4 known pre-existing failures |

### Failing tests (pre-existing, no new failures)

| Test | Category |
|------|----------|
| `Library_EmitMatchesGoldenSource` | LibraryMath CRLF flake |
| `LibraryMath_GeneratedSource_Snapshot` | LibraryMath CRLF flake |
| `Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` | ScoreCrossed (pre-existing) |
| `TickFrame_1000Frames_AllocatesZeroBytes` | AllocatesZeroBytes (pre-existing) |

All four were on the permitted pre-existing list from the task constraints. Zero new failures.

---

## STOP

None. All constraints satisfied:
- Branch `blueprint-integ-1`; projection-only.
- No commit made.
- No user experiment files touched (Counting/Loco1/InlineEd1/EnumDemo .bp.json untouched).
- Build: 0 CS errors across all affected projects.
- Tests: 0 new failures.
