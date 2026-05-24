# BATCH-05 Report

## Summary

Implemented `BTreeDefinitionGenerator` (FBT-012, pragmatic proxy variant) and 7 new source generator tests (FBT-014). All 135 tests pass; build is clean.

## Tasks Completed

- [x] FBT-012: BTreeDefinitionGenerator (pragmatic Lazy/proxy implementation)
- [x] FBT-014: Phase 2 source generator tests

## New Files Created

| File | Purpose |
|------|---------|
| `FDP/ExtDeps/FastBTree/src/Fbt.SourceGen/BTreeDefinitionGenerator.cs` | Second `[Generator]` class; emits `FbtTreeCatalog.g.cs` |
| `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/TestFixtures/SampleTreeDefinitions.cs` | `[BTreeDefinition("Sample_BT")]` fixture method |
| `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/DefinitionGeneratorTests.cs` | 4 catalog generator tests |
| `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/GeneratorOutputTests.cs` | 3 action registrar generator tests |

## Test Results

Total passing: **135 / 135** (128 pre-existing + 7 new)

New tests:
1. `DefinitionGeneratorTests.FbtTreeCatalog_GetSample_BT_ReturnsNonNullBlob`
2. `DefinitionGeneratorTests.FbtTreeCatalog_GetSample_BT_MatchesDirectCompile`
3. `DefinitionGeneratorTests.FbtTreeCatalog_IsStaticClass`
4. `DefinitionGeneratorTests.FbtTreeCatalog_GetSample_BT_MethodExists`
5. `GeneratorOutputTests.GeneratedRegistrar_ContainsBTreeAction_Method`
6. `GeneratorOutputTests.GeneratedRegistrar_IsTaggedWithFbtRegistrarAttribute`
7. `GeneratorOutputTests.GeneratedRegistrar_RegisterAll_PopulatesRegistry`

## Known Deviations

- **DT-005 (FBT-012):** `BTreeDefinitionGenerator` does not statically evaluate the `BTreeBuilder` call chain at Roslyn compile time. The generated `Get{TreeName}()` method simply delegates to the annotated method at runtime. Static Roslyn evaluation of the full builder chain was explicitly out of scope for this batch per the instructions.

## Generator Outputs

Both generators produce output when the test assembly is compiled:
- `FbtActionRegistrar.g.cs` — emitted by `BTreeActionGenerator` (existing, unchanged)
- `FbtTreeCatalog.g.cs` — emitted by `BTreeDefinitionGenerator` (new); contains `GetSample_BT()` discovered from `SampleTreeDefinitions.BuildSampleTree()`

Generated namespace: `Fbt.Tests.Generated` (assembly name `Fbt.Tests` + `.Generated`).

## Developer Insights

**Q1: Issues encountered?**

None. The incremental generator pattern from `BTreeActionGenerator` transferred cleanly. The only design decision was using reflection in tests rather than direct type references — this avoids any potential compilation ordering issues and keeps the tests independent of the exact generated type name at the source level.

**Q2: Design decisions?**

- Identifier sanitizer uses a simple `StringBuilder` loop (no regex dependency) — keeps the generator free of `System.Text.RegularExpressions`.
- `BTreeDiag002` emits a `Warning` (not error) so downstream projects can suppress it if needed; consistent with `BTree001`.
- Tests use `Type.GetType("Fbt.Tests.Generated.FbtTreeCatalog, Fbt.Tests")` reflection because it avoids any source-ordering issue and documents the contract explicitly.

**Q3: Weak points?**

- `StructureHash` equality test (`FbtTreeCatalog_GetSample_BT_MatchesDirectCompile`) calls `BuildSampleTree()` twice (once via catalog, once directly). The hashes match because both code paths are identical; however, if the lambda compiler-generated name ever differs between call sites this test would fail. Using a named static delegate in the fixture would be more robust.
- Generator does not deduplicate methods with the same tree name — last one wins silently.

**Suggested commit message:**
```
feat(fluent-btree): BATCH-05 complete -- BTreeDefinitionGenerator + Phase 2 tests

- Add BTreeDefinitionGenerator ([Generator]) to Fbt.SourceGen:
  emits FbtTreeCatalog.g.cs with Get{TreeName}() proxy methods.
  Validates annotated methods (static, returns BehaviorTreeBlob, 0 params);
  emits BTree002 Warning and skips invalid ones.
  Skips file emission when no valid [BTreeDefinition] methods found.
- Add SampleTreeDefinitions fixture with [BTreeDefinition("Sample_BT")] method.
- Add DefinitionGeneratorTests (4 tests) and GeneratorOutputTests (3 tests).
- All 135 tests pass; build clean.
- DT-005: runtime proxy approach (no Roslyn static evaluation of builder chain).
```
