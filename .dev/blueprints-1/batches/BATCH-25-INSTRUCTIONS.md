# BATCH-25: Phase 7 Demos -- Runtime Integration Demo Tests

**Batch Number:** BATCH-25
**Tasks:** TASK-DEMO-001 through TASK-DEMO-005
**Phase:** 7 -- Demos
**Estimated Effort:** 3-4 days
**Priority:** HIGH
**Dependencies:** BATCH-24 (Phase 6 complete)

---

## 0. Onboarding

### Required Reading (IN ORDER)

1. `.dev/blueprints-1/batches/BATCH-25-INSTRUCTIONS.md` (this file)
2. `.dev/blueprints-1/TASK-DETAIL.md` §DEMO-001 through §DEMO-005
3. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs` -- READ ALL (critical!)
4. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/HotReload/RuntimeIntegration/AiPrimitiveReloadTests.cs` -- pattern example
5. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/HotReload/RuntimeIntegration/SoftReloadTests.cs` -- pattern example
6. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/EndToEnd/MathUtilsLib_EndToEndTests.cs` -- existing EndToEnd patterns
7. All 5 existing EndToEnd test files in `Compiler/EndToEnd/` -- understand what is already covered

### Asset State

The demo JSON assets already exist in `TestAssets/`. Their current state:
- `LibraryMath.bp.json`: Has "Add" function graph (stub). Compiles. May not Roslyn-compile due to `System.Math.Add` not existing in BCL.
- `HealthRegen.bp.json`: Has Variables (CurrentHealth, MaxHealth) but NO graphs. Compiles. `CompileAndLoad` will work.
- `DoorActor.bp.json`: Has Variables (IsOpen) but NO graphs. Compiles.
- `DoorSensor.bp.json`: Has Variables but NO graphs. Compiles.
- `HasVisibleTarget.bp.json`: Has graph: EventEntry -> Return (always returns Success/Failure depending on return node default).
- `MoveToAndFire.bp.json`: Has full action graph with ChannelCommand + WaitForChannel. Most complete asset.

### IMPORTANT: Asset limitations affect what can be tested

