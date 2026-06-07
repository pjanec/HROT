# AI Editor Integration — Design

> **Status:** Detailed design, derived from `.dev/blueprint-integ-1/design-talk.md` (NotebookLM cumulative talk) + the host/shared specs in `docs/blueprints/` (`BTree_Editor_NodeEditor_Host_Design.md`, `HSM_Editor_NodeEditor_Host_Design.md`, `AI_Editor_Shared_Infrastructure.md`, `Blueprint_Subsystem_Editor_Detailed_Design.md`, `NodeEdit/*.md`, `NodeEditor_Extension_*.md`), and **verified against the codebase** via the codebase-memory graph (June 2026).
> **Audience:** implementation agents + human reviewer.
> **Companion docs:** [TASK-DETAIL.md](./TASK-DETAIL.md) (per-task success conditions), [TASK-TRACKER.md](./TASK-TRACKER.md) (status), [ONBOARDING.md](./ONBOARDING.md), [DEBT-TRACKER.md](./DEBT-TRACKER.md).

---

## 1. Goal

Wire the already-built **NodeEdit-backed BTree, HSM, and Blueprint visual editors** into the `Hrot.ClusterRunner` **Editor subsystem** so a user can, inside the running editor:

- Browse all BTree / HSM / Blueprint assets in one place.
- Open and **visually edit** their graphs on a real NodeEdit canvas (place/connect/configure nodes).
- Edit element properties in a details panel; manage variables; navigate via a "My Blueprint" outliner.
- **Save → regenerate deterministic C# (or `.bp.json`) → hot-reload into the live simulation.**
- **Debug** live: breakpoints, step controls, executing-node overlays, runtime state inspection, trace timeline.

The work is overwhelmingly **integration/wiring + a small number of genuinely missing pieces**, not re-implementation. See §3 for the verified gap.

### 1.1 Non-goals (v1)

- Multiple OS windows / multi-monitor side-by-side editing (see §4.6 and DEBT tracker — deferred; needs an engine-shell backend change).
- Advanced Blueprint data-flow authoring polish (variable promotion, collapse-to-function, reroute UX nuance) beyond core structural editing.
- Live multi-author collaboration; dock-layout cloud sync.

---

## 2. Conceptual model: three editors, one host abstraction

The three asset kinds are **semantically different graphs**, unified by the NodeEdit `IEditorHostServices` seam:

| Kind | Graph semantics | Pins | Details UI | Notable extras |
|------|-----------------|------|-----------|----------------|
| **BTree** | execution tree (one parent → many children) | single implicit `bt.exec` (reversed-pin trick for fan-in) | StructEdit facet forms | decorator **pills** (attachments), subtree boundaries, observer-guard badges |
| **HSM** | statechart (states + transitions) | hidden in/out pins per state; transitions are links | StructEdit facet forms | **container nodes** (composite/parallel regions), transition labels, history glyphs, globals strip |
| **Blueprint** | **data-flow** (typed pins, Unreal-like) | typed data + exec pins, casts | **node drawers** (not StructEdit) + **My Blueprint** outliner | variables, custom events, event dispatchers, callable peers |

Because each kind already implements (BTree/HSM) — or will implement (Blueprint) — the NodeEdit host contract, the canvas, selection, undo, hit-testing, and rendering pipeline are **shared and kind-agnostic**. The host services translate generic canvas operations to the kind's in-memory model.

**NodeEdit canvas assembly contract** (verified from `NodeEditor.Demo/DemoShell`):

```csharp
var view   = new GraphView(graphModel, host.CommandSink, host.Validator,
                           host.TypeSystem, host.NodeCatalog, host);
// per frame, inside the canvas window:
canvasRenderer.Render(view, findBar, commands);
```

So a canvas window needs, per active asset: an `IGraphModel` + an `IEditorHostServices` (bundling `INodeCatalog`, `ITypeSystem`, `ILinkValidator`, `IGraphCommandSink`, `IPickerRegistry`, `IClipboard`, `IIconProvider`, `IInputSource`, `IEditorTheme`, optional `IDebugSession`, custom renderers) → a `GraphView` → rendered by a shared `CanvasRenderer`.

