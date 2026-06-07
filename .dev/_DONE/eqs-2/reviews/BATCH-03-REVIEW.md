# BATCH-03 REVIEW

**Batch:** BATCH-03
**Tasks:** TASK-EQS-007 (DDS translators full), TASK-EQS-008 (core query interfaces)
**Decision:** APPROVED

---

## Summary

All four translator stubs converted to full implementations. One architectural fix
(EqsSolverSystem lifecycle query) and one dependency resolution (EqsResultUpdateEvent moved
to FDP toolkit). All 7 new tests pass; 7 BATCH-02 EQS tests unaffected.

---

## Code Review

### EqsSensorConfigEgressTranslator

Clean. Correct SmartEgressUtil pattern (ShouldPublish -> Write -> MarkPublished). Authority
gate present. Nullable writer guarded. DisposeInstance in Dispose(networkEntityId). Uses
`EqsSensorConfigTopic` (not pseudocode type name). No issues.

### EqsSensorConfigIngressTranslator

Correct. Uses `DdsTypeSupport.FromNative<EqsSensorConfigTopic>(sample.NativePtr)` to extract
entity key from NOT_ALIVE samples — consistent with EntityMasterIngressTranslator pattern.
NotAliveDisposed path removes EqsSensor correctly. Nullable reader guarded.

**Deviation D-01 (ACCEPTABLE):** Key extraction from disposed samples uses `DdsTypeSupport.FromNative`
rather than the `sample.Info.InstanceHandle` approach described in EntityMissionIngressTranslator.
The subagent chose the stronger pattern (EntityMasterIngressTranslator). Both are valid; the
`FromNative` pattern more precisely matches the [DdsKey]-annotated field.

### EqsResultEventEgressTranslator

Correct. Zero-count events (Phase 1 stub) are published with empty `List<EqsResultEntry>` without
touching the pool (no HasSingleton guard needed for zero-entry path).

**Bug Fix D-02 (REQUIRED, APPROVED):** Original instructions would have caused the pool's
HasSingletonUnmanaged guard to swallow all Phase 1 stub events (the pool is not registered when
EntryCount=0). The subagent correctly inverted the guard: pool access is inside the
`EntryCount > 0` branch only.

Entity-shaped result: `TryGetNetworkId` fallback to 0 is correct (no entity → positional).
Rejection sentinel -1L check is present. No issues.

### EqsResultIngressTranslator

Correct. Maps SensorNetworkId -> local entity via entityMap. Publishes managed EqsResultUpdateEvent
via repo.Bus.PublishManaged. Direct assignment of `data.Results` (same type). No issues.

### EqsResultUpdateEvent relocation

Moved from `Hrot.SimHost.Systems` to `Fdp.Toolkit.Spatial.Eqs` to avoid circular dependency.
The redirect comment in the original file is informative. The redirect is clear.

**Trade-off:** EqsResultUpdateEvent is now a FDP-layer type rather than a Hrot-layer type.
This is acceptable: it carries `EqsResultEntry` (FDP type) and `Entity` (FDP type), so it has
no Hrot-specific dependencies.

### EqsSolverSystem (unscheduled fix)

**Bug Fix D-03 (REQUIRED, APPROVED):** `.WithLifecycle(EntityLifecycle.Ghost)` added to the
solver's entity query. Muscle entities are created as ghosts by GhostCreationSystem; the default
`EntityLifecycle.Active` filter excluded them, causing the solver to never fire.

This is a correctness fix that was blocking T9 from passing.

### CgfLogicPack

Added `new EqsResultUpdateSystem()` to sim systems list.

**Architectural addition D-04 (REQUIRED, APPROVED):** In the distributed Brain/Muscle topology,
EqsResultUpdateEvent is published by EqsResultIngressTranslator on the CGF (Brain) world's bus.
Without EqsResultUpdateSystem running on CGF's sim loop, the event would be discarded. The
system was already in SimHostCoreLogicPack (offline/Editor path); this adds it to the CGF path.

The system is stateless and idempotent. Duplicate registration is harmless.

### EqsQueryTemplate.cs

All types present and correctly defined: EqsTestPhase enum (correct ordinal values), IEqsGenerator,
IEqsTest, EqsQueryTemplate struct (nullable phase arrays), IEqsTemplateRegistry, EqsTemplateAttribute
(correct AttributeUsage), EqsTemplateBase abstract class. No issues.

### SimHostAuxiliaryTranslatorPack

Brain and Muscle registration blocks correctly add 4 translators in the right roles. No issues.

---

## Test Quality

### TASK-EQS-007 Integration Tests (T8, T9, T10) — QUALITY: GOOD

T8 verifies the config egress/ingress pipeline end-to-end: Brain adds EqsSensor → polls DDS →
Muscle ghost has EqsSensor with correct SearchRadius. Tests real replication, not a mock.

T9 verifies the full result round-trip across 5 system hops: config replication → solver → result
egress → DDS → result ingress → managed event → EqsResultUpdateSystem → EqsCognitiveBuffer.IsReady.
This is the most important integration test in the batch.

T10 verifies the NOT_ALIVE_DISPOSED path.

**Note on T10:** Spec said "remove EqsSensor from Brain entity" but the subagent correctly
identified that component removal does not trigger DDS disposal notification. Entity destruction
is the right trigger for `translator.Dispose(networkEntityId)`. The deviation is documented
in the test file's XML doc comment.

Domain counter starts at 70 (private static, avoids collision with HrotRunnerHarness 100+ range).

### TASK-EQS-008 Unit Tests — QUALITY: GOOD

T-EQS-008-1: Phase enum ordinals verified.
T-EQS-008-2: Full trivial composition test — generator populates 2 candidates, filter rejects
index 0, exactly 1 survivor asserted. Verifies correct rejection sentinel -1L usage.
T-EQS-008-3: Registry miss path verified.
T-EQS-008-4: Attribute AssetId storage verified.

All 4 tests are pure (no ECS, no harness), fast, and composable.

---

## Issues Found

### P1 Issues (blocking)
None.

### P2 Issues (important, track)
- D-01: EqsSensorConfigIngressTranslator uses `DdsTypeSupport.FromNative` for disposed sample key extraction. If CycloneDDS updates its serialization format, this may need updating. Low risk. Track in debt.

### P3 Issues (minor)
- Domain counter in EqsTranslatorTests.cs starts at 70 rather than the >300 range noted in the conversation summary. This is fine as 71-73 are confirmed free, but a comment explaining the range choice would be better. Non-blocking.

---

## Verdict

**APPROVED.** All required functionality is implemented correctly. Tests are meaningful. All 7
new tests pass. No P1 issues. Two minor notes tracked above.
