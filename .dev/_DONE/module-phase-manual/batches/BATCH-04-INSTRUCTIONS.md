# BATCH-04: Phase 4 - SystemPhase.Manual

**Batch Number:** BATCH-04  
**Tasks:** MPM-P4-T01, MPM-P4-T02, MPM-P4-T03, MPM-P4-T04, MPM-P4-T05  
**Phase:** Phase 4 - SystemPhase.Manual  
**Estimated Effort:** 6-8 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01, BATCH-02, BATCH-03 completed

---

## Onboarding & Workflow

### Developer Instructions
This batch introduces `SystemPhase.Manual` as a first-class enum value so that modules managing their own execution order (like `AutonomousPerceptionModule`) can still register their systems with the kernel for diagnostics and profiling. The core mechanism is a `RegisterManualSystem` API that wraps the system in a profiling shim while storing it under the `Manual` phase bucket in the scheduler. The kernel skips `Manual` in its automatic phase runner, so the module retains full manual control over tick order and bus swapping.

**Complete tasks in sequence.** Each task builds on the previous one.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Task Details:** `.dev/module-phase-manual/TASK-DETAIL.md` - MPM-P4-T01 through MPM-P4-T05
3. **Design Document:** `.dev/module-phase-manual/DESIGN.md` - Sections 4.1 through 4.6 (read all of Phase 4)

### Key Source Files
- `FDP/Engine/Fdp.ModuleHost/Abstractions/SystemPhase.cs` - Target for T01
- `FDP/Engine/Fdp.ModuleHost/Abstractions/ISystemRegistry.cs` - Target for T02
- `FDP/Engine/Fdp.ModuleHost/Scheduling/SystemScheduler.cs` - Target for T02/T03 (profile wrapper)
- `FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs` - Target for T03 (CapturingSystemRegistry)
- `FDP/Toolkits/Fdp.Toolkits/Perception/Systems/LocalGridBuilderSystem.cs` - Target for T04
- `FDP/Toolkits/Fdp.Toolkits/Perception/Systems/VisionBroadphaseSystem.cs` - Target for T04
- `FDP/Toolkits/Fdp.Toolkits/Perception/Systems/LosRequestBatchingSystem.cs` - Target for T04
- `FDP/Toolkits/Fdp.Toolkits/Perception/Systems/SensorTrackDebounceSystem.cs` - Target for T04
- `FDP/Toolkits/Fdp.Toolkits/Perception/Modules/AutonomousPerceptionModule.cs` - Target for T05
- `Hrot/Subsystems/Hrot.SimHost/SimHostCoreLogicPack.cs` - Companion change for T05

### Test Projects
- `FDP/Engine/Fdp.ModuleHost.Tests/` - Run after T01/T02/T03
- `FDP/Toolkits/Fdp.Toolkits.Tests/` - Run after T05

### Report Submission
**Submit your report to:** `.dev/module-phase-manual/reports/BATCH-04-REPORT.md`  
**Questions to:** `.dev/module-phase-manual/questions/BATCH-04-QUESTIONS.md`

---

## Context

`AutonomousPerceptionModule` currently creates its four systems in the constructor and ticks them directly in `Tick`. They are invisible to the `ArchitectureDiagnosticsPanel` and profiling tools because they are never registered with the kernel's `SystemScheduler`. After this batch:
- `SystemPhase.Manual = 255` exists and the kernel skips it automatically
- `RegisterManualSystem<T>` returns a profiling wrapper
- The four perception systems appear in the diagnostics UI under `Manual`
- `AutonomousPerceptionModule.Tick` order and bus swaps are unchanged

---

## MANDATORY WORKFLOW

Build after EVERY task. Do NOT proceed to the next task until the build is green.

```
T01 → build ✅ → T02 → build + ModuleHost tests ✅ → T03 → build ✅ → T04 → build ✅ → T05 → build + all tests ✅
```

**DO NOT stop to ask for permission. Fix compile errors autonomously.**

---

## Tasks

### Task 1: Add SystemPhase.Manual to Enum and Guard ExecutePhase (MPM-P4-T01)

**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 4.1  
**Task Detail:** `.dev/module-phase-manual/TASK-DETAIL.md` § MPM-P4-T01

**File to modify:** `FDP/Engine/Fdp.ModuleHost/Abstractions/SystemPhase.cs`

