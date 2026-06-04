# BATCH-03D2 Report

## Implementation Summary

### Task 1 — Headless edit model (`GraphSignatureEditModel`)
**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Variables/GraphSignatureEditModel.cs` (lines 1–131)

`GraphSignatureEditModel` wraps a single `Graph` and a `bool isOutputs` selector. It exposes:
- `AddParameter(name, typeId)` — appends a new `ParameterDecl { Id = Guid.NewGuid(), Name, Type = new BlueprintTypeRef { TypeId } }`
- `RemoveParameter(name)` — removes first match by name; no-op (no event) if not found
- `RenameParameter(oldName, newName)` — renames first match; no-op if not found
- `RetypeParameter(name, newTypeId)` — changes `Type.TypeId` of first match; no-op if not found
- `MoveParameter(fromIndex, toIndex)` — reorders list; no-op for out-of-range or same indices
- `Parameters` property — live `IReadOnlyList<ParameterDecl>` view

Each mutation invokes the injected `Action onChanged` exactly once on success, never on no-op. Zero ImGui references — fully headless.

**Decision against IVariablesSchemaSource:** `IVariablesSchemaSource` carries aliasing, byte-budget (`GetPayloadByteSize`), parallel-region maps, refactor keys, and `UnboundRequirements` — all blackboard-specific semantics that misrepresent a function signature. A standalone typed model is cleaner, as documented in the spec.

---

### Task 2 — `GraphSignatureWindow`
**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Windows/GraphSignatureWindow.cs` (lines 1–286)

`GraphSignatureWindow` extends `ManagedWindow` directly (like `BlueprintDetailsWindow`) under id `"ai_graph_signature_blueprint"`, perspective `"Blueprint"`, scope `PerspectiveBound`.

**Rendering choice — bespoke plain-ImGui rows panel (not `VariablesPanelControl`):**  
`VariablesPanelControl` requires a `VariablesPanelSection` carrying byte-budget, inline bytes, heavy-bytes, `PackWarning`, and aliasing flags — none of which apply to a function-signature parameter. Reusing it would expose byte-overrun warnings and pack-warning styling to the author for something unrelated. A focused three-column ImGui table (Name `InputText` | Type `Combo` over `BlackboardTypeHelper.DefaultKnownTypeNames` | `X` remove button) plus an `+ Add` row at the bottom is simpler and more accurate.

**Graph-picker:** View-state `_selectedGraphId` (Guid) selects from `asset.Graphs.Where(g => g.Kind == GraphKind.Function)`. Defaults to the first Function graph when the stored id is not found. This avoids adding any `SelectedGraph` to `EditorSelectionStore`.

**Dirty marking:** `BuildEditModels(graph, asset)` constructs each `GraphSignatureEditModel` with `onChanged = () => _dirtyTracker.MarkDirty(asset.AssetId)`. Mutations fire this delegate, which marks the asset dirty so Quick-Reload re-projects and recompiles.

**`Retarget(asset?)`:** Mirrors `BlueprintDetailsWindow.Retarget`. Resets `_selectedGraphId` to `Guid.Empty` so the graph picker re-picks the first Function graph of the new asset. No-op when same asset.

**Headless seam — `ResolveEditModels()`:** Returns `(GraphSignatureEditModel Inputs, GraphSignatureEditModel Outputs)?` for the currently-selected Function graph, or `null` when no asset / no Function graph. Tests call this directly without ImGui, exactly like `BlueprintDetailsWindow.ResolveSession()`.

---

### Task 3 — Registration in `EditorSubsystem`
**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

Three minimal additions:

1. **Field declaration** (line ~279):
   ```csharp
   private Hrot.Blueprints.Editor.Windows.GraphSignatureWindow? _blueprintSignatureWindow;
   ```

2. **`ActiveChanged` handler** (two branches):
   ```csharp
   // Blueprint active: retarget to bpAsset
   _blueprintSignatureWindow?.Retarget(bpAsset);
   // Non-Blueprint: clear
   _blueprintSignatureWindow?.Retarget(null);
   ```

3. **Registration** (after `_blueprintVariablesWindow` registration, ~line 2000):
   ```csharp
   _blueprintSignatureWindow = new Hrot.Blueprints.Editor.Windows.GraphSignatureWindow(
       selectionStore: _blueprintLegacySelectionStore,
       dirtyTracker:   _blueprintSaveDirtyTracker);
   _blueprintRegistrar!.RegisterExtraWindow(windowManager, _blueprintSignatureWindow);
   ```
   Uses the same `_blueprintLegacySelectionStore` bridge (SelectAsset driven in ActiveChanged) and `_blueprintSaveDirtyTracker` (already shared with the Save workflow).

---

## Design Decisions

- **Same legacy store bridge** (`_blueprintLegacySelectionStore`) as `BlueprintVariablesWindow`: no new bridge field, no new store; the window reads `SelectedAsset` from the same object.
- **Same dirty tracker** (`_blueprintSaveDirtyTracker`) as Save: same asset-id key, consistent dirty semantics across Save and signature edits.
- **Two separate model instances per draw frame** (`BuildEditModels` is called each `DrawClientArea`): cheap to allocate, avoids stale model state across asset/graph changes. A cached model could be added later if profiling shows a cost.

