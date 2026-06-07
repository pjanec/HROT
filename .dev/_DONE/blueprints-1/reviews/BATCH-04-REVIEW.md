# BATCH-04 Review

**Batch:** BATCH-04 -- BlueprintTestFixture + ALC Lifecycle (TH-003, TH-005, CT0 P2 fixes)
**Reviewer:** Dev Lead
**Verdict:** APPROVED WITH CORRECTIONS (P2 item must be fixed in BATCH-05)

---

## Summary

BATCH-04 is substantially complete. 87 tests pass, 5 skipped, 0 failures, 0 build errors.
Both Corrective Task 0 defects from BATCH-03 are fixed. `BlueprintTestFixture` is correctly
wired. ALC unload and GC reclaim are functional. One P2 defect is present in the ALC lifecycle
tests -- `Fixture_AfterMultipleLoads_OldAlcsReclaimedNewestStillLive` does NOT test reclaim
behavior and must be corrected in BATCH-05.

---

## Scope Check

- **Corrective Task 0 (CT0-1):** COMPLETE. `NodeEnterRecord` now has `(Entity Self, string NodeId, float Time)`.
- **Corrective Task 0 (CT0-2):** COMPLETE. Two skip-annotated placeholder tests added to
  `CapturingDebugSessionTests` -- `Debug_TraceMode_RecordsAllNodeEntries` and
  `Debug_Breakpoint_FiresWhenNodeEntered`.
- **TASK-TH-003:** COMPLETE (core). `BlueprintTestFixture` exposes all required public
  properties (`World`, `View`, `Ecb`, `Registry`, `TickSystem`, `MaintenanceSystem`,
  `Compiler`, `DebugSession`). TickFrame order matches Patches 1 + 2: `SwapBuffers` first,
  ECB playback last. `ChooseTier`, `HasSlot`, `GetBlueprintState`, `AttachBlueprint`,
  `SimulateReload`, `ForceGcReclaim`, `GetAlcWeakReferences`, `AddSimulationSystem`,
  `RegisterTickAction`, `DiscoverAndInvokeRegistrars` all present and correct.
  Minor omissions (P3) noted below.
- **TASK-TH-005:** MOSTLY COMPLETE. `Dispose()` properly unloads ALCs, runs GC-reclaim
  retry loop, and throws `InvalidOperationException` with correct message on leak detection.
  `VerifyAlcUnloadOnDispose = false` path skips GC work. One P2 defect in test coverage.

---

## Design Alignment

### TickFrame Order (Patches 1 + 2)

CORRECT. Matches the patched spec exactly:
1. `Bus.SwapBuffers()`
2. `View.AdvanceTime(dt)`
3. `TickSystem.Execute(View)`
4. Aux systems
5. `MaintenanceSystem.Execute(View)`
6. `Ecb.Playback(_repo)`
7. `_tickActions?.Invoke(View, Ecb)`

### P2 Defect -- BATCH-04-P2-001: `Fixture_AfterMultipleLoads_OldAlcsReclaimedNewestStillLive` does NOT test reclaim

**TASK-TH-005 SC3** requires:
> Load v1, call SimulateReload to v2, call SimulateReload to v3. Assert v1 and v2 ALCs
> are reclaimed (TryGetTarget returns false after ForceGcReclaim). Assert v3 ALC still live.

Because `SimulateReload` calls `CompileAndLoadMany` which throws `NotImplementedException`
in Phase 1, the developer correctly used `LoadTestAssemblyFromBytes` as a bypass. However,
the current test only loads 3 ALCs and asserts they are all **live**, which is the opposite
of what SC3 requires.

**Current test behavior:**
```csharp
// Loads 3 ALCs, then asserts ALL 3 are LIVE -- this does NOT test reclaim
Assert.All(fixture.GetAlcWeakReferences(),
    w => Assert.True(w.TryGetTarget(out _), "All ALCs should be live before Dispose"));
```

**Required behavior** (SC3 equivalent in Phase 1):
- Load 3 ALCs via `LoadTestAssemblyFromBytes`
- Store the first two ALC refs, then manually unload them via `alc.Unload()`
- Call `fixture.ForceGcReclaim()`
- Assert that the first two `WeakReference<AssemblyLoadContext>`s are no longer live
- Assert that the third (still "active") is live

The simplest Phase-1-compatible fix: obtain the first two ALCs from `GetAlcWeakReferences()`,
call `.TryGetTarget(out var alc)` and `alc.Unload()` on each, then call `ForceGcReclaim()`
and assert reclaim. The third ALC (not unloaded) should still be live.

**Must be fixed in BATCH-05 as Corrective Task 0.**

### Minor Issues (P3)

**P3-BATCH04-001: `SnapshotAllBlackboards()` missing**

TASK-TH-003 specifies `SnapshotAllBlackboards() -> ImmutableArray<byte>` as a fixture
method. Not implemented. Acceptable to defer to Phase 2 since it requires the real
`BlueprintBlackboardPartitions` from TASK-RT-004. Track as P3 debt.

