# BATCH-18: Phase 8 Final Task (ANC-P8-04 Networked Stage-2 Integration Suite)

**Batch Number:** BATCH-18  
**Tasks:** ANC-P8-04  
**Phase:** Phase 8 — Stride backend + networked stage-2  
**Estimated Effort:** 12-18 hours  
**Priority:** HIGH (final task — completes all animation control work)  
**Dependencies:** All Phases 0-7 complete; Phase 8 Part 1 (ANC-P8-01/02/03) complete (BATCH-15); Phase 6 replication translators complete (BATCH-14)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch implements the **final task** in the animation control feature: the networked stage-2 integration suite. This validates the full Brain↔Muscle DDS replication pipeline end-to-end.

The suite lives in `Hrot.Animation.Network.Integration.Tests` — a new test project that reuses the eight scenarios from the networkless stage-1 suite (Phase 7) but exercises them over the actual replication translators (DD-2 Phases 6).

**MANDATORY WORKFLOW:** Implement the fixture → verify build → implement all 8 scenarios → verify all 8 pass.

**DO NOT** stop and ask for permission to fix compilation errors or run tests. Finish implementation and report when complete.

### Required Reading (IN ORDER)

1. **Task Detail:** `.dev/anim-ctrl/TASK-DETAIL.md` — ANC-P8-04 definition (Phase 8 section)
2. **Test Design Doc:** `.dev/anim-ctrl/DD-Tests_AnimationControl_v1_1.md` — §10 (Stage-2 networked variant)
3. **Replication Design:** `.dev/anim-ctrl/DD-2_AnimationReplication_v1_1.md` — §8 (round-trip topology)
4. **Stage-1 Scenarios Reference:**
   - `Hrot/Subsystems/Hrot.Animation.Integration.Tests/AnimationIntegrationScenarios.cs` (8 scenarios to port)
   - `Hrot/Subsystems/Hrot.Animation.Integration.Tests/AnimationIntegrationFixture.cs` (fixture pattern)
5. **HrotRunnerHarness Reference:**
   - `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/HrotRunnerHarness.cs` (harness pattern)
6. **Replication Translators (Phase 6):**
   - `Hrot/Subsystems/Hrot.Animation.Replication/` (all translators: channels, descriptors, events, side-buffers)
7. **BATCH-14 Review:** `.dev/anim-ctrl/reviews/BATCH-14-REVIEW.md` (Phase 6 approval, confirms all translators work)

### Source Code Locations

**New project to create:**
- `Hrot/Subsystems/Hrot.Animation.Network.Integration.Tests/Hrot.Animation.Network.Integration.Tests.csproj`
- `Hrot/Subsystems/Hrot.Animation.Network.Integration.Tests/NetworkedAnimationScenarios.cs` — 8 scenario tests
- `Hrot/Subsystems/Hrot.Animation.Network.Integration.Tests/Harness/AnimationNetworkLoopbackFixture.cs` — Two-world loopback harness
- `Hrot/Subsystems/Hrot.Animation.Network.Integration.Tests/Harness/AnimationTestHelpers.cs` — Test helpers (adapted from stage-1)
- `Hrot/Subsystems/Hrot.Animation.Network.Integration.Tests/Data/TestData.cs` — Inline TKB test data

**Reference projects:**
- `Hrot/Subsystems/Hrot.Animation.Integration.Tests/` (stage-1 suite to mirror)
- `Hrot/Subsystems/Hrot.Animation.Replication/` (translators under test)

### Report Submission

Write report to:
`.dev/anim-ctrl/reports/BATCH-18-REPORT.md`

---

## Context

**Why this is the final task:** All eight phases of animation control are complete. This suite validates the replication layer (Phase 6) end-to-end by replaying the same 8 behavioral scenarios under DDS round-trip conditions. It proves that Brain→Muscle intent replication and Muscle→Brain status/event replication work correctly together.

**Architecture (per DD-Tests §10 + DD-2 §8):**
- Two `EntityRepository` instances (BrainWorld, MuscleWorld) in one process
- Brain egress translators capture intent (CapturingWriter pattern — no real DDS)
- Ingress translators on Muscle side apply captured intent
- Muscle animation systems execute (same 8 systems as stage-1)
- Muscle egress translators capture status and events
- Brain ingress translators apply captured status/events to BrainWorld
- Round-trip latency: ~2 ticks (1 Brain→Muscle, 1 Muscle→Brain)
- PumpUntil budgets add 4-6 extra frames over stage-1 equivalents

---

## 📐 Task Specification

### ANC-P8-04 — Networked Stage-2 Integration Suite

**Refs:** DD-Tests §10; DD-2 §8.

Create `Hrot.Animation.Network.Integration.Tests` containing:

#### Loopback Fixture: `AnimationNetworkLoopbackFixture`

Two-world in-process loopback. Does NOT use live DDS (`HrotRunnerHarness` would require DDS which is expensive; use the CapturingWriter pattern used in other replication tests).

