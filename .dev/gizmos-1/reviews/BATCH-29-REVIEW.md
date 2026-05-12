# BATCH-29 Review

**Status: APPROVED**

---

## Summary

BATCH-29 implements the full Phase 22 scope (TASK-GZ064 through TASK-GZ067: Composite Gizmo
Identity) plus the three corrective fixes from BATCH-28-REVIEW. Build is clean (0 errors). All
newly introduced tests exercise real behaviour and match the DESIGN.

---

## Corrective Fixes from BATCH-28

### EDG-001 (Box2D shape)
`EntityDragGizmoTests.UpdateAndDraw_EmitsBox2DWithValidPickToken` now correctly searches for
`Box2D` primitive. Test verifies `AnchorIndex`, `AnchorGeneration`, and shape in a single
assertion chain. **Accepted.**

### SIS-002 / SIS-008 (rubber-band start-of-click)
Tests rewritten to reflect the post-BATCH-28 behaviour: a null-entity click starts rubber-band
mode rather than immediately clearing selection. `SIS-002` confirms no immediate clear on start;
`SIS-008` confirms the `OnSelectionChanged` callback fires after the tiny-drag commit that
finalises the rubber-band. **Accepted.**

---

## TASK-GZ064: Add GizmoTypeId to Network Contracts

### Code quality
`DebugPrimitive`: `[FieldOffset(60)] public uint GizmoTypeId` with accurate comment explaining
the alias with `SemanticShape.ResolvedRollRad` and the safety guarantee. Layout analysis in the
comment is correct: Box2D ends at offset 52 for the anchor id, so offset 60 is free.

`GizmoPickToken`, `GizmoInteractionBatch`, `PickToken`: field additions are minimal and
backward-compatible (default = 0 = legacy).

`IGizmoDefinition.GizmoTypeId`: declared with no default implementation — all implementors
(including `EntityDragGizmoDefinition`, mock types, `D003GizmoDef`) provide an explicit value.
This is the correct design decision; a default of 0 would silently defeat composite routing.

### Test quality
| ID | Test | Assessment |
|----|------|-----------|
| SC-GZ064-1 | `Marshal.SizeOf<DebugPrimitive>() == 64` | Strong — guards layout forever |
| SC-GZ064-2 | `GizmoPickToken.GizmoTypeId` defaults to 0 | Adequate |
| SC-GZ064-4 | `PickToken.GizmoTypeId` round-trips | Adequate |
| SC-GZ064-5 | `EntityDragGizmoDefinition.GizmoTypeId` is non-zero and stable across instances | Strong — verifies FNV-1a consistency |

**Verdict: Accepted.**

---

## TASK-GZ065: GizmoTypeId Injection into Emitted Primitives

### Code quality
`DebugPrimitiveBuffer.StampGizmoTypeId(int fromIndex, uint gizmoTypeId)`:
- Iterates `[fromIndex, Count)` and writes only to `Box2D`, `StructInspector`,
  `ContextMenuBinding` — correctly excludes `SemanticShape` and `SpatialAnchor`.
- Uses `Count` (not `_count`) for the upper bound, so the cap at `_primitives.Length` is
  respected.
- Persistent primitives are not stamped (method operates on `_primitives`, not `_persistent`).

`DataDrivenGizmoSystem` and `GlobalGizmoManager` both record a watermark before each
`UpdateAndDraw` call and invoke `StampGizmoTypeId(mark, gi.Definition.GizmoTypeId)` immediately
after. This is the correct two-line pattern specified in the DESIGN.

### Test quality
| ID | Test | Assessment |
|----|------|-----------|
| SC-GZ065-1 | Box2D stamped | Core happy path |
| SC-GZ065-2 | SemanticShape NOT stamped while Box2D in same batch IS stamped | Critical — guards the alias |
| SC-GZ065-3 | Stamp with 0 writes 0 (sentinel clears previous value) | Correct edge case |
| SC-GZ065-4 | `fromIndex >= Count` is a no-op, no exception | Bounds check |
| SC-GZ065-5 | ContextMenuBinding stamped | Covers third allowed shape |