1. Add the new enum value (use the exact XML doc from DESIGN.md § 4.1):
   ```csharp
   /// <summary>
   /// Explicitly excluded from the kernel's automatic phase execution.
   /// Systems in this phase are registered for diagnostics and profiling
   /// but must be manually ticked by their owning module.
   /// </summary>
   Manual = 255
   ```

2. **Guard `ExecutePhase`:** Find the `ExecutePhase` method (in `SystemScheduler.cs` or wherever it lives). Add a guard at the top that returns immediately if the phase is `SystemPhase.Manual`:
   ```csharp
   if (phase == SystemPhase.Manual) return;
   ```
   This ensures even if someone calls `kernel.ExecutePhase(SystemPhase.Manual, ...)` it is a safe no-op.

**Verify:**
- `dotnet build IOS-IG-SimHost.sln` passes.
- The enum value `Manual = 255` is present.
- The guard in `ExecutePhase` returns early for `SystemPhase.Manual`.

---

### Task 2: Add RegisterManualSystem to ISystemRegistry and Implement in SystemScheduler (MPM-P4-T02)

**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 4.2, 4.3  
**Task Detail:** `.dev/module-phase-manual/TASK-DETAIL.md` § MPM-P4-T02

**Step A - Interface:** `FDP/Engine/Fdp.ModuleHost/Abstractions/ISystemRegistry.cs`

Add (using exact XML doc from DESIGN.md § 4.2):
```csharp
/// <summary>
/// Registers a system in the Manual phase for diagnostics tracking.
/// Returns a profiled wrapper. The module must tick the wrapper manually
/// so execution time is recorded in the kernel's profiling UI.
/// </summary>
IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem;
```

**Step B - Scheduler:** `FDP/Engine/Fdp.ModuleHost/Scheduling/SystemScheduler.cs`

Add `RegisterManualSystem<T>` method and a private nested `ProfiledManualSystemWrapper` class.

The method must:
1. Call `RegisterSystem(system)` to register the system under `SystemPhase.Manual` (since it carries `[UpdateInPhase(SystemPhase.Manual)]` — see T04 — this just works)
2. Return `new ProfiledManualSystemWrapper(system, this)`

The `ProfiledManualSystemWrapper`:
- `private sealed class ProfiledManualSystemWrapper : IEcsModuleSystem`
- Constructor: `(IEcsModuleSystem inner, SystemScheduler scheduler)`
- `Execute(ISimulationView view, float deltaTime)`:
  - Gets profile data: `var profile = _scheduler.GetProfileData(_inner);`
  - Uses `Stopwatch.StartNew()`, calls `_inner.Execute(view, deltaTime)` in try/finally, records `profile?.RecordExecution(sw.Elapsed.TotalMilliseconds)` in finally

Read `SystemScheduler.cs` carefully first to understand how `RegisterSystem` and `GetProfileData` work before implementing.

**Verify:**
- `dotnet build IOS-IG-SimHost.sln` passes.
- `dotnet test FDP/Engine/Fdp.ModuleHost.Tests/Fdp.ModuleHost.Tests.csproj --no-build` passes.

---

### Task 3: Update CapturingSystemRegistry in ModuleHostKernel (MPM-P4-T03)

**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 4.4  
**Task Detail:** `.dev/module-phase-manual/TASK-DETAIL.md` § MPM-P4-T03

**File to modify:** `FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs`

Find the private nested class `CapturingSystemRegistry` (~line 1815). It implements `ISystemRegistry` and wraps `SystemScheduler`. Add:

```csharp
public IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem
{
    Captured.Add(system);
    return _scheduler.RegisterManualSystem(system);
}
```

Read the surrounding code to understand the `Captured` list and `_scheduler` field names — they may differ slightly.

**Verify:**
- `dotnet build IOS-IG-SimHost.sln` passes.
- No interface implementation errors.

---

### Task 4: Tag Four Perception Systems with [UpdateInPhase(SystemPhase.Manual)] (MPM-P4-T04)

**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 4.5  
**Task Detail:** `.dev/module-phase-manual/TASK-DETAIL.md` § MPM-P4-T04

The `SystemScheduler.RegisterSystem<T>` reads `[UpdateInPhase(...)]` to determine a system's phase. If a system lacks this attribute, the scheduler will throw or default to wrong behavior. Add the attribute to these four files:

