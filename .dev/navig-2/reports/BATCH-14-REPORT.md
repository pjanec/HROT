# BATCH-14 Report — Navigation Integration Test Suite (Phase 1)

## Status: COMPLETE

All tasks delivered. Build succeeded with 0 warnings / 0 errors.
Both integration tests pass.

---

## Files Created

| File | Lines | Purpose |
|------|-------|---------|
| `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavTestHarness.cs` | 215 | All-in-one harness + `CapturedEventLog` |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S1_SimpleCorridorTests.cs` | 48 | Corridor arrival integration test |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S7_FailedUnreachableTests.cs` | 46 | FailedUnreachable integration test |

---

## Test Results

```
Passed  S1_SimpleCorridorTests.Corridor_InfantryMovesToFarEnd_Arrives        [88 ms]
Passed  S7_FailedUnreachableTests.Stuck_InfantryMovesToDisconnectedDest_ReturnsUnreachable  [2 ms]

Total tests: 2  |  Passed: 2  |  Failed: 0
```

---

## Design Decisions

### Tick pipeline order
The harness drives the following sequence per tick to match production system ordering:

1. `Bridge.Execute` — publishes `PathfindingRequestEvent` to write buffer
2. `Bus.SwapBuffers()` — requests become readable
3. `Solver.Execute` — reads requests, publishes `PathfindingResultEvent` via ECB
4. `FlushCommandBuffers()` — ECB results move to write buffer
5. `Bus.SwapBuffers()` — `PathfindingResultEvent` becomes readable
6. `Materialize.Execute` — reads results, updates corridor/status, publishes `MoveStartedEvent`
7. `CrowdUpdate.Execute` — integrates agent positions
8. `NavExec.Execute` — checks arrival, publishes `MoveCompletedEvent` on Arrived
9. `Bus.SwapBuffers()` — `MoveStartedEvent` / `MoveCompletedEvent` become readable
10. `EventLog.Capture` — accumulates events from readable buffer
11. Frame counter increment

### `PathfindingResultEvent` not registered by factory
`NavigationTestWorldFactory.Create()` does not call `RegisterEvent<PathfindingResultEvent>()`.
The harness adds this registration explicitly immediately after `Create()`, before any systems run.

### `PathfindingBatchData` missing singleton
The factory does not create the `PathfindingBatchData` singleton required by
`PathfindingResultMaterializationSystem` (which has a guard `if (!repo.HasSingleton<PathfindingBatchData>()) return`).
The harness allocates a `NativeArray<PathResult>` of `DefaultCapacity = 256` with
`Allocator.Persistent` and calls `world.SetSingleton(batch)`.  The array is disposed in
`NavTestHarness.Dispose()`.

### S7 — FailedUnreachable / NavExec interaction
`PathfindingResultMaterializationSystem` sets `status.Result = FailedUnreachable` on tick 1
when the path is unreachable.  Without a fix, `NavigationExecutionSystem` would detect
`status.IntentId != intent.IntentId` (because status was default-initialised to `IntentId = 0`)
and overwrite the result with `InProgress`.

Fix in `IssueMoveTo`: pre-set `status.IntentId = instanceId` and `status.Result = InProgress`
before the first tick.  NavExec now sees matching IntentIds, skips the new-command-detection
block, and then skips the arrival logic because `status.Result != InProgress` (FailedUnreachable
is a terminal state).

### S7 — No MoveCompletedEvent
Neither `PathfindingResultMaterializationSystem` nor `NavigationExecutionSystem` publishes
`MoveCompletedEvent` for the FailedUnreachable case.  The S7 test therefore reads
`NavigationStatus.Result` directly after `PumpFor(5)` rather than waiting for an event.

### S1 arrival geometry
- Corridor: three 10x10 polygons at XZ centres (5,5), (15,5), (25,5); adjacent 0-1-2.
- Entity starts at `(0,0)` in XY (maps to SimTransform `(0, 0, 0)`).
- Destination `(28, 0)` maps to FakeDtCrowd target `(28, 0, 0)`.
- Both start and destination are confirmed inside the navmesh (winding-number test: +1).
- FakeDtCrowd: MaxSpeed = 5 m/s, MaxAcceleration = 20 m/s^2, dt = 1/60 s.
  Max delta-velocity per tick = 20/60 ≈ 0.333 m/s.  Reaches full speed in ~15 ticks.
  Total distance ~28 m at average ~4.5 m/s takes ~375 ticks; `PumpUntil(maxTicks=600)` is safe.

### S7 destination geometry
- Stuck map: single polygon Square(5,5), spans XZ (0,0)-(10,10).
- Entity at `(1,1)` XY is inside the polygon (confirmed by winding number = +1).
- Destination `(50,50)` XY maps to FakeDtCrowd / solver end `(50,0,0)`.
  XZ point (50,0) is outside the polygon — solver returns 0 polygons → unreachable.
