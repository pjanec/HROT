# BF-02: SetVariable/GetVariable with `var:`-prefixed VariableId resolves to index -1 (emits uncompilable `s.__var_-1`)

**Type:** Bug fix (single, focused)   **Est:** ~2h
**Onboarding:** `.dev/.guides/DEV-GUIDE.md` (your working contract). ONE objective only — do not touch unrelated files.

## The bug
A blueprint authored with a variable node dragged from the My-Blueprint panel produces a `SetVariable` (or `GetVariable`) node whose `VariableId` is in the form `"var:<Guid>"` (the panel item-id format, e.g. `BlueprintMyBlueprintModel` emits `ItemId = $"var:{v.Id}"`). The compiler then generates uncompilable C#:
```
s.__var_-1 = __t3;   // variable index -1 → CS1061: 'State' does not contain a definition for '__var_'
```
Repro asset (user experiment, do NOT commit it): `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Count5.bp.json` — its first `SetVariable` uses the bare GUID `a0000006-…` (works) but its second `SetVariable` uses `"var:a0000006-…"` (fails). Both forms are valid authoring.

## Root cause (already diagnosed — just fix)
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs`, method `FindVariableIndex(string variableId)` (~line 1535) calls `Guid.TryParse(variableId, …)` on the RAW string. For a `"var:<Guid>"` id the parse fails, the name fallback also fails, and it returns **-1** → `EmissionContext.VarFieldName(-1)` produces `__var_-1`.

Other resolvers already strip the prefix and work correctly — `Stage0_Rehydrate.ResolveVariableTypeId` (lines 487-490), `Host/NodePinSchema.cs`, `Host/BlueprintNodeModel.cs`. `FindVariableIndex` is the one that was missed (so types resolve but the write/read INDEX does not).

## The fix (prescribed — mirror the existing idiom)
In `Stage5_Schedule.FindVariableIndex`, strip a leading `"var:"` (case-insensitive) BEFORE `Guid.TryParse`, exactly like `Stage0_Rehydrate.ResolveVariableTypeId`:
```csharp
var idStr = variableId.StartsWith("var:", StringComparison.OrdinalIgnoreCase)
    ? variableId.Substring(4)
    : variableId;
```
Then `Guid.TryParse(idStr, …)` and use `idStr` for both the GUID and the name-fallback comparisons. Do NOT change `EmissionContext`, the IR ops, or any other resolver. Keep the existing search order (Variables → WorkingState → Parameters).

## Test (prescribed — assert the discriminating behavior, do not invent your own)
Add ONE test (new file `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage5VarPrefixResolutionTests.cs`, or co-locate with existing Stage5 tests if a suitable file exists — match the existing compiler-test pattern, e.g. `BcpBatch02BlueprintTests` which already tests `var:`-prefixed GetVariable type resolution):
- Build an **Instance** blueprint asset with one declared variable (e.g. `Count`, System.Int32), an `EventEntry → SetVariable(VariableId = "var:<the variable's Guid>", value literal 7) → Return`.
- Compile it (full pipeline through Stage5/Stage7).
- **Discriminating assertions (BOTH):**
  1. The generated source for the `SetVariable` must write the real field (`s.Count = …`) and must **NOT** contain `__var_-1` (regression guard for the exact bug).
  2. Prefer a behavioral check if the fixture supports it: compile + run one tick via `BlueprintTestFixture` and assert the entity's `Count` variable equals `7` after the tick (proves the index resolved correctly, not just the text). If a runtime check is impractical for a pure-`var:`-prefix unit test, the generated-source assertion in (1) is acceptable as the primary, but add a second assertion that `FindVariableIndex`-equivalent resolution returns the correct index (e.g. assert the emitted field name equals the declared variable name).
- Also add a parallel case for `GetVariable` with a `var:`-prefixed id resolving to the correct read (optional but preferred).

## Do-not-stop-until-green (MANDATORY)
Run the FULL affected suite yourself and loop until `Failed: 0` (no `BLUEPRINT_REGENERATE_SNAPSHOTS`, no regen flags):
- `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests`
The ONLY acceptable remaining failures are these documented pre-existing reds (do NOT "fix", suppress, exclude, or weaken anything to pass): `AiPrimitive_EmitMatchesGoldenSource` (×2), `Stage8_PdbContainsEmbeddedSource`, `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb`, `TickFrame_1000Frames_AllocatesZeroBytes`, `MoveToAndFire_GeneratedSource_Snapshot`, `WhenNode_ZeroAllocOnHotPath`. Any NEW failure is yours — root-cause and fix, re-run the whole suite, loop until green. A transient `MapKeyboardKey.idl` (DDS codegen) build error → just re-run.

## Constraints
- Touch ONLY `Stage5_Schedule.cs` (the prefix-strip in `FindVariableIndex`) and the new test file. Do NOT edit other resolvers, do NOT commit `Count5.bp.json` or any `.bp.json`, do NOT exclude assets, do NOT suppress diagnostics, do NOT weaken existing tests.
- Do NOT commit. Write a short report to `.dev/_DONE/blueprint-dbg-2/reports/BF-02-REPORT.md` (the change, the test, the exact `dotnet test` summary line). The lead reviews and commits.
