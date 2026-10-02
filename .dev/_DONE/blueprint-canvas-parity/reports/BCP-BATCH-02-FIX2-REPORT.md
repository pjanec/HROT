# BCP-BATCH-02-FIX2 Report

## Implementation Summary

### Task 1 (SERIOUS) — wire-from-connected-pin
`FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/HitTester.cs:206` submitted pin hits at
`ZLayerNodeElement` (40), below `ZLayerWire` (90), so a wire crossing a pin's screen
position won the hit test and clicking a connected pin selected the wire instead of
starting a new wire drag. Fixed the single line to submit the pin hit at `ZLayerPin`
(100), matching the file's own documented Z-order (`Wire < Pin`). subLayer/priority
unchanged. This is the only NodeEdit-core edit.

- Test added to `HitTesterZOrderTests.cs`:
  `Pin_coincident_with_wire_endpoint_resolves_to_pin_not_link` — feeds the exact
  `(z, subLayer, priority)` tuples `UpdateHover` now uses (wire at `ZLayerWire`, pin at
  `ZLayerPin`, same subLayer/priority) and asserts the winner is `HoverKind.Pin`, not
  `HoverKind.Link`.
- Verified no NodeEdit test asserts pins at `ZLayerNodeElement` (grep clean).

### Task 2 — full node palette
`BlueprintEditorBootstrap.CreatePaletteRegistry()` registered only When/ReadEqsResult/
SpawnEqsSensor (3 kinds → 3 picker items). Added a new `BlueprintNodePaletteEntries.All()`
factory that yields `NodeKindDescriptor`s for every core blueprint `Node` subtype, and
registered them after the When/EQS trio. Pins are NOT hand-authored on the descriptors —
`CreateInstance` returns a default-constructed typed node with empty `Pins`, and
`NodePinSchema` hydrates the canonical pins at render time (projection-only). The
registry now holds **27 kinds** (24 new + 3 existing When/EQS).

Palette list added (Kind → DisplayName → Category):
- Flow Control: `Branch` (Branch), `Sequence` (Sequence), `Return` (Return)
- Events: `EventEntry` (Event Entry), `CallCustomEvent` (Call Custom Event),
  `CallDispatcher` (Call Event Dispatcher), `BindDispatcher` (Bind Event Dispatcher),
  `WaitForEvent` (Wait For Event)
- Variables: `GetVariable` (Get Variable), `SetVariable` (Set Variable)
- Function: `FunctionCall` (Function Call), `Literal` (Literal), `Cast` (Cast),
  `CallPeerBlueprint` (Call Peer Blueprint)
- Array: `ArrayMake` (Make Array), `ArrayGet` (Get Array Element)
- Latent: `Delay` (Delay)
- Channel: `ChannelCommand` (Channel Command), `WaitForChannel` (Wait For Channel)
- Decision: `ScoreDecision` (Score Decision), `ReadRankedResult` (Read Ranked Result)
- Squad: `PartitionElements`, `AssignRoles`, `AdvancePhase`, `AcquireSlot`
- (kept) EQS/Reactive: `When`, `ReadEqsResult`, `SpawnEqsSensor`

Note: the `Delay` kind name matches the JSON discriminator for `LatentDelayNode` ("Delay")
and `NodePinSchema`'s short-name lookup, so pin projection resolves correctly.

Tests (`BcpBatch02BlueprintTests.cs`):
- `Palette_RegistersFullBlueprintNodeSet_WithCategories` — asserts ≥ 25 kinds; When/EQS
  present; Branch/Sequence/FunctionCall/GetVariable present with correct Category; and
  `BlueprintNodeCatalog.Query("")` returns all of them.
- `Palette_CreateInstance_ReturnsTypedNodesWithFreshIds` — asserts the factory returns the
  right concrete type with a non-empty, distinct `Id` per call.