- `FDP/Toolkits/Fdp.Toolkits/Perception/Systems/LocalGridBuilderSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits/Perception/Systems/VisionBroadphaseSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits/Perception/Systems/LosRequestBatchingSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits/Perception/Systems/SensorTrackDebounceSystem.cs`

In each file, find the class declaration and add the attribute:
```csharp
[UpdateInPhase(SystemPhase.Manual)]
```

Ensure the required `using` directive is present if needed.

**Verify:**
- `dotnet build IOS-IG-SimHost.sln` passes.
- All four systems have the attribute.

---

### Task 5: Refactor AutonomousPerceptionModule + Update SimHostCoreLogicPack (MPM-P4-T05)

**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 4.6  
**Task Detail:** `.dev/module-phase-manual/TASK-DETAIL.md` § MPM-P4-T05

**IMPORTANT:** Read both files in full before making any changes. The module has constructor logic, field declarations, `RegisterSystems`, and `Tick`. Understand the existing bus swap order in `Tick` before touching anything.

**File 1: `FDP/Toolkits/Fdp.Toolkits/Perception/Modules/AutonomousPerceptionModule.cs`**

1. Change the four private system fields from concrete types to `IEcsModuleSystem`, initialized to `null!`:
   ```csharp
   private IEcsModuleSystem _localGridBuilder    = null!;
   private IEcsModuleSystem _visionBroadphase    = null!;
   private IEcsModuleSystem _losRequestBatching  = null!;
   private IEcsModuleSystem _sensorTrackDebounce = null!;
   ```

2. Remove the concrete system instantiations from the constructor (they will move to `RegisterSystems`).

3. In `RegisterSystems(ISystemRegistry registry)`, create each system and register it via `RegisterManualSystem`:
   ```csharp
   _localGridBuilder    = registry.RegisterManualSystem(new LocalGridBuilderSystem(_localGrid));
   _visionBroadphase    = registry.RegisterManualSystem(new VisionBroadphaseSystem(_localGrid));
   _losRequestBatching  = registry.RegisterManualSystem(new LosRequestBatchingSystem(
                              mockMode: false, colliderRadiusReader: _colliderRadiusReader));
   _sensorTrackDebounce = registry.RegisterManualSystem(new SensorTrackDebounceSystem());
   ```
   Check the actual constructor signatures of each system before writing this code!

4. In `Tick`, replace concrete field calls with wrapper calls. **Preserve the exact bus swap order** - do NOT change when `_scopedBus.SwapBuffers()` is called relative to each system execution.

**Wrapper casting rule:** The `IEcsModuleSystem` fields must NEVER be downcast to the concrete type. If any code outside `Tick` accesses system-specific properties, use the dual-reference pattern (see DESIGN.md § 4.6 and TASK-DETAIL.md § MPM-P4-T05). For the current module, this is NOT needed since only `Execute(view, dt)` is called in `Tick`.

**File 2: `Hrot/Subsystems/Hrot.SimHost/SimHostCoreLogicPack.cs`**

`AutonomousPerceptionModule` is a private field inside `SimHostCoreLogicPack`. For `RegisterManualSystem` calls inside `AutonomousPerceptionModule.RegisterSystems` to reach the kernel's scheduler, `SimHostCoreLogicPack.RegisterSystems` MUST forward the registry:

```csharp
public void RegisterSystems(ISystemRegistry registry)
{
    // ... existing registrations ...
    _perceptionModule.RegisterSystems(registry); // ADD THIS LINE
}
```

Without this forwarding call, the perception systems are never registered and remain invisible.

**Verify:**
- `AutonomousPerceptionModule.RegisterSystems` calls `RegisterManualSystem` four times.
- `AutonomousPerceptionModule.Tick` bus swap order unchanged.
- `SimHostCoreLogicPack.RegisterSystems` forwards to `_perceptionModule.RegisterSystems(registry)`.
- `dotnet build IOS-IG-SimHost.sln` passes with 0 errors.
- `dotnet test IOS-IG-SimHost.sln --no-build` - same pass/fail as BATCH-03 baseline (130 pass, 10 pre-existing integration failures, 4 pre-existing Hrot.IG.Tests failures).

---

## Testing Requirements

