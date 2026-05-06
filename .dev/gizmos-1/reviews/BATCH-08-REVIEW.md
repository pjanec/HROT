# BATCH-08 Review — Entity Rotation, Visibility Cone, Hill Attack Gizmos

**Reviewer:** Dev Lead
**Decision:** APPROVED

---

## Test Results

| Scope | Tests | Result |
|---|---|---|
| Hrot.IG.Tests (gizmo filter) | 28 | All pass |

13 new tests (3 ROT + 3 VIS + 5 HA + 2 registrar), 15 prior.

---

## GZ021-ROT — Entity Rotation Display Gizmo

### Review

- `EntityRotationGizmoSettings`: single `ArrowLength` float setting (30m default). Clean.
- `EntityRotationGizmoDefinition`: `RequiredComponents = [typeof(SimTransform)]`. `AlwaysVisiblePolicy`. Correct.
- `EntityRotationGizmoInstance`: extracts yaw from `SimTransform.Rotation` quaternion using
  `atan2(2*(W*Z + X*Y), 1 - 2*(Y*Y + Z*Z))`. Computes compass degrees (0=north, 90=east, clockwise)
  matching the existing `EntityRotationTool` formula. Draws arrow + heading label. Correct.
- 3 tests pass (arrow direction, text emission, RequiredComponents check).

### Deviation

- `FixedString32` uses constructor syntax (`new FixedString32("...")`) not `TryWrite` — correct; matches
  existing usage in HealthBarGizmoInstance.

---

## GZ021-VIS — Visibility Cone Gizmo

### Review

- `VisibilityConeGizmoDefinition`: `RequiredComponents = [typeof(SimTransform), typeof(PerceptionReceptor)]`. Correct.
- `VisibilityConeGizmoInstance`: computes half-angle via `MathF.Acos(FieldOfViewCos)`, draws two edge
  lines + 8-segment arc for cone boundary. Uses semi-transparent cyan. Returns early when `VisionRange == 0`.
- 3 tests pass (RequiredComponents, DrawLine count >=2, no draws when range=0).

---

## GZ021-HA — Platoon Hill Attack Gizmo

### Review

- `HillAttackGizmoSettings`: single `ShowSlots` bool setting (true default).
- `HillAttackGizmoDefinition`: `RequiredComponents = [typeof(BrainBlackboard), typeof(BehaviorState), typeof(SimTransform)]`. Passes settings to instance.
- `HillAttackGizmoInstance`: correctly skips draw when `ActiveBehaviorHash != 3014`. Uses `unsafe`
  context with `Unsafe.AsRef(in bb)` to cast away `readonly` before projecting `PlatoonHillAttackParams`.
  Draws fire line (blue) + baseline (green). Draws numbered slots via `DrawSphere` when `ShowSlots=true`.
- 5 tests pass (RequiredComponents, no draw when wrong hash, lines drawn, slots on/off).
- `Hrot.IG.csproj` + `Hrot.IG.Tests.csproj`: added `<ProjectReference>` to `Hrot.AI.Behaviors.csproj`
  for `PlatoonHillAttackParams` type. Acceptable dependency — Hrot.IG already depends on Hrot domain.

### Deviation

- `BrainBlackboard` fixed buffer: `Unsafe.AsRef(in bb)` used to drop `readonly` before `fixed` pinning.
  Correct workaround for the C# limitation on `fixed` on `ref readonly` structs.

---

## Infrastructure

- `FullCapturingDrawBuilder.cs`: shared test double capturing all draw call types. Replaces the
  partial stub from BATCH-07. Clean consolidation.
- `GizmoRegistrar.cs`: extended to register all four gizmos (health bar + 3 new). Correct.

---

## Remaining GZ021 work (BATCH-09)

- Map measure tool (global interactive gizmo wrapping MeasureTool)
- Spatial grid global gizmo (requires SpatialHashGrid exposure or ISpatialGridQuery interface)
