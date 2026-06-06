# BF-UX1: live-editor usability — stop reload-on-edit, fix channel-node collapse, wire selection→Details, delete old stub editor

Four fixes, all lead-diagnosed (file:line below are verified). Keep edits surgical, ESPECIALLY in
`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (user WIP — change only the lines named here).

---

## FIX A — node move / mid-edit must NOT auto-recompile (jerky editor)
**Diagnosis:** Any asset edit fires `IEditableAsset.Changed` → `EditorSubsystem.cs:2310-2315` schedules the asset
in `RegenerationScheduler` → after 500ms the `flushAction` (EditorSubsystem.cs:2459) runs, and for
`AssetKind.Blueprint` its ONLY action is `_blueprintQuickReloadTrigger?.Invoke(asset)` (line 2466) — a full
Roslyn compile + ALC load + registry swap. So every node move (which only changes EditorMetadata.X/Y via
`BlueprintCommandSink.ApplyChangeParentMultiple:441` → `_markDirty`) and every inline-value edit triggers a
recompile 500ms later. User confirms: "quick reload notification after every node move."

**Desired model (user-stated):** compile happens on SAVE or before running, NOT on every edit. Explicit
"Quick Reload"/"Full Rebuild" toolbar buttons already exist as the compile triggers. Autosave of JSON is fine.

**Fix (minimal):** Gate the Blueprint auto-quick-reload in the flushAction behind an opt-in that defaults OFF,
reusing the existing (currently-dormant) `Hrot.Blueprints.Editor.BlueprintEditorPreferences.AutoReloadOnSave`
(default `false`). At EditorSubsystem.cs:2462-2468, change:
```csharp
if (asset.Kind == Hrot.Editor.AiShared.AssetKind.Blueprint)
{
    _blueprintQuickReloadTrigger?.Invoke(asset);
    return;
}
```
to only invoke the trigger when auto-reload is enabled:
```csharp
if (asset.Kind == Hrot.Editor.AiShared.AssetKind.Blueprint)
{
    if (_blueprintAutoReloadOnEdit)          // default false; see field below
        _blueprintQuickReloadTrigger?.Invoke(asset);
    return;
}
```
Add a private field `private bool _blueprintAutoReloadOnEdit = false;` near the other blueprint editor fields.
If the `BlueprintEditorPreferences` instance is reachable where the prefs/PreferencesWindow is constructed, wire
the field from `AutoReloadOnSave` (set it when the pref changes); if not cleanly reachable, leave it as a
default-false field (the pref already defaults false, so behavior matches intent) and add a `// TODO` noting the
pref wiring. Do NOT remove the `doc.MarkDirty()` at 2312 (Save-All needs it) and do NOT change the BTree/HSM
flush branch (their JSON autosave is desired). BTree/HSM keep scheduling+saving as-is.

**Result:** by default no edit (move or value) auto-recompiles; the user recompiles via the existing toolbar
buttons or before-run. Verify the toolbar Quick-Reload / Full-Rebuild callbacks still trigger a compile.

---

## FIX B — ChannelCommand node collapses to exec-only after edit
**Diagnosis:** `BlueprintCommandSink.ApplyPinIds` (Host/BlueprintCommandSink.cs:212) calls
`NodePinSchema.GetCanonicalPins(node, _catalog.KindRegistry, _asset, containingGraph: _graph)` **without the
`channelCommands` argument**. For a `ChannelCommandNode`, `NodePinSchema.ChannelCommandPins` returns exec-only
pins when `channelCommands == null`. Those exec-only pins get stamped into `node.Pins` (line 228 `node.Pins =
ordered`). Thereafter every `BlueprintGraphModel.Rebuild` short-circuits at `NodePinSchema.GetCanonicalPins`
pass-0 (`if (node.Pins.Count > 0) return node.Pins;`) and returns ONLY the exec pins — the dynamic MoveTo param
pins are dropped. (JSON-loaded nodes with `Pins:[]` use the slow path and project correctly, which is why a
freshly-opened recipe looks right until the first rebuild after an edit re-stamps via this path.)

**Fix:** Thread the channel-command catalog into `BlueprintCommandSink` and pass it in `ApplyPinIds`:
- Add `private readonly IChannelCommandCatalog? _channelCommands;` to `BlueprintCommandSink`, set from a new
  ctor parameter (`BlueprintCommandSink` ctor is at ~line 50). Confirm the exact `IChannelCommandCatalog` type
  (`Hrot.Blueprints.Core.Compiler.Catalogs.IChannelCommandCatalog`).
- Update the construction site of `BlueprintCommandSink` (find it — likely `BlueprintDocumentFactory.cs`, which
  already has the channel catalog used by `BlueprintGraphModel`) to pass the same catalog instance.
