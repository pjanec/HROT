# Technical Debt & Deferred Issues Tracker

Tracks P2/P3 issues, known risks, and design decisions deferred from batch reviews.  
**P1 issues are never deferred** — they become Corrective Task 0 in the next batch.

Update this file when an item is resolved. Do not delete resolved rows — mark them ✅.

---

## How to Use

- **Dev lead:** during each review, add any new P2/P3 items here before writing the next batch.  
- **Developer:** check this file during onboarding. If your batch touches a file mentioned here, fix the relevant item even if it wasn't explicitly assigned.
- **Priority:** P2 = fix within the next 1–2 batches; P3 = fix before Phase complete or whenever the area is touched.

---

## Open Items

| ID | Sev | Source | Description | Target | Status |
|---|---|---|---|---|---|
| IG-DEBT-001 | P2 | IG-BATCH-01 | CycloneDDS IDLC prerequisite undocumented in main guide. Update README. | Docs | ✅ Resolved IG-BATCH-02 |
| IG-DEBT-002 | P3 | IG-BATCH-01 | rlImGui vs rlImgui-cs naming ambiguity in TASK-DETAILS-IG.md. | Docs | ✅ Resolved IG-BATCH-02 |
| IG-DEBT-003 | P3 | IG-BATCH-01 | `MapCamera.ZoomSpeed` misnamed, lacks symmetric inverse (log-scale proposed). | FDP.Toolkit.Vis2D | Open |
| IG-DEBT-004 | P3 | IG-BATCH-01 | `MapCamera.ProcessInput` `isInputCaptured` causes boilerplate. | FDP.Toolkit.Vis2D | Open |
| IG-DEBT-005 | P3 | IG-BATCH-01 | `MapCamera.FocusOn` bypasses interpolation, needs `NavigateTo` for smooth programmatic panning. | FDP.Toolkit.Vis2D | Open |
| IG-DEBT-006 | P3 | IG-BATCH-02 | `StubVisualizerAdapter` hit radius is constant, should adapt to zoom for accuracy at different scales. | IG Module | Open |
| IG-DEBT-007 | P3 | IG-BATCH-02 | `StubVisualizerAdapter.Render` uses string interpolation `$"#{netId.Value}"` creating per-frame allocation per entity. Cache label string text. | IG Module | Open |
| IG-DEBT-008 | P3 | IG-BATCH-02 | `IEntityCommandBuffer` lacks a `SetAnyComponent` allowing `[DdsManaged]` structs like `EntityInfo` to bypass indirect publication commands. | FDP.Interfaces | Open |
| IG-DEBT-009 | P3 | IG-BATCH-03 | `view.HasManagedComponent<T>` causes a dictionary lookup in `StyleResolutionSystem`. Cache this as an unmanaged tag bitset to skip clean entities. | IG Module | Open |
| IG-DEBT-010 | P3 | IG-BATCH-04 | `cmd.AddComponent/SetComponent` in `MapCullingSystem` is called unconditionally every tick. Add a read-modify-write guard to heavily reduce command buffer pressure. | IG Module | Open |
| IG-DEBT-011 | P3 | IG-BATCH-04 | AABB test in `MapCullingSystem` iterates entities individually. Refactor to archetype chunking for SIMD vectorisation. | IG Module | Open |
| IG-DEBT-012 | P3 | IG-BATCH-05 | `MeasureTool` computes flat Euclidean distance instead of Haversine algorithm which applies proper Geolocation map warping ratios. Fix when Geo-Projections added. | IG Module | Open |
| IG-DEBT-013 | P4 | IG-BATCH-05 | `MeasureTool` leaks `_startPoint` if swapped out mid-measure via `canvas.PushTool`. Clear internal state in `OnEnter` or explicitly validate state contexts on re-entry. | IG Module | Open |
| IG-DEBT-014 | P4 | IG-BATCH-06 | `HistoryTrail` uses a fixed 64-element array in an unmanaged component. If longer trails are required, this needs refactoring to a chunked linked-list approach to avoid exploding struct size. | IG Module | Open |

---

## Resolved Items (archive)

| ID | Sev | Description | Resolved In |
|---|---|---|---|
| | | | |

---

## Notes
- Initialized for SimHost development.
