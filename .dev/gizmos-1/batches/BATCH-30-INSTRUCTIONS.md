# BATCH-30 Instructions

## Context

This batch implements **Phase 23: StructInspector Refinements** — three tasks that complete the
gizmo composite-identity work.  All three tasks touch the same file
(`ImGuiPropertyTreeAdapter.cs`) plus the composition root (`GizmoMap.Viewer/Program.cs`).

**Workspace root:** `d:\Work\IOS-IG-SimHost-FDP-2`
**Solution file:** `IOS-IG-SimHost.sln`
**Build command:** `dotnet build IOS-IG-SimHost.sln -c Debug --nologo -v q`
**Test framework:** xUnit

---

## Reference Material

All three tasks are fully specified in the design and detail documents:

| Resource | Path |
|----------|------|
| Design § 11.7 | `.dev/gizmos-1/DESIGN.md` (search `### 11.7`) |
| Design § 11.8 | `.dev/gizmos-1/DESIGN.md` (search `### 11.8`) |
| Design § 11.9 | `.dev/gizmos-1/DESIGN.md` (search `### 11.9`) |
| Task detail GZ068 | `.dev/gizmos-1/TASK-DETAIL.md` (search `TASK-GZ068`) |
| Task detail GZ069 | `.dev/gizmos-1/TASK-DETAIL.md` (search `TASK-GZ069`) |
| Task detail GZ070 | `.dev/gizmos-1/TASK-DETAIL.md` (search `TASK-GZ070`) |

**Read the design and task detail sections before starting.**  This batch only gives
implementation checkpoints and critical guidance that is not already in those documents.

---

## Existing File State (after BATCH-29)

`FDP/ExtDeps/GizmoMap/GizmoMap.Presentation/UI/ImGuiPropertyTreeAdapter.cs` currently:
- Has `ScheduledItem.GizmoTypeId` field (added in GZ066).
- Has `DrawScheduled(Action<long, uint, string>? onStructUpdate = null)` signature (GZ066).
- Window title stable ID is `###StructInsp_{item.NetworkId}` — missing `_{item.GizmoTypeId}` (GZ068 will add this).
- Renders root node via `DrawEditNode(doc!.Root, item.IsReadOnly)` — still the old form (GZ068 will change this).
- No `_inspectorStates` dict yet (GZ069 will add this).
- No `ReceiveUiState` method yet (GZ070 will add this).

`GizmoMap.Viewer/Program.cs` currently:
- Has `DdsReader<DebugPrimitivesBatch>`, `DdsReader<StringInternEntry>`, `DdsWriter<GizmoInteractionBatch>`.
- Does NOT yet subscribe to `GizmoUiState` (GZ070 will add this).

---

## TASK-GZ068 — Fix ImGui Window Stable ID and Eliminate Redundant Root Node

**File:** `FDP/ExtDeps/GizmoMap/GizmoMap.Presentation/UI/ImGuiPropertyTreeAdapter.cs`

**Change 1:** Append `_{item.GizmoTypeId}` to the stable-ID portion of both `windowTitle`
string variants.

Current code (two lines, one for hasSchema and one for fallback):
```
$"...###StructInsp_{item.NetworkId}"
```
Required (visible title before `###` stays unchanged):
```
$"...###StructInsp_{item.NetworkId}_{item.GizmoTypeId}"
```

**Change 2:** Replace the single `DrawEditNode(doc!.Root, item.IsReadOnly)` call with:
```csharp
foreach (var child in doc!.Root.Children)
    DrawEditNode(child, item.IsReadOnly);
```

`DrawEditNode` itself is NOT modified.

### Tests for GZ068

Add tests to `FDP/ExtDeps/GizmoMap/GizmoMap.Presentation.Tests/GizmoPresentationTests.cs`
in a new `ImGuiWindowStableIdTests` class.

**Important:** `ImGui.Begin` requires an ImGui context. The GZ068 tests MUST NOT call
`DrawScheduled` via a live ImGui context.  Instead they should test the window title string
directly by making the title generation logic testable (see approach below).

**Recommended testability approach for SC-GZ068-1 and SC-GZ068-2:**

Add an `internal` helper to `ImGuiPropertyTreeAdapter` (not a public method, not changing any
public API) that formats the window title string for a given `ScheduledItem`. Mark the method
`internal` and add `[assembly: InternalsVisibleTo("GizmoMap.Presentation.Tests")]` to
`ImGuiPropertyTreeAdapter.cs`.

Alternatively: extract the window-title interpolation into a `private static string
MakeWindowTitle(...)` method that is `internal static` for testability.

