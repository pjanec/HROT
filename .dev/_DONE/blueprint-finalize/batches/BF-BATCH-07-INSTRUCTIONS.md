# BF-BATCH-07: Inline mini-editors for node value pins
**Goal:** Unconnected data pins of common types get an **inline ImGui editor on the node** (type the literal
directly on the canvas). Replace the no-op `NullPinDefaultValueEditorRegistry` wired into the Blueprint canvas
with a REAL `IPinDefaultValueEditorRegistry` populated with `IPinDefaultValueEditor`s for common types.

## Lead-verified facts (re-verify, cite)
- The NodeEdit framework already HAS the inline-editor contract + render integration:
  - `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IPinDefaultValueEditorRegistry.cs` (Register / RegisterFallback / GetEditor)
  - `IPinDefaultValueEditor` in `.../ITypeSystem.cs:38` (the per-type editor contract — read its members: how it draws + reads/writes the pin's default value).
  - Consumed via `IDetailsViewProvider.Editors` (`IDetailsViewProvider.cs:30`) and `ITypeSystem.GetDefaultEditor` /
    `BlueprintTypeSystem.GetDefaultEditor` (Hrot.Blueprints.Editor/Host/BlueprintTypeSystem.cs:103).
- **Reference implementation:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo` — `S05_InlineEditors` scenario
  (`DemoShell.cs:81`) + `FakeBlueprint/FakeHostServices.cs` shows a real `IPinDefaultValueEditorRegistry` with
  editors registered. **Model the blueprint editors on this.**
- The Blueprint host currently injects `NullPinDefaultValueEditorRegistry.Instance`
  (Hrot.Blueprints.Editor/Host/NullPinDefaultValueEditorRegistry.cs — returns null for every type → no inline
  editors). Find where `BlueprintTypeSystem` is constructed with this Null registry (the wiring site) and inject
  the new real registry instead.

## Tasks
1. Implement a real `IPinDefaultValueEditorRegistry` (e.g. `BlueprintPinDefaultValueEditorRegistry`) + concrete
   `IPinDefaultValueEditor`s for the common types: `int`, `float`, `bool`, `string`, and enums (model on the
   S05 demo editors). Each editor draws the ImGui widget and reads/writes the pin's default value
   (the LiteralNode value / pin default — confirm where the value lives in the blueprint pin model:
   `Host/BlueprintPinModel.cs`). Register a sensible fallback (read-only text or none) for unknown types.
2. Wire it: replace `NullPinDefaultValueEditorRegistry.Instance` at the `BlueprintTypeSystem` construction site
   with the real registry. (Keep `NullPinDefaultValueEditorRegistry` for genuinely-headless contexts if any.)
3. Ensure edits write back through the existing edit/command path (so they mark the doc dirty + persist via the
   projection-only save — value pins ARE part of the model that saves). Do NOT break projection-only.

## Success criteria
- [ ] The Blueprint canvas uses a real pin-default-value-editor registry; common-typed unconnected value pins
      have inline editors that write the value back to the model (dirties the doc). + a headless test where
      feasible (registry returns the right editor per TypeKey; editor get/set round-trips a value).
- [ ] `dotnet build IOS-IG-SimHost.sln` 0 errors / 0 new warnings; Full Rebuild still 0 errors.
- [ ] Blueprints suite failures stay a SUBSET of the 7 pre-existing (0 new) — list the final set; do NOT claim
      0 regressions without the before/after comparison. EditorSubsystemBoot 10/10; Hrot.Editor.AiShared.Tests green.
- [ ] Report → `.dev/_DONE/blueprint-finalize/reports/BF-BATCH-07-REPORT.md`. NOTE in the report that the visual/ImGui
      behavior needs the user's running-editor verification (headless gates can't render the canvas).

## Constraints
Branch `blueprint-integ-1`. Projection-only invariant. Do NOT regenerate golden snapshots. Do NOT touch user WIP
(RecipeCreateModal/AssetBrowserWindow/EditorSubsystem). Do NOT commit (lead commits). If the running editor locks
dlls, report it. Model on the existing S05 demo — do not invent a new editor contract.
