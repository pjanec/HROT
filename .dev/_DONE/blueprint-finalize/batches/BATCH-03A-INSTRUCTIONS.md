# BATCH-03A — Compiler core: in-blueprint function-graph calls (Option B foundation)

> **Coder contract:** read `.dev/.guides/DEV-GUIDE_claude.md` first. Verify-first, cite `file:line`,
> never fake a pass, implement→build→test→fix to green. **Codebase Memory MCP first**
> (`search_graph`/`get_code_snippet`). Project `D-Work-IOS-IG-SimHost-FDP-2`. No `search_code`/tree grep.
> This is a COMPILER feature — be conservative and test end-to-end.

## Mission

Enable an **Instance** blueprint to define a local **Function** graph (a private helper with typed
Inputs/Outputs) and **call it** from another graph (e.g. Tick) via a `FunctionCallNode`. This is the
compiler foundation of the user-chosen Option B. Scope of THIS batch: **multi-input, single-output (or
void)** synchronous function-graph calls, plus the safety validation that forbids latent nodes in a
called function graph. (Multi-output values, recursion/arg-type diagnostics, and all editor/UI work are
SEPARATE later batches — do NOT do them here.)

## Verified architecture (lead-confirmed — re-verify, cite)

- `IrGraph` already has `Inputs`/`Outputs` (`List<IrField>`) and `IrGraphKind {Function, Event,
  AiPrimitiveMain, Construction}` (`Compiler/Ir/IrGraph.cs`). `Graph` (asset) has the same
  (`Assets/GraphTypes.cs`).
- **Precedent — Library emits function graphs as methods:** `LibraryEmitter.EmitFunctionGraph`
  (`Compiler/Emit/LibraryEmitter.cs:30-49`) emits `public static {ret} {Name}({inputs})` and calls
  `EmitGraphBody`. Reuse this shape.
- **Instance ignores non-Tick function graphs today:** `InstanceEmitter.EmitClass`
  (`Compiler/Emit/InstanceEmitter.cs:71-87`) emits Event methods + ONE Tick body
  (`EmitTickMethod`, lines 173-177: `Graphs.FirstOrDefault(Kind==Function && Name=="Tick") ??
  FirstOrDefault(Kind==Function)`). Other Function graphs are dropped at emit.
- **Tick method signature (the 7 context params)** — `InstanceEmitter.EmitTickMethod` (~157-181):
  `(ref State s, ISimulationView view, IEntityCommandBuffer ecb, Entity self, float time,
  float deltaTime, uint instanceVersion)`. Function-graph methods MUST thread these same 7 params so the
  body's `IrOp_Self`/`IrOp_Time`/`IrOp_GetComponent`/`IrOp_WriteVariable`/etc. resolve (they reference
  `s`/`view`/`ecb`/`self`/`time`/`deltaTime` by name via `EmissionContext`).
- **Input reads — `IrOp_ReadInputArg(int ArgIndex)`** exists in IR (`Compiler/Ir/IrOperation.cs:10`) and
  renders to the input param name (`StatementEmitter.cs:44-48`, via `ctx.CurrentGraph.Inputs[ArgIndex].Name`).
  **BUT it is NEVER GENERATED** — `Stage5_Schedule.ResolveNodeOutput` has no case for reading an Entry
  node's data-out pin. **This batch must add that generation.**
- **Call-arg precedent:** `IrOp_LibraryCall`/`IrOp_PeerCall` carry `IReadOnlyList<IrValue> Args`
  (positional). `ResolveAllDataInputs` (`Stage5_Schedule.cs:1090-1096`) collects data-IN pins in pin order.
  Rendered positionally (`StatementEmitter.cs:84-106`).
- **Return:** `IrTerm_Return(IrValue?)` renders `return __t{idx};` / `return;`
  (`TerminatorEmitter.cs:22-26`). `BuildReturnTerminator` (`Stage5_Schedule.cs:834-849`) already produces
  `IrTerm_Return(retVal)` for Instance dispatch by resolving the Return node's data pin. Reuse as-is.
- **Latent hazard (HARD constraint):** there is ONE `BlueprintLatentCursor` per instance (a single flat
  `s.Cursor.ResumeAt`). A function graph emitted as a separate method cannot own a cursor. **Latent nodes
  (`LatentDelayNode`, `WaitForChannelNode`, `WaitForEventNode`) inside a CALLED function graph must be
  rejected by validation** (this batch). Forbid, don't try to support.

## Changes

