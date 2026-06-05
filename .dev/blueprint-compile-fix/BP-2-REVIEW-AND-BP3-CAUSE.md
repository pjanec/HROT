# BP-1 result, BP-2 review (FAILED), and the confirmed BP-3 cause
Date: 2026-06-05 (lead review)

## BP-1 — SUCCESS (committed ebd8e61d) + it disproved the polymorphism guess
De-swallowing the generator's parse/compile catches immediately revealed the REAL Full-Rebuild failure:
```
BP0002 ... Detail: System.IO.FileNotFoundException: Could not load file or assembly
'Fdp.Toolkits, Version=0.1.1.0' ...
```
So **BP-3's cause is NOT System.Text.Json polymorphism** (my and the architect's earlier guess). It is:
`BlueprintJsonServices.Deserialize`, when run in the **netstandard2.0 Roslyn analyzer host**, triggers loading
**`Fdp.Toolkits`**, which isn't available in that host → FileNotFoundException → FailedParse. BP-3 must make the
generator's deserialization not depend on `Fdp.Toolkits` being loadable in the analyzer (e.g., a converter /
type-touch in the deserialize path pulls in Fdp.Toolkits; isolate or stub it for the generator).

## BP-2 — FAILED REVIEW (not committed; Stage0 wiring disabled, code preserved for rework)
**Symptom:** with `Stage0_Rehydrate` wired into `BlueprintCompiler.Compile`, the Blueprints suite went from the
**8 baseline** (= 7 pre-existing DEBT-006/ConditionSummary/AllocationFree + `CountingDemo_PinsStripped` which
*requires* the feature) to **27** — i.e. **~20 NEW Compile-path regressions** (RecipeIntegrity ×5, CoverAwarePatrol
E2E ×4, HotReload Alc/QuickReload/FailureRollback/RegistrarInjection/PdbLoad/AiPrimitiveReload, MoveToAndFire
reload/tick ×3). The coder's "0 regressions" claim was wrong.

**Isolation (verified):** commenting the single `Stage0_Rehydrate.Run` line at `BlueprintCompiler.cs:28` →
27 drops back to 8. So Stage0 is the sole cause. The golden/snapshot failures (AiPrimitiveEmitGolden,
LibraryEmitGolden, LibraryMath/MoveToAndFire snapshots) are **pre-existing DEBT-006** — they call Stage2-5
directly (bypassing Compile/Stage0), so they're unaffected by BP-2.

**Root cause (verified via BP-1 de-swallow on a failing test):** the regressing tests fail with
`BlueprintCompileException` — the EMITTED C# is malformed:
```
CS0400 'op_Eq_Byte' could not be found in the global namespace
CS0400 'op_Eq_NodeStatus' ...   CS0103 'MoveTo' does not exist   CS0400 'LocomotionChannel' ...
```
So Stage0's rehydration produces **incorrect pin types/structure** for several node kinds (byte/NodeStatus
comparisons, ChannelCommand/MoveTo, locomotion), which makes the downstream emitter generate references to
undefined operator-helpers / channel types → in-memory Roslyn compile fails. The keystone `CountingDemo`
(simple data-flow: EventEntry/SetVariable/GetVariable/Literal/FunctionCall/Return) happens to rehydrate
correctly, but the registry shapes + dynamic derivation are **wrong/incomplete** for the broader node set.

**Deeper nuance for rework:** before BP-2, these projection-only (`Pins:[]`) assets compiled to a *minimal/
trivially-compiling* form and the runtime tests passed on that. The architect premise ("the compiler can't do
pins-empty at all") is too coarse — exec-flow shapes partly survive; the gap is data-pin connectivity. The
rehydration must produce pins whose **types** match what the emitter expects (so it emits valid comparison /
channel-command code), and must be reconciled with the existing pin-less behavior rather than blanket-applied.

## Recommended path
1. **BP-3 (now well-defined):** fix the generator's `Fdp.Toolkits`-load dependency in the netstandard2.0
   analyzer host (the de-swallowed exception names it exactly).
2. **BP-2 rework (needs architect consult):** the registry must encode CORRECT pin *types* per node kind (the
   `op_Eq_*`/`LocomotionChannel`/`MoveTo` errors show the type info is wrong), and Stage0 must not regress the
   node kinds the compiler already emitted acceptably. Re-consult the architect with THESE empirical errors
   (malformed emit for byte/NodeStatus/channel nodes) — this is more than a one-pass registry fill.

## Working-tree state left by the lead
- BP-1: committed (ebd8e61d).
- BP-2: UNCOMMITTED. `Stage0_Rehydrate.Run` wiring at `BlueprintCompiler.cs:28` is **commented out** so the
  buggy rehydration does NOT run in the editor's Quick Reload. `INodeRegistry`/`BuiltInNodeRegistry`/
  `Stage0_Rehydrate.cs` + 2 BP-2 test files remain for rework (the 2 tests fail while Stage0 is disabled — expected).
- `Count2.bp.json` was moved to `Count2.bp.json.setaside` (it triggered the BP-3 Fdp.Toolkits build failure under
  `Blueprints/`). Restore after BP-3.
