# BATCH-30 Report

## Summary

| Task   | Status   | Tests Added |
|--------|----------|-------------|
| GZ068  | Complete | 3           |
| GZ069  | Complete | 5           |
| GZ070  | Complete | 5           |
| **Total** | **All pass** | **13** |

Build result: **0 errors, 90 warnings (pre-existing)** — `dotnet build IOS-IG-SimHost.sln -c Debug --nologo -v q`

---

## TASK-GZ068 — Fix ImGui Window Stable ID and Eliminate Redundant Root Node

### Files Changed

| File | Change |
|------|--------|
| `FDP/ExtDeps/GizmoMap/GizmoMap.Presentation/UI/ImGuiPropertyTreeAdapter.cs` | Added `MakeWindowTitle` helper; updated both `windowTitle` variants to include `_{item.GizmoTypeId}` in the stable ID; replaced `DrawEditNode(doc!.Root, ...)` with `foreach (var child in doc!.Root.Children) DrawEditNode(child, ...)` |
| `FDP/ExtDeps/GizmoMap/GizmoMap.Presentation.Tests/GizmoPresentationTests.cs` | Added `ImGuiWindowStableIdTests` class with 3 tests |

### Description of Changes

1. **`MakeWindowTitle` helper** (`internal static`): extracts the window-title interpolation from `DrawScheduled` so tests can call it directly without an ImGui context. Both the `hasSchema` and fallback variants now append `_{gizmoTypeId}` after `_{networkId}` in the `###StructInsp_...` stable-ID segment. The visible title (before `###`) is unchanged.

2. **`DrawEditNode` call**: replaced the single `DrawEditNode(doc!.Root, item.IsReadOnly)` call with a `foreach` loop over `doc!.Root.Children`. Top-level fields of the struct now render directly inside the panel without the extra collapsible root wrapper.

### Tests

**Class:** `ImGuiWindowStableIdTests`

| Test ID | Method | What it verifies |
|---------|--------|-----------------|
| SC-GZ068-1 | `SC_GZ068_1_DifferentGizmoTypeId_DifferentStableId` | Two calls with the same `NetworkId`/`SchemaHash` but different `GizmoTypeId` produce different `###...` stable IDs |
| SC-GZ068-2 | `SC_GZ068_2_SameGizmoTypeId_SameStableId` | Same `NetworkId`/`GizmoTypeId` (different `SchemaHash`) produces the same stable ID — no regression |
| SC-GZ068-3 | `SC_GZ068_3_ExistingTestsUnaffected` | `MakeWindowTitle` produces a title containing `###` as the stable-ID separator; validates API surface is intact |

---

## TASK-GZ069 — Add Per-Inspector Viewing/Editing State Machine

### Files Changed

| File | Change |
|------|--------|
| `FDP/ExtDeps/GizmoMap/GizmoMap.Presentation/UI/ImGuiPropertyTreeAdapter.cs` | Added `InspectorState` enum (`internal`); added `_inspectorStates` dictionary (`internal`); added `InternalsVisibleTo`; added state cleanup at the top of `DrawScheduled`; added Viewing/Editing transitions and Apply-button guard inside `if (ImGui.Begin)` block; added `internal void DrawScheduled(Action<...>?, Func<string,bool>?)` test-only overload |

### Description of Changes

1. **`internal enum InspectorState { Viewing, Editing }`**: nested inside `ImGuiPropertyTreeAdapter`, marked `internal` so tests can reference it via `InternalsVisibleTo`.

2. **`internal readonly Dictionary<(long, uint), InspectorState> _inspectorStates`**: keyed by `(NetworkId, GizmoTypeId)`.

3. **State cleanup** (top of both `DrawScheduled` overloads): builds the current-frame key set from `_items`; removes any `_inspectorStates` entries whose key is absent from the current frame.

4. **State transitions** (inside `if (ImGui.Begin) { if (hasSchema) { ... } }`):
   - Viewing + focused → Editing; no callback.
   - Editing + unfocused → Viewing; invokes `onStructUpdate` once (if schema and not read-only).

