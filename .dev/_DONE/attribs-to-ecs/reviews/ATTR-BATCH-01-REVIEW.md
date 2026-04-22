# ATTR-BATCH-01 Review

**Batch:** ATTR-BATCH-01  
**Reviewer:** Development Lead  
**Date:** 2026-03-12  
**Status:** ✅ APPROVED

---

## Summary

DDS API structs migrated successfully and IG CreationTool simplified to dumb pipe. All tests updated and passing correctly.

---

## Issues Found

No issues found.

*(Note on report Q3/Q4: The report states `_nameResolver?.Invoke()` is still called and returning discarded strings, but you actually correctly removed that invocation from `BuildAndPublishCreateRequest` per the spec. Your implementation in the code is perfectly correct.)*

---

## Verdict

**Status:** APPROVED

**All requirements met. Ready to merge.**

---

## 📝 Commit Message

```
feat: DDS json API migration and IG pipe simplification (ATTR-BATCH-01)

Completes ATTR-S1T1, ATTR-S1T2, ATTR-S2T1

Replaces InitialAttributes and update payload fields with JSON strings in DDS messages. 
Removes old EntityAttribute enum and Union. Simplifies CreationTool to forward raw JSON instead of building dtEntityInfo descriptors.

GenericMessages (ATTR-S1T1, ATTR-S1T2):
- Replaced InitialAttributes with InitialAttributesJson string in CreateEntityRequest
- Replaced AttributeId/Payload with AttributePatchJson string in UpdateEntityAttributeRequest
- Removed unused EntityAttribute enum and Payload union

CreationTool (ATTR-S2T1):
- Removed dtEntityInfo descriptor synthesis
- Forwards initialPropertiesJson verbatim to InitialAttributesJson
- Retained ParseAffiliationFromJson for local ghost rendering

Testing:
- 10 new/updated tests covering Reflection checks for new DDS struct shapes
- Verified CreationTool emits exactly 2 descriptors and forwards JSON correctly
```

---

**Next Batch:** Preparing ATTR-BATCH-02
