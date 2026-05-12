# BATCH-29: BATCH-28 Fixes + Phase 22 Composite Gizmo Identity

**Batch Number:** BATCH-29  
**Tasks:** Corrective-0 (BATCH-28 fixes), TASK-GZ064, TASK-GZ065, TASK-GZ066, TASK-GZ067  
**Phase:** Phase 22 — Composite Gizmo Identity  
**Priority:** HIGH  
**Dependencies:** BATCH-28 committed (architecture in place)

---

## Onboarding & Workflow

### Developer Instructions

This batch has two parts:
1. **Corrective Task 0** — fix 3 failing tests from BATCH-28 before touching anything new.
2. **Phase 22** — introduce `GizmoTypeId` as the third routing-key component across network contracts, emitter infrastructure, routing system, and terminal pick-token population.

**Read every file listed before writing a single line of code.** Do not stop to ask permission to run tests or fix errors — run them, fix the root cause, run again. Only submit the report when all tests pass.

### Required Reading (IN ORDER)

1. **BATCH-28 Review:** `.dev/gizmos-1/reviews/BATCH-28-REVIEW.md` — understand what to fix
2. **Phase 22 task details:** `.dev/gizmos-1/TASK-DETAIL.md` lines 1620–1820 (TASK-GZ064 through TASK-GZ067)
3. **Design §11:** `.dev/gizmos-1/DESIGN.md` §11 (lines 805–980) — composite gizmo identity
4. **DebugPrimitive struct:** `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/Primitives/DebugPrimitive.cs`
5. **GizmoPickToken:** `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/Sources/GizmoPickToken.cs`
6. **GizmoInteractionBatch:** `FDP/ExtDeps/GizmoMap/GizmoMap.Network/Topics/GizmoInteractionBatch.cs`
7. **PickToken:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/PickToken.cs`
8. **IGizmoDefinition:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IGizmoDefinition.cs`
9. **DebugPrimitiveBuffer:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts/DebugPrimitiveBuffer.cs`
10. **DataDrivenGizmoSystem:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs`
11. **GlobalGizmoManager:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/GlobalGizmoManager.cs`
12. **GizmoInteractionEvents:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Events/GizmoInteractionEvents.cs`
13. **GizmoInteractionIngressTranslator:** `Hrot/Network/Hrot.Network.NED/Gizmos/GizmoInteractionIngressTranslator.cs`
14. **GizmoInteractionEgressTranslator:** `Hrot/Network/Hrot.Network.NED/Gizmos/GizmoInteractionEgressTranslator.cs`
15. **DdsGizmoInteractionPublisher:** `FDP/ExtDeps/GizmoMap/GizmoMap.Network/Transport/DdsGizmoInteractionPublisher.cs`
16. **DebugGizmoLayer:** `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs`
17. **GizmoMap.Viewer/Program.cs:** `FDP/ExtDeps/GizmoMap/GizmoMap.Viewer/Program.cs`
18. **All existing IGizmoDefinition implementors** (search for `IGizmoDefinition` in the solution)
19. **GizmosPrimitiveTests:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests/ContractsStandaloneTests.cs`
20. **GizmoInteractionTranslatorTests:** `Hrot/Network/Hrot.Network.NED.Tests/` (search for translator tests)

### Source Code Locations

- GizmoMap contracts: `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/`
- GizmoMap network: `FDP/ExtDeps/GizmoMap/GizmoMap.Network/`
- FDP diagnostics contracts: `FDP/Diagnostics/Fdp.Diagnostics.Contracts/`
- FDP toolkits gizmos: `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/`
- Hrot NED network: `Hrot/Network/Hrot.Network.NED/Gizmos/`
- Presentation tests: `Hrot/Engine/Hrot.Presentation.Tests/`
- GizmoMap tests: `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts.Tests/` (if exists)

### Build & Test Commands

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln -c Debug --nologo -v q
dotnet test Hrot/Engine/Hrot.Presentation.Tests/ --no-build -v q
dotnet test FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests/ --no-build -v q
dotnet test Hrot/Network/Hrot.Network.NED.Tests/ --no-build -v q
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/ --no-build -v q
```

### Test Baseline (do not regress)

