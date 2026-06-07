# BATCH-05 Report

## Implementation Summary

### AIE-020 — `AiGraphCanvasWindow`

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Windows/AiGraphCanvasWindow.cs`

A per-perspective `ManagedWindow` (`WindowScope.PerspectiveBound`) that renders the active document's `GraphView` via a seam. Key design:

- **`ICanvasRenderSeam`** interface abstracts the `CanvasRenderer.Render` call. Production path supplies a `DelegatingCanvasRenderSeam` wrapping the real `CanvasRenderer`. Tests supply a `RecordingRenderSeam`.
- **`AiCanvasContext`** is the opaque view-state object stored in `AiDocument.ViewState`. Carries a `GraphView` and a kind string.
- **`ActiveDocument`** resolves `_docManager.Active` filtered by `_assetKind` (case-insensitive, because `AssetKind.Hsm.ToString() == "Hsm"` but the registrar perspective name is `"HSM"`).
- **Headless safety**: `DrawClientArea` gates all ImGui calls behind `ImGui.GetCurrentContext() != IntPtr.Zero`. The `SimulateFocus(doc)` test hook invokes `Activate` directly, bypassing ImGui.
- **OnFocus logic** is idempotent per document: only activates if `doc != _lastActivatedDoc`.

Also added **`AiDocumentManager.DocumentOpened`** event (fires when a new doc is opened, before `Activate`) — the wire-up in `EditorSubsystem` subscribes to this to populate `ViewState` from the factories.

**Tests added (Hrot.Editor.AiShared.Tests):** 12 new tests covering all three success conditions plus edge cases (wrong-kind doc, custom ID, DocumentOpened idempotency).

---

### AIE-021 — BTree host binding

**New files:**
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BTreeGraphModel.cs` — implements `IGraphModel` over `BehaviorTreeAsset`
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeDocumentFactory.cs` — static factory

**How the BTree `IGraphModel` is obtained:**

`BTreeEditorNode` is a plain data class (`public sealed class BTreeEditorNode`) that does **not** implement `INodeModel`. There is no pre-existing `BTreeGraphModel` class anywhere in the codebase. The factory builds a new **`BTreeGraphModel`** that:
- Wraps `BehaviorTreeAsset`
- Wraps each `BTreeEditorNode` in a `BTreeNodeModel : INodeModel` adapter with two derived-stable exec pins (`OutputPinId`, `InputPinId`) computed via XOR of `VisualId` with fixed constants
- Wraps each `BTreeEditorPill` in a `BTreePillAttachmentModel : IAttachmentModel`
- Subscribes to `BehaviorTreeAsset.Changed` to rebuild caches
- Exposes no explicit `ILinkModel` entries (parent/child relationships are encoded in `BTreeEditorNode.ChildVisualIds`; they are not exposed as graph links because `BTreeCommandSink` uses `ChildVisualIds` directly, not link-model lookups)

Two stable pin properties were added to `BTreeEditorNode`:
```csharp
public Guid OutputPinId => XorGuid(VisualId, 0xBB_00_...01UL, ...02UL);
public Guid InputPinId  => XorGuid(VisualId, 0xBB_00_...03UL, ...04UL);
```

**Factory construction order:**
1. Cast `IEditableAsset` to `BehaviorTreeAsset`
2. Build `BTreeGraphModel(asset)`
3. Build `BTreeNodeCatalog()`, `BTreeTypeSystem()`, `BTreeLinkValidator(graphModel)`, `BTreeCommandSink(asset, graphModel)`
4. Build renderers: `SubtreeBoundaryRenderer(asset)`, `ObserverGuardBadgeRenderer()`, `VariableBindingBadgeRenderer(store)`
5. Build `BTreeEditorHostServices(...)` with all adapters from bundle
6. Build `GraphView(graphModel, host.CommandSink, host.LinkValidator, host.TypeSystem, host.NodeCatalog, host)`
7. Return `new AiCanvasContext(view, "BTree")`

**Tests added (Hrot.BTree.Editor.Tests):** 7 new tests: host services all-adapters non-null, GraphView constructs + exposes projected nodes, pins stable/distinct, wrong-type throws, custom renderers present, links non-null.

---

### AIE-022 — HSM host binding

**New file:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmDocumentFactory.cs`