### Task 3 — variable node title shows NAME, not UUID
`BlueprintNodeModel.BuildTitle` rendered `"Get {gv.VariableId}"` (raw `var:<guid>`). Threaded
the owning `BlueprintAsset` into the `BlueprintNodeModel` constructor (mirrors how the asset
is already threaded into `NodePinSchema` for pin typing) and added `ResolveVariableName`,
which strips a `var:` prefix, parses the GUID, matches `VariableDecl.Id`, and returns the
declared `Name`. Falls back to the raw id when the asset is null or the variable is missing.
`BlueprintGraphModel.Rebuild` now passes `_asset` when constructing each node model.

Tests:
- `VariableNodeTitle_GetNode_ShowsVariableName_NotUuid` — variable "Health", `var:<guid>`
  id form → Title == "Get Health" and does not contain the guid.
- `VariableNodeTitle_SetNode_ShowsVariableName` → "Set Health".
- `VariableNodeTitle_UnknownVariable_FallsBackToId` → "Get <rawid>".

### Task 4 (trivial) — window title char
`AiGraphCanvasWindow.UpdateTitle` used an em-dash (`—`, U+2014) the engine font cannot
render (showed as "?"). Changed to a plain ASCII hyphen: `"{assetName} - {assetKind}"`.

Test (`AiGraphCanvasWindowTests.cs`):
- `UpdateTitle_UsesAsciiSeparator_AndContainsAssetName` — opens a Blueprint doc named
  "PatrolBehavior", runs the headless `SimulateDrawClientArea` title path, asserts the
  title contains the asset name, contains no em-dash, is pure ASCII (every char ≤ 0x7F),
  and uses the `" - "` separator.

### Task 5 — variable-create modal
The My Blueprint `+` previously invoked `editor.create-variable` → `AddVariable`, which
auto-created a `VariableDecl` with a default name/type. Implemented:
- `BlueprintDocumentFactory.CreateVariable(asset, name, typeId, markDirty)` — the
  headless-testable create path: trims/dedups the name, defaults blank name→"NewVar" and
  blank type→`System.Boolean`, appends the `VariableDecl`, marks dirty. The legacy
  `AddVariable` now delegates to it (the two pre-existing create-variable tests stay green).
- `BlueprintTypeSystem.SelectableTypeIds` — public ordered list of type ids for the modal
  dropdown (previously the `_types` palette was private).
- `VariableCreateModal` (new, `Windows/`) — a small ImGui modal (name `InputText` + type
  `Combo` from `SelectableTypeIds`) gated behind `ImGui.GetCurrentContext() != Zero`; it
  owns only transient UI state and delegates creation to a `(name, typeId)` callback, so
  the create path is fully headless-testable.
- `BlueprintDocumentFactory.RegisterCreateVariableCommand(commands, openModal)` overload —
  routes the `+` command to open the modal instead of creating directly.
