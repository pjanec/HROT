# BATCH-13: Phase 6 — Animation Replication (ANC-P6-01 to ANC-P6-06)

**Batch Number:** BATCH-13  
**Tasks:** ANC-P6-01, ANC-P6-02, ANC-P6-03, ANC-P6-04, ANC-P6-05, ANC-P6-06  
**Phase:** Phase 6 — Replication (DD-2)  
**Estimated Effort:** 14–18 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-12 APPROVED (Phase 7 networkless stage-1 complete)

---

## 📋 Onboarding & Workflow

### Developer Instructions

Phase 6 implements cross-node DDS replication of the animation Brain↔Muscle contract (DD-2).
You will create a new project `Hrot.Animation.Replication` under `Hrot/Subsystems/` containing:
- Channel intent/status translators for `AnimationChannel` and `LookAtChannel`
- Descriptor translators for `StanceIntent`/`StanceStatus`
- Side-buffer translators for `AnimationMontageQueue`/`AnimationMontageQueueState`
- Seven event translator pairs (one per cross-node event)
- Topic/QoS registration and observability hooks

**Do not stop and ask for permission to run tests, fix compilation errors, or proceed with obvious implementation steps. Complete all tasks, fix all issues, run all tests, and then write the report. No partial submissions.**

### Required Reading (IN ORDER)

1. **Workflow guide:** `.dev/.guides/DEV-GUIDE.md` — batch workflow
2. **Task definitions:** `.dev/anim-ctrl/TASK-DETAIL.md` — Phase 6 section (lines ~353–430)
3. **Design document:** `.dev/anim-ctrl/DD-2_AnimationReplication_v1_1.md` — the authoritative spec
4. **Previous review:** `.dev/anim-ctrl/reviews/BATCH-12-REVIEW.md`

### Source Code Location

**Primary work area (NEW project to create):**  
`Hrot/Subsystems/Hrot.Animation.Replication/`

**Existing animation contracts (read):**  
`Hrot/Subsystems/Hrot.MuscleCharacter.Animation/`

**Existing translator patterns (study these before implementing):**  
- Descriptor egress: `Hrot/Network/Hrot.Network.NED/Replication/Map/Egress/NavigationIntentEgressTranslator.cs`
- Descriptor status: `Hrot/Network/Hrot.Network.NED/Replication/Map/Egress/NavigationStatusEgressTranslator.cs`
- Event translator: `Hrot/Network/Hrot.Network.NED/Replication/Map/FireInteractionEventTranslator.cs`
- Module registration: `Hrot/Network/Hrot.Network.NED/Replication/NedReplicationModule.cs`

**Existing translator interfaces (read):**  
- `FDP/Engine/Fdp.Core/Abstractions/IDescriptorTranslator.cs`
- `FDP/Engine/Fdp.Core/Abstractions/INetworkTranslator.cs`
- `FDP/Engine/Fdp.Core/Abstractions/INetworkEventTranslator.cs`

**Solution file:** `IOS-IG-SimHost.sln`

### Report Submission

**When done, submit report to:**  
`.dev/anim-ctrl/reports/BATCH-13-REPORT.md`

**If you have questions, create:**  
`.dev/anim-ctrl/questions/BATCH-13-QUESTIONS.md`

---

## Context

Phase 6 connects the animation system to the network. The animation contract (DD-1) defines
8 components and events that cross the Brain↔Muscle DDS boundary. DD-2 specifies all
translator shapes, QoS settings, dirty-detection logic, and the partial-serialization scheme
for the montage queue side-buffer.

**Key insight from DD-2 §2.4 (IMPORTANT before implementing):**
Prior to implementing, verify whether `LocomotionChannel`/`WeaponChannel` channel translators
still exist (DD-0-08 spike noted they may have been deleted). Search for `LocomotionChannelIntentEgress`
or `LocomotionChannel*Egress` and `WeaponChannel*Egress` in the codebase. If they exist,
use them as the template for `AnimationChannel` and `LookAtChannel` translators. If they were
deleted, use `NavigationIntentEgressTranslator` and `NavigationStatusEgressTranslator` as the
descriptor-translator pattern instead (the only difference is SmartEgress uses `ActionInstanceId`
as the dirty signal instead of `IntentId`).

