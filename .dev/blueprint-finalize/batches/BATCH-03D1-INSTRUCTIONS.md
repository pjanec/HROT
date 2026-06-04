# BATCH-03D1 — Editor UI: FunctionCall Details drawer (CLR method + in-blueprint graph picker)

> **Coder contract:** read `.dev/.guides/DEV-GUIDE_claude.md` first. Verify-first, cite `file:line`,
> never fake a pass, implement→build→test→fix to green. **Codebase Memory MCP first**. Project
> `D-Work-IOS-IG-SimHost-FDP-2`. No `search_code`/tree grep. UI batch — headless-test the non-rendering
> logic; the `Draw()` ImGui body is verified by a later manual smoke pass (not in this batch).

## Mission

Answer the user's "how do we configure Function Call?" Add a node-drawer Details panel for
`FunctionCallNode`: pick either a **CLR library type+method** (sets `TargetTypeId`/`MethodName`/`IsPure`)
or an **in-blueprint Function graph** (sets `TargetGraphId`). The two modes are mutually exclusive. Edits
mutate the node + mark the asset dirty so the editor's Quick-Reload re-projects pins (BATCH-03C) and
recompiles.

The node-drawer pump already exists: `BlueprintDetailsWindow` (`Windows/BlueprintDetailsWindow.cs`) reads
the active node selection, resolves a drawer via `BlueprintNodeDrawerRegistry.GetDrawerFor`, creates/caches
an `INodeEditSession`, and calls `Draw()`; its `ResolveSession()` is headless-testable. So a registered
`FunctionCallNodeDrawer` is picked up automatically.

## Read first (mirror these exactly)
- `NodeDrawers/IBlueprintNodeDrawer.cs`, `NodeDrawers/INodeEditSession.cs`, `NodeDrawers/IEditService.cs`
  (`MarkDirty(BlueprintAsset)` stub).
- `NodeDrawers/SpawnEqsSensorNodeDrawer.cs` — the simplest drawer+session example (direct node mutation,
  `IsDirty`, `ResetDirty`, a `*ForTest` hook). `NodeDrawers/WhenNodeDrawer.cs` — example that takes
  `IEditService` and calls `MarkDirty(_parent)` on edit.
- `BlueprintEditorBootstrap.cs` `CreateNodeDrawerRegistry` (~23-48) — the registration site (already has an
  `IEditService editService` param).
- `Windows/BlueprintDetailsWindow.cs` `ResolveSession()` — the pump + the headless seam.
- `Assets/Nodes.cs` `FunctionCallNode` (`TargetTypeId`, `MethodName`, `IsPure`, `TargetGraphId`).
- `Assets/GraphTypes.cs` `Graph`/`GraphKind` (enumerate `asset.Graphs.Where(g => g.Kind == GraphKind.Function)`).
- Tests: `Tests/Editor/SpawnEqsSensorNodeDrawerTests.cs` + `Tests/Integration/WhenNodeEditorWiringTests.cs`
  (+ `DrawerRegistryTests.cs`) — the headless test patterns to mirror.

## Changes

### 1. `NodeDrawers/FunctionCallNodeDrawer.cs` (new)
- `public sealed class FunctionCallNodeDrawer : IBlueprintNodeDrawer` — ctor takes `IEditService editService`.
  `Handles(node) => node is FunctionCallNode`. `CreateSession(node, parentAsset) =>
  new FunctionCallNodeSession((FunctionCallNode)node, parentAsset, _editService)`.
- `FunctionCallNodeSession : INodeEditSession`:
  - Fields: `_node`, `_parent`, `_editService`; `public bool IsDirty { get; private set; }`.
  - `Draw()` (ImGui):
    - `IsPure` checkbox → on toggle set `_node.IsPure`, `MarkChanged()`.
    - A mode selector ("In-blueprint function" vs "CLR method") — current mode inferred from
      `!string.IsNullOrEmpty(_node.TargetGraphId)`.
    - **In-blueprint mode:** a combo listing `_parent.Graphs.Where(g => g.Kind == GraphKind.Function)`
      by `Name`; on select set `_node.TargetGraphId = graph.Id.ToString()` and **clear**
      `_node.TargetTypeId`/`_node.MethodName`; `MarkChanged()`. Show "(no function graphs in this
      blueprint)" when none.
    - **CLR mode:** `InputText` for `TargetTypeId` and `MethodName`; on edit set them and **clear**
      `_node.TargetGraphId`; `MarkChanged()`. Optionally show a one-line resolved-signature hint (reuse
      reflection like `NodePinSchema`'s `ResolveMethod` if cheap; OK to skip the hint if it adds risk).
  - `private void MarkChanged()` → `IsDirty = true; _editService?.MarkDirty(_parent);`.
  - `ResetDirty()` → `IsDirty = false`. `Dispose()` no-op.
  - **Test hooks** (mirror `SelectTemplateForTest`): `internal void SelectFunctionGraphForTest(Guid graphId)`
    (sets `TargetGraphId`, clears CLR fields, MarkChanged); `internal void SetClrTargetForTest(string
    typeId, string methodName, bool isPure)` (sets CLR fields + IsPure, clears TargetGraphId, MarkChanged).
  - Keep ALL ImGui calls inside `Draw()`; keep the mutation logic in small helpers the test hooks call so
    state changes are exercised without ImGui.

### 2. Register the drawer
`BlueprintEditorBootstrap.CreateNodeDrawerRegistry`: add
`registry.Register(typeof(FunctionCallNode), new FunctionCallNodeDrawer(editService));` alongside the
existing registrations.

## Tests (headless — mirror SpawnEqsSensorNodeDrawerTests + WhenNodeEditorWiringTests)
- `Handles` true for `FunctionCallNode`, false otherwise.
- `CreateSession` returns a session; `SelectFunctionGraphForTest(g)` sets `TargetGraphId == g`, clears
  `TargetTypeId`/`MethodName`, and `IsDirty == true`.
- `SetClrTargetForTest("T","M",true)` sets the CLR fields + `IsPure`, clears `TargetGraphId`, `IsDirty`.
- `MarkDirty` is invoked on the injected `IEditService` (use a spy `IEditService`).
- Registration: `CreateNodeDrawerRegistry(...)` resolves a `FunctionCallNodeDrawer` for a `FunctionCallNode`
  (mirror the existing registry-completeness test).
- Pump: build a `BlueprintDetailsWindow` with the registry + an asset containing a Function graph and a
  selected `FunctionCallNode`; assert `ResolveSession()` returns non-null and `ResolvedDrawerKind ==
  typeof(FunctionCallNodeDrawer)` (mirror how existing wiring tests drive `ResolveSession`/selection).

## Verification (paste real output)
1. `dotnet build IOS-IG-SimHost.sln` — 0 errors; 0 new warnings in touched projects.
2. New drawer tests green; existing drawer/wiring tests green.
3. Full `Hrot.Blueprints.Tests`: failures a SUBSET of the pre-existing **7**, 0 new, no golden changed.
4. `Hrot.ClusterRunner.Integration.Tests --filter FullyQualifiedName~EditorSubsystemBoot` → 10/10.

## Report
`.dev/blueprint-finalize/reports/BATCH-03D1-REPORT.md`: drawer/session added (file:line), registration,
how edits mark dirty, the test names + output, full-suite classification, and an explicit note that a
richer CLR type/method *browser* (vs the type/method text fields) is deferred as an enhancement (no CLR
method catalog exists today — `StaticTypeRegistry` lists primitives only). Note the `Draw()` body needs a
manual visual smoke (later). **Do not commit** — lead reviews/commits.
