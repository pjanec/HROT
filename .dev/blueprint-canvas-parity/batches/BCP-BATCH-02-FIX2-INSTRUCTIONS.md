# BCP-BATCH-02-FIX2: wire-from-connected-pin + full node palette + variable title/modal + title char
User re-test surfaced these. Root causes confirmed by code reading.

## Onboarding
1. `.dev/.guides/DEV-GUIDE_claude.md`; `.dev/blueprint-canvas-parity/DESIGN.md` (projection-only still binds).
Use codebase-memory MCP; not search_code. GizmoMap.Contracts 0.2.2; don't touch Hrot.IG/DDS. Headless tests gated behind `ImGui.GetCurrentContext() != Zero`.

## Task 1 (P1, SERIOUS) — wire can't be dragged from an already-connected pin
**Root cause (confirmed):** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/HitTester.cs:206` submits pin hits at `ZLayerNodeElement` (=40), but wires are submitted at `ZLayerWire` (=90), so a wire at a pin's location WINS the hit test → clicking a connected pin selects the wire instead of starting a new wire drag. The file's own documented Z-order (lines 23–27) says `Wire < Pin` with `ZLayerPin = 100`. So pins are submitted at the WRONG z-layer.
**Fix:** at `HitTester.cs:206`, submit the pin hit with `ZLayerPin` instead of `ZLayerNodeElement` (keep the same subLayer/priority). This makes pins win over wires (matches the documented intent) so clicking a connected pin starts a wire drag (`CanvasInput` HandleIdle Pin case), while empty-space/wire clicks still hit the wire.
**Tests (`NodeEditor.UI.Tests` or `NodeEditor.Core.Tests` — wherever HitTester is tested):** a pin whose screen position coincides with a wire endpoint resolves to `HoverKind.Pin` (not `HoverKind.Link`). If no HitTester test fixture exists, add a minimal one. Ensure existing hit-test tests still pass (the change only raises pin priority, which is the documented order).
**Verify** no NodeEdit test asserts pins at `ZLayerNodeElement`.

## Task 2 (P1) — node picker only offers 3 kinds; must offer the full blueprint node set
**Root cause:** `Hrot.Blueprints.Editor/BlueprintEditorBootstrap.cs:54-61` `CreatePaletteRegistry()` registers only `When`, `ReadEqsResult`, `SpawnEqsSensor`. The `nodes.all` picker is backed by this registry → only 3 items.
**Fix:** register palette entries (`NodeKindDescriptor` — Kind/DisplayName/Category/Tooltip/Icon/`CreateInstance`) for the full set of blueprint `Node` subtypes in `Hrot.Blueprints.Compiler/Assets/Nodes.cs`: FunctionCall, Branch, Sequence, GetVariable, SetVariable, Literal, EventEntry, Return, Cast, ArrayMake, ArrayGet, LatentDelay (Delay), CallCustomEvent, CallPeerBlueprint, ChannelCommand, WaitForChannel, WaitForEvent, CallEventDispatcher, BindEventDispatcher, ScoreDecision, ReadRankedResult, PartitionElements, AssignRoles, AdvancePhase, AcquireSlot (+ keep the existing When/EQS). Mirror `WhenNodePaletteEntries` shape; `CreateInstance` returns the typed node; pins are projected by `NodePinSchema` at render time (do NOT hand-author pins on the descriptor unless a kind needs specific ones). Give sensible DisplayName/Category (FlowControl, Variable, Event, Function, Array, Latent, Channel, EQS, Utility…) so the picker groups them like the demo.
**Tests (`Hrot.Blueprints.Tests`):** `CreatePaletteRegistry` registers ≥ ~25 kinds; `BlueprintNodeCatalog.Query("")` returns them all; a spot check that "Branch"/"Sequence"/"FunctionCall"/"GetVariable" are present with the right Category.

## Task 3 (P2) — variable node title shows UUID instead of the variable name
**Root cause:** `BlueprintNodeModel.BuildTitle` renders `$"Get {gv.VariableId}"` / `$"Set {sv.VariableId}"` using the raw id (e.g. `var:<guid>`).
**Fix:** resolve the variable's display NAME from the active `BlueprintAsset.Variables` (strip a `var:` prefix, match `VariableDecl.Id`) → title `"Get <name>"` / `"Set <name>"`. Pass the asset (or a `Func<string,string>` name resolver) into `BlueprintNodeModel` (mirror how the asset is already threaded for pin typing in `NodePinSchema`). Fall back to the id if not found.
**Test:** a Get node for a variable named "Health" → Title == "Get Health" (not the guid).

## Task 4 (P2, trivial) — window title shows "?" instead of a separator
**Root cause:** `AiGraphCanvasWindow.UpdateTitle` uses an em-dash "—" that the engine ImGui font can't render → "?".
**Fix:** use an ASCII separator, e.g. `"{assetName} - {assetKind}"` (plain hyphen) or `"{assetKind}: {assetName}"`. Apply in `AiGraphCanvasWindow`.
**Test:** title contains the asset name and only ASCII.

## Task 5 (P3) — variable creation should show a name/type modal
**Currently:** My Blueprint `+` auto-creates a `VariableDecl` with a default name/type. The demo opens an intermediate modal to enter name + type before creating.
**Fix:** add a small ImGui modal (name text field + type dropdown from `BlueprintTypeSystem`/the type picker) opened by the `+` command; on confirm, create the `VariableDecl` with the entered name/type; on cancel, nothing. Keep ImGui gated; extract the create logic so it's headless-testable (the modal UI itself need not be tested, but the "create with name+type" path must be).
**Test:** the create path with name="Speed", type="System.Single" adds a matching `VariableDecl` to the asset.
(If a clean modal is not achievable without large UI scaffolding, implement the create-with-name+type method + wire a minimal modal, and note any limitation in the report — do NOT leave it half-wired.)

## Success Criteria
- [ ] Dragging a NEW wire from an already-connected pin works (pin wins hit-test over wire); existing wire selection still works when clicking the wire body.
- [ ] TAB/wire-drop node picker lists the full blueprint node set (~25+ kinds), grouped by category.
- [ ] Variable Get/Set node title shows the variable name; window title uses ASCII (no "?").
- [ ] Variable `+` opens a name/type modal and creates the variable accordingly.
- [ ] Byte-stability + compiler golden unchanged. Build 0 errors / 0 warnings; GizmoMap.Contracts 0.2.2.
- [ ] Green: `Hrot.Blueprints.Tests` (no new failures beyond the 10 DEBT-006; flaky sub-80ns perf re-run isolated), `Hrot.Editor.AiShared.Tests`, `Hrot.BTree.Editor.Tests`, `Hrot.Hsm.Editor.Tests`, `EditorSubsystemBoot`, and the NodeEdit test suite touched by Task 1.
- [ ] Report at `.dev/blueprint-canvas-parity/reports/BCP-BATCH-02-FIX2-REPORT.md`.

## Execution rules
- Task 1 first (one-line NodeEdit fix + test). Then Task 2 (palette), 3 (title), 4 (separator), 5 (modal).
- Run suites yourself; assert real behavior (HoverKind.Pin over wire; palette kind count + categories; resolved title name; ASCII title; variable created with given name/type). Never fake a pass.
- Reuse `NodePinSchema`, `WhenNodePaletteEntries` shape, `BlueprintTypeSystem`, `ManagedWindow.Title`. Verify signatures first. Projection-only stays mandatory.

## Report
Document: the HitTester z-layer fix + its test; the full palette list added + categories; the title name-resolution; the separator change; the variable modal/create-path; actual test counts; build 0/0; byte-stability + golden status; suggested commit message. No comprehension questions.
