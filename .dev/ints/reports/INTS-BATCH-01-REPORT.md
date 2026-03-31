# INTS-BATCH-01 Report

**Batch:** INTS-BATCH-01  
**Developer:** GitHub Copilot  
**Date:** 2025-07-15  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| INTS-P1-001 | ✅ Complete | TKB catalog registered in SimHost and IG; new `SimHostNetworkConstants` class |
| INTS-P1-002 | ✅ Complete | `SpawnVehicle` now publishes `SpawnEntityCommand` via event bus |
| INTS-P1-003 | ✅ Complete | `DdsWriterAdapter<T>` created; replaces `NullDdsWriter` in Program.cs and IosSubsystem |
| INTS-P1-004 | ✅ Complete | `PassthruCentralNode` flag added to `DockSpaceOverViewport` |
| INTS-P1-005 | ✅ Complete | `BdcCommandGateway`, `MapClickEvent` writer, and config reader wired in IG |

---

## 🧪 Testing Results

**Unit Tests Passed:** 22 / 22  
**Integration Tests Passed:** N/A (no new integration tests; existing suite unaffected)

**Test assemblies:**
- `Hrot.SimHost.Tests`: 13 passed (5 TkbRegistration + 6 SpawnEntityCommand + 2 pre-existing)
- `Hrot.IG.Tests`: 5 passed (4 MapEventTranslator + 1 pre-existing)
- `Hrot.ExCon.Tests`: 4 passed (3 DdsWriterAdapter + 1 pre-existing)

**Key Test Scenarios Verified:**
- [x] `BdcTkbCatalog.RegisterAll` populates Tank, IFV, Truck, Infantry types in a fresh `TkbDatabase`
- [x] Fresh `TkbDatabase` (before `RegisterAll`) correctly returns false for known types
- [x] `SpawnVehicle("tank")` publishes a `SpawnEntityCommand` with `TkbType = TkbEntityTypes.Tank_M1Abrams`
- [x] `SpawnVehicle("pedestrian")` publishes with `TkbType = TkbEntityTypes.Infantry_Rifleman`
- [x] Unknown vehicle class defaults to `TkbEntityTypes.Truck_HMMWV`
- [x] Published command carries correct `NetworkId` (0) and matches `SimHostNetworkConstants.LocalNodeId`
- [x] `DdsWriterAdapter<T>` implements `IDdsWriter<T>`
- [x] `Dispose()` is safe to call twice (idempotent)
- [x] `Write()` after `Dispose()` throws `ObjectDisposedException`
- [x] `MiniIosPanelState.SubmitViaGateway(null)` does not throw
- [x] `StandardInteractionTool.OnWorldClick` event can be subscribed and unsubscribed

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

Three non-trivial issues arose:

1. **`DdsReader<T>` has no `TryTake` method.** The initial implementation used a `while (_configReader.TryTake(out var config))` pattern mirrored from DDS docs for other bindings. CycloneDDS.Runtime 1.x exposes `Take(int maxSamples)` returning a `DdsLoan<T>` instead. Fixed to `using var loan = _configReader.Take(1); foreach (var sample in loan) { if (!sample.IsValid) continue; ... = sample.Data.ActiveContextId; }`.

2. **`DdsParticipant` constructor takes `uint`, not `int`.** `Program.cs` passed the `domainId` variable (typed `int`) directly, yielding CS1503. Fixed with `(uint)domainId` cast.

3. **`CreateEntityRequest` has no direct `TkbType` property.** The TKB type must be nested inside an `EntityDescriptorUnion` with `_d = EDescriptorType.dtEntityMaster` and `EntityMaster = new EntityMaster { TkbType = ... }`, then added to `InitialDescriptors`. Corrected `SubmitViaGateway` accordingly.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- **`NullDdsWriter` anti-pattern proliferation:** Before this batch, both `Program.cs` and `IosSubsystem.cs` contained inline `NullDdsWriter` implementations. These silent no-ops make it easy to ship broken integrations without any indication that DDS traffic is missing. A single `NullDdsWriter<T>` utility class in a shared location (already partially present) or a build-time assertion would prevent recurrence.
- **`SpawnVehicle`'s string-based dispatch:** `MapVehicleClassToTkbType` maps lower-cased arbitrary strings to TKB types. If the caller typos "tanks" instead of "tank", the entity silently spawns as a HMMWV. An enum-based or discriminated-union API surface would be more robust.
- **Fire-and-forget `CreateEntityAsync` in `SubmitViaGateway`:** The task is discarded (`_ = gateway.CreateEntityAsync(...)`) with no error feedback to the UI. Spawn failures go silently unnoticed by the operator.

