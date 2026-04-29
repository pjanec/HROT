# BATCH-05 Review

**Status: APPROVED**

**Date:** 2026-04-29

---

## Summary

BATCH-05 approved. 135/135 tests pass. FBT-012 (BTreeDefinitionGenerator) and FBT-014 (Phase 2 tests) complete.

---

## Code Review

### FBT-012 — BTreeDefinitionGenerator
✅ `BTreeDefinitionGenerator` class in `Fbt.SourceGen/BTreeDefinitionGenerator.cs` with `[Generator]` attribute.
✅ Scans for `[BTreeDefinition]` methods using same `SyntaxProvider` pattern as `BTreeActionGenerator`.
✅ Tree name extracted from `AttributeData.ConstructorArguments[0].Value as string`.
✅ Tree name sanitized to valid C# identifier (spaces/special chars → `_`).
✅ `BTreeDiag002` warning emitted for invalid methods (non-static, non-`BehaviorTreeBlob`-returning, or parameterized).
✅ Emits `FbtTreeCatalog.g.cs` with `Get{TreeName}()` proxying to annotated method.
✅ No emission when no `[BTreeDefinition]` methods found.
✅ Multiple trees → multiple `Get...()` methods in same `FbtTreeCatalog` class.

### FBT-014 — Phase 2 Tests
✅ `SampleTreeDefinitions.BuildSampleTree()` — correctly annotated, returns `BehaviorTreeBlob`, zero params, static.
✅ `DefinitionGeneratorTests` — 4 tests: non-null blob, matching StructureHash, static class check, method-exists.
✅ `GeneratorOutputTests` — 3 tests: registrar type exists, FbtRegistrar attribute applied, RegisterAll populates registry.
✅ All tests use reflection for generated type access — avoids source-order compiler issues.

---

## Deviations from Spec (FBT-012)
FBT-012 spec requires compile-time static evaluation of the builder call chain via Roslyn Semantic Model. This batch uses a proxy approach (generated method calls the annotated factory method). Recorded as DT-005.

---

## Technical Debt Recorded
| ID | Description | Priority |
|----|-------------|----------|
| DT-005 | FBT-012: Generator proxies annotated method at runtime rather than emitting statically initialized blob data. Implementing full Roslyn-based builder evaluation is deferred. | P2 |

---

## Decision: APPROVED — Proceed to BATCH-06
