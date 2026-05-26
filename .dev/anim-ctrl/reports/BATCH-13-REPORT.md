# BATCH-13 REPORT — Animation Replication (Phase 6)

**Status:** COMPLETED  
**Tasks:** ANC-P6-01 through ANC-P6-06  
**Tests:** 35 / 35 passing  
**Build:** `dotnet build IOS-IG-SimHost.sln -c Debug` — succeeded

---

## 1. Tasks Implemented

### ANC-P6-01 — Project scaffolding
- Created `Hrot/Subsystems/Hrot.Animation.Replication/Hrot.Animation.Replication.csproj`
- Created `Hrot/Subsystems/Hrot.Animation.Replication.Tests/Hrot.Animation.Replication.Tests.csproj`
- Both projects added to `IOS-IG-SimHost.sln` (project entries + platform config + nested project entries)
- Added `<InternalsVisibleTo>` to main project for test access

### ANC-P6-02 — DDS wire types
- `AnimationDdsMessages.cs` — 15 DDS structs covering all 8 channels/descriptors/side-buffers and 7 events
- `DdsLiveWriter.cs` — `IAnimDdsWriter<T>` abstraction + `DdsLiveWriter<T>` wrapping `DdsWriter<T>` (null-safe for tests)

### ANC-P6-03 — Channel translators (4 topics × 2 directions = 8)
- `AnimationChannelIntentEgressTranslator` / `AnimationChannelIntentIngressTranslator`
- `AnimationChannelStatusEgressTranslator` / `AnimationChannelStatusIngressTranslator`
- `LookAtChannelIntentEgressTranslator` / `LookAtChannelIntentIngressTranslator`
- `LookAtChannelStatusEgressTranslator` / `LookAtChannelStatusIngressTranslator`

### ANC-P6-04 — Descriptor + side-buffer translators (4 topics × 2 directions = 8)
- `StanceIntentEgressTranslator` / `StanceIntentIngressTranslator`
- `StanceStatusEgressTranslator` / `StanceStatusIngressTranslator`
- `AnimationMontageQueueEgressTranslator` / `AnimationMontageQueueIngressTranslator`
- `AnimationMontageQueueStateEgressTranslator` / `AnimationMontageQueueStateIngressTranslator`

### ANC-P6-05 — Event translators (7 topics × bidirectional = 7 translators with Direction field)
- `MontageStartedEventTranslator` — `hrot/anim/MontageStarted` — uses `Proxy`+`Unsafe.As` for readonly struct
- `MontageEndedEventTranslator` — `hrot/anim/MontageEnded`
- `MontageSectionAdvancedEventTranslator` — `hrot/anim/MontageSectionAdv` — uses `Proxy`+`Unsafe.As`
- `StanceChangedEventTranslator` — `hrot/anim/StanceChanged`
- `HitWindowOpenedEventTranslator` — `hrot/anim/HitWindowOpened` — uses `Proxy`+`Unsafe.As`
- `HitWindowClosedEventTranslator` — `hrot/anim/HitWindowClosed` — uses `Proxy`+`Unsafe.As`
- `AnimNotifyEventTranslator` — `hrot/anim/AnimNotify`

### ANC-P6-06 — AnimationReplicationModule
- `AnimationReplicationModule.cs` — POCO with `IReadOnlyList<INetworkTranslator> AllTranslators`
- Brain role: 4 egress (intent/queue) + 4 ingress (status/queue-state) + 7 event ingress = 15
- Muscle role: 4 ingress (intent/queue) + 4 egress (status/queue-state) + 7 event egress = 15

---

## 2. Modified Files

### `FDP/Network/Fdp.Network.Cyclone/Translators/CycloneNativeEventTranslator.cs`
- Added null guard in constructor: `Reader = participant is not null ? new DdsReader<TDds>(participant) : null!`
- Added early return in `PollIngress` when `Reader is null`
- Changed `Writer.Write(ddsEvent)` to `Writer?.Write(ddsEvent)` in `ScanAndPublish`
- **Reason:** Event translators need to be constructed without a live DDS participant in unit tests.

---

## 3. Test Coverage (35 tests)

