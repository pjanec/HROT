# BATCH-01 Review

**Batch:** BATCH-01
**Reviewer:** Development Lead
**Date:** 2026-05-03
**Status:** APPROVED

---

## Summary

All 4 Phase 1 tasks completed. JSON converters moved to Fdp.Core, registry singletons created
and frozen, aesthetic formatter extracted, and 6 callers refactored. Build is clean on affected
projects; 21 new tests pass.

---

## Issues Found

### Issue 1: Namespace in TASK-DETAIL diverges from project convention (P3 — Documentation)

**File:** `.dev/dump-diag/TASK-DETAIL.md`
**Problem:** Task spec says `Fdp.Toolkits.Serialization` but the Fdp.Toolkits project uses
`Fdp.Toolkit.*` (singular) convention. Developer correctly matched the codebase convention.
The task spec was wrong.
**Fix:** No code change needed. Log as a documentation note.

### Issue 2: HrotSerializerOptions retains CamelCase policy (P3 — Acceptable)

**File:** `Hrot/Engine/Hrot.Core/HrotSerializerOptions.cs`
**Problem:** Cannot use `FdpJsonOptionsRegistry.Indented` directly — must copy and add CamelCase.
The options instance is no longer frozen (no `MakeReadOnly()` after copy).
**Fix:** The copy constructor + `static readonly` field ensures effective immutability (no public
setters exposed). Acceptable for now. The registry singletons themselves remain frozen.
Note in DEBT-TRACKER at P3.

---

## Test Quality Assessment

Tests verify actual behaviour:
- `FixedString64Converter_Serialize_ReturnsQuotedString` checks the literal string `"\"hello\""`
- `StrictStringEnumConverter_Deserialize_IntegerThrows` actually invokes `JsonException`
- `DefaultRelaxed_IsFrozen_MutationThrows` actually mutates and expects `InvalidOperationException`
- `JsonAestheticFormatter` tests check collapsed vs expanded array output correctly

Tests are behavioural, not shallow. Quality is acceptable.

---

## Verdict

**Status:** APPROVED

All requirements met. Ready to merge.

---

## Commit Message

```
feat: JSON serialisation foundation - centralise converters and options (BATCH-01)

Completes DD-P1-T01, DD-P1-T02, DD-P1-T03, DD-P1-T04

Consolidates scattered JSON options instances, fixes the FixedString64 struct
serialisation bug, and extracts the numeric-array aesthetic formatter.

DD-P1-T01 (Converters to Fdp.Core):
- VectorArrayConverters, FixedStringConverters, StrictStringEnumConverter moved to
  Fdp.Core.Serialization.Converters
- [Obsolete] forwarding subclasses left in ScenarioJsonConverters.cs

DD-P1-T02 (FdpJsonOptionsRegistry):
- DefaultRelaxed and Indented frozen singletons in Fdp.Core.Serialization
- TypeInfoResolver set before MakeReadOnly() to satisfy .NET 8 requirement

DD-P1-T03 (JsonAestheticFormatter):
- FlattenNumericArrays extracted from ScenarioFileService into Fdp.Toolkit.Serialization
- ScenarioFileService delegates to new public static method

DD-P1-T04 (Callers refactored):
- 6 callers updated: FdpAutoSerializer (x2), MetadataSerializer, HrotSerializerOptions,
  OrchestrationJsonOptions, EventBrowserPanel, EntityJsonDumper

Tests: 21 new tests (ConverterTests, FdpJsonOptionsRegistryTests, JsonAestheticFormatterTests)
```

---

**Next Batch:** BATCH-02 — Diagnostic Data Service Interfaces and Implementations (Phase 2)
