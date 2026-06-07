# BATCH-06: Phase 4 — Events & Engine Event Catalog

**Delegate to:** Claude Sonnet 4.6 (dev-lead-agent)  
**Reference:** [TASK-DETAIL.md](../TASK-DETAIL.md#phase-4--events--engine-event-catalog-dd-3), [DD-3_EventCatalog_AnimationNotify_v1_3.md](../DD-3_EventCatalog_AnimationNotify_v1_3.md), [BATCH-05 Review](../reviews/BATCH-05-REVIEW.md)

---

## Scope

Implement the eight animation event types with mandatory attributes, picker attributes and drawers for the Blueprint editor, register all events in the `BuiltInEngineEventCatalog`, and add two When-node validator rules (BP2016/BP2017) for event qos/propagation warnings.

**Tasks:** ANC-P4-01 through ANC-P4-04 (4 tasks)

**Estimated effort:** 20–25 hours (Phase 4 is smaller than Phase 3).

---

## Context & Onboarding

### Prerequisites (✅ BATCH-05 shipped)
- **Phase 3 complete:** All 11 ECS systems + reactor + tests (111 tests passing).
- **Phase 2 complete:** TKB descriptor system and query API ready.
- **NotifyEventEmitterSystem ready:** Drains RawNotifyEvent; Phase 4 provides the event types it will emit.
- **D-01 note:** DD-3 doc still uses old `8000–8099` event ID block. **Real implementation uses `8200–8299`** per architect ruling. Task ANC-P4-01 enforces this; see DEBT-TRACKER.

### Key Design References
- **DD-3 §3:** Eight event types: `MontageStartedEvent`, `MontageEndedEvent` (+`MontageEndReason`), `MontageSectionAdvancedEvent`, `StanceChangedEvent`, `FootstepEvent`, `HitWindowOpenedEvent`, `HitWindowClosedEvent`, `AnimNotifyEvent`. All `[EventId(82xx)]` (8201–8213).
- **DD-3 §3.1–3.2:** Mandatory attributes (all events have `Entity Target` first field) + event-specific fields (EndReason enum, marker hash, etc.).
- **DD-3 §3.3–3.4:** Picker attributes (`[AnimMarkerPicker]` on marker hash, `[MontagePicker]` on montage ID fields).
- **DD-3 §4–4.3:** Catalog registration (BuiltInEngineEventCatalog, display names, filterable fields, QoS, `PropagatesAcrossNodes` boolean).
- **DD-3 §5.2, §5.3:** FootstepEvent exclusion (Muscle-side only, not visible in Brain dropdown).
- **DD-3 §6–6.1:** BP2016 (When-node on BestEffort event → warning), BP2017 (Brain When-node on local-only event → error).
- **DD-3 §9–9.7:** Event ID architect ruling — use `8200–8299`, not revoked `8000–8099`.

### Known Issues from DEBT-TRACKER
- **D-01 (P3):** DD-3 document body still uses `8000–8099` for event IDs. Implementation uses `8200–8299`. Code is correct; doc needs reconciliation (not blocking Phase 4 implementation, but noted for Phase 4 review).

---

## Developer Insights (Report Requirements)

In your batch report (`.dev/anim-ctrl/reports/BATCH-06-REPORT.md`), explicitly answer:

1. **Event type hierarchy:** Did you model all eight events as separate types, or use inheritance/composition? Why? Any type-safety concerns with the mandatory `Entity Target` field?

2. **Picker attribute integration:** How do `[AnimMarkerPicker]` and `[MontagePicker]` attributes integrate with the Blueprint editor's property drawer system? Did you encounter any attribute registry limitations?

3. **FootstepEvent exclusion mechanism:** How did you implement the "Muscle-side only" rule for FootstepEvent? Does the catalog API provide a clean way to exclude events per node type (Brain vs Muscle)?

4. **QoS and Propagation flags:** The eight events have different QoS (Reliable vs BestEffort) and `PropagatesAcrossNodes` values. How did you validate these in the catalog? Any risk of serialization mismatches?

5. **BP2016/BP2017 validator integration:** How do you detect "BestEffort event" or "local-only event" in the When-node compiler? Is there a clean way to query catalog metadata from the validator, or did you hard-code the seven event names?

6. **Event ID collision check:** The architect ruled out `8000–8099` (GlobalActionRequestedEvent=8059 already there). How do you verify that `8201–8213` don't collide with any other event IDs in the registry? Add a unit test for this.

7. **Design decisions beyond the spec:** Did you create helper structs or factory methods for event construction? Any utility functions for EndReason classification or marker lookup?

---

## Test-Driven Task Progression

**Mandatory workflow (same as previous batches):**

1. **Read task spec** in TASK-DETAIL.md + corresponding DD sections.
2. **Write tests first** (event type tests, catalog tests, validator tests).
3. **Implement** to satisfy tests.
4. **Verify all tests pass** locally.
5. **Document blocking issues** or design clarifications.

**Test expectations (Phase 4):**
- Event type tests: 8 tests (one per event type, verifying field presence + types)
- Catalog tests: 5–8 tests (entries present, QoS correct, Footstep excluded on Brain)
- Validator tests: 5–8 tests (BP2016 warning cases, BP2017 error cases, positive controls)
- Total: 15–25 tests (modest compared to Phase 3)

---

## Report Format

When finished, write `.dev/anim-ctrl/reports/BATCH-06-REPORT.md` with:

```markdown
# BATCH-06 Report — Phase 4 Implementation

## Summary
- [ ] All 4 tasks (ANC-P4-01–04) complete and green.
- [ ] 15–25 new tests passing.
- [ ] No breaking changes to Phase 0–3 contracts.

## Scope Completed
- **Event types:** [list with brief status]
- **Picker attributes:** [status]
- **Catalog entries:** [# entries, Footstep exclusion verified]
- **Validators:** [BP2016, BP2017 verified]

## Developer Insights
### 1. Event type hierarchy
[Your answer]

### 2. Picker attribute integration
[Your answer]

### 3. FootstepEvent exclusion mechanism
[Your answer]

### 4. QoS and Propagation flags
[Your answer]

### 5. BP2016/BP2017 validator integration
[Your answer]

### 6. Event ID collision check
[Your answer]

### 7. Design decisions beyond the spec
[Your answer]

## Validation
- [ ] `dotnet build Hrot.MuscleCharacter.Animation.csproj -c Debug` succeeds.
- [ ] `dotnet test Hrot/Subsystems/Hrot.Blueprints.Compiler.Tests` (validator tests) green.
- [ ] Full solution builds clean.
```

---

## Tasks Checklist

- [ ] **ANC-P4-01** Eight event types + mandatory attributes (Event ID 8201–8213, [EventId]/[DataPolicy] attributes)
- [ ] **ANC-P4-02** Picker attributes + drawers ([AnimMarkerPicker], [MontagePicker] on relevant fields)
- [ ] **ANC-P4-03** Catalog entries (BuiltInEngineEventCatalog registration, 8 entries, Footstep exclusion Brain-side)
- [ ] **ANC-P4-04** BP2016 / BP2017 validator rules (When-node warnings for QoS/propagation)

---

## Known Issues to Address

### D-01 (P3) — DD-3 doc/code mismatch (documentation-only)
**Note:** DD-3 §3 and §9.7 still reference `8000–8099` block. Architect ruling: use `8200–8299`. Implementation is correct per this batch. D-01 is deferred to "DD-3 docs reconciliation" (low-priority async documentation task, not blocking Phase 4).

---

## Next Steps (for Dev Lead post-review)

After BATCH-06 is reviewed and committed:
1. **Verify** that ANC-P4-01 through ANC-P4-04 are marked `[x]` in TASK-TRACKER.md.
2. **Verify** DEBT-TRACKER entries (D-01 remains as "DD-3 docs reconciliation" P3).
3. **Proceed to BATCH-07** (Phase 5: Blueprint authoring primitives, 8 tasks, ~40 hours) if no critical issues.

---

## Communication

**Key dependencies for Phase 5:**
- Phase 4 provides event types and picker attributes that Phase 5 AiPrimitive nodes will reference.
- Phase 5's `[MontagePicker]` and `[AnimMarkerPicker]` depend on the attribute definitions and drawers from Phase 4.
- Phase 5 validators (ANIM008–012) may reference Phase 4 event types (e.g., BP2016/BP2017 detection).

**Unblocking note:** Phase 4 is a smaller, more straightforward phase after the heavy Phase 3 systems work. Most complexity is in the validator rules (BP2016/BP2017) which need clean integration with the When-node compiler. Invest time in test coverage for edge cases (e.g., When-node on Reliable vs BestEffort events).

---

**Expected completion:** ~20–25 hours of focused work.  
**Success condition:** All 4 Phase 4 tasks green, 15–25 tests passing, event ID validation clean, catalog entries verified, validators wired.
