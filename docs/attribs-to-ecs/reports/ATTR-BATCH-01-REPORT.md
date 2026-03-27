# ATTR-BATCH-01 Developer Report

**Batch:** ATTR-BATCH-01  
**Tasks:** ATTR-S1T1, ATTR-S1T2, ATTR-S2T1  
**Status:** ✅ COMPLETE — all three tasks implemented, tests written and passing

---

## ✅ Completion Summary

| Task | Description | Status | New Tests |
|------|-------------|--------|-----------|
| ATTR-S1T1 | `CreateEntityRequest.InitialAttributesJson` | ✅ Done | 2 |
| ATTR-S1T2 | `UpdateEntityAttributeRequest.AttributePatchJson` + enum deletion | ✅ Done | 4 |
| ATTR-S2T1 | `CreationTool` dumb-pipe simplification | ✅ Done | 4 + 3 updated |

**Final test counts (all green):**
- `Bagira.DDS.DataModel.Tests` — 9/9 ✅ (was 3; +6 field tests)
- `Bagira.IG.Tests / CreationTool filter` — 21/21 ✅ (was 17; +4 new, 3 updated)
- `Bagira.SimHost.Tests` — 88/88 ✅
- `Bagira.Map.Common.Tests` — 10/10 ✅

---

## Q1: Compilation Issues Encountered

**Two non-obvious breakages:**

**1. `[DdsManaged]` annotation required on all `string` fields.**  
The DDS code generator validates at build time that every `string` and `List<T>` field in a DDS message struct carries `[DdsManaged]`. The existing `InitialAttributes: List<EntityAttributePayload>` already had the annotation, so the pattern was visible — but I initially added the new `string?` fields without it, producing a code-gen error. The fix was straightforward (add `[DdsManaged]` to both new fields) but the error message identifies the violation precisely, so it was quick to resolve once you understand the convention exists.

**2. `NetworkEntityMap` namespace ambiguity when trimming `using` imports.**  
`UpdateEntityAttributeRequestSystem.cs` had a block of nine `using` directives. When I rewrote the system stub I rebuilt the import list by examining what the new code actually needs, and accidentally dropped `using FDP.Toolkit.Replication.Services;`. That takes down `NetworkEntityMap`, which lives in that namespace despite the `FDP.Toolkit.NetworkSpawning` namespace suggesting otherwise. The `FDP.Toolkit.Replication.Services` namespace is not obvious from the class name alone; the fix was to add it back. Lesson: when trimming imports from a system that changes responsibilities, always re-verify the namespace of every type still in use.

---

## Q2: Weak Points Observed

**1. `UpdateEntityAttributeRequestSystem` had no abstraction layer over the enum dispatch.**  
The old `ProcessRequest` method mapped directly from `EntityAttribute` enum cases to concrete compiler calls with no interface or strategy pattern in between. Swapping to JSON now required rewriting the entire method body rather than swapping a single delegate. A `IAttributeHandler` interface or at minimum an `Action<...>` dispatch table keyed on attribute ID would have localised this change to a lookup table update.

**2. `EntityAttributeCompiler` had no interface, no base class, no registration.**  
It was the sole implementation and the only consumer of `EntityAttributePayload`. Deletion was clean, but if a second implementation existed (or a mock in tests), there would have been no common type boundary to change. The `JsonAttributeCompiler` planned for S3T1 should be registered behind an interface from day one.

**3. `CreationTool` XML documentation listed its own implementation details.**  
The constructor summary named `dtEntityInfo`, `entityName`, and affiliation as outputs of the JSON parsing. These were correct at write-time but became wrong immediately after ATTR-S2T1. Internal implementation details in public XML doc is brittle; outward-facing doc should describe *what the caller observes*, not internal data transformations.

---

## Q3: Edge Cases Not Mentioned in the Batch

**1. `MapAffiliation` became dead code silently.**  
The batch spec said to remove `ParseNameFromJson` and `aff` local variable, but `MapAffiliation` was only ever called to populate `aff` (the `EntityInfo.ForceIdentifier`). Once `dtEntityInfo` was dropped and `aff` removed, `MapAffiliation` became unreachable. The batch said nothing about it. I removed it as dead code; it was the correct call, but the spec could have been explicit.

**2. Four pre-existing test failures mask potential regressions in CI.**  
`EditToolTests` and `AdvancedFeaturesIntegrationTests` have 4 tests asserting specific floating-point/vector values that were already wrong before this batch (they fail against the current HEAD with no ATTR changes). In a heavily automated pipeline, these pre-existing failures pad the "failed" count and make it harder to notice when a new regression slips in. They should be filed as a separate fix-tracker item.

**3. `_nameResolver` field is retained dead code per spec.**  
The existing constructor call `_nameResolver?.Invoke()` is still called inside `BuildAndPublishCreateRequest` (it's retained and invoked, but the return value is no longer used). This means every click fires the delegate unnecessarily. If callers pass a resolver that has side effects (e.g., increments a counter or allocates), this is observable behaviour. The batch says "DO NOT REMOVE" because future wiring needs it, but a code comment flagging its current inertness would prevent confusion during code review.

---

## Q4: Improvements for `CreationTool` / Generic Property Injection

**1. Collapse the two separate JSON paths.**  
`ParseAffiliationFromJson` and `_initialPropertiesJson` both traverse the same JSON blob at construction time — the former for the affiliation string, the latter saved as-is for the message. A small `ParsedInitialProperties` record (deserialized once in the constructor) could hold both `(ForceId affiliationForDisplay, string rawJson)` and avoid double-allocation on hot re-construction paths.

**2. Consider a typed `IInitialProperties` contract rather than raw `string?`.**  
Injecting the JSON as a raw opaque string is correct for the dumb-pipe design, but IOS-side callers have no schema validation before the string reaches the SimHost. A minimal shared value object (even just a validated `record InitialPropertiesJson(string Value)` that throws on null) would surface construction errors at the IOS boundary rather than at SimHost parse time.

**3. Move `_nameResolver` invocation behind a feature-flag or remove the call until it's wired.**  
If future wiring genuinely needs the resolver, it should be invoked *after* `InitialAttributesJson` is assigned, so the resolver can optionally override the name inside the JSON blob. Calling it currently (with the result discarded) is at best harmless but invites confusion. A `TODO ATTR-S5Tx: wire nameResolver output into InitialAttributesJson` comment on the call site gives the next developer the right breadcrumb.
