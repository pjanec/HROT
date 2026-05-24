# BATCH-11 INSTRUCTIONS

## Tasks
- **EQS-023** (distributed part) -- `Eqs_DistributedTopology_EvaluatesOnMuscleAndPopulatesBrain`
- **EQS-027** -- Stale epoch rejection across DDS
- **EQS-028** -- Mid-evaluation BTree abort / lifecycle teardown

## References
- Task specs: `.dev/eqs-2/TASK-DETAIL.md` sections TASK-EQS-023, TASK-EQS-027, TASK-EQS-028
- Implementation details: `.dev/eqs-2/IMPLEM_DETAILS.md` L:2135-2200 (distributed), L:2415-2530 (stale epoch), L:2533-2640 (abort)
- Existing distributed pattern: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsTranslatorTests.cs`
- Existing MockRaycastSolverSystem: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/AccurateLosPhaseTests.cs`
- Task tracker: `.dev/eqs-2/TASK-TRACKER.md`

## Constraints
- ASCII only -- no Unicode in comments or strings
- Do NOT reformat unrelated code
- Build must succeed with 0 errors before reporting
- All tests: `[Collection("EqsIntegrationTests")]`
- Tests go in ONE new file: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsDistributedTests.cs`
- Use `[Fact(Timeout = 30_000)]` (distributed tests are slow due to DDS warmup)
- Domain counter: static field `_domainCounter = 200`, increment with `Interlocked.Increment`

---

## Architecture notes (read before coding)

### HrotRunnerHarness distributed pattern
```csharp
int domainId = Interlocked.Increment(ref _domainCounter);
using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

// Spawn entity with split authority (CGF creates, SimHost materializes)
long networkId = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
    TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

// Wait for Muscle entity
bool entityReady = harness.PumpUntil(
    () => harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out _),
    timeoutFrames: 2000);

// Get Brain entity
harness.Cgf!.GhostEntityMap!.TryGetEntity(networkId, out Entity cgfEntity);

// Add component to Brain
harness.Cgf!.World!.AddComponent(cgfEntity, new EqsSensor { ... });