---

## 3. Verified current state (what exists vs what's missing)

### 3.1 Already implemented (do **not** rebuild)

- **Project references:** `Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj` already references `Hrot.BTree.Editor`, `Hrot.Hsm.Editor`, and `Hrot.Editor.AiShared`. (design-talk "Step 1" is done.)
- **BTree/HSM host trio + visuals + debug** (in `Hrot.BTree.Editor` / `Hrot.Hsm.Editor`): `BTreeEditorHostServices` / `HsmEditorHostServices`, node catalogs, type systems, link validators, command sinks, graph models, asset projectors, fluent emitters, custom renderers (`SubtreeBoundaryRenderer`, `HsmTransitionLabelRenderer`, …), facet mappers + field drawers, validators, `HsmOutputLaneMaskInferrer`, `BTreeDebugSession` / `HsmDebugSession`, `BTreeAssetContributor` / `HsmAssetContributor`.
- **Shared AI editor infra** (in `Hrot.Editor.AiShared`): `AssetCatalog` + `IAssetCatalogContributor`, `DebugSessionRegistry`, `AiTracerCoordinator`, `EditorSelectionStore` (+ `SubSelectionRecords` incl. `BlueprintNodeSelection`), windows `AssetBrowserWindow` / `InspectorWindow` / `RuntimeInspectorWindow` / `TraceTimelineWindow` / `FindResultsWindow` / `BlackboardAuthoringWindow` / `DiagnosticsWindow`, `SharedAiWindowRegistrar`, refactor / reference-catalog / comparison-sanitizer / blackboard-aggregator services.
- **AiHotReloadCoordinator** is constructed and ticked in `EditorSubsystem` (`_aiCoordinator`), reflecting `Hrot.AI.Behaviors.dll` on load and after MSBuild rebuilds.
- **NodeEdit library** is complete: `GraphView`, `CanvasRenderer`, `CanvasInput`, `PickerRegistry`, `MyBlueprintPanel` (+ `IMyBlueprintModel`), node-attachment / container-node / custom-renderer extensions, `DefaultTheme`, `NullIconProvider`, and a full data-flow **`FakeBlueprint` demo** (`FakeGraphModel`/`FakeCommandSink`/`FakeTypeSystem`/`FakeNodeCatalog`/`FakeLinkValidator`/`FakeHostServices`/`FakeMyBlueprintModel`) — the reference template for the Blueprint host.
- **Engine famfamfam-silk icon atlas**: `FDP/Engine/Fdp.Presentation/ImGui/Icons/IconAtlas.cs` (+ `EmbeddedAtlasResources`), UV-addressed sprite sheet, loaded by `RaylibPresentationShell.LoadIconAtlas()`.
- **Blueprint editor building blocks** (in `Hrot.Blueprints.Editor`): node drawers (`WhenNodeDrawer`, `PlayMontageChainNodeDrawer`, `ReadEqsResultNodeDrawer`, `SpawnEqsSensorNodeDrawer`), `NodeKindRegistry` (palette), attachment providers, custom renderers (`WhenFiringPulseRenderer`), `BlueprintEditorTheme`, `GraphCommands`/`CommandHistory`/`SelectionState`, `QuickReloadService`, `FullRebuildService`, `BlueprintDebugSession`, `BlueprintComparisonSanitizer`.

### 3.2 Missing / not wired (the actual work)