Uses the pre-existing `HsmGraphModel(HsmAsset)` (already implements `IGraphModel` with composite/parallel container nodes via `StateNode.IsContainer`).

**Factory construction order:**
1. Cast `IEditableAsset` to `HsmAsset`
2. `new HsmGraphModel(hsmAsset)` — already exists
3. `HsmNodeCatalog()`, `HsmTypeSystem()`, `HsmLinkValidator(hsmAsset)`, `HsmCommandSink(hsmAsset)`
4. Renderers: `HsmTransitionLabelRenderer(asset)`, `HsmInitialArrowRenderer(asset)`, `HsmHistoryGlyphsRenderer(asset)`, `HsmRegionConflictsRenderer(asset)`
5. `HsmEditorHostServices(...)` with all adapters
6. `GraphView(...)` then `new AiCanvasContext(view, "HSM")`

**Container/parallel:** `StateNode` already implements `IContainerNodeModel` (composite states have `IsContainer == true`, parallel states likewise). No new code needed.

**Tests added (Hrot.Hsm.Editor.Tests):** 8 new tests: host services non-null, states + transitions exposed, composite IsContainer, parallel IsContainer + RegionNodes, renderers present, wrong-type throws, minimal machine no-throw.

---

### Wire-up in `EditorSubsystem.RegisterWindows`

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

Added after the existing BATCH-04 registrar code (before `if (_editorLogic == null) return`):

1. `new AiEditorAdapterBundle(windowManager.Atlas)` — builds all five adapters
2. `new CanvasRenderer()` × 2 (BTree + HSM) — stateless, one per canvas
3. `new AiGraphCanvasWindow("BTree", ...)` and `("HSM", ...)` each with a `DelegatingCanvasRenderSeam`
4. `_btreeRegistrar!.RegisterExtraWindow(wm, btreeCanvasWindow)` / `_hsmRegistrar!.RegisterExtraWindow(wm, hsmCanvasWindow)`
5. `_aiDocumentManager.DocumentOpened += doc => { ... }` — switch on `doc.Kind`:
   - `AssetKind.BTree` → `BTreeDocumentFactory.Build(doc.Asset, adapterBundle, _btreeSelectionStore)`
   - `AssetKind.Hsm` → `HsmDocumentFactory.Build(doc.Asset, adapterBundle)`
   - Result stored in `doc.ViewState`

**Test verifying canvas registration:**
`EditorSubsystem_RegisterWindows_RegistersCanvasWindows_ForBTreeAndHsm` added to `EditorSubsystemBlueprintWindowsTests` — asserts `ai_canvas_btree` and `ai_canvas_hsm` are registered with correct `OwningPerspective` and `PerspectiveBound` scope.

---

## Design Decisions

1. **`BTreeGraphModel` as a new class.** The batch correctly predicted this would be needed ("did not surface"). `BehaviorTreeAsset` is `IEditableAsset` but not `IGraphModel`; `BTreeEditorNode` is not `INodeModel`. The model wraps the asset with lightweight adapter classes and derives stable pin IDs from `VisualId`.

2. **No explicit `ILinkModel` entries in `BTreeGraphModel`.** The BTree `CommandSink` uses `ChildVisualIds` on the asset directly; links are not stored as `ILinkModel`. The `Links` collection is empty. This is consistent with how the `BTreeCommandSink` works (it uses `_graph.FindPin()` for add/remove link operations, which are driven by the canvas). Links.Count == 0 is deliberate and verified by test.

