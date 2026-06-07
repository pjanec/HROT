# BATCH-16 Report — ANC-P8-04: Networked Brain↔Muscle Stage-2 Integration Suite

**Batch**: BATCH-16
**Task**: ANC-P8-04
**Status**: COMPLETE — all 8 scenarios pass, all regression suites green, solution builds clean.

---

## 1. Summary

Implemented a full in-process loopback integration test suite that proves the 8
existing phase-7 animation scenarios work across a Brain↔Muscle DDS replication
round-trip. No live CycloneDDS participant is required; two separate
`EntityRepository` instances communicate through the actual translator encode/decode
seams.

---

## 2. Files Created / Modified

| File | Action | Description |
|---|---|---|
| `Hrot/Subsystems/Hrot.Animation.Replication/Hrot.Animation.Replication.csproj` | Modified | Added `InternalsVisibleTo` for `Hrot.Animation.Network.Integration.Tests` |
| `Hrot/Subsystems/Hrot.Animation.Network.Integration.Tests/Hrot.Animation.Network.Integration.Tests.csproj` | Created | New test project (net8.0, unsafe, xUnit 2.6.6) |
| `Hrot/Subsystems/Hrot.Animation.Network.Integration.Tests/Data/TestData.cs` | Created | Local copy of stage-1 TestData (self-contained, no Hrot.SimHost dependency) |
| `Hrot/Subsystems/Hrot.Animation.Network.Integration.Tests/Harness/AnimationTestHelpers.cs` | Created | Trimmed local copy of stage-1 command helpers |
| `Hrot/Subsystems/Hrot.Animation.Network.Integration.Tests/Harness/AnimationNetworkLoopbackFixture.cs` | Created | Two-node loopback fixture (BrainWorld + MuscleWorld + all translators) |
| `Hrot/Subsystems/Hrot.Animation.Network.Integration.Tests/NetworkedAnimationScenarios.cs` | Created | 8 networked scenario tests (ANC-P8-04) |
| `IOS-IG-SimHost.sln` | Modified | Added `Hrot.Animation.Network.Integration.Tests` project + config entries |

---

## 3. Architecture: Two-Node Loopback Harness

### World layout

```
Brain EntityRepository                    Muscle EntityRepository
  AnimationChannel (intent)     ──────>     AnimationChannel (full pipeline)
  LookAtChannel (intent)                     LookAtChannel (full pipeline)
  StanceIntent                               StanceIntent + StanceStatus
  AnimationMontageQueue                      AnimationMontageQueue + State
  NetworkIdentity                            CharacterAnimationDefRuntime
                                             AnimationExecutorState
                                             LookAtExecutorState
                                             ActorCapabilityState
                                             NetworkIdentity
```

### Per-tick sequence (DD-2 §8)

1. **Brain egress**: `CapturingWriter<T>` captures intent DDS messages
   (`AnimationChannelIntent`, `LookAtChannelIntent`, `StanceIntent`, `MontageQueue`)
2. **Route Brain→Muscle**: intent ingress `ProcessSample` applies via `EntityCommandBuffer.Playback`
3. **Muscle animation systems execute** (same 9 systems as stage-1 fixture)
4. **Muscle egress**: `CapturingWriter<T>` captures status DDS messages
   (`AnimationChannelStatus`, `LookAtChannelStatus`, `StanceStatus`)
5. **Capture Muscle events** from readable bus (events from previous tick)
6. **Route Muscle→Brain**: status ingress `ProcessSample` + event `EncodeForTest`/`DecodeForTest`/`Publish`
7. **Swap both event buses**

### Round-trip latency

- **Component status**: 1 tick (Muscle component updated in step 3 → captured in step 4 → Brain updated in step 6)
- **Events**: 2 ticks (event published to Muscle write buffer in step 3 → readable after step 7 → captured and routed to Brain in tick N+1 step 5-6 → readable on Brain after step 7 of tick N+1)

Tests that assert on both component status AND events use a combined `PumpUntil`
condition that exits only when both are satisfied (S1, S4). Tests that assert
only on events exit when the event is received (S2, S3, S5, S6, S7, S8).

Extra frame budget: all `PumpUntil` calls add `RoundTripBuffer = 6` extra frames
over stage-1 equivalents.

---

## 4. Scenario Matrix

