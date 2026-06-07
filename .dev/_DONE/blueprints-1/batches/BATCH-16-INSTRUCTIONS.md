# BATCH-16 Instructions — BATCH-15 Fix-Up + Phase 5 DBG-000, DBG-001

**Tasks:** CT0-A (DEBT-016 fix), CT0-B (DEBT-017 fix), TASK-DBG-000, TASK-DBG-001  
**Phase:** 4 fix-up + Phase 5 Debug Protocol start  
**Previous review:** `.dev/blueprints-1/reviews/BATCH-15-REVIEW.md`  
**Current test state:** 341 pass / 6 fail / 5 skip (BATCH-15 code uncommitted, 6 HotReload tests failing)

---

## 0. Onboarding

**Required reading:**
1. `.dev/blueprints-1/BATCH-15-REVIEW.md` — root cause of the 6 failures you are fixing.
2. `.dev/blueprints-1/TASK-DETAIL.md` §HR-001, §HR-002, §HR-003 — Phase 4 spec (already implemented).
3. `.dev/blueprints-1/TASK-DETAIL.md` §DBG-000, §DBG-001 — Phase 5 tasks you implement this batch.
4. `.dev/blueprints-1/Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md` §1–§3.
5. `.dev/blueprints-1/Blueprint_Subsystem_Debug_Protocol_Detailed_Design_InlinePatches.md` — Patch 1 and Patch 2 supersede parts of the main doc.
6. `.dev/blueprints-1/DEBT-TRACKER.md` — DEBT-009, DEBT-010, DEBT-011, DEBT-016, DEBT-017.

**Key codebase facts:**
- Uncommitted BATCH-15 code is already in the workspace (verified via `git status`).
- `BlueprintTestFixture.cs` is at `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs`.
- `AiHotReloadCoordinator.cs` is at `FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs`.
- Hot reload tests are at `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/HotReload/`.
- `Hrot.Blueprints.Core.Debug` namespace — add new files at `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Debug/`.
- `Hrot.Blueprints.Editor.Debug` namespace — add at `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/`.
- `DebugProbe` already exists at `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Debug/DebugProbe.cs` — **do NOT recreate it, only extend if needed**.

---

## 1. Corrective Task 0-A: Fix `[NoInlining]` on ALC-creating fixture methods (DEBT-016)

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs`

**Problem:** `CompileAndLoadMany`, `SimulateReload`, `SimulateQuickReload`, `SimulateReloadWithThrowingRegistrar`, and `SimulateReloadFromAlc` are NOT marked `[MethodImpl(MethodImplOptions.NoInlining)]`. Per DEBT-011: in Debug JIT, the JIT may inline these methods into their calling frame, keeping local variables (including `alc`, `assembly`, `roslynCompiler`) alive for the calling frame's lifetime and preventing ALC GC.

**Fix:** Add `[MethodImpl(MethodImplOptions.NoInlining)]` to each of these five fixture methods:
```csharp
[MethodImpl(MethodImplOptions.NoInlining)]
public Assembly CompileAndLoadMany(...)

[MethodImpl(MethodImplOptions.NoInlining)]
public void SimulateReload(...)

[MethodImpl(MethodImplOptions.NoInlining)]
public void SimulateQuickReload(...)

[MethodImpl(MethodImplOptions.NoInlining)]
public void SimulateReloadWithThrowingRegistrar()

[MethodImpl(MethodImplOptions.NoInlining)]
internal void SimulateReloadFromAlc(...)
```

`using System.Runtime.CompilerServices;` is already present in the file.

---

## 2. Corrective Task 0-B: Fix `FailedReload_DoesNotLeakNewAlc` (DEBT-017)

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/HotReload/Coordinator/AlcLifecycleTests.cs`

**Problem:** In `FailedReload_DoesNotLeakNewAlc_Body`, the code calls `Record.Exception(() => fixture.SimulateReloadWithThrowingRegistrar())` and stores the result in `var ex`. Then — while `ex` is still a live local — it calls `fixture.ForceGcReclaim()` and asserts `liveAlcs == 1`. The exception `ex` holds `InnerException.TargetSite` pointing to `ThrowingRegistrar.Register` in the failed ALC, keeping it alive during the GC check.

**Fix:** Extract the `Record.Exception` call and `Assert.NotNull` into a `[NoInlining]` helper so `ex` goes out of scope before the GC check:

```csharp
[MethodImpl(MethodImplOptions.NoInlining)]
private static void ThrowingRegistrarMustThrow(BlueprintTestFixture fixture)
{
    var ex = Record.Exception(() => fixture.SimulateReloadWithThrowingRegistrar());
    Assert.NotNull(ex);
    // ex (which holds InnerException.TargetSite from failed ALC) goes out of scope here
}
```

