# BCP-BATCH-01: Visible core — demo theme + movable nodes + pins/wires
**Tasks:** BCP-C, BCP-B, BCP-A   **Est:** ~12h
Directly fixes the three user-visible defects: yellow marquee (wrong theme), nodes can't be moved, pins & wires completely missing.

## Onboarding
1. `.dev/.guides/DEV-GUIDE_claude.md`.
2. `.dev/_DONE/blueprint-canvas-parity/DESIGN.md` (read the **PINS ARE PROJECTION-ONLY** decision — it is mandatory).
3. Specimen: `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/` — `DemoShell.cs`, `FakeBlueprint/{FakeEditorTheme,FakeNodeModel,FakeGraphModel,FakeCommandSink}.cs`. Mirror these.

Use **codebase-memory MCP**; not `search_code`. **GizmoMap.Contracts stays 0.2.2; do not touch Hrot.IG/DDS.** Headless tests must not call ImGui without a context. **DO NOT change the `.bp.json` serialization format or write pins to disk** (see guardrail).

## Task C — Demo theme on all three perspectives
- File: `Hrot/Editor/Hrot.Editor.AiShared/Adapters/EngineEditorTheme.cs`. It currently forwards every color/geometry to the engine `DefaultTheme` (`_base`). Replace the color + geometry members with the demo's literal values from `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/FakeBlueprint/FakeEditorTheme.cs` (BackgroundColor, Grid Minor/Major, **SelectionAccent (0.21,0.52,0.89,1)**, PrimarySelectionAccent (0.26,0.65,0.99,1), Error/Warning, TextDefault/Muted, NodeCornerRadius 4, NodeBorderThickness 1.5, NodeHeaderHeight 28, PinGlyphSize 10, WireThicknessExec 3, WireThicknessData 2, and `GetCategoryHeaderColor` per-category map). Keep `GetFontForSize` (engine font atlas) and the Attachment* colors as they are. This single shared instance (`AiEditorAdapterBundle`) covers BTree/HSM/Blueprint.
- **Tests:** `EngineEditorThemeTests` asserting SelectionAccent, corner radius, exec/data wire thickness, and `GetCategoryHeaderColor(Event)` equal the demo values. Check `Hrot.Editor.AiShared.Tests` first for any existing theme-color assertions and update them.

## Task B — In-place node movement (no rebuild-on-drag)
Mirror `BTreeCommandSink.ApplyNodeMoves` (`node.Position = m.NewPosition`, mark dirty, no model rebuild) and demo `FakeCommandSink.cs:48-51`.
- `Host/BlueprintNodeModel.cs`: make `Position` a mutable backing field with `internal void SetPosition(Vector2)`; stop relying solely on the ctor snapshot.
- `Host/BlueprintGraphModel.cs`: add `NotifyMoved(IReadOnlyCollection<NodeId>)` that fires `Changed` with a move-kind notification and does **not** call `Rebuild()`. (Check `GraphChangeKind` for a NodesMoved/Layout kind; if none fits, use the least-invasive existing kind — do not rebuild caches.)
- `Host/BlueprintCommandSink.cs` `ApplyMoveNodes`: update `assetNode.EditorMetadata.X/Y`, update the existing `BlueprintNodeModel` instance in place via `_model.FindNode(...)` + `SetPosition`, `_markDirty`, then `NotifyMoved(...)` — **remove the `RebuildAndNotify()` call**.
- **Tests:** extend `BlueprintCommandSinkTests` — after `MoveNodes`, the SAME `INodeModel` instance reference is retained (identity unchanged) and its `Position` updated; assert no full rebuild occurred (spy a rebuild counter on the model).