- `Hrot.Presentation.Tests`: 71 total, currently 3 failing → must reach 71/71 after Corrective-0
- `Fdp.Diagnostics.Contracts.Tests`: 19 passed — must stay 19
- `Hrot.Network.NED.Tests`: all passing — must not regress
- `Fdp.Toolkits.Tests`: all passing — must not regress

### Mandatory Workflow

**CRITICAL: Complete tasks in sequence with passing tests at each step.**

1. **Corrective-0** fixes → build → all 71 Hrot.Presentation.Tests pass ✅
2. **TASK-GZ064** → build → tests ✅
3. **TASK-GZ065** → build → tests ✅
4. **TASK-GZ066** → build → tests ✅
5. **TASK-GZ067** → build → tests ✅
6. Full solution build → zero errors ✅

**DO NOT** move to the next task until all tests pass for the current task.

### Report Submission

Submit your report to: `.dev/gizmos-1/reports/BATCH-29-REPORT.md`

Questions: `.dev/gizmos-1/questions/BATCH-29-QUESTIONS.md`

---

## Corrective Task 0 — Fix 3 Failing BATCH-28 Tests

**Files to fix:**
- `Hrot/Engine/Hrot.Presentation.Tests/EntityDragGizmoTests.cs`
- `Hrot/Engine/Hrot.Presentation.Tests/SelectionInteractionSystemTests.cs`

### Fix 1: EDG-001 — `UpdateAndDraw_EmitsSphereWithValidPickToken`

`EntityDragGizmo.UpdateAndDraw` emits a `DebugPrimitiveShape.Box2D` for entity hit-testing (not a Sphere). The test incorrectly searches for `DebugPrimitiveShape.Sphere`.

**Fix:** Update the test to search for `Box2D` (and optionally verify that `Box2D` has the correct entity anchor set via `AnchorIndex`/`AnchorGeneration`). The implementation is correct; the test is wrong.

```csharp
// Find the Box2D primitive with entity anchor (the pick hitbox).
bool found = false;
foreach (var prim in frame)
{
    if (prim.Shape != DebugPrimitiveShape.Box2D) continue;
    var token = prim.GetPickToken();
    if (!token.IsValid) continue;
    Assert.Equal(_entity, token.Target);
    found = true;
    break;
}
Assert.True(found, "No Box2D with valid entity pick token found.");
```

### Fix 2: SIS-002 — `GizmoInteractionStartedEvent_WithNullEntity_ClearsSelection`

`SelectionInteractionSystem` changed behavior: null entity click starts rubber-band selection (does NOT immediately clear selection). The test expects an immediate clear. Update the test to match actual behavior: null entity event starts rubber-band; a tiny-drag commit clears selection.

**Fix:** Rewrite SIS-002 to test the correct behavior:

```csharp
// SIS-002: After null-entity GizmoInteractionStartedEvent, selection is NOT cleared immediately.
// A tiny-drag commit (GizmoInteractionCommitEvent without intervening GizmoDragUpdateEvent) clears all.
[Fact]
public void GizmoInteractionStartedEvent_WithNullEntity_StartsRubberBand_NotImmediateClear()
{
    var entity = CreateSelectableEntity();
    _world.SetComponent(entity, new SelectionState { IsSelected = true, IsPrimarySelection = true });

    // Step 1: null entity click -> rubber-band starts, selection NOT yet cleared
    PublishStartedEvent(Entity.Null);
    _system.Tick(0f);
    // Still selected (rubber-band in progress, no commit yet)
    Assert.True(_world.GetComponent<SelectionState>(entity).IsSelected);

    // Step 2: commit without any drag event -> tiny drag path -> clears selection
    _world.Bus.Publish(new GizmoInteractionCommitEvent { Token = new PickToken { Target = Entity.Null } });
    _world.Bus.SwapBuffers();
    _system.Tick(0f);

    var state = _world.GetComponent<SelectionState>(entity);
    Assert.False(state.IsSelected);
}
```

### Fix 3: SIS-008 — `OnSelectionChanged_FiresWithNull_OnEmptySpaceClick`

Same root cause: null entity click starts rubber-band, does not immediately call `OnSelectionChanged`. Update to test that `OnSelectionChanged` fires after a tiny-drag commit.

