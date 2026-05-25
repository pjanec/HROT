# BATCH-48: UBP-P10T1 + UBP-P10T2 — Editor & CGF Subsystem Wiring

**Batch Number:** BATCH-48  
**Tasks:** UBP-P10T1 (Editor subsystem wiring), UBP-P10T2 (CGF subsystem wiring)  
**Phase:** P10 — Production integration  
**Estimated Effort:** 12–16 hours  
**Priority:** HIGH (blocker for all remaining P10 tasks)  
**Dependencies:** BATCH-47 complete (all tests green, 103 passing)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch wires the `DataBreakpointManager` + `DebugSnapshotProvider` + `DataBreakpointSystem` into the two subsystems that actually run the AI brain: **`EditorSubsystem`** (offline authoring) and **`CgfSubsystem`** (online Brain node). All the library plumbing was built and validated in BATCH-35–47; this batch makes it reachable from the production runtime. No new library types are created.

### Required Reading (IN ORDER)

1. **Design:** `.dev/breakpoints-1/DESIGN.md` — §5 (Triple-buffer architecture), §11.1 (Per-subsystem isolation), §11.4 (Window scope)
2. **Task definitions:** `.dev/breakpoints-1/TASK-DETAIL.md` — §UBP-P10T1, §UBP-P10T2 (lines ~415–455)
3. **Last batch review:** `.dev/breakpoints-1/reviews/BATCH-47-REVIEW.md`
4. **Existing library entry point:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` — constructor signature (lines 55–75)
5. **Adapter:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/MasterSyncTimeControllerAdapter.cs` — wraps `MasterSyncController` → `IEngineDebugTimeController`
6. **EditorSubsystem structure:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — read lines 1–360 to understand fields and `Initialize()` structure
7. **CgfSubsystem structure:** `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` — read lines 65–400 to understand `_context` (`HrotNodeContext`) pattern
8. **Kernel registration:** `FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs` — `RegisterGlobalSystem<T>()` (line ~151) — must be called **before** `Initialize()`
9. **Integration test harness patterns:** 
   - `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` — understand what EditorHarness boots (NOT via EditorSubsystem.Initialize)
   - `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/HrotRunnerHarness.cs` — `Cgf` property (line 62)
