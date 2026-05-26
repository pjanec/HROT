# BATCH-06 REVIEW

**Reviewer:** Dev Lead (Autonomous)  
**Batch:** BATCH-06 — Phase 4 (Events & Engine Event Catalog)  
**Report File:** `.dev/anim-ctrl/reports/BATCH-06-REPORT.md`  
**Status:** ✅ **APPROVED** (Phase 4 complete)

---

## Verification Summary

| Check | Status | Notes |
|-------|--------|-------|
| **Build** | ✅ Clean | Full solution `IOS-IG-SimHost.sln` builds with 0 errors, 0 warnings |
| **Event Type Tests** | ✅ 19/19 passing | Event IDs, field ordering, data policies all correct |
| **Catalog Tests** | ✅ 7/7 passing | Event registration, propagation flags, QoS verified |
| **Validator Tests** | ✅ 8/8 passing | BP2016 (BestEffort) and BP2017 (LocalOnly) rules validated |
| **Full Animation Suite** | ✅ 130/130 passing | No regressions in Phase 0–3 or Phase 1 tests |
| **Blueprint Compiler Suite** | ✅ 44/44 passing (subset) | All event/catalog/validator tests included |
| **Design alignment** | ✅ Verified | Event IDs 8200–8299 per architect ruling; FootstepEvent exclusion; QoS=Reliable; BP2016/BP2017 integration |
| **Test quality** | ✅ Behavioral | Tests verify logic, constraints, and edge cases—not smoke tests |
| **Phase 4 coverage** | ✅ 100% | All 4 tasks complete (ANC-P4-01 through ANC-P4-04) |

---

## What's Good

### Event Type Definitions (ANC-P4-01) — All 8 Complete ✅

All eight animation events created as `readonly struct` types (ECS idiom):
- **MontageStartedEvent** [8201] — lifecycle
- **MontageEndedEvent** [8202] — lifecycle with EndReason enum
- **MontageSectionAdvancedEvent** [8203] — lifecycle
- **StanceChangedEvent** [8204] — lifecycle
- **FootstepEvent** [8210] — notify (muscle-local)
- **HitWindowOpenedEvent** [8211] — notify
- **HitWindowClosedEvent** [8212] — notify
- **AnimNotifyEvent** [8213] — notify

**Test coverage (19 tests):**
- `EventIds_AreInRange_8200_to_8299` — verifies range
- `AllAnimationEvents_HaveDistinctEventIds` — no collisions
- `AllAnimationEventIds_DoNotCollideWith_GlobalActionRequestedEvent` — explicit 8059 check
- `AnimationEventIds_AreAssignedInExpectedOrder` — exact ID values pinned
- `AllAnimationEvents_HaveDataPolicyNoRecord` — metadata correct
- `[EventName]_HasTargetFieldFirst` tests (one per event) — field ordering enforced via reflection

**Quality:** Tests verify structural constraints (field ordering, ID ranges, data policies) using reflection + exact value checks. No placeholders; all IDs are pinned to specific values. Excellent.

### Picker Attributes (ANC-P4-02) — Complete ✅

- `AnimMarkerPickerAttribute` on `AnimNotifyEvent.MarkerHash`
- `MontagePickerAttribute` on montage ID fields (MontageStartedEvent, MontageEndedEvent, MontageSectionAdvancedEvent)
- Both in `Hrot.MuscleCharacter.Animation.Events` namespace
- Simple marker attributes with no data; editor property drawer discovers them via reflection

**Design decision well-documented:** No attribute registry conflicts. Picker is already in place in Blueprint system for DD-5. Phase 4 just extends it to new event struct fields. Clean.

### Catalog Entries (ANC-P4-03) — All 8 Registered ✅

All 8 events registered in `BuiltInEngineEventCatalog`:
- **Lifecycle:** MontageStartedEvent, MontageEndedEvent, MontageSectionAdvancedEvent, StanceChangedEvent → category `Animation/Lifecycle`
- **Notify:** FootstepEvent, HitWindowOpenedEvent, HitWindowClosedEvent, AnimNotifyEvent → category `Animation/Notify`
- **QoS:** All use `EventQoS.Reliable` (state transitions; delivery critical)
- **Propagation:** 7 events have `PropagatesAcrossNodes=true`; FootstepEvent has `false` (muscle-local)
- **TargetFieldName:** All correctly identify "Target"
- **FilterableFields:** MontageEndedEvent exposes `EndReason` for event filters