**SC-GZ068-1** — Two items with the same `NetworkId` and `SchemaHash` but different
`GizmoTypeId` values produce different stable IDs (the `###StructInsp_..._<id>` suffix
differs).

**SC-GZ068-2** — Two items with the same `NetworkId` and same `GizmoTypeId` produce the SAME
stable ID (regression: items that should share a window still share it after the fix).

**SC-GZ068-3** — Existing `GizmoPresentationTests` compile and pass without modification.

---

## TASK-GZ069 — Add Per-Inspector Viewing/Editing State Machine

**File:** `FDP/ExtDeps/GizmoMap/GizmoMap.Presentation/UI/ImGuiPropertyTreeAdapter.cs`

Add the following members:

```csharp
private enum InspectorState { Viewing, Editing }
private readonly Dictionary<(long, uint), InspectorState> _inspectorStates = new();
```

In `DrawScheduled`, add two responsibilities:

**A. State cleanup (at the top of DrawScheduled, before the items loop):**
```csharp
// Remove stale entries whose (NetworkId, GizmoTypeId) is not in the current frame.
var keys = new HashSet<(long, uint)>(_items.Select(it => (it.NetworkId, it.GizmoTypeId)));
foreach (var k in _inspectorStates.Keys.Except(keys).ToList())
    _inspectorStates.Remove(k);
```

**B. Per-item state transitions (inside the `if (ImGui.Begin(windowTitle))` block, after
rendering the schema content but BEFORE the `ImGui.Separator` / Apply button):**

```csharp
var stateKey = (item.NetworkId, item.GizmoTypeId);
bool isFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);

if (!_inspectorStates.TryGetValue(stateKey, out var state))
    state = InspectorState.Viewing;

if (state == InspectorState.Viewing && isFocused)
{
    _inspectorStates[stateKey] = InspectorState.Editing;
}
else if (state == InspectorState.Editing && !isFocused)
{
    _inspectorStates[stateKey] = InspectorState.Viewing;
    if (hasSchema && !item.IsReadOnly && onStructUpdate != null)
    {
        string json = EditDocumentJsonSerializer.Serialize(doc!);
        onStructUpdate.Invoke(item.NetworkId, item.GizmoTypeId, json);
    }
}
```

**Apply button (already at the bottom of the hasSchema block):** Keep the existing
`if (ImGui.Button("Apply"))` block. When Apply is clicked AND the current state is `Editing`,
transition to `Viewing` and invoke `onStructUpdate` — but ONLY if the focus-loss branch has not
already invoked it in the same frame. To avoid double-invocation: the Apply block should check
that the state is still `Editing` before invoking (the focus-loss block above runs first and
may have already transitioned).

```csharp
if (!item.IsReadOnly && onStructUpdate != null)
{
    ImGui.Separator();
    if (ImGui.Button("Apply"))
    {
        if (!_inspectorStates.TryGetValue(stateKey, out var applyState) ||
            applyState == InspectorState.Editing)
        {
            _inspectorStates[stateKey] = InspectorState.Viewing;
            string json = EditDocumentJsonSerializer.Serialize(doc!);
            onStructUpdate.Invoke(item.NetworkId, item.GizmoTypeId, json);
        }
    }
}
```

**Critical constraints (from DESIGN.md §11.8):**
- State machine key is `(NetworkId, GizmoTypeId)` — never `SchemaHash`.
- `IsReadOnly` flag is NOT changed by state — the widgets remain interactive so
  focus/Viewing→Editing can fire.
- `onStructUpdate` must be invoked exactly once per Editing→Viewing transition even if both
  focus-loss and Apply happen in the same frame.
- `ImGui.IsWindowFocused` must be called AFTER `ImGui.Begin` and only when Begin returned true.

**Test testability seam for GZ069:**

The tests for GZ069 cannot call live ImGui, so `_inspectorStates` must be `internal` (not
private) and the test project must be added to `InternalsVisibleTo`.

Additionally, to simulate focus changes, add the following `internal` overload of `DrawScheduled`
that accepts an injected focus map:

```csharp
internal void DrawScheduled(
    Action<long, uint, string>? onStructUpdate,
    Func<string, bool>? isFocusedOverride)
```

When `isFocusedOverride` is non-null, replace `ImGui.IsWindowFocused(...)` with
`isFocusedOverride(windowTitle)`. When `isFocusedOverride` is null (production path), call
`ImGui.IsWindowFocused` as normal — but only if `ImGui.Begin` actually returned true (the
production path unchanged). Also: when `isFocusedOverride` is non-null, skip the actual
`ImGui.Begin` / `ImGui.End` calls and run only the state-machine and callback logic; this
avoids requiring an ImGui context in tests.