- `BlueprintMyBlueprintWindow` now builds the modal per active asset (wiring its confirm
  callback to `CreateVariable` with the asset's dirty callback), re-registers the
  create-variable command to open it, and draws it each frame in `DrawClientArea`.

Tests:
- `CreateVariable_WithNameAndType_AddsMatchingVariableDecl` — name="Speed",
  type="System.Single" → a single matching `VariableDecl` is added and the doc is dirtied.
- `CreateVariable_DuplicateName_IsMadeUnique` — repeat creates yield distinct names.
- `CreateVariable_BlankInputs_FallBackToDefaults` — blank name/type → defaults.
- `VariableCreateModal_ConfirmCallback_CreatesVariable` — `Draw()` is a safe headless
  no-op (creates nothing on its own); firing the confirm callback creates the variable.

## Design Decisions
- **No hand-authored pins on the new palette descriptors** (per spec): pins come from
  `NodePinSchema` at render time. Consequence: the `nodes.by-pin` picker filter (which reads
  `CreateInstance().Pins` via the catalog) sees no pins for these kinds, so by-pin filtering
  is a no-op for them and they fall back to appearing in the unfiltered `nodes.all` list.
  This matches the projection-only rule; richer by-pin filtering would require either
  hand-authored descriptor pins or teaching the catalog to consult `NodePinSchema`. Noted as
  a follow-up, not a regression (the picker still lists every kind).
- **Asset threaded as an optional ctor arg** on `BlueprintNodeModel` (default null) so existing
  unit tests that build the model without an asset keep compiling/passing.
- **Modal hosted in the My Blueprint window**, not the canvas window, because that window
  already owns the asset + commands via `Retarget` and renders the My Blueprint `+`.

## Deviations
- Added a second `RegisterCreateVariableCommand` overload rather than changing the existing
  one. WHY: two pre-existing tests invoke the old (direct-create) overload and assert a
  variable appears immediately; changing it to open a modal would break them and the headless
  create-path coverage. BENEFIT: production gets the modal; the headless create path stays
  directly testable. RISK: two registration entry points for the same command id — mitigated
  because production calls the modal overload last (last registration wins).

## Test Results
- `NodeEditor.UI.Tests`: **41 passed / 0 failed** (includes the new HitTester regression test;
  the focused `HitTesterZOrderTests` run = 17/17).
- `Hrot.Blueprints.Tests`: **1097 passed / 11 failed / 8 skipped**. The 11 = the 10
  pre-existing **DEBT-006** failures (6 `Compiler.*EmitGoldenTests`, 2 `Demos.*Snapshot`,
  1 `Runtime.AllocationFreeTests`, 1 `Editor.ConditionSummaryAttachmentTests`) + 1 flaky
  sub-ns perf (`WhenNodePerfTests.WhenNode_ConditionMet_Under200ns_perTick`), which **passes
  when re-run isolated** (1/1). No new failures. My new `BcpBatch02BlueprintTests` = 26/26.
- `Hrot.Editor.AiShared.Tests`: **761 passed / 0 failed**.
- `Hrot.BTree.Editor.Tests`: **382 passed / 0 failed**.
- `Hrot.Hsm.Editor.Tests`: **333 passed / 0 failed**.
- `Hrot.ClusterRunner.Integration.Tests --filter ~EditorSubsystemBoot`: **10 passed / 0 failed**.
- Byte-stability: the Blueprints byte-stability fixtures are part of the Blueprints suite and
  passed (not in the failure set); no `.bp.json`/`BlueprintJsonServices`/Pin-schema change was
  made, so byte-stability + compiler golden are unaffected by this batch.

## Build
`dotnet build IOS-IG-SimHost.sln` → **0 errors**. The 18 reported warnings are all in
pre-existing test files I did not touch (`Hrot.Common.Tests` migration tests,
`Hrot.Utility.Editor.Tests`, `Fdp.Core.Tests` migration tests,
`Hrot.Diagnostics.Breakpoints.Tests`) — none in any file created/modified by this batch.
Both projects I edited build at **0 warnings** (`Hrot.Blueprints.Editor`,
`Hrot.Editor.AiShared`).

## Developer Insights
- The bug in Task 1 was purely a wrong-constant: the Z constants already encoded `Pin > Wire`,
  but the call site used `ZLayerNodeElement`. The pre-existing z-order tests only checked the
  constants, never the actual submission, so they couldn't catch it — the new test mirrors the
  real submission tuple.
- `NodePinSchema`'s registry lookup tries both `<Type>Name` and the suffix-stripped short name,
  and the new descriptors key on the short name (e.g. "Branch"), so they resolve cleanly.
- The catalog builds pin signatures from `CreateInstance().Pins`; with projection-only pins
  these are empty, which is why by-pin filtering on the new kinds is currently a no-op (see
  Design Decisions).

## Known Issues
- `nodes.by-pin` filtering does not narrow the new (pin-less-descriptor) kinds, since the
  catalog derives pin signatures from the descriptor instance rather than `NodePinSchema`.
  Acceptable under projection-only; a future improvement could route the catalog through
  `NodePinSchema`.
- DEBT-006's 10 golden/snapshot/allocation failures remain (pre-existing, unrelated).

## Suggested Commit Message
fix(bp-canvas): wire-from-connected-pin hit order, full node palette, variable name titles, ASCII window title, variable-create modal (BCP-BATCH-02-FIX2)
