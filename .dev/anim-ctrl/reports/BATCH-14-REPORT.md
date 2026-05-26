# BATCH-14 Report

**Batch:** BATCH-14  
**Tasks:** Corrective Task 0 + ANC-P6-02, ANC-P6-04, ANC-P6-06 completion hardening  
**Status:** COMPLETE

---

## Files Changed (by task)

### Corrective Task 0 — Warning hygiene / build baseline

| File | Change |
|---|---|
| `Hrot/Subsystems/Hrot.Animation.Replication.Tests/Hrot.Animation.Replication.Tests.csproj` | Fixed `ProjectReference` path depth: `..\..\..\..\FDP\Engine\Fdp.Core\Fdp.Core.csproj` → `..\..\..\FDP\Engine\Fdp.Core\Fdp.Core.csproj` (4 levels → 3 levels, same depth as the main project). |

### Task 1 — ANC-P6-04 Partial queue serialization compliance

| File | Change |
|---|---|
| `Hrot/Subsystems/Hrot.Animation.Replication/Translators/SideBuffers/AnimationMontageQueueEgressTranslator.cs` | (1) Added `internal static int LogicalPayloadBytes(byte count) => 12 + count * 16` per DD-2 §4.2 formula. (2) Changed `Buffer.MemoryCopy` to copy only `count * 16` bytes instead of full 128, leaving tail entries zero in the DDS struct. (3) Updated doc-comment to explain fixed-size DDS framing vs logical payload distinction. |
| `Hrot/Subsystems/Hrot.Animation.Replication.Tests/MontageQueueTranslatorTests.cs` | Replaced weak SC-3 budget test with two behavioral tests (SC-3 `LogicalPayloadBytes` formula + SC-3b tail-zeroing in wire message). |

### Task 2 — ANC-P6-02 LookAtEntity ingress entity-ID remap

| File | Change |
|---|---|
| `Hrot/Subsystems/Hrot.Animation.Replication/Translators/Channels/LookAtChannelIntentIngressTranslator.cs` | Added `using Hrot.MuscleCharacter.Animation.Contracts;`. Split `ProcessSample` to branch on `LookAtActionIds.LookAtEntity`: copies params, then remaps `TargetEntityId` via `_entityMap.TryGetEntity`; returns early (channel unchanged) if target not in map. Non-entity actions copy params as before. |
| `Hrot/Subsystems/Hrot.Animation.Replication.Tests/AnimationChannelTranslatorTests.cs` | Added `using Hrot.MuscleCharacter.Animation.Contracts;`. Added SC-8 (successful remap) and SC-9 (unknown target = channel unchanged). |

### Task 3 — ANC-P6-06 QoS verification hardening

| File | Change |
|---|---|
| `Hrot/Subsystems/Hrot.Animation.Replication/AnimationReplicationModule.cs` | Added `using CycloneDDS.Schema;`. Added public `AnimTopicQosPolicy` class. Added static `TopicQosPolicies` property backed by `BuildTopicQosPolicies()` listing all 15 topics with `DdsReliability`/`DdsDurability` per DD-2 §6 table. |
| `Hrot/Subsystems/Hrot.Animation.Replication.Tests/AnimationReplicationModuleTests.cs` | Added `using CycloneDDS.Schema;`. Added SC-8 (15-entry count), SC-9 (state-bearing topics = Reliable+TransientLocal), SC-10 (event topics = Reliable+Volatile), SC-11 (no FootstepEvent topic anywhere). |

---

## Tests Added / Updated

### MontageQueueTranslatorTests.cs

| Test | What it proves |
|---|---|
| `MontageQueue_LogicalPayloadBytes_EqualsHeaderPlusLiveEntries` (SC-3, new) | `LogicalPayloadBytes(3)==60`, `LogicalPayloadBytes(0)==12`, `LogicalPayloadBytes(8)==140` — proves the DD-2 §4.2 formula. |
| `MontageQueueEgress_WireMessage_ZeroesTailEntriesBeyondCount` (SC-3b, new) | Egress with 3 live entries + stale tail slots: wire message has correct data for entries 0-2 and zeros for 3-7. |
| `MontageQueueEgress_PublishesOnQueueVersionBump` (SC-1, retained) | Dirty trigger fires on QueueVersion change. |
| `MontageQueueEgress_DoesNotPublish_WhenQueueVersionUnchanged` (SC-2, retained) | No publish when only Count changes without QueueVersion bump. |
| `MontageQueueRoundTrip_ThreeEntries_TailIsZeroed` (SC-4, retained) | Full egress→ingress round-trip; ingress zeros tail entries 3-7. |
| `MontageQueueStateEgress_PublishesOnCurrentEntryIndexChange` (SC-5, retained) | State egress dirty trigger on structural change. |
| `MontageQueueStateEgress_DoesNotPublish_WhenOnlyElapsedSecondsChanges` (SC-6, retained) | EntryElapsedSeconds change alone does not trigger egress. |
| `MontageQueueStateIngress_PreservesObservedQueueVersion` (SC-7, retained) | Ingress does not overwrite Muscle-internal ObservedQueueVersion. |

