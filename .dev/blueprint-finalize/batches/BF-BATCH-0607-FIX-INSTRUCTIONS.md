# BF-BATCH-0607-FIX: make value-pin editors + ChannelCommand config actually usable
Visual smoke found both delivered batches incomplete. Two corrective fixes.

## FIX A (BATCH-07) — inline value editors must show on UNSET pins
**Bug:** `BlueprintPinModel.cs:46` gates `Default` on `pin.DefaultValue != null`, so a fresh unconnected value
pin (no value yet) gets NO editor → the user can never type the first value (e.g. arithmetic operands show
nothing). 
**Fix:** Return an editor-backed `Default` for **unconnected In-data pins whose type has a registered editor**,
regardless of whether `DefaultValue` is set; when unset, the editor shows the type's zero (0 / 0f / false / "").
- `BlueprintPinModel.Default`: condition becomes `!pin.IsExec && pin.Direction == "In"` AND the pin's type has an
  editor in the registry (consult the same `IPinDefaultValueEditorRegistry`/`BlueprintTypeSystem` used by the
  canvas — thread it into BlueprintPinModel/BlueprintGraphModel if not already reachable). Don't return Default
  for types with no editor (avoids empty widgets).
- `BlueprintPinDefaultValue`: handle a null/empty raw value → boxed type-zero (so the widget renders at 0/false/"").
- The NodeEdit renderer already hides the editor when the pin is connected — confirm (don't double-implement).
- When the user edits, persist via the existing `SetPinDefault` path (Node.PinDefaults) so it round-trips.
**Test:** an unconnected int/float In-data pin yields a non-null `Default` (type-zero) even with `DefaultValue==null`;
a connected pin / Out / Exec / unsupported-type pin yields null.

## FIX B (BATCH-06) — ChannelCommand needs a config drawer (select channel + action)
**Bug:** No `ChannelCommandNodeDrawer`. A ChannelCommand node can't be configured → no `ActionId` → no param
pins → blank title. (`BlueprintNodeModel.cs:87` already titles it `"Command: {ActionId}"`, so once ActionId is
set the title shows it.)
**Fix:** Add `ChannelCommandNodeDrawer : IBlueprintNodeDrawer` modeled on `FunctionCallNodeDrawer`
(NodeDrawers/FunctionCallNodeDrawer.cs — ctor takes `IEditService`; `Draw()` uses `ImGui.Combo`; mutations go
through `_editService.RecordPropertyEdit` + rebuild):
- A Combo listing the channel actions from the catalog (`IChannelCommandCatalog.GetEntries()` →
  ChannelType + ActionId per entry; show a readable label e.g. `"{ChannelType} / {ActionId}"`).
- On selection, set the node's `ChannelType` + `ActionId` (verify the exact `ChannelCommandNode` field names in
  Hrot.Blueprints.Compiler/Assets/Nodes.cs) via `IEditService.RecordPropertyEdit` (undo/redo) then trigger the
  graph rebuild so `NodePinSchema.ChannelCommandPins` re-projects the now-resolvable param pins (the BATCH-06
  data — MoveTo→Destination etc.).
- Register the drawer in `BlueprintNodeDrawerRegistry` (mirror how FunctionCallNodeDrawer is registered;
  confirm the registration + IEditService injection site).
**Test:** selecting an action sets ChannelType/ActionId on the node (headless via the drawer's mutation hook,
like FunctionCallNodeDrawer's test hooks); after setting MoveTo, `NodePinSchema.GetCanonicalPins` for that node
projects MoveToParams' data-IN pins.

## Gate (both)
- `dotnet build IOS-IG-SimHost.sln` 0 errors / 0 new warnings; Full Rebuild 0 errors.
- Blueprints failures a SUBSET of the 7 pre-existing (0 new) — list the final set; do NOT claim 0 regressions
  without the before/after comparison. `Hrot.Editor.AiShared.Tests` green; `EditorSubsystemBoot` 10/10.
- Report → `.dev/blueprint-finalize/reports/BF-BATCH-0607-FIX-REPORT.md`. NOTE the visual behavior (editors
  appear + typeable; ChannelCommand Combo selects + param pins appear + title updates) needs the user's
  running-editor verification.

## Constraints
Branch `blueprint-integ-1`. Projection-only (Node.PinDefaults is the persisted bag; pins still saved as []).
Do NOT regenerate goldens. Do NOT touch user WIP (RecipeCreateModal/AssetBrowserWindow/EditorSubsystem unless a
required wiring site is there — then change only the minimal needed line). Do NOT commit (lead commits). If the
running editor locks dlls, report it. Model on the existing FunctionCallNodeDrawer + the NodeEdit editor registry —
don't invent new contracts.