5. **Apply-button guard**: checks that `_inspectorStates[stateKey] == Editing` before invoking `onStructUpdate`; prevents double-invocation when focus-loss fires in the same frame as Apply.

6. **Test-only overload** `DrawScheduled(onStructUpdate, isFocusedOverride)`: runs state cleanup and the full state-machine logic for each item, using `isFocusedOverride(windowTitle)` instead of `ImGui.IsWindowFocused`. Skips all ImGui draw calls. Also clears `_items` at the end (consistent with the public overload). When `isFocusedOverride` is `null`, focus defaults to `false`.

7. **`InternalsVisibleTo`**: `[assembly: InternalsVisibleTo("GizmoMap.Presentation.Tests")]` added at file scope (outside namespace) so the test project can access `_inspectorStates` and `InspectorState`.

### Testability Approach

The internal `DrawScheduled(onStructUpdate, Func<string, bool>? isFocusedOverride)` overload is used for SC-GZ069-1 and SC-GZ069-2 to inject focus without requiring an ImGui context. State pre-seeding via `adapter._inspectorStates[key] = InspectorState.Editing` is used to set up the initial conditions. `_items.Clear()` is called at the end of the internal overload so the stale-key cleanup in subsequent calls works correctly (SC-GZ069-4).

### Tests

**Class:** `InspectorStateMachineTests`

| Test ID | Method | What it verifies |
|---------|--------|-----------------|
| SC-GZ069-1 | `SC_GZ069_1_ViewingAndFocused_TransitionsToEditing_NoCallback` | Viewing + focused => state becomes Editing; `onStructUpdate` not called |
| SC-GZ069-2 | `SC_GZ069_2_EditingAndUnfocused_TransitionsToViewing_CallbackOnce` | Editing + unfocused => state becomes Viewing; `onStructUpdate` called exactly once |
| SC-GZ069-3 | `SC_GZ069_3_CallbackInvokedExactlyOnce_OnEditingToViewingTransition` | Same Editing->Viewing path; verifies callback receives correct `(networkId, gizmoTypeId, json)` tuple and is called exactly once |
| SC-GZ069-4 | `SC_GZ069_4_StaleEntry_RemovedWhenItemNotScheduled` | After an item is not scheduled in the next frame, its state entry is removed from `_inspectorStates` |
| SC-GZ069-5 | `SC_GZ069_5_NullCallback_DoesNotThrow` | `DrawScheduled` with `null` callback and `Editing` state does not throw |

---

## TASK-GZ070 — Wire GizmoUiState Subscription on Terminal Side

### Files Changed

| File | Change |
|------|--------|
| `FDP/ExtDeps/GizmoMap/GizmoMap.Presentation/UI/ImGuiPropertyTreeAdapter.cs` | Added `using GizmoMap.Network;`; added `public void ReceiveUiState(GizmoUiState state)` |
| `FDP/ExtDeps/GizmoMap/GizmoMap.Presentation/GizmoViewerFrontend.cs` | Added optional `ImGuiPropertyTreeAdapter? externalAdapter = null` parameter; uses `externalAdapter ?? new ImGuiPropertyTreeAdapter(schemaRegistry)` |
| `FDP/ExtDeps/GizmoMap/GizmoMap.Viewer/Program.cs` | Added `using var uiStateReader = new DdsReader<GizmoUiState>(participant)`; created `adapter` before `GizmoViewerFrontend.Run`; added `uiStateLoan` loop in `onUpdateTick`; passed `externalAdapter: adapter` to `Run` |
| `FDP/ExtDeps/GizmoMap/GizmoMap.Presentation.Tests/GizmoPresentationTests.cs` | Added `ReceiveUiStateTests` class with 5 tests; added `BoxBinding` and `TestDocFactory` helpers |

### Description of Changes

1. **`ReceiveUiState(GizmoUiState state)`**:
   - Returns silently if `_registry == null`.
   - Returns silently if `state.GizmoInstanceId` not found in registry.
   - Iterates `_items` to find all items matching `state.GizmoInstanceId` by `SchemaHash`; if any has `_inspectorStates[(networkId, gizmoTypeId)] == Editing`, returns without calling `Deserialize`.
   - Calls `EditDocumentJsonSerializer.Deserialize(state.EditDocumentJson, doc)` exactly once when all matching items are Viewing (or absent from the state dict).

