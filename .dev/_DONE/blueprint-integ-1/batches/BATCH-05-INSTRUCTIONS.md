# BATCH-05: Graph canvas window + BTree/HSM host binding
**Tasks:** AIE-020, AIE-021, AIE-022   **Phase:** 2   **Est:** ~15h
**Dependencies:** BATCH-01 (adapters), BATCH-02 (`AiDocumentManager`), BATCH-03 (`PerspectiveWorkspaceRegistrar` extension seam), BATCH-04 (composition root).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — working contract.
2. `.dev/blueprint-integ-1/DESIGN.md` §2 (canvas assembly contract), §5.2, §5.3.
3. `.dev/blueprint-integ-1/TASK-DETAIL.md` AIE-020, AIE-021, AIE-022 — authoritative success conditions.
4. `.dev/blueprint-integ-1/reviews/BATCH-04-REVIEW.md` — current composition state.

Use the **codebase-memory MCP** first (project `D-Work-IOS-IG-SimHost-FDP-2`); not `search_code`.

## Goal of this batch
Make BTree and HSM assets **render on a real NodeEdit canvas** inside the editor. After this batch: open a BTree/HSM asset from the global Asset Browser → its graph renders in the per-perspective canvas window; activating switches perspective; each open doc keeps its own GraphView/view-state.

## Ground truth — verified APIs
- Canvas assembly (from `NodeEditor.Demo/DemoShell`): `var view = new GraphView(graphModel, host.CommandSink, host.Validator, host.TypeSystem, host.NodeCatalog, host);` then `canvasRenderer.Render(view, findBar, commands)`.
- `CanvasRenderer.Render(GraphView view, FindBar? findBar, IEditorCommands? commands = null)` — `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasRenderer.cs`. (Find a parameterless/standard ctor; check `DemoShell` for construction.)
- `BTreeEditorHostServices` ctor (verified): `(BTreeNodeCatalog, BTreeTypeSystem, BTreeLinkValidator, BTreeCommandSink, IPickerRegistry, IClipboard, IIconProvider, IDiagnosticsSink?, IInputSource, IEditorTheme, IDebugSession? = null, IReadOnlyList<ICustomCanvasRenderer>? = null)`. `HsmEditorHostServices` ctor is analogous.
- Adapters: `AiEditorAdapterBundle` (BATCH-01, `Hrot.Editor.AiShared/Adapters/`) supplies `Pickers/Clipboard/Icons/Diagnostics/Input/Theme`. Build it from the engine `IconAtlas` — available as `windowManager.Atlas` inside `RegisterWindows`.
- Projectors: `BehaviorTreeAssetProjector` (`Hrot.BTree.Editor/Model/`), `HsmAssetProjector` (`Hrot.Hsm.Editor/Model/`) — build the editor model (`BehaviorTreeAsset`/`HsmAsset`) from compiled blob + debug metadata + layout. **Verify their exact `Project(...)` signatures and how the contributors expose the projected asset** (the `IEditableAsset` from the catalog may already be / wrap the projected asset). Mirror how existing tests construct these.
- Graph models: `HsmGraphModel` exists (`Hrot.Hsm.Editor/Model/`). **`BTreeGraphModel` did not surface in the graph — verify how the BTree host exposes `IGraphModel`** (it may be a differently-named class, or `BehaviorTreeAsset` may implement `IGraphModel`, or `BTreeEditorHostServices` may construct the view). Check `Hrot.BTree.Editor.Tests` for how a BTree `GraphView`/host is assembled and mirror it. **Do not invent** — use the existing pattern.
- Command sinks: `BTreeCommandSink(asset, graph)`, `HsmCommandSink(asset)` (verify ctors).
- `PerspectiveWorkspaceRegistrar` (BATCH-03) has `RegisterExtraWindow(wm, window)` + virtual `RegisterWindows` — use this seam to add the canvas per perspective.
- `AiDocumentManager` (BATCH-02): `AiDocument.ViewState` is the opaque slot — store the per-document canvas context (GraphView + host services + graph model) here.

## Tasks (in order)

