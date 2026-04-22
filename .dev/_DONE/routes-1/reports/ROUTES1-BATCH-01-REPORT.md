# ROUTES1-BATCH-01 Report

**Batch:** ROUTES1-BATCH-01  
**Developer:** GitHub Copilot  
**Date:** 2025-07-17  
**Status:** Complete

---

## 📊 Task Completion

| Task ID      | Status | Notes |
|--------------|--------|-------|
| ROUTES1-T001 | ✅ Done | `RoutePlan` managed component + `RouteWaypoint` struct; `GlobalComponentIds` constants 220–222 added |
| ROUTES1-T002 | ✅ Done | `PersonalRouteRef`, `RouteTrajectoryCache` blittable structs; `CmdAppendPersonalWaypoint` event struct |
| ROUTES1-T003 | ✅ Done | `TacGraphic_Route` (type 8802) registered in `BdcTkbCatalog`; `RoutePlan` attached via `RouteTkbExtensions` |
| ROUTES1-T004 | ✅ Done | `MapRouteEgressTranslator` — version-delta dirty tracking, geo conversion, DDS `MapRoute` publish |
| ROUTES1-T005 | ✅ Done | `MapRouteIngressTranslator` — DDS ingress, `_pendingRoutes` retry queue, `ApplyToEntity` for replay |

---

## 🧪 Testing Results

**Unit Tests Passed:** 30 new / 30 new (all passing)  
**Integration Tests Passed:** N/A (no integration harness changes required)

**Test files added:**
- `Hrot.Map.Common.Tests/RoutePlanTests.cs` — 12 tests (T001 + T002)
- `Hrot.SimHost.Tests/TacGraphicRouteBlueprintTests.cs` — 8 tests (T003)
- `Hrot.Map.Common.Tests/MapRouteTranslatorTests.cs` — 11 tests (T004 + T005)

**Total solution tests after batch:**  
`Hrot.Map.Common.Tests`: 83 Passed, 0 Failed  
`Hrot.SimHost.Tests`: 282 Passed, 0 Failed (1 pre-existing flaky test in `JsonToRecordCompilerTests` unrelated to ROUTES1)  
`Hrot.ExCon.Tests`: 283 Passed, 0 Failed

**Key Test Scenarios Verified:**
- [x] `RoutePlan` ECS round-trip preserves all waypoint fields (position, speed, extension JSON)
- [x] `PersonalRouteRef`, `RouteTrajectoryCache`, `CmdAppendPersonalWaypoint` are blittable (`Marshal.SizeOf` does not throw)
- [x] `ComponentId` attribute values correct (220, 221, 222)
- [x] TKB blueprint spawns `RoutePlan` with empty waypoints and independent instances per entity
- [x] TKB blueprint does NOT include `EditablePolyline`
- [x] `road_graphs` layer predicate accepts `TkbType == TkbEntityTypes.TacGraphic_Route` (8802)
- [x] Egress: 3-waypoint entity publishes exactly once with correct `Points.Count`
- [x] Egress: `IsLoop`, `TargetSpeed`, `ExtensionJson` faithfully propagated
- [x] Egress: `GeoPoint` round-trip error ≤ 1 mm (WGS84 at 48.8566°N, 2.3522°E)
- [x] Egress: dirty flag suppresses re-publish when `RoutePlan.Version` unchanged
- [x] Ingress: 5-waypoint `MapRoute` sample produces correct waypoint count
- [x] Ingress: `GeoPoint` round-trip error ≤ 1 mm
- [x] Ingress: `IsLoop`, `TargetSpeed`, `ExtensionJson` faithfully propagated
- [x] Ingress: `Version` increments on each processed sample
- [x] Ingress: unknown entity ID deferred to `_pendingRoutes`, resolved on next `PollIngress`

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation, particularly with blittable memory configurations and DDS network propagation? How did you resolve them?**

Two compiler issues surfaced during implementation:

1. **`in` parameter on a pattern-matched local variable (CS8156):** `ApplyToEntity` receives `object data` and pattern-matches it to a local `MapRoute mapRoute`. Passing that local with `in` to a helper—`BuildRoutePlan(in mapRoute, …)`—was rejected by the compiler because a pattern-match binding is not a writable memory location eligible for by-ref aliasing. Also, calling `ProcessSample(in sample.Data, …)` failed because `sample.Data` is a property accessor, not a ref-eligible location. Both were fixed by materialising the value into a local variable first (`var sampleData = sample.Data;`) and dropping the `in` qualifier on the pattern-match binding.

2. **`IDdsWriter<T>.DisposeInstance` not implemented in existing test stubs:** Adding `DisposeInstance(T key)` to the `IDdsWriter<T>` interface broke three pre-existing stub implementations (`CapturingMenuWriter`, `CapturingWriter<T>`, `StubAckWriter`). Each stub required a no-op implementation method.

**Q2: Did you spot any weak points in the existing Map Layer mapping architecture or ECS definitions? What would you improve?**

- **`EDescriptorType` is an unguarded enum.** Values are assigned by implicit position (dtMapRoute = 5). If a future developer inserts an enum member before dtMapRoute, the ordinal changes silently and breaks DDS topic discrimination. Explicit assignment (`dtMapRoute = 5`) or a dedicated constants block would guard against this.

