# BATCH-03C — Editor projection: Entry/Return value pins + FunctionCall mirrors in-blueprint graph signature

> **Coder contract:** read `.dev/.guides/DEV-GUIDE_claude.md` first. Verify-first, cite `file:line`,
> never fake a pass, implement→build→test→fix to green. **Codebase Memory MCP first**. Project
> `D-Work-IOS-IG-SimHost-FDP-2`. No `search_code`/tree grep.

## Mission

Complete the EDITOR side of the in-blueprint function-graph call feature (compiler core landed in
BATCH-03A). Project canonical pins so the canvas shows, for a `GraphKind.Function` graph:
- the **Entry** node's **output value pins** from `Graph.Inputs`,
- the **Return** node's **output value pin** from `Graph.Outputs[0]`,
- a **FunctionCall** node with a non-empty `TargetGraphId` **mirroring the target graph's signature**
  (data-IN per Input, data-OUT for the single Output).

These projected pins MUST match exactly what the BATCH-03A compiler consumes (names + directions), or the
wires won't bind. Pins remain **projection-only** (never persisted). **Scope: local function-graph
projection only.** The `CallPeerBlueprintNode` arg-pin work (extend `BlueprintSignature` + sibling
registry) is a SEPARATE batch — do NOT do it here.

## Compiler contract (lead-verified — the projected pins MUST satisfy these)
- **Entry inputs:** Stage5 reads `EventEntryNode` **data-OUT** pins (`!IsExec && Direction=="Out"`) and
  matches each to `Graph.Inputs` **by name** (OrdinalIgnoreCase; ordinal fallback) → `IrOp_ReadInputArg`
  (`Stage5_Schedule.cs` ~1157-1189). So project one data pin per `Graph.Inputs[i]` with
  `Direction="Out"`, `Name = inp.Name`, typed from `inp.Type.TypeId`.
- **Return output:** Stage5 reads `rn.Pins.FirstOrDefault(!IsExec && Direction=="Out")` as the returned
  value (`BuildReturnTerminator`, `Stage5_Schedule.cs` ~881-897). So the Return node's value pin has
  `Direction="Out"` (NOT "In") — this is compiler-dictated, same convention as the GetVariable value pin.
  Project one data pin `Direction="Out"`, `Name = Outputs[0].Name`, typed from `Outputs[0].Type.TypeId`.
- **FunctionCall(TargetGraphId):** Stage5 consumes the node's data-IN pins **positionally**
  (`ResolveAllDataInputs`) as call args, and the **first** data-OUT pin as the return slot
  (`Stage5_Schedule.cs` ~642-679). So project data-IN pins in `target.Inputs` order (names = input names
  for readability) and one data-OUT pin for `target.Outputs[0]`.

## Changes (all in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/`)

### 1. Thread the containing graph into projection
`Host/NodePinSchema.cs` — add an optional trailing parameter to `GetCanonicalPins`:
`Graph? containingGraph = null`. Update the two real call sites that have the graph in hand to pass it:
- `Host/BlueprintGraphModel.cs:145` (`Rebuild`) — pass `_graph`.
- `Host/BlueprintCommandSink.cs:209` (`ApplyPinIds`) — pass `_graph` (constructor field).
- `Host/BlueprintNodeCatalog.cs:186` (catalog) — leave as-is (no graph; null → graceful fallback).
Verify these are the only call sites (the research found exactly these three + tests). Cite.

### 2. EventEntryNode arm
Replace `EventEntryNode => ExecOnly("Out")` with a helper:
- If `containingGraph?.Kind == GraphKind.Function` and `containingGraph.Inputs.Count > 0`:
  emit `MakeExec("Out","Out")` then, for each `inp` in `containingGraph.Inputs`,
  `MakeData(inp.Name, "Out", inp.Type?.TypeId ?? "System.Object")`.
- Else: `ExecOnly("Out")` (unchanged behavior for Event/AiPrimitive graphs and inputless functions).