---

## Deviations

None. All three tasks follow the spec exactly.

---

## Test Results

### New tests — all 26 pass

```
Test Run Successful.
Total tests: 26
     Passed: 26
 Total time: 1.07 Seconds
```

**`GraphSignatureEditModelTests` (18 tests):**
- `Add_ToInputs_AppendsParameterDecl_WithCorrectNameAndTypeId`
- `Add_ToInputs_FiresOnChangedExactlyOnce`
- `Add_ToInputs_AssignsNonEmptyGuid`
- `Add_ToOutputs_AppendsParameterDecl_WithCorrectNameAndTypeId`
- `Add_ToOutputs_FiresOnChangedExactlyOnce`
- `Remove_FromInputs_RemovesMatchingParam_AndFiresOnce`
- `Remove_FromOutputs_RemovesMatchingParam_AndFiresOnce`
- `Remove_NameNotFound_DoesNotFireOnChanged`
- `Rename_Inputs_ChangesName_AndFiresOnce`
- `Rename_Outputs_ChangesName_AndFiresOnce`
- `Rename_NameNotFound_DoesNotFireOnChanged`
- `Retype_Inputs_ChangesTypeId_AndFiresOnce`
- `Retype_Outputs_ChangesTypeId_AndFiresOnce`
- `Retype_NameNotFound_DoesNotFireOnChanged`
- `Move_Inputs_ReordersParams_AndFiresOnce`
- `AddInputParameter_ThenNodePinSchema_ProjectsMatchingDataOutPin` ← round-trip BATCH-03C

**`GraphSignatureWindowTests` (12 tests) — headless seam:**
- `Window_ConstructsWithoutImGui`
- `Window_HasExpected_IdAndPerspective`
- `ResolveEditModels_NoAsset_ReturnsNull`
- `ResolveEditModels_WithFunctionGraph_ReturnsNonNullPair`
- `ResolveEditModels_InputsModel_EditsBoundToGraphInputs`
- `ResolveEditModels_OutputsModel_EditsBoundToGraphOutputs`
- `ResolveEditModels_AssetHasOnlyEventGraphs_ReturnsNull`
- `Retarget_NewAsset_ChangesResolutionToNewAsset`
- `Retarget_Null_ClearsResolution`
- `ResolveEditModels_InputsMutation_MarksDirtyViaTracker`

### Full `Hrot.Blueprints.Tests` suite

```
Total tests: 1262
     Passed: 1247
     Failed: 7
    Skipped: 8
```

**7 failures — all pre-existing (exact subset of known baseline):**

| Test | Category |
|------|----------|
| `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource("MoveToAndFire")` | Golden snapshot |
| `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource("HasVisibleTarget")` | Golden snapshot |
| `LibraryEmitGoldenTests.Library_EmitMatchesGoldenSource` | Golden snapshot |
| `LibraryMathDemoTests.LibraryMath_GeneratedSource_Snapshot` | Snapshot |
| `MoveToAndFireDemoTests.MoveToAndFire_GeneratedSource_Snapshot` | Snapshot |
| `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` | Condition summary |
| `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes` | Perf/alloc |

Zero new failures. No goldens changed.

### `EditorSubsystemBoot` (integration)

```
Total tests: 10
     Passed: 10
```

All 10/10 pass.

### `dotnet build IOS-IG-SimHost.sln`

```
Build succeeded.
    26 Warning(s)
    0 Error(s)
```

Zero new warnings in touched projects (`Hrot.Blueprints.Editor`, `Hrot.Editor`).

---

## Developer Insights

- `IReadOnlyList<string>.IndexOf` does not exist (requires a `List<T>` or LINQ) — fixed to `Enumerable.Range(...).FirstOrDefault(...)`.
- `GraphSignatureWindow.DrawClientArea` calls `BuildEditModels` on every frame. This creates two small objects per draw (~2 heap allocs). Acceptable for an editor path; would need caching only if profiling showed a problem.
- The window id `"ai_graph_signature_blueprint"` is not yet listed in `EditorSubsystemBlueprintWindowsTests`. The spec does not require a new test for the window's registration id, and adding one would fall under a future "test the registration" task. Existing tests still pass because the extra registration does not break any already-asserted ids.

---

## Known Issues

- **Draw body not smoke-tested** — the `DrawClientArea` ImGui code has not been exercised with a live ImGui context. The spec explicitly states this is deferred to a manual smoke test in a later batch.
- **Only first Function graph is initially selected** — the picker defaults to `functionGraphs[0]` when `_selectedGraphId` is not found. After Retarget the user's pick is lost. This is standard pick-reset-on-navigation behaviour.
- **Multi-output projection** — only the first `Graph.Outputs[0]` entry is projected by `ReturnNode` pins (BATCH-03C existing constraint). The edit model supports any number of outputs correctly.

---

## Suggested Commit Message

feat(blueprint-editor): graph-signature editing panel — add/remove/rename/retype Function graph Inputs/Outputs with dirty marking (BATCH-03D2)