In practice the internal overload's body should be:
1. Run the state cleanup.
2. For each item, build `windowTitle` (for use as the focus-override key), check
   `isFocusedOverride?.Invoke(windowTitle)`, apply state transitions, and invoke callbacks.
3. Skip all ImGui draw calls (no `Begin`, no `End`, no `Button`).

The public overload calls the internal overload with `isFocusedOverride: null` and then runs
the full ImGui render path as before.

**Alternatively**, if this two-overload approach makes the code too complex, a simpler approach
is to:
- Make `_inspectorStates` internal.
- In tests, manually set `_inspectorStates[(key)] = InspectorState.Editing` to pre-seed state,
  then call `DrawScheduled(onStructUpdate)` (the normal overload) and only assert on callback
  invocations via `onStructUpdate`, not on ImGui rendering.
- Only SC-GZ069-1 and SC-GZ069-2 (which test the focus-transition path) need the injected
  focus path; SC-GZ069-3 (Apply) and SC-GZ069-4 (cleanup) and SC-GZ069-5 (null callback) can
  be tested via state pre-seeding alone.

**Whatever testability approach is chosen, document it in the report.**

### Tests for GZ069

**SC-GZ069-1:** Schedule one item. Call the internal overload with `isFocused = true`. Assert
that `_inspectorStates[(networkId, gizmoTypeId)] == InspectorState.Editing` after the call, and
that `onStructUpdate` was NOT invoked.

**SC-GZ069-2:** Pre-seed `_inspectorStates[(networkId, gizmoTypeId)] = Editing`. Call the
internal overload with `isFocused = false`. Assert that `onStructUpdate` was invoked exactly
once and that `_inspectorStates[(networkId, gizmoTypeId)] == Viewing`.

**SC-GZ069-3:** Pre-seed `_inspectorStates[(networkId, gizmoTypeId)] = Editing`. Call
`ReceiveUiState` (from GZ070 — add in GZ070 first or add a stub). Assert Apply
path invokes onStructUpdate; OR add a separate ApplyButton test once GZ070 is done. *(This can
be simplified to: use the internal overload, seed Editing, simulate Apply by calling
`onStructUpdate` directly if the Apply logic is in the same code path as focus-loss.)*
See SC-GZ069-3 in TASK-DETAIL.md for the full spec.

**SC-GZ069-4:** Schedule an item, draw one frame, then call `DrawScheduled` WITHOUT scheduling
the item. Assert `_inspectorStates` does not contain the key.

**SC-GZ069-5:** Call the normal `DrawScheduled(onStructUpdate: null)`. Must compile and not
throw even with no callback.

---

## TASK-GZ070 — Wire GizmoUiState Subscription on Terminal Side

### Part A: `ImGuiPropertyTreeAdapter.ReceiveUiState`

**File:** `FDP/ExtDeps/GizmoMap/GizmoMap.Presentation/UI/ImGuiPropertyTreeAdapter.cs`

Add the `using GizmoMap.Network;` import (already referenced by the project) then add the
public method:

```csharp
public void ReceiveUiState(GizmoUiState state)
{
    if (_registry == null) return;
    if (!_registry.TryGet(state.GizmoInstanceId, out var doc) || doc == null) return;

    // Block update if ANY scheduled item matching this schema is being edited.
    foreach (var item in _items)
    {
        if (item.SchemaHash != state.GizmoInstanceId) continue;
        if (_inspectorStates.TryGetValue((item.NetworkId, item.GizmoTypeId), out var s) &&
            s == InspectorState.Editing)
            return;
    }

    EditDocumentJsonSerializer.Deserialize(state.EditDocumentJson, doc);
}
```

**`_items` availability note:** `_items` is cleared at the END of `DrawScheduled`, so
`ReceiveUiState` sees the items that were scheduled in the most recent frame's `Schedule` calls
(before `DrawScheduled` ran). This is the correct temporal window: the terminal calls
`Schedule` in the render pass, then calls `DrawScheduled`, then calls `ReceiveUiState` with
any new DDS samples. Adjust the `_items.Clear()` timing if needed so it clears at the START of
`DrawScheduled` (after state cleanup) rather than at the end — OR keep it at the end and
document that `ReceiveUiState` uses the previous frame's item list (acceptable because DDS
sample rate is low).

**Constraints (from DESIGN.md §11.9 and TASK-DETAIL.md TASK-GZ070):**
- Return silently if `_registry == null`.
- Return silently if `GizmoInstanceId` not found in registry.
- Never call `Deserialize` if any matching item is in Editing state.
- `Deserialize` is called at most once per `ReceiveUiState` call.

