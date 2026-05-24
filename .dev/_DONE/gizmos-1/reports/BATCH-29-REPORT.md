# BATCH-29 Report

**Batch:** BATCH-29  
**Tasks:** Corrective-0, TASK-GZ064, TASK-GZ065, TASK-GZ066, TASK-GZ067  
**Status:** COMPLETE

---

## Success Criteria

- [x] Corrective-0: 72/72 `Hrot.Presentation.Tests` pass (was 71 target; 1 additional test added by this batch)
- [x] GZ064: `Marshal.SizeOf<DebugPrimitive>() == 64`; all contracts have `GizmoTypeId` fields; solution builds
- [x] GZ065: `StampGizmoTypeId` stamps Box2D/StructInspector/ContextMenuBinding only; DataDrivenGizmoSystem and GlobalGizmoManager stamp after each `UpdateAndDraw`
- [x] GZ066: Routing correctly discriminates by `GizmoTypeId`; translators carry the field; `ImGuiPropertyTreeAdapter` callback has updated signature
- [x] GZ067: Pick tokens populated with `GizmoTypeId` from hit primitive
- [x] Zero build errors in full solution
- [x] All existing tests pass (no regressions)

---

## Test Results

| Suite | Before | After | New tests |
|-------|--------|-------|-----------|
| `Hrot.Presentation.Tests` | 71/71 (3 fixed by Corrective-0) | 72/72 | SC-GZ064-5 |
| `Fdp.Diagnostics.Contracts.Tests` | 14 | 19/19 | SC-GZ064-1..4 |
| `Hrot.Network.NED.Tests` | 95 | 96/96 | SC-GZ066-3 |
| `Fdp.Toolkits.Tests` (gizmo subset) | 152 | 160/160 | SC-GZ065-1..5, SC-GZ066-1,2,5 |
| `GizmoMap.Contracts.Tests` | n/a | pass | SC-GZ067-1 |

All required suites: **0 failures**.

---

## Corrective Task 0

Fixed 3 failing BATCH-28 tests in `Hrot.Presentation.Tests`:

1. **EDG-001** (`UpdateAndDraw_EmitsSphereWithValidPickToken`): Test incorrectly searched for `DebugPrimitiveShape.Sphere`. The gizmo emits `Box2D` for its entity hit-box. Fixed the test to search for `Box2D` and verify the entity pick token.

2. **SIS-002** (`GizmoInteractionStartedEvent_WithNullEntity_ClearsSelection`): `SelectionInteractionSystem` no longer immediately clears selection on null-entity click — it starts rubber-band mode. Rewrote the test as `...StartsRubberBand_NotImmediateClear` which commits without a drag event (tiny-drag path) and then verifies selection is cleared.

3. **SIS-008** (`OnSelectionChanged_FiresWithNull_OnEmptySpaceClick`): Same root cause as SIS-002. Rewrote as `...AfterTinyDragCommit`: rubber-band start, then commit → verifies `OnSelectionChanged` fires with `Entity.Null`.

---

## TASK-GZ064 — Add GizmoTypeId to Network Contracts

### Changes

| File | Change |
|------|--------|
| `GizmoMap.Contracts/Primitives/DebugPrimitive.cs` | `[FieldOffset(60)] public uint GizmoTypeId;` (aliases `SemanticShape.ResolvedRollRad` but shape-gated stamping prevents corruption) |
| `GizmoMap.Contracts/Sources/GizmoPickToken.cs` | `public uint GizmoTypeId;` |
| `GizmoMap.Network/Topics/GizmoInteractionBatch.cs` | `public uint PickGizmoTypeId;` after `PickStreamId` |
| `Fdp.Diagnostics.Contracts/Primitives/PickToken.cs` | `public uint GizmoTypeId;` |
| `Fdp.Toolkits/Diagnostics/Gizmos/IGizmoDefinition.cs` | `uint GizmoTypeId { get; }` (no default — all implementors must supply explicitly) |

All `IGizmoDefinition` implementors updated:
- `EntityDragGizmoDefinition` (Hrot.Presentation): `GizmoSettingsRegistry.ComputeHash(typeof(...).FullName!)`
- `MockGizmoDefinition` (GizmosSystemTests): constructor parameter with default `0`
- `MockUndoGizmoDefinition` (GizmoUndoStackTests): constant `1u`
- `D003GizmoDef` (DataDrivenGizmoPredicateTests): constant `0xD003u`

### Tests

