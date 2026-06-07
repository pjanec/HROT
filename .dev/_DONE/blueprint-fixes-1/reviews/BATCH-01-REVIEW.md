# BATCH-01 Review

**Batch:** BATCH-01  
**Reviewer:** Development Lead  
**Date:** 2026-05-31  
**Status:** APPROVED

---

## Summary

9 compiler defects fixed (3 Critical, 2 High, 3 Medium, 1 Low). 19 new tests added. 823/831 passing.

---

## Issues Found

No issues found. All fixes are correct and tests are high quality.

---

## Test Quality Assessment

Tests pass the quality bar:

- **BPF-014**: Filters to non-comment lines before checking for `s.Cursor.WaitUntilTime`; separately asserts `ws.__waitUntilTime` is absent from non-comment lines. Would catch the bug if reintroduced.
- **BPF-015**: Checks non-comment lines only; additionally verifies the legacy `// [DebugProbe] NodeEnter` comment form is absent. Verifies semicolon termination.
- **BPF-016**: Locates the `Event_OnHit` declaration specifically (excluding `_Thunk`); checks for `deltaTime` in the parameter list. Call-site check also excludes declarations and thunks.
- **BPF-019**: Builds multi-block graphs with 2-Delay chain and Branch nodes; asserts `IrTerm_Return`/`IrTerm_ReturnStatus` terminator types on specific blocks -- would catch wrong-block resolution.
- **BPF-020**: Finds non-comment call sites (filtering declaration and thunk lines); asserts legacy comment form absent; verifies semicolon.
- **BPF-039**: Tests residuals are in ascending Guid order; separately tests two different insertion orders produce identical output.
- **BPF-040**: Reference list sorted via `StringComparer.Ordinal`; companion test verifies sorted order.
- **BPF-041**: Extracts embedded source from PDB using `System.Reflection.Metadata` CDI enumeration; strips BOM; asserts content equality. Definitively better than size heuristic.
- **BPF-050**: Runs N=4 parallel compilations against a sequential reference; asserts byte-identical `GeneratedSource` output.

---

## Verdict

**Status: APPROVED**

All requirements met. Ready to merge.

---

## 📝 Commit Message

```
fix: blueprint compiler critical emit defects + determinism (BATCH-01)

Completes BPF-014, BPF-015, BPF-016, BPF-019, BPF-020, BPF-039, BPF-040, BPF-041, BPF-050

Fixes three Critical emit bugs that caused either uncompilable generated C# or
silently dead runtime behavior (debug probes, custom events, LatentDelay resume).
Two High emit bugs fixed (BuildReturnTerminator block resolution, RaiseCustomEvent).
Three compiler determinism/test-quality issues resolved. Parallel-determinism test added.

Compiler fixes:
- BPF-014 (WaitLowering_Instance): resume reads s.Cursor.WaitUntilTime via new IrOp_ReadCursorWaitUntilTime
- BPF-015 (StatementEmitter): DebugProbe.NodeEnter/PinValue emitted as real calls, not // comments
- BPF-016 (InstanceEmitter/StatementEmitter): event-poll includes payload args; deltaTime removed from Event_ signature
- BPF-019 (Stage5_Schedule): BuildReturnTerminator uses current block parameter, not last-allocated
- BPF-020 (StatementEmitter): IrOp_RaiseCustomEvent emits Event_{name}(ref s, ...) real call
- BPF-039 (Stage5_Schedule): GetOrdered residuals appended via .OrderBy(f => f.Id) for stable order
- BPF-040 (MetadataReferenceResolver): ForRuntimeAssemblies sorts by Location (Ordinal)
- BPF-041 (Stage8Tests): PDB test extracts and compares embedded source via Reflection.Metadata
- BPF-050 (CompilerDeterminismTests): parallel-determinism Theory test added (N=4, 3 asset types)

Tests: 823 passing, 8 skipped (up from 804). 19 new tests in 8 new/updated files.
```

---

**Next Batch:** BATCH-02 (Blueprint Debug Map + Debug Protocol)