Then in `FailedReload_DoesNotLeakNewAlc_Body`, replace:
```csharp
// Failed reload — coordinator should unload the new (failed) ALC.
var ex = Record.Exception(() => fixture.SimulateReloadWithThrowingRegistrar());
Assert.NotNull(ex);

// Force GC to reclaim the failed ALC.
fixture.ForceGcReclaim();
```

With:
```csharp
// Failed reload — coordinator should unload the new (failed) ALC.
// Use [NoInlining] helper so the exception (which holds TargetSite from the failed ALC)
// goes out of scope before the GC check (DEBT-017).
ThrowingRegistrarMustThrow(fixture);

// Force GC to reclaim the failed ALC.
fixture.ForceGcReclaim();
```

---

## 3. Verification: all BATCH-15 tests must pass

After CT0-A and CT0-B:

```powershell
# Run only the HotReload tests
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --filter "FullyQualifiedName~HotReload" -v normal

# Run full suite
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -v minimal
```

**Expected:** All HotReload tests pass (0 failures). Full suite: ≥ 347 pass / 5 skip / 0 fail.

If any HotReload tests still fail after the `[NoInlining]` fixes, check whether there are additional fixture methods that create ALCs without `[NoInlining]`, or whether the GC loop in the Fact uses `TryGetTarget(out _)` in a way that creates strong refs (DEBT-010). As a last resort, increase `GcReclaimRetries` in `BlueprintTestFixtureOptions.Default` from 10 to 20.

---

## 4. Commit BATCH-15 + CT0 fixes

After all tests pass, commit the Phase 4 work:

```powershell
cd d:\WORK\IOS-IG-SimHost-FDP
git add .
git commit -m "feat(blueprints): BATCH-15/16 Phase 4 Hot Reload + GC fix

- AiHotReloadCoordinator: all 4 patches (main-thread ALC, static HsmDispatcher,
  ApplyQuickReload, RCU contract guard)
- BlueprintTestFixture: SimulateReload/QuickReload/WithThrowingRegistrar wired to
  coordinator; GetCurrentAlc, SimulateReloadFromAlc helpers
- Hot reload test suite: Coordinator, AlcLifecycle, RegistrarInjection,
  QuickReload, RuntimeIntegration (Soft/Hard/AiPrimitive/LatentCursor), PdbLoad
- Fix DEBT-016: [NoInlining] on all ALC-creating fixture methods
- Fix DEBT-017: ThrowingRegistrarMustThrow [NoInlining] helper in AlcLifecycleTests
- Baseline: 347+ pass / 5 skip / 0 fail"
```

> Check `git status` before committing to ensure the submodule (FDP) is also staged and committed if any FDP files were modified. Look for modified files under `FDP/`. If any exist, commit the FDP submodule first with a matching message, then commit the top-level repo.

---

## 5. TASK-DBG-000: Blueprint Time Controller Adapter

See `TASK-DETAIL.md §DBG-000` for full scope, code samples, and success conditions.

**Summary:**
1. Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Debug/IBlueprintTimeController.cs` with the interface in `Hrot.Blueprints.Core.Debug` namespace.
2. Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/MasterSyncTimeControllerAdapter.cs` with the adapter in `Hrot.Blueprints.Editor.Debug` namespace.

Exact code samples are in TASK-DETAIL.md §DBG-000. Follow them precisely.

**Build check:**
```powershell
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Hrot.Blueprints.Core.csproj -v minimal
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Hrot.Blueprints.Editor.csproj -v minimal
```

---

## 6. TASK-DBG-001: Debug Session Interface and DebugProbe Dispatcher

See `TASK-DETAIL.md §DBG-001` for full scope and success conditions.  
See `Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md §1-§3` and the `InlinePatches.md` for detailed design.  
**Patch 1 and Patch 2 from InlinePatches SUPERSEDE parts of the main doc** — read them first.

### 6.1 Key types to create