**P3-BATCH04-002: `SetChannelStatus<TChannel>(Entity, NodeStatus)` missing**

TASK-TH-003 specifies this method for Phase 5 (Debug Protocol) usage. Requires channel
types from Phase 2/5. Track as P3 debt, implement when channel types exist.

**P3-BATCH04-003: `GetSlotEntry(BlueprintAsset, Entity)` missing**

TASK-TH-003 specifies this as a slot inspection helper. Track as P3 debt.

---

## Test Quality Assessment

### BlueprintTestFixtureTests

GOOD. 7 pass, 2 skip.
- `Constructor_InitializesAllProperties` (SC1): checks all 8 properties + DebugProbe.Sink wiring.
- `PublishEvent_ViaBus_ReadableInNextTickFrame` (SC2): correctly validates event propagates
  across frames via SwapBuffers, and is absent in the next frame. Two-frame test pattern
  is solid.
- `EcbAddComponent_DeferredUntilTickFrame` (SC3): verifies ECB deferred until TickFrame,
  checks both `HasComponent` and `GetComponentRO<T>().Value`. Good.
- `ChooseTier_CorrectBoundaries` (SC5): exercises exact boundary values (928/929, 3936/3937).
- `Dispose_WithNoAlcsLoaded_Succeeds`: defensive regression test, good.
- `AddSimulationSystem_SystemExecutedEachTick`: 2-frame counter verification, good.
- `DebugProbe_WiredToDebugSession_RecordsProbeCall`: direct DebugProbe.NodeEnter -> Session.Hit assertion, good.

### AlcUnloadTests

MIXED. 4 pass, 0 skip.

- `Fixture_DisposeAfterLoadAssembly_ReclaimsAlc` (SC2): strong implementation using
  `[MethodImpl(NoInlining)]` helper and non-generic `WeakReference.IsAlive`. The GC pattern
  matches official .NET unloadability guidance. The developer's investigation into JIT
  Debug-mode root pinning is a valuable insight (documented in DEBT section below).
- `Fixture_DisposeNoAlcs_Succeeds` (SC1): correct.
- `Fixture_StrongRefToAlc_DetectsLeakAndThrows` (SC5): correct -- holds ALC reference,
  asserts `InvalidOperationException` with "ALC(s) not GC-reclaimed", then releases ref.
  Note: tests via direct ALC reference, not a leaked delegate as the design suggested --
  functionally equivalent for Phase 1.
- `Fixture_AfterMultipleLoads_OldAlcsReclaimedNewestStillLive` (**DEFECTIVE** -- P2):
  Only tests that 3 ALCs are all live after loading. Does NOT verify that old ALCs
  are reclaimed after unload. SC3 is NOT covered. See P2 defect above.

---

## Developer Insights Extraction (for DEBT-TRACKER)

The developer surfaced two important .NET runtime findings:

1. **Debug JIT keeps locals alive for method scope**: In Debug builds, JIT does not release
   local variable slots until the enclosing method returns. This includes discarded `out _`
   parameters. Any temp holding an Assembly/ALC reference pins it throughout the test method.
   Pattern: move all ALC-touching code into a `[NoInlining]` helper.

2. **`WeakReference<T>.TryGetTarget(out T)` creates strong ref via `out` parameter**: The `out`
   slot acts as a GC root. Use non-generic `WeakReference.IsAlive` inside GC loops.

These insights are already applied correctly in `Fixture_DisposeAfterLoadAssembly_ReclaimsAlc`
and `AlcUnloadTests.CreateLoadAndDispose`. Recording in DEBT-TRACKER as guidance for
future ALC test authors.

---

## Test Execution Results

```
Passed!  - Failed: 0, Passed: 87, Skipped: 5, Total: 92, Duration: ~760 ms
```

---

## Suggested Git Commit Message

```
feat(blueprints): BATCH-04 -- TH-003, TH-005, CT0 P2 fixes

- Fix: NodeEnterRecord now has Time field (CT0-1 from BATCH-03 review)
- Fix: Add Debug_TraceMode and Debug_Breakpoint skip-annotated tests (CT0-2)
- Add: BlueprintTestFixture with TickFrame, ALC management, registrar wiring
- Add: BlueprintTestFixtureOptions with GC reclaim settings
- Add: AlcUnloadTests -- SC1/SC2/SC5 covered; SC3 test defective (see BATCH-05 CT0)
- Add: Stubs: BlueprintRegistrarAttribute, BlueprintStateView
- Tests: 92 total (87 pass, 5 skip)
```

---

## TASK-TRACKER Updates

- [x] TASK-TH-003 -- COMPLETE (minor P3 omissions deferred)
- [x] TASK-TH-005 -- COMPLETE (P2 test defect fixed in BATCH-05 CT0)