Because HealthRegen has no Tick graph and DoorActor/DoorSensor have no function graphs, some of the runtime behavior tests from the TASK-DETAIL doc CANNOT be implemented (e.g., "CurrentHealth increases after tick", "peer call sets IsOpen=true"). Focus on what IS testable:
- `CompileAndLoad` (Roslyn compile + ALC lifecycle)
- `InvokeBTreeAction` for AiPrimitive assets
- `GetBlueprintState` for Instance assets (initial variable values)
- ALC leak/reload tests
- Snapshot tests (generated C# source comparison)

### Report Submission

`.dev/blueprints-1/reports/BATCH-25-REPORT.md`

---

## 1. Test File Structure

Create all demo test files in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Demos/`:

```
Hrot.Blueprints.Tests/Demos/
  LibraryMathDemoTests.cs
  HealthRegenDemoTests.cs
  DoorActorDoorSensorDemoTests.cs
  HasVisibleTargetDemoTests.cs
  MoveToAndFireDemoTests.cs
```

---

## 2. DEMO-001: LibraryMath Demo Tests

`Demos/LibraryMathDemoTests.cs`:

Tests to implement:

**SC1: `LibraryMath_CompileAndLoad_Succeeds`**
- `fixture.CompileAndLoad(asset)` does not throw.
- The returned assembly has at least one type.

**SC2: `LibraryMath_ALC_ReclaimedAfterReload`** (follow `[NoInlining]` + GC loop pattern)
- `CompileAndLoad`, then `SimulateReload(new[] { asset })`.
- GC loop (50 retries, Thread.Sleep(50ms)).
- Assert all but the current ALC are reclaimed.

**SC3: `LibraryMath_GeneratedSource_Snapshot`**
- `new BlueprintCompiler().Compile(asset, opts)` (Blueprint-only compile -- no Roslyn).
- `TestData.ReadOrRegenerateSnapshot("Demos/LibraryMath.cs.txt", result.GeneratedSource!)`.
- This test creates the snapshot on first run with `BLUEPRINT_REGENERATE_SNAPSHOTS=1`.

Add manual authoring walkthrough in a `// MANUAL WALKTHROUGH: DEMO-001` comment block at the bottom of the class.

**IMPORTANT:** `CompileAndLoad` may fail for LibraryMath if the generated C# has `System.Math.Add` which doesn't exist. If `CompileAndLoad` throws (Roslyn error), wrap SC1/SC2 with `Skip("LibraryMath.bp.json requires completed graph nodes")` using xUnit's `[Fact(Skip = ...)]`. Do NOT hardcode a Try/Catch that ignores exceptions.

---

## 3. DEMO-002: HealthRegen Demo Tests

`Demos/HealthRegenDemoTests.cs`:

**SC1: `HealthRegen_CompileAndLoad_Succeeds`**
- `fixture.CompileAndLoad(asset)` does not throw.

**SC2: `HealthRegen_InitialVariables_CurrentHealth_DefaultsTo100`**
- `CompileAndLoad(asset)`.
- `CreateEntity()`, `AttachBlueprint(asset, entity)`.
- `GetBlueprintState(asset, entity)` is not null.
- The state is valid (StateSize > 0).
- Note: Reading the actual CurrentHealth float value from the slot requires unsafe pointer arithmetic. Use `GetBlueprintState` to confirm the slot exists; verify StateSize matches expected variable layout.

**SC3: `HealthRegen_SoftReload_SlotPreserved`** (reuse pattern from `SoftReloadTests`)
- Load asset, create entity, attach, tick once, get state before reload, reload same asset, get state after, assert StructureHash unchanged.
- Follow `[NoInlining]` + GC loop pattern from `SoftReloadTests`.

**SC4: `HealthRegen_ALC_ReclaimedAfterReload`**
- Follow same GC loop pattern. Assert ALC reclaimed.

Add manual walkthrough comment at the bottom.

---

## 4. DEMO-003: DoorActor + DoorSensor Demo Tests

`Demos/DoorActorDoorSensorDemoTests.cs`:

**SC1: `DoorActor_And_DoorSensor_CompileAndLoadTogether`**
- `fixture.CompileAndLoadMany(new[] { doorActor, doorSensor })` does not throw.
- Returns an assembly with 2+ generated types.

**SC2: `DoorActor_ALC_ReclaimedAfterReload`** (NoInlining + GC loop)
- Load both, reload both, GC loop, assert reclaimed.

**SC3: `DoorActor_HasIsOpen_Variable_InRegistry`**
- After `CompileAndLoad`, `fixture.Registry.TryGetById(hash, out var def)` returns true.
- `def.StateSize > 0`.

Add manual walkthrough comment (including: "Note: Peer call tests require graph nodes in DoorActor/DoorSensor assets -- deferred to when graph authoring is complete").

---

## 5. DEMO-004: HasVisibleTarget Demo Tests

`Demos/HasVisibleTargetDemoTests.cs`:

**SC1: `HasVisibleTarget_CompileAndLoad_Succeeds`**
- `fixture.CompileAndLoad(asset)` does not throw.

**SC2: `HasVisibleTarget_InvokeBTreeAction_ReturnsValidStatus`**
- `CompileAndLoad(asset)`.
- `CreateEntity()`.
- `InvokeBTreeAction(asset, entity)` returns `Success` or `Failure` (no exception thrown).
- The condition graph (EventEntry -> Return) returns a deterministic result.

**SC3: `HasVisibleTarget_ALC_ReclaimedAfterReload`**
- NoInlining + GC loop pattern.

Add manual walkthrough comment.

---

## 6. DEMO-005: MoveToAndFire Demo Tests

`Demos/MoveToAndFireDemoTests.cs`:

This is the most complete asset with a real graph. Use the `[NoInlining]` + GC loop pattern throughout.

**SC1: `MoveToAndFire_Tick1_ReturnsRunning`** (NoInlining + GC loop)
- `CompileAndLoad(asset)`.
- `CreateEntity()`.
- `InvokeBTreeAction(asset, entity)` returns `Running` (first tick: issues ChannelCommand, WaitForChannel -> not yet arrived -> Running).

**SC2: `MoveToAndFire_MultipleReloads_AllAlcsReclaimed`** (NoInlining + GC loop)
- Load asset.
- 3x `SimulateReload(new[] { asset })`.
- GC loop.
- Assert all ALCs that were created (all 4 = initial + 3 reloads) are reclaimed except the last one.
- Wait: the 3 OLD ALCs (initial + reload1 + reload2) should be reclaimed; the current ALC (reload3) is still live.
- The `GetAlcWeakReferences()` returns all 4; only the last one (`GetCurrentAlc()`) should still be alive.
- So `GetAlcWeakReferences().Count(w => !w.TryGetTarget(out _)) >= 3`.

**SC3: `MoveToAndFire_ALC_ReclaimedAfterSingleReload`** (NoInlining + GC loop)
- Standard ALC lifecycle test.

**SC4: `MoveToAndFire_GeneratedSource_Snapshot`**
- `TestData.ReadOrRegenerateSnapshot("Demos/MoveToAndFire.cs.txt", generatedSource)`.

Add manual walkthrough comment block:
```csharp
// MANUAL WALKTHROUGH: DEMO-005 (Roadmap Section 10 acceptance)
// 1. Open Asset Browser -> double-click MoveToAndFire.bp.json
// 2. Verify Main graph shows: EventEntry -> ChannelCommand(Locomotion.MoveTo) -> WaitForChannel -> Return
// 3. In debug mode: set breakpoint on ChannelCommand node, tick simulation
// 4. Verify DebugPanel shows [PAUSED] with breakpoint hit info
// 5. Step Over: proceeds to WaitForChannel node
// 6. Quick Reload: change TargetEntity param name in JSON, reload, verify graph refreshes
// 7. Verify HotReloadLog shows QuickReloadViaApi source, Succeeded=true
```

---

## 7. Build + Verify

```powershell
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --filter "FullyQualifiedName~Demo" -v minimal
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -v minimal
```

Expected: 0 errors, 0 failures. Total count >= 474 (463 + ~11 new tests).

---

## 8. Order of Operations

1. Read BATCH-25-INSTRUCTIONS.md.
2. Read `BlueprintTestFixture.cs` FULLY -- especially `CompileAndLoad`, `InvokeBTreeAction`, `AttachBlueprint`, `GetBlueprintState`, `SimulateReload`, `GetAlcWeakReferences`.
3. Read the 2 HotReload test files as patterns.
4. Read all 5 existing EndToEnd test files to avoid duplication.
5. Create `Demos/` subfolder + all 5 demo test files.
6. Build Tests: `dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -v quiet`. Fix errors.
7. Run Demo filter tests. Fix failures.
8. Run full suite. Fix any failures.
9. SNAPSHOT GENERATION: Run with `$env:BLUEPRINT_REGENERATE_SNAPSHOTS="1"` to create initial snapshots for Demos/LibraryMath.cs.txt and Demos/MoveToAndFire.cs.txt, then run tests normally to verify they pass.
10. Commit.
11. Write report.

---

## 9. GC Pattern for NoInlining Tests

Every ALC leak test must follow this EXACT pattern from `SoftReloadTests`:
```csharp
[Fact]
public void Test_Name()
{
    WeakReference<AssemblyLoadContext>[] alcWeakRefs;
    Test_Name_Body(out alcWeakRefs);
    for (int i = 0; i < 50; i++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        if (alcWeakRefs.All(w => !w.TryGetTarget(out _))) return;
        Thread.Sleep(50);
    }
    int leaked = alcWeakRefs.Count(w => w.TryGetTarget(out _));
    Assert.True(leaked == 0, $"{leaked} ALC(s) not GC-reclaimed.");
}

[MethodImpl(MethodImplOptions.NoInlining)]
private static void Test_Name_Body(out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
{
    using var fixture = new BlueprintTestFixture(
        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    // ... test body ...
    alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
}
```

For MULTI-RELOAD tests (SC2 of MoveToAndFire), modify the final assertion to check that all BUT the current ALC are reclaimed. Use `fixture.GetCurrentAlc()` to identify the live ALC.

---

## 10. Snapshot Generation

For snapshot tests, first run with regenerate flag to create the files:
```powershell
$env:BLUEPRINT_REGENERATE_SNAPSHOTS="1"
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --filter "Snapshot" -v minimal
Remove-Item env:BLUEPRINT_REGENERATE_SNAPSHOTS
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --filter "Snapshot" -v minimal
```

The snapshot files go in `Hrot.Blueprints.Tests/Snapshots/Demos/`.

---

## 11. Commit

```
git add Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Demos/
git add Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Snapshots/Demos/
git commit -m "feat(blueprints): BATCH-25 Phase 7 demo runtime integration tests

- LibraryMathDemoTests: CompileAndLoad, ALC reclaim, generated source snapshot
- HealthRegenDemoTests: CompileAndLoad, InitialVariables, SoftReload slot preserve, ALC reclaim
- DoorActorDoorSensorDemoTests: CompileAndLoadTogether (both assets one ALC), ALC reclaim
- HasVisibleTargetDemoTests: CompileAndLoad, InvokeBTreeAction returns valid status, ALC reclaim
- MoveToAndFireDemoTests: Tick1 returns Running, 3-reload ALC chain reclaim, snapshot
- Snapshots/Demos/: LibraryMath.cs.txt, MoveToAndFire.cs.txt
- Manual walkthrough comment blocks in each demo test class

Baseline: 463 -> X pass / 5 skip / 0 fail"
```

---

## Success Criteria

| SC | Check |
|----|-------|
| SC1-3 | LibraryMath: load, ALC reclaim, snapshot |
| SC1-4 | HealthRegen: load, initial state, soft reload, ALC reclaim |
| SC1-3 | DoorActor/Sensor: load together, ALC reclaim, registry entry |
| SC1-3 | HasVisibleTarget: load, BTree invoke returns valid status, ALC reclaim |
| SC1-4 | MoveToAndFire: tick1=Running, 3-reload chain, ALC reclaim, snapshot |
| Build | 0 errors |
| Tests | 0 failures full suite |