10. **Existing integration tests for reference:** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorSubsystemBootTests.cs`

### Source Code Locations

- **EditorSubsystem:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`
- **EditorSubsystem csproj:** `Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj`
- **CgfSubsystem:** `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`
- **CgfSubsystem csproj:** `Hrot/Subsystems/Hrot.CGF/Hrot.CGF.csproj`
- **BP library:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/`
- **Adapter:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/MasterSyncTimeControllerAdapter.cs`
- **Test project:** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/`
- **Build from root:** `dotnet build IOS-IG-SimHost.sln -v quiet`
- **Run new tests:** `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --filter "FullyQualifiedName~BreakpointSubsystemWiringTests" --verbosity normal`
- **Run existing BP tests (must stay green):** `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/ --verbosity quiet`

### Report Submission

**When done, submit your report to:**  
`.dev/breakpoints-1/reports/BATCH-48-REPORT.md`

---

## Context

The entire Universal Breakpoints library (P0–P9 + INT1–INT3) exists as a self-contained library in `Hrot.Diagnostics.Breakpoints` and tests in `Hrot.Diagnostics.Breakpoints.Tests` (103 tests, all green). 

**What is missing:** the library is never instantiated in production. There is no call to `new DataBreakpointManager(...)` anywhere in the real subsystem startup paths. This batch adds exactly that: two construction sites + kernel registration + test-hook exposure, one per subsystem.

The design mandates one manager per subsystem (§11.1). Only EditorSubsystem and CgfSubsystem host the cognitive components (`BrainBlackboard`, `BTreeTraceWorkingMemory1024`, etc.) worth inspecting with breakpoints; SimHost (Muscle) is deferred per the design's scope table.

**Related Tasks:**  
- [UBP-P10T1](../TASK-DETAIL.md#ubp-p10t1--editor-subsystem-wiring) — EditorSubsystem wiring  
- [UBP-P10T2](../TASK-DETAIL.md#ubp-p10t2--cgf-subsystem-wiring) — CgfSubsystem wiring

---

## 🎯 Batch Objectives

1. Wire `DebugSnapshotProvider` + `DataBreakpointManager` + `DataBreakpointSystem` into `EditorSubsystem.Initialize()` with correct `RegisterGlobalSystem` calls before `_kernel.Initialize()`.
2. Identical wiring in `CgfSubsystem.Initialize()` using `_context.Kernel`.
3. Expose both managers as `internal IDataBreakpointManager DataBreakpointManager` test hooks.
4. 5 new integration tests proving the wiring works and the zero-overhead gate closes when no BPs are armed.

---

## ✅ Tasks

### Task 1: Add project references (both csproj files)

**Files:**
- `Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj` (UPDATE)
- `Hrot/Subsystems/Hrot.CGF/Hrot.CGF.csproj` (UPDATE)

Neither project currently references `Hrot.Diagnostics.Breakpoints` or `Hrot.Blueprints.Editor`. Add both references to each csproj.

**Relative paths from `Hrot/Subsystems/Hrot.Editor/`:**
```xml
<ProjectReference Include="..\..\Diagnostics\Hrot.Diagnostics.Breakpoints\Hrot.Diagnostics.Breakpoints.csproj" />
<ProjectReference Include="..\Blueprints\Hrot.Blueprints.Editor\Hrot.Blueprints.Editor.csproj" />
```

**Relative paths from `Hrot/Subsystems/Hrot.CGF/`:**
```xml
<ProjectReference Include="..\..\Diagnostics\Hrot.Diagnostics.Breakpoints\Hrot.Diagnostics.Breakpoints.csproj" />
<ProjectReference Include="..\Blueprints\Hrot.Blueprints.Editor\Hrot.Blueprints.Editor.csproj" />
```

After adding references, run `dotnet build IOS-IG-SimHost.sln -v quiet` and confirm 0 errors before proceeding.

---

### Task 2: Wire BP stack into EditorSubsystem

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (UPDATE)

**Task Definition:** See [TASK-DETAIL.md §UBP-P10T1](../TASK-DETAIL.md#ubp-p10t1--editor-subsystem-wiring)

#### 2a. Add new using directives (at top of file, within the existing using block)

```csharp
using Hrot.Diagnostics.Breakpoints;
using Hrot.Blueprints.Editor.Debug;
using StructEdit.Reflection;
using Fdp.Toolkit.ReplayBrowser.Search;
```

Check whether `StructEdit.Reflection` and `Fdp.Toolkit.ReplayBrowser.Search` are already in the file's using list before adding.

#### 2b. Add fields (in the "Core state" region, near `_timeController`)

```csharp
// ── Universal breakpoints (UBP-P10T1) ────────────────────────────────────
private EntityRepository?       _bpPreTickSnapshot;
private DebugSnapshotProvider?  _bpSnapshotProvider;
private DataBreakpointManager?  _bpManager;
private DataBreakpointSystem?   _bpSystem;
```

#### 2c. Wire in Initialize() — placement

In `EditorSubsystem.Initialize()`, after:
- `_kernel = new ModuleHostKernel(_world, accumulator);` (kernel allocated)
- `_timeController = ...` and `_kernel.SetTimeController(...)` (time controller set)
- Component registrations (`SimHostComponentRegistry.RegisterAll(...)`, etc.)
- **But BEFORE** `_kernel.Initialize();` (systems must be registered before kernel initialises)

The `_kernel.Initialize()` call is near line 863. Insert the BP wiring block somewhere after component registration but before that call.

#### 2d. BP wiring block

```csharp
// ── Universal breakpoints (UBP-P10T1) ────────────────────────────────────
// Allocate the pre-tick snapshot repo and mirror all component registrations.
_bpPreTickSnapshot = new EntityRepository();
SimHostComponentRegistry.RegisterAll(_bpPreTickSnapshot);
CgfComponentRegistry.RegisterAll(_bpPreTickSnapshot);
_bpPreTickSnapshot.RegisterManagedComponent<Hrot.Map.Common.Components.ZoneMembership>();
_bpPreTickSnapshot.RegisterComponent<MapDisplayComponent>();
_bpPreTickSnapshot.RegisterComponent<Hrot.IG.Components.CullingState>();
_bpPreTickSnapshot.RegisterComponent<Hrot.IG.Components.ResolvedStyle>();
_bpPreTickSnapshot.RegisterManagedComponent<Hrot.IG.Components.IgSymbolOverride>();
_bpPreTickSnapshot.RegisterComponent<VisualEffectState>();
_bpPreTickSnapshot.RegisterComponent<TracerTarget>();