- Change line 212 to:
  `var canonical = NodePinSchema.GetCanonicalPins(node, _catalog.KindRegistry, _asset, channelCommands: _channelCommands, containingGraph: _graph);`
  (verify the exact parameter name/order against `NodePinSchema.GetCanonicalPins`'s signature.)

**Test:** headless — build a ChannelCommandNode (ChannelType="LocomotionChannel", ActionId="MoveTo") via the
command sink's add-node path (the canvas palette path that calls ApplyPinIds), then assert `node.Pins` contains
the MoveTo param data pins (not just 2 exec pins). Also: after a `SetPinDefault` + `RebuildAndNotify`, assert the
graph model's node still exposes the param pins.

---

## FIX C — Details panel always "No node selected" (selection→Details bridge missing)
**Diagnosis:** `BlueprintDetailsWindow.ResolveSession` (Windows/BlueprintDetailsWindow.cs:89) reads
`_selectionStore.ActiveSubSelection as BlueprintNodeSelection`; if null → "No node selected." **No production
code ever sets `ActiveSubSelection`** — only tests do (`new BlueprintNodeSelection(...)` appears only in test
files). The canvas `GraphView.Selection` (NodeEditor.Core.View.SelectionState) is never bridged to the blueprint
selection store. So `sub` is always null in the live editor → every node (not just ChannelCommand) shows
"No node selected". Node identity is consistent: `BlueprintNodeModel.Id = new NodeId(node.Id)` (the asset
`Node.Id` Guid), and rebuilds reuse the same `assetNode.Id`, so there is NO id desync — the bridge is simply
absent.

**Reference pattern:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/DemoShell.cs:400-426` polls `_view.Selection`
each frame and, when exactly one node is selected, sets the details target.

**Fix:** Add a per-frame bridge for the active Blueprint document: when the blueprint canvas `GraphView` has
exactly one selected node, set
`_blueprintSelectionStore.ActiveSubSelection = new BlueprintNodeSelection(graphId, selectedNodeId)`, and set it
to `null` when the selection is empty/multi. Critical: the `graphId` stored MUST be the asset `Graph.Id` (Guid)
that `ResolveSession` compares against (`_asset.Graphs.FirstOrDefault(g => g.Id == sub.GraphId)`) — i.e. the
backing `bpAsset.Graphs[<active>].Id`, NOT the deterministic canvas `GraphId`. The selected node id is the
canvas `NodeId.Value` (== asset `Node.Id`).
- Find where the blueprint canvas (the `GraphView` for the active blueprint doc) is rendered/updated each frame
  (search `AiGraphCanvasWindow`, the blueprint perspective render, or where `GraphView`/`bundle` for the active
  blueprint doc is accessible per-frame). Add the poll there, or a small per-frame method invoked from the
  blueprint perspective's draw, reading that doc's `GraphView.Selection`.
- Keep the EditorSubsystem footprint minimal; prefer adding the bridge in the blueprint editor/canvas window
  code over EditorSubsystem if the GraphView is reachable there.

**Test:** headless is hard for a per-frame ImGui poll; at minimum add/keep a test that, given a single-node
`GraphView.Selection`, the bridge method publishes the correct `BlueprintNodeSelection(graphId, nodeId)` to the
store (extract the mapping into a small testable pure method: `(selection, asset) -> BlueprintNodeSelection?`).

---

## FIX D — delete the old stub GraphEditorWindow (user request: "old graph editor should be deleted")
`GraphEditorWindow.cs` is a non-functional placeholder (its canvas just draws `ImGui.TextDisabled("Graph: ...")`).
The real editing canvas is the NodeEdit-based canvas. Remove the confusion:
- Delete `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/GraphEditorWindow.cs`.
- Remove its registration at `BlueprintWindowRegistrar.cs:60` (`() => new GraphEditorWindow(...)`) and any now-unused
  fields/usings that only served it (e.g. if `_quickReloadService`/`_fullRebuildService` on the registrar become
  unused — verify before removing; the QuickReloadService itself is still used by the scheduler, so don't delete
  the service).
- Remove/disable the now-dead `GraphEditorWindow` tests in `Hrot.Blueprints.Tests/Editor/EditorWindowTests.cs`
  (delete just those test methods that construct `GraphEditorWindow`; keep the rest of the file).
- If removing the window registration leaves the perspective with no canvas window, STOP and report — do NOT
  remove a window the perspective actually depends on. (It should be a redundant stub; confirm the real canvas
  window is a different type before deleting.)

---

## Gate (all)
- `dotnet build IOS-IG-SimHost.sln -c Debug` → 0 errors / 0 new warnings.
- Blueprints suite WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS` set: failures a SUBSET of the current 2 pre-existing
  (`ConditionSummary ScoreCrossed`, `AllocationFree`). List the final failing set by name; 0 new. Run it a final
  time with the env var UNSET (regen mode masks snapshot failures — see lessons).
- `EditorSubsystemBoot` 10/10; `Hrot.Editor.AiShared.Tests` green; `Hrot.Blueprints.Tests` green except the 2.
- Report → `.dev/blueprint-finalize/reports/BF-BATCH-UX1-REPORT.md`: per-fix diff summary, the exact
  EditorSubsystem lines touched (must be minimal), test results (real, non-regen), and note that the live ImGui
  behavior (no reload-on-move, channel pins persist on edit, Details shows the drawer on selection) needs the
  user's running-editor verification.

## Constraints
Branch `blueprint-integ-1`. Projection-only invariant. Do NOT regenerate goldens (this batch changes no emit).
EditorSubsystem.cs is user WIP — touch ONLY the FIX-A flush gate + (if unavoidable) the FIX-C bridge site, minimal
lines, no reformatting/refactoring of surrounding code. Do NOT touch RecipeCreateModal/AssetBrowserWindow or the
Count*/Loco1/InlineEd1 .bp.json files. Do NOT commit (lead commits). If the editor app locks DLLs during build,
STOP and report. Sub-agent model: sonnet.