### 3. ReturnNode arm
Replace `ReturnNode => ExecOnly("In")` with a helper:
- If `containingGraph?.Kind == GraphKind.Function` and `containingGraph.Outputs.Count > 0`:
  emit `MakeExec("In","In")` then `MakeData(Outputs[0].Name, "Out", Outputs[0].Type?.TypeId ?? "System.Object")`.
  (Direction MUST be `"Out"` per the compiler contract above.)
- Else: `ExecOnly("In")`.
- Note in the helper's XML-doc WHY the value pin is `Direction="Out"` (compiler reads
  `!IsExec && Direction=="Out"`; mirrors GetVariable convention). Only project the single first output
  (BATCH-03A is single-output; multi-output is a later batch).

### 4. FunctionCall(TargetGraphId) arm
In `GetCanonicalPins`, BEFORE the existing `FunctionCallNode fc => FunctionCallPins(fc)` routing, add a
guard: if `fc.TargetGraphId` is non-empty and `asset != null`, parse it and find
`target = asset.Graphs.FirstOrDefault(g => g.Id == guid && g.Kind == GraphKind.Function)`. If found, return
a new `FunctionGraphCallPins(fc, target)`:
- if `!fc.IsPure`: `MakeExec("In","In")`, `MakeExec("Out","Out")`.
- one `MakeData(inp.Name, "In", inp.Type?.TypeId ?? "System.Object")` per `target.Inputs` (declaration order).
- if `target.Outputs.Count > 0`: `MakeData(target.Outputs[0].Name, "Out", target.Outputs[0].Type?.TypeId ?? "System.Object")`.
If `TargetGraphId` is empty / unparseable / target not found → fall through to the existing CLR-reflection
`FunctionCallPins(fc)` (graceful, unchanged). Do NOT throw.

Keep the existing CLR `FunctionCallPins` path intact for CLR-method FunctionCalls.

## Tests
Extend `Hrot.Blueprints.Tests/Host/NodePinSchemaEnrichmentTests.cs` (follow its inline-asset pattern):
- EventEntryNode in a Function graph with 2 Inputs → exec-Out + 2 data-OUT pins named per input, right types.
- EventEntryNode in a non-Function (Event) graph or with no inputs → exec-only (unchanged).
- ReturnNode in a Function graph with 1 Output → exec-In + 1 data-OUT pin (Direction=="Out") named/typed
  from Outputs[0]; ReturnNode with no outputs → exec-only.
- FunctionCall with TargetGraphId → exec In/Out + data-IN per target Input + 1 data-OUT (verify names,
  directions, types, and order). Also a pure (IsPure) variant → no exec pins. Also unknown-GUID → falls
  back to CLR path (no throw).
- A round-trip name/direction check asserting the projected Entry/Return/Call pins satisfy the compiler's
  selectors (e.g. Entry has data pins with Direction=="Out"; Return value pin Direction=="Out"; Call has
  data-IN pins and a data-OUT pin) — so the projection provably matches BATCH-03A's consumption.

## Verification (paste real output)
1. `dotnet build IOS-IG-SimHost.sln` — 0 errors; 0 new warnings in touched projects.
2. New + existing NodePinSchema tests green.
3. Full `Hrot.Blueprints.Tests`: failures a SUBSET of the pre-existing **7** (AiPrimitiveEmitGolden ×2,
   LibraryEmitGolden, LibraryMath snapshot, MoveToAndFire snapshot, ConditionSummary, AllocationFree).
   0 NEW failures, no golden changed. List + classify.
4. `Hrot.ClusterRunner.Integration.Tests --filter FullyQualifiedName~EditorSubsystemBoot` → 10/10.

## Report
`.dev/_DONE/blueprint-finalize/reports/BATCH-03C-REPORT.md`: changes (file:line), the exact pin names/directions
projected for each arm with the compiler selector each satisfies, call-site updates, test names + output,
full-suite classification, confirmation that CallPeerBlueprint arg pins were left for a later batch.
**Do not commit** — lead reviews/commits.