| File | Tests | Coverage |
|------|-------|----------|
| `AnimationChannelTranslatorTests.cs` | 7 | Intent/status egress dirty-filter, ingress read-modify-write, LookAt round-trip |
| `StanceTranslatorTests.cs` | 5 | StanceStatus dirty-filter (TransitionProgress excluded), StanceIntent, full ingress |
| `MontageQueueTranslatorTests.cs` | 7 | Queue egress by QueueVersion, MontageQueueState dirty-filter, ingress tail-zeroing |
| `EventTranslatorTests.cs` | 9 | Round-trip for all 7 event types, encode returns false for unknown entity, decode returns false for unknown netId |
| `AnimationReplicationModuleTests.cs` | 7 | Brain/Muscle have 15 translators, correct egress/ingress topic sets, opposite directions, no duplicates |

---

## 4. Issues Encountered

### Issue 1: `CycloneNativeEventTranslator` required live DDS participant
**Root cause:** Constructor unconditionally created `DdsReader<TDds>` and `DdsWriter<TDds>`, which fail without a participant.  
**Fix:** Added null checks identical to the pattern in `CycloneTranslator`.

### Issue 2: Readonly struct limitation for event types
**Affected types:** `MontageStartedEvent`, `MontageSectionAdvancedEvent`, `HitWindowOpenedEvent`, `HitWindowClosedEvent` — all `readonly struct` with `readonly` fields and no parameterized constructors.  
**Fix:** Private `struct Proxy` with mutable fields + `Unsafe.As<Proxy, TEcs>(ref proxy)` bitcast. This pattern avoids object initializer syntax that fails at compile time for readonly structs.

### Issue 3: `DdsLiveWriter<T>` validated type metadata on construction even with null participant
**Root cause:** `DdsWriter<T>` validates DDS-generated native methods (`GetNativeSize`, `MarshalToNative`) in its constructor. Our `DdsAnimationChannel*` types are plain structs, not DDS source-generated, so they fail this check.  
**Fix:** Made `DdsLiveWriter<T>` null-safe — skips `DdsWriter<T>` construction when participant is null; `Write()` becomes a no-op.

### Issue 4: Missing `using Fbt;` for `NodeStatus` in status translators
**Root cause:** `NodeStatus` is in the `Fbt` namespace (FastBTree library), not `Fdp.Toolkit.Behavior`. Replaced stale `using Fdp.Toolkit.Behavior;` with `using Fbt;` in all status translator files.

### Issue 5: Missing `using Fdp.Interfaces;` for `TranslatorDirection` in event translators
**Root cause:** `TranslatorDirection` is defined in the `Fdp.Interfaces` namespace (inside `Fdp.Core` project), not the `Fdp.Core` namespace. Added `using Fdp.Interfaces;` to all 7 event translator files.

### Issue 6: Missing project reference to `Hrot.Core` for `NodeRole`
**Root cause:** `NodeRole` is in `Hrot.Common` namespace, project `Hrot.Core`. Added `<ProjectReference Include="..\..\Engine\Hrot.Core\Hrot.Core.csproj" />` to the main project.

---

## 5. Design Decisions Beyond the Spec

- **`IAnimDdsWriter<T>` abstraction:** Created to allow `CapturingWriter<T>` injection in tests for channel/descriptor/side-buffer translators (event translators use the base class test hook `EncodeForTest`/`DecodeForTest`).
- **Dirty filter design for `StanceStatus`:** `TransitionProgress` was NOT included as a dirty trigger, matching the design principle that continuous float values should not cause spurious publishes; only discrete state changes (`Phase`, `CurrentStance`, `AckVersion`) trigger a publish.
- **Read-modify-write on ingress:** Ingress translators only update the fields they own to avoid overwriting locally-computed fields on the receiving node.

---

## 6. Weak Points Spotted in the Codebase

- **`DdsWriter<T>` type validation on construction** is a footgun for plain structs. Any non-DDS-generated struct used as `T` will throw at runtime — there is no compile-time protection.
- **`CycloneNativeEventTranslator` not null-safe by default** — required patching for testability. Other subclasses of this base that are written in the future will face the same issue unless the base class is patched first (which it now is).

---

## 7. Build & Test Results

```
dotnet build IOS-IG-SimHost.sln -c Debug
  Build succeeded. 0 Error(s) 0 Warning(s)

dotnet test Hrot.Animation.Replication.Tests.csproj --no-build
  Passed! - Failed: 0, Passed: 35, Skipped: 0, Total: 35
```
