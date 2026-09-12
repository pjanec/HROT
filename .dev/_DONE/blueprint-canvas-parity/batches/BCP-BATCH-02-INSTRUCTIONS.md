# BCP-BATCH-02: Pickers + Find/Commands + variable Get/Set value pin
**Tasks:** BCP-E (pickers), BCP-F (find bar + IEditorCommands), variable-drag value-pin fix.   **Est:** ~14h
The node-creation + search experience the user explicitly asked for: TAB add-node picker, wire-drop-to-empty picker, data-type/variable/asset pickers, Ctrl+F find, and variable Get/Set nodes that have their typed value pin.

## Onboarding
1. `.dev/.guides/DEV-GUIDE_claude.md`; `.dev/_DONE/blueprint-canvas-parity/DESIGN.md` (projection-only rule still binds).
2. **Specs:** `docs/blueprints/NodeEdit/C-picker.md`, `docs/blueprints/NodeEdit/A-canvas-interactions.md`, `docs/blueprints/NodeEdit/D0-action-api.md`, `docs/blueprints/NodeEdit/D1-to-D4-flows.md`.
3. **Specimen:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/` — `DemoShell.cs` (how FindBar + EditorCommandsImpl + BuiltinCommandHandlers + picker sources are constructed and passed to `_canvas.Render(_view, _findBar, _commands)`), `FakeBlueprint/FakeHostServices.cs` + `FakeNodePickerSource.cs` (picker source registration), and Scenarios `S07_AddNodePicker`, `S08_WireDropPicker`, `S09_VariablePicker`, `S10_TypePicker`, `S11_FlagsEnumMultiPicker`, `S12_AssetGridPicker`, `S28_FindInGraph`, `S29_FindInAsset`, `S30_GoToDefinition`.

Use codebase-memory MCP; not search_code. GizmoMap.Contracts stays 0.2.2; don't touch Hrot.IG/DDS. Headless tests must not call ImGui without a context (`ImGui.GetCurrentContext() != Zero`).

## Ground truth (verify before coding)
- **Picker registry** already flows through the host: `AiEditorAdapterBundle.PickerRegistry` → `BlueprintDocumentFactory.Build(... bundle.PickerRegistry ...)` → `BlueprintEditorHostServices.Pickers`. What's missing is **registered sources**. NodeEdit `IPickerRegistry`/`PickerRegistry` + `IPickerSource` (verify exact interface in `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/` and `NodeEditor.Core`). `PickerRegistry.Get<TItem>` was implemented in DEBT-003.
- **BlueprintNodeCatalog** (`Hrot.Blueprints.Editor/Host/BlueprintNodeCatalog.cs`) already exposes node-kind query APIs (e.g. `Query`, `QueryForPinContext` — verify names) backed by `NodeKindRegistry`. Use these for the node-type / wire-drop pickers.
- **Find + commands:** NodeEdit `FindBar`, `FindEngine`, `IEditorCommands`/`EditorCommandsImpl`, `BuiltinCommandHandlers.RegisterAll(...)` (verify signatures in `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Action/` and `.../Find/`). `CanvasRenderer.Render(GraphView, FindBar?, IEditorCommands?)`.
- **Canvas wiring:** today `EditorSubsystem.cs:~1746-1760` builds three `DelegatingCanvasRenderSeam(view => canvasRenderer.Render(view, null))` — no FindBar/commands. `AiGraphCanvasWindow.DrawClientArea` calls `_renderer.Render(ActiveContext.View)`. `AiCanvasContext` (in `Hrot.Editor.AiShared/Windows/AiGraphCanvasWindow.cs`) currently carries `View`, `Kind`, `AssetRef`.
- **Variable drag:** find the My-Blueprint → canvas drag-create handler (likely in `BlueprintMyBlueprintModel`/`BlueprintMyBlueprintWindow` or a drop handler) that creates a `GetVariableNode`/`SetVariableNode`. It currently produces a node missing its value pin. `NodePinSchema.GetCanonicalPins` already emits a `Value` data pin for Get/Set — confirm the created node flows through the projection (so the pin appears) AND that the value pin's **type** comes from the dragged variable's declared type (not `System.Object`).

## Tasks (in order)

### Task 1 — FindBar + IEditorCommands wired into the canvas (BCP-F), all 3 perspectives
Build, per opened document, a `FindBar` (`new FindBar(view, new FindEngine(view.Model, ...))`) and an `IEditorCommands` (`EditorCommandsImpl` + `BuiltinCommandHandlers.RegisterAll(commands, view, findBar, ...)`) — mirror `DemoShell.cs`. Carry them on `AiCanvasContext` (add optional `FindBar?`/`IEditorCommands?` slots) — built in each document factory (`BlueprintDocumentFactory`, `BTreeDocumentFactory`, `HsmDocumentFactory`). Change the render seam so `AiGraphCanvasWindow` passes them: `Render(view, ctx.FindBar, ctx.Commands)`. Verify Ctrl+F opens the find overlay and command dispatch works for all three perspectives.
**Tests:** `FindEngine` query over a `BlueprintGraphModel` returns the expected node matches (assert matched node ids); `IEditorCommands` add-node command dispatch produces an `AddNode` on the sink. Headless.

### Task 2 — Picker sources fully registered (BCP-E)
Register blueprint-backed `IPickerSource`s into the picker registry (in `BlueprintDocumentFactory` or a new `Host/BlueprintPickerSources.cs`), mirroring `FakeHostServices`/`FakeNodePickerSource`:
- **Add-node** (`nodes.all`) + **wire-drop by-pin** (`nodes.by-pin`): back by `BlueprintNodeCatalog.Query`/`QueryForPinContext`. TAB on empty canvas opens add-node; dragging a wire to empty canvas opens the by-pin picker filtered to pins compatible with the dragged pin's type/kind (via `BlueprintLinkValidator`/`BlueprintTypeSystem`).
- **Variable** (`variables.all`): back by the active `BlueprintAsset.Variables`.
- **Type** (`types.all`): back by `BlueprintTypeSystem` (nested categories per C-picker).
- **Asset-grid** (`assets.by-type`): back by the asset catalog / `BlueprintAssetContributor`.
- **Flags/enum** (`enum.values`): reflected enum values / vocabulary.
Wire TAB and wire-drop to open the pickers via the `IEditorCommands`/canvas interaction from Task 1 (BuiltinCommandHandlers usually wires Tab→add-node when a node source is registered — verify and complete).
**Tests:** headless per-source query/fuzzy/context-filter assertions (e.g. `nodes.by-pin` for an exec-out pin returns only exec-in-compatible kinds; `variables.all` lists the asset's variables; `types.all` returns the type set). Assert real filtered results, not non-null.

### Task 3 — Variable Get/Set value-pin fix
Make the My-Blueprint variable-drag create-path produce a Get/Set node whose **typed value pin** appears: ensure the created node projects through `NodePinSchema` (Value pin present) and that `NodePinSchema` resolves the Value pin's `TypeRef` from the dragged variable's declared type (extend `GetVariablePins`/`SetVariablePins` to take the variable's type; the create-path must set the node's `VariableId` so the projection can look up the variable + its type). Mirror demo `S15_VariablesGetSet` / `FakeCommandSink` variable node creation.
**Tests:** creating a Get node for a variable of type `System.Single` yields a node with a `Value` output pin of type `System.Single` (assert pin direction + TypeRef); Set yields exec in/out + typed value input.

## Success Criteria
- [ ] TAB opens add-node picker; wire-drop-to-empty opens the by-pin picker; data-type/variable/asset pickers work; Ctrl+F opens find — in the Blueprint perspective (and find/commands in BTree/HSM too).
- [ ] Variable Get/Set nodes show their typed value pin.
- [ ] Byte-stability test green; compiler golden suite unchanged (projection-only; any `NodeMetadata` use is ignore-null).
- [ ] `dotnet build IOS-IG-SimHost.sln` 0 errors / 0 warnings; GizmoMap.Contracts 0.2.2.
- [ ] Green: `Hrot.Blueprints.Tests` (no new failures beyond the 10 DEBT-006; the sub-80ns `WhenNodePerfTests` is flaky under load — re-run isolated), `Hrot.Editor.AiShared.Tests`, `Hrot.BTree.Editor.Tests`, `Hrot.Hsm.Editor.Tests`, `EditorSubsystemBoot` filter.
- [ ] Report at `.dev/_DONE/blueprint-canvas-parity/reports/BCP-BATCH-02-REPORT.md`.

## Execution rules
- Order: Task 1 (find/commands infra) → Task 2 (pickers, which depend on commands/TAB) → Task 3 (variable pin). Run suites yourself; never fake a pass; assert real values (matched node ids, filtered picker results, pin type/direction).
- **Reuse** NodeEdit's FindBar/FindEngine/EditorCommandsImpl/BuiltinCommandHandlers/PickerRegistry and `BlueprintNodeCatalog` queries — do NOT reimplement. Verify every type/member against the code before use.
- Projection-only stays mandatory: no `Pin` schema field, no writes to `.bp.json` beyond ignore-null `NodeMetadata` if strictly needed; no `BlueprintJsonServices` change. Keep `AiCanvasContext`/factory changes additive and applied symmetrically to BTree/HSM where they share the seam.
- Keep ImGui-dependent code gated for headless tests.

## Report
Document: how FindBar/commands are built per-document and threaded through `AiCanvasContext` + the seam; the picker sources registered + their backing queries; the TAB/wire-drop wiring; the variable value-pin fix (how the type is resolved); actual test counts; build 0/0; byte-stability + compiler-golden status; suggested commit message. No comprehension questions.