| ID area | Gap |
|---|---|
| **A. Canvas window** | Nothing in the codebase hosts a NodeEdit `GraphView`/`CanvasRenderer`. Blueprint's `GraphEditorWindow` is a placeholder (`ImGui.TextDisabled`, `TODO(D-BP-04)`). |
| **B. Engine adapters** | No production `IInputSource` / `IClipboard` / `IIconProvider` / `IEditorTheme` / `IDiagnosticsSink` — only `Fake*`/demo + core `DefaultTheme`/`NullIconProvider`. |
| **C. Composition root** | `EditorSubsystem` wires none of the shared AI editor: no shared `AssetCatalog`+contributor `LoadFrom`, `SharedAiWindowRegistrar` never instantiated, `_aiEditorSelectionStore` only bridged to *entity* selection, no facet dispatch, no `DebugSessionRegistry` wiring, no documents/perspectives. |
| **D. Blueprint host trio** | Blueprint has **no** NodeEdit host (`IGraphModel`/`IGraphCommandSink`/`INodeCatalog`/`ITypeSystem`/`ILinkValidator`/`IEditorHostServices`). Must be built (template: `FakeBlueprint`). |
| **E. Blueprint parallel infra** | Blueprint uses its own `Blueprints.Editor.EditorSelectionStore` + `FileSystemAssetCatalog` (legacy `IAssetCatalog`) + `BlueprintWindowRegistrar`. To be **retired** in favour of AiShared. |
| **F. Stubs to finish** | `HsmGlobalsStrip` (TODO stub); `IEditService` (currently `NoOpEditService`). |
| **G. My Blueprint model** | NodeEdit's `MyBlueprintPanel` widget exists; a `BlueprintMyBlueprintModel : IMyBlueprintModel` projecting `BlueprintAsset` must be written (real sections where data exists, faked otherwise). |

---

## 4. Target architecture

### 4.1 One OS window, three perspectives, one active asset

The ClusterRunner shell is **single-OS-window** (Raylib `InitWindow`, rlImGui, ImGui `DockingEnable`; `ViewportsEnable` is off and unsupported by rlImGui — see §4.6). Therefore:

- **Perspectives** are emergent from `ManagedWindow.OwningPerspective` (the `WindowManager` groups windows by it to build the switcher + "Windows" menu). We create three: `"BTree"`, `"HSM"`, `"Blueprint"`.
- **The active asset's kind *is* the current perspective.** Activating an asset calls `WindowManager.SwitchPerspective(kind)` and focuses its canvas.
- **One active asset at a time**; many may be open (kept alive in `AiDocumentManager`). The active asset is rendered into the current perspective's windows. Cross-kind open assets are hidden (visibility gate) but alive; same-kind open assets are reachable by retargeting.
- **Per-perspective window instances** (distinct `###Id`s) so each perspective remembers its own dock layout. Windows are thin views over a **single shared backing layer** (one `AssetCatalog`, contributors, `DebugSessionRegistry`, `AiTracerCoordinator`, hot-reload coordinator).
- **Per-perspective selection** (each perspective's `EditorSelectionStore` tracks its active asset's selection).

### 4.2 Window inventory per perspective

| Window | BTree | HSM | Blueprint | Source |
|---|:--:|:--:|:--:|---|
| Asset Browser (Open-docs + catalog) | **global, shared** | | | AiShared `AssetBrowserWindow` (extended, §4.4) |
| Graph Canvas (retargets to active asset) | ✓ | ✓ | ✓ | **new** `AiGraphCanvasWindow` (A) |
| StructEdit Inspector | ✓ | ✓ | — | AiShared `InspectorWindow` |
| My Blueprint | — | — | ✓ | NodeEdit `MyBlueprintPanel` + new model (G) |
| Node-drawer Details | — | — | ✓ | Blueprint node drawers |
| Variables | — | — | ✓ | Blueprint `BlueprintVariablesWindow` |
| HSM Globals strip | — | ✓ | — | finish `HsmGlobalsStrip` (F) |
| Blackboard Authoring | ✓ | ✓ | (working-state) | AiShared `BlackboardAuthoringWindow` |
| Runtime Inspector / Watch / Breakpoints / Trace Timeline / Diagnostics | ✓ | ✓ | ✓ | AiShared + universal-breakpoint stack |
| Callstack / Hot Reload Log | — | — | ✓ | Blueprint debug windows |