### 1. Asset model — discriminator on FunctionCallNode
`Assets/Nodes.cs` `FunctionCallNode`: add `public string TargetGraphId { get; set; } = "";`. Empty →
existing CLR library call (unchanged). Non-empty (a graph GUID string) → in-blueprint function-graph call.
Backward compatible (existing assets have empty → no behavior change).

### 2. IR — the call op
`Compiler/Ir/IrOperation.cs`: add
`public sealed record IrOp_GraphCall(System.Guid TargetGraphId, IReadOnlyList<IrValue> Args, IrTypeRef ReturnType) : IrOperation;`

### 3. Stage5 — Entry-input binding (generate IrOp_ReadInputArg)
`Stage5_Schedule.ResolveNodeOutput` (~882): add a case for the graph's **entry node** (`EventEntryNode`)
as a data source. When a consumer link's `FromNode` is the EventEntryNode and `FromPin` is a data-OUT pin:
- Resolve the arg index: find `i` where `graph.Inputs[i].Name == sourcePin.Name` (match the Entry's
  data-out pin name to an input parameter name). Fallback to the ordinal position of the pin among the
  Entry node's data-out pins if no name match. Use the AST `Graph.Inputs` (the scheduler's `_graph`).
- Emit `new IrOp_ReadInputArg(i)` with result `AllocValue(pinType)`; cache on the pin.
- Verify-first: confirm the scheduler's graph object exposes `Inputs` (it is the asset `Graph`); confirm
  how `EventEntryNode` is identified as the entry. Cite.

### 4. Stage5 — the call (impure + pure)
- **Impure (exec) path** — in the node-statement scheduler (`EmitNodeStatements`/`ScheduleBlock`, the
  same place the existing `FunctionCallNode fc when !fc.IsPure` library case lives, ~635): add a case
  `FunctionCallNode fc when !fc.IsPure && !string.IsNullOrEmpty(fc.TargetGraphId)` (place it BEFORE the
  existing library `!fc.IsPure` case so the graph-call discriminator wins):
  - `target = _typed.Asset.Graphs.FirstOrDefault(g => g.Id == Guid.Parse(fc.TargetGraphId))`; if not found
    or not `GraphKind.Function`, fall through to a diagnostic (reuse BP4004-style "unknown impure node")
    — do NOT crash.
  - `args = ResolveAllDataInputs(node, stmts)`.
  - `retType = target.Outputs.Count > 0 ? <typeref of target.Outputs[0].Type> : UnknownType` (void-like).
  - `outPin = node.Pins.FirstOrDefault(!IsExec && Direction=="Out")`; emit
    `IrOp_GraphCall(target.Id, args, retType)` with `ResultValue` allocated from retType when there is an
    out pin; cache result on outPin (mirror the existing LibraryCall case exactly).
- **Pure path** — in `ResolveNodeOutput`, mirror the existing `FunctionCallNode fc when fc.IsPure` case
  (~921): when `!string.IsNullOrEmpty(fc.TargetGraphId)`, emit `IrOp_GraphCall` instead of `IrOp_PureCall`.
- Determine the IrTypeRef for `target.Outputs[0].Type` the same way the pipeline builds type refs from a
  `BlueprintTypeRef`/`ParameterDecl` elsewhere (find the existing helper; cite). Reuse it.

### 5. Stage7 — emit Instance function-graph methods
`InstanceEmitter.EmitClass`: after `EmitTickMethod`, add a loop emitting each Function graph that is NOT
the Tick graph as a private static method. Compute the Tick graph id the SAME way `EmitTickMethod` selects
it, and exclude it. New helper `EmitInstanceFunctionMethod(e, asset, graph)` mirroring
`LibraryEmitter.EmitFunctionGraph` but **prepending the 7 context params**:
```
private static {retType} Func_{Sanitize(graph.Name)}(
    ref State s,
    global::Fdp.ModuleHost.Abstractions.ISimulationView view,
    global::Fdp.Interfaces.IEntityCommandBuffer ecb,
    global::Fdp.Core.Entity self,
    float time, float deltaTime, uint instanceVersion{, <one param per graph.Input: $"{CSharpType(f.Type)} {f.Name}">})
{
    <EmitGraphBody(e, asset, graph)>
}
```
`retType = graph.Outputs.Count > 0 ? CSharpType(graph.Outputs[0].Type) : "void"`. Reuse the same
`CSharpType`/`EmitGraphBody`/name-sanitizer helpers used by `LibraryEmitter` and `InstanceEmitter`
(find and reuse — do NOT invent a new sanitizer; cite the existing one). Ensure `EmissionContext.CurrentGraph`
is set so `IrOp_ReadInputArg` renders the right param name (EmitGraphBody already sets it).

