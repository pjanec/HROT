# BATCH-04 Report

**Batch:** BATCH-04  
**Developer:** GitHub Copilot (Claude Sonnet 4.6)  
**Date:** 2026-04-06  
**Status:** Complete

---

## 📊 Task Completion

| Task ID     | Status | Notes |
|-------------|--------|-------|
| EDIT1-E001  | ✅ Done | `EmbarkEntityCommand` + `DisembarkEntityCommand` created; constants added to `BehaviorConstants`; 3 round-trip tests passing |
| EDIT1-E002  | ✅ Done | `SeedTargetCommand` added to `PerceptionEvents.cs`; constant added to `PerceptionConstants`; 2 round-trip tests passing |
| EDIT1-E003  | ✅ Done | `SpawnZoneObstacleCommand` + `UpdateZoneConfigCommand` sealed classes created in `Hrot.Map.Common/Events/`; 4 round-trip tests passing |
| EDIT1-E004  | ✅ Done | `CognitiveComponentRegistry` registers `EmbarkEntityCommand` + `DisembarkEntityCommand`; `CombatComponentRegistry` registers `SeedTargetCommand`; 3 no-throw registration tests passing |

---

## 🧪 Testing Results

**New Tests Written:** 12  
**Required Minimum:** 7  
**All suites pass (excluding pre-existing failures):** ✅

### Test breakdown

| Test file | Project | Tests | Result |
|-----------|---------|-------|--------|
| `EmbarkDisembarkCommandTests.cs` | `FDP.Toolkit.Behavior.Tests` | 3 | ✅ Passed |
| `SeedTargetCommandTests.cs` | `FDP.Toolkit.Perception.Tests` | 2 | ✅ Passed |
| `ZoneCommandRoundTripTests.cs` | `Hrot.Map.Common.Tests` | 4 | ✅ Passed |
| `EventRegistrationTests.cs` | `Hrot.SimHost.Tests` | 3 | ✅ Passed |

### Full suite results

| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| `FDP.Toolkit.Behavior.Tests` | 77 | 80 | +3 new |
| `FDP.Toolkit.Perception.Tests` | 35 | 37 | +2 new |
| `Hrot.Map.Common.Tests` | 111 | 115 | +4 new |
| `Hrot.SimHost.Tests` | 442 | 445 | +3 new; 5 pre-existing failures unchanged |
| `Hrot.ClusterRunner.Tests` | 189 / 192 | 189 / 192 | No change; 3 pre-existing failures unchanged |

Pre-existing failures confirmed by `git stash` / restore verification — they existed before any BATCH-04 change was applied:  
- `ActionDispatchModuleTests.ActionDispatchModule_EmptyExecutorLists_StillRegistersDispatchers`  
- `ActionDispatchModuleTests.ActionDispatchModule_RegistersLocoAndWeaponDispatchers`  
- `CgfLogicPackTests.CgfLogicPack_EmptyWorld_AllSystemsRegisterAndRunWithoutException`  
- `SimulationLogicModuleTests.SimulationLogicModule_EmptyWorld_AllSystemsRegisterAndUpdateWithoutException`  
- `GeoSpatialEgressTranslatorTests.Dispose_AlsoCallsBaseDispose`  

### Key test scenarios verified

- [x] `EmbarkEntityCommand` publish → swap → consume: Passenger And Vehicle fields preserved exactly
- [x] `DisembarkEntityCommand` publish → swap → consume: Passenger field preserved exactly  
- [x] `SeedTargetCommand` publish → swap → consume: Perceiver, Target, and ScoreBoost fields preserved
- [x] `SpawnZoneObstacleCommand` managed publish → swap → consume: ZoneName and Radius correct
- [x] `UpdateZoneConfigCommand` managed publish → swap → consume: ZoneName, RoadNetworkPath (including `null`) correct
- [x] `EmbarkEntityCommand` + `DisembarkEntityCommand` publishable after `CognitiveComponentRegistry.RegisterAll` — no exception
- [x] `SeedTargetCommand` publishable after `CombatComponentRegistry.RegisterAll` — no exception

---

## 📝 Developer Insights

**Q1: What issues did you encounter creating the events or registering them?**

No structural issues. The main thing to watch was the pre-existing xUnit2013 analyzer rule — it warns about `Assert.Equal(n, collection.Count)` and suggests `Assert.Single`. I caught this early and wrote the round-trip tests using `Assert.Single(events.ToArray())` for the managed-event tests (which return `IReadOnlyList<T>`) and likewise for the `ReadOnlySpan<T>` returned by `Consume<T>()`.

One naming subtlety: I placed `SeedTargetCommand` tests in `FDP.Toolkit.Perception.Tests` rather than `FDP.Toolkit.Behavior.Tests` because that project already references `FDP.Toolkit.Perception`. The Behavior.Tests project does NOT reference Perception, so adding a Perception test there would have required adding a project reference — unnecessary coupling.  
Note that `FDP.Toolkit.Perception.Tests` is only in `FDP/FDP.sln`, not in `IOS-IG-SimHost.sln`. This was addressed pragmatically: the batch instructions said "similar to EmbarkEntityCommand tests" but did not mandate using Behavior.Tests for the Perception event. The natural home is Perception.Tests.

**Q2: Did you find any inconsistency between the TASK-DETAIL spec and the actual FDP kernel API (e.g. `RegisterManagedEvent` not existing)?**

Yes — as already called out in the batch instructions' "Critical Codebase Fact" section. The spec document (TASK-DETAIL) mentioned `world.RegisterManagedEvent<T>()`, but this method does NOT exist on `EntityRepository`. Only `RegisterEvent<T>()` is present (for unmanaged structs via `Bus.Register<T>()`).