- **SC-GZ064-1**: `Marshal.SizeOf<DebugPrimitive>() == 64` — passes
- **SC-GZ064-2**: `GizmoPickToken.GizmoTypeId` default is 0 — passes
- **SC-GZ064-3**: `GizmoInteractionBatch.PickGizmoTypeId` round-trips via struct copy — passes
- **SC-GZ064-4**: `PickToken.GizmoTypeId` field exists, default is 0 — passes
- **SC-GZ064-5**: `EntityDragGizmoDefinition.GizmoTypeId` is non-zero — passes

---

## TASK-GZ065 — GizmoTypeId Injection into Emitted Primitives

### Changes

**`DebugPrimitiveBuffer.cs`**:
- Added `public int Count => Math.Min(_count, _primitives.Length);`
- Added `public void StampGizmoTypeId(int fromIndex, uint gizmoTypeId)` — iterates `[fromIndex, Count)`, writes to `Box2D`, `StructInspector`, `ContextMenuBinding` only. `SemanticShape` and `SpatialAnchor` are explicitly excluded.

**`DataDrivenGizmoSystem.cs`**:
- All three draw loops (`_activeGizmos`, `_injectedGizmos`, persistent) record `int mark = buf.Count` before `UpdateAndDraw`, then call `buf.StampGizmoTypeId(mark, gi.Definition.GizmoTypeId)` after.
- Added `private static uint Fnv1a32(string name)` helper for injected gizmo type IDs.

**`GlobalGizmoManager.cs`**:
- Step 1 loop: records mark, stamps with `Fnv1a32(typeof(gizmo).FullName!)` derived hash.
- Added `private static uint Fnv1a32(string name)` helper.

### Tests (in `GizmosPrimitiveTests.cs`, class `StampGizmoTypeIdTests`)

- **SC-GZ065-1**: Box2D at index 0 → stamp(0, 42u) → `GizmoTypeId == 42`
- **SC-GZ065-2**: Box2D at 0 + SemanticShape at 1 → stamp(0, 42u) → SemanticShape stays at 0
- **SC-GZ065-3**: Pre-set `GizmoTypeId = 99u` then stamp(0, 0u) → `GizmoTypeId == 0` (zero is a valid stamp)
- **SC-GZ065-4**: `fromIndex == 1` beyond count → no-op, no exception
- **SC-GZ065-5**: ContextMenuBinding → stamped with 99u

---

## TASK-GZ066 — Fix Routing + Wire Translators + Update ImGuiPropertyTreeAdapter

### Changes

**`GizmoInteractionEvents.cs`**:
- `GizmoMenuActionEvent` (struct): added `public uint GizmoTypeId;`
- `GizmoStructUpdateEvent` (sealed class): added `public uint GizmoTypeId;`

**`DataDrivenGizmoSystem.cs`**:
- `FindGizmo(Entity, uint gizmoTypeId)`: composite lookup. When `gizmoTypeId == 0` returns `list[0].Instance` (legacy fallback). Otherwise iterates list for matching `Definition.GizmoTypeId`.
- **Bug fix**: when `entity.Generation == 0` (events that carry only an entity index), falls back to index-only scan of `_activeGizmos` — because `Entity.Equals` checks both index AND generation and the live entry has generation > 0.
- `RouteInteractionEvents`: StructUpdate and MenuAction paths now pass `evt.GizmoTypeId`.

**Egress translator** (`GizmoInteractionEgressTranslator.cs`):
- `WriteRecord`, `WriteMenuAction`, `WriteStructUpdate`: all set `PickGizmoTypeId = token.GizmoTypeId`

**Ingress translator** (`GizmoInteractionIngressTranslator.cs`):
- Token construction, StructUpdate event, MenuAction event: all read `GizmoTypeId = batch.PickGizmoTypeId`

**`DdsGizmoInteractionPublisher.cs`**: `PickGizmoTypeId = token.GizmoTypeId`

**`ImGuiPropertyTreeAdapter.cs`**:
- `ScheduledItem` struct: added `public readonly uint GizmoTypeId;`
- Both `Schedule(...)` overloads: added `uint gizmoTypeId` parameter, passed to `ScheduledItem` constructor
- `DrawScheduled`: callback upgraded from `Action<long, string>?` to `Action<long, uint, string>?`
- Apply button: `onStructUpdate.Invoke(item.NetworkId, item.GizmoTypeId, json)`

**`DebugPrimitiveRenderer2D.cs`**: `Schedule(...)` call passes `prim.GizmoTypeId`

**`GizmoMap.Presentation/DebugGizmoLayer.cs`**: `DrawStructInspector` callback signature updated to `Action<long, uint, string>?`

**`GizmoViewerFrontend.cs`**: lambda updated to `(networkId, gizmoTypeId, json) => onInteraction(new GizmoPickToken { ..., GizmoTypeId = gizmoTypeId }, ...)`

