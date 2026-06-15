# BATCH-04 (S1-5) — verification note (no code change)
**Date:** 2026-06-15

S1-5's success conditions were already implemented by the DEC-05 inspector work; no new code was required. Lead verified the existing tests assert the exact S1-5 behaviors and pass.

| S1-5 named requirement | Existing test (passing) | Asserts |
|---|---|---|
| `FieldPicker_ListsOnlyTypeMatchingVariables` | `BlackboardFieldPickerDrawerTests.GetItems_ReturnsOnlyCompatibleVars_ForKnownFqn` | picker returns ONLY the variable whose type matches the action's `DtoType` ("only `floatVar` for a float-DTO action") |
| `FieldPicker_SelectsVariable_SetsExpressionTargetField` + `PromoteToNewVariable_CreatesAutoManagedAndBinds` | `PromoteBindTests.Promote_CreatesVar_AndFacetApply_SetsExpressionTargetField_BTree` | promote creates a var of the action's DtoType, `IsAutoManaged==true`, and `ApplyFacet` persists `ExpressionTargetField` on the node (round-trip survives) |
| auto-managed of DtoType | `BlackboardFieldPickerDrawerTests.Promote_CreatesAutoVar_WithCorrectNameAndType_AndIsAutoManaged` | name/type correct + `IsAutoManaged` |

Verified by lead: `Hrot.BTree.Editor.Tests` (`Inspector.BlackboardFieldPickerDrawerTests` + `Inspector.PromoteBindTests`) 18/0.

**Marked S1-5 done.** The two remaining Slice-1 live-wiring items — DEBT-AIB-009 (hardcoded-DTO read-only reflection wired into the live Variables-panel render path) and DEBT-AIB-012 (inspector reads multi-DTO managed blackboards per-variable, not only offset 0) — are folded into **S1-G** (BATCH-05), where the live/manual gate verification exercises them.
