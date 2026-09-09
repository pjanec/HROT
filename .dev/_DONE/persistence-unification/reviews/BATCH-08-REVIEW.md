# BATCH-08 Review (PU-401, Phase 4 part 1)
**Status:** ✅ APPROVED   **Date:** 2026-06-05

## Summary
PU-401 migration-equivalence proof under the user-approved **blob/behavioral** criterion (PU-D06) + migration
JSON generation. Purely additive — **nothing in the live tree/csproj/flushAction changed**; decommit is PU-402.

## Verified (read source + assertions, ran suites myself)
- **`BlobEquivalence` helper** (test-only): genuine structural compare. `BehaviorTreeBlob` — TreeName/Version/
  StructureHash/ParamHash + `Nodes[]` byte-compared via `MemoryMarshal.AsBytes` (per-field diagnostic) +
  MethodNames/FloatParams(exact bits)/IntParams/SubtreeAssetIds. `HsmDefinitionBlob` — full `Header` field-by-
  field + States/Transitions/Regions/GlobalTransitions byte-compared + Action/Guard tables by `FunctionId`.
  Exclusions justified + documented: `[NonSerialized]` CompiledDelegate/DebugMetadata, managed `Metadata`
  sidecar, runtime-linked `FunctionPointer` (0 at compile). ✅
- **Blob-equivalence tests (the PU-D06 criterion)** — NOT tautological: reference blob = committed
  `SampleScout.Build()`/`SampleGuard.Compile()` called directly; regenerated blob = committed→JSON
  (`ToDtoWithTypeNames` for BTree) → `CSharpGeneratorDriver` → **`CompileMultiAndLoad`** (in-memory Roslyn vs full
  runtime refs, collectible ALC) → reflection-invoke generated `Build()`/`Compile()` → `BlobEquivalence.AssertEqual`.
  Both pass ⇒ runtime behavior identical. ✅
- **Divergence sentinels (replace the PU-D05 tautologies, which are DELETED)** — real: BTree mutates Wait
  `Duration 1→99` (ParamHash/FloatParams), HSM mutates `EventId 1→99` (ParameterHash); regenerate; assert
  `AssertEqual` **throws**. (Coder confirmed state-name/IsInitial mutations don't reach the blob → correctly
  chose fields that do; green run confirms it throws.) ✅
- **Migration JSON** round-trips byte-stable (Serialize→Deserialize→Serialize), carries non-empty per-node/state
  layout (X/Y) from `[BTreeLayout]`/`[HsmLayout]` (decommit safety: editor restores layout from JSON via PU-301),
  and (BTree) populated `BlackboardTypeName`/`ContextTypeName`. Written to
  `.dev/_DONE/persistence-unification/migration-artifacts/{SampleScout.btree.json, Machines/SampleGuard.hsm.json}`
  — the EXACT files PU-402 will move into the live tree. ✅
- **Live tree untouched (confirmed):** `git status` shows no `Trees/`/`Machines/`/`.cs`/`.csproj`/`EditorSubsystem`
  changes; no `.json` under live `Trees|Machines/`. ✅
- **ALC hygiene:** collectible ALC + unload + GC-await (DEBT-009 pattern). ✅
- **Ran myself:** build 0 errors/0 warnings; Generators.Tests **41/41** (was 29 → +12); EditorSubsystemBoot 10/10;
  Blueprints 7 pre-existing (DEBT-006)/0 new.

## Issues / Debt
- PU-D04/PU-D05 are now satisfied by the real blob-compare + divergence tests (marked RESOLVED/subsumed under PU-D06).
- The pre-existing byte-identical *string* gate tests (BATCH-03) are RETAINED — they're a stricter check that
  still holds for the topology core and are complementary to the blob compare. Fine.

## Verdict
APPROVED. PU-401 complete: blob/behavioral equivalence PROVEN for both editor-owned assets; migration JSON staged
and validated (layout + type names preserved). De-risks PU-402 (atomic decommit) — the next batch swaps the
staged `.json` into the live tree and removes the `.cs`, with the bridge "tickable" test as the regression anchor.

## Commit Message
```
feat(persistence): prove blob/behavioral migration-equivalence + stage migration JSON (BATCH-08, PU-401)

PU-D06 criterion (user-approved): compile committed .cs AND JSON-regenerated .cs, compare runtime blobs.
Purely additive — live tree/csproj/flushAction UNCHANGED (decommit is PU-402).
- BlobEquivalence test helper: structural compare of BehaviorTreeBlob (Nodes via MemoryMarshal + params/hashes)
  and HsmDefinitionBlob (Header + States/Transitions/Regions/GlobalTransitions + linker FunctionIds); excludes
  [NonSerialized]/managed-sidecar/runtime-linked fields (documented).
- MigrationEquivalenceTests: real blob-equivalence (committed Build()/Compile() vs JSON->generator->in-memory
  Roslyn CompileMultiAndLoad->reflection-invoke) for SampleScout + SampleGuard; real divergence sentinels
  (mutate a behavior-affecting JSON field, assert AssertEqual throws) replacing the two PU-D05 tautologies.
- Migration JSON generated + validated (byte-stable round-trip, carries layout X/Y + BTree BB/Ctx type names),
  staged to .dev/_DONE/persistence-unification/migration-artifacts/ (NOT the live tree).
Generators.Tests 41/41 (+12); build 0/0; boot 10/10; Blueprints 7 pre-existing/0 new. PU-D04/D05 subsumed.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
```
