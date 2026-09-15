# BATCH-03D1 Report — FunctionCall Node Details Drawer

## Implementation Summary

### Task 1 — `NodeDrawers/FunctionCallNodeDrawer.cs` (new file)

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/FunctionCallNodeDrawer.cs`

- **`FunctionCallNodeDrawer`** (lines 14–26): `public sealed class`, implements `IBlueprintNodeDrawer`.
  - Ctor takes `IEditService editService` (validated non-null).
  - `Handles(node)` → `node is FunctionCallNode` (line 23).
  - `CreateSession(node, parentAsset)` → `new FunctionCallNodeSession(...)` (line 25).

- **`FunctionCallNodeSession`** (lines 28–211): `internal sealed class`, implements `INodeEditSession`.
  - Fields: `_node` (FunctionCallNode), `_parent` (BlueprintAsset), `_editService` (IEditService).
  - `IsDirty` (bool, auto-prop, private set).
  - **Mutation helpers** (lines 68–87):
    - `ApplyFunctionGraphSelection(Guid)` — sets `TargetGraphId`, clears `TargetTypeId`/`MethodName`, calls `MarkChanged()`.
    - `ApplyClrTarget(string, string, bool)` — sets `TargetTypeId`/`MethodName`/`IsPure`, clears `TargetGraphId`, calls `MarkChanged()`.
    - `MarkChanged()` — `IsDirty = true; _editService?.MarkDirty(_parent);`
  - **Test hooks** (lines 52–65):
    - `internal void SelectFunctionGraphForTest(Guid graphId)` → delegates to `ApplyFunctionGraphSelection`.
    - `internal void SetClrTargetForTest(string typeId, string methodName, bool isPure)` → delegates to `ApplyClrTarget`.
  - **`Draw()`** (lines 89–155): All ImGui calls live here. IsPure checkbox; mode combo ("CLR Method" vs "In-blueprint Function"); delegates to `DrawFunctionGraphPicker()` or `DrawClrMethodForm()`.
  - **`DrawFunctionGraphPicker()`** (lines 157–183): Enumerates `_parent.Graphs.Where(g => g.Kind == GraphKind.Function)`. Shows "(no function graphs in this blueprint)" when empty. On selection calls `ApplyFunctionGraphSelection(chosen.Id)`.
  - **`DrawClrMethodForm()`** (lines 185–208): `InputText` for `TargetTypeId` and `MethodName`. Each fires `MarkChanged()` on change. Shows deferred-browser note.
  - `ResetDirty()` → `IsDirty = false` (line 210). `Dispose()` → no-op (line 211).

### Task 2 — Registration in `BlueprintEditorBootstrap`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorBootstrap.cs` — line 42 added:

```csharp
// BATCH-03D1: Register FunctionCallNode drawer
registry.Register(typeof(FunctionCallNode), new FunctionCallNodeDrawer(editService));
```

Placed after the three existing WHEN-M11-T1 registrations (SpawnEqsSensor, ReadEqsResult, WhenNode), before the ANC-P5-08a conditional block.

## Design Decisions

1. **Mutation helpers extract the shared logic** used by both Draw() and the test hooks. This avoids code duplication and keeps ImGui calls strictly inside Draw(). The test hooks call the exact same code paths exercised by the UI.

2. **Mode inferred from TargetGraphId, not stored separately.** Current mode is `!string.IsNullOrEmpty(_node.TargetGraphId)`. This matches the spec and avoids introducing a separate mode field that could drift out of sync with the node data.

3. **IsPure is mode-independent.** The spec says IsPure is a property of the call itself; it belongs outside the mode-selector in Draw(). Toggling IsPure does not clear TargetGraphId or CLR fields.

4. **`_editService?.MarkDirty(...)` null-guard preserved.** The WhenNodeDrawer pattern uses this; followed for consistency. The ctor validates non-null anyway, but the guard is defensive inside `MarkChanged()`.

5. **CLR method browser deferred.** As noted in the spec, `StaticTypeRegistry` lists primitives only — no catalog for arbitrary CLR types/methods exists. The text fields are sufficient for now. A one-line `ImGui.TextDisabled` in `DrawClrMethodForm()` flags this explicitly.

## Deviations

None. Implementation follows the spec exactly.

## Test Results

**New tests** — `Hrot.Blueprints.Tests.Editor.FunctionCallNodeDrawerTests` (19 tests):

```
Passed!  - Failed: 0, Passed: 19, Skipped: 0, Total: 19, Duration: 33 ms
```

