# BATCH-01: Project Scaffolding + SharedApplicationBootstrapper

**Batch Number:** BATCH-01  
**Tasks:** SM-001 (Project Scaffolding), SM-002 (SharedApplicationBootstrapper)  
**Phase:** Phase 1 + Phase 2  
**Estimated Effort:** 12-16 hours  
**Priority:** HIGH  
**Dependencies:** None (first batch)

---

## Mandatory Workflow — Complete Without Stopping

**YOU MUST FINISH THIS BATCH COMPLETELY BEFORE WRITING THE REPORT.**  
Do not ask for permission to run tests. Do not stop after implementing to "check if this is the right approach".  
Fix all build errors and test failures before submitting. No partial work is acceptable.

**Task Progression:**

1. **Task SM-001 (Scaffolding):** Create projects → wire solution → `dotnet build` succeeds ✅  
2. **Task SM-002 (Bootstrapper):** Implement class → write all tests → ALL tests pass ✅

Do NOT move to the next task until the current one compiles and all its tests pass.

---

## Onboarding & Workflow

### Required Reading (IN ORDER)
1. `.dev/.guides/DEV-GUIDE.md` — project conventions and dev workflow
2. `.dev/stride-mock/DESIGN.md` — full architecture (read §1–§4 before coding)
3. `.dev/stride-mock/TASK-DETAILS.md` — see SM-001, SM-002 sections with success conditions
4. `.dev/stride-mock/ONBOARDING.md` — folder layout overview

### Source Code Locations
- **New library project:** `Hrot\Subsystems\Hrot.StrideMock\` (create it)
- **New app project:** `Hrot\Runner\Hrot.FakeStrideApp\` (create it)
- **Bootstrapper target:** `Hrot\Engine\Hrot.Common\Infrastructure\SharedApplicationBootstrapper.cs` (create it)
- **HrotNodeBuilder (reference):** `Hrot\Engine\Hrot.Common\Infrastructure\HrotNodeBuilder.cs`
- **SimHostApp (reference pattern):** `Hrot\Subsystems\Hrot.SimHost\SimHostApp.cs`
- **NodeBootstrapper (reference):** `Hrot\Subsystems\Hrot.SimHost\NodeBootstrapper.cs`
- **SimHostApp tests (reference for patterns):** `Hrot\Subsystems\Hrot.SimHost.Tests\`
- **Solution file:** `IOS-IG-SimHost.sln` (root of repo)

### Report Submission
**When done, submit your report to:**  
`.dev/stride-mock/reports/BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev/stride-mock/questions/BATCH-01-QUESTIONS.md`

---

## Context

This batch lays the entire foundation. SM-001 creates the two new C# projects and wires them into the solution and `ClusterRunner`. SM-002 implements the abstract `SharedApplicationBootstrapper` in `Hrot.Common.Infrastructure` — the Template Method base class that all node bootstrappers (StrideMock, SimHost, IG) will share.

**SM-002 is the most complex task. Read DESIGN.md §4 thoroughly before writing a single line of code.**

---

## Task 1: SM-001 — Create Project Scaffolding

**Reference:** [TASK-DETAILS.md SM-001](../TASK-DETAILS.md#sm-001--create-project-scaffolding)

Create two new C# projects and wire them into the solution and `ClusterRunner`.

### 1.1 Hrot.StrideMock (Class Library)

**File:** `Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.csproj` (NEW)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <RootNamespace>Hrot.StrideMock</RootNamespace>
    <AssemblyName>Hrot.StrideMock</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <!-- SharedApplicationBootstrapper, HrotNodeBuilder, MapCamera -->
    <ProjectReference Include="..\..\Engine\Hrot.Common\Hrot.Common.csproj" />
    <!-- Domain modules: SimHostComponentRegistry, KinematicComponentRegistry,
         GroundKinematicsModule, CombatModule, CognitiveSpatialModule, NavigationSolverModule -->
    <ProjectReference Include="..\Hrot.SimHost\Hrot.SimHost.csproj" />
    <!-- FDP kernel -->
    <ProjectReference Include="..\..\..\FDP\Engine\Fdp.Core\Fdp.Core.csproj" />
    <!-- Raylib/ImGui shell -->
    <ProjectReference Include="..\..\..\FDP\Engine\Fdp.Presentation\Fdp.Presentation.csproj" />
    <!-- Toolkits (FdpApplication, ISubsystem, etc.) -->
    <ProjectReference Include="..\..\..\FDP\Toolkits\Fdp.Toolkits\Fdp.Toolkits.csproj" />
  </ItemGroup>
</Project>
```

