# BATCH-03B — Compiler: function-graph-call validation hardening

> **Coder contract:** read `.dev/.guides/DEV-GUIDE_claude.md` first. Verify-first, cite `file:line`,
> never fake a pass, implement→build→test→fix to green. **Codebase Memory MCP first**. Project
> `D-Work-IOS-IG-SimHost-FDP-2`. No `search_code`/tree grep.

## Mission

BATCH-03A landed in-blueprint function-graph calls (`FunctionCallNode.TargetGraphId` → `IrOp_GraphCall`,
emitted as `Func_{name}(...)`), with one safety validator already present:
`V_FunctionGraphCallRules` in `Compiler/Stages/Stage2_Validate.cs` (currently only emits **BP1650** for
latent nodes in a called function graph). Harden the validation so malformed function-graph calls are
caught at compile time (Stage 2) with clear diagnostics instead of producing broken generated C# or
infinite recursion at runtime. **Extend `V_FunctionGraphCallRules`** (do not create a parallel validator).

## Read first
- `Compiler/Stages/Stage2_Validate.cs` — `V_FunctionGraphCallRules` (added in 03A) and the surrounding
  validator patterns; how `ctx.Diagnostics.Add(Diagnostic.Error(...))` is used; how other validators walk
  graphs/nodes/pins. Cite.
- `Compiler/Diagnostics/DiagnosticCodes.cs` — the BP16xx block (BP1650 was added in 03A).
- `Compiler/Stages/Stage5_Schedule.cs` — the `FunctionCallNode ... TargetGraphId` cases (03A) that
  currently emit a **BP4004 warning** for missing/non-Function targets. After this batch, Stage 2 should
  catch those as errors first (Stage 5's warning becomes a defensive fallback).
- How `ParameterDecl.Type` (`BlueprintTypeRef`) is compared / resolved for type compatibility — find the
  TypeRegistry API used elsewhere in Stage2/Stage4 (e.g. `TryResolve`, type-equality helpers). Cite the
  exact API before using it.

## New diagnostics (add to DiagnosticCodes.cs, BP16xx block)
- **BP1651** — FunctionCall target graph not found OR not a `GraphKind.Function` graph.
- **BP1652** — FunctionCall argument count mismatch (caller data-IN pin count ≠ target graph `Inputs.Count`).
- **BP1653** — FunctionCall argument type mismatch (a caller data-IN pin type incompatible with the
  corresponding target `Inputs[i].Type`).
- **BP1654** — Function-graph call cycle detected (direct or transitive recursion among Function graphs).

## Validation rules to add in `V_FunctionGraphCallRules`

For every `FunctionCallNode` with a non-empty `TargetGraphId` (across all graphs in the asset):

1. **BP1651 — target resolution.** Parse `TargetGraphId` as a Guid and find the matching `Graph`. If it
   does not parse, no graph matches, or the matched graph's `Kind != GraphKind.Function` → `Error(BP1651)`
   citing the node + the bad id. (Skip the remaining per-node checks when the target is unresolved.)

2. **BP1652 — arg count.** Count the caller node's data-IN pins (`!IsExec && Direction=="In"`). If that
   count ≠ `targetGraph.Inputs.Count` → `Error(BP1652)` with both counts. (This prevents the CS-level
   arg-count mismatch in generated C#.)

3. **BP1653 — arg type (best-effort, positional).** When arg count matches, for each i compare the
   caller's i-th data-IN pin type to `targetGraph.Inputs[i].Type`. Use the SAME type-resolution/compat
   mechanism the compiler already uses (find it — likely via TypeRegistry / `BlueprintTypeRef` equality;
   cite). Emit `Error(BP1653)` per incompatible parameter (name + expected vs actual). If pin types are
   not yet resolved at Stage 2 (Stage4 does type resolution), and a robust compat check isn't available
   pre-Stage4, do a conservative check (e.g. compare `BlueprintTypeRef.TypeId` strings, treating empty/
   "System.Object" as wildcard) and document the limitation in the report. Do NOT emit false positives —
   when in doubt, do not flag.

4. **BP1654 — recursion/cycle.** Build the directed call graph among Function graphs: an edge `A → B`
   exists when graph A contains a `FunctionCallNode` whose `TargetGraphId` resolves to graph B. Detect any
   cycle (including a self-edge A → A) via DFS/colour-marking. For each graph that participates in a cycle,
   emit one `Error(BP1654)` naming the cycle path (e.g. "A → B → A"). This is essential: function-graph
   calls compile to direct synchronous C# method calls, so a cycle would stack-overflow at runtime.
   Emit the error once per detected cycle (dedupe), not once per node.

Keep BP1650 (latent) as-is.

## Tests (negative tests — assert each diagnostic)
Add to the BATCH-03A test file or a new `BATCH03B_FunctionGraphCallValidationTests.cs`:
- BP1651: FunctionCallNode.TargetGraphId pointing at a non-existent guid → BP1651; and pointing at an
  Event (non-Function) graph → BP1651.
- BP1652: caller with 1 data-IN pin, target graph with 2 Inputs → BP1652.
- BP1653: caller arg pin typed `System.Int32` into a target Input typed `System.Single` (or another clear
  mismatch) → BP1653. (If your conservative check can't distinguish, document why and still include a test
  for a case it CAN catch.)
- BP1654: graph A calls B, B calls A → BP1654 (and a self-recursion A→A case).
- A POSITIVE control: a valid call (matching count + types, no cycle, no latent) → NO BP165x diagnostics
  (reuse/extend the 03A happy-path asset).
Build assets as hand-constructed `BlueprintAsset` object graphs (as 03A tests do). Run validation via the
same entry point the existing Stage2 tests use (find it; cite).

## Verification (paste real output)
1. `dotnet build IOS-IG-SimHost.sln` — 0 errors; 0 new warnings in touched projects.
2. New validation tests green; the 03A tests still green.
3. Full `Hrot.Blueprints.Tests`: failures must remain the SUBSET of the pre-existing **7**
   (AiPrimitiveEmitGolden ×2, LibraryEmitGolden, LibraryMath snapshot, MoveToAndFire snapshot,
   ConditionSummary, AllocationFree). **0 NEW failures, no golden changed.** List + classify the failing set.
4. `Hrot.ClusterRunner.Integration.Tests --filter FullyQualifiedName~EditorSubsystemBoot` → 10/10.

## Report
`.dev/blueprint-finalize/reports/BATCH-03B-REPORT.md`: the rules + codes added (file:line), the type-compat
mechanism used (and any conservatism/limitation in BP1653), the cycle-detection approach, test names +
real output, full-suite failure classification. **Do not commit** — lead reviews/commits.
