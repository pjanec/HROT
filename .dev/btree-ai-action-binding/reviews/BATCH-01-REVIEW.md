# BATCH-01 Review
**Status:** ✅ APPROVED (with 2 P2 debts)   **Date:** 2026-06-15

## Summary
S1-0 (`bool [MarshalAs(I1)]`) and S1-1 (read-only hardcoded-DTO reflection in the Variables panel VM) are implemented per spec; `Hrot.Editor.AiShared.Tests` 1100/0 verified by the lead.

## Issues Found
### Issue 1 (P2): S1-0 runtime-layout assertion is non-discriminating
**File:** `Hrot.Editor.AiShared.Tests/Blackboard/BlackboardDtoEmitterTests.cs` (`Emit_BoolField_CarriesMarshalAsI1`).
**Problem:** the Roslyn-compiled layout check uses fields `{int A; bool B; int C}`. Because `C` realigns to 4, `OffsetOf(C)==8` and `SizeOf==12` hold **whether bool is 1 or 4 bytes** — so the runtime portion does not actually distinguish the fix. The regression *is* still caught by the source-level assertions in the same test (`Assert.Contains("[MarshalAs(UnmanagedType.I1)]")` and the "attribute line immediately precedes `public bool B;`" check), which fail if the emitter change is reverted. So the test is not dead — only its runtime check is decorative.
**Fix (deferred):** DEBT-AIB-008 — add a discriminating layout (e.g. `{bool B; byte D}`: I1 ⇒ SizeOf 2, bare bool ⇒ 8) so the runtime assertion proves bool=1 byte independently. Low risk; source assertions guard it meanwhile.

### Issue 2 (P2): S1-1 not wired into the live render path
**File:** `Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs:375`.
**Problem:** the production render call `BuildViewModel(_store.ActiveAsset, aggregationResult: ...)` does not pass `actionSchemaExporter` or `boundActionFqns`, so `HardcodedDtoFields` is always empty in the live editor, and nothing ImGui-renders that collection yet. The VM contract (S1-1's stated success conditions = the two named VM tests) is met; live surfacing is a gate-level manual check.
**Fix (deferred):** DEBT-AIB-009 — wire the render path to source bound action FQNs + the exporter, and render `HardcodedDtoFields` read-only. Close at S1-5 (node-inspector) / S1-G (live demo gate).

## Test Quality
S1-1 tests assert real behavior (count, `IsReadOnly==true`, exclusion from editable set, field names, empty-without-exporter). S1-0 source assertions are precise and discriminating. Only the S1-0 runtime portion is weak (Issue 1).

## Verdict
APPROVED. Both tasks satisfy their stated success conditions; full suite green (1100/0). The two issues are P2 (recorded), neither blocks the Slice-1 chain. S1-1's live wiring naturally completes at S1-5/S1-G.

## Commit Message
```
feat(btree-ai-binding): bool MarshalAs(I1) + read-only hardcoded-DTO reflection (BATCH-01)

Completes S1-0, S1-1
- BlackboardDtoEmitter emits [MarshalAs(UnmanagedType.I1)] on bool fields so
  the emitted struct's Marshal layout matches the bin-packer's 1-byte bool.
- ActionSchemaExporter exposes ActionSchemaEntry.DtoFields (first ref-param DTO's
  public fields); BlackboardAuthoringWindow.BuildViewModel surfaces them as
  read-only VariableViewModel rows (HardcodedDtoFields), separate from editables.
Tests: +11 (Hrot.Editor.AiShared.Tests 1100/0); Roslyn-compiled layout check +
VM reflection/read-only/exclusion assertions.
```