**Test coverage (7 tests):**
- `BuiltInEngineEventCatalog_AnimationEntries_Exist` — counts 8
- `BuiltInEngineEventCatalog_FootstepEvent_IsExcludedBrainSide` — verifies `PropagatesAcrossNodes=false` + not in brain-visible list
- `BuiltInEngineEventCatalog_AnimationEntries_HaveCorrectCategory` — category routing
- `BuiltInEngineEventCatalog_AnimationEntries_HaveTargetFieldName` — field metadata
- `BuiltInEngineEventCatalog_AllAnimationEntries_AreReliable` — QoS validation
- `BuiltInEngineEventCatalog_AnimationEntries_HaveFilterableFields` — MontageEndedEvent.EndReason

**Quality:** Tests check catalog metadata (not just presence, but correctness of category, QoS, propagation flag, field names). Footstep exclusion is explicitly verified. Excellent.

### Validators BP2016 & BP2017 (ANC-P4-04) — Fully Integrated ✅

Both validators implemented in `Stage2_Validate.ValidateEventFired()`:

**BP2016 (BestEffort Event Warning):**
- Fires if `matchedEntry.QoS == EventQoS.BestEffort` and event is wired to WhenNode
- Severity: Warning (not error)
- Intent: Alert developer that event may be lost

**BP2017 (Local-Only Event in Brain Context Error):**
- Fires if `!matchedEntry.PropagatesAcrossNodes && ctx.ExecutionNode == ExecutionNodeHint.Brain`
- Severity: Error (blocks compilation in Brain context)
- Intent: Prevent Brain Blueprints from subscribing to Muscle-local events (e.g., FootstepEvent)
- Opt-in: Default `ExecutionNodeHint=Any` means BP2017 never fires unless caller explicitly sets Brain context

**Test coverage (8 tests):**
- `Validate_BestEffortEvent_EmitsBP2016_Warning` — positive case for BP2016
- `Validate_ReliableEvent_NoBP2016` — negative case (Reliable event must not emit BP2016)
- `Validate_BrainBlueprintOnLocalOnlyEvent_EmitsBP2017_Error` — positive case for BP2017
- `Validate_MuscleContextOnLocalOnlyEvent_NoBP2017` — negative case (Muscle context allows local-only)
- `Validate_AnyContextOnLocalOnlyEvent_NoBP2017` — negative case (default=Any never fires BP2017)
- Plus 3 more error/non-error cases

Custom `BestEffortTestCatalog` inner class provides three test events (Reliable, BestEffort, LocalOnly) to test all code paths in isolation without relying on real production data.

**Quality:** Tests use a controlled test catalog. Both positive and negative cases covered. Opt-in semantics (ExecutionNodeHint default=Any) is verified to avoid false positives in existing tests. Excellent defensive testing.

---

## Test Quality Deep-Dive

### Event Type Tests: 19 tests
✅ **Structural verification:** Field reflection + exact value checks (Event IDs 8201–8213). Not smoke tests.
✅ **Constraint enforcement:** All Target fields verified as first field, Entity type, via reflection.
✅ **Known-value regression:** EventIds pinned to exact values (8204 → 8210 gap is intentional).

### Catalog Tests: 7 tests
✅ **Metadata integrity:** Category, QoS, PropagatesAcrossNodes all verified per entry.
✅ **Exclusion mechanism:** FootstepEvent explicitly checked to have `PropagatesAcrossNodes=false` and NOT appear in brain-visible list.
✅ **No hard-coded event names:** All checks query the catalog; no string literals checking for "FootstepEvent" by name in the tests (clean contract-based design).

### Validator Tests: 8 tests
✅ **Controlled catalog:** `BestEffortTestCatalog` isolates validator testing from production data.
✅ **Positive + negative paths:** BP2016 fires on BestEffort, doesn't fire on Reliable. BP2017 fires on Brain+LocalOnly, doesn't fire on Muscle+LocalOnly or Any+LocalOnly.
✅ **Opt-in semantics:** `ExecutionNodeHint.Any` (default) never triggers BP2017. Verified explicitly.

**None of these tests are fake/smoke tests.** All verify measurable behavior and constraints. Full confidence in test quality.

---

## Design Decisions & Insights (from Report)

### 1. Event Struct Hierarchy (Not)
No inheritance or composition; each event is a flat `readonly struct`. ECS idiom. The `Target` field (Entity type, first position) provides consistency without type hierarchy. Enforced via reflection test. ✅

### 2. MontageEndReason Enum Relocation
Moved from `AnimationStateReporterSystem.cs` (System namespace) to `AnimationEvents.cs` (Events namespace) because it's part of the event contract, not the system implementation. System file updated to add `using`. Clean design decision. ✅

