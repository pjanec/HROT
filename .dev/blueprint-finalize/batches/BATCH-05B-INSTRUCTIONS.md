# BATCH-05B — Math node-palette entries (BlueprintMath in the node picker)

> **Coder contract:** read `.dev/.guides/DEV-GUIDE_claude.md` first. Verify-first, cite `file:line`,
> never fake a pass, implement→build→test→fix to green. **Codebase Memory MCP first**. Project
> `D-Work-IOS-IG-SimHost-FDP-2`. No `search_code`/tree grep. Mostly headless-testable.

## Mission

Surface the `BlueprintMath` functions (BATCH-05, `Fdp.Toolkit.Blueprints.BlueprintMath`) in the blueprint
**node picker** (TAB / wire-drop palette), mirroring the NodeEdit demo's "Math" category
(`FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/FakeBlueprint/FakeNodeCatalog.cs:51-73`). Each math entry, when
chosen, drops a `FunctionCallNode` **pre-configured** to call the corresponding `BlueprintMath` method; its
data pins are then projected by reflection via `NodePinSchema.FunctionCallPins` (already works for a
FunctionCall with `TargetTypeId`+`MethodName`). No new compiler work — this is pure editor palette + the
existing reflection pin projection.

## Read first
- `NodeDrawers/BlueprintNodePaletteEntries.cs` — `All()` yields `NodeKindDescriptor`s via `Make<TNode>(kind,
  display, category, tooltip)` whose `CreateInstance` returns a default node. Quote `Make` + a couple of
  entries + the `Categories` block.
- How the palette is registered/consumed: `BlueprintEditorBootstrap.CreatePaletteRegistry` (or wherever
  `BlueprintNodePaletteEntries.All()` is registered into the `NodeKindRegistry`), and how
  `BlueprintNodeCatalog`/`NodePinSchema` resolve a descriptor's `Kind` → node → pins. Confirm a descriptor
  can create a `FunctionCallNode` (not just parameterless kinds) and that distinct `Kind` keys are allowed
  for the same node Type.
- `Host/NodePinSchema.cs` `FunctionCallPins` / `ResolveMethod` — confirms a `FunctionCallNode` with
  `TargetTypeId="Fdp.Toolkit.Blueprints.BlueprintMath"` + `MethodName="AddInt"` (IsPure=true) projects the
  reflected param/return pins. (BlueprintMath uses DISTINCT method names per function — no overloads — so
  `ResolveMethod`'s first-match is unambiguous.)
- `Assets/Nodes.cs` `FunctionCallNode` (`TargetTypeId`/`MethodName`/`IsPure`/`TargetGraphId`).
- Tests: `Tests/...` palette/catalog tests (e.g. how `BlueprintNodePaletteEntries` or the catalog is tested
  — find an existing palette test to mirror).

## Changes

### 1. Math palette entries
Add `BlueprintMathPaletteEntries` (new file under `NodeDrawers/`) — a static `All()` yielding one
`NodeKindDescriptor` per BlueprintMath function, each with:
- a unique `Kind` (e.g. `"Math.AddInt"`, `"Math.Add"`, `"Math.Dot"`),
- a friendly `DisplayName` (mirror the NodeEdit demo where applicable, e.g. "Int + Int", "Float + Float",
  "Clamp (Float)", "Dot Product"),
- a `Category` (`"Math"`, `"Math/Int"`, `"Math/Compare"`, `"Math/Bool"`, `"Math/Vector"`),
- `CreateInstance = () => new FunctionCallNode { Id = Guid.NewGuid(),
    TargetTypeId = "Fdp.Toolkit.Blueprints.BlueprintMath", MethodName = "<Method>", IsPure = true }`.
Cover the full BlueprintMath set (or, if the count is unwieldy, the NodeEdit-demo set + the int/compare/bool
core — your call, but include `AddInt` since the demo uses it). Add a category constant block if needed.
Add a `Categories.Math*` constants consistent with the existing `BlueprintNodePaletteEntries.Categories`.

### 2. Register them
Register `BlueprintMathPaletteEntries.All()` into the same palette `NodeKindRegistry` that
`BlueprintNodePaletteEntries.All()` is registered into (find that site — likely
`BlueprintEditorBootstrap.CreatePaletteRegistry`). Keep ordering deterministic.

## Tests (headless)
- Each math descriptor's `CreateInstance()` returns a `FunctionCallNode` with the right `TargetTypeId`,
  `MethodName`, `IsPure=true`, and a fresh `Id`.
- For a representative few (AddInt, Add(float), Clamp, Dot), `NodePinSchema.GetCanonicalPins` on the created
  node projects the reflected pins: correct count, names, directions (data-IN per param + one data-OUT
  Return), and CLR types matching the `BlueprintMath` signature. (This proves the picker→node→pins chain.)
- The palette registry, after registration, contains the math kinds (mirror the existing palette/catalog
  test). Deterministic ordering.
- A round-trip sanity: a created `Math.AddInt` FunctionCall node, given two int data-IN links + a SetVariable
  consumer, schedules to an `IrOp_PureCall("Fdp.Toolkit.Blueprints.BlueprintMath.AddInt", ...)` (reuse the
  Stage5 test harness if convenient; optional but valuable).

## Verification (paste real output)
1. `dotnet build IOS-IG-SimHost.sln` — 0 errors; 0 new warnings in touched projects.
2. New palette tests green; existing palette/catalog tests green.
3. Full `Hrot.Blueprints.Tests`: failures a SUBSET of the pre-existing **7**, 0 new, no golden changed.
4. `Hrot.ClusterRunner.Integration.Tests --filter FullyQualifiedName~EditorSubsystemBoot` → 10/10.

## Report
`.dev/blueprint-finalize/reports/BATCH-05B-REPORT.md`: the entries added (file:line) + the function set
covered + categories, the registration site, the pin-projection proof, test names + output, full-suite
classification. Note the picker `Draw()` itself is the existing palette UI (no new ImGui). **Do not commit**
— lead reviews/commits.
