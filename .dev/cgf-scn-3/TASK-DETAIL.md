# CGF Scenario Fix 3 — Task Detail

**Reference:** See [DESIGN.md](./DESIGN.md) for full architectural context.

---

## Phase 1 — Core ECS Correctness

---

### TASK-S301 — Fix SetManagedComponent / RemoveManagedComponent for ActiveMissionPlan

**Design Reference:** [DESIGN.md § 1.1](./DESIGN.md#11-managed-component-api-fix-in-missioncontrolexecutionsystem)

**Scope**

Fix the two incorrect ECS API calls in `MissionControlExecutionSystem.ProcessIntent` that use
the unmanaged struct API on a managed class.

NOT included: test changes beyond the unit tests below. NOT included: any changes to
`ActiveMissionPlan` itself.

**Constraints**

- `ActiveMissionPlan` is a managed `class`. The unmanaged `SetComponent<T>` and
  `RemoveComponent<T>` APIs are restricted to `where T : unmanaged`. Calling them on a class type
  silently bypasses the `ManagedComponentTable` and the component becomes invisible to
  `repo.HasManagedComponent<T>`.
- Do not change the order of the `repo.SetComponent(entity, queue)` call — `MissionPlanQueue` is
  an unmanaged struct and the call is correct.
- `SmartEgressUtil.MarkDirty` call must remain immediately after `SetManagedComponent`.

**Files to Modify**

- `Hrot/Engine/Hrot.Common/Systems/MissionControlExecutionSystem.cs`

**Changes**

In the `CMD_REPLACE_MISSION` case (~line 172):
```csharp
// BEFORE:
repo.SetComponent(entity, new ActiveMissionPlan { Plan = domainPlan });
// AFTER:
repo.SetManagedComponent(entity, new ActiveMissionPlan { Plan = domainPlan });
```

In the `CMD_ABORT_ALL` case (~line 213):
```csharp
// BEFORE:
repo.RemoveComponent<ActiveMissionPlan>(entity);
// AFTER:
repo.RemoveManagedComponent<ActiveMissionPlan>(entity);
```

**Success Conditions**

SC1 — After `CMD_REPLACE_MISSION` is processed, `repo.HasManagedComponent<ActiveMissionPlan>(entity)` returns `true`.

SC2 — After `CMD_REPLACE_MISSION`, `repo.GetManagedComponent<ActiveMissionPlan>(entity)` is not null and `Plan.Tasks` has the expected count.

SC3 — After `CMD_ABORT_ALL`, `repo.HasManagedComponent<ActiveMissionPlan>(entity)` returns `false`.

SC4 — Existing tests in `Hrot.Common` / `Hrot.SimHost.Tests` that exercise `MissionControlExecutionSystem` continue to pass.

---

### TASK-S302 — Fix InlineArray Span Mutation in TryBuildQueue

**Design Reference:** [DESIGN.md § 1.2](./DESIGN.md#12-inlinearray-span-mutation-fix-in-trybuildqueue)

**Scope**

Fix the `TryBuildQueue` method in `MissionControlExecutionSystem` to avoid the `[InlineArray]`
defensive-copy trap when writing to `queue.Phases`.

NOT included: any changes to `MissionPlanQueue` or `MissionPhaseBuffer` themselves.

**Constraints**

- The fix must use the `Span<MissionPhase> phases = queue.Phases;` pattern documented in the
  `MissionPhaseBuffer` type remarks. This is the only pattern guaranteed safe when the queue is
  an `out` parameter.
- `queue.PhaseCount = (byte)count` must remain AFTER the loop, not inside it.
- The `orderedTaskIds` population must remain correct — do not change the `orderedTaskIds.Add`
  line or the `FollowRoute` translation block.

**Files to Modify**

- `Hrot/Engine/Hrot.Common/Systems/MissionControlExecutionSystem.cs`

**Changes**

In `TryBuildQueue`, before the for-loop, add:
```csharp
Span<MissionPhase> phases = queue.Phases;
```
Replace every `queue.Phases[i] = new MissionPhase { ... }` with `phases[i] = new MissionPhase { ... }`.

**Success Conditions**

SC1 — After processing a `CMD_REPLACE_MISSION` intent with a 3-task plan, `repo.GetComponent<MissionPlanQueue>(entity).PhaseCount` equals 3.

SC2 — Each phase `phases[0]`, `phases[1]`, `phases[2]` has the correct `DoctrineId`, `Trigger`, and `TriggerParam` matching the input tasks.

SC3 — A `CMD_REPLACE_MISSION` with a 0-task plan produces a queue where `PhaseCount == 0`.

SC4 — Existing tests that indirectly exercise `TryBuildQueue` continue to pass.

---

### TASK-S303 — Add DataPolicy.NoSave to BrainBlackboard

**Design Reference:** [DESIGN.md § 1.3](./DESIGN.md#13-brainblackboard-data-policy)

**Scope**

Add `[DataPolicy(DataPolicy.NoSave)]` to the `BrainBlackboard` struct. Verify that the attribute
is the correct one (same as already applied to `LocomotionChannel`, `WeaponChannel`, and
`InteractionChannel` in `ChannelComponents.cs`).

NOT included: any change to `CheckpointIOWorker` — `DataPolicy.NoSave` does not affect binary
checkpoint recording.

**Constraints**

- Use `DataPolicy.NoSave` (not `DataPolicy.Transient`, not `DataPolicy.NoRecord`). The blackboard
  must still appear in LZ4 checkpoint payloads for binary rollback.
- The attribute must be on the struct declaration, not on a field.
- Do not change `BrainBlackboardByteSize` or the `fixed byte Memory[]` field.

**Files to Modify**

- `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs`

**Changes**

```csharp
// BEFORE:
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.BrainBlackboard)]
public unsafe struct BrainBlackboard

// AFTER:
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.BrainBlackboard)]
[DataPolicy(DataPolicy.NoSave)]
public unsafe struct BrainBlackboard
```

**Success Conditions**

SC1 — After saving a scenario that contains an entity with an active doctrine, the resulting JSON
does not contain any `"BrainBlackboard"` key.

SC2 — `FdpAutoSerializer.Serialize` output for a world containing a `BrainBlackboard` component
does not include the component's data.

SC3 — A binary checkpoint (`CheckpointIOWorker`) taken from the same world still contains the
`BrainBlackboard` bytes (checkpoint uses a separate serialization path).

SC4 — Existing scenario round-trip tests pass.

---

### TASK-S304 — Fix SteppingTimeController.GetMode()

**Design Reference:** [DESIGN.md § 1.4](./DESIGN.md#14-steppingtimecontroller-mode-reporting)

**Scope**

Fix the `GetMode()` method in `SteppingTimeController` to return `TimeMode.Deterministic`
instead of `TimeMode.Continuous`.

NOT included: any change to `MasterSyncController.GetMode()` — that controller correctly returns
`Deterministic` only when in the internal `Stepping` state.

**Constraints**

- The comment on the current return statement ("Or add TimeMode.Stepping? treating as continuous
  mode compatible") is the documented acknowledgment of the bug. Remove it.
- Do not change any other method in `SteppingTimeController`.

**Files to Modify**

- `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/SteppingTimeController.cs`

**Changes**

```csharp
// BEFORE:
public TimeMode GetMode()
{
    return TimeMode.Continuous; // Or add TimeMode.Stepping? treating as continuous mode compatible
}

// AFTER:
public TimeMode GetMode()
{
    return TimeMode.Deterministic;
}
```

**Success Conditions**

SC1 — `new SteppingTimeController(new GlobalTime()).GetMode()` returns `TimeMode.Deterministic`.

SC2 — Existing `SteppingTimeController` unit tests (time advancement, scaling, seed/reset) pass.

---

## Phase 2 — CGF Multi-Phase Architecture

---

### TASK-S305 — MissionControlModule Two-Group Registration Overload

**Design Reference:** [DESIGN.md § 2.1](./DESIGN.md#21-missioncontrolmodule-phase-split)

**Scope**

Add `RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup)` overload to
`MissionControlModule`. The existing single-group overload must remain.

**Constraints**

- `DoctrineIngressSystem` routes to `inputGroup` — it parses `AssignDoctrineHashEvent` (an
  input-phase event) and must not run after the Simulation phase starts populating its read
  buffer.
- `MissionDirectorSystem` routes to `simGroup` — it reads `GlobalTime.DeltaTime` and must run
  after physics.
- Null checks on both parameters are required.

**Files to Modify**

- `FDP/Toolkits/Fdp.Toolkits/Behavior/Modules/MissionControlModule.cs`

**Changes**

Add the new overload:
```csharp
public void RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup)
{
    if (inputGroup == null) throw new ArgumentNullException(nameof(inputGroup));
    if (simGroup   == null) throw new ArgumentNullException(nameof(simGroup));

    inputGroup.AddSystem(new DoctrineIngressSystem(_registry));
    simGroup.AddSystem(new MissionDirectorSystem());
}
```

**Success Conditions**

SC1 — Calling the new overload with two non-null groups adds exactly one `DoctrineIngressSystem`
to `inputGroup` and exactly one `MissionDirectorSystem` to `simGroup`.

SC2 — Calling the existing `RegisterSystems(SystemGroup group)` single-group overload still
adds both systems to the same group (no regression).

SC3 — `ArgumentNullException` is thrown if either parameter is null.

SC4 — Existing `MissionControlModuleTests` pass.

---

### TASK-S306 — CgfLogicPack Two-Group Registration Overload

**Design Reference:** [DESIGN.md § 2.2](./DESIGN.md#22-cgflogicpack-phase-split)

**Scope**

Add `RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup)` overload to `CgfLogicPack`.
The existing `RegisterSystems(SystemGroup simGroup)` single-group overload must remain unchanged.

**Constraints**

- `_missionExecutionSystem` routes to `inputGroup` only.
- `_missionAdapterSystem` routes to `simGroup` only.
- `_missionControlModule.RegisterSystems(inputGroup, simGroup)` uses the new Phase 2.1 overload.
- `HealthApplicationSystem`, `CgfThreatEvaluationSystem`, and `RouteContextSystem` all stay in
  `simGroup`.
- `_cognitiveRuntimeModule.RegisterSystems(simGroup)` and
  `_actionDispatchModule.RegisterSystems(simGroup)` are unchanged.
- Null checks on both parameters are required.
- The `CgfLogicPackTests` that count systems per group must be updated to account for the split.

**Files to Modify**

- `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs`

**Changes**

Add the new overload with the system routing described above.

**Success Conditions**

SC1 — The new overload routes `MissionControlExecutionSystem` to `inputGroup` and all remaining
systems to `simGroup`.

SC2 — `DoctrineIngressSystem` ends up in `inputGroup` (via `_missionControlModule.RegisterSystems(inputGroup, simGroup)`).

SC3 — `MissionDirectorSystem` ends up in `simGroup`.

SC4 — The existing `RegisterSystems(SystemGroup simGroup)` overload still adds all systems to
the single group.

SC5 — `CgfLogicPackTests` are updated and pass.

---

### TASK-S307 — CgfInputGroupAdapter in Hrot.Common

**Design Reference:** [DESIGN.md § 2.3](./DESIGN.md#23-cgfinputgroupadapter-shared-utility)

**Scope**

Create the `CgfInputGroupAdapter` class in `Hrot.Common`. It wraps a `SystemGroup` and causes it
to run during the kernel's Input phase.

NOT included: any changes to existing files in `Hrot.Common`.

**Constraints**

- Class must be `public sealed`. `Hrot.CGF` and `Hrot.Editor` are not in `Hrot.Common`'s
  `InternalsVisibleTo` list, so the class must be `public` to be accessible from both callers.
- Must carry `[UpdateInPhase(SystemPhase.Input)]` so the kernel's `RegisterGlobalSystem`
  correctly places it in the Input phase dispatch.
- Must implement `IEcsModuleSystem` (Execute-based, not `IEcsModule` Tick-based).
- `Hrot.Common` already has transitive access to `IEcsModuleSystem` via
  `Fdp.Network.Cyclone → Fdp.ModuleHost`. No new project reference is needed.
- Place in namespace `Hrot.Common.Infrastructure` (consistent with other infrastructure types
  in `Hrot.Common`).

**Files to Create**

- `Hrot/Engine/Hrot.Common/Infrastructure/CgfInputGroupAdapter.cs`

**Success Conditions**

SC1 — The class compiles without adding any new project reference to `Hrot.Common.csproj`.

SC2 — `CgfInputGroupAdapter` is accessible from both `Hrot.CGF` and `Hrot.Editor` without
adding `InternalsVisibleTo` entries.

SC3 — `typeof(CgfInputGroupAdapter).GetCustomAttribute<UpdateInPhaseAttribute>().Phase`
equals `SystemPhase.Input`.

SC4 — Calling `Execute(view, dt)` invokes `_group.Run()` exactly once.

---

### TASK-S308 — CgfSubsystem Registration Update

**Design Reference:** [DESIGN.md § 2.4](./DESIGN.md#24-cgfsubsystem-registration-update)

**Scope**

Update `CgfSubsystem.Initialize()` to create a separate `inputGroup`, call the new two-group
`CgfLogicPack.RegisterSystems(inputGroup, simGroup)` overload, and register the input group as a
global Input-phase system. The existing `CgfSimGroupModule` wiring must remain.

NOT included: any change to `CgfSimGroupModule` itself.

**Constraints**

- The `inputGroup` must be created and `Create`d on `_context.World` before it is passed to
  `RegisterSystems`.
- The existing `_simGroup = simGroup; cgfLogicPack.RegisterSystems(simGroup)` lines must be
  replaced — only the new two-group call should remain.
- The existing `_context.Kernel.RegisterModule(new CgfSimGroupModule(_simGroup))` stays.
- Use the shared `CgfInputGroupAdapter` from `Hrot.Common.Infrastructure` (created in TASK-S307).
  Do NOT define a local private adapter class inside `CgfSubsystem`.
- `_context.Kernel.RegisterGlobalSystem(new CgfInputGroupAdapter(inputGroup))` must be called
  after the group is created.
- `using` directive for `Hrot.Common.Infrastructure` must be added if not already present.

**Files to Modify**

- `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`

**Success Conditions**

SC1 — An integration test that spawns an entity and sends a `MissionControlIntent` confirms that
`MissionControlExecutionSystem` processes it in the Input phase (the intent is drained before
the simulation group ticks in the same frame).

SC2 — `DoctrineIngressSystem` runs before `BTreeTickSystem` within the same kernel update.

SC3 — Existing `CgfSubsystem` integration tests that exercise mission assignment pass.

---

## Phase 3 — Editor Composition Root

---

### TASK-S309 — EditorSubsystem System Group Wiring

**Design Reference:** [DESIGN.md § 3.1](./DESIGN.md#31-system-group-wiring-in-editorsubsystem)

**Scope**

Replace the broken `_kernel.RegisterModule(simHostCorePack)` and
`_kernel.RegisterModule(cgfLogicPackInst)` calls in `EditorSubsystem.Initialize()` with explicit
system group construction and registration.

**Constraints**

- Three `SystemGroup` instances (`inputGroup`, `simGroup`, `postSimGroup`) must be created and
  `Create`d on `_world` before any pack calls `RegisterSystems` on them.
- `simHostCorePack.RegisterSystems(inputGroup, simGroup, postSimGroup)` and
  `cgfLogicPackInst.RegisterSystems(inputGroup, simGroup)` must be called in this order.
- The input group is registered via `_kernel.RegisterGlobalSystem(new CgfInputGroupAdapter(inputGroup))`.
- The sim group is registered via a local `SimGroupModule` wrapper (`IEcsModule` with `Tick()` calling `_group.Run()`).
- The post-sim group is registered via a local `PostSimGroupModule` wrapper similarly.
- Both wrapper classes can be private nested classes inside `EditorSubsystem`.
- The `logicPacks` list (`List<IEcsModule> { simHostCorePack, perceptionMod, cgfLogicPackInst }`)
  must not change — `EditorApplication.SwitchToExternalAsync` uses it to unregister/re-register
  packs, not to tick them directly.
- The `_kernel.RegisterModule(perceptionMod)`, `_kernel.RegisterModule(orchPack)`,
  `_kernel.RegisterModule(scenarioMod)`, ELM, and `SimHostModule` registrations are unchanged.

**Files to Modify**

- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

**Success Conditions**

SC1 — After calling `Initialize()` (headless), `_kernel.Update()` without a prior `Step()` does
not throw. Specifically, `MissionControlExecutionSystem` runs and returns immediately (empty bus).

SC2 — After spawning an entity and publishing a `MissionControlIntent` to the bus, pumping one
kernel frame results in `repo.HasManagedComponent<ActiveMissionPlan>(entity)` returning `true`.

SC3 — After the fix, saving a scenario containing an entity with an authored mission produces a
JSON that includes the `ActiveMissionPlan` data under the entity's component array.

SC4 — Existing `EditorSubsystem` headless tests pass.

---

### TASK-S310 — EditorSubsystem MasterSyncController Replacement

**Design Reference:** [DESIGN.md § 3.2](./DESIGN.md#32-editorsubsystem-time-controller-replacement)

**Scope**

Replace `SteppingTimeController` with `MasterSyncController` in `EditorSubsystem`. The editor
must boot into Deterministic (frozen) mode.

**Constraints**

- The field type changes from `SteppingTimeController?` to `MasterSyncController?`.
- `TimeControllerFactory.Create(world.Bus, new TimeControllerConfig { Role = TimeRole.Standalone })`
  returns a `MasterSyncController` — the cast is safe.
- `SwitchToDeterministic(new HashSet<int>())` must be called immediately after `SetTimeController`.
  This places the controller in Stepping mode with no expected slave ACKs, yielding `dt = 0` on
  every `kernel.Update()`.
- `_stepping?.Step(deltaTime)` in `Update()` must be removed entirely.
- `_kernel?.Update()` remains in `Update()` — the kernel internally calls
  `_timeController.Update()` to compute the current `GlobalTime`.
- The `Hrot.Editor` project already references `Fdp.Toolkits` which provides
  `TimeControllerFactory`, `TimeControllerConfig`, and `TimeRole`. No new project reference is
  needed.

**Files to Modify**

- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

**Success Conditions**

SC1 — After `Initialize()`, `_timeController.GetMode()` returns `TimeMode.Deterministic`.

SC2 — Calling `_kernel.Update()` (without any prior `Step`) yields a `GlobalTime.DeltaTime`
of exactly `0.0f` in the ECS world.

SC3 — Kinematics do not advance between two consecutive `kernel.Update()` calls in authoring
mode (entities with non-zero velocity do not move).

SC4 — Existing headless editor tests that do not specifically test time behavior continue to pass.

---

### TASK-S311 — EditorPreviewController Time Mode Wiring

**Design Reference:** [DESIGN.md § 3.3](./DESIGN.md#33-editorpreviewcontroller-time-mode-transitions)

**Scope**

Wire `EditorPreviewController.EnterPreviewMode()` / `ExitPreviewMode()` to call
`SwitchToContinuous()` / `SwitchToDeterministic()` on the `MasterSyncController`.

**Constraints**

- `MasterSyncController` is injected via the constructor, not accessed via a singleton or static.
- `SwitchToContinuous()` is called AFTER `_handler.TriggerLoadingPreview()` (state snapshot must
  exist before physics start running).
- `SwitchToDeterministic(new HashSet<int>())` is called AFTER `_handler.TriggerUnloadingPreview()`.
- The `IsInPreviewMode` property logic is unchanged.

**Files to Modify**

- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (the `EditorPreviewController` nested class
  and its construction site)

**Success Conditions**

SC1 — Calling `EnterPreviewMode()` transitions `_timeController.GetMode()` to
`TimeMode.Continuous`.

SC2 — Calling `ExitPreviewMode()` transitions `_timeController.GetMode()` back to
`TimeMode.Deterministic`.

SC3 — After `ExitPreviewMode()`, `kernel.Update()` yields `GlobalTime.DeltaTime == 0.0f` again.

SC4 — `IsInPreviewMode` returns `true` between enter and exit, and `false` otherwise.

---

### TASK-S312 — EditorHarness Fix

**Design Reference:** [DESIGN.md § 3.4](./DESIGN.md#34-editorharness-fix)

**Scope**

Apply the same composition root fix to `EditorHarness` (the integration test harness). After
Phase 2 changes the `CgfLogicPack` API, the harness will fail to compile if not updated.

**Constraints**

- The fix mirrors §3.1 and §3.2: create groups, call `RegisterSystems(...)`, register with kernel.
- `PumpFrames(int frames)` must call `_timeController.Step(PumpSleepMs / 1000f)` before
  `Kernel.Update()` to advance deterministic time during test frames.
- `PumpUntil(Func<bool> condition, int timeoutMs)` must do the same.
- The `SteppingTimeController _stepping` field is replaced by `MasterSyncController _timeController`.
- The harness boots in Deterministic mode (same as `EditorSubsystem`).
- `SimGroupModule` and `PostSimGroupModule` wrappers must be accessible from `EditorHarness` —
  either define them locally or use the same pattern as `EditorSubsystem`.

**Files to Modify**

- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs`

**Success Conditions**

SC1 — `EditorHarness` compiles after Phase 2 changes.

SC2 — Existing integration tests that use `EditorHarness.PumpFrames` continue to pass.

SC3 — An integration test that uses `PumpUntil(() => repo.HasManagedComponent<ActiveMissionPlan>(entity))` completes successfully after publishing a `MissionControlIntent`.

---

## Phase 4 — Distributed Load Safety

---

### TASK-S313 — StagingEntityExtractor Child Entity Remapping

**Design Reference:** [DESIGN.md § 4.1](./DESIGN.md#41-stagingentityextractor-child-entity-remapping)

**Scope**

Fix `StagingEntityExtractor.Extract` to apply the network ID remapping to child entity component
lists before assembling the `ChildComponentOverrides` dictionary.

NOT included: any change to the root entity remapping (that path is already correct).

**Constraints**

- Extract the existing remapping block (both the `behaviorRemapper` loop and the Intent DTO `for`
  loop) verbatim into a private static method
  `RemapComponentNetworkIds(List<object>, Dictionary<long, long>, ScenarioBehaviorRemapper?)`.
- The signature must accept a nullable `ScenarioBehaviorRemapper?` — child entities may or may
  not carry `ActiveMissionPlan` components.
- Call the helper for the root `comps` list where the inline code was.
- In the `childBuffer` assembly block (inside the `if (childBuffer.TryGetValue(...))` branch),
  iterate `harvested` and call `RemapComponentNetworkIds` on each child's `components` list
  BEFORE calling `overrideDict[kvp.Key] = (...)`.
- The logic for casting `kvp.Value.components` to `List<object>` must be verified — the harness
  stores `List<object>` but the type exposed through the interface is `IReadOnlyList<object>`. Use
  the actual stored type to avoid unnecessary copies.
- The EpisodeTag append and `childBuffer` lookup logic are unchanged.

**Files to Modify**

- `Hrot/Subsystems/Hrot.CGF/Orchestration/StagingEntityExtractor.cs`

**Success Conditions**

SC1 — Given a scenario JSON containing a multi-part entity (root + one child) where the child
carries an `InitialPassengersIntent` with a passenger network ID equal to `oldId`, after
extraction the child's `InitialPassengersIntent.PassengerNetworkIds` contains `newId` (from the
`oldToNewMap`) and not `oldId`.

SC2 — Given a child entity that carries an `ActiveMissionPlan` whose `BehaviorParams` JSON
contains the string representation of `oldId`, after extraction the `BehaviorParams` contains
`newId` (the `behaviorRemapper` was applied to the child).

SC3 — Root entity components continue to be remapped correctly (no regression on the existing
path).

SC4 — Existing `StagingEntityExtractor` unit tests pass.

SC5 — An extractor run with `behaviorRemapper = null` does not throw a NullReferenceException
for child entities.