**Related Tasks:**
- [ANC-P6-01](../TASK-DETAIL.md#anc-p6-01--animationchannel-intentstatus-translators) — AnimationChannel translators
- [ANC-P6-02](../TASK-DETAIL.md#anc-p6-02--lookatchannel-intentstatus-translators) — LookAtChannel translators
- [ANC-P6-03](../TASK-DETAIL.md#anc-p6-03--stance-descriptor-translators) — Stance translators
- [ANC-P6-04](../TASK-DETAIL.md#anc-p6-04--side-buffer-replication) — MontageQueue side-buffer
- [ANC-P6-05](../TASK-DETAIL.md#anc-p6-05--seven-event-translator-pairs) — 7 event translator pairs
- [ANC-P6-06](../TASK-DETAIL.md#anc-p6-06--topicqos-registration--observability) — Topic registration + observability

---

## 🎯 Batch Objectives

1. Create `Hrot.Animation.Replication` project with correct project references
2. Implement 8 component/descriptor translators (4 egress + 4 ingress pairs, 8 topics)
3. Implement 7 event translator pairs (egress + ingress, 7 topics = 15 topics total)
4. Implement `AnimationReplicationModule` that registers all 15 topics
5. Implement partial serializer for `AnimationMontageQueue` (only `Count` live entries)
6. Add observability counters (publish rate, bandwidth, dirty false-positive)
7. Write unit tests verifying serialization round-trips and dirty-detection logic

---

## ✅ Tasks

### Task 1: Create `Hrot.Animation.Replication` project (ANC-P6-01 foundation)

**File:** `Hrot/Subsystems/Hrot.Animation.Replication/Hrot.Animation.Replication.csproj` (NEW PROJECT)

**Requirements:**
- Target net8.0, `AllowUnsafeBlocks=true`, `Nullable=enable`, `TreatWarningsAsErrors=true`
- Project references:
  - `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Hrot.MuscleCharacter.Animation.csproj`
  - `FDP/Engine/Fdp.Core/Fdp.Core.csproj`
  - `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj`
  - `FDP/Network/Fdp.Network.Cyclone/Fdp.Network.Cyclone.csproj` (for DdsParticipant + CycloneNativeEventTranslator)
- Add the project to `IOS-IG-SimHost.sln`
- Namespace root: `Hrot.Animation.Replication`

**Folder layout:**
```
Hrot/Subsystems/Hrot.Animation.Replication/
  Hrot.Animation.Replication.csproj
  AnimationReplicationModule.cs
  Translators/
    Channels/
      AnimationChannelIntentEgressTranslator.cs
      AnimationChannelIntentIngressTranslator.cs
      AnimationChannelStatusEgressTranslator.cs
      AnimationChannelStatusIngressTranslator.cs
      LookAtChannelIntentEgressTranslator.cs
      LookAtChannelIntentIngressTranslator.cs
      LookAtChannelStatusEgressTranslator.cs
      LookAtChannelStatusIngressTranslator.cs
    Descriptors/
      StanceIntentEgressTranslator.cs
      StanceIntentIngressTranslator.cs
      StanceStatusEgressTranslator.cs
      StanceStatusIngressTranslator.cs
    SideBuffers/
      AnimationMontageQueueEgressTranslator.cs
      AnimationMontageQueueIngressTranslator.cs
      AnimationMontageQueueStateEgressTranslator.cs
      AnimationMontageQueueStateIngressTranslator.cs
      QueueWirePayload.cs
    Events/
      MontageStartedEventTranslator.cs
      MontageEndedEventTranslator.cs
      MontageSectionAdvancedEventTranslator.cs
      StanceChangedEventTranslator.cs
      HitWindowOpenedEventTranslator.cs
      HitWindowClosedEventTranslator.cs
      AnimNotifyEventTranslator.cs
```

### Task 2: Channel translators (ANC-P6-01, ANC-P6-02)

**Spec:** DD-2 §2.1–2.3  
**Task Definition:** TASK-DETAIL.md lines 358–379

**AnimationChannel Intent egress (Brain → Muscle):**
- Topic: `hrot/anim/intent/AnimationChannel`
- Dirty signal: change in `(ActiveAction, ActionInstanceId, ActionParams[..], BehaviorInstanceId)`
- Payload: `{ Entity, ActiveAction, ActionInstanceId, BehaviorInstanceId, ActionParams[32B] }`
- QoS: Reliable, TransientLocal
- Does NOT touch `DispatchedInstanceId` (Muscle-side state)

**AnimationChannel Status egress (Muscle → Brain):**
- Topic: `hrot/anim/status/AnimationChannel`
- Dirty signal: change in `(Status, DispatchedInstanceId)`
- Payload: `{ Entity, Status, DispatchedInstanceId }`
- QoS: Reliable, TransientLocal

**LookAtChannel:** Same pattern, different topics:
- `hrot/anim/intent/LookAtChannel`, `hrot/anim/status/LookAtChannel`
- Intent payload includes entity-ref params; resolve via `NetworkEntityMap` on ingress

**Implement SmartEgress:** Each egress translator stores the last-published key value in a `Dictionary<Entity, T>` (same as `NavigationIntentEgressTranslator._lastPublishedIntentId`). For `AnimationChannel`, use `ActionInstanceId` as the dirty key.

**Tests required (in `Hrot.Animation.Replication.Tests`):**
- Intent egress: fires on `ActionInstanceId` change, NOT on `DispatchedInstanceId` change
- Status egress: fires on `Status` change, NOT on unrelated field change
- Ingress: writes correct fields without touching reserved fields

### Task 3: Stance descriptor translators (ANC-P6-03)

**Spec:** DD-2 §3  
**Task Definition:** TASK-DETAIL.md lines 380–393

- `StanceIntentEgress` / `StanceIntentIngress` — topic `hrot/anim/StanceIntent`, Reliable, TransientLocal
- `StanceStatusEgress` / `StanceStatusIngress` — topic `hrot/anim/StanceStatus`, Reliable, TransientLocal
- **Critical:** `StanceStatusEgress` dirty trigger is `(Phase, CurrentStance, AckVersion)` — NOT `TransitionProgress`. `TransitionProgress` rides in the payload but does not drive egress firing.

**Tests required:**
- Stance status egress: fires on Phase change; does NOT fire on `TransitionProgress` change alone
- Round-trip: `StanceIntent` write on Brain ghost appears on Muscle ghost after ingress

### Task 4: Side-buffer replication (ANC-P6-04)

**Spec:** DD-2 §4 (§4.1–4.5)  
**Task Definition:** TASK-DETAIL.md lines 394–420

This is the novel piece. Implement:

**`QueueWirePayload` struct:**
```csharp
// Wire format per DD-2 §4.2
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct QueueWirePayload
{
    public long EntityId;       // 8 bytes
    public uint QueueVersion;   // 4 bytes
    public byte Count;          // 1 byte
    public fixed byte Padding[3]; // 3 bytes alignment
    // Followed by Count * sizeof(MontageQueueEntry) bytes = up to 128 bytes
    // Total max: 12 + 8 * 16 = 140 bytes
}
```

**`AnimationMontageQueueEgress`:**
- Topic: `hrot/anim/MontageQueue`, Reliable, TransientLocal
- Dirty signal: `QueueVersion != lastPublishedQueueVersion`
- Serializes only `Count` live entries (partial serialization — see DD-2 §4.2)
- Maximum payload: 140 bytes (12 + 8 * 16)

**`AnimationMontageQueueIngress`:**
- Deserializes `Count` entries, writes to receiving entity's inline array
- Zeros the unused tail entries (8 - Count)

**`AnimationMontageQueueStateEgress`:**
- Topic: `hrot/anim/MontageQueueState`, Reliable, TransientLocal
- Dirty trigger: `(CurrentEntryIndex, InBlendOutWindow)` only
- `EntryElapsedSeconds` rides in payload but does NOT drive dirty trigger (same pattern as StanceStatus.TransitionProgress)
- `ObservedQueueVersion` is Muscle-internal — NOT replicated

**Tests required:**
- Serializer: `Count=3` produces ≤60B payload, deserializes with entries 0–2 set, entries 3–7 zeroed
- Egress: fires on `QueueVersion` bump; does NOT fire on `EntryElapsedSeconds` change alone
- Round-trip: queue with 2 entries serializes and deserializes correctly

### Task 5: Seven event translator pairs (ANC-P6-05)

**Spec:** DD-2 §5  
**Task Definition:** TASK-DETAIL.md lines 421–435

Use `CycloneNativeEventTranslator<TEcs, TDds>` as the base class (pattern from `FireInteractionEventTranslator`).
All seven events: Reliable, Volatile, keyed on `Target` (Entity).

| Event type | Topic | Approx wire size |
|---|---|---|
| `MontageStartedEvent` | `hrot/anim/MontageStarted` | 17 bytes |
| `MontageEndedEvent` | `hrot/anim/MontageEnded` | 18 bytes |
| `MontageSectionAdvancedEvent` | `hrot/anim/MontageSectionAdv` | 14 bytes |
| `StanceChangedEvent` | `hrot/anim/StanceChanged` | 10 bytes |
| `HitWindowOpenedEvent` | `hrot/anim/HitWindowOpened` | 13 bytes |
| `HitWindowClosedEvent` | `hrot/anim/HitWindowClosed` | 13 bytes |
| `AnimNotifyEvent` | `hrot/anim/AnimNotify` | 24 bytes |

**`FootstepEvent` has NO translator** — it's local-only per DD-3 §5.2. Do not implement one.

**For each event translator:**
- Implement `TryEncode(in EcsEvent, out DdsEvent)` — serialize all fields
- Implement `TryDecode(in DdsEvent, out EcsEvent)` — deserialize all fields
- You will need DDS message types (`DdsMontageStartedEvent`, etc.) — define them as `[StructLayout(Sequential)]` unmanaged structs in the same file or a `DdsMessages.cs` file (they don't need to be IDL-generated since they're used only in this assembly)

**Tests required:**
- Per-event: round-trip `TryEncode` → `TryDecode` preserves all fields
- `FootstepEvent` has no translator (verify by asserting it doesn't appear in the module's event translator list)

### Task 6: Topic/QoS registration + observability (ANC-P6-06)

**Spec:** DD-2 §6, §9  
**Task Definition:** TASK-DETAIL.md lines 436–448

**`AnimationReplicationModule` class:**
- Implements `IEcsModule` (or equivalent module interface used in this codebase — check `NedReplicationModule`)
- Accepts `NodeRole` to determine which translators to register (Brain = egress intent, Muscle = egress status)
- Registers all 15 topics with correct QoS
- Exposes all translators as an `IReadOnlyList<INetworkTranslator>` for external collection

**Observability — per-translator counters (already in `INetworkTranslator`):**
- `SentSampleCount` — increment on each publish
- `ReceivedSampleCount` — increment on each valid ingress
- Additionally, expose per-topic `DirtyFalsePositiveCount` — increment when egress checks dirty but finds no change (fine-grained filter kills the publish). This is a `long` field on the egress translator, not part of the interface.

**Topic table test:**
Assert that `AnimationReplicationModule.AllTranslators` contains exactly 15 translators with the correct `TopicName` values (see DD-2 §6 table). This is the "15 topics test" required by ANC-P6-06 success criteria.

---

## 🧪 Testing Requirements

**Project:** `Hrot/Subsystems/Hrot.Animation.Replication.Tests/` (NEW)

**Required test coverage (minimum 20 unit tests):**

1. `AnimationChannel` intent egress — fires on ActionInstanceId change (1 test)
2. `AnimationChannel` intent egress — does NOT fire on DispatchedInstanceId change (1 test)
3. `AnimationChannel` status egress — fires on Status change (1 test)
4. `AnimationChannel` round-trip intent (encode + decode) (1 test)
5. `LookAtChannel` intent round-trip (1 test)
6. `StanceStatus` egress — fires on Phase change (1 test)
7. `StanceStatus` egress — does NOT fire on TransitionProgress change alone (1 test)
8. `AnimationMontageQueue` serializer — Count=3 payload ≤60B (1 test)
9. `AnimationMontageQueue` serializer — round-trip 3 entries, tail zeroed (1 test)
10. `AnimationMontageQueue` egress — fires on QueueVersion bump (1 test)
11. `AnimationMontageQueue` egress — does NOT fire on Count alone (without QueueVersion bump) (1 test)
12. `AnimationMontageQueueState` egress — fires on CurrentEntryIndex change (1 test)
13. `AnimationMontageQueueState` egress — does NOT fire on EntryElapsedSeconds change alone (1 test)
14. All 7 event translators — round-trip TryEncode/TryDecode for each (7 tests)
15. Topic table — AnimationReplicationModule has exactly 15 translators (1 test)

**Total: at least 20 tests**

**Test quality bar:**
- Tests must verify actual field values after round-trip, not just "does not throw"
- Dirty-detection tests must verify NO publish occurred (check `SentSampleCount` stays at 0)

---

## ⚠️ Quality Standards

**❗ IMPORTANT — look before coding:**

Before implementing, search the codebase for:
- `LocomotionChannel` or `WeaponChannel` egress translators to find the exact channel-translator pattern
- `DdsParticipant` usage in `Fdp.Network.Cyclone` to understand topic creation API
- `CycloneNativeEventTranslator<,>` to understand the base class for event translators
- `SmartEgress` or `QueryDelta` patterns to understand dirty detection

The design doc (DD-2) is the authoritative spec. Do not deviate from it unless you find a genuine
implementation conflict — in that case, document the conflict in the report.

**❗ TEST QUALITY:**
- Dirty-detection tests MUST verify `SentSampleCount` did NOT increment
- Round-trip tests MUST verify each field by name, not just "fields are equal"
- Do not write "object exists" tests

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `Hrot.Animation.Replication` project compiles with zero errors
- [ ] All 15 topics registered with correct QoS (verified by topic-table test)
- [ ] All 4 channel translators (intent + status for AnimationChannel + LookAtChannel) implemented and tested
- [ ] All 4 descriptor translators (StanceIntent + StanceStatus) implemented and tested
- [ ] All 4 side-buffer translators (MontageQueue + MontageQueueState) implemented and tested
- [ ] Partial serializer: `Count=N` ships N*16 bytes of entries, tail zeroed on ingress
- [ ] All 7 event translator pairs implemented and tested (no FootstepEvent translator)
- [ ] `AnimationReplicationModule` registers all 15 translators
- [ ] Minimum 20 unit tests passing
- [ ] `dotnet build IOS-IG-SimHost.sln` completes without errors
- [ ] Report submitted

---

## 📚 Reference Materials

- **Design:** `.dev/anim-ctrl/DD-2_AnimationReplication_v1_1.md`
- **Task defs:** `.dev/anim-ctrl/TASK-DETAIL.md` — Phase 6 section (~lines 353–448)
- **Events (source):** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Events/AnimationEvents.cs`
- **Components (source):** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Components/ReplicatedComponents.cs`
- **NavigationIntentEgressTranslator (pattern):** `Hrot/Network/Hrot.Network.NED/Replication/Map/Egress/NavigationIntentEgressTranslator.cs`
- **FireInteractionEventTranslator (pattern):** `Hrot/Network/Hrot.Network.NED/Replication/Map/FireInteractionEventTranslator.cs`
- **NedReplicationModule (registration pattern):** `Hrot/Network/Hrot.Network.NED/Replication/NedReplicationModule.cs`
- **IDescriptorTranslator:** `FDP/Engine/Fdp.Core/Abstractions/IDescriptorTranslator.cs`
- **INetworkTranslator:** `FDP/Engine/Fdp.Core/Abstractions/INetworkTranslator.cs`
- **Previous review:** `.dev/anim-ctrl/reviews/BATCH-12-REVIEW.md`

---

## 🔄 MANDATORY WORKFLOW

1. Read DD-2 fully before writing code
2. Study `NavigationIntentEgressTranslator` and `FireInteractionEventTranslator` patterns
3. Create project + folder structure
4. Implement channel translators → tests passing
5. Implement stance translators → tests passing
6. Implement side-buffer serializer → tests passing
7. Implement event translators → tests passing
8. Implement `AnimationReplicationModule` → topic-table test passing
9. Full solution build: `dotnet build IOS-IG-SimHost.sln`
10. Write report

Do not stop between steps for permission. Complete everything and then report.
