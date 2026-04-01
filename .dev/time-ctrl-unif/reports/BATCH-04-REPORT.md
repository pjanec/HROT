# BATCH-04 Report

## Completion Status

All tasks complete. All success criteria met.

| Task | Status | Notes |
|------|--------|-------|
| TCU-TR001 MasterLockstepTranslator | ✅ Done | `Translators/MasterLockstepTranslator.cs` — ordinal 202 |
| TCU-TR002 SlaveLockstepTranslator | ✅ Done | `Translators/SlaveLockstepTranslator.cs` — ordinal 203 |
| TCU-TR003 TimeNetworkModule factory | ✅ Done | Two new methods; `CreateLockstepTranslator` marked `[Obsolete]` |
| TCU-W005 TimeControllerFactory | ✅ Done | Master+Continuous → MasterSyncController; Slave+Any → SlaveSyncController |
| TCU-T003 Translator unit tests | ✅ Done | 9 tests in `LockstepTranslatorTests.cs` |
| TCU-T004 Factory unit tests | ✅ Done | 4 tests added to `TimeControllerFactoryTests.cs` |

## Test Results

```
Passed!  - Failed: 0, Passed: 124, Skipped: 1, Total: 125
```

- Pre-existing pass count: 111
- New tests added: 13 (9 translator + 4 factory)
- No regressions

### New tests

**LockstepTranslatorTests (9 tests):**
- `MasterLockstepTranslator_NullParticipant_DoesNotThrow`
- `MasterLockstepTranslator_Egress_PublishesFrameOrderFromAdvanceFrameIntent`
- `MasterLockstepTranslator_Ingress_PublishesFrameStepCompletedEvent`
- `MasterLockstepTranslator_TopicName_IsFrameOrder`
- `MasterLockstepTranslator_DescriptorOrdinal_Is202`
- `SlaveLockstepTranslator_NullParticipant_DoesNotThrow`
- `SlaveLockstepTranslator_Ingress_PublishesAdvanceFrameIntent`
- `SlaveLockstepTranslator_Egress_DrainFrameStepCompletedEvent`
- `SlaveLockstepTranslator_DescriptorOrdinal_Is203`

**TimeControllerFactoryTests — added (4 tests):**
- `TimeControllerFactory_Master_Continuous_ReturnsMasterSyncController`
- `TimeControllerFactory_Slave_Continuous_ReturnsSlaveSyncController`
- `TimeControllerFactory_Slave_Deterministic_ReturnsSlaveSyncController`
- `TimeControllerFactory_Standalone_ReturnsUnchangedType`

**TimeControllerFactoryTests — updated (3 existing tests) to reflect new return types:**
- `Create_ContinuousMaster_ReturnsMasterController` → now asserts `MasterSyncController`
- `Create_ContinuousSlave_ReturnsSlaveController` → now asserts `SlaveSyncController`
- `Create_DeterministicSlave_ReturnsSteppedSlave` → now asserts `SlaveSyncController`

## Developer Insights

**Q1: Challenges with the translator abstraction vs. existing IDescriptorTranslator interface?**

The `IDescriptorTranslator` interface mandates `ScanAndPublish`, `PollIngress`, `ApplyToEntity`, and `Dispose`. Only the first two are relevant here; the latter two are ECS entity-lifecycle hooks that have no meaning in a pure time-sync translator — implemented as empty no-ops, consistent with `TimePulseEgressTranslator`.

The most important discipline was understanding which bus API to use: `AdvanceFrameIntent` and `FrameStepCompletedEvent` have no `[EventId]` attribute and must use `PublishManaged`/`ConsumeManaged` rather than `Publish`/`Consume`. Getting this wrong silently loses events with no exception.

**Q2: Weak points spotted in the DDS translation layer?**

The `FrameOrderDescriptor` has a `SequenceID` field that is not mapped in the `MasterLockstepTranslator` (set to default 0). If the consuming slave relies on `SequenceID` for ordering or deduplication, this gap could cause issues. The field is not mentioned in the task spec and the existing `SteppedMasterController` appears not to use it, but it is a latent risk.

**Q3: Design decisions made beyond the spec?**

The `MasterLockstepTranslator.ScanAndPublish` drains `AdvanceFrameIntent` via `ConsumeManaged` in the outer loop *before* checking `_orderWriter is null`. This ensures events are always drained from the bus even when participant is null, which is the correct null-safety contract (bus stays clean). The alternative — returning early on null writer — would leave stale intents in the bus.

**Q4: Anything about the TimeControllerFactory update worth noting?**

Updating the factory required updating three pre-existing tests that were asserting the old return types (`MasterTimeController`, `SlaveTimeController`, `SteppedSlaveController`). These tests were not factually wrong before BATCH-04 but became incorrect after the factory routes were changed. They were updated in-place rather than deleted/replaced to maintain the same test *intent* with the new expected types. The Standalone path remains a `MasterTimeController` with a dummy bus — unchanged as required.

**Q5: Suggested commit message**

```
feat(time-ctrl-unif): BATCH-04 complete - Role-Split Lockstep Translators

- MasterLockstepTranslator (ordinal=202): AdvanceFrameIntent→FrameOrder egress,
  FrameAck ingress→FrameStepCompletedEvent; no tracking state
- SlaveLockstepTranslator (ordinal=203): FrameOrder ingress→AdvanceFrameIntent,
  FrameStepCompletedEvent→FrameAck egress; no tracking state
- TimeNetworkModule: CreateMasterLockstepTranslator + CreateSlaveLockstepTranslator;
  CreateLockstepTranslator marked [Obsolete]
- TimeControllerFactory: Master+Continuous→MasterSyncController,
  Slave+Any→SlaveSyncController; Standalone unchanged
- 13 new tests (9 translator + 4 factory); all 124 pass
- TASK-TRACKER: TCU-TR001, TCU-TR002, TCU-TR003, TCU-W005, TCU-T003, TCU-T004 done
```