### 6. Stage7 — render the call
`StatementEmitter` (alongside the `IrOp_LibraryCall`/`IrOp_PeerCall` cases, ~84): add
`case IrOp_GraphCall op:`:
- `fg = ctx.Asset.Graphs.First(g => g.Id == op.TargetGraphId)` (verify ctx exposes Asset; cite).
- `argList = string.Join(", ", new[]{"ref s","view","ecb","self","time","deltaTime","instanceVersion"}.Concat(op.Args.Select(a => $"__t{a.Index}")))`.
- `call = $"Func_{Sanitize(fg.Name)}({argList})"`; render `var __t{idx} = {call};` when `idx>=0` else `{call};`.
- Use the SAME sanitizer as the method emission so names match.

### 7. Stage2 — forbid latent nodes in called function graphs (safety)
Add a validator (extend `V_LatentRules` or a new `V_FunctionGraphCallRules`): for each `Graph` of
`Kind==Function` that is referenced by some `FunctionCallNode.TargetGraphId` in the asset, if it contains
any `LatentDelayNode`/`WaitForChannelNode`/`WaitForEventNode`, emit an ERROR diagnostic with a NEW code
`BP1650` ("a function graph invoked by FunctionCall must not contain latent nodes; latent execution is only
supported in the top-level Tick/event graphs"). Mirror the existing Library latent rule
(`Stage2_Validate.cs` V_LatentRules, ~438-456 — cite the existing pattern/code). Register the validator
where the others run.

## Tests (end-to-end — this is the proof the feature works)

Add compiler tests in `Hrot.Blueprints.Tests`:
1. **Golden/Schedule IR test** (Stage5): a hand-built Instance asset with a Tick graph that calls a local
   Function graph `Add(int a, int b) -> int` (Inputs a,b; one Output; Entry data-out pins a,b wired to a
   pure add or to a LibraryCall; Return wired to the sum). Assert the scheduled IR contains an
   `IrOp_GraphCall` with 2 args and the right return type, and that the function graph's body contains
   `IrOp_ReadInputArg(0)`/`(1)`. (Follow the existing Stage5_ScheduleTests patterns.)
2. **End-to-end compile-and-run** (the real payoff): build an Instance blueprint whose Tick calls a local
   function graph to compute a value and writes it to a variable `Result`; compile via the real
   Roslyn-compile harness (`BlueprintTestFixture.CompileAndLoad`), attach, tick, and assert `Result` via
   `BlueprintStateView.TryGetField` (this composes with BATCH-04's StateFields). Prove the called function
   actually executed and returned the right value.
3. **Validation test:** a function graph containing a `LatentDelayNode` that is a call target → asserts
   `BP1650`.

Use hand-built assets/builders (the `BlueprintAssetBuilder` / test builders). If the builder can't express
a second Function graph or Entry input pins, construct the `BlueprintAsset` object graph directly in the
test (as other compiler tests do). Cite the harness you used.

## Verification (paste real output)

1. `dotnet build IOS-IG-SimHost.sln` — 0 errors; 0 new warnings in touched projects.
2. New tests green.
3. Full `Hrot.Blueprints.Tests`: failures must be a SUBSET of the known pre-existing set
   (after BATCH-04 the baseline is **7**: AiPrimitiveEmitGolden x2, LibraryEmitGolden, LibraryMath snapshot,
   MoveToAndFire snapshot, ConditionSummary, AllocationFree). **0 NEW failures.** If a golden changes,
   STOP and report (this batch should not change any existing golden — it only ADDS function-method
   emission for assets that have extra Function graphs, and existing goldens have none). List the exact
   failing tests and classify each.
4. `Hrot.ClusterRunner.Integration.Tests --filter FullyQualifiedName~EditorSubsystemBoot` → 10/10
   (project: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/...`).

## Report
Write `.dev/_DONE/blueprint-finalize/reports/BATCH-03A-REPORT.md`: each change with file:line, the IR/emit
shapes produced (paste a snippet of generated C# for the function-method + call site), the e2e test proof,
the BP1650 validation, the full-suite before/after failure list, and any deviation. Note explicitly that
multi-output values, recursion/arg-type diagnostics, and editor/UI work are deferred to later batches.
**Do not commit** — the lead reviews and commits.