2. **`GizmoViewerFrontend.Run`**: new optional parameter `ImGuiPropertyTreeAdapter? externalAdapter` (default `null`). This is backward-compatible; existing callers (including `GizmoMap.Example/Program.cs`) do not need to change.

3. **`Program.cs` composition root**:
   - `adapter` is created before `GizmoViewerFrontend.Run` and captured in `onUpdateTick` via lambda closure.
   - `uiStateReader.Take()` runs in `onUpdateTick` after the existing `primitivesReader.Take()` loop.
   - `adapter.ReceiveUiState(sample.Data)` is called per valid sample.

### Tests

**Helpers added:** `BoxBinding` (minimal `IValueBinding` for tests), `TestDocFactory.MakeIntDoc` (builds an `EditDocument` with one int leaf at path `$.X`).

**Class:** `ReceiveUiStateTests`

| Test ID | Method | What it verifies |
|---------|--------|-----------------|
| SC-GZ070-1 | `SC_GZ070_1_ReceiveUiState_AppliesJsonToBinding` | With all items Viewing, `ReceiveUiState` deserializes the JSON and updates the binding value |
| SC-GZ070-2 | `SC_GZ070_2_ReceiveUiState_BlockedWhenAnyItemIsEditing` | When one of two matching items is Editing, `Deserialize` is NOT called (binding unchanged) |
| SC-GZ070-3 | `SC_GZ070_3_UnknownGizmoInstanceId_NoException` | Unknown `GizmoInstanceId` silently returns, no exception |
| SC-GZ070-4 | `SC_GZ070_4_NullRegistry_NoException` | `null` registry: `ReceiveUiState` returns silently, no exception |
| SC-GZ070-5 | `SC_GZ070_5_ReceiveUiStateMethodExists` | `ReceiveUiState` is present on the public API surface (also verifies composition-root compiles at build time) |

---

## Build Result

```
dotnet build IOS-IG-SimHost.sln -c Debug --nologo -v q
  0 Error(s)
  90 Warning(s) (all pre-existing xUnit analyzer warnings in Hrot.ClusterRunner.Tests)
Time Elapsed 00:00:38.52
```

---

## Test Results

```
GizmoMap.Presentation.Tests: Failed: 0, Passed: 19, Skipped: 0, Total: 19
Hrot.Presentation.Tests (regression): Failed: 0, Passed: 72, Skipped: 0, Total: 72
```

The 6 pre-existing tests in `GizmoPresentationTests` (SC-GZ055-1 through SC-GZ055-6) were not modified and continue to pass.

---

## Design Deviations

| Deviation | Rationale |
|-----------|-----------|
| `GizmoViewerFrontend.Run` signature extended with `ImGuiPropertyTreeAdapter? externalAdapter = null` | The instructions noted that `adapter` "is already created before `GizmoViewerFrontend.Run` is called", which was not the case for the current code. Moving adapter creation to `Program.cs` required an optional parameter on `Run`. The change is backward-compatible (default `null`); `GizmoMap.Example/Program.cs` is unaffected. |
| `InspectorState` enum is `internal` (not `private`) | Required for `InternalsVisibleTo` testability; the design section implied `private`, but the batch instructions explicitly required `internal` for the test seam. |
| Internal `DrawScheduled` overload clears `_items` | Not stated explicitly in the instructions, but required for SC-GZ069-4 (stale-key cleanup) to function correctly without a live ImGui context. Consistent with the public overload's behaviour. |
| SC-GZ069-3 implemented via focus-loss path (not Apply button) | The Apply button path requires a live ImGui context (`ImGui.Button`). The batch instructions explicitly allowed simplifying SC-GZ069-3 to the same Editing→Viewing code path that focus-loss uses. The Apply-button guard code is present in production but not directly exercised by the test. |