**Fix:** Rewrite SIS-008:

```csharp
// SIS-008: OnSelectionChanged fires with Entity.Null on tiny-drag commit (empty-space rubber-band commit).
[Fact]
public void OnSelectionChanged_FiresWithNull_AfterTinyDragCommit()
{
    Entity? callbackEntity = null;
    _system.OnSelectionChanged += (e, _) => callbackEntity = e;

    // Start rubber-band on empty space (null entity)
    PublishStartedEvent(Entity.Null);
    _system.Tick(0f);
    Assert.Null(callbackEntity); // not yet fired

    // Commit without drag = tiny drag = deselect all
    _world.Bus.Publish(new GizmoInteractionCommitEvent { Token = new PickToken { Target = Entity.Null } });
    _world.Bus.SwapBuffers();
    _system.Tick(0f);

    Assert.Equal(Entity.Null, callbackEntity);
}
```

**After all 3 fixes:** run `dotnet test Hrot/Engine/Hrot.Presentation.Tests/` and confirm 71/71.

---

## TASK-GZ064 — Add GizmoTypeId to Network Contracts

**Task Definition:** See [TASK-DETAIL.md §TASK-GZ064](../TASK-DETAIL.md) (line 1633)  
**Design Reference:** DESIGN.md §11.2, §11.3

**Files to modify:**

| File | Change |
|------|--------|
| `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/Primitives/DebugPrimitive.cs` | Add `[FieldOffset(60)] public uint GizmoTypeId;` |
| `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/Sources/GizmoPickToken.cs` | Add `public uint GizmoTypeId;` |
| `FDP/ExtDeps/GizmoMap/GizmoMap.Network/Topics/GizmoInteractionBatch.cs` | Add `public uint PickGizmoTypeId;` after `PickStreamId` |
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/PickToken.cs` | Add `public uint GizmoTypeId;` |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IGizmoDefinition.cs` | Add `uint GizmoTypeId { get; }` (no default) |

**Key constraints (from TASK-DETAIL.md):**
- `DebugPrimitive` MUST remain exactly 64 bytes. Verify with test.
- Offset 60 comment: must document that `SemanticShape.ResolvedRollRad` also uses offset 60 but shape-gated stamping prevents corruption.
- `GizmoMap.*` assemblies must NOT reference `IGizmoDefinition` — `GizmoTypeId` is a plain `uint`.
- `IGizmoDefinition.GizmoTypeId` must NOT have a default implementation — all implementors must provide it explicitly.

**After adding `GizmoTypeId` to `IGizmoDefinition`, the solution will not compile** until all implementors provide the property. Find all implementations:
```
Search in solution: "IGizmoDefinition"
```
Add `public uint GizmoTypeId => FnvHash.Of(GetType().FullName!);` to each (or a type-specific constant). Look at how `FnvHash.Of` is already used in the codebase for the pattern.

**Tests Required (add to `FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests/ContractsStandaloneTests.cs` or a new test class in the nearest test project):**

- SC-GZ064-1: `Assert.Equal(64, Marshal.SizeOf<DebugPrimitive>());`
- SC-GZ064-2: `GizmoPickToken` has `GizmoTypeId` field of `uint`; default-initialized == 0.
- SC-GZ064-3: `GizmoInteractionBatch` has `PickGizmoTypeId` field; round-trip serialise-deserialise preserves value. (Use mock serialization or struct copy — actual DDS round-trip is out of scope for unit tests.)
- SC-GZ064-4: `PickToken` has `GizmoTypeId` field of `uint`.
- SC-GZ064-5: Solution compiles — verified by build success with all `IGizmoDefinition` implementors updated.

---

## TASK-GZ065 — GizmoTypeId Injection into Emitted Primitives

**Task Definition:** See [TASK-DETAIL.md §TASK-GZ065](../TASK-DETAIL.md) (line 1678)  
**Design Reference:** DESIGN.md §11.4

**Files to modify:**

