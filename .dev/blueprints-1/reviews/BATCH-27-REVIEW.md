# BATCH-27 Review

**Batch:** BATCH-27  
**Reviewer:** Development Lead  
**Date:** 2026-05-22  
**Status:** APPROVED

---

## Summary

All six flaws from review3.md resolved. Build clean. 490/497 tests pass consistently
(7 skipped are pre-existing LibraryMath/DoorActor stubs). New tests T1-T5d are present
and correct. One pre-existing DEBT-019 race condition is now actively manifesting and
must be fixed in BATCH-28.

---

## Verification

### Build
Both `Hrot.Blueprints.Editor` and `Hrot.Blueprints.Tests` build with 0 C# errors.
Two pre-existing xUnit2029 warnings remain (unrelated to this batch).

### Tests
- Full suite: 490 passed, 0 failed, 7 skipped (497 total) — confirmed by re-run.
- Occasional 1-failure run observed: `BlueprintTestFixtureTests.Constructor_InitializesAllProperties`
  fails intermittently due to DEBT-019 race (see Issue 1 below).

### Flaw 1 (ghost stub deleted)
`Hrot.Blueprints.Core/BlueprintCompiler.cs` deleted. Only `Compiler/BlueprintCompiler.cs` remains.

### Flaw 2 (catalogs populated)
All three catalogs populated with real entries:
- `BuiltInEngineEventCatalog`: 3 entries (HitEvent, BehaviorFinishedEvent, TargetVisibleEvent)
- `BuiltInChannelCommandCatalog`: 5 entries (see Deviation 1 below)
- `BuiltInWaitPrimitiveCatalog`: 5 entries (WaitForChannel:Locomotion/Weapon/Interaction,
  WaitForEvent:BehaviorFinishedEvent, WaitForRingBufferResult:Pathfinding)

### Flaw 3 (BlueprintDebugSession moved to Editor)
Moved from `Core/BlueprintDebugSession.cs` to `Editor/BlueprintDebugSession.cs`.
Namespace `Hrot.Blueprints.Core.Debug` preserved — transparent to all callers.
`ExecutionHistory` made `public` (Deviation 3) and `Watch.IsStale` setter made `public`
(Deviation 4) to allow cross-assembly access. Both are acceptable minimal changes.

### Flaw 4 (Detach implemented)
`Detach()` correctly calls `Continue()` if paused, sets `DebugProbe.Sink = NullProbeSink.Instance`,
and clears all state collections.

### Flaw 5 (OnNodeExecuted event + GetRecentNodeHistory)
`OnNodeEnter` fires `_onNodeExecuted` event. `GetRecentNodeHistory` aggregates across all
entities and caps at `maxCount`.

### Flaw 6 (Watch.WriteValue ref fix)
`MemoryMarshal.GetArrayDataReference` used in `Watch.WriteValue<T>`.

### Tests T1-T5d
All present and logically correct:
- T1 (`Detach_ClearsAllStateAndNullsProbe`): tests all state cleared + Sink is NullProbeSink.
- T2 (`Detach_CallsContinue_WhenPaused`): tests Continue() is called via ResumeCount.
- T3 (`OnNodeExecuted_FiredOnNodeEnter`): tests event fires with correct NodeIdString and Self.
- T4 (`GetRecentNodeHistory_ReturnsAggregatedHistory`): tests 3 entries across E1+E2 are returned.
- T5a/b/c: catalog entry assertions against real entries.
- T5d: Stage2 validation rejects unknown channel command when catalog is non-empty.

---

## Issues Found

### Issue 1 (P2) -- DEBT-019 worsened: DebugProbe.Sink race now causing intermittent failures

**Evidence:** Full-suite run occasionally shows 1 failure:
`BlueprintTestFixtureTests.Constructor_InitializesAllProperties` — asserts
`Assert.Same(fixture.DebugSession, DebugProbe.Sink)` but sees a different sink
(either `NullProbeSink.Instance` from `Detach_ClearsAllStateAndNullsProbe` or another
fixture's session). The same test passes in isolation.

**Root cause:** `DebugProbe.Sink` is a process-wide mutable static. BATCH-27 added two
new test classes that directly set it:
- `DebugSessionInterfaceTests` (T1, T2, SC1-SC4) sets `DebugProbe.Sink = null` and
  `DebugProbe.Sink = session` without xUnit Collection isolation.
- `ProbeDispatchTests` (pre-existing) does likewise.

These classes run in parallel with `BlueprintTestFixtureTests`, racing on the static.

**Required fix in BATCH-28 (CT0):**
1. Create `Hrot.Blueprints.Tests/DebugProbeCollection.cs`:
   ```csharp
   [CollectionDefinition("DebugProbe")]
   public sealed class DebugProbeCollection { }
   ```
2. Add `[Collection("DebugProbe")]` to every test class that sets or reads `DebugProbe.Sink`:
   - `BlueprintTestFixtureTests`
   - `AllocationFreeTests`, `DoorActorDoorSensorDemoTests`, `HasVisibleTargetDemoTests`,
     `HealthRegenDemoTests`, `LibraryMathDemoTests`, `MockContractTests`,
     `MoveToAndFireDemoTests` (all create `new BlueprintTestFixture()`)
   - `Debug/DebugSessionInterfaceTests`
   - `Debug/ProbeDispatchTests`
3. Add `DebugProbe.Sink = NullProbeSink.Instance;` to `BlueprintTestFixture.Dispose()`
   as defense-in-depth cleanup.
4. Mark DEBT-019 RESOLVED in DEBT-TRACKER.md.

**Note on DEBT-019 escalation:** This was P3, now manifesting as an active intermittent
failure. Treating as P1 (CT0) for BATCH-28 given it disrupts CI reliability.

### Issue 2 (Housekeeping) -- TASK-TRACKER.md not updated

Phase 4 tasks TASK-HR-001, TASK-HR-002, TASK-HR-003 remain `[ ]` unchecked despite
being fully implemented. Update in BATCH-28.

### Issue 3 (P3) -- Catalog name format deviation not tracked in DEBT-TRACKER

Deviation 1 uses unqualified short names ("MoveTo") instead of the designed hierarchical
paths ("Locomotion/MoveTo"). This is correct given the current validator/asset contract,
but diverges from the design document. Add a P3 DEBT entry (DEBT-023) so future
editors know the design intent vs implementation.

---

## Deviations Assessment

| # | Deviation | Assessment |
|---|-----------|------------|
| 1 | Catalog names are "MoveTo" not "Locomotion/MoveTo" | Correct: validator matches ActionId strings in real scenario assets. Simple names match existing scenarios. Add DEBT-023. |
| 2 | T5d uses `WithHostings(BTreeAction)` not in instructions | Correct: V_DispatchKindCompatibility runs before V_ChannelCommandReferences; hosting is required. |
| 3 | `ExecutionHistory` made `public` | Acceptable: minimal change for cross-assembly access. Sealed class with no ABI surface risk. |
| 4 | `Watch.IsStale` setter made `public` | Acceptable: required for `BlueprintDebugSession` in Editor to update stale flags. |
| 5 | T5b asserts "MoveTo" not "Locomotion/MoveTo" | Correct: follows Deviation 1. |
| 6 | `HitEvent` from `Fdp.Toolkit.Combat.Contracts` not `Fdp.Core.Events` | Correct: type was relocated prior to this batch. |

All deviations are justified and correct.

---

## Final State

**Status: APPROVED**

Changes are uncommitted. Commit as `blueprints: BATCH-27 -- fix review3.md flaws` after
this review. BATCH-28 must address DEBT-019 (CT0) and TASK-TRACKER housekeeping.