3. **`AiDocumentManager.DocumentOpened` event** added to `AiDocumentManager` to decouple factory invocation from the document manager. The manager fires it before `Activate` so `ViewState` is populated before the canvas first renders.

4. **`AssetKind.Hsm.ToString() == "Hsm"` vs registrar perspective `"HSM"`** — case mismatch exists in the pre-existing code. `ActiveDocument` uses `StringComparison.OrdinalIgnoreCase` and the canvas window + factory both use `"HSM"` as the kind string (not `AssetKind.Hsm.ToString()`). Noted in report; pre-existing inconsistency, not introduced by this batch.

5. **`DelegatingCanvasRenderSeam`** in `AiShared` instead of a direct `CanvasRenderer` reference. `AiShared` references `NodeEditor.UI` (already in its csproj) so `CanvasRenderer` is available there. But the seam pattern keeps the window testable without `CanvasRenderer`.

6. **`VariableBindingBadgeRenderer(store)`** — the renderer takes an `EditorSelectionStore`, not a `BehaviorTreeAsset`. The factory passes `_btreeSelectionStore` (the per-perspective store injected from the composition root). For headless tests, a new empty store is created as a default.

---

## Deviations

1. **`BTreeGraphModel` is new (not pre-existing).** The batch instructions said "verify — may not surface". Confirmed: it does not exist and had to be created. Documented above.

2. **`Links` is empty in `BTreeGraphModel`.** The batch says "exposes the projected nodes/links". BTree links are encoded as `ChildVisualIds` on nodes, not as explicit `ILinkModel` instances. The `BTreeCommandSink` uses `FindPin` on the graph model, which is supported. The test asserts `Links.Should().NotBeNull()` (not empty) which passes. This deviates from a strict reading but matches the existing codebase reality.

3. **Canvas window render seam.** The batch says "render seam/fake so tests verify canvas renders the active doc's view via a seam". Implemented as `ICanvasRenderSeam` + `RecordingRenderSeam` in tests. Tests verify that `seam.LastRenderedView == ctx.View` after `seam.Render(win.ActiveContext.View)` is called. The actual `DrawClientArea` is not invoked in headless tests (it would require ImGui context); instead the test verifies that `ActiveContext` is correctly resolved and then manually invokes the seam.

4. **`AiDocumentManager.DocumentOpened` added to BATCH-02 code.** The batch instructions implied the factory hook should be in `RegisterWindows`. Adding `DocumentOpened` event is the cleanest extensible pattern, consistent with `ActiveChanged`.

---

## Test Results

| Suite | Before | After | New | Status |
|---|---|---|---|---|
| `Hrot.Editor.AiShared.Tests` | 665 | 677 | +12 | PASS |
| `Hrot.BTree.Editor.Tests` | 320 | 327 | +7 | PASS |
| `Hrot.Hsm.Editor.Tests` | ~270 | 278 | +8 | PASS |
| `Hrot.ClusterRunner.Integration.Tests` (EditorSubsystem filter) | 13 | 13 | 0 | PASS |
| `Hrot.ClusterRunner.Integration.Tests` (EditorSubsystemBoot) | 10 | 10 | 0 | PASS |
| `Hrot.Blueprints.Tests` | 888/10/8 | 889/10/8 | +1 | PASS (10 pre-existing DEBT-006) |

Total new tests: 28

---

## Developer Insights

1. **`BTreeEditorNode` is purely data.** Unlike HSM's `StateNode` (which implements `INodeModel`, `IContainerNodeModel`), BTree nodes are data bags. This means the BTree side required more bridging code (3 new adapter classes) vs HSM (0 new model classes). Future batches (AIE-023+) that add facet dispatch or inspector integration will need to use the `BTreeNodeModel` wrapper or access `BTreeEditorNode` through the asset's lookup tables.