### Task 1: AiGraphCanvasWindow (AIE-020) — file: `Hrot/Editor/Hrot.Editor.AiShared/Windows/AiGraphCanvasWindow.cs` (NEW)
Per-perspective `ManagedWindow` (id e.g. `ai_canvas_btree`/`_hsm`, `OwningPerspective` = kind, `PerspectiveBound`). On `DrawClientArea`: resolve the active document for **this** perspective from `AiDocumentManager`; if none → empty-state text; else render its cached `GraphView` via a shared `CanvasRenderer.Render(view, findBar, commands)`. On focus → `AiDocumentManager.Activate(doc)`. The window must **not** build GraphViews — a document factory (Tasks 2/3) populates `AiDocument.ViewState`; the window only renders what's there. Keep ImGui calls headless-safe (gate so unit tests can construct + drive logic without a context). Provide a seam/fake so tests verify "renders active doc's view" without a GPU.
**Tests required:** `AiGraphCanvasWindow_NoActiveDoc_ShowsEmptyState`; `AiGraphCanvasWindow_RendersActiveDocumentView` (fake document with a view/context → window invokes the render seam with that view); `AiGraphCanvasWindow_OnFocus_ActivatesDocument`. Headless-constructible.

### Task 2: BTree document factory + host binding (AIE-021) — file: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeDocumentFactory.cs` (NEW)
A factory that, given a BTree `IEditableAsset` (projected `BehaviorTreeAsset`) + the `AiEditorAdapterBundle` (+ optional debug session, null for now), builds: the BTree `IGraphModel`, `BTreeCommandSink`, `BTreeEditorHostServices` (injecting the adapters + the existing custom renderers), and a `GraphView`. Returns a canvas-context object stored in `AiDocument.ViewState`. Re-query the catalog/registry on hot reload is already handled by the contributors; ensure the factory rebuilds cleanly when an asset is (re)opened.
**Tests required (`Hrot.BTree.Editor.Tests`):** `BTreeDocumentFactory_Build_ProducesHostServices_WithAllAdapters` (catalog/type-system/validator/command-sink/pickers/clipboard/icons/input/theme non-null); `BTreeDocumentFactory_Build_GraphViewConstructs` (`new GraphView(...)` succeeds and the view exposes the projected nodes/links). Use a small in-memory `BehaviorTreeAsset` (mirror existing BTree tests) — no GPU.

### Task 3: HSM document factory + host binding (AIE-022) — file: `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmDocumentFactory.cs` (NEW)
Same as Task 2 for HSM: `HsmGraphModel` + `HsmCommandSink` + `HsmEditorHostServices` + `GraphView`; composite/parallel states project as `IContainerNodeModel` with children/regions; inject existing HSM custom renderers.
**Tests required (`Hrot.Hsm.Editor.Tests`):** `HsmDocumentFactory_Build_ProducesHostServices`; `HsmDocumentFactory_GraphView_ExposesStatesAndTransitions` (composite/parallel → container model with children/regions). No GPU.

### Wire-up (in `EditorSubsystem.RegisterWindows`)
- Build the `AiEditorAdapterBundle` from `windowManager.Atlas`.
- Register an `AiGraphCanvasWindow` into the **BTree** and **HSM** `PerspectiveWorkspaceRegistrar`s via the extension seam (Blueprint canvas is Phase 4).
- Wire `AiDocumentManager.Open` so that opening a BTree/HSM asset invokes the matching document factory to populate `ViewState`. Keep `EditorSubsystemBootTests` green.

## Success Criteria
- [ ] AIE-020, AIE-021, AIE-022 per TASK-DETAIL success conditions.
- [ ] Green (full, no crashes): `Hrot.Editor.AiShared.Tests`, `Hrot.BTree.Editor.Tests`, `Hrot.Hsm.Editor.Tests`, `Hrot.ClusterRunner.Integration.Tests` (EditorSubsystemBoot). `Hrot.Blueprints.Tests` no new failures beyond DEBT-006's 10.
- [ ] No warnings; docs; no leftover TODO/debug.
- [ ] Report at `.dev/blueprint-integ-1/reports/BATCH-05-REPORT.md`.

## Execution rules
- Tasks in sequence; full suites green before moving on. Run them yourself; fix root causes; never fake a pass.
- **Verify, don't invent:** confirm the BTree `IGraphModel`, projector signatures, command-sink/host-services ctors, and `CanvasRenderer` construction against the existing code/tests and mirror them. If a real API contradicts this batch, follow the code and note it in the report.
- Headless tests must not call ImGui without a context (use the BATCH-04 `GetCurrentContext` pattern / seams).

## Report Requirements
In `reports/BATCH-05-REPORT.md`: how the BTree `IGraphModel` is actually obtained; projector signatures used; the canvas-context object stored in `ViewState`; how the canvas window renders headless-safely in tests; what wire-up landed in `EditorSubsystem`; any API that contradicted the batch; actual test counts (all suites); suggested commit message. No comprehension questions.
