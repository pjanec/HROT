# BATCH-06 Review — Remote Visualization Foundation (GZ015-GZ018 + D-003)

**Reviewer:** Dev Lead
**Decision:** APPROVED (deviations noted and accepted)

---

## Test Results Summary

| Scope | Tests | Result |
|---|---|---|
| Fdp.Toolkits.Tests (all gizmo) | 102 | All pass |
| Hrot.IG.Tests (gizmo filter) | 7 | All pass |
| Hrot.ClusterRunner.Tests (gizmo filter) | 2 | All pass |
| **Total new in this batch** | **~14** | **All pass** |

---

## Per-Task Review

### D-003 — Selection Predicate Wiring

Verified: `DataDrivenGizmoSystem` is not constructed in Hrot.ClusterRunner at this point
(no registration site exists yet). Unit tests in Hrot.ClusterRunner.Tests verify the predicate
contract at system level (predicate-false skips, predicate-true allows). This is the appropriate
scope for now — wiring will happen once the kernel registration site is established.
**Result: 2 tests pass. Accepted.**

### GZ015 — GlobalDebugSettings ECS Singleton

- `GlobalDebugSettings` struct: `[StructLayout(LayoutKind.Sequential)]`, `[ComponentId(185)]`,
  `[DataPolicy(DataPolicy.Transient)]`. `ForceAllGizmosVisible` (bool with `[MarshalAs(I1)]`),
  `DebugLayerMask` (ushort). Correct.
- `HrotComponentIds.GlobalDebugSettings = 185` added.
- `GlobalDebugSettingsPanel.cs` stub created in Hrot.IG.Gizmos.
- **SC-GZ015-1/2/3/4: 4 tests pass.**

### GZ016 — DebugPrimitivesBatch DDS Topic

- Follows `StringInternBatch.cs` pattern exactly: `[DdsTopic]`, `[DdsQos(BestEffort/Volatile/KeepLast/1)]`.
  `FrameNumber` and `NodeId` as `[DdsKey]`. `Primitives` as `[DdsManaged] DebugPrimitive[]`.
- **SC-GZ016-1: 1 test pass.** Round-trip skipped (CycloneDDS.Runtime not in test project — correct).

### GZ017 — GizmoUiState DDS Topic + GizmoSettingsPublisherSystem

- `GizmoUiState`: `[DdsTopic("GizmoUiState")]`, `[DdsQos(Reliable/TransientLocal/KeepLast/1)]`.
- `IGizmoUiStatePublisher` interface: clean abstraction for test injection.
- `GizmoSettingsPublisherSystem`: correctly uses `IsDirty`, `_firstFrame` guard, `ClearDirty()`.
  `EnumerateAll()` tuple names adapted to actual `GizmoSettingValue` property names
  (`Type`/`BoolValue`/`FloatValue`/`IntValue` instead of spec's `.Kind`/`.AsBool` etc.).
- `CapturingPublisher` test helper: clean.
- **SC-GZ017-1/2/3/4: 4 tests pass.**

### GZ018 — IGCapabilitiesAnnounce + IGCapabilitiesPublisherSystem

- `IGCapabilitiesAnnounce`: `[DdsTopic("IGCapabilitiesAnnounce")]`, `[DdsQos(Reliable/TransientLocal/KeepLast/1)]`.
  `NodeId` (uint, `[DdsKey]`), `SupportedTargets` (PipelineTarget), `SupportedLayerMask` (ushort),
  `SupportedShapes` (byte), `LayerNamesJson` (`[DdsManaged] string`).
- `IGCapabilitiesPublisherSystem`: idempotent publish on first Execute call. `_published` flag correct.
- `CapturingDdsWriter<T>` test helper follows established pattern.
- **SC-GZ018-1/2/3: 3 tests pass.**

---

## Accepted Deviations

| # | Deviation | Impact |
|---|---|---|
| D-004 | `GizmoSettingValue` uses `Type`/`BoolValue`/`FloatValue`/`IntValue` properties, not `.Kind`/`.AsBool`/`.AsFloat`/`.AsInt` as spec stated. Code adapted correctly. | None — spec had wrong property names |
| D-005 | `IEcsModuleSystem` used (not non-existent `IModuleSystem`). | None — correct interface |
| D-006 | `SystemPhase.Initialization` does not exist — `IGCapabilitiesPublisherSystem` uses `PostSimulation` with `_published` flag. | None — semantically equivalent |
| D-007 | `DataDrivenGizmoSystem` not constructed in ClusterRunner — no wiring site exists yet. Predicate contract verified at unit level only. | Low — wiring tracked as D-003 open |