## Task A — Pin/wire hydration (THE #1 fix; projection-only)
Loaded asset nodes have `Pins: []`; links carry real pin GUIDs. Hydrate pins in the projection and bind their GUIDs from the incident links so wires resolve. **Persist nothing.**
- New `Host/NodePinSchema.cs`: `static IReadOnlyList<Pin> GetCanonicalPins(Node node)` returning the per-kind pin list. Source first from the kind registry descriptor (`BlueprintNodeCatalog.KindRegistry.TryGet(node.GetType().Name)?.CreateInstance().Pins` when non-empty) and a built-in fallback table for the `Node` subtypes in `Hrot.Blueprints.Compiler/Assets/Nodes.cs` that the registry doesn't populate (Branch: In exec / True exec / False exec; Sequence: In + Then exec(s); FunctionCall: In/Out exec + data params; Return: In exec; GetVariable: Value data-out; SetVariable: In/Out exec + Value data-in/out; Literal: Value data-out; EventEntry: Out exec; Cast: In/Out exec + In/Out data; When/Eqs already in `NodeDrawers/WhenNodePaletteEntries.cs` — reuse). Pin name/direction/IsExec/TypeRef per kind. Keep it pragmatic but cover the kinds in the test assets (MoveToAndFire etc.).
- `Host/BlueprintGraphModel.cs` `Rebuild`: make it **two-pass**:
  1. For each node, get canonical pins; collect that node's incident links from `_graph.Links`.
  2. Assign each connected canonical pin the GUID from its incident link (match by direction/role/order so `FromPinId` binds an output pin, `ToPinId` an input pin); unconnected pins get `IdGenerator.Deterministic($"pin:{nodeId}:{name}:{dir}")`.
  3. Build `BlueprintNodeModel`/`BlueprintPinModel` with those GUIDs; then build links (they now resolve via `FindPin`).
  This likely means `BlueprintNodeModel` takes the resolved pin list (refactor its ctor to accept pre-resolved `IReadOnlyList<Pin>` or a pin-GUID map) rather than reading `node.Pins` directly.
- Keep `MakeLinkId` the deterministic `(fromPin,toPin)` hash so `RemoveLinks` still inverts.
- **Tests (`Hrot.Blueprints.Tests/Host`):** load `TestAssets/MoveToAndFire.bp.json`; assert every node has its expected canonical pins; assert **every link resolves** (`FindPin(FromPin)!=null && FindPin(ToPin)!=null`); assert connected pins' GUIDs equal the JSON link GUIDs. Add the **byte-stability guardrail test**: load each `TestAssets/**.bp.json` + `Comparison/Fixtures/*.bp.json`, serialize via `BlueprintJsonServices`, assert byte-identical to the original.

## Success Criteria
- [ ] BCP-C/B/A done; Blueprint canvas shows pins+wires, nodes drag smoothly, demo color scheme on all 3 perspectives.
- [ ] **Byte-stability test green** (no `.bp.json` changes) and the **compiler golden/snapshot suite unchanged** (run it; any drift = stop and fix the approach).
- [ ] `dotnet build IOS-IG-SimHost.sln` 0 errors / 0 warnings; GizmoMap.Contracts 0.2.2.
- [ ] Green: `Hrot.Blueprints.Tests` (no new failures beyond the 10 DEBT-006), `Hrot.Editor.AiShared.Tests`, `Hrot.BTree.Editor.Tests`, `Hrot.Hsm.Editor.Tests`, `EditorSubsystemBoot` filter.
- [ ] Report at `.dev/_DONE/blueprint-canvas-parity/reports/BCP-BATCH-01-REPORT.md`.

## Execution rules
- Order C → B → A. Run suites yourself; fix root causes; never fake a pass; assert real values (theme color tuples, instance identity on move, resolved link endpoints + matching GUIDs), not non-null.
- **Projection-only is mandatory:** do not add fields to `Pin`, do not write pins to JSON, do not change `BlueprintJsonServices`. If you think you must, STOP and report why.
- Reuse demo patterns + existing NodeEditor.UI; don't reimplement renderers.

## Report Requirements
`reports/BCP-BATCH-01-REPORT.md`: the canonical pin schema you implemented (per kind), the two-pass GUID-binding algorithm, the move-notify change, the theme values; byte-stability + compiler-golden results; actual test counts; full build 0/0; suggested commit message. No comprehension questions.