Good coverage. SC-GZ065-2 is the most important test and correctly verifies both sides of the
predicate. **Accepted.**

---

## TASK-GZ066: Fix DataDrivenGizmoSystem Routing + Wire Egress/Ingress

### Code quality
`FindGizmo(Entity entity, uint gizmoTypeId)`:
- Generation-0 path: scans `_activeGizmos` by index when `entity.Generation == 0`. This is
  necessary because `GizmoStructUpdateEvent.AnchorId` and `GizmoMenuActionEvent.AnchorId` carry
  only entity index; the live entity has generation ≥ 1 and would fail `TryGetValue`.
- Legacy fallback: returns `list[0]` when `gizmoTypeId == 0`.
- Injected on-demand gizmos checked first (unchanged priority).

The fix is correct and targeted. The design section 11.5 specifies this exact strategy.

Egress/ingress translators all flow `PickGizmoTypeId` through the `GizmoInteractionBatch` DDS
topic field. `DdsGizmoInteractionPublisher` is also updated. Network round-trip is complete.

`GizmoInteractionEvents.GizmoStructUpdateEvent` and `GizmoMenuActionEvent` both gain
`public uint GizmoTypeId`. `ImGuiPropertyTreeAdapter.DrawScheduled` callback signature upgraded
to `Action<long, uint, string>?`; apply button invokes with `item.GizmoTypeId` — correctly uses
gizmo class hash, NOT `item.SchemaHash`.

### Test quality
| ID | Test | Assessment |
|----|------|-----------|
| SC-GZ066-1 | Interaction started routed to def1 only (by GizmoTypeId) | Strong — negative assertion verifies def2 untouched |
| SC-GZ066-2 | StructUpdate routed to def2 only; JSON payload preserved | Strong — verifies full pass-through |
| SC-GZ066-3 | Egress translator: `PickGizmoTypeId` in written batch equals token's `GizmoTypeId` | Strong |
| SC-GZ066-5 | MenuAction routed to def2 only; `actionId` value preserved | Strong — negative assertion included |

All four tests use a realistic fixture with a `TrackingGizmo` / `TrackingGizmoDefinition` pair
that accurately mimics production routing. The generation-0 bug fix is exercised by SC-GZ066-2
and SC-GZ066-5 (both use `entity.Index` rather than the full entity). **Accepted.**

---

## TASK-GZ067: Populate GizmoTypeId in Terminal Pick Token

### Code quality
`DebugGizmoLayer.HandleInput` (both GizmoMap and Fdp variants): `GizmoTypeId = hit.GizmoTypeId`
added to token construction. `GizmoMap.Viewer/Program.cs` also updated. The chain is complete:
stamped primitive → pick token → interaction batch → ingress translator → ECS event.

### Test quality
| ID | Test | Assessment |
|----|------|-----------|
| SC-GZ067-1 | Token construction replicates the HandleInput logic and asserts `GizmoTypeId` is preserved | Adequate — logic is data-copy only, deep layer test would require ImGui harness |

The test replicates the data-copy logic in isolation. This is the appropriate test boundary since
`DebugGizmoLayer.HandleInput` is not unit-testable without an ImGui context. **Accepted.**

---

## Issues Found

None. No regressions introduced.

**Pre-existing test failures confirmed not caused by this batch:**
- `Fdp.Toolkits.Tests`: 26 failures in unrelated test classes
  (`NavigationIntentBridgeSystemTests`, `BicycleModelTests`, `MissionDirectorSystemTests`, etc.)
  — all existed before BATCH-29 (verified by stash/revert).
- `Hrot.Network.NED.Tests`: flaky DDS integration test (`CanPublishAndSubscribeEntityMaster`)
  that fails intermittently when re-run in rapid succession; 96/96 on dedicated run.

---

## Verdict

**APPROVED — proceed to commit and Phase 23.**

Tasks completed by this batch:
- [x] TASK-GZ064
- [x] TASK-GZ065
- [x] TASK-GZ066
- [x] TASK-GZ067