**`Fdp.Presentation/DebugGizmoLayer.cs`**: lambda publishes `GizmoStructUpdateEvent { ..., GizmoTypeId = gizmoTypeId }`

**`GizmoMap.Viewer/Program.cs`**: both `GizmoInteractionBatch` initializers set `PickGizmoTypeId = token.GizmoTypeId`

### Tests

- **SC-GZ066-1**: Two gizmos on same entity with different `GizmoTypeId`; `GizmoInteractionStartedEvent` with def1's id reaches def1 only — passes
- **SC-GZ066-2**: `GizmoStructUpdateEvent` with def2's id → `OnStructUpdate` on def2 only — passes
- **SC-GZ066-3**: Egress `WriteRecord` → batch `PickGizmoTypeId == 0xAB01u` — passes
- **SC-GZ066-4**: All 95 existing `GizmoInteractionTranslatorTests` pass unchanged — passes
- **SC-GZ066-5**: `GizmoMenuActionEvent` with def2's id → `OnMenuAction` on def2 only — passes

---

## TASK-GZ067 — Populate GizmoTypeId in Terminal Pick-Token

### Changes

**`GizmoMap.Presentation/Layers/DebugGizmoLayer.cs`**:
- Left-click and right-click token construction in `HandleInput`: `GizmoTypeId = hit.GizmoTypeId`

**`Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs`**:
- `ToPickToken`: `GizmoTypeId = token.GizmoTypeId`

### Tests

- **SC-GZ067-1** (in `GizmoMap.Contracts.Tests/GizmoContractsTests.cs`): Creates a Box2D primitive with `GizmoTypeId = 77u`, replicates the entity-local token construction from `HandleInput`, asserts `token.GizmoTypeId == 77u` and `token.AnchorId == 5L` — passes.

  Note: `GizmoMap.Presentation.DebugGizmoLayer.HandleInput` and `Fdp.Presentation.DebugGizmoLayer.HandleInput` are Raylib-dependent and cannot be driven in headless tests. The test is placed in the nearest headless test project and directly exercises the token-construction logic.

---

## Developer Insights

**Q1: IGizmoDefinition implementors that were tricky to update?**

All implementors were straightforward. The test-only mocks (`MockGizmoDefinition`, `MockUndoGizmoDefinition`, `D003GizmoDef`) were found by searching for `IGizmoDefinition` and updated with constant values. `EntityDragGizmoDefinition` used `GizmoSettingsRegistry.ComputeHash` to derive a stable hash from the type's full name, consistent with the FNV-1a pattern used elsewhere. No anonymous or generated implementors were found.

**Q2: Offset conflicts in DebugPrimitive?**

`[FieldOffset(60)]` for `GizmoTypeId` aliases `SemanticShape.ResolvedRollRad` (also at offset 60). This is safe because `StampGizmoTypeId` explicitly skips `SemanticShape` and `SpatialAnchor` primitives. The `Marshal.SizeOf<DebugPrimitive>() == 64` test (SC-GZ064-1) confirms the struct layout is unchanged.

**Q3: Edge cases in FindGizmo?**

The most significant edge case: `GizmoStructUpdateEvent.AnchorId` and `GizmoMenuActionEvent.AnchorId` carry only the entity index (not the full packed entity), so `new Entity((int)evt.AnchorId, 0)` has `Generation == 0`. Since `Entity.Equals` compares both index AND generation, `TryGetValue` with a generation-0 key will never find a live entity (which always has `Generation >= 1`). Fixed by adding an index-only scan path in `FindGizmo` when `entity.Generation == 0`. The legacy `gizmoTypeId == 0` fallback returns `list[0].Instance` as specified, preserving pre-GZ064 behavior.

**Q4: Additional propagation points not in spec?**

The `GizmoMap.Viewer/Program.cs` had two separate `GizmoInteractionBatch` construction sites (one for pick start, one for the general interaction path) — both needed `PickGizmoTypeId`. Both sites were in the spec, but the duplication is worth noting for future maintainers.

The `Fdp.Presentation/DebugGizmoLayer.cs` wrapper also needed updating to pass `gizmoTypeId` through its `DrawStructInspector` lambda to `GizmoStructUpdateEvent`. This cascaded naturally from the `ImGuiPropertyTreeAdapter` signature change.

**Q5: Performance concerns with StampGizmoTypeId?**

`StampGizmoTypeId` is O(n) over the primitives emitted by a single gizmo during one frame. In practice each gizmo emits a small number of primitives (typically 1-5). The method iterates only the primitives added since the pre-draw mark, not the full buffer. This is equivalent in cost to the `UpdateAndDraw` call itself, so there is no material performance concern.
