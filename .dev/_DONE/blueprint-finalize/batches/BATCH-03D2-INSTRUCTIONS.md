# BATCH-03D2 — Editor UI: graph-signature editing panel (Graph.Inputs / Graph.Outputs)

> **Coder contract:** read `.dev/.guides/DEV-GUIDE_claude.md` first. Verify-first, cite `file:line`,
> never fake a pass, implement→build→test→fix to green. **Codebase Memory MCP first**. Project
> `D-Work-IOS-IG-SimHost-FDP-2`. No `search_code`/tree grep. UI batch — headless-test the edit model;
> the ImGui `Draw` body is verified by a later manual smoke (not in this batch).

## Mission

Let an author edit a Function graph's **signature** — its `Inputs` and `Outputs` (`List<ParameterDecl>`,
each `Name` + `BlueprintTypeRef Type`). Editing the signature is what drives the Entry/Return value pins
(BATCH-03C) and FunctionCall mirroring (BATCH-03A/C). Add/remove/rename/retype rows for both lists, marking
the asset dirty so Quick-Reload re-projects + recompiles.

## Read first
- `Variables/BlueprintVariablesWindow.cs` + `BlueprintVariableSchemaSource` — the closest analog (edits the
  asset's `Parameters`/`WorkingState` lists, marks dirty via an `onChanged` delegate → `DirtyTracker`).
  Quote the `IVariablesSchemaSource` interface and the Add/Remove/Rename/Move methods; decide if reuse fits
  (see "Rendering" below).
- `Hrot.Editor.AiShared` `VariablesPanelControl` (+ `VariablesPanelSection`, `BlackboardVariableEntry`) —
  what it requires (note any byte-budget / pack-warning semantics that are blackboard-specific and may NOT
  suit function-signature params).
- `Blackboard/BlackboardTypeHelper.cs` `DefaultKnownTypeNames` — the type list for the type picker.
- `Assets/GraphTypes.cs` `Graph` (`Inputs`/`Outputs : List<ParameterDecl>`, `Kind`),
  `Assets/Declarations.cs` `ParameterDecl` (`Id`, `Name`, `Type : BlueprintTypeRef`, `Comment`).
- `DirtyTracker.cs`; how `BlueprintVariablesWindow` is registered/retargeted in `EditorSubsystem`
  (`RegisterExtraWindow`, selection store). `Windows/BlueprintDetailsWindow.cs` for the ManagedWindow +
  headless-seam pattern. `EditorSelectionStore` exposes only `SelectedAsset` (NO active graph) — so the
  panel will use a **graph-picker combo** to choose which Function graph to edit.

## Changes

### 1. Headless edit model (the testable core)
Add a `GraphSignatureEditModel` (e.g. under `Variables/`) that wraps a single `Graph` and exposes
mutations on either its `Inputs` or `Outputs` list (a `bool isOutputs` or two instances):
- `AddParameter(string name, string typeId)` → append a `ParameterDecl { Id = Guid.NewGuid(), Name,
  Type = new BlueprintTypeRef { TypeId = typeId } }`.
- `RemoveParameter(string name)` / `RenameParameter(string oldName, string newName)` /
  `RetypeParameter(string name, string newTypeId)` / (optional) `MoveParameter(int from, int to)`.
- Each mutation invokes an injected `Action onChanged` (the window wires it to
  `dirtyTracker.MarkDirty(asset.AssetId)`).
Keep this class free of ImGui so it is fully unit-testable. (If reusing `IVariablesSchemaSource`/
`BlueprintVariableSchemaSource` cleanly expresses these on `ParameterDecl` lists, you MAY implement the
model as an `IVariablesSchemaSource` instead — but only if it does not drag in blackboard byte-budget
semantics that misrepresent a function signature. Justify the choice in the report.)

### 2. The panel/window
Add a `GraphSignatureWindow` (`ManagedWindow`, mirror `BlueprintDetailsWindow`'s structure + a `Retarget`
for the active asset). `DrawClientArea`:
- read active asset from the selection store; if none → `ImGui.TextDisabled("No blueprint selected.")`.
- a **graph-picker combo** over `asset.Graphs.Where(g => g.Kind == GraphKind.Function)` (by `Name`);
  remember the selected graph id as view-state.
- two sections — "Inputs" and "Outputs" — each rendering the chosen graph's param rows: Name (InputText),
  Type (combo over `BlackboardTypeHelper.DefaultKnownTypeNames`), and add/remove buttons, driving the
  `GraphSignatureEditModel`. **Rendering choice:** reuse `VariablesPanelControl` ONLY if it cleanly fits
  (no inappropriate byte-budget UI); otherwise build a focused rows panel with plain ImGui widgets. Either
  way, keep all ImGui inside the Draw body and the mutation logic in the (tested) edit model.
- expose a headless seam (like `BlueprintDetailsWindow.ResolveSession`) — e.g. a method that returns the
  current `GraphSignatureEditModel` for the selected graph — so tests can drive add/remove without ImGui.

### 3. Register the window
Wire `GraphSignatureWindow` into the editor the same way `BlueprintVariablesWindow`/`BlueprintDetailsWindow`
are (construct with the selection store + dirty tracker, `RegisterExtraWindow` in `EditorSubsystem`, and
`Retarget` on document/selection change). Keep `EditorSubsystemBoot` green.

## Tests (headless)
- `GraphSignatureEditModel`: Add appends a `ParameterDecl` with the right Name/TypeId to `graph.Inputs`
  (and separately `graph.Outputs`); Remove/Rename/Retype work; each fires `onChanged` (spy) exactly once.
- A round-trip: after `AddParameter`, projecting the graph's EventEntryNode via `NodePinSchema`
  (BATCH-03C) yields a matching data-OUT pin — proving the signature edit drives the pins. (Optional but
  valuable; reuse the BATCH-03C test helpers.)
- Window headless seam: with a selected asset + a chosen Function graph, the window returns an edit model
  bound to that graph; selecting a different graph rebinds.
- Registration/boot: window constructs without ImGui; `EditorSubsystemBoot` stays 10/10.

## Verification (paste real output)
1. `dotnet build IOS-IG-SimHost.sln` — 0 errors; 0 new warnings in touched projects.
2. New edit-model + window tests green.
3. Full `Hrot.Blueprints.Tests`: failures a SUBSET of the pre-existing **7**, 0 new, no golden changed.
4. `Hrot.ClusterRunner.Integration.Tests --filter FullyQualifiedName~EditorSubsystemBoot` → 10/10.

## Report
`.dev/_DONE/blueprint-finalize/reports/BATCH-03D2-REPORT.md`: the edit model + window (file:line), the rendering
choice + justification (VariablesPanelControl reuse vs bespoke), how edits mark dirty, the graph-picker
approach to the active-graph gap, test names + output, full-suite classification, and that the Draw body
needs a manual smoke. **Do not commit** — lead reviews/commits.