**`IBlueprintDebugSession` interface** in `Hrot.Blueprints.Core.Debug`:
- Consult Debug Protocol DD §2.1 for the full member list.
- Event `OnPinValueChangedEvent` (NOT `OnPinValueChanged` — C# conflict with generic method name, per DEBT-004).
- `PinValueChanged` record uses `byte[] ValueBytes + Type ValueType` per Patch 2 (NOT `object Value`).

**`IBlueprintProbeSink` interface** in `Hrot.Blueprints.Core.Debug`:
- `void OnNodeEnter(Entity entity, string nodeId)`
- `void OnPinValueChanged<T>(Entity entity, string pinId, T value) where T : unmanaged`  
- `void OnPeerCallEnter(Entity entity, string targetAssetName, string targetGraphName)`
- `void OnPeerCallExit(Entity entity)`

**`DebugProbe` static class** — ALREADY EXISTS at `Hrot.Blueprints.Core/Debug/DebugProbe.cs`. Check its current state. If it already has the correct methods, do not recreate. If missing methods, add them. The `Sink` field and probe methods (`NodeEnter`, `PinValueChanged<T>`, `PeerCallEnter`, `PeerCallExit`) must match `IBlueprintProbeSink`.

**`BlueprintDebugSession` class skeleton** in `Hrot.Blueprints.Core.Debug`:
- Constructor: `(BlueprintRegistry registry, ISimulationView view, IBlueprintTimeController timeController)`
- Implements `IBlueprintDebugSession`
- All methods throw `NotImplementedException` except `OnNodeEnter`, `OnPinValueChanged<T>`, `OnPeerCallEnter`, `OnPeerCallExit` (these must be real implementations calling `DebugProbe.Sink?.OnX(...)` or routing to the session's internal state — per SC1/SC2 of TASK-DBG-001).
- **Important:** `CapturingDebugSession` (already in tests) is for the TEST harness. `BlueprintDebugSession` is the production session.

**Supporting records and types:**
- `BreakpointHit` record: `Entity Self`, `string NodeId`, `Guid AssetId`, `float SimulationTime`, `uint Tick`
- `PinValueChanged` record (Patch 2): `Entity Self`, `string PinId`, `byte[] ValueBytes`, `Type ValueType`, `uint Tick`
- `NodeHistoryEntry` record: `string NodeId`, `uint Tick`, `float SimTime`
- `WatchId` record struct (wraps `int`)
- `BreakpointId` record struct (wraps `int`)
- `MockTimeController` in `Hrot.Blueprints.Tests`: `PauseWasRequested`, `PauseRequestCount`, `ResumeCount`, `StepRequestCount` properties; implement `IBlueprintTimeController`

### 6.2 DebugProbe verification

The `DebugProbe` class is already used (TASK-TH-008 `CapturingDebugSession` routes `DebugProbe.Sink`). Read `Hrot.Blueprints.Core/Debug/DebugProbe.cs` first before touching it. If the existing implementation already satisfies SC1 and SC2, write tests that verify it and move on.

### 6.3 Test file

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/DebugSessionInterfaceTests.cs`:
- Tests for SC1: `DebugProbe.NodeEnter(entity, "n1")` with null Sink — no exception, zero allocation (use `GC.GetAllocatedBytesForCurrentThread()` guard).
- Tests for SC2: `DebugProbe.PinValueChanged<int>(entity, "p1", 42)` with null Sink — same.
- Tests for SC3: Set `DebugProbe.Sink = session`; call `DebugProbe.NodeEnter(entity, "bp-node")` where "bp-node" matches a registered breakpoint in the session; assert `MockTimeController.PauseWasRequested == true`.
  > Note: SC3 requires minimal breakpoint wiring in `BlueprintDebugSession`. At minimum, `OnNodeEnter` must check `DebugProbe.Sink` and call `_timeController.RequestPause()` when a matching BP is found. Full breakpoint logic is DBG-003; for this task, just stub it.
- Tests for SC4: `PinValueChanged` record has `ValueBytes` property (not `Value`), assertion via reflection.

### 6.4 Build and test

```powershell
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Hrot.Blueprints.Core.csproj -v minimal
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -v minimal
```

Expected: 0 failures. New DBG-001 tests should pass.

---

## 7. Test-Driven Task Progression

For each task:

1. **Read** the design before writing any code.
2. **Write tests first** (or alongside implementation) that verify the success conditions.
3. **Implement** to make tests pass.
4. **Run** `dotnet test` and confirm zero failures.
5. **Do not skip tests** — a test that throws `NotImplementedException` is a failing test, not a skip.

---

## 8. Developer Insights Section

In your report, answer:
1. **Issues encountered:** Did any existing code conflict with the new types (e.g., `DebugProbe` already having some methods)? Any naming conflicts?
2. **Weak points spotted:** Any patterns in the codebase that seem fragile or inconsistent with the design?
3. **Design decisions beyond spec:** Did you have to make any judgment calls not covered by the task spec or design docs?
4. **ALC reclaim confidence:** After the `[NoInlining]` fixes, did all HotReload tests pass consistently across multiple runs? Were there any flaky failures?

---

## 9. Report

Submit your report to: `.dev/blueprints-1/batches/BATCH-16-REPORT.md`

**Required sections:**
- Work completed (tasks CT0-A, CT0-B, DBG-000, DBG-001)
- Test results before CT0 fixes and after
- Success criteria coverage table
- Developer insights (answer all 4 questions above)

---

## Success Criteria Summary

| SC | Task | Check |
|----|------|-------|
| CT0-A | DEBT-016 | All HotReload tests pass (0 failures) |
| CT0-B | DEBT-017 | `FailedReload_DoesNotLeakNewAlc` passes (0 failures) |
| Full suite | All | 347+ pass / 5 skip / 0 fail after CT0 fixes |
| DBG-000 SC1-SC6 | TASK-DBG-000 | Interface and adapter exist; `dotnet build` 0 errors |
| DBG-001 SC1-SC5 | TASK-DBG-001 | `DebugProbe` null-sink calls are allocation-free; mock time controller gets pause request on BP hit |