1. **After T01:** `dotnet build IOS-IG-SimHost.sln`
2. **After T02:** `dotnet build IOS-IG-SimHost.sln` + `dotnet test FDP/Engine/Fdp.ModuleHost.Tests/Fdp.ModuleHost.Tests.csproj --no-build`
3. **After T03:** `dotnet build IOS-IG-SimHost.sln`
4. **After T04:** `dotnet build IOS-IG-SimHost.sln`
5. **After T05 (final):** `dotnet test IOS-IG-SimHost.sln --no-build`

---

## Report Requirements

Submit to `.dev/module-phase-manual/reports/BATCH-04-REPORT.md`.

```markdown
# BATCH-04 Report

## Completion Status
- [ ] MPM-P4-T01: Add SystemPhase.Manual + guard ExecutePhase
- [ ] MPM-P4-T02: RegisterManualSystem in ISystemRegistry + ProfiledManualSystemWrapper in SystemScheduler
- [ ] MPM-P4-T03: Update CapturingSystemRegistry in ModuleHostKernel
- [ ] MPM-P4-T04: Tag four perception systems with [UpdateInPhase(SystemPhase.Manual)]
- [ ] MPM-P4-T05: Refactor AutonomousPerceptionModule + SimHostCoreLogicPack forwarding

## Build Status
[Result of: dotnet build IOS-IG-SimHost.sln]

## Test Status
[Results of ModuleHost tests and full solution sweep]

## Developer Insights

**Q1:** What was the exact structure of SystemScheduler you found? How did RegisterSystem/GetProfileData work?

**Q2:** Did the AutonomousPerceptionModule constructor need any additional cleanup beyond removing system instantiations?

**Q3:** Did the bus swap order in Tick change at all? Describe the final Tick order.

**Q4:** Were there any places that accessed the concrete system fields outside of Tick that needed the dual-reference pattern?

**Q5:** What did you find in SimHostCoreLogicPack.RegisterSystems - was the forwarding call already partially present or entirely missing?

## Suggested Commit Message
[Your commit message suggestion]
```

---

## Success Criteria

- [ ] `SystemPhase.Manual = 255` exists in `SystemPhase.cs`
- [ ] `ExecutePhase` skips when `phase == SystemPhase.Manual`
- [ ] `ISystemRegistry.RegisterManualSystem<T>` interface method exists
- [ ] `SystemScheduler.RegisterManualSystem<T>` implementation exists with `ProfiledManualSystemWrapper`
- [ ] `CapturingSystemRegistry.RegisterManualSystem<T>` in `ModuleHostKernel` exists
- [ ] Four perception systems have `[UpdateInPhase(SystemPhase.Manual)]`
- [ ] `AutonomousPerceptionModule` fields are `IEcsModuleSystem`, filled via `RegisterManualSystem` in `RegisterSystems`
- [ ] `SimHostCoreLogicPack.RegisterSystems` forwards to `_perceptionModule.RegisterSystems(registry)`
- [ ] `dotnet build IOS-IG-SimHost.sln` - 0 errors
- [ ] ModuleHost tests pass
- [ ] Full solution test count unchanged from BATCH-03 baseline
- [ ] Report submitted

---

## Common Pitfalls

- **ExecutePhase guard placement:** The guard `if (phase == SystemPhase.Manual) return;` must be at the TOP of the method, before any iterator or loop over registered systems.
- **ProfiledManualSystemWrapper GetProfileData:** The method may return null for newly-registered systems if called before the first execution. Use `profile?.RecordExecution(...)` (null-conditional).
- **Bus swap order in Tick:** DO NOT reorder `_scopedBus.SwapBuffers()` calls. The swap order was tuned for the LOS pipeline. Any reordering would be a behavioral regression.
- **AutonomousPerceptionModule constructor:** After removing system instantiations from the constructor, check if any other setup code in the constructor that depended on those instances also needs updating.
- **SimHostCoreLogicPack:** The forwarding call `_perceptionModule.RegisterSystems(registry)` must be INSIDE the `RegisterSystems` override, not in the constructor or anywhere else.
- **`[UpdateInPhase]` attribute import:** The four perception systems will need the correct `using` for `SystemPhase` and `UpdateInPhase` attribute.

---

## Reference Materials
- **Design:** `.dev/module-phase-manual/DESIGN.md` §§ 4.1-4.6
- **Task Details:** `.dev/module-phase-manual/TASK-DETAIL.md` §§ MPM-P4-T01 through MPM-P4-T05
- **Previous Reviews:** `.dev/module-phase-manual/reviews/BATCH-03-REVIEW.md`