**Q3: What design decisions did you make beyond the instructions? How did you resolve them?**

- **`DdsWriterAdapter<T>` placed in `Hrot.ExCon/Services/`** (same assembly as `IDdsWriter<T>`). An alternative was `Hrot.Map.Common`, but that would introduce a CycloneDDS.Runtime dependency in a module that currently has none. Keeping it in `Hrot.ExCon` avoids a circular reference and the extra dependency.
- **`SubmitViaGateway` added as a second `Submit` overload** rather than replacing the existing `Submit(FdpEventBus)`. This preserves backward compatibility with the local-bus path used by existing tests and demo code.
- **`SpawnEntityLocal` private helper** extracted inside `SimHostScenarioManager` to keep demo-only entity wiring out of the networked path, making the distinction between local-only and networked spawn explicit.
- **`SimHostNetworkConstants.LocalNodeId`** extracted as a constant (value `1`) to eliminate the magic number that appeared in both `SimHostApp.cs` and the spawning code. The instructions called out the magic number as a smell; a static constants class was the most lightweight fix consistent with project patterns.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- **`_networkEnabled = false` guard in `IgApplication.InitializeNetwork`:** The gateway, writer, and config reader are only created when `_networkEnabled` is true. Without this guard, `_commandGateway` and `_clickWriter` would be null in headless/test contexts, causing NREs in `Update()` and `OnCanvasClicked`. Added null checks throughout.
- **`SetGateway(null)` on `MiniIosPanel`:** The gateway is set after construction via `SetGateway`. `SubmitViaGateway` must tolerate `null` gracefully (early return) since there is a window between panel creation and network init where the user could click Spawn.
- **Double-dispose on `DdsWriterAdapter<T>`:** The `finally` block in `Program.cs` unconditionally disposes all writers. If construction of a later writer throws, an earlier writer might be disposed, then the outer `finally` disposes it again. `DdsWriterAdapter.Dispose()` uses a `_disposed` flag and idempotent disposal to handle this safely.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- **`Take(1)` polling every frame in `IgApplication.Update()`:** This allocates a `DdsLoan<MapInteractionConfig>` on every tick even when no update is available. CycloneDDS.Runtime's `DdsLoan` is a `ref struct` / pooled handle (implementation-dependent), so the allocation cost may be negligible, but a `WaitForHistoricalData`/listener pattern or a status-check before `Take` would be cleaner for low-frequency config updates.
- **`HitStack` allocation in `OnCanvasClicked`:** `new List<HitResult>()` allocates on every click. For the click-rate expected from a human operator this is acceptable, but if this path is ever driven programmatically the allocation can be avoided with a pooled list.
- **LINQ in `SpawnEntityCommandTests`:** `OfType<SimTransform>().First()` is used only in tests, so this is fine. However, the production `SimHostScenarioManager` must not use LINQ inside `SpawnVehicle`; the current implementation uses a direct struct constructor with no LINQ, which is correct.

---

## ⚠️ Outstanding Issues / Next Steps

- [ ] `SubmitViaGateway` discards the `CreateEntityAsync` task. A follow-up task should propagate spawn failure back to the MiniIOS panel (e.g., a brief error overlay or log entry).
- [ ] `DdsWriterAdapterTests` connect to DDS domain 99 and require `ddsc.dll` / `libddsc.so` to be present on the test runner's `PATH`. CI agents without CycloneDDS native libs will fail these tests. Consider marking with `[Trait("Category","RequiresDds")]` and skipping in clean-room CI.
- [ ] TkbType string-to-enum dispatch in `SimHostScenarioManager.MapVehicleClassToTkbType` should be hardened or the call sites converted to use an enum before the next batch that extends vehicle types.
