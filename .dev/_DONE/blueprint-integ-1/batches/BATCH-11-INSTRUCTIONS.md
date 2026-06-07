# BATCH-11: Blueprint data-flow host adapters (graph model, type system, link validator, node catalog)
**Tasks:** AIE-040, AIE-041, AIE-042, AIE-043   **Phase:** 4 (Blueprint structural B2)   **Est:** ~13h
**Dependencies:** BATCH-10 (samples + contracts); BATCH-02 (BlueprintAssetContributor); BATCH-05 (canvas + document factory pattern).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md`.
2. `.dev/blueprint-integ-1/DESIGN.md` §2 (data-flow row + canvas contract), §5.5.
3. `.dev/blueprint-integ-1/TASK-DETAIL.md` AIE-040, AIE-041, AIE-042, AIE-043.
4. **Reference template:** the NodeEdit **FakeBlueprint** demo — `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/FakeBlueprint/` (`FakeGraphModel`, `FakeTypeSystem`, `FakeNodeCatalog`, `FakeLinkValidator`). This is a near-exact data-flow blueprint template; mirror its structure.

Use **codebase-memory MCP** first (project `D-Work-IOS-IG-SimHost-FDP-2`); not `search_code`. Headless tests must not call ImGui without a context.

## Goal
Build the four read/validate NodeEdit host adapters that project the Blueprint **data-flow** graph onto the canvas (Unreal-like: typed data pins + exec pins). This batch is **read + project + validate**; mutation (CommandSink), host-services bundle, canvas binding, and My Blueprint come in BATCH-12/13. All new classes go in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/`.

## Ground truth (verify before coding)
- Blueprint model: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/` — `BlueprintAsset` (`Graphs: List<Graph>`, `Variables`, `CustomEvents`, `EventDispatchers`, `CallablePeers`, `Parameters`, `WorkingState`), `Nodes.cs` (node kinds incl. `CallPeerBlueprintNode`), and the Graph/Node/Pin/Link types + pin **type** representation. **Read these to learn the exact node/pin/link shape and the pin type model** — the type system depends on it.
- Existing Blueprint editor palette: `Hrot.Blueprints.Editor/NodeDrawers/NodeKindRegistry.cs` + `NodeKindDescriptor.cs` (the node-kind catalog the new `BlueprintNodeCatalog` should wrap), and `WhenNodePaletteEntries`.
- NodeEdit interfaces: `IGraphModel`, `ITypeSystem`, `ILinkValidator`, `INodeCatalog`, `INodeModel`, `IPinModel`, `ILinkModel`, `TypeKey`, `PinShape`, `NodeCatalogEntry`, `NodeKindKey` (`FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/`). Mirror how `HsmGraphModel`/`BTreeGraphModel` implement them.

## Tasks (in order)

### Task 1: BlueprintTypeSystem (AIE-041) — file: `.../Host/BlueprintTypeSystem.cs`
`ITypeSystem` for Blueprint **data-flow** pins. Map Blueprint pin types → `TypeKey`s with display info, colors, shapes; implement `AreCompatible` (same-type + the exec-pin rule), `IsImplicitCast` (where Blueprint allows, e.g. int→float if the model supports it; else false), default-value editors where applicable. Model template: `FakeTypeSystem`.
**Tests:** `BlueprintTypeSystem_ExecPins_OnlyConnectToExec`; `_DataPins_CompatibleBySameType`; `_IncompatibleTypes_NotCompatible`; `_ImplicitCast_MatchesModelRules`; `_PinColor/Shape_StablePerType`. Assert actual compatibility results, not non-null.

### Task 2: BlueprintGraphModel (AIE-040) — file: `.../Host/BlueprintGraphModel.cs` (+ node/pin/link model adapters as needed)
`IGraphModel` projecting the active `BlueprintAsset` graph: nodes (each with typed input/output pins + exec pins), links between pins. Implement `Nodes`, `Links`, `FindNode`, `FindPin`, `FindLink`; raise `Changed` on asset mutation; rebuild caches. Model template: `FakeGraphModel` + `HsmGraphModel`.
**Tests:** build a known `BlueprintAsset` graph (use `Hrot.Blueprints.Tests/Builders/BlueprintAssetBuilder` if suitable) and assert: exact node count + ids; pins per node (typed + exec) match the model; exact link count + each link's From/To pin ids match the asset's connections; `FindNode`/`FindPin`/`FindLink` resolve; `Changed` fires on mutation. No NotNull-only assertions.

### Task 3: BlueprintLinkValidator (AIE-042) — file: `.../Host/BlueprintLinkValidator.cs`
`ILinkValidator` enforcing data-flow rules: reject type-incompatible data connections (delegate to `BlueprintTypeSystem`), reject exec↔data mixing, reject self-loops/illegal cycles per Blueprint semantics, and the single-input-data-pin rule (a data input pin takes one source; adding another replaces). Model template: `FakeLinkValidator`.
**Tests:** `LinkValidator_RejectsIncompatibleDataTypes`; `_RejectsExecToData`; `_AllowsValidDataLink`; `_AllowsValidExecLink`; `_SingleDataInput_ReplacesExisting` (or rejects per model); `_RejectsCycle` (if exec cycles are illegal in the model — verify).

### Task 4: BlueprintNodeCatalog (AIE-043) — file: `.../Host/BlueprintNodeCatalog.cs`
`INodeCatalog` wrapping the existing `NodeKindRegistry` palette (static node kinds) + dynamic entries for `CallablePeers` and `CustomEvents`; `Query`/`QueryForPinContext` filter by text/category/pin-type. Re-query on catalog change (hot reload). Model template: `FakeNodeCatalog` + `BTreeNodeCatalog`.
**Tests:** `BlueprintNodeCatalog_All_IncludesPaletteKinds` (entries match `NodeKindRegistry`); `_Query_FiltersByTextAndCategory`; `_QueryForPinContext_FiltersByCompatibleType` (only nodes with a pin compatible with the dragged pin's type); `_IncludesCallablePeers_AfterChange`.

## Success Criteria
- [ ] AIE-040..043 per success conditions, in `Hrot.Blueprints.Editor/Host/`.
- [ ] Green (full, no crashes): `Hrot.Blueprints.Tests` (no **new** failures beyond the 10 pre-existing DEBT-006), `Hrot.Editor.AiShared.Tests`, `EditorSubsystemBoot` filter.
- [ ] `dotnet build IOS-IG-SimHost.sln` still 0 errors.
- [ ] No warnings; docs; no leftover TODO/debug.
- [ ] Report at `.dev/blueprint-integ-1/reports/BATCH-11-REPORT.md`.

## Execution rules
- Tasks in sequence (TypeSystem first — GraphModel/LinkValidator/Catalog depend on it). Run suites yourself; fix root causes; never fake a pass; assert real values (compat results, projected node/pin/link ids/counts, catalog entries).
- **Verify the Blueprint pin/type model against the code** (Nodes.cs + Graph/Pin types) — do NOT invent the pin type representation; mirror what the asset actually stores. Reuse `NodeKindRegistry` for the palette rather than re-listing kinds.

## Report Requirements
In `reports/BATCH-11-REPORT.md`: the actual Blueprint pin/type model (how pins store their type, exec vs data); how each adapter mirrors the FakeBlueprint template vs deviates; the cast rules implemented; how the catalog wraps NodeKindRegistry + dynamic peers/events; actual test counts; confirm full-solution build 0 errors + Blueprints no new failures; suggested commit message. No comprehension questions.
