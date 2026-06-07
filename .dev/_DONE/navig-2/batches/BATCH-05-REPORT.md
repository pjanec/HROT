# BATCH-05 Report: Phase 2 Part B — Fake Backends Completion

**Batch:** BATCH-05
**Phase tasks:** NAV-P2-T2 (FakeDtCrowdProvider), NAV-P2-T5 (NavTestMap + fixtures + NavigationFakesModule)
**Status:** COMPLETE

---

## Summary

BATCH-05 completes Phase 2 of the navigation subsystem. All fake provider infrastructure
is now in place: crowd steering, test map DSL + JSON loader, canonical fixtures, and the
all-in-one NavigationFakesModule.

---

## Files Created

### Fdp.Toolkits (production)

| File | Description |
|------|-------------|
| `Navigation/NavLayerMask.cs` | New `[Flags] enum NavLayerMask : uint` (Infantry, Vehicle, Naval, Air, All) |
| `Navigation/IDtCrowdProvider.cs` | New interface + `CrowdAgentParams` + `CrowdAgentSnapshot` structs |
| `Navigation/Fake/FakeCrowdComponents.cs` | ECS components `FakeCrowdAgentState` (id=264) and `FakeCrowdGlobalState` (id=263) |
| `Navigation/Fake/FakeDtCrowdProvider.cs` | Full O(N^2) crowd simulation with separation + acceleration clamping; implements `IFakeDtCrowdProviderTestApi` |
| `Navigation/Fake/NavTestMap.cs` | `NavTestMap` class + `NoFlyVolume` struct |
| `Navigation/Fake/NavTestMapLoader.cs` | JSON loader using Newtonsoft.Json; parses NavLayerMask, SurfaceType, TraversalKind enums |
| `Navigation/Fake/NavTestMapBuilder.cs` | Fluent DSL: `NavTestMapBuilder` + `NavLayerBuilder` |
| `Navigation/Fake/NavTestMaps.cs` | 10 canned maps: Corridor, LBend, TwoLayers, OffMeshJump, Replan, Crowded, Stuck, Frustration, Flying, Naval |
| `Navigation/Fake/NavigationFakesModule.cs` | All-in-one test module; registers INavmeshProvider as ECS singleton via `RegisterProviders()` |

### Files Modified

| File | Change |
|------|--------|
| `Navigation/Fake/NavFakeIds.cs` | IDs moved from 250-256 to 262-268 (avoids collision with PerceptionReceptor@251 and NavigationContractsComponentIds@257-261) |
| `Navigation/Fake/FakeNavmeshProvider.cs` | Added `NavTestMap? _loadedMap` field; added `FakeNavmeshProvider(NavTestMap)` ctor; `GetLoadedMap()` return type changed from `object?` to `NavTestMap?` |
| `Navigation/Fake/FakeVolumetricPathProvider.cs` | Added `FakeVolumetricPathProvider(NavTestMap)` ctor overload |

### Fdp.Toolkits.Tests (test-only)

| File | Description |
|------|-------------|
| `Navigation/FakeDtCrowdProviderTests.cs` | 10 unit tests for FakeDtCrowdProvider |
| `Navigation/NavTestMapLoaderTests.cs` | 11 unit tests for NavTestMapLoader |
| `Navigation/NavigationFakesModuleTests.cs` | 4 integration tests for NavigationFakesModule |
| `Navigation/data/navmaps/corridor.json` | JSON fixture |
| `Navigation/data/navmaps/l_bend.json` | JSON fixture |
| `Navigation/data/navmaps/two_layers.json` | JSON fixture |
| `Navigation/data/navmaps/off_mesh_jump.json` | JSON fixture |
| `Navigation/data/navmaps/replan.json` | JSON fixture (middle polygon is_blocked=true) |
| `Navigation/data/navmaps/crowded.json` | JSON fixture |
| `Navigation/data/navmaps/stuck.json` | JSON fixture (single polygon) |
| `Navigation/data/navmaps/frustration.json` | JSON fixture |
| `Navigation/data/navmaps/flying.json` | JSON fixture (no_fly_zone + max_altitude=200) |
| `Navigation/data/navmaps/naval.json` | JSON fixture (Naval layer, surface_type=Water) |
| `Fdp.Toolkits.Tests.csproj` | Added `<Content Include="Navigation\data\navmaps\*.json" CopyToOutputDirectory="PreserveNewest" />` |

---

## Test Results

| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| Navigation (all tests) | 125 | 151 | +26 |
| FakeDtCrowdProviderTests | 0 | 10 | +10 |
| NavTestMapLoaderTests | 0 | 11 | +11 |
| NavigationFakesModuleTests | 0 | 4 | +4 |
| Pre-existing Navigation tests | 125 | 125 | 0 (no regression) |

All 151 tests pass. 0 failures. 0 skipped.

---

## Design Decisions & Deviations

1. **`RegisterProviders` only registers `INavmeshProvider`**: `IDtCrowdProvider`, `IVolumetricPathProvider`, and `IPathRegistry` are NOT registered as ECS managed singletons because the ECS requires `[ComponentId]` attributes on all registered types. These interfaces lack ComponentIds (and adding them to `GlobalComponentIds` would require production infrastructure changes outside scope). Tests access these providers directly via module properties.

2. **`FakeCrowdComponents` not registered in `NavigationTestWorldFactory`**: The `FakeCrowdAgentState` and `FakeCrowdGlobalState` ECS components are not used by any existing systems yet (Phase 3 adds the crowd system). They will be registered in BATCH-06.

3. **`NavTestMapBuilder.Adjacent()` is bidirectional**: The DSL `Adjacent(from, to)` adds both directions (from->to and to->from). This matches JSON adjacency format which requires explicit bidirectional entries.

4. **`FakeDtCrowdProvider.Update()` fallback position**: If an entity has no `SimTransform` in the view, the provider uses the agent's last velocity as a fallback position origin (harmless for tests).

---

## Debt Items

No new debt added. Pre-existing debt entries unchanged.

---

## Next: BATCH-06

Phase 3: NAV-P3-T1 (CrowdAgent admission + CrowdAgentUpdateSystem) and NAV-P3-T2 (OffMeshLinkDetectionSystem).
