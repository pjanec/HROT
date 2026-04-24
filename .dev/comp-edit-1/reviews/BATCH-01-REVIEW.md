# BATCH-01 Review

**Batch:** BATCH-01
**Reviewer:** Development Lead
**Date:** 2026-04-24
**Status:** APPROVED

---

## Issues Found

No issues found. All three tasks correctly implemented.

---

## Test Quality Assessment

Tests verify actual behavior throughout:

- `T-CE01a` reads back from `parentBinding.GetBoxed()` (not the nested binding), proving the write propagated through the parent — exactly the contract that matters.
- `T-CE03b` calls `session.Commit()` and asserts the committed component's field value, verifying the full mutation chain end-to-end.
- `T-CE03c` calls `Resize`, then `MarkStructuralChange`+`RebuildDocument`, then asserts the rebuilt child count — tests the structural-change cycle, not just the initial build.
- `T-CE02d` uses `Assert.Same` (reference equality) to verify no allocation on the empty-singleton path.

No shallow tests. No "object is not null" assertions.

---

## Verdict

**APPROVED.** All 184 tests pass (171 pre-existing + 13 new). All CE01/CE02/CE03 spec success conditions have corresponding tests. Implementation matches design intent precisely.

---

## Commit Message

```
feat(comp-edit-1): Phase 1 StructEdit core extensions (BATCH-01)

CE01 - NestedMemberBinding (StructEdit.Core.Bindings): new internal sealed class.
  Wraps an existing IValueBinding and exposes one field/property of the parent value.
  SetBoxed writes the mutated boxed struct back to the parent for value types
  (copy-on-box correctness for array element struct mutation).

CE02 - EditNodeMetadata.CustomAttributes: IReadOnlyList<Attribute> property added,
  defaulting to Array.Empty<Attribute>(). ReadMetadata now harvests all non-StructEdit
  attributes into CustomAttributes, enabling opaque flow of domain attributes to UI.

CE03 - Array element node generation in ReflectionEditDocumentBuilder:
  BuildNode gains explicitBinding/parentBinding optional parameters.
  New BuildArrayElements helper generates one EditNode per IContainerBinding element.
  DynamicArray, InlineArray, FixedBuffer cases call BuildArrayElements and store
  result as children. BuildChildren propagates parentBinding so leaf fields inside
  managed struct array elements are backed by NestedMemberBinding.
  CreateLeafBinding detects managed-element path and returns NestedMemberBinding.

Fix: StructEdit.Tests.csproj was missing <AllowUnsafeBlocks>true</AllowUnsafeBlocks>.

Tests: 13 new tests covering all T-CE01a/b/c, T-CE02a/b/c/d, T-CE03a/b/c/d/e/f.
```

---

**Next Batch:** BATCH-02 (Phase 2 picker infrastructure + project reference)
