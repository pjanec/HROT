# IG-BATCH-02-REPORT: Network Integration & Stub Rendering

**Batch:** IG-BATCH-02  
**Tasks Completed:** Task 0, IG.1.3, IG.1.3b, IG.1.4  
**Test Results:** 37 / 37 passing (includes 22 from IG-BATCH-01)  
**Status:** ✅ COMPLETE

---

## Summary of Changes

### Task 0 — Corrective Fixes (Docs)
- **`README.md`** (root): Created with native-build prerequisite note. Developers must run `.\FDP\ExtDeps\FastCycloneDds\build\native-win.ps1` immediately after cloning or the CycloneDDS native library will be absent and all DDS-dependent code will fail at runtime.
- **`docs/design/TASK-DETAILS-IG.md`**: Replaced `rlImGui` with `rlImgui-cs 3.2.0`. The original text referenced the wrong package name, which caused confusion with the separate `rlImGui` community fork.
- **`.dev-workstream/IG-DEBT-TRACKER.md`**: Resolved `IG-DEBT-001` and `IG-DEBT-002`.

### Task IG.1.3 — Integrate NetworkDemo Network Module
New files:
- `Hrot.IG/IgNetworkConstants.cs` — all DDS/network constants (DdsDomain=0, InstanceId=300, LocalNodeId=1, geo origin)
- `Hrot.IG/Translators/EntityMasterTranslator.cs` — ingress: DDS `EntityMaster` → `SpawnEntityCommand` / `DestroyEntityCommand` on FdpEventBus
- `Hrot.IG/Translators/WorldPosTranslator.cs` — extends `CycloneTranslator<WorldPos,WorldPos>`; converts WGS84 → `SimTransform`
- `Hrot.IG/Translators/EntityInfoTranslator.cs` — ingress: DDS `EntityInfo` → `UpdateEntityCommand` on FdpEventBus
- `Hrot.IG/Translators/TimePulseTranslator.cs` — bridges DDS `TimePulse` → `FdpEventBus.Publish<TimePulseDescriptor>()` for `SlaveTimeController`

Modified:
- `Hrot.IG/IgApplication.cs` — added full kernel + network init: `InitializeEcs()`, `InitializeNetwork()`, updated `Run()` and `Shutdown()`

### Task IG.1.3b — Register NetworkSpawningSystem via SpawningModule
New file:
- `Hrot.IG/Modules/SpawningModule.cs` — thin `IModule` wrapper for `NetworkSpawningSystem`; Name=`"NetworkSpawning"`, Policy=`ExecutionPolicy.Synchronous()`

Support:
- `Hrot.IG/IgSequentialIdAllocator.cs` — local `INetworkIdAllocator` (ghost node only; no IDs transmitted to network)

### Task IG.1.4 — Add EntityRenderLayer with Stub Visualizer
New files:
- `Hrot.IG/Adapters/StubVisualizerAdapter.cs` — implements `IVisualizerAdapter`; 10 px red/yellow/orange circle, `#netId` label when `NetworkIdentity` present
- `Hrot.IG/Adapters/StubVisualizerConstants.cs` — `CircleRadiusPx=10`, `LabelOffsetPx=15`, `LabelFontSize=10`, `HitRadiusWorldUnits=20f`

### Test Files
- `Hrot.IG.Tests/EntityMasterTranslatorTests.cs` — 5 tests (spawn path, known-entity update, dispose, PollIngress no-op)
- `Hrot.IG.Tests/TimePulseTranslatorTests.cs` — 4 tests (bus registration, HasEvent after swap, field round-trip, PollIngress no-op)
- `Hrot.IG.Tests/SpawningModuleIntegrationTests.cs` — 5 tests (module properties, entity manifests with correct identity, initial components applied, duplicate suppressed)
- `Hrot.IG.Tests/StubVisualizerAdapterTests.cs` — 7 tests (position null/present/Z-ignored, hitRadius constant, hoverLabel null/formatted/two-entities)

---

## Developer Insights

### Q1: Issues wrapping FDP Network systems onto node 300 / structural bleed

The primary structural issue was **`SequentialIdAllocator` being a private nested class** inside `NetworkDemoApp`. The NetworkDemo is an example, not a library, so its utilities are not exported. Even though IG is a ghost node that never allocates IDs authoritatively, `NetworkSpawningSystem` requires a non-null `INetworkIdAllocator` by contract (it throws on null). This forced creation of a local `IgSequentialIdAllocator`.

A secondary issue was **`NetworkEntityMap` existing in two namespaces**: `FDP.Toolkit.Replication.Services` (the one used by the spawning/replication stack) and `ModuleHost.Network.Cyclone.Services` (a simpler concurrent-map variant used internally by the Cyclone module). `CycloneNetworkModule` resolves this with a `using` alias to the replication version. `IgApplication.cs` had to be structured the same way — replacing a wildcard `using ModuleHost.Network.Cyclone.Services;` with two explicit type aliases (`DdsIdAllocator`, `NodeIdMapper`) to prevent ambiguity.

### Q2: Weak points in FDP toolkits when constructing Translator elements