Add a placeholder class so the project compiles:
- `Hrot\Subsystems\Hrot.StrideMock\StrideMockPlaceholder.cs` — empty `namespace Hrot.StrideMock { }` (remove once real classes exist)

### 1.2 Hrot.FakeStrideApp (Executable)

**File:** `Hrot\Runner\Hrot.FakeStrideApp\Hrot.FakeStrideApp.csproj` (NEW)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <RootNamespace>Hrot.FakeStrideApp</RootNamespace>
    <AssemblyName>Hrot.FakeStrideApp</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Subsystems\Hrot.StrideMock\Hrot.StrideMock.csproj" />
    <ProjectReference Include="..\..\..\FDP\Engine\Fdp.Presentation\Fdp.Presentation.csproj" />
    <ProjectReference Include="..\..\Network\Hrot.Network.NED\Hrot.Network.NED.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Raylib-cs" Version="7.0.2" />
    <PackageReference Include="rlImGui-cs" Version="3.2.0" />
  </ItemGroup>
</Project>
```

Add a minimal `Program.cs` stub so the project compiles:
```csharp
// Hrot\Runner\Hrot.FakeStrideApp\Program.cs
namespace Hrot.FakeStrideApp;

class Program
{
    static void Main(string[] args)
    {
        // Stub - implementation in SM-008
        Console.WriteLine("FakeStrideApp - not yet implemented");
    }
}
```

### 1.3 Wire into ClusterRunner

**File:** `Hrot\Runner\Hrot.ClusterRunner\Hrot.ClusterRunner.csproj`

Add to the `<ItemGroup>` with other subsystem references:
```xml
<ProjectReference Include="..\..\Subsystems\Hrot.StrideMock\Hrot.StrideMock.csproj" />
```

### 1.4 Wire into Solution File

**File:** `IOS-IG-SimHost.sln`

Add both projects to the solution using the same format as existing entries. Use fresh GUIDs. Place `Hrot.StrideMock` in the same `Hrot\Subsystems` solution folder as `Hrot.SimHost`, and `Hrot.FakeStrideApp` in the same `Hrot\Runner` solution folder as `Hrot.ClusterRunner`.

Look at the existing solution file format carefully and follow it exactly — wrong nesting will break the IDE layout.

### 1.5 SM-001 Success Verification

Run these commands from the repo root and confirm no errors:
```
dotnet build Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.csproj
dotnet build Hrot\Runner\Hrot.FakeStrideApp\Hrot.FakeStrideApp.csproj
dotnet build Hrot\Runner\Hrot.ClusterRunner\Hrot.ClusterRunner.csproj
```

**No tests required for SM-001** (scaffolding only — tests come in later batches).

---

## Task 2: SM-002 — Implement SharedApplicationBootstrapper

**Reference:** [TASK-DETAILS.md SM-002](../TASK-DETAILS.md#sm-002--implement-sharedapplicationbootstrapper)  
**Design Reference:** [DESIGN.md §4](../DESIGN.md#4-sharedapplicationbootstrapper-hrotcommoninfrastructure) — read this entire section before coding.

### 2.1 Implementation

**File:** `Hrot\Engine\Hrot.Common\Infrastructure\SharedApplicationBootstrapper.cs` (NEW)

Implement the `SharedApplicationBootstrapper` abstract class exactly as specified in DESIGN.md §4.3 API Contract. Key points:

**Strict 7-phase pipeline in `BootstrapNode()` (non-overridable):**

- **Phase 1:** `HrotNodeBuilder(config).WithReplication(role, networkFactory).Build()` — this is mandatory. Skipping `.WithReplication()` leaves `context.NedReplication` permanently null and silently breaks Phase 6a+. The builder is from `Hrot.Common.Infrastructure.HrotNodeBuilder`.

- **Phase 2:** `RegisterDomainComponents(context.World)` — abstract hook. All components must be registered BEFORE Phase 3.

- **Phase 3:** Call `BuildSerializer(GetBehaviorRegistry())` — abstract hook. Returns `ScenarioSerializer`. This hook is abstract (not base-class concrete) because `HrotScenarioSerializerFactory` lives in `Hrot.SimHost`, above `Hrot.Common` in the dependency hierarchy. Circular dependency prevention.

- **Phase 4a:** Call `PopulateSystems(context, inputSystems, simSystems, postSimSystems)` — abstract hook. Then create:
  - `TogglableInputGroup` from `inputSystems`
  - `TogglableSimulationGroup` from `simSystems`
  - `TogglablePostSimulationGroup` from `postSimSystems`

- **Phase 4b:** Call `GetAdditionalModules()` — virtual hook (default: empty). Register each module via `context.Kernel.RegisterModule(mod)`.

- **Phase 5:** Call `BuildOrchestration(context, inputGroup, simGroup, postSimGroup, serializer)` — abstract hook. Returns `ClusterSlave`. This hook is abstract for the same reason as `BuildSerializer` — `NodeBootstrapper` lives in `Hrot.SimHost`. The concrete subclass calls `NodeBootstrapper.BuildOrchestration(...)` and **must** pass `lifecycleGroup: context.NedReplication?.NetworkLifecycleGroup`.

- **Phase 6a:** `RegisterSpawningPipeline(context)` — abstract hook.

- **Phase 6a+:** Base-class only (no hook): if `context.NedReplication != null`, call `context.Kernel.RegisterModule(context.NedReplication)`. Subclasses must NOT do this.

- **Phase 6b:** `RegisterNetworkTranslators(context, configuredFactory)` — abstract hook. The `configuredFactory` is the result of `networkFactory.ConfigureForNode(context...)` — NOT the raw input factory.

- **Phase 6c:** Base-class only (no hook): wire time-sync translators:
  - `TimeNetworkModule.CreateDescriptorTranslator(configuredFactory, context)`
  - `TimeNetworkModule.CreateSlaveLockstepTranslator(configuredFactory, context)`
  - `TimeNetworkModule.CreateSlaveTimeSyncTranslator(configuredFactory, context)`
  - Call `configuredFactory.CreateTimeControlGateway()` and store result in `TimeControl` property.

- **Phase 7:** `context.Kernel.Initialize()` — always last.

**Produced properties (available after BootstrapNode):**
- `public ITimeControlGateway? TimeControl { get; private set; }` — set in Phase 6c only

**Abstract hooks (must be implemented by subclasses):**
- `RegisterDomainComponents(EntityRepository world)`
- `BuildSerializer(BehaviorRegistry? registry)` → `ScenarioSerializer`
- `PopulateSystems(HrotNodeContext context, List<IEcsModuleSystem> input, List<IEcsModuleSystem> sim, List<IEcsModuleSystem> postSim)`
- `BuildOrchestration(HrotNodeContext context, TogglableSimulationGroup simGroup, TogglablePostSimulationGroup postSimGroup, ScenarioSerializer serializer)` → `ClusterSlave`
- `RegisterSpawningPipeline(HrotNodeContext context)`
- `RegisterNetworkTranslators(HrotNodeContext context, INetworkFactory configuredFactory)`

**Virtual hooks (subclasses may override):**
- `GetAdditionalModules()` → `IEnumerable<IEcsModule>` (default: `Array.Empty<IEcsModule>()`)
- `GetBehaviorRegistry()` → `BehaviorRegistry?` (default: `null`)

**Look at existing usage of `HrotNodeBuilder` in `HrotNodeBuilder.cs` and `SimHostApp.cs` to understand how `.WithReplication()` and `.Build()` chain works.**

### 2.2 Tests

**New test project:** Create `Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.Tests\Hrot.StrideMock.Tests.csproj`

Reference: `Hrot.StrideMock.csproj`, `Hrot.SimHost.csproj`, xUnit + Moq packages.

Add this test project to the solution file and add it to `Hrot.Common` `InternalsVisibleTo` if needed.

**Test file:** `SharedApplicationBootstrapperTests.cs`

Write tests for all 10 success conditions from TASK-DETAILS.md SM-002 (SC_SM002_1 through SC_SM002_10).

For SC_SM002_1 through SC_SM002_5 use a **concrete test subclass** that implements all abstract hooks with minimal in-memory stubs (no real DDS, no real NodeBootstrapper). This allows pure unit testing of the base class phase ordering.

Example test subclass pattern (do not copy verbatim — adapt to actual API):
```csharp
private sealed class TestBootstrapper : SharedApplicationBootstrapper
{
    public List<string> CallLog { get; } = new();