var bpTimeAdapter     = new MasterSyncTimeControllerAdapter(_timeController!);
var bpEditSvc         = new ComponentEditServiceBuilder().Build();
var bpPredicateCompiler      = new PredicateCompiler(bpEditSvc, _behaviorRegistry);
var bpEventScannerCompiler   = new EventScannerCompiler(bpEditSvc);
_bpSnapshotProvider   = new DebugSnapshotProvider(_bpPreTickSnapshot);
_bpManager            = new DataBreakpointManager(
    _world!, _bpPreTickSnapshot, _bpSnapshotProvider,
    bpTimeAdapter, bpPredicateCompiler, bpEventScannerCompiler);
_bpSystem             = new DataBreakpointSystem(_bpManager, _world!.Bus);

_kernel.RegisterGlobalSystem(_bpSnapshotProvider);
_kernel.RegisterGlobalSystem(_bpSystem);
// ─────────────────────────────────────────────────────────────────────────
```

**Important:** Mirror exactly the component registrations that `_world` gets above. If `_world` receives additional component registrations later (via modules), those module-registered components are NOT mirrored here — that is acceptable per DESIGN §5 (module-owned components are registered per-tick via the pre-tick snapshot's Execute; the snapshot only needs to know what's registered before its first Execute call).

#### 2e. Expose test hook

Add immediately after the existing test hook properties (around line 300, the `/// <summary>Internal test hook...` block):

```csharp
/// <summary>Internal test hook: exposes the data breakpoint manager (UBP-P10T1).</summary>
internal IDataBreakpointManager? DataBreakpointManager => _bpManager;

/// <summary>Internal test hook: exposes the debug snapshot provider (UBP-P10T1).</summary>
internal DebugSnapshotProvider? BpSnapshotProvider => _bpSnapshotProvider;
```

---

### Task 3: Wire BP stack into CgfSubsystem

**File:** `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` (UPDATE)