- **`IDdsWriter<T>.DisposeInstance` semantics are unclear.** The interface contract does not document when `DisposeInstance` must be called vs. when `Dispose(long networkEntityId)` on the translator is called. As translators multiply, this ambiguity risks leaked DDS instances. A short XML-doc comment on the method would clarify the lifecycle.

- **`RoutePlan.Version` is manually maintained.** Callers must remember to increment `Version` whenever waypoints change; there is no enforced write path that does this automatically. A `Mutate(Action<RoutePlan>)` helper or a derived `SetWaypoints` method on the class would guarantee version consistency.

**Q3: What design decisions did you make regarding spatial anchoring, conversion handling or component configuration?**

- **Version-delta dirty tracking instead of `SmartEgressUtil`:** The egress translator tracks a `Dictionary<Entity, int> _publishedVersions` keyed on entity rather than using the shared `SmartEgressUtil` flag infrastructure. This is deliberate because `SmartEgressUtil` operates at a single-flag granularity per descriptor type, while the route may be updated by many systems at different frequencies. The per-entity version integer provides natural idempotency: if a system stamps `RoutePlan.Version` before publish, no extra publish occurs on the next frame.

- **Circular-dependency break via `RouteTkbExtensions`:** `Hrot.Map.Definitions` does not reference `Hrot.Map.Common` (doing so would create a circular reference because `Hrot.Map.Common` depends on `Hrot.Map.Definitions` for `TkbEntityTypes`). To attach `RoutePlan` to the `TacGraphic_Route` blueprint, a `RouteTkbExtensions.ApplyRoutePlanToBlueprint(TkbDatabase)` static method was added in `Hrot.Map.Common` and called post-hoc from `HrotEnvironment.CreateTkb()`. This keeps all TKB registration in `BdcTkbCatalog` while the component wiring lives where the component is defined.

- **`double` → `float` precision choice for `TargetSpeed` and `ExtensionJson`:** DDS `Waypoint.SpeedMetersPerSec` is `double`; the ECS `RouteWaypoint.TargetSpeed` is `float` (4 bytes). The truncation is intentional: ECS physics operates in single-precision. The loss for speed (max ~1.5 × 10³ m/s) at `float` resolution is sub-millimetre/second and acceptable.

**Q4: What edge cases did you discover mapping local floating point spaces into DDS packets that weren't mentioned in the spec?**

- **Null `Points` list on incoming `MapRoute`:** `MapRoute.Points` can be `null` (the DDS IDL default-constructs it as `null` for zero-waypoint routes rather than an empty list). `BuildRoutePlan` guards with a `if (data.Points != null)` check; without it a null-dereference crash would occur silently on well-formed but empty route announcements.

- **`null` vs. empty string for `ExtensionJson`:** DDS serialises a missing string field as `null`; the ECS `RouteWaypoint.ExtensionJson` documents `null` as "no extension data". Using `string.IsNullOrEmpty(wp.ExtensionJson) ? null : wp.ExtensionJson` on ingress normalises empty-string DDS artefacts to `null`, keeping ECS state canonical.

- **Origin dependency of `WGS84Transform`:** `WGS84Transform.ToCartesian` is relative to the configured origin. If the origin is not set before the ingress translator resolves deferred samples, coordinates are silently offset from the world origin. This is an existing concern across all map translators; a null-check on `_geoTransform` is already gated at translator construction time (the translator is only created when `_geoTransform != null` in `IgApplication`).

**Q5: Are there any performance concerns or optimization opportunities you noticed while reading the ECS components and iterating over Lists?**

- **`List<RouteWaypoint>` allocation per `RoutePlan` set:** Every call to `BuildRoutePlan` allocates a new `RoutePlan` instance and a new `List<RouteWaypoint>`. For routes with many waypoints updated at high frequency (e.g., a live track), this produces significant GC pressure. A pooled `RoutePlan` with an in-place `Waypoints.Clear()` + re-fill approach, or a blittable waypoint array stored inline, would reduce allocations to zero after warm-up.

- **`Dictionary<Entity, int>` lookup on every `ScanAndPublish` frame:** The egress translator iterates all route entities and performs a `_publishedVersions.TryGetValue` per entity. With many concurrent routes (hundreds), this is O(n) dictionary reads per frame. An alternative would be to store `RoutePlan.Version` at last-publish time directly as a secondary component (`RouteEgressMeta`), avoiding the external dictionary entirely.

- **`_pendingRoutes` retry on every `PollIngress` tick:** Currently all deferred routes are re-tried every frame until resolved. If entity creation is delayed by many frames (common in late-join scenarios), this causes repeated `Dictionary` lookups. A more elegant approach would be a registration callback from `NetworkEntityMap` that triggers ingress processing only when a specific `netId` becomes available.

---

## ⚠️ Outstanding Issues / Next Steps

- [ ] `RoutePlan.Version` has no enforced write path — consider a helper method or setter guard to auto-increment
- [ ] `EDescriptorType.dtMapRoute` uses implicit ordinal assignment — recommend explicit `= 5`
- [ ] Per-entity version dictionary in `MapRouteEgressTranslator` adds GC pressure at scale — worth revisiting when route count grows
- [ ] `_pendingRoutes` retry queue re-scans every frame — a `NetworkEntityMap` registration callback would be more efficient
