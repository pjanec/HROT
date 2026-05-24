# BATCH-09 Review — Map Measure Tool Gizmo

**Reviewer:** Dev Lead
**Decision:** APPROVED

---

## Test Results

| Scope | Tests | Result |
|---|---|---|
| Hrot.IG.Tests (gizmo filter) | 36 | All pass |

8 new tests, 28 prior.

---

## GZ021-MT — Map Measure Tool Gizmo

### Review

- `MeasureToolGizmoSettings`: two settings — `MeasureTool.Active` (bool, default false) and
  `MeasureTool.Units` (int 0=meters/1=km, default 0). Registration clean.
- `MeasureToolGizmoAdapter`: bridges `GizmoSettingsRegistry` to `MapCanvas.PushTool`/`PopTool`.
  Push when `Active` transitions false→true, pop when true→false (guards with `canvas.ActiveTool == _tool`).
  Syncs `DisplayUnits` on push and while active. Correct.
- `MeasureTool` (modified): added `MeasureDisplayUnits` enum + `DisplayUnits` property + km label branch.
  Only label formatting line changed; rest of tool unchanged. Clean diff.
- `GizmoRegistrar`: extended with `MeasureToolGizmoSettings.Register`. Correct.
- `IgApplication`: adapter created in `InitializeNetwork` (where `_gizmoRegistry` is available),
  adapter.Update called before `_canvas.Update`. Correct.

### Deviation

- Wiring placed in `InitializeNetwork`, not `InitializeEcs` — `_gizmoRegistry` is initialized there.
  This is correct; the instructions noted "find the exact location."
- 8 tests instead of 5 — additional coverage for no-op call (Active stays false), already-popped guard,
  second push guard. Good quality.

---

## DEBT-TRACKER

D-005 added: spatial grid gizmo deferred (requires ISpatialGridView public interface in FDP).

---

## GZ021 completion status

All six GZ021 sub-gizmos now done:
- [x] Entity health bar (BATCH-07)
- [x] Entity rotation display (BATCH-08)
- [x] Visibility cones (BATCH-08)
- [x] Platoon hill attack (BATCH-08)
- [x] Map measure tool (BATCH-09)
- [ ] Spatial grid — deferred as D-005

GZ020 and GZ021 (5/6 sub-gizmos) complete. Workstream effectively done; spatial grid is
the only open item and is explicitly deferred.
