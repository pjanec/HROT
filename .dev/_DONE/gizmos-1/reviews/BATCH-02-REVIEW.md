# BATCH-02 Review

**Batch:** BATCH-02
**Reviewer:** Dev Lead
**Decision:** APPROVED (with debt item noted)

---

## Summary

All three Phase 2 tasks are complete. Build of `Fdp.Toolkits.csproj` is clean. All 74 gizmo tests pass. The review focuses on test quality.

---

## Test Quality Review

### Coverage breadth

All TASK-DETAIL success conditions are exercised:

- **SC-GZ004-x**: Mask has exactly the two required component bits (verified via `IsSet(idA)` / `IsSet(idB)`); unregistered component throws `InvalidOperationException`; AlwaysVisiblePolicy returns true from both methods; NeverVisiblePolicy returns false from both; multiple registrations accumulate with correct `RuleIndex` values.
- **SC-GZ005-x**: ConstructionOrder → `OnInitialize` called exactly once with correct entity; DestructionOrder → `OnTeardown` called, entity removed from map; selection predicate correctly gates `UpdateAndDraw`; null predicate draws all unconditionally; `NeverVisiblePolicy` suppresses even when predicate passes; entity with non-matching component mask gets no gizmo; `IsAlive` check skips dead entities; global visibility cache evaluated exactly once per frame (not once per entity) — verified by resetting the mock call counter between frames.
- **SC-GZ006-x**: `AssignBehaviorEvent` activates gizmo and calls `Rent()`; `ClearBehaviorEvent` calls `OnTeardown` + `Return()`; `DestructionOrder` also tears down behavior gizmo; second `AssignBehaviorEvent` for same entity tears down old gizmo (1 Return) then rents new one (2 Rents); unknown behavior name silently ignored; `Rent`/`Return` counts verified via `MockBehaviorFactory`.

### Test depth — specific observations

**SC-GZ005-8 (global visibility cache)**: The test creates three entities with the required component, runs the construction frame, then resets `mockPolicy.IsGloballyEnabledCallCount = 0` and runs one more frame. It asserts `IsGloballyEnabledCallCount == 1` (not 3). This is the correct way to verify the caching invariant — directly measuring the call count, not inferring it from draw counts.

**SC-GZ005-7 (dead entity generational safety)**: The test calls `repo.DestroyEntity(entity)` without publishing a `DestructionOrder` (simulates an external kill), then runs the system again and asserts that `UpdateAndDrawCount` does not increase. This correctly tests the `view.IsAlive(entity)` path.

**SC-GZ005-3 (selection predicate)**: The test uses a predicate that checks for `GizmoSelectedTag` component presence (a zero-size tag, no bool layout issues). Frame 1 without the tag: `UpdateAndDrawCount == 0`. Frame 2 with the tag added: `UpdateAndDrawCount == 1`. Clean boundary test.

**SC-GZ006-4 (replace gizmo on re-assign)**: Asserts `RentCount == 2`, `ReturnCount == 1` after the second assign. This precisely verifies that the teardown path ran before the new gizmo was activated.

**Mock implementations**: `MockVisibilityPolicy` tracks both `IsGloballyEnabled` and `IsEntityVisible` call counts. `MockBehaviorFactory` tracks `Rent()` / `Return()` counts. `MockGizmoDefinition` collects all created `MockGizmo` instances so individual gizmo call counts can be asserted.

### Minor issues (non-blocking)

1. **SC-GZ005-5 (NeverVisiblePolicy)**: The test comment says "Execute a second frame; NeverVisiblePolicy.IsGloballyEnabled returns false." The system draws gizmos in the construction frame too (before the global-visibility check). This is why `gizmo.UpdateAndDrawCount` is checked after the second frame, not after the first. The comment could be clearer, but the assertion is correct.

2. **`GizmoTestRepo.Create()` is a disposable `EntityRepository`** but tests don't call `Dispose()`. This is acceptable in an xUnit test context (process lifetime) but could be tracked as a pattern to improve.

---

## Production Code Quality

- `DataDrivenGizmoSystem` and `BehaviorGizmoManagerSystem` have comprehensive XML doc comments explaining the design deviation (SelectionState not reachable from Fdp.Toolkits), the deferred GlobalDebugSettings integration, and the per-frame global-visibility cache semantics.
- `GizmoRegistry` comment correctly states that `Register` is not thread-safe and must only be called during startup.
- `BehaviorGizmoRegistry.TryGetFactory` follows the standard TryGet pattern with `out` parameter.
- `BehaviorGizmoManagerSystem` correctly processes `AssignBehaviorEvent` via `ReadManagedEvents` (managed class event) and `ClearBehaviorEvent` via `ReadEvents` (unmanaged struct event).

## Design Deviations Accepted

1. **Selection predicate pattern** (instead of `SelectionState` ECS query): correct architectural decision; avoids a layering violation. The game-host layer can supply the predicate.
2. **GlobalDebugSettings deferred to GZ015**: correct; stub-free approach is cleaner.
3. **`GizmoRegistry.Rules` is `internal`**: required by C# accessibility rules; correct solution.
4. **`GizmoSelectedTag` is a presence tag**: correct workaround for the ECS layout validator rejecting `bool` fields without `[MarshalAs(UnmanagedType.I1)]`.

## Debt Logged

Added to DEBT-TRACKER.md:
- D-002: `EntityRepository` in tests not disposed — minor test hygiene.
- D-003: Selection predicate hookup not yet wired in the game host layer — must be done when `DataDrivenGizmoSystem` / `BehaviorGizmoManagerSystem` are registered in the simulation kernel.