For managed events (class types), no registration API exists at all. `FdpEventBus.PublishManaged<T>` and `ConsumeManaged<T>` both use a `ConcurrentDictionary<int, object>` keyed by `GetManagedTypeId<T>()` (hash of the CLR type's full name), and the stream is created lazily on first publish. This is confirmed by:
- `AssignDoctrineEvent` (a sealed class) — has no `[EventId]`, no registration call anywhere, and works fine.
- `FdpEventBus.GetOrCreateManagedStream<T>()` creates the stream on demand.

Therefore `SpawnZoneObstacleCommand` and `UpdateZoneConfigCommand`:
- Correctly have no `[EventId]` attribute
- Correctly have no registration call in `HrotSharedComponentRegistry` (or anywhere else)
- This is the intended design, not a gap

**Q3: What design decisions did you make?**

1. **`SeedTargetCommand` as an in-file addition to `PerceptionEvents.cs`** — Rather than creating a separate `Events/SeedTargetCommand.cs`, I added the struct to the existing `PerceptionEvents.cs` file which already collects all perception event structs. This keeps the pattern consistent with the existing four events in that file, and avoids an unnecessary extra file for a single struct.

2. **`SeedTargetCommand` tests in `FDP.Toolkit.Perception.Tests`** — Explained in Q1 above. Keeps test–producer coupling correct without adding a cross-toolkit project reference.

3. **`Assert.Single` instead of `Assert.Equal(1, ...)` for collection-size assertions** — Chose the xUnit-idiomatic form to avoid the xUnit2013 analyzer warning and match the spirit of existing test code. For `ReadOnlySpan<T>` the conversion `.ToArray()` is required before `Assert.Single` since `ReadOnlySpan<T>` is not `IEnumerable<T>`.

4. **No `[StructLayout(LayoutKind.Sequential)]` on embarkation structs** — The batch instructions do not specify it for `EmbarkEntityCommand` / `DisembarkEntityCommand`, and neither does the existing `AssignDoctrineHashEvent` reference pattern. `SeedTargetCommand` carries the `[StructLayout]` attribute following `PerceptionEvents.cs` precedent (all four existing perception structs have it).

**Q4: Are the CGF or Editor registries also missing these events? Will publishing them throw at runtime?**

**The CGF and Editor registries do NOT register these three events.** `CgfComponentRegistry.RegisterAll` calls `HrotSharedComponentRegistry.RegisterAll` and then registers its own cognitive and kinematic components, but never calls `CognitiveComponentRegistry.RegisterAll` or `CombatComponentRegistry.RegisterAll`, so the event streams are not pre-created.

However, **this will NOT throw at runtime for ordinary `Bus.Publish<T>()` calls**. Looking at `FdpEventBus.GetOrCreateNativeStream<T>()`:
```csharp
private NativeEventStream<T> GetOrCreateNativeStream<T>() where T : unmanaged
{
    int typeId = EventType<T>.Id; // validates [EventId]
    var stream = _nativeStreams.GetOrAdd(typeId, _ => new NativeEventStream<T>());
    return (NativeEventStream<T>)stream;
}
```
The stream is created lazily. `Publish<T>` and `Consume<T>` both go through this path — they never require prior registration.

The only path that **would throw** is `Bus.PublishRaw(typeId, data)`, which is used by **EntityCommandBuffer playback**. If a CGF or Editor simulation records an ECB command containing `EmbarkEntityCommand` or `SeedTargetCommand` and then replays it, `PublishRaw` will throw `InvalidOperationException: "Event type {typeId} not registered via RegisterEvent<T>()"`.

**Practical risk:** Minimal for BATCH-05 — unless the adapter or system writes ECB commands for these events (which is unusual; ECBs are mainly used for component mutations, not event forwarding). The registries can be updated later when CGF adapter systems are written in BATCH-05/06.

**Q5: What is the highest-risk item for BATCH-05 (Editor adapters)?**

The highest-risk item is the **managed-event consumption pattern** in the Editor context. Managed events do NOT have pre-registration and are routed by CLR type name hash. If the Editor adapter's assembly is loaded in a different AppDomain (unlikely in .NET 8, but possible in unusual host configurations), the hash computed by `GetManagedTypeId<T>()` could differ between the publisher assembly and the consumer. More practically:

- The Editor process and the SimHost process run in separate processes and share state via DDS/Cyclone. **Managed events cannot cross the process boundary.** `SpawnZoneObstacleCommand` and `UpdateZoneConfigCommand` will need to be serialised (likely to JSON/protobuf) and shipped over DDS if the Editor must issue them to SimHost. The adapter wrapping these commands must translate between the Editor's in-process managed event and the DDS wire format. This is a design gap that BATCH-05 must address explicitly — it cannot just `bus.PublishManaged` across processes.

---

## ⚠️ Outstanding Issues / Next Steps

- [ ] `CgfComponentRegistry` and the Editor's equivalent registry do not register the three new unmanaged events. This is acceptable now (no CGF code uses ECBs for embarkation/seeding), but should be tracked and addressed when CGF adapters are implemented.
- [ ] `FDP.Toolkit.Perception.Tests` is not in `IOS-IG-SimHost.sln`. A future cleanup batch should either add it or add `SeedTargetCommandTests` to a project that is in the main solution.
- [ ] Five pre-existing test failures in `Hrot.SimHost.Tests` and three in `Hrot.ClusterRunner.Tests` remain from previous batches (time-mode, action-dispatch, geo-spatial). These are not BATCH-04 regressions.
