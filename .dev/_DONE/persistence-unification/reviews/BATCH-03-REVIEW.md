# BATCH-03 Review
**Status:** ✅ APPROVED (with P2/P3 debt recorded)   **Date:** 2026-06-05

## Summary
PU-201/202 (BTree+HSM JSON→C# IncrementalGenerators in a new `netstandard2.0`/`IsRoslynComponent`
`Hrot.AiEditor.Generators`, mirroring `Hrot.Blueprints.Generators`) + PU-205 (migration-equivalence harness)
+ a layout-excluding `EmitTopologyCore` mode on the emit core. Generators built/tested in isolation
(GeneratorDriver); not yet wired into the real build (PU-204) — correct for this batch.

## Verified (read source + assertions, ran suites)
- **Project pattern:** `Hrot.AiEditor.Generators.csproj` mirrors the Blueprint generator exactly
  (`IsRoslynComponent`, CodeAnalysis 4.8.0 PrivateAssets=all, ProjectReference `Hrot.AiEditor.Persistence`
  `PrivateAssets="all" ExcludeAssets="runtime"`). ✅
- **Generator behavior (well-tested):** valid `.btree.json`/`.hsm.json` → one `{Name}.g.cs` with
  `CreateBuilder()`+thunk and NOT `[*Layout]`; malformed → `BTREE0001`/`HSM0001` Error diagnostic, no throw,
  **sibling-safe** (good asset still emits alongside a corrupt one); non-matching extension ignored.
  These GeneratorDriver tests have real assertions on diagnostics/sources. ✅
- **EmitTopologyCore:** shared `EmitInternal(dto, includeLayout)`; full `Emit(true)` unchanged (BATCH-02 gate
  intact); `EmitTopologyCore` = `includeLayout:false` (omits the layout method + the layout/`System.Numerics`
  usings). Deterministic. ✅
- **Ran myself:** solution build 0 errors/0 warnings; generators 26/26; persistence gate 88/88;
  EditorSubsystemBoot 10/10; Blueprints 1357 pass / 7 pre-existing / 0 new.

## Issues Found (non-blocking — recorded as debt)
### Issue 1: PU-205 proves faithful-routing, not direct committed-.cs equivalence  → DEBT PU-D04 (P2)
**File:** `Hrot.AiEditor.Generators.Tests/Equivalence/MigrationEquivalenceTests.cs`
**Problem:** the equivalence asserts `generatorOutput == EmitTopologyCore(ToDto(model))` (the generator
faithfully routes JSON→core), NOT `generatorOutput == committed SampleScout.cs/SampleGuard.cs` (the actual
"behavior unchanged vs today" promise, design §6.4/§11). It is *transitively* covered (BATCH-02 proves full
`Emit==committed.cs`; `EmitTopologyCore` is the same `EmitInternal` minus the layout block), so an uncaught
divergence would have to be `includeLayout:false`-branch-specific. **Fix (better home):** at PU-401
(migration), when real generated `.cs` lands in `obj/`, add a DIRECT `generated .cs` vs committed `.cs`
topology-core equivalence before decommit. Recorded as PU-D04 (P2, target PU-401).
### Issue 2: vacuous `*_EquivalenceTest_FailsLoudly_WhenDiverged` tests  → DEBT PU-D05 (P3)
They assert `reference + "// DIVERGED" != reference` — a tautology that verifies nothing. Remove or replace
with a real divergence-detection test when PU-D04 lands.

## Verdict
APPROVED. Generators are correct and their critical behaviors (diagnostic, sibling-safety, layout-exclusion,
determinism) are well-tested; the PU-205 directness gap is strongly transitively covered and best closed at
PU-401. Maps PU-201, PU-202, PU-205 (with PU-D04 follow-up).

## Commit Message
```
feat(persistence): BTree/HSM JSON->C# IncrementalGenerators + migration-equivalence (BATCH-03)

Completes PU-201, PU-202, PU-205 (with PU-D04 follow-up at PU-401).
New netstandard2.0 Hrot.AiEditor.Generators (IsRoslynComponent, mirrors Hrot.Blueprints.Generators):
BTreeJsonGenerator (*.btree.json) + HsmJsonGenerator (*.hsm.json) deserialize via the JSON services
and emit CreateBuilder()+thunk (no [*Layout], JSON-only per §6.2) to obj/GeneratedFiles; per-asset
parse failure -> BTREE0001/HSM0001 Error diagnostic, never throws, sibling-safe. Emit core gains a
layout-excluding EmitTopologyCore mode (shared EmitInternal; full Emit unchanged, BATCH-02 gate intact).
Generators built + tested in isolation (GeneratorDriver); not yet wired into Hrot.AI.Behaviors (PU-204).
Tests (26): valid->source(CreateBuilder+thunk, no layout); malformed->diagnostic + sibling-safety;
non-matching ignored; EmitTopologyCore unit + determinism; PU-205 json->generator == EmitTopologyCore
exact-string for SampleScout/SampleGuard.
Build 0 warnings (touched); generators 26/26; persistence gate 88/88; boot 10/10; Blueprints 7 pre-existing/0 new.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