| # | Test Method | Brain Intent | Brain-side Assertion | Passes |
|---|---|---|---|---|
| S1 | `Networked_PlayMontage_RunsToCompletionAndBrainSeesSuccess` | PlayMontage(Walk) | `AnimationChannel.Status == Success` + `MontageEndedEvent(NaturalEnd)` on Brain bus | YES |
| S2 | `Networked_PlayMontage_NotifyFiresOnBrainBus` | PlayMontage(Run) | `AnimNotifyEvent(MagOut)` on Brain bus with correct MontageId | YES |
| S3 | `Networked_StopMontage_MidPlayBrainSeesInterruptedEvent` | PlayMontage(Walk) then StopMontage | `MontageEndedEvent(Interrupted)` on Brain bus; `Status != Running` | YES |
| S4 | `Networked_StanceIntent_BrainSeesReplicatedStanceStatus` | SetStance(Crouched) | `StanceStatus.CurrentStance == Crouched` + `StanceChangedEvent(Standing→Crouched)` on Brain bus | YES |
| S5 | `Networked_PlayMontageQueue_BrainSeesThreeEndedEventsInOrder` | PlayMontageQueue([Walk,Run,Run]) | 3 × `MontageEndedEvent(NaturalEnd)` in QueueIndex order on Brain bus | YES |
| S6 | `Networked_EnqueueMidPlay_BrainSeesBothEndedEvents` | PlayMontage(Walk) + EnqueueMontage(Run) | `MontageEndedEvent(Walk)` before `MontageEndedEvent(Run)` on Brain bus | YES |
| S7 | `Networked_Locomotion_BrainSeesFootstepEvents` | PlayMontage(Walk) | ≥3 footstep `AnimNotifyEvent`s on Brain bus; all Target == brainEntity | YES |
| S8 | `Networked_LookAt_BrainSeesReplicatedStatusTransitions` | AcquireLookAt → ReleaseLookAt | `LookAtChannel.Status`: Failure→Running→Success, all via Muscle replication | YES |

---

## 5. Command Output Summaries

### 5.1 New project: `dotnet test Hrot.Animation.Network.Integration.Tests`

```
Passed!  - Failed: 0, Passed: 8, Skipped: 0, Total: 8, Duration: 216 ms
```

### 5.2 Regression: `dotnet test Hrot.Animation.Replication.Tests`

```
Passed!  - Failed: 0, Passed: 42, Skipped: 0, Total: 42, Duration: 139 ms
```

### 5.3 Regression: `dotnet test Hrot.MuscleCharacter.Animation.Stride.Tests`

```
Passed!  - Failed: 0, Passed: 31, Skipped: 0, Total: 31, Duration: 85 ms
```

### 5.4 Full solution build: `dotnet build IOS-IG-SimHost.sln -c Debug`

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:45.56
```

---

## 6. Design Decisions

### 6.1 In-process loopback (no live CycloneDDS)

Using two `EntityRepository` instances with internal translator seams
(`CapturingWriter<T>` + `ProcessSample` + `EncodeForTest`/`DecodeForTest`) keeps
tests CI-safe, deterministic, and dependency-free. No native DDS libraries needed.

### 6.2 Self-contained project (no reference to Hrot.Animation.Integration.Tests)

`Hrot.Animation.Integration.Tests` references `Hrot.SimHost` (heavyweight). The
new project copies only the 60 lines of `TestData` and `AnimationTestHelpers` it
actually needs. This keeps the dependency graph minimal and build times fast.

### 6.3 Event bus timing (2-tick event delay)

Muscle events are published to the Muscle write buffer during system execution.
They become readable only after `MuscleWorld.Bus.SwapBuffers()` at end of the
same tick. Brain therefore receives events 2 ticks after they're generated
(compared to 1 tick for component status). Tests that assert on both component
state AND events use a combined condition in `PumpUntil` to handle this
deterministically.

### 6.4 `NetworkEntityMap.Unregister` in `ResetWorlds`

`NetworkEntityMap` has no `Clear()` method. `ResetWorlds()` uses `Unregister`
(which allows re-registration by removing the netId from the graveyard when
`Register` is called again).

---

## 7. Test Counts Summary

| Suite | Tests | Status |
|---|---|---|
| Hrot.Animation.Network.Integration.Tests (new, ANC-P8-04) | 8 | All pass |
| Hrot.Animation.Replication.Tests (regression) | 42 | All pass |
| Hrot.MuscleCharacter.Animation.Stride.Tests (regression) | 31 | All pass |
| **Total** | **81** | **All pass** |