### AnimationChannelTranslatorTests.cs

| Test | What it proves |
|---|---|
| `LookAtChannelIngress_RemapsTargetEntityId_ForLookAtEntityAction` (SC-8, new) | Network TargetEntityId 777 is remapped to the local entity's Index; ReceivedSampleCount increments. |
| `LookAtChannelIngress_KeepsChannelUnchanged_WhenTargetEntityNotInMap` (SC-9, new) | When target network ID 888 is absent from the map, the channel is not modified and ReceivedSampleCount stays 0. |
| SC-1..SC-7 (all retained) | Existing channel/status dirty-trigger and round-trip behaviors. |

### AnimationReplicationModuleTests.cs

| Test | What it proves |
|---|---|
| `TopicQosPolicies_HasExactly15Entries` (SC-8, new) | Exactly 15 QoS entries in the static table. |
| `TopicQosPolicies_StateBearingTopics_AreReliableTransientLocal` (SC-9, new) | All 8 state-bearing topics (channels, descriptors, side-buffers) have Reliable + TransientLocal. |
| `TopicQosPolicies_EventTopics_AreReliableVolatile` (SC-10, new) | All 7 event topics have Reliable + Volatile. |
| `BrainModule_HasNoFootstepEventTopic` (SC-11, new) | Neither translator list nor QoS table contains any FootstepEvent topic name. |
| SC-1..SC-7 (all retained) | Topic count, direction, uniqueness, and construction tests. |

---

## Build / Test Command Results

### `dotnet test Hrot/Subsystems/Hrot.Animation.Replication.Tests/Hrot.Animation.Replication.Tests.csproj -c Debug`

```
Test Run Successful.
Total tests: 42
     Passed: 42
  Total time: 1.5374 Seconds
```

(Previous batch: 31 tests. New tests added: 11.)

### `dotnet build IOS-IG-SimHost.sln -c Debug --no-restore`

```
Build succeeded.
```

Zero errors, zero warnings (no MSB9008 from test project after path fix).

---

## Design Trade-offs and Constraints

### Partial serialization: logical vs wire payload

`DdsMontageQueue` is a fixed-size blittable C# struct required by CycloneDDS for zero-copy DDS serialization — the runtime always writes the full struct size over the wire. True variable-length encoding of only `Count * 16` bytes is not achievable within this DDS binding.