**Task Definition:** See [TASK-DETAIL.md §UBP-P10T2](../TASK-DETAIL.md#ubp-p10t2--cgf-subsystem-wiring)

The CGF subsystem uses `HrotNodeContext` (`_context`) which wraps the kernel and world. The structure mirrors EditorSubsystem but uses `_context.World` and `_context.Kernel`.

#### 3a. Add using directives

Same as Task 2a. Verify which namespaces are already present in `CgfSubsystem.cs`.

#### 3b. Add fields (near `_context` declaration, around line 70)

```csharp
// ── Universal breakpoints (UBP-P10T2) ────────────────────────────────────
private EntityRepository?       _bpPreTickSnapshot;
private DebugSnapshotProvider?  _bpSnapshotProvider;
private DataBreakpointManager?  _bpManager;
private DataBreakpointSystem?   _bpSystem;
```

#### 3c. Wire in Initialize()

In `CgfSubsystem.Initialize()`, after `_context = new HrotNodeBuilder(...).Build()` and after `_physicsModule.Initialize(_context.World)`, but **before** any module registrations. Look for where `_context.Kernel.RegisterModule(...)` or `_context.Kernel.RegisterGlobalSystem(...)` is first called — insert just before that block.

**Determine the component registrations:** CGF's `_context.World` gets its components registered by `HrotNodeBuilder`. You need to check what `CognitiveComponentRegistry`, `CombatComponentRegistry`, and `CgfComponentRegistry` register. Mirror those same registrations on `_bpPreTickSnapshot`. Look for the `RegisterAll(...)` calls in `CgfSubsystem.Initialize()` or inside `HrotNodeBuilder.Build()`.

In practice: call the same registry `RegisterAll` methods on `_bpPreTickSnapshot` that the CGF world uses. If `HrotNodeBuilder` calls them internally and they're not exposed, call `CgfComponentRegistry.RegisterAll(_bpPreTickSnapshot)` and similar registrars that are referenced elsewhere in CGF startup. The important thing is the schema is compatible enough for the snapshot to capture component data.

```csharp
// ── Universal breakpoints (UBP-P10T2) ────────────────────────────────────
_bpPreTickSnapshot = new EntityRepository();
CgfComponentRegistry.RegisterAll(_bpPreTickSnapshot);
// Add other component registries used by CGF (check HrotNodeBuilder.Build() for the list)

var bpTimeAdapter     = new MasterSyncTimeControllerAdapter(_context.MasterSync); // adjust property name
var bpEditSvc         = new ComponentEditServiceBuilder().Build();
var bpPredicateCompiler    = new PredicateCompiler(bpEditSvc, _behaviorRegistry);
var bpEventScannerCompiler = new EventScannerCompiler(bpEditSvc);
_bpSnapshotProvider   = new DebugSnapshotProvider(_bpPreTickSnapshot);
_bpManager            = new DataBreakpointManager(
    _context.World, _bpPreTickSnapshot, _bpSnapshotProvider,
    bpTimeAdapter, bpPredicateCompiler, bpEventScannerCompiler);
_bpSystem             = new DataBreakpointSystem(_bpManager, _context.World.Bus);

_context.Kernel.RegisterGlobalSystem(_bpSnapshotProvider);
_context.Kernel.RegisterGlobalSystem(_bpSystem);
// ─────────────────────────────────────────────────────────────────────────
```

**Important:** You'll need to find the `MasterSyncController` inside CGF. Look at `HrotNodeContext` for a property that exposes the time controller — it may be `_context.TimeController` or similar. If `HrotNodeContext` doesn't expose it, check how `_clusterTimeAdapter` or the kernel's time controller is accessed and use that. Read `HrotNodeContext` source to find the right property name.

#### 3d. Expose test hook

Add internal test hook properties on `CgfSubsystem` (after the existing `internal NetworkEntityMap? GhostEntityMap` around line 135):

```csharp
/// <summary>Internal test hook: exposes the data breakpoint manager (UBP-P10T2).</summary>
internal IDataBreakpointManager? DataBreakpointManager => _bpManager;

/// <summary>Internal test hook: exposes the debug snapshot provider (UBP-P10T2).</summary>
internal DebugSnapshotProvider? BpSnapshotProvider => _bpSnapshotProvider;
```

---

### Task 4: Write integration tests

**File:** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/BreakpointSubsystemWiringTests.cs` (NEW FILE)

**Test project build:** `dotnet build Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ -v quiet`  
**Test run:** `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --filter "FullyQualifiedName~BreakpointSubsystemWiringTests" --verbosity normal`

#### Test 1: `EditorSubsystem_Init_RegistersManager`

Boot `EditorSubsystem` in headless mode. Assert the breakpoint manager is non-null.

```csharp
[Fact]
public void EditorSubsystem_Init_RegistersManager()
{
    var subsystem = new EditorSubsystem();
    var config    = new SubsystemConfig { Headless = true };
    try
    {
        subsystem.Initialize(config);
        Assert.NotNull(subsystem.DataBreakpointManager);
    }
    finally
    {
        subsystem.Shutdown();
    }
}
```

#### Test 2: `EditorSubsystem_Init_RegistersBreakpointSystems`

Boot editor headless. Assert that both `DebugSnapshotProvider` and `DataBreakpointSystem` were registered with the kernel (check via `_kernel._registeredGlobalSystems` — you'll need to expose that or verify indirectly by running 1 tick and asserting the snapshot provider's `_isEnabled` is readable). The simplest approach: verify via the test hooks added in Task 2e.

If `ModuleHostKernel` doesn't expose `_registeredGlobalSystems`, verify indirectly:
- Assert `subsystem.BpSnapshotProvider != null` (proves it was constructed)
- Run 1 tick via `subsystem.Kernel.Update(1/60f)`, assert no exception (proves systems are registered and Execute is called safely)

```csharp
[Fact]
public void EditorSubsystem_Init_RegistersBreakpointSystems()
{
    var subsystem = new EditorSubsystem();
    var config    = new SubsystemConfig { Headless = true };
    try
    {
        subsystem.Initialize(config);
        
        // Both systems were constructed (registered)
        Assert.NotNull(subsystem.BpSnapshotProvider);
        Assert.NotNull(subsystem.DataBreakpointManager);
        
        // Running a tick exercises Execute() on both systems with no crash
        // (if not registered, kernel would not call Execute and we'd have no breakpoint coverage)
        subsystem.Kernel.Update(1f / 60f);
        // No exception = both systems are in the kernel execution pipeline
    }
    finally
    {
        subsystem.Shutdown();
    }
}
```

#### Test 3: `EditorSubsystem_Boot_NoExtraCost_WhenNoBreakpoints`

Boot headless, run 100 ticks, assert the snapshot provider gate is closed (no BPs were armed, so `_isEnabled` should be 0 / `IsPaused` false, and pre-tick snapshot stays empty).

```csharp
[Fact]
public void EditorSubsystem_Boot_NoExtraCost_WhenNoBreakpoints()
{
    var subsystem = new EditorSubsystem();
    var config    = new SubsystemConfig { Headless = true };
    try
    {
        subsystem.Initialize(config);
        var mgr = subsystem.DataBreakpointManager!;
        
        // Pump 100 ticks
        for (int i = 0; i < 100; i++)
            subsystem.Kernel.Update(1f / 60f);
        
        // Gate stayed closed: no BPs → snapshot provider never enabled
        Assert.False(mgr.IsPaused);
        Assert.Equal(0, mgr.PendingMutationsCount);
        // HasMountedDelegates == false proves the scan loop never ran
        Assert.False(mgr.HasMountedDelegates);
    }
    finally
    {
        subsystem.Shutdown();
    }
}
```

#### Test 4: `CgfSubsystem_Init_RegistersManager`

For CGF, `CgfSubsystem.Initialize()` requires DDS. Use the existing `HrotRunnerHarness` pattern from the integration test project. The `HrotRunnerHarness("cgf", domainId)` boots only CGF with SimHost omitted — but check if CGF requires SimHost. If it does, use `"simhost,cgf"`.

Look at the existing `CgfSubsystemHeadlessTests` in the integration project to understand the right setup. Use a unique domain ID (around 230+) to avoid conflicts.

```csharp
[Fact]
public void CgfSubsystem_Init_RegistersManager()
{
    int domainId = Interlocked.Increment(ref _domainCounter);
    using var harness = new HrotRunnerHarness("simhost,cgf", domainId);
    
    Assert.NotNull(harness.Cgf);
    Assert.NotNull(harness.Cgf.DataBreakpointManager);
}
```

Add `private static int _domainCounter = 230;` as a class-level field using a safe domain range (check existing tests to avoid conflicts — look at domain counter values in `CgfSubsystemHeadlessTests.cs` line ~44).

#### Test 5: `CgfSubsystem_HeavyScenario_NoBreakpoints_ZeroOverhead`

Re-run the INT2 assertion ("zero overhead when gate is closed") against the wired CGF subsystem. Use the harness, pump frames, assert manager state.

```csharp
[Fact]
public void CgfSubsystem_HeavyScenario_NoBreakpoints_ZeroOverhead()
{
    int domainId = Interlocked.Increment(ref _domainCounter);
    using var harness = new HrotRunnerHarness("simhost,cgf", domainId);
    
    var mgr = harness.Cgf!.DataBreakpointManager!;
    Assert.False(mgr.HasMountedDelegates);
    
    // Pump 50 ticks without registering any breakpoints
    harness.PumpFrames(50);
    
    // Gate stayed closed — no overhead incurred
    Assert.False(mgr.IsPaused);
    Assert.False(mgr.HasMountedDelegates);
}
```

Check if `HrotRunnerHarness.PumpFrames(int)` exists; if not, look for equivalent pump methods in that class and use the right one.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with ALL tests passing before moving on:**

1. **Add project references** → `dotnet build IOS-IG-SimHost.sln -v quiet` → **0 errors** ✅
2. **Wire EditorSubsystem** → `dotnet build IOS-IG-SimHost.sln -v quiet` → **0 errors** ✅
3. **Wire CgfSubsystem** → `dotnet build IOS-IG-SimHost.sln -v quiet` → **0 errors** ✅
4. **Write tests** → all 5 new tests pass + 103 existing BP tests still green ✅

**Do NOT stop and ask for permission before running tests, fixing compilation errors, or building. Do everything until all tests pass, then write the report.**

---

## 🧪 Testing Requirements

- **5 new tests** in `BreakpointSubsystemWiringTests.cs`
- **103 existing tests** in `Hrot.Diagnostics.Breakpoints.Tests` must remain green
- Run full solution build before submitting: `dotnet build IOS-IG-SimHost.sln -v quiet`

---

## ⚠️ Common Pitfalls to Avoid

1. **Registering systems after `_kernel.Initialize()`** — will throw `InvalidOperationException`. Both `RegisterGlobalSystem` calls must happen BEFORE `_kernel.Initialize()`.

2. **Missing component registrations on `_bpPreTickSnapshot`** — the snapshot repo must have the same schema as `_liveRepo` for `SyncFrom` to copy data correctly. Mirror every `RegisterComponent` / `RegisterManagedComponent` / `RegisterEvent` call that `_world` or `_context.World` receives before the kernel initialises.

3. **CGF `MasterSyncController` access** — `HrotNodeContext` may not expose it directly. Check `HrotNodeContext.cs` source. If it's not exposed, look at whether `_clusterTimeAdapter` wraps it or if the kernel's time controller can be cast to `MasterSyncController`.

4. **Domain ID conflicts in integration tests** — check all existing `_domainCounter` values across CgfSubsystemHeadlessTests, EqsDistributedTests, etc. Use a value ≥230 and verify it doesn't overlap.

5. **`EventScannerCompiler` constructor** — check its signature in `Hrot.Diagnostics.Breakpoints` — it may accept `(IComponentEditService)` only or also `(IComponentEditService, BehaviorRegistry?)`. Use the overload that matches the existing test patterns (see `DataBreakpointManagerTests.cs` line 73).

6. **`PredicateCompiler` needs BehaviorRegistry for trace-buffer scans** — pass `_behaviorRegistry` (available after it's initialised in step §3 of `EditorSubsystem.Initialize()`). For CGF, pass the CGF behavior registry once it's available. If it's initialised later in the method, move the BP wiring block to after behavior registry construction.

---

## 📊 Report Requirements

`.dev/breakpoints-1/reports/BATCH-48-REPORT.md`

Include:

**Q1:** What issues did you encounter wiring the BP stack into each subsystem? How did you resolve them?

**Q2:** What was the exact property/method name for `MasterSyncController` in `HrotNodeContext`? What did you have to do to access it?

**Q3:** Which component registries did you end up mirroring onto `_bpPreTickSnapshot` for CGF? Did any differ from what you expected?

**Q4:** Did you spot any weak points in the subsystem initialization sequence that could bite future BP users?

**Q5:** Suggested commit message for this batch.

---

## 🎯 Success Criteria

This batch is DONE when:

- [ ] `Hrot.Editor.csproj` and `Hrot.CGF.csproj` both reference `Hrot.Diagnostics.Breakpoints` and `Hrot.Blueprints.Editor`
- [ ] `EditorSubsystem.Initialize()` constructs + registers `DebugSnapshotProvider` and `DataBreakpointSystem` with the kernel
- [ ] `CgfSubsystem.Initialize()` does the same via `_context.Kernel`
- [ ] Both subsystems expose `internal IDataBreakpointManager? DataBreakpointManager` test hook
- [ ] 5 new tests in `BreakpointSubsystemWiringTests.cs` pass
- [ ] 103 existing BP tests still pass
- [ ] `dotnet build IOS-IG-SimHost.sln -v quiet` → 0 errors, 0 new warnings

---

## 📚 Reference Materials

- **Task Defs:** [TASK-DETAIL.md](../TASK-DETAIL.md) — §UBP-P10T1, §UBP-P10T2
- **Design:** `.dev/breakpoints-1/DESIGN.md` — §5, §11.1, §11.4
- **DataBreakpointManager ctor:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` lines 55–80
- **Existing tests that show how to construct the stack:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/IntegrationTests.cs` lines 50–60
- **HrotNodeContext source:** search for `class HrotNodeContext` in `Hrot/` to find the property for `MasterSyncController`
