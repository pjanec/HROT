# BATCH-07 Review — Gizmo Renderer Wiring & Entity Health Bar

**Reviewer:** Dev Lead
**Decision:** APPROVED (deviations are spec fixes, not regressions)

---

## Test Results

| Scope | Tests | Result |
|---|---|---|
| Hrot.IG.Tests (gizmo filter) | 15 | All pass |

---

## GZ020 — Local Gizmo Renderer Wiring

### Review

`IgApplication` correctly wired:
- `_gizmoBuffer = new DebugPrimitiveBuffer(capacity: 4096)` — sensible default capacity.
- `_gizmoRegistry = new GizmoRegistry()`
- `new DebugGizmoLayer(31, _gizmoBuffer, _world.Bus)` added after `ZoneObstacleRenderLayer`.
- `DataDrivenGizmoSystem` registered via `_kernel.RegisterGlobalSystem(...)` with `_gizmoRegistry`
  and `_gizmoBuffer` (which implements `IDebugDrawBuilder`). Predicate null for now (D-003 open).
- `GizmoRegistry` exposed as public property for external gizmo registration.
- `GizmoBuffer` exposed as internal for testing.

Tests SC-GZ020-1/2/3 all pass.

### Deviations (spec fixes)

- `IStatefulGizmo.UpdateAndDraw` actual signature: `(ISimulationView, Entity, float, IDebugDrawBuilder)`
  — no `bool isSelected` parameter. Instructions had wrong signature. Code adapted correctly.
- `IGizmoDefinition.RequiredComponents` is `Type[]` not `int[]`. Instructions had wrong type. Adapted.
- Kernel registration uses `RegisterGlobalSystem` not `RegisterSystem`.

---

## GZ021 (partial) — Entity Health Bar Gizmo

### Review

- `HealthBarGizmoSettings`: clean key constants + typed defaults with `GizmoSettingValue.From(float)`.
- `HealthBarGizmoDefinition`: correctly declares `RequiredComponents` as `new[] { typeof(IgHealthState) }`.
  `VisibilityPolicy = AlwaysVisiblePolicy.Instance`. `CreateInstance()` returns `HealthBarGizmoInstance`.
- `HealthBarGizmoInstance`: reads `IgHealthState.Damage`, computes health %, reads bar dimensions from
  settings via `GizmoSettingsRegistry.ComputeHash(key)` then `.Read(hash).FloatValue`. Colors:
  green >=66%, yellow >=33%, red <33%. Calls `draw.DrawEntityBadge(entity, text)` with % text.
- `GizmoRegistrar.Register(registry, settings)`: registers settings defaults then health bar definition.
- `CapturingDrawBuilder` test double in tests captures `DrawEntityBadge` calls.

Tests SC-GZ021-HB-1 through SC-GZ021-HB-5 all pass.

### Accepted deviation

- `GizmoSettingsRegistry.IsRegistered` is `internal` — tests use `EnumerateAll()` to verify
  registration instead. Correct pattern; no change needed.
- Health bar renders as `DrawEntityBadge` text ("100%") rather than a geometric bar. Acceptable as
  first pass — geometric `DrawBox2D` gizmo requires `Box2D` shape support in renderer.

---

## Remaining Phase 7 work (BATCH-08+)

- GZ021 remaining gizmos: measure tool, hill attack, spatial grid, entity rotation, visibility cones
- These require understanding more domain components (HillAttack behavior state, SimTransform, etc.)