Key components:
- `BrainWorld` + `MuscleWorld`: separate `EntityRepository` instances
- Brain egress: `AnimationChannelIntentEgressTranslator`, `LookAtChannelIntentEgressTranslator`, `StanceIntentEgressTranslator`, `AnimationMontageQueueEgressTranslator` (all reading BrainWorld)
- Muscle ingress: corresponding `*IngressTranslator`s applying to MuscleWorld
- All 8 Muscle animation systems (same as stage-1 fixture)
- Muscle egress: `AnimationChannelStatusEgressTranslator`, `LookAtChannelStatusEgressTranslator`, `StanceStatusEgressTranslator`
- Muscle event egress: all 7 `INetworkEventTranslator` pairs (not FootstepEvent)
- Brain ingress: corresponding ingress translators applying to BrainWorld
- `SpawnPairedHumanoid(netId)`: creates entity in both worlds with matching network IDs
- `PumpFrame()`: one tick (Brain egress → route → Muscle systems → Muscle egress → route to Brain)
- `PumpUntil(predicate, maxFrames, conditionName)`: frame-budgeted wait

#### The Eight Networked Scenarios

Each mirrors the corresponding stage-1 scenario with:
1. Intent authored on BrainWorld (Brain-side)
2. Assertions on BrainWorld (Brain-side observations of replicated status/events)
3. Extra frames budget (+4-6 per PumpUntil) to absorb round-trip latency

| Scenario | Stage-1 Name | Networked Assertion |
|----------|-------------|---------------------|
| S1 | `PlayMontage_RunsToCompletionAndReportsSuccess` | Brain sees `AnimationChannel.Status == Success` + `MontageEndedEvent{NaturalEnd}` on BrainWorld bus |
| S2 | `PlayMontage_NotifyFiresAtAuthoredKeyframe` | `AnimNotifyEvent{MagOut}` appears on BrainWorld bus with correct `MarkerHash` |
| S3 | `StopMontage_MidPlayInterruptsAndPublishesInterruptedEvent` | `MontageEndedEvent{Interrupted}` on Brain bus after stop |
| S4 | `StanceIntent_DrivesTransitionAndPublishesStanceChangedEvent` | Brain sees `StanceStatus.CurrentStance == Crouched` replicated |
| S5 | `PlayMontageQueue_ThreeEntriesPlaysInOrderAndReportsOneSuccess` | Brain sees final `Status == Success` after queue completes |
| S6 | `EnqueueMontage_DuringActiveQueueAppendsAndPlays` | Queue state replicates correctly (Brain observes `QueueVersion` bump + eventual Success) |
| S7 | `Locomotion_DrivesFootstepEventsAtCorrectCadence` | FootstepEvent does NOT appear on Brain bus (`PropagatesAcrossNodes=false`) |
| S8 | `LookAtPoint_AcquiresAndReleasesAimWithStatusTransitions` | Brain sees `LookAtChannel.Status == Running` on acquire, `Success` on release |

**S7 Note:** FootstepEvent does not have a translator (DD-3 §5.2, `PropagatesAcrossNodes=false`). The S7 scenario asserts that footstep events do NOT appear on Brain bus — this is the Muscle-local-only behavior verification.

#### Success Criteria

- ✅ 8/8 networked scenarios pass
- ✅ Round-trip latency absorbed by +4-6 frame budgets per `PumpUntil`
- ✅ Brain-side assertions only (assertions on BrainWorld, not MuscleWorld directly) — this proves replication works
- ✅ S7 confirms FootstepEvent stays Muscle-local (Brain bus clean)
- ✅ Build succeeds: `dotnet build IOS-IG-SimHost.sln -c Debug --no-restore -maxcpucount:4` → 0 errors
- ✅ Tests run under 500ms total (in-process loopback, no real DDS)

---

## ✅ Quality Standards

**Test Quality:**
- Tests assert Brain-side observations, not Muscle-side state — this is the key distinction from stage-1
- Round-trip latency is explicit: PumpUntil budgets are stage-1-budget + RoundTripBuffer (documented constant)
- S7 explicitly asserts FootstepEvent absence on Brain bus (DD-3 §5.2 verified)
- All assertions check actual field values (Status enum, EndReason enum, entity IDs, MontageIds)
- No smoke-only tests: every test verifies the replication path end-to-end

**Architecture:**
- No real DDS (CapturingWriter, not CycloneDDS) — keeps tests fast and deterministic
- Two-world topology is the minimal representative test of the replication contracts
- All 15 replication translators (DD-2 §6.1) exercised across the 8 scenarios

**Build & Regression:**
- All existing tests must remain green (0 regressions)
- New suite: 8 tests, all passing

---

## 📝 Completion Checklist (For Developer)

- [ ] `AnimationNetworkLoopbackFixture` implemented (Brain+Muscle worlds, all egress/ingress translators, PumpFrame, PumpUntil)
- [ ] `SpawnPairedHumanoid` creates entity in both worlds with matching network IDs
- [ ] All 8 scenarios implemented with Brain-side assertions
- [ ] S7 confirms FootstepEvent Muscle-local (no Brain bus propagation)
- [ ] All 8 tests pass: `dotnet test Hrot.Animation.Network.Integration.Tests` → 8/8
- [ ] Full solution build clean: 0 new errors
- [ ] Regression check: stage-1 tests still pass (8/8)
- [ ] Report written: `.dev/anim-ctrl/reports/BATCH-18-REPORT.md`
- [ ] Git committed with clean commit message

**When complete: ANC-P8-04 done = 58/58 tasks complete. Animation control feature delivery is DONE.**