    protected override void RegisterDomainComponents(EntityRepository world)
    {
        CallLog.Add("RegisterDomainComponents");
        // register a test component
        world.RegisterComponent<TestComponentA>();
    }

    protected override ScenarioSerializer BuildSerializer(BehaviorRegistry? registry)
    {
        CallLog.Add("BuildSerializer");
        // return a no-op serializer — use HrotScenarioSerializerFactory if possible,
        // or a test stub if factory requires live DDS
        return CreateTestSerializer();
    }

    protected override void PopulateSystems(HrotNodeContext ctx,
        List<IEcsModuleSystem> input,
        List<IEcsModuleSystem> sim,
        List<IEcsModuleSystem> postSim)
    {
        CallLog.Add("PopulateSystems");
        sim.Add(new TestSimSystem());
    }

    protected override ClusterSlave BuildOrchestration(...)
    {
        CallLog.Add("BuildOrchestration");
        // Use NodeBootstrapper.BuildOrchestration with the provided context
        // Pass lifecycleGroup: context.NedReplication?.NetworkLifecycleGroup
        ...
    }

    protected override void RegisterSpawningPipeline(HrotNodeContext ctx)
    {
        CallLog.Add("RegisterSpawningPipeline");
    }

    protected override void RegisterNetworkTranslators(HrotNodeContext ctx, INetworkFactory factory)
    {
        CallLog.Add("RegisterNetworkTranslators");
    }
}
```

**Required tests for SC_SM002_1 to SC_SM002_5 (pure unit tests, no DDS required):**

- `BootstrapNode_WithMinimalSubclass_DoesNotThrow()` — SC_SM002_1
- `RegisterDomainComponents_RunsBeforeBuildSerializer()` — SC_SM002_2: component registered in `RegisterDomainComponents` is present in world before `BuildSerializer` is invoked (verify via CallLog ordering + component presence)
- `PopulateSystems_SystemsAppearsInSimGroup_BeforeBuildOrchestration()` — SC_SM002_3: verify a system registered in `PopulateSystems` appears in the `TogglableSimulationGroup` before `BuildOrchestration` is called
- `KernelInitialize_CalledOnce_AfterAllTranslators()` — SC_SM002_4: `Kernel.Initialize()` called exactly once; verify via a mock kernel or tracking
- `AbstractHooks_ExactlyThese_NoMore_NoLess()` — SC_SM002_5: reflection test that the abstract methods are exactly the 6 specified and virtuals are the 2 specified

**Additional tests for SC_SM002_6 through SC_SM002_10** require more complex setup (live DDS or careful mocking). Use the actual `HrotNodeBuilder` and `NodeBootstrapper` where practical:

- `TimeControl_NonNull_AfterBootstrapWithLiveFactory()` — SC_SM002_6 (can use mock INetworkFactory that returns a test gateway)
- `TimeTranslators_RegisteredByBaseClass_SlaveSyncController_ReceivesEvent()` — SC_SM002_7
- `NedReplication_RegisteredByBaseClass_GhostCreationSystemPresent()` — SC_SM002_8
- `WithReplication_Required_NedReplication_NonNull()` — SC_SM002_9
- `BuildOrchestration_ReceivesLifecycleGroup_FromNedReplication()` — SC_SM002_10

**Quality bar for these tests:**
- Tests must verify actual behavior, not just log presence. If verifying phase ordering, assert actual state (component registered, system in group) not just CallLog.
- For SC_SM002_7 the test must verify the `SlaveSyncController` actually transitions state (not just that a translator was registered).
- For SC_SM002_8 the test must verify `GhostCreationSystem` is present in the kernel (not just that `RegisterModule` was called).

---

## Testing Requirements

- **Minimum:** 10 tests covering all SC_SM002_x conditions
- **Quality:** Tests must verify actual behavior — not just that methods were called or strings exist
- ALL tests must pass before submitting the report
- Run with: `dotnet test Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.Tests\Hrot.StrideMock.Tests.csproj`

---

## Quality Standards

**Test Quality — NOT ACCEPTABLE:**
- Tests that only check a method was called (CallLog presence)
- Tests that check `Assert.NotNull` on the bootstrapper itself
- Tests that verify compilation only

**Test Quality — REQUIRED:**
- Tests that verify actual state after BootstrapNode (components in world, systems in groups, kernel state)
- Tests that simulate a behavior and observe the outcome (e.g., trigger a time event, observe SlaveSyncController state change)

---

## Success Criteria

This batch is DONE when:
- [ ] `dotnet build Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.csproj` — no errors
- [ ] `dotnet build Hrot\Runner\Hrot.FakeStrideApp\Hrot.FakeStrideApp.csproj` — no errors
- [ ] `dotnet build Hrot\Runner\Hrot.ClusterRunner\Hrot.ClusterRunner.csproj` — no errors (with StrideMock ref added)
- [ ] `SharedApplicationBootstrapper.cs` implemented with all 8 abstract/virtual hooks
- [ ] All 10 SM-002 success conditions have corresponding tests
- [ ] All tests pass
- [ ] Report submitted

---

## Developer Insights (Report Questions)

**Q1:** What issues did you run into implementing `SharedApplicationBootstrapper`? How did you resolve them? (Focus on the phase ordering, hook abstraction, or dependency hierarchy issues.)

**Q2:** Did you spot any weak points or inconsistencies in the existing `HrotNodeBuilder` or `NodeBootstrapper` code? What would you improve?

**Q3:** What design decisions did you make beyond the spec? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the instructions?

**Q5:** Suggested commit message for this batch?

---

## Reference Materials

- **Design:** `.dev/stride-mock/DESIGN.md` §1–§4 (especially §4.2 The 5 Fragile Init Traps, §4.3 API Contract)
- **Task Details:** `.dev/stride-mock/TASK-DETAILS.md` SM-001, SM-002
- **Pattern Reference:** `Hrot\Engine\Hrot.Common\Infrastructure\HrotNodeBuilder.cs`
- **Pattern Reference:** `Hrot\Subsystems\Hrot.SimHost\SimHostApp.cs` (existing OnLoad phase sequence)
- **Pattern Reference:** `Hrot\Subsystems\Hrot.SimHost\NodeBootstrapper.cs`
- **Solution format reference:** `IOS-IG-SimHost.sln`