The implemented approach:
- Egress copies only `count * 16` bytes into the DDS struct; the tail bytes stay zero-initialized (struct default in C#).
- `LogicalPayloadBytes(count)` expresses the DD-2 §4.2 formula (`12 + 16 * count`) as a testable utility.
- The distinction between fixed-size DDS framing (always 144 bytes) and logical payload bytes (12 + 16*count) is documented in both the egress class XML comment and `LogicalPayloadBytes`.

This satisfies DD-2 §4.2 semantics (wire data is deterministic, receiver relies on Count, tail is zeroed) while being honest about the DDS constraint.

### LookAtEntity remap fail behavior

When `TryGetEntity` fails for the remapped target ID, `ProcessSample` returns without modifying the channel and without incrementing `ReceivedSampleCount`. The chosen behavior is **channel unchanged** — the Muscle retains its previous LookAt state until either (a) the target entity is registered and a subsequent sample arrives, or (b) a different action (e.g. ReleaseLook) arrives.

This is the safest fail mode: silent corruption (binding to a wrong local entity) is worse than a stale channel. It is documented in the code and tested by SC-9.

### QoS table as static data

The animation translators use a raw blittable-struct + topic-name pattern (not the `[DdsQos]`-attributed struct pattern used by some older FDP DDS topics). The DDS reader/writer wrappers do not expose QoS metadata via a reflection API in the current binding. A static `TopicQosPolicies` table on `AnimationReplicationModule` is therefore the deterministic verification surface per BATCH-14 instructions.

The table uses `DdsReliability` and `DdsDurability` from `CycloneDDS.Schema` — the same enums as the rest of the codebase — avoiding introducing new parallel types.

---

## Developer Insights

### 1. Issues encountered and resolution

- **`error CS0213` (cannot use fixed on already-fixed expression)**: `fixed byte Params[32]` inside a struct is already a fixed buffer; wrapping it in a `fixed()` statement in an unsafe method is a compile error. Resolution: in unsafe methods that already hold a pointer to the struct (via `&`), access the fixed buffer directly: `LookAtEntityParams* p = (LookAtEntityParams*)pUpd->Params;`. Same for tests: take `&msg` and cast without `fixed()`.

- **Four vs three path levels in csproj**: The test project sat at depth 3 from workspace root (`Hrot/Subsystems/Hrot.Animation.Replication.Tests/`), but the path used 4 `..` segments, landing above the workspace root. Fixed to 3 segments, matching the pattern in the main project.

### 2. Weak points spotted

- `LookAtEntityParams.TargetEntityId` is `uint` (32 bits) while `NetworkEntityMap` keys are `long` (64 bits). If a game session assigns entity network IDs above `uint.MaxValue`, the remap call would silently truncate. A runtime assertion or widening the field to `long` should be considered when network IDs scale up.
- The egress still depends on the convention that every Brain-side mutation of `AnimationMontageQueue.Entries` bumps `QueueVersion`. There is no compile-time enforcement of this convention — a Brain-side code path that writes entries without bumping `QueueVersion` would silently prevent replication.

### 3. Design decisions beyond the written spec

- **`LogicalPayloadBytes` as `internal static int`**: made internal (not private) so the test project can access it via `InternalsVisibleTo` without exposing it as public API. The utility has no state, so static is the right choice.
- **Early return on remap failure** in `LookAtChannelIntentIngressTranslator`: the spec says "fail early on invalid mapping behavior." Early return without modifying the channel is the chosen behavior. An alternative (write the action but not the params) was rejected because it would leave `TargetEntityId` pointing at a stale/invalid entity, which is worse than keeping the previous valid channel state.
- **`AnimTopicQosPolicy` as a public sealed class**: made public so tests can reference it without needing internal visibility. It carries no state beyond the three properties, so it's essentially a record.

### 4. Edge cases discovered

- `ProcessSample` early-return for remap failure must happen **before** `cmd.SetComponent` — otherwise even with a missing target, the channel's `ActiveAction`/`ActionInstanceId` would be overwritten with the new intent (partially corrupt state). The current implementation is correct: it returns before any `SetComponent` call.
- `MontageQueueEntry` size must be exactly 16 bytes for the `count * 16` formula to hold. This is verified by the existing SC-4 round-trip test (which would fail on misaligned offsets) and is an invariant of the struct layout.
- `DdsMontageQueue` struct zero-initialization in C#: `new DdsMontageQueue { ... }` zero-initializes the fixed `EntriesData` buffer before field initializers run, so the tail-zeroing behavior of the egress relies on standard C# value-type initialization semantics. The SC-3b test proves this holds in practice.

### 5. Suggested commit message

```
BATCH-14: ANC-P6 corrective — partial queue serialization, LookAtEntity remap, QoS table

- Fix Hrot.Animation.Replication.Tests.csproj: correct Fdp.Core project reference depth
  (4 levels → 3; eliminates MSB9008 warning)

- ANC-P6-04: AnimationMontageQueueEgressTranslator now copies only Count*16 bytes
  into DDS struct; tail entries are zero in wire message. Add LogicalPayloadBytes(count)
  utility (12 + 16*count per DD-2 §4.2). Replace weak budget test with strict formula +
  tail-zeroing behavioral tests.

- ANC-P6-02: LookAtChannelIntentIngressTranslator remaps TargetEntityId via
  NetworkEntityMap when action == LookAtEntity. Channel kept unchanged if target not
  in map (fail-safe over silent corruption). Add positive + negative remap tests.

- ANC-P6-06: AnimationReplicationModule exposes static TopicQosPolicies table
  (DdsReliability/DdsDurability per DD-2 §6 for all 15 topics). Add QoS verification
  tests: state-bearing topics = Reliable+TransientLocal, events = Reliable+Volatile,
  no FootstepEvent topic.

Tests: 42 passing (was 31). Build: clean.
```