### 3. EventQoS & ExecutionNodeHint Enums
Added to `CatalogInterfaces.cs` with backward-compatible defaults (EventQoS.Reliable, ExecutionNodeHint.Any). Existing callers require no changes. Existing tests that don't set ExecutionNode default to Any and never trigger BP2017. No false positives. ✅

### 4. File-scoped AnimFqn Class
`file static class AnimFqn` in `BuiltInEngineEventCatalog.cs` holds FQN prefix string to avoid repetition across 8 entries. Uses C# 11 `file` modifier (available under `<LangVersion>latest</LangVersion>`). Invisible outside file. No public API bloat. ✅

### 5. No Factory Methods
Events are simple value types with public fields. Callers construct inline with object initializers. No factory overengineering needed for Phase 4 scope. ✅

---

## D-01 (Documentation Debt)

**Status:** Still open (non-blocking for Phase 4)

DD-3 document body references `8000–8099` for animation event IDs. Implementation correctly uses `8200–8299` per architect ruling. Reconciliation deferred to documentation task (not an implementation issue—code is correct).

---

## Summary

**BATCH-06 is APPROVED. Phase 4 is now 100% COMPLETE.**

All deliverables met:
- ✅ 8 event types defined (ANC-P4-01): all IDs in 8200–8299, pinned exact values, Target field first
- ✅ Picker attributes registered (ANC-P4-02): AnimMarkerPickerAttribute and MontagePickerAttribute
- ✅ 8 catalog entries registered (ANC-P4-03): correct metadata, FootstepEvent excluded from Brain
- ✅ BP2016/BP2017 validators integrated (ANC-P4-04): BestEffort warning, LocalOnly error, opt-in semantics
- ✅ 34 new tests passing (19 event + 7 catalog + 8 validator): all behavioral, no smoke tests
- ✅ Full solution builds clean (0 errors, 0 warnings)
- ✅ 130 total animation tests passing (no regressions Phase 0–3)
- ✅ D-01 documented as non-blocking documentation reconciliation
- ✅ All design decisions within scope and well-documented

**Phase 4 represents the event contract layer.** All 4 tasks now green. The system is ready for Phase 5 (Blueprint Authoring Primitives, 8 tasks: 9 AiPrimitive nodes + validators), which will use these event types and catalog entries as inputs.

---

## Next Steps

1. ✅ Mark ANC-P4-01 through ANC-P4-04 as `[x]` in TASK-TRACKER.md
2. ✅ Note D-01 status in DEBT-TRACKER.md (already recorded, no changes)
3. ✅ Commit BATCH-06 review to git
4. → **Proceed to BATCH-07** (Phase 5: Blueprint Authoring Primitives, ~50–60 hours, 8 tasks)

---

## Commit Message

```
ANC-P4-01 through ANC-P4-04: Phase 4 — Events & Engine Event Catalog

- AnimationEvents.cs: 8 event types (MontageStartedEvent [8201], MontageEndedEvent [8202], MontageSectionAdvancedEvent [8203], StanceChangedEvent [8204], FootstepEvent [8210], HitWindowOpenedEvent [8211], HitWindowClosedEvent [8212], AnimNotifyEvent [8213])
- MontageEndReason enum: moved to Events namespace (contract layer, not system impl)
- Picker attributes: AnimMarkerPickerAttribute, MontagePickerAttribute in Events namespace
- Catalog registration: 8 entries with correct metadata (category, QoS=Reliable, PropagatesAcrossNodes)
- FootstepEvent exclusion: PropagatesAcrossNodes=false (Muscle-local, not visible in Brain)
- BP2016 validator: warn on BestEffort events wired to WhenNode
- BP2017 validator: error on Brain Blueprint subscribing to local-only events
- EventQoS + ExecutionNodeHint enums: backward-compatible defaults (no false positives)

Test Results: 44 passing (Blueprints subset) | 19 new event tests | 130 total animation suite
Build: clean (0 errors, 0 warnings) | No regressions Phase 0–3

Verified:
- Phase 4 complete (all 4 tasks: ANC-P4-01 through ANC-P4-04)
- Event IDs 8200–8299 per architect ruling; no collision with GlobalActionRequestedEvent
- Catalog propagation and QoS flags correct
- Validator rules BP2016/BP2017 integrated with opt-in semantics
- Test coverage: structural verification, metadata integrity, positive/negative validator paths
- D-01 recorded (DD-3 doc body needs reconciliation; implementation correct)
- All prior phases intact (no regressions)

Ready for Phase 5 (Blueprint Authoring Primitives, 8 tasks).
```

---

**Review Complete.** Phase 4 APPROVED. Ready for BATCH-07 delegation.
