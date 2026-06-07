# BATCH-09 Review

**Reviewer:** Dev Lead
**Date:** 2025
**Status:** APPROVED

---

## Scope Covered

- TASK-CP-000: Catalog interface stubs (`IEngineEventCatalog`, `IChannelCommandCatalog`, `IWaitPrimitiveCatalog`, `INodeRegistry`, `ITypeRegistry`, 3 `BuiltIn*` empty implementations)
- TASK-CP-001: Compiler infrastructure skeleton (42 files: full IR hierarchy, DiagnosticCodes, FnvHasher, Sanitizer, CompileOptions, CompileResult, BlueprintSignature, stage stubs, lowering stubs, Roslyn stubs, Determinism stubs)

---

## Build & Test Results

- **Build:** 0 errors, 0 warnings
- **Tests:** 160 pass, 3 skip, 0 fail (baseline unchanged)

---

## Critical Constraint Verification

| Constraint | Result |
|---|---|
| `CompileOptions.SiblingSignatures` (Patch 1, NOT SiblingAssets) | PASS — `SiblingAssets` absent |
| `IrOp_ReadInstanceVersion` in hierarchy (Q-18.1) | PASS |
| `DiagnosticCodes` is static class with `public const string` fields | PASS |
| `FnvHasher` is deterministic (no random seed) | PASS |
| `Sanitizer.GeneratedFileName` class name format `{Name}_{Id:X8}_Bp` (Q-18.4) | PASS |
| All IR types are `record` or `sealed record` | PASS |
| `CompilerMode` enum reuse (Fdp.Toolkit.Blueprints) avoids duplication | PASS — valid deviation |

---

## Deviation Assessment

### Deviation 1: BuiltIn* placed in Hrot.Blueprints.Core rather than Fdp.Toolkits
**Verdict: ACCEPTED.** Fdp.Toolkits → Hrot.Blueprints.Core would be a circular dependency. The instructions stated to place them in Fdp.Toolkits but acknowledged to use empty stubs if engine types don't exist. Placing them alongside the interfaces in Core is architecturally cleaner for now; they can be moved in a future refactor if needed.

### Deviation 2: `CompilerMode` reused from Fdp.Toolkit.Blueprints
**Verdict: ACCEPTED.** Avoids duplicate enum definition. The enum already has the correct values (`Release, Debug, Trace`).

### Deviation 3 & 4: Root compiler stubs kept for backward compat
**Verdict: ACCEPTED.** Test fixture stability is the priority. The old stub wrappers are clearly documented. Future TASK-CP-005/CP-006 will migrate the fixture to use the real compiler.

### Deviation 5: BlueprintIdHash.Compute not updated
**Verdict: ACCEPTED.** Pre-existing implementation uses correct FNV-1a 32-bit constants — functionally equivalent to `FnvHasher.Hash32`.

### Deviation 6: `GeneratedFileName` added to CompileResult
**Verdict: ACCEPTED.** Minor convenience property, non-breaking. Does not affect any success conditions.

---

## Test Quality Assessment

No new tests were written in this batch (catalog stubs + IR scaffolding only). All test quality criteria come from the unchanged baseline. The test suite for the compiler itself is deferred to TASK-CP-006.

---

## Conclusion

BATCH-09 is **APPROVED**. The scaffold is correctly structured, all constraints are met, and the codebase baseline is preserved.