| File | Change |
|------|--------|
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts/DebugPrimitiveBuffer.cs` | Add `Count` property + `StampGizmoTypeId` method |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs` | Record mark + stamp after each `UpdateAndDraw` |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/GlobalGizmoManager.cs` | Record mark + stamp after each `UpdateAndDraw` |

**Key constraints (from TASK-DETAIL.md):**
- `StampGizmoTypeId` must only write to `Box2D`, `StructInspector`, and `ContextMenuBinding` shapes. Must NOT write to `SemanticShape`, `SpatialAnchor`, or any other shape.
- `StampGizmoTypeId` is on the **concrete** `DebugPrimitiveBuffer` class, NOT on `IDebugDrawBuilder`.
- The orchestrators downcast to `DebugPrimitiveBuffer` to call it (they own the concrete instance).
- Persistent primitives (`_persistent` array) are NOT stamped.

**Tests Required (add to nearest test project for each):**

- SC-GZ065-1: Buffer with Box2D at index 0 → `StampGizmoTypeId(0, 42u)` → `primitive.GizmoTypeId == 42`.
- SC-GZ065-2: Buffer with Box2D at 0 + SemanticShape at 1 → `StampGizmoTypeId(0, 42u)` → SemanticShape still has `GizmoTypeId == 0`.
- SC-GZ065-5: Buffer with ContextMenuBinding → `StampGizmoTypeId(0, 99u)` → `GizmoTypeId == 99`.
- SC-GZ065-3: Integration test (same test project as DataDrivenGizmoSystem tests, or create one) — two gizmo definitions registered on the same entity, each emitting one Box2D primitive; after `Execute`, each primitive has a different `GizmoTypeId` matching its respective definition.
- SC-GZ065-4: Solution builds, existing `DataDrivenGizmoSystem` and `GlobalGizmoManager` tests pass.

---

## TASK-GZ066 — Fix DataDrivenGizmoSystem Routing and Wire Egress/Ingress Translators

**Task Definition:** See [TASK-DETAIL.md §TASK-GZ066](../TASK-DETAIL.md) (line 1725)  
**Design Reference:** DESIGN.md §11.5, §11.6

**Files to modify:**

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs` | Replace `FindGizmo(Entity)` with `FindGizmo(Entity, uint)` + add StructUpdate + GizmoMenuAction routing |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Events/GizmoInteractionEvents.cs` | Add `uint GizmoTypeId` to `GizmoStructUpdateEvent` + `GizmoMenuActionEvent` |
| `Hrot/Network/Hrot.Network.NED/Gizmos/GizmoInteractionIngressTranslator.cs` | Populate `GizmoTypeId` from `batch.PickGizmoTypeId` in all cases |
| `Hrot/Network/Hrot.Network.NED/Gizmos/GizmoInteractionEgressTranslator.cs` | Set `PickGizmoTypeId` in `WriteRecord` and `WriteStructUpdate` |
| `FDP/ExtDeps/GizmoMap/GizmoMap.Network/Transport/DdsGizmoInteractionPublisher.cs` | Set `PickGizmoTypeId = token.GizmoTypeId` |
| `FDP/ExtDeps/GizmoMap/GizmoMap.Presentation/UI/ImGuiPropertyTreeAdapter.cs` | Change `Action<long, string>?` to `Action<long, uint, string>?`; add `GizmoTypeId` to `ScheduledItem` |

**Key constraints (from TASK-DETAIL.md):**
- `FindGizmo(entity, 0)` must gracefully fall back to `list.FirstOrDefault()?.Instance` for legacy callers.
- Ingress translator must not throw if `PickGizmoTypeId == 0`.
- `GizmoStructUpdateEvent.GizmoTypeId == 0` (legacy path) must still reach `GlobalGizmoManager` via its AnchorId routing.
- All existing `GizmoInteractionTranslatorTests` in `Hrot.Network.NED.Tests` must still pass.
- In `ImGuiPropertyTreeAdapter`: the callback passes `item.GizmoTypeId` (hash of gizmo class), NEVER `item.SchemaHash` (hash of DTO struct type). Conflating these causes `FindGizmo` to drop every StructUpdate on the host.

**Tests Required:**

- SC-GZ066-1: Two `MockGizmoDefinition`s with different `GizmoTypeId` on same entity. Two `GizmoInteractionStartedEvent`s with different `Token.GizmoTypeId`. Assert each reaches the correct gizmo's `OnInteractionStarted`; the other receives nothing.
- SC-GZ066-2: `GizmoStructUpdateEvent` with matching `GizmoTypeId` → `OnStructUpdate` called on correct gizmo.
- SC-GZ066-3: `GizmoInteractionEgressTranslator.WriteRecord` produces batch with `PickGizmoTypeId` equal to source `PickToken.GizmoTypeId`.
- SC-GZ066-4: All existing `GizmoInteractionTranslatorTests` pass without modification.
- SC-GZ066-5: Two `MockGizmoDefinition`s on same entity; `GizmoMenuActionEvent` with second gizmo's `GizmoTypeId` → only second gizmo's `OnMenuAction` called.

---

## TASK-GZ067 — Populate GizmoTypeId in Terminal Pick-Token

**Task Definition:** See [TASK-DETAIL.md §TASK-GZ067](../TASK-DETAIL.md) (line 1777)  
**Design Reference:** DESIGN.md §11.6 (pick-token and transport portions)

**Files to modify:**

| File | Change |
|------|--------|
| `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs` | In both pick-token construction blocks (left-click and right-click), add `GizmoTypeId = hit.GizmoTypeId` |
| `FDP/ExtDeps/GizmoMap/GizmoMap.Viewer/Program.cs` | In the `onInteraction` lambda, add `PickGizmoTypeId = token.GizmoTypeId` |

**Key constraints (from TASK-DETAIL.md):**
- `GizmoTypeId == 0` is a valid sentinel for legacy primitives; do not treat it as an error.
- Read `GizmoTypeId` unconditionally from `hit.GizmoTypeId` for both entity-local and BoxAnchorId-routed primitives.

**Tests Required:**

- SC-GZ067-1: Unit test in `DebugGizmoLayerGizmoTests` (or nearest test class): create a Box2D primitive with `GizmoTypeId = 77u`, drive `HandleInput` to simulate a left-click, assert that the `GizmoPickToken` forwarded to `onInteraction` has `GizmoTypeId == 77`.
- SC-GZ067-2: `GizmoMap.Viewer` compiles and runs without errors after the `PickGizmoTypeId` addition. (Build success is sufficient for this condition.)

---

## Quality Standards

**Tests must verify actual values and behavior, not just compilation:**

- Assertions must check concrete field values (e.g., `Assert.Equal(77u, token.GizmoTypeId)`), not just that the object is non-null.
- Integration tests for GZ065 and GZ066 must use real or mock gizmo instances that emit primitives and receive events — not string checks.
- Tests for `StampGizmoTypeId` must confirm both that the stamped shape gets the value AND that excluded shapes do not.

**NOT acceptable:**
- `Assert.NotNull(token)` alone (no field value check)
- `Assert.True(batch.PickGizmoTypeId >= 0)` (tautology for uint)
- Tests that only verify compilation (no behavioral assertion)

---

## Success Criteria

- [ ] Corrective-0: 71/71 `Hrot.Presentation.Tests` pass
- [ ] GZ064: `Marshal.SizeOf<DebugPrimitive>() == 64`; all contracts have `GizmoTypeId` fields; solution builds
- [ ] GZ065: `StampGizmoTypeId` stamps Box2D/StructInspector/ContextMenuBinding only; DataDrivenGizmoSystem and GlobalGizmoManager stamp after each `UpdateAndDraw`
- [ ] GZ066: Routing correctly discriminates by `GizmoTypeId`; translators carry the field; `ImGuiPropertyTreeAdapter` callback has updated signature
- [ ] GZ067: Pick tokens populated with `GizmoTypeId` from hit primitive
- [ ] Zero build errors in full solution
- [ ] All existing tests pass (no regressions)

---

## Developer Insights Questions

**Q1:** Did you find any `IGizmoDefinition` implementors that were tricky to update (e.g., anonymous or generated)? How did you handle them?

**Q2:** Were there any offset conflicts or struct layout issues discovered when adding `GizmoTypeId` at offset 60 in `DebugPrimitive`?

**Q3:** What edge cases did you encounter when updating `FindGizmo` to use the composite key?

**Q4:** Did you notice any additional places where `GizmoTypeId` should propagate that were not in the spec?

**Q5:** Are there any performance concerns with the `StampGizmoTypeId` approach (e.g., for large primitive counts)?
