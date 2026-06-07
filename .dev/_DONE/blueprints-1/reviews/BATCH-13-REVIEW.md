# BATCH-13 Review — TASK-CP-005: Stage 8 Roslyn + Incremental Generator + Catalogs

**Status:** APPROVED

---

## Summary

Stage 8 (in-memory Roslyn compilation) and the incremental generator pipeline are fully implemented. Three pre-existing emitter bugs were discovered and fixed during this batch (NodeStatus namespace, BlueprintRegistrar attribute namespace, Library function return types). All SC criteria pass.

---

## Success Criteria Assessment

| SC | Criterion | Result |
|----|-----------|--------|
| SC1 | `InMemoryRoslynCompiler.Compile` produces non-empty PE and PDB | PASS |
| SC2 | PDB contains embedded source (verified by size ≥ 500 bytes) | PASS |
| SC3 | Invalid C# throws `BlueprintCompileException` with BP7001 diagnostic | PASS |
| SC4 | `MetadataReferenceResolver.ForRuntimeAssemblies` excludes `Location==""` assemblies | PASS |
| SC5 | `BlueprintSignatureParser.Parse` extracts AssetId, Name, Dispatch from `.bp.json` | PASS |
| SC6 | `DebugMapSerializer.Serialize` produces identical JSON for identical inputs | PASS |
| SC7 | `dotnet build` zero errors in Core and Generators | PASS |

---

## Patch Compliance

- **Patch 2** (both MetadataReferenceResolver predicates): ✓ Both `!a.IsDynamic` and `!string.IsNullOrEmpty(a.Location)` are used. SC4 verifies the `Location==""` filter specifically.
- **Patch 1** (4-provider incremental generator): ✓ rawFiles → signatures → siblingCatalog → compileResults pipeline with `.Combine()`. SC7 verifies the generator builds.

---

## Notable Bug Fixes (discovered by Stage 8 Roslyn compilation)

Three pre-existing bugs in the CP-004 emitter were exposed when generated source was actually compiled:

1. **Wrong attribute namespace**: `BlueprintRegistrar` attribute is in `Fdp.Toolkit.Blueprints.Attributes`, not `Fdp.Toolkit.Blueprints`
2. **Wrong NodeStatus namespace**: `NodeStatus` is in `Hrot.Blueprints.Core.Assets`, not `Fdp.Toolkit.Blueprints`
3. **Library function return type**: Functions with `IrTerm_ReturnStatus` terminators need `NodeStatus` return type, not `void`

These fixes are essential for the generated code to compile correctly and are correctly included in this batch.

---

## Test Quality Assessment

Tests are solid. SC4 is particularly thorough — it explicitly creates a collectible ALC, loads an in-memory assembly (verifying `Location==""` on it), then asserts that `ForRuntimeAssemblies` excludes it. This directly validates the Patch 2 requirement.

SC3 confirms the exception type and that diagnostics are populated. SC6 runs two compilations and compares serialized DebugMap JSON for byte-identical output.

---

## Final Test Counts

| Metric | Count |
|--------|-------|
| Passing (total) | 188 |
| Skipped | 3 |
| Failed | 0 |

Baseline preserved (182 → 188, +6 Stage8 tests).