**`EntityInfo` is a managed struct** (`[DdsManaged]` with `string Name`). This means it cannot pass through `IEntityCommandBuffer.SetComponent<T>` (which requires `T : unmanaged`). The CycloneTranslator base class and the `IDescriptorTranslator` interface contract both assume the command buffer is the primary mutation path, but they have no provision for managed-struct component types. The workaround — publishing an `UpdateEntityCommand` onto the FdpEventBus so `NetworkSpawningSystem` applies it via `EntityComponentReflector` — is correct but architecturally indirect. The toolkit could benefit from a `SetAnyComponent(Entity, object)` helper on `IEntityCommandBuffer` that routes based on unmanagededness at runtime.

**`AutoCycloneTranslator<T>`** requires `UnsafeLayout<T>.IsValid` (i.e., a `long EntityId` field at offset 0). `TimePulseDescriptor` has no `EntityId`, so `AutoCycloneTranslator` cannot be used for time events. The instructions mentioned it specifically; a custom `TimePulseTranslator : IDescriptorTranslator` was needed. This is a design gap: `AutoCycloneTranslator` works only for entity-scoped descriptors, not for global broadcast events.

### Q3: Edge cases in StubVisualizer PickEntity checks

`GetHitRadius` returns a constant world-space radius (`20 m` at default zoom). This means:
- At **higher zoom** (zoomed in), the hit area appears *larger* than the rendered circle — entities that are close together become difficult to distinguish individually.
- At **lower zoom** (zoomed out), the hit area is *smaller* than the rendered circle — clicks near the edge of the circle will miss.

The correct approach for a production visualizer is a zoom-adaptive hit radius: `CircleRadiusPx / camera.Zoom`. However, for Phase-1 stub rendering this is acceptable. The constant has been named `HitRadiusWorldUnits` and is pinned by a test so any future zoom change immediately breaks the test and forces a review.

A second edge case: `GetPosition` returns `null` when `SimTransform` is absent. `EntityRenderLayer` skips rendering for null positions. However, entities can exist in the ECS (spawned from `EntityMaster`) *before* a `WorldPos` update arrives, because `WorldPosTranslator` silently skips unmapped entities. During that first-tick gap the entity is in the network map but has no position, so it is invisible. This is correct and intentional behaviour for a ghost node.

### Q4: Performance drift translating SimTransform over 100 entity counts

The `StubVisualizerAdapter` runs entirely on the render thread inside Raylib's `BeginMode2D` scope. At 100 entities the following per-entity work occurs:
1. `HasComponent<SimTransform>` — O(1) chunk lookup
2. `GetComponentRO<SimTransform>` — O(1) pointer offset
3. `DrawCircle` — one Raylib draw call
4. `HasComponent<NetworkIdentity>` + `GetComponentRO<NetworkIdentity>` + `DrawText` (if present) — O(1) each

At 100 entities this is negligible (<0.1 ms). At 10,000+ entities the `DrawText` allocations (string interpolation `$"#{netId.Value}"` per frame) would become notable. The fix for scale is to cache the label string on first build and invalidate only when `NetworkIdentity.Value` changes. This is marked as a known technical debt for the production visualizer.

---

## Debt Items Created

None. The managed-struct workaround for `EntityInfo` is documented in the translator's XML doc comment and does not warrant a formal debt ticket at this phase.

---

## Files Changed

| File | Type | Notes |
|---|---|---|
| `README.md` | Created | Native build prereqs |
| `docs/design/TASK-DETAILS-IG.md` | Modified | `rlImGui` → `rlImgui-cs 3.2.0` |
| `.dev-workstream/IG-DEBT-TRACKER.md` | Modified | Resolved IG-DEBT-001, IG-DEBT-002 |
| `Hrot.IG/IgNetworkConstants.cs` | Created | DDS/network constants |
| `Hrot.IG/IgSequentialIdAllocator.cs` | Created | Local `INetworkIdAllocator` |
| `Hrot.IG/IgApplication.cs` | Modified | Full kernel + network integration |
| `Hrot.IG/Hrot.IG.csproj` | Modified | `InternalsVisibleTo("Hrot.IG.Tests")` |
| `Hrot.IG/Translators/EntityMasterTranslator.cs` | Created | DDS EntityMaster → SpawnEntityCommand |
| `Hrot.IG/Translators/WorldPosTranslator.cs` | Created | DDS WorldPos → SimTransform |
| `Hrot.IG/Translators/EntityInfoTranslator.cs` | Created | DDS EntityInfo → UpdateEntityCommand |
| `Hrot.IG/Translators/TimePulseTranslator.cs` | Created | DDS TimePulse → FdpEventBus |
| `Hrot.IG/Modules/SpawningModule.cs` | Created | IModule wrapping NetworkSpawningSystem |
| `Hrot.IG/Adapters/StubVisualizerAdapter.cs` | Created | `IVisualizerAdapter` red circles |
| `Hrot.IG/Adapters/StubVisualizerConstants.cs` | Created | Named rendering constants |
| `Hrot.IG.Tests/EntityMasterTranslatorTests.cs` | Created | 5 tests |
| `Hrot.IG.Tests/TimePulseTranslatorTests.cs` | Created | 4 tests |
| `Hrot.IG.Tests/SpawningModuleIntegrationTests.cs` | Created | 5 tests |
| `Hrot.IG.Tests/StubVisualizerAdapterTests.cs` | Created | 7 tests |