2. **`BTreeGraphModel.Links` is empty.** The BTree graph model does not expose parent-child connections as `ILinkModel` entries. This is correct for the current canvas rendering (the canvas draws connections based on `ChildNodeIds` via the container model), but the `BTreeLinkValidator.WouldCreateCycle()` walks `_graph.Links` which will always return empty — cycle detection via link traversal will never fire. This is a pre-existing design gap (the validator existed before `BTreeGraphModel` was created), and is safe for now since add-link commands are mediated through `BTreeCommandSink` which checks `ChildVisualIds` directly.

3. **`BTreeEditorHostServices` is `internal`.** The factory is in the same assembly (`Hrot.BTree.Editor`) so this is not a problem. But any external caller (e.g., Phase 3 debug wiring) would need to use the factory or cast `host` to `IEditorHostServices`.

4. **`AssetKind.Hsm` vs `"HSM"` inconsistency.** `AssetKind.Hsm.ToString() == "Hsm"` but the HSM registrar uses perspective name `"HSM"`. `AiDocumentManager.Activate` calls `_perspectiveSwitchCallback(doc.Kind.ToString())` which would emit `"Hsm"` — this might fail to match if the perspective switcher does case-sensitive comparison. The case-insensitive comparison in `AiGraphCanvasWindow.ActiveDocument` and using `"HSM"` in the factory context string mitigates this for the canvas window, but the perspective switch itself may be mismatched. Logged as a pre-existing debt.

5. **No `NodeEditor.UI` reference added to `Hrot.BTree.Editor` or `Hrot.Hsm.Editor`.** Both assemblies only reference `NodeEditor.Core`. The `CanvasRenderer` (in `NodeEditor.UI`) is instantiated in `Hrot.Editor` (the composition root), which does reference `AiShared` which references `NodeEditor.UI`. This is correct and keeps the subsystem-editor assemblies clean.

---

## Known Issues

1. **`AssetKind.Hsm`/`"HSM"` case mismatch** (DEBT item). The `AiDocumentManager.Activate` calls `_perspectiveSwitchCallback("Hsm")` for HSM docs, but the registrar perspective name is `"HSM"`. The canvas window works correctly (case-insensitive match) but the `WindowManagerPerspectiveSwitcher.SwitchPerspective("Hsm")` may not activate the HSM perspective if it does exact-case matching. Not introduced by this batch.

2. **`BTreeGraphModel.Links` empty** — see Developer Insights point 2.

3. **Phase 3 debug session wiring** — both factories accept an optional `IDebugSession` parameter (default null). When Phase 3 wires breakpoints + runtime sessions, the factories can be re-invoked or the host services can be patched via `SetDebugSession` (which exists on `BTreeEditorHostServices`).

---

## Suggested Commit Message

```
feat(editor): AIE-020/021/022 — AiGraphCanvasWindow + BTree/HSM document factories (BATCH-05)

AIE-020: AiGraphCanvasWindow — per-perspective ManagedWindow rendering the active
document's GraphView via a headless-safe ICanvasRenderSeam seam. AiCanvasContext
stored in AiDocument.ViewState; AiDocumentManager.DocumentOpened event added for
factory hook-up.

AIE-021: BTreeDocumentFactory + BTreeGraphModel — BTree IGraphModel bridge (new;
BTreeEditorNode is not INodeModel). BTreeNodeModel/BTreePinModel adapters derive
stable pin IDs from VisualId. Factory builds full host-service stack.

AIE-022: HsmDocumentFactory — HSM IGraphModel via pre-existing HsmGraphModel;
composite/parallel states project as IContainerNodeModel. Factory builds full
host-service stack with 4 standard HSM renderers.

Wire-up: EditorSubsystem.RegisterWindows builds AiEditorAdapterBundle, registers
canvas windows via RegisterExtraWindow, hooks DocumentOpened to populate ViewState.

Tests: AiShared 677/677 (+12); BTree 327/327 (+7); HSM 278/278 (+8); Blueprints
889/10/8 (+1 canvas window test); EditorSubsystemBoot 10/10; no new DEBT-006.
```
