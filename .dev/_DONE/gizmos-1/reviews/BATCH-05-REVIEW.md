# BATCH-05 Review — 2D Presentation Adapter (GZ011-GZ014)

**Status: APPROVED**

## Verification

### Scope
All four tasks implemented:
- GZ011: `DebugPrimitiveRenderer2D` — filter/sort/dispatch pipeline ✅
- GZ012: EntityLocal spatial resolution via `ISimulationView` ✅
- GZ013: `DebugGizmoLayer` integration with hit-testing ✅
- GZ014: `RichTextRenderer` with control-byte parser ✅

### Test Results
```
dotnet test ... --filter "FullyQualifiedName~Gizmos"
Passed! - Failed: 0, Passed: 24, Skipped: 0, Total: 24
```
All 24 new tests pass. 3 pre-existing `EntityInspectorPanel` failures confirmed unrelated.

### Code Quality
- `DebugPrimitiveRenderer2D` is non-sealed with `protected virtual DispatchShape` — correct for test injection via `CapturingRenderer2D`.
- `CapturingRenderer2D` defined at namespace level, usable by both renderer tests and layer tests — good.
- `HandleInput` iterates the frame buffer correctly; uses `const float HitRadiusWorld = 5f` and publishes `GizmoInteractionStartedEvent`.
- `RichTextRenderer.ParseChunks` has unit tests covering control bytes, plain text, Yellow, and unknown color bytes.
- GZ012 tests use real `EntityRepository` instead of Moq — this is the correct approach for `ref readonly T` returning interfaces.

### Design Deviations — All Acceptable
1. **Token as computed property**: Size=64 explicit layout prevents adding a field. Derived from Anchor is semantically equivalent.
2. **Event-only hit response**: Publishing `GizmoInteractionStartedEvent` instead of pushing a proxy tool. Correct architectural decision without canvas access. Tracked as D-005.
3. **EntityRepository in GZ012 tests**: Better than a hand-written stub; gives real IsAlive/HasComponent/GetComponentRO semantics.
4. **ParseChunks allocation**: Documented and tracked as D-004. Acceptable for frame-rate rendering.

### Early-Failure Check
- No swallowed exceptions found.
- `DispatchShape` default branch silently skips unrecognized shapes — acceptable (log if needed in future).
- `HandleInput` returns `false` cleanly when no entity is hit.

## Debt Updates
- D-003 retargeted to GZ015 (selection predicate wiring belongs with GlobalDebugSettings).
- D-004, D-005, D-006 added.

## Suggested Git Commit Message
```
feat(gizmos): GZ011-GZ014 — 2D presentation adapter

- Add DebugPrimitiveRenderer2D: filter/sort/dispatch pipeline for Map2D
  layer with MinZoomLod/MaxZoomLod culling and layer-bit masking
- Add RichTextRenderer: control-byte parser and Raylib DrawText drawer
  for EntityBadge rich-text content (FixedString32)
- Modify DebugGizmoLayer: integrate renderer (3 constructors for prod/test),
  HandleInput hit-testing publishing GizmoInteractionStartedEvent
- Modify DebugPrimitive: add Token computed property (Anchor -> PickToken)
- Add 24 tests: SC-GZ011-1..9, SC-GZ012-1..2, SC-GZ013-1..4, SC-GZ014-x
```

## Next Batch
Plan BATCH-06 to cover **Phase 6: Remote Visualization Foundation** (GZ015-GZ018).
D-003 (selection predicate wiring) should be included alongside GZ015.