### Part B: Wire DDS subscription in GizmoMap.Viewer

**File:** `FDP/ExtDeps/GizmoMap/GizmoMap.Viewer/Program.cs`

1. Add `using var uiStateReader = new DdsReader<GizmoUiState>(participant);` after the existing
   readers.
2. Inside `onUpdateTick`, after the `renderBuffer.Clear()` and the existing readers' Take loops,
   add:
   ```csharp
   using var uiStateLoan = uiStateReader.Take();
   foreach (var sample in uiStateLoan)
   {
       if (sample.IsValid)
           adapter.ReceiveUiState(sample.Data);
   }
   ```
   where `adapter` is the `ImGuiPropertyTreeAdapter` instance. (If `adapter` is not currently
   in scope at `onUpdateTick`, capture it from the outer scope as a local variable — it is
   already created before `GizmoViewerFrontend.Run` is called.)

**Check:** Read the current `Program.cs` carefully to locate where `adapter` is created and
confirm it is accessible in the lambda. Adjust capture if needed.

### Tests for GZ070

Add to `GizmoMap.Presentation.Tests/GizmoPresentationTests.cs` in a new
`ReceiveUiStateTests` class.

**SC-GZ070-1:** Create an `ImGuiPropertyTreeAdapter` with a `GizmoSchemaRegistry` that
contains one registered `EditDocument`. Call `ReceiveUiState` with a `GizmoUiState` whose
`GizmoInstanceId` matches the schema hash and `EditDocumentJson` encodes a value override.
Assert that the `EditDocument`'s `IValueBinding` reflects the new value. *(Use
`EditDocumentJsonSerializer.Deserialize` behaviour to construct the expected state.)*

**SC-GZ070-2:** Register one schema. Schedule two items with the same `SchemaHash` but different
`GizmoTypeId`. Pre-seed `_inspectorStates[(networkId1, gizmoTypeId1)] = Editing`. Call
`ReceiveUiState` with a JSON payload. Assert the `EditDocument` is NOT updated (blocking
works).

**SC-GZ070-3:** Call `ReceiveUiState` with an unrecognised `GizmoInstanceId`. No exception;
no side effects.

**SC-GZ070-4:** Construct `ImGuiPropertyTreeAdapter` with `registry: null`. Call
`ReceiveUiState`. No exception.

**SC-GZ070-5:** Confirm `GizmoMap.Viewer` compiles (`dotnet build` exits 0). This covers the
composition-root wiring.

---

## Checklist

Before writing the report, verify:

- [ ] GZ068: Both `windowTitle` string variants include `_{item.GizmoTypeId}` in the stable-ID
      segment; the visible title (before `###`) is unchanged.
- [ ] GZ068: `DrawEditNode(doc!.Root, ...)` replaced by `foreach` over `doc!.Root.Children`.
- [ ] GZ068: SC-GZ068-1 verifies different GizmoTypeId → different stable IDs.
- [ ] GZ068: SC-GZ068-2 verifies same GizmoTypeId → same stable ID (no regression).
- [ ] GZ069: `_inspectorStates` is `internal`; `InternalsVisibleTo` added.
- [ ] GZ069: State cleanup runs at top of DrawScheduled; stale keys removed.
- [ ] GZ069: Viewing→Editing on focus gained; no callback.
- [ ] GZ069: Editing→Viewing on focus lost; callback invoked exactly once.
- [ ] GZ069: Apply button transitions Editing→Viewing; callback invoked exactly once even if
      focus-loss already fired.
- [ ] GZ069: SC-GZ069-1..5 all pass.
- [ ] GZ070: `ReceiveUiState` silently no-ops when registry null or id not found.
- [ ] GZ070: `ReceiveUiState` blocks `Deserialize` when any matching item is Editing.
- [ ] GZ070: `ReceiveUiState` calls `Deserialize` once when all matching items are Viewing.
- [ ] GZ070: `Program.cs` has `DdsReader<GizmoUiState>` and calls `ReceiveUiState` per frame.
- [ ] GZ070: SC-GZ070-1..5 all pass.
- [ ] Build: `dotnet build IOS-IG-SimHost.sln -c Debug --nologo -v q` exits 0.

---

## Deliverable

Write your report to: `.dev/gizmos-1/reports/BATCH-30-REPORT.md`

Standard report format:
- Summary table (task / status / tests added)
- Per-task: files changed, what changed, why
- Tests: class name, test IDs, what each test verifies
- Build result
- Any design deviations and their rationale
