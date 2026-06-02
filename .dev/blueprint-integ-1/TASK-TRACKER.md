# AI Editor Integration — Task Tracker

**Reference:** see [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions and success conditions, and [DESIGN.md](./DESIGN.md) for architecture.

Status: `[ ]` not done · `[x]` done. Keep this in sync as tasks complete.

---

## Phase 0 — Foundations: NodeEdit icon UV + engine adapters
**Goal:** the host-provided NodeEdit services (input, theme, icons, clipboard, diagnostics, pickers) exist as production adapters; icons addressable by UV rect.

- [x] **AIE-001** NodeEdit `IconHandle`/`IIconProvider` UV-rect support [details](./TASK-DETAIL.md#aie-001--nodeedit-iconhandleiiconprovider-uv-rect-support) — BATCH-01 ✅
- [x] **AIE-002** `SilkIconProvider` [details](./TASK-DETAIL.md#aie-002--silkiconprovider--iiconprovider) — BATCH-01 ✅
- [x] **AIE-003** `ImGuiInputSource` [details](./TASK-DETAIL.md#aie-003--imguiinputsource--iinputsource) — BATCH-01 ✅
- [x] **AIE-004** `EngineEditorTheme` [details](./TASK-DETAIL.md#aie-004--engineeditortheme--ieditortheme) — BATCH-01 ✅
- [x] **AIE-005** `ImGuiClipboard` [details](./TASK-DETAIL.md#aie-005--imguiclipboard--iclipboard) — BATCH-01 ✅
- [x] **AIE-006** `NLogDiagnosticsSink` [details](./TASK-DETAIL.md#aie-006--nlogdiagnosticssink--idiagnosticssink) — BATCH-01 ✅
- [x] **AIE-007** `AiEditorAdapterBundle` [details](./TASK-DETAIL.md#aie-007--aieditoradapterbundle) — BATCH-01 ✅

## Phase 1 — Shared backing + document/perspective infrastructure
**Goal:** one shared catalog/debug backing; documents + three perspectives; global Asset Browser; Blueprint parallel infra retired.

- [x] **AIE-010** Unified `AssetCatalog` + contributors + `LoadFrom` [details](./TASK-DETAIL.md#aie-010--unified-assetcatalog--contributors--loadfrom) — BATCH-02 ✅ (builder; composition-root wiring in AIE-015)
- [x] **AIE-011** `BlueprintAssetContributor` [details](./TASK-DETAIL.md#aie-011--blueprintassetcontributor-retire-legacy-filesystemassetcatalog) — BATCH-02 ✅ (legacy `FileSystemAssetCatalog` removal deferred to AIE-015)
- [x] **AIE-012** `AiDocumentManager` [details](./TASK-DETAIL.md#aie-012--aidocumentmanager) — BATCH-02 ✅
- [x] **AIE-013** Global `AssetBrowserWindow` + Open-docs section [details](./TASK-DETAIL.md#aie-013--global-assetbrowserwindow-with-open-docs-section) — BATCH-03 ✅
- [x] **AIE-014** `PerspectiveWorkspaceRegistrar` infra + active-asset→perspective [details](./TASK-DETAIL.md#aie-014--perspectiveworkspaceregistrar-infra--active-assetperspective) — BATCH-03 ✅
- [x] **AIE-015** `EditorSubsystem` composition rewrite [details](./TASK-DETAIL.md#aie-015--editorsubsystem-composition-rewrite-retire-blueprint-parallel-infra) — BATCH-04 ✅ (+ Corrective Task 0: adapter AV fix)

## Phase 2 — BTree + HSM perspectives (authoring)
**Goal:** open, edit, inspect, and save BTree/HSM graphs end-to-end on the shared canvas.

- [x] **AIE-020** `AiGraphCanvasWindow` [details](./TASK-DETAIL.md#aie-020--aigraphcanvaswindow) — BATCH-05 ✅
- [x] **AIE-021** BTree host binding [details](./TASK-DETAIL.md#aie-021--btree-host-binding) — BATCH-05 + BATCH-06 (links) ✅
- [x] **AIE-022** HSM host binding [details](./TASK-DETAIL.md#aie-022--hsm-host-binding) — BATCH-05 ✅
- [x] **AIE-023** Inspector facet dispatch [details](./TASK-DETAIL.md#aie-023--inspector-facet-dispatch) — BATCH-06 ✅
- [x] **AIE-024** Custom StructEdit field pickers [details](./TASK-DETAIL.md#aie-024--custom-structedit-field-pickers) — BATCH-06 ✅ (+ DEBT-003 PickerRegistry.Get)
- [x] **AIE-025** Blackboard Authoring per perspective [details](./TASK-DETAIL.md#aie-025--blackboard-authoring-per-perspective) — BATCH-07 ✅
- [x] **AIE-026** Save → emit → hot-reload loop [details](./TASK-DETAIL.md#aie-026--save--emit--hot-reload-loop) — BATCH-07 ✅ (Phase 2 complete)
- [x] **AIE-027** `HsmGlobalsStrip` implementation [details](./TASK-DETAIL.md#aie-027--hsmglobalsstrip-implementation) — BATCH-06 ✅

## Phase 3 — Debug (BTree + HSM)
**Goal:** breakpoints, step controls, runtime overlays, runtime inspector, trace timeline wired to the live sim.

- [x] **AIE-030** DebugSessionRegistry + AiTracerCoordinator + session factories [details](./TASK-DETAIL.md#aie-030--debugsessionregistry--aitracercoordinator--session-factories) — BATCH-08 ✅
- [x] **AIE-031** RuntimeInspector panes per perspective [details](./TASK-DETAIL.md#aie-031--runtimeinspector-panes-per-perspective) — BATCH-08 ✅
- [x] **AIE-032** TraceTimeline lane providers per perspective [details](./TASK-DETAIL.md#aie-032--tracetimeline-lane-providers-per-perspective) — BATCH-08 ✅
- [x] **AIE-033** Canvas runtime overlays + breakpoint toggles [details](./TASK-DETAIL.md#aie-033--canvas-runtime-overlays--breakpoint-toggles) — BATCH-09 ✅
- [x] **AIE-034** Watch / Breakpoints / Diagnostics windows per perspective [details](./TASK-DETAIL.md#aie-034--watch--breakpoints--diagnostics-windows-per-perspective) — BATCH-09 ✅ (Phase 3 complete)

## Phase 4 — Blueprint perspective (full structural B2 + My Blueprint)
**Goal:** Blueprint data-flow graphs open/edit/connect/delete on the shared canvas, with My Blueprint outliner + node-drawer details.

- [x] **AIE-040** `BlueprintGraphModel` [details](./TASK-DETAIL.md#aie-040--blueprintgraphmodel--igraphmodel)
- [x] **AIE-041** `BlueprintTypeSystem` [details](./TASK-DETAIL.md#aie-041--blueprinttypesystem--itypesystem)
- [x] **AIE-042** `BlueprintLinkValidator` [details](./TASK-DETAIL.md#aie-042--blueprintlinkvalidator--ilinkvalidator)
- [x] **AIE-043** `BlueprintNodeCatalog` [details](./TASK-DETAIL.md#aie-043--blueprintnodecatalog--inodecatalog)
- [ ] **AIE-044** `BlueprintCommandSink` [details](./TASK-DETAIL.md#aie-044--blueprintcommandsink--igraphcommandsink)
- [ ] **AIE-045** `BlueprintEditorHostServices` [details](./TASK-DETAIL.md#aie-045--blueprinteditorhostservices--ieditorhostservices)
- [ ] **AIE-046** Blueprint host binding into canvas [details](./TASK-DETAIL.md#aie-046--blueprint-host-binding-into-aigraphcanvaswindow)
- [ ] **AIE-047** `BlueprintMyBlueprintModel` + `MyBlueprintPanel` [details](./TASK-DETAIL.md#aie-047--blueprintmyblueprintmodel--myblueprintpanel)
- [ ] **AIE-048** Blueprint Details + Variables windows [details](./TASK-DETAIL.md#aie-048--blueprint-details--variables-windows)
- [ ] **AIE-049** Real `IEditService` [details](./TASK-DETAIL.md#aie-049--real-ieditservice)

## Phase 5 — Cross-asset services (P2)
**Goal:** comparison/diff, find-references/refactor, blackboard aggregation, collision/dangling diagnostics wired (mostly existing services).

- [ ] **AIE-050** Comparison sanitizers + ComparisonExportBuilder [details](./TASK-DETAIL.md#aie-050--comparison-sanitizers--comparisonexportbuilder)
- [ ] **AIE-051** Reference catalog contributors + RefactorService + FindResults [details](./TASK-DETAIL.md#aie-051--reference-catalog-contributors--refactorservice--findresults)
- [ ] **AIE-052** Blackboard aggregator strategies [details](./TASK-DETAIL.md#aie-052--blackboard-aggregator-strategies)
- [ ] **AIE-053** SubElementCollision + dangling-reference classification [details](./TASK-DETAIL.md#aie-053--subelementcollision--dangling-reference-classification)

---

### Milestone gates
- **M-Foundations** (Phase 0–1): editor boots; three perspectives appear; global Asset Browser lists all three kinds; no Blueprint parallel infra remains.
- **M-Authoring** (Phase 2): BTree + HSM open/edit/inspect/save → hot-reload works end-to-end.
- **M-Debug** (Phase 3): live breakpoints + overlays + runtime/trace for BTree + HSM.
- **M-Blueprint** (Phase 4): Blueprint structural editing + My Blueprint present.
- **M-CrossAsset** (Phase 5, P2): diff/refactor/aggregation wired.