Test names and coverage:
- `FC-01` Handles: `Drawer_Handles_FunctionCallNode_True`, `Drawer_Handles_OtherNodeTypes_False`
- `FC-02` CreateSession: `Drawer_CreateSession_ReturnsNonNull`, `Drawer_CreateSession_InitiallyNotDirty`
- `FC-03` SelectFunctionGraph hook: `Session_SelectFunctionGraphForTest_SetsTargetGraphId`, `Session_SelectFunctionGraphForTest_ClearsCLRFields`, `Session_SelectFunctionGraphForTest_MarksDirty`
- `FC-04` SetClrTarget hook: `Session_SetClrTargetForTest_SetsCLRFields`, `Session_SetClrTargetForTest_ClearsTargetGraphId`, `Session_SetClrTargetForTest_MarksDirty`
- `FC-05` Mutual exclusivity: `Session_GraphPickThenClr_OnlyCLRFieldsSet`, `Session_ClrThenGraphPick_OnlyGraphIdSet`
- `FC-06` MarkDirty on IEditService: `Session_GraphPick_CallsMarkDirtyOnEditService`, `Session_ClrSet_CallsMarkDirtyOnEditService`, `Session_TwoEdits_CallsMarkDirtyTwice`
- `FC-07` ResetDirty: `Session_ResetDirty_ClearsDirtyFlag`
- `FC-08` Registry: `DrawerRegistry_Contains_FunctionCallNodeDrawer`, `DrawerRegistry_TryGet_FunctionCallNode_Succeeds`
- `FC-09` Pump: `DetailsWindow_ResolveSession_ReturnsFunctionCallSession_WithCorrectDrawerKind`

**Existing drawer/wiring tests** (26 tests — all green):
```
Passed!  - Failed: 0, Passed: 26, Skipped: 0, Total: 26, Duration: 74 ms
```
Covers: SpawnEqsSensorNodeDrawerTests, WhenNodeEditorWiringTests, DrawerRegistryTests, BlueprintDetailsWindowTests.

**Full Hrot.Blueprints.Tests suite:**
```
Failed: 7, Passed: 1221, Skipped: 8, Total: 1236, Duration: 31 s
```

The 7 failures are exactly the pre-existing baseline (zero new failures):

| Test | Classification |
|------|----------------|
| `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource(MoveToAndFire)` | Pre-existing golden mismatch |
| `AiPrimitiveEmitGoldenTests.AiPrimitive_EmitMatchesGoldenSource(HasVisibleTarget)` | Pre-existing golden mismatch |
| `LibraryEmitGoldenTests.Library_EmitMatchesGoldenSource` | Pre-existing golden mismatch |
| `LibraryMathDemoTests.LibraryMath_GeneratedSource_Snapshot` | Pre-existing snapshot mismatch |
| `MoveToAndFireDemoTests.MoveToAndFire_GeneratedSource_Snapshot` | Pre-existing snapshot mismatch |
| `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` | Pre-existing |
| `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes` | Pre-existing |

**EditorSubsystemBoot integration tests:**
```
Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10, Duration: 2 s
```

## Developer Insights

- `BlueprintDetailsWindowTests.SC4` tests that `FunctionCallNode` is **not** resolved when using the local test-only `MakeRegistry()` (which does not call the bootstrap). This test is unaffected by registration in the bootstrap because it constructs its own registry. The test correctly continues to assert null session for an unregistered node type within that custom registry.

- The `Draw()` method uses `ImGui.InputText` with a `ref string` pattern. C# InputText overloads using `ref string` require the local variable to be captured first — this is handled correctly in `DrawClrMethodForm()`.

- `ApplyFunctionGraphSelection` and `ApplyClrTarget` are private helpers rather than being inlined in the test hooks, ensuring the production Draw() path calls the exact same code paths as the headless test hooks.

## Known Issues

- **`Draw()` body needs a manual visual smoke test.** The ImGui rendering is not exercised by the headless tests. A quick smoke (open a blueprint with a FunctionCallNode, check Details panel shows IsPure checkbox + mode combo) is needed before ship. Noted as deferred visual verification.

- **CLR method browser not implemented.** The `TargetTypeId`/`MethodName` text fields are plain text inputs. A full type/method browser (catalog + search) is deferred — no `StaticTypeRegistry` covers arbitrary CLR types. The `DrawClrMethodForm()` shows a `TextDisabled` note flagging this.

- **Graph-signature editing panel** (editing inputs/outputs of the selected in-blueprint function graph) is out of scope for this batch (separate batch per spec).

## Suggested Commit Message

feat(blueprint-editor): add FunctionCallNode details drawer with CLR/in-blueprint-graph mode picker (BATCH-03D1)