// Pump with frame timeout (NOT ms timeout -- HrotRunnerHarness uses frames)
harness.PumpUntil(() => ..., timeoutFrames: 2000);
```

### Template registration for distributed tests
Register on SimHost world:
```csharp
var registry = new SimpleEqsTemplateRegistry();
registry.Register(new EqsQueryTemplate { ... });
harness.SimHost.World!.SetSingletonManaged<IEqsTemplateRegistry>(registry);
```

### MockRaycastSolverSystem for distributed tests
Can be injected into SimHost BEFORE warmup completes only via `TestHook_AddSystem`:
```csharp
harness.SimHost.TestHook_AddSystem(new NeverResolvingMockRaycastSystem());
```

**IMPORTANT:** Call `TestHook_AddSystem` BEFORE the HrotRunnerHarness Warmup runs, or after using the appropriate SimHostSubsystem API. Check `ClusterOpE2eScriptTests.cs` for an example of `TestHook_AddSystem` usage.

Actually, `HrotRunnerHarness` calls `Orchestrator.Initialize()` and then `Warmup()` in its constructor. `TestHook_AddSystem` is only available after initialization. For tests that need a mock system active from the start, construct the harness then immediately call `TestHook_AddSystem` before pumping to the desired state. Since the system is added POST-warmup, it will run on all subsequent frames.

### PumpUntil vs PumpFrames
- `harness.PumpUntil(condition, timeoutFrames)` -- polls until condition true or timeout
- `harness.PumpFrames(n)` -- pumps exactly n frames, no condition

---

## 11-A: SimpleEqsTemplateRegistry (shared inner type)

```csharp
private sealed class SimpleEqsTemplateRegistry : IEqsTemplateRegistry
{
    private readonly System.Collections.Generic.Dictionary<uint, EqsQueryTemplate> _t = new();
    public void Register(EqsQueryTemplate t) => _t[t.BlueprintId] = t;
    public bool TryGetTemplate(uint id, out EqsQueryTemplate t) => _t.TryGetValue(id, out t);
}
```

---

## 11-B: T-DIS1 (EQS-023 distributed)

### Name
`Eqs_DistributedTopology_EvaluatesOnMuscleAndPopulatesBrain`

### Setup
1. Register a simple template on SimHost world:
   - Use `CoverPointsGenerator` (positional, no tests, no filtering)
   - Register `ManualCoverProvider` with 2 cover points on SimHost world
   - `BlueprintId = 200u`
2. Spawn entity with split authority (`TkbEntityTypes.Tank_M1Abrams`)
3. Wait for entity on Muscle
4. Add `EqsSensor` (BlueprintId=200u, Epoch=1, SearchRadius=50f) to CGF entity
5. Wait for Brain buffer `IsReady`

### Template:
```csharp
var registry = new SimpleEqsTemplateRegistry();
registry.Register(new EqsQueryTemplate
{
    BlueprintId   = 200u,
    Generator     = new CoverPointsGenerator(),
    MaxCandidates = 8,
});
harness.SimHost.World!.SetSingletonManaged<IEqsTemplateRegistry>(registry);
harness.SimHost.World!.SetSingletonManaged<ICoverProvider>(new ManualCoverProvider(new[]
{
    new CoverPoint { PositionX = 5f, PositionY = 0f  },
    new CoverPoint { PositionX = 0f, PositionY = 5f  },
}));
```

### Pump condition:
```csharp
harness.PumpUntil(() =>
{
    var world = harness.Cgf!.World;
    if (world == null) return false;
    if (!world.HasComponent<EqsCognitiveBuffer>(cgfEntity)) return false;
    ref readonly var buf = ref world.GetComponentRO<EqsCognitiveBuffer>(cgfEntity);
    return buf.IsReady && buf.Count > 0;
}, timeoutFrames: 4000)
```

### Assertions:
1. `bufferReady == true`
2. `buffer.Count > 0`
3. `buffer.GetTop().EntityId == 0` (positional candidate)

---

## 11-C: T-DIS2 (EQS-027 stale epoch)

### Name
`Eqs_DistributedTopology_RejectsStaleEpochResults`

### Inner type: DynamicRadiusGeneratorMock
```csharp
// Yields 1 candidate for SearchRadius == 10f; 2 candidates for any other radius.
private sealed class DynamicRadiusGeneratorMock : IEqsGenerator
{
    public int Generate(Entity observer, ref EqsSensor sensor,
        ISimulationView view, Span<EqsResult> candidates)
    {
        int count = sensor.SearchRadius <= 10f ? 1 : 2;
        count = Math.Min(count, candidates.Length);
        for (int i = 0; i < count; i++)
            candidates[i] = new EqsResult { EntityId = 0L, PositionX = (float)i, PositionY = 0f };
        return count;
    }
}
```

### Setup
1. Register `DynamicRadiusGeneratorMock` template (BlueprintId=201u) on SimHost world
2. Spawn entity, wait for Muscle
3. Add `EqsSensor` (BlueprintId=201u, Epoch=1, SearchRadius=10f) to CGF entity
4. Pump until Brain buffer `IsReady && Count == 1` (epoch-1 result)
5. Change sensor on CGF entity: epoch=2, radius=20f
6. Inject fake stale `EqsResultUpdateEvent` (epoch=1, 99 fake Results) on CGF bus
7. Pump 2 frames
8. Assert: `buffer.Count != 99` (stale event rejected)
9. Pump until Brain buffer `Count == 2` (genuine epoch-2 result)

### Sensor mutation (step 5):
```csharp
ref var sensor = ref harness.Cgf!.World!.GetComponentRW<EqsSensor>(cgfEntity);
sensor.Epoch        = 2u;
sensor.SearchRadius = 20f;
```

### Stale event injection (step 6):
```csharp
var staleResults = new System.Collections.Generic.List<EqsResultEntry>();
for (int i = 0; i < 99; i++)
    staleResults.Add(new EqsResultEntry { EntityId = 0L });