Per-perspective windows are created by a **`PerspectiveWorkspaceRegistrar`** (one per kind) that registers that perspective's window instances with `OwningPerspective` = the kind, bound to shared services.

### 4.3 The document manager

A global **`AiDocumentManager`** owns:
- the set of **open documents** (`{ asset, kind, GraphView, viewState, isDirty }`), each keeping its own `GraphView` alive so pan/zoom/selection persist;
- the **active document**.

API: `Open(asset)` (or focus if already open), `Activate(doc)` (sets active → `SwitchPerspective(kind)` → focus canvas → point that perspective's selection store at the doc), `Close(doc)`.

The per-perspective `AiGraphCanvasWindow` renders **the active document's `GraphView`** for its kind. A canvas document calls `Activate(self)` when it gains ImGui focus.

### 4.4 The global Asset Browser (catalog + open-docs switcher)

The AiShared `AssetBrowserWindow` is made `Global` scope and gains an **"Open" section** at the top:

```
▾ OPEN (n)        ● = active (drives perspective) · * = dirty · [×] = close
   ● CoverPatrol  ⟨BTree⟩ *
     GuardFSM     ⟨HSM⟩
     OpenDoor     ⟨Blueprint⟩
──────────────────────────
🔍 filter
▾ BTrees / HSMs / Blueprints …   (double-click = Open or focus)
```

- **Open section** = cross-kind switcher → `AiDocumentManager.Activate` (switches perspective). The only way to reach a hidden cross-kind doc.
- **Catalog section** = everything from the unified `AssetCatalog` → double-click `Open`.

### 4.5 Asset sources & hot reload

- **BTree / HSM**: code-first — `BTreeAssetContributor` / `HsmAssetContributor` reflect `Hrot.AI.Behaviors.dll`. Editing → fluent emitter writes C# → file watcher → MSBuild → `AiHotReloadCoordinator` swaps blobs → contributors `LoadFrom` re-runs → projection reconciles by `VisualId`/`StableId`.
- **Blueprint**: file-first `.bp.json` — a `BlueprintAssetContributor` enumerates files; editing → `QuickReloadService` (in-memory Roslyn, ≤100 ms) or `FullRebuildService` (MSBuild).
- Contributors `LoadFrom`/refresh on `Initialize` **and** on `_aiCoordinator.OnReloadCompleted`.

### 4.6 Multiple OS windows — deferred (rationale)

Raylib is single-window; rlImGui does not implement ImGui platform-viewport callbacks; `ViewportsEnable` is off. True multi-OS-window (e.g., asset A on monitor 1, asset B on monitor 2) would require swapping the ImGui platform backend (engine-wide, affects all subsystems) or a multi-process editor (second ECS world). **Out of scope for v1**; recorded as a debt item. The single-OS-window / one-active-asset model is the chosen, bounded design.

### 4.7 Engine / library changes

The **only** change outside host code is NodeEdit's `IconHandle`/`IIconProvider` gaining a **UV-rect** (so `SilkIconProvider` maps directly onto the engine atlas). Everything else is host-side composition in `Hrot.Editor` and the subsystem editor assemblies.

---

## 5. Component design

### 5.1 Engine adapters (Phase 0)

All implement NodeEdit `Core.Interfaces` and live in a new `Hrot.Editor.AiShared/Adapters/` (or `Hrot.Editor`) area:

- **`SilkIconProvider : IIconProvider`** — wraps the engine `IconAtlas`; maps NodeEdit icon keys (`bt/sequence`, `hsm/state_parallel`, `bp/*`, status icons) to silk atlas cells, returning a handle that includes the **UV rect** (per AIE-001). Falls back to a default glyph for unknown keys.
- **`ImGuiInputSource : IInputSource`** — maps ImGuiNET state to mouse/wheel/keys/modifiers/text (`EditorKey`/`MouseButton`/`KeyModifiers` mapping tables). The canvas already consults ImGui directly for hover/active; this provides the abstracted snapshot.
- **`EngineEditorTheme : IEditorTheme`** — wraps NodeEdit `DefaultTheme` for geometry/colors, implements `GetFontForSize` against the editor's already-loaded ImGui/rlImgui fonts; palette aligned to the engine theme.
- **`ImGuiClipboard : IClipboard`** — `ImGui.GetClipboardText`/`SetClipboardText`.
- **`NLogDiagnosticsSink : IDiagnosticsSink`** — routes to the engine's NLog/message-log.
- **`AiEditorAdapterBundle`** — constructs the above once; instantiates `PickerRegistry` and calls `SetServices(icons, theme)`; exposes them to host-services factories.

### 5.2 The canvas window (Phase 2/4)

**`AiGraphCanvasWindow : ManagedWindow`** — one instance per perspective (`ai_canvas_btree` / `_hsm` / `_blueprint`). On `DrawClientArea`:
1. resolve the active document for this perspective from `AiDocumentManager`;
2. if none → empty state;
3. else render its cached `GraphView` via the shared `CanvasRenderer.Render(view, …)`;
4. an optional top tab-bar lists same-kind open docs → `Activate`.

The `GraphView` + host services for a document are built **once** when the document is opened (§5.3–5.5) and cached in the document; the window only renders.

### 5.3 BTree / HSM host binding (Phase 2)

Per opened asset, a factory builds the kind's `IGraphModel` (`BTreeGraphModel` from the projected `BehaviorTreeAsset` / `HsmGraphModel` from `HsmAsset`) and `BTreeEditorHostServices` / `HsmEditorHostServices` (ctor takes node catalog, type system, link validator, command sink, `IPickerRegistry`, `IClipboard`, `IIconProvider`, `IDiagnosticsSink`, `IInputSource`, `IEditorTheme`, optional `IDebugSession`, custom renderers — all of which exist; adapters from §5.1 supply the cross-cutting ones). The custom renderers and debug session (Phase 3) are injected here.

### 5.4 Inspector facet dispatch (Phase 2)

The per-perspective `InspectorWindow` reads its perspective's `EditorSelectionStore.ActiveSubSelection`, routes through `BTreeFacetMapper` / `HsmFacetMapper` to a StructEdit facet struct, renders it, and on commit applies back to the model (which marks dirty → regeneration). Custom field pickers (`[BehaviorHashPicker]`, `[BlackboardFieldPicker]`, `[HsmActionPicker]`, `[HsmGuardPicker]`, `[HsmStateSelector]`, `[HsmEventPicker]`, `[HsmSyncGroupPicker]`) are registered with the StructEdit service builder.

### 5.5 Blueprint host trio (Phase 4, B2)

Built new, modelled on `FakeBlueprint`, over `BlueprintAsset` (`Graphs[].Nodes[]`, typed pins, `Variables`/`CustomEvents`/`EventDispatchers`/`CallablePeers`):
- **`BlueprintGraphModel : IGraphModel`** — projects the active graph's nodes/pins/links; raises `Changed`.
- **`BlueprintTypeSystem : ITypeSystem`** — data-flow typed pins: colors/shapes per type, compatibility, implicit casts, default-value editors.
- **`BlueprintLinkValidator : ILinkValidator`** — data-flow rules (type compatibility, exec vs data, no illegal cycles).
- **`BlueprintNodeCatalog : INodeCatalog`** — wraps the existing `NodeKindRegistry` palette + dynamic callable-peer / custom-event entries.
- **`BlueprintCommandSink : IGraphCommandSink`** — add/move/connect/delete/property over `BlueprintAsset`, routed through the existing `GraphCommands`/`CommandHistory`, marking dirty; property edits go through the real `IEditService` (AIE-049).
- **`BlueprintEditorHostServices : IEditorHostServices`** — bundles the above + adapters + the existing node drawers / attachment providers / custom renderers / `IEditService`.
- **`BlueprintMyBlueprintModel : IMyBlueprintModel`** — projects `Variables`, `Graphs`, `CustomEvents`, `EventDispatchers` (real); `Functions`/`Macros` faked/empty where the model has no data yet. Registered into NodeEdit's `MyBlueprintPanel`.

### 5.6 Debug wiring (Phase 3)

Instantiate `AiTracerCoordinator` + `DebugSessionRegistry`; register `BTreeDebugSession`/`HsmDebugSession` (and reuse `BlueprintDebugSession`) factories bound to the live ECS world / kernel / time controller (the editor already owns these). Wire `NodeDebugMetadata` into sessions via the contributors for symbolication. Per-perspective `RuntimeInspectorWindow` + `TraceTimelineWindow` register kind panes/lane providers; canvas runtime overlays + breakpoint-gutter renderers read the active `IDebugSession`; breakpoint toggle commands route through the command sink.

### 5.7 Cross-asset services (Phase 5, P2)

Register the already-built `SanitizerRegistry` sanitizers (BTree/HSM/Blueprint/Blackboard/Utility), `ReferenceCatalog` contributors (BTree blackboard-var, HSM, Blueprint), and `BlackboardAggregatorService` strategies (BTree/HSM); surface `SubElementCollision` diagnostics and dangling-reference classification in the shared windows. Most of this is wiring of existing classes.

---

## 6. Phases & tasks

Task IDs are detailed in [TASK-DETAIL.md](./TASK-DETAIL.md); status in [TASK-TRACKER.md](./TASK-TRACKER.md).

### Phase 0 — Foundations: NodeEdit icon UV + engine adapters
`AIE-001` IconHandle/IIconProvider UV-rect change (NodeEdit lib) · `AIE-002` SilkIconProvider · `AIE-003` ImGuiInputSource · `AIE-004` EngineEditorTheme · `AIE-005` ImGuiClipboard · `AIE-006` NLogDiagnosticsSink · `AIE-007` AiEditorAdapterBundle.

### Phase 1 — Shared backing + document/perspective infrastructure
`AIE-010` Unified AssetCatalog + contributors + LoadFrom on init/reload · `AIE-011` BlueprintAssetContributor (retire legacy FileSystemAssetCatalog) · `AIE-012` AiDocumentManager · `AIE-013` Global AssetBrowserWindow with Open-docs section · `AIE-014` PerspectiveWorkspaceRegistrar infra + active-asset→perspective · `AIE-015` EditorSubsystem composition rewrite (retire Blueprint parallel infra).

### Phase 2 — BTree + HSM perspectives (authoring)
`AIE-020` AiGraphCanvasWindow · `AIE-021` BTree host binding · `AIE-022` HSM host binding · `AIE-023` Inspector facet dispatch · `AIE-024` Custom StructEdit pickers · `AIE-025` Blackboard Authoring per perspective · `AIE-026` Save→emit→hot-reload loop · `AIE-027` HsmGlobalsStrip implementation.

### Phase 3 — Debug (BTree + HSM)
`AIE-030` DebugSessionRegistry + AiTracerCoordinator + session factories · `AIE-031` RuntimeInspector panes · `AIE-032` TraceTimeline lane providers · `AIE-033` Canvas runtime overlays + breakpoint toggles · `AIE-034` Watch/Breakpoints/Diagnostics windows per perspective.

### Phase 4 — Blueprint perspective (full structural B2 + My Blueprint)
`AIE-040` BlueprintGraphModel · `AIE-041` BlueprintTypeSystem · `AIE-042` BlueprintLinkValidator · `AIE-043` BlueprintNodeCatalog · `AIE-044` BlueprintCommandSink · `AIE-045` BlueprintEditorHostServices · `AIE-046` Blueprint host binding into canvas · `AIE-047` BlueprintMyBlueprintModel + MyBlueprintPanel · `AIE-048` Blueprint Details + Variables windows · `AIE-049` Real IEditService.

### Phase 5 — Cross-asset services (P2)
`AIE-050` Comparison sanitizers + ComparisonExportBuilder · `AIE-051` Reference catalog contributors + RefactorService + FindResults · `AIE-052` Blackboard aggregator strategies · `AIE-053` SubElementCollision + dangling-reference classification.

---

## 7. Project dependencies & risks

- **Dependency direction:** `Hrot.Editor` (composition root) already references `Hrot.BTree.Editor`, `Hrot.Hsm.Editor`, `Hrot.Editor.AiShared`, `Hrot.Blueprints.Editor`. Adapters live in `Hrot.Editor.AiShared` (referenced by everything). The Blueprint host trio lives in `Hrot.Blueprints.Editor` (already references NodeEdit + AiShared). **No new circular references.**
- **NodeEdit lib change (AIE-001)** is the only edit to a shared external library; it is additive (UV field) and must keep `Fake*`/demo + tests compiling — verify the demo + `NodeEditor.*.Tests` build.
- **Risk — Blueprint host (Phase 4)** is the largest new surface (data-flow type system + link validation). Mitigated by the `FakeBlueprint` demo as a near-exact template and the existing drawers/palette/attachments/renderers.
- **Risk — perspective/window-instance count.** Per-perspective instances multiply windows; mitigated by thin view classes over shared services and the single-active-asset model.
- **Risk — retiring Blueprint parallel infra (AIE-015)** touches `EditorSubsystem` and Blueprint tests; do it behind the existing `EditorSubsystemBlueprintWindowsTests` / `BlueprintWindowRegistrarTests` coverage and update them.

---

## 8. Definition of Done (v1)

1. Launch ClusterRunner in editor mode → a **Blueprints/BTree/HSM** perspective is reachable; the **global Asset Browser** lists BTree, HSM, and Blueprint assets.
2. Double-click any asset → it opens on the **shared canvas** in the correct perspective; multiple assets can be open; switching the active asset (Open-docs list) switches perspective and preserves each graph's view state.
3. **BTree & HSM:** place/connect/configure nodes (pills, containers, transitions), edit properties via StructEdit Inspector, save → C# regenerated → hot-reloaded; validation surfaces in Diagnostics.
4. **Blueprint:** open on canvas, add/connect/delete typed nodes (B2), edit via node drawers, **My Blueprint** panel present and populated (real where data exists), save → Quick Reload.
5. **Debug:** set a breakpoint, run the sim, see the executing node/state highlighted, inspect runtime state, view the trace timeline — for all three kinds wired.
6. Full automated xUnit suites for the new/changed assemblies pass; the editor boots headless in `EditorSubsystemBootTests`.
7. Every "final idea" in the design-talk is covered or explicitly deferred in the DEBT tracker (see §9 mapping).

---

## 9. Design-talk coverage map

| design-talk topic | Covered by |
|---|---|
| Project refs (Step 1) | §3.1 (already done) |
| Asset catalog wiring (Step 2) | AIE-010, AIE-011 |
| Host services init (Step 3) | AIE-007, AIE-021/022/045 |
| Window registration (Step 4) | AIE-013/014/020, Phase 2/4 |
| Inspector facet dispatch (Step 5) | AIE-023, AIE-024 |
| Debug session registry (Step 6) | AIE-030–034 |
| Reference catalog (Step 7) | AIE-051, AIE-053 |
| Blackboard aggregation (Step 8) | AIE-025, AIE-052 |
| Custom canvas renderers (Step 9) | AIE-021/022 (inject existing renderers), AIE-033 |
| Fluent emitters & save pipeline (Step 10) | AIE-026 |
| Comparison sanitizers (Step 11) | AIE-050 |
| NodeEdit host services & graph adapters | §3.1 (BTree/HSM exist), AIE-040–045 (Blueprint) |
| Node attachments / container nodes / custom renderers extensions | NodeEdit (exists); injected via host services |
| Validation pipelines | AIE-023 (surface), existing validators |
| Debug sessions & trace timelines | AIE-030–034 |
| HsmGlobalsStrip stub | AIE-027 |
| IEditService stub | AIE-049 |
| My Blueprint panel | AIE-047 |
| Single OS window / multi-window | §4.6 + DEBT item |