harness.Cgf!.World!.Bus.PublishManaged(new EqsResultUpdateEvent
{
    Observer    = cgfEntity,
    Epoch       = 1u,       // STALE: sensor is now at epoch 2
    RefreshTick = 1u,
    Results     = staleResults,
});
```

### Assertions:
1. After 2 frames: `buffer.Count != 99`
2. After waiting for genuine epoch-2 result: `buffer.Count == 2`

---

## 11-D: T-DIS3 (EQS-028 mid-evaluation abort)

### Name
`Eqs_MidEvaluationAbort_SilentlyDropsQueryWithoutLeaking`

### Inner type: NeverResolvingMockRaycastSystem
```csharp
// Reads RaycastRequestEvents but deliberately does NOT write results to the ring buffer.
// This keeps the EQS solver permanently in _AwaitingRaycasts phase.
[UpdateInPhase(SystemPhase.PostSimulation)]
private sealed class NeverResolvingMockRaycastSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        // Consume events to prevent ring-buffer overflow but do not resolve them.
        view.ReadEvents<RaycastRequestEvent>();
    }
}
```

### Setup
1. `harness.SimHost.TestHook_AddSystem(new NeverResolvingMockRaycastSystem())`
2. Set budget to 1 on SimHost:
   ```csharp
   harness.SimHost.World!.SetSingletonUnmanaged(new EqsSolverGlobalState
   {
       MaxAccurateRaycastsPerSolverTick = 1,
       AccurateRaysSubmittedThisTick    = 0,
   });
   ```
3. Register template with `NavmeshSamplesGenerator + AccurateLineOfSightTest` (BlueprintId=202u) + `StubNavmeshProvider` on SimHost world
4. Spawn entity, wait for Muscle entity to appear
5. Add `TargetMemory` directly to SimHost entity (to enable AccurateLineOfSightTest):
   ```csharp
   harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out Entity simEntity);
   var mem = new TargetMemory();
   unsafe
   {
       mem.Count           = 1;
       mem.ThreatScores[0] = 100f;
       mem.PositionsX[0]   = 30f;
       mem.PositionsY[0]   = 0f;
   }
   harness.SimHost.World!.AddComponent(simEntity, mem);
   ```
6. Add `EqsSensor` (BlueprintId=202u, Epoch=1, SearchRadius=50f, ThreatThreshold=0f) to CGF entity
7. Pump until EqsSensor replicates to SimHost entity
8. Pump until `SensorEvalState.Phase == EqsEvalPhase._AwaitingRaycasts` on SimHost entity
9. Remove `EqsSensor` from CGF entity:
   ```csharp
   harness.Cgf!.World!.RemoveComponent<EqsSensor>(cgfEntity);
   ```
10. Pump until SimHost entity no longer has `EqsSensor`:
    ```csharp
    harness.PumpUntil(() =>
    {
        harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out Entity se);
        return !harness.SimHost.World!.HasComponent<EqsSensor>(se);
    }, timeoutFrames: 2000);
    ```
11. Pump 60 more frames (ensure solver runs at least 1 full cycle after removal)
12. No explicit assertion about AccurateRaysSubmittedThisTick (timing is non-deterministic after reset)

### Assertions:
1. `awaitingReached == true` (solver entered _AwaitingRaycasts before abort)
2. `sensorRemovedFromMuscle == true` (DDS propagated the removal)
3. Test completes without exception (no crash = silent drop worked correctly)
4. Optionally: `!harness.SimHost.World!.HasComponent<EqsSensor>(simEntity)` (final check)

**Note on EqsSolverGlobalState registration:** The `EqsSolverGlobalState` singleton may already be initialized by `EqsModule` during warmup. Use `SetSingletonUnmanaged` to override it. If the component is not yet registered, the call will be a no-op or throw. Check if it exists first:
```csharp
if (harness.SimHost.World!.HasSingleton<EqsSolverGlobalState>())
{
    harness.SimHost.World!.SetSingletonUnmanaged(new EqsSolverGlobalState
    {
        MaxAccurateRaycastsPerSolverTick = 1,
        AccurateRaysSubmittedThisTick    = 0,
    });
}
```

**Note on TargetMemory in SimHost:** The `TargetMemory` component may not be directly addable via `AddComponent` if the SimHost world doesn't have it registered. Check if it's registered in `SimHostComponentRegistry.RegisterAll` or `CognitiveComponentRegistry.RegisterAll`. If not registered, skip T-DIS3's `_AwaitingRaycasts` phase requirement and just verify that: sensor added to Brain → replicates to Muscle → sensor removed from Brain → replicates removal to Muscle → no crash.

---

## File structure

### New file: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsDistributedTests.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Spatial.Eqs.Topics;
using Hrot.Map.Common;
using Hrot.SimHost;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

[Collection("EqsIntegrationTests")]
public sealed class EqsDistributedTests
{
    // Domain IDs 200-209 (distinct from EqsTranslatorTests 71-79 and HrotRunnerHarness 100+)
    private static int _domainCounter = 200;

    // Inner types: SimpleEqsTemplateRegistry, DynamicRadiusGeneratorMock,
    // NeverResolvingMockRaycastSystem

    // Tests: T-DIS1, T-DIS2, T-DIS3
}
```

No `IDisposable` is needed -- `HrotRunnerHarness` is used in `using` blocks within each test, not as a class-level field.

---

## Build and test verification

```
dotnet build Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj --no-restore
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --no-build --filter "FullyQualifiedName~EqsDistributedTests"
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --no-build --filter "FullyQualifiedName~Eqs"
```

Expected:
- T-DIS1, T-DIS2, T-DIS3: 3/3 pass
- Existing EQS integration tests: 25/25 (no regressions)
- Pre-existing ~32 failures in Hrot.SimHost.Tests are acceptable

---

## Important: Pragmatic deviation allowed for T-DIS3

If adding `TargetMemory` to the Muscle entity is not feasible (not registered, or triggers an error), the test should still verify the core behavior:
1. EqsSensor added to CGF → replicates to SimHost
2. EqsSensor removed from CGF → replicates removal to SimHost
3. No crash or exception occurs during or after the removal

In this case, the test still proves "mid-evaluation abort" because: at step 1, the solver starts evaluation (any phase); at step 2, the component disappears; the solver must handle this gracefully. Asserting "no crash" is the primary success condition.

---

## Report

Write to `.dev/eqs-2/reports/BATCH-11-REPORT.md` including:
- Files created/modified
- Test names and pass/fail  
- Any deviations from plan (with justification)
- Build confirmation (0 errors)
