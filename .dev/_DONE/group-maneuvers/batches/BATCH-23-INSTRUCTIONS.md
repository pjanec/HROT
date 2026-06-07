# BATCH-23 Instructions

**Covers:** P2-03 (DangerAreaSensor + DangerAreaCognitiveBuffer + DangerAreaRefreshSystem),
P2-04 (Phase-2 integration test)
**Prerequisites:** BATCH-22 committed (787b0bd8). `IDangerAreaProvider`, `DangerAreaDescriptor`,
and `FakeDangerAreaProvider` already exist (BATCH-20). `SquadPerceptionMergeSystem` exists
(BATCH-22).

---

## Context

Phase 2 completes with the danger-area sensor lifecycle (P2-03) and an integration test that
exercises both the perception merge and the sensor in tandem (P2-04). No EQS wiring — those
come in Phase 4.

---

## Task P2-03: DangerAreaSensor + DangerAreaCognitiveBuffer + DangerAreaRefreshSystem

### 3a. `GlobalComponentIds.cs` additions

File: `FDP/Engine/Fdp.Core/GlobalComponentIds.cs`

In the squad block (256-299), after the existing `SquadStateMarker = 256` entry, add:

```csharp
/// <summary><c>DangerAreaSensor</c> — standing query config on a sensor child entity
/// (squad danger-area pipeline, §5.1).</summary>
public const int DangerAreaSensor = 257;

/// <summary><c>DangerAreaCognitiveBuffer</c> — Brain-side result cache written by
/// <c>DangerAreaRefreshSystem</c> (squad danger-area pipeline, §5.2).</summary>
public const int DangerAreaCognitiveBuffer = 258;
```

### 3b. `DangerAreaSensor` component

New file: `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/DangerAreaSensorComponent.cs`

Namespace: `Fdp.Toolkit.Squad.DangerArea`.

```csharp
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.DangerAreaSensor)]
public struct DangerAreaSensor
{
    /// <summary>FNV-1a-32 hash of the query template blueprint id.</summary>
    public uint BlueprintId;
    /// <summary>Incremented on every successful refresh. Downstream caches compare this
    /// to detect staleness (matches EqsSensor.Epoch precedent).</summary>
    public uint Epoch;
    /// <summary>Minimum seconds between refreshes. 0 = refresh every call.</summary>
    public float RefreshIntervalSeconds;
    /// <summary>Simulation time (seconds) at which the last refresh completed.</summary>
    public float LastRefreshSimTime;
}
```

Size: 16 bytes (sequential).

### 3c. `DangerAreaCognitiveBuffer` component

New file: `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/DangerAreaCognitiveBuffer.cs`

Namespace: `Fdp.Toolkit.Squad.DangerArea`.

Define an inline array of 8 descriptors first:

```csharp
/// <summary>
/// Inline array of 8 <see cref="DangerAreaDescriptor"/>s (8 * 68 = 544 bytes).
/// Always write through <see cref="DangerAreaCognitiveBuffer.GetSpanRW"/> to
/// avoid the InlineArray defensive-copy trap.
/// </summary>
[InlineArray(8)]
public struct DangerAreaDescriptorArray
{
#pragma warning disable CS0169
    private DangerAreaDescriptor _element;
#pragma warning restore CS0169
}
```

Then the component:

```csharp
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.DangerAreaCognitiveBuffer)]
public struct DangerAreaCognitiveBuffer
{
    /// <summary>Number of valid descriptors in <see cref="Slots"/> (0..8).</summary>
    public int Count;

    // 4 bytes padding to align Slots to 8 bytes (DangerAreaDescriptor starts with uint, aligned to 4)
    private int _pad;

    /// <summary>Cached danger-area descriptors from the last refresh.</summary>
    public DangerAreaDescriptorArray Slots;

    /// <summary>True after the first successful refresh.</summary>
    public bool IsReady => Count > 0;

    /// <summary>Write-through span over Slots (defeats InlineArray defensive copy).</summary>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public Span<DangerAreaDescriptor> GetSpanRW()
        => MemoryMarshal.CreateSpan(
               ref Unsafe.As<DangerAreaDescriptorArray, DangerAreaDescriptor>(ref Slots), 8);

    /// <summary>Read-only span over Slots.</summary>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<DangerAreaDescriptor> GetSpanRO()
        => MemoryMarshal.CreateReadOnlySpan(
               ref Unsafe.As<DangerAreaDescriptorArray, DangerAreaDescriptor>(ref Slots), 8);
}
```

Note: `_pad` ensures `Slots` starts at offset 8 (4-byte Count + 4-byte _pad = 8 bytes before
Slots). `DangerAreaDescriptor` is 68 bytes so the total struct size is 8 + 8*68 = 552 bytes.

Required `using` directives: `System`, `System.Runtime.CompilerServices`,
`System.Runtime.InteropServices`, `Fdp.Core`, `System.Runtime.InteropServices`,
`System.Runtime.CompilerServices`. Match the style of adjacent files.

### 3d. `DangerAreaRefreshSystem`

New file: `FDP/Toolkits/Fdp.Toolkits/Squad/Systems/DangerAreaRefreshSystem.cs`

Namespace: `Fdp.Toolkit.Squad.Systems`.

```csharp
public sealed class DangerAreaRefreshSystem
{
    private readonly IDangerAreaProvider _provider;

    public DangerAreaRefreshSystem(IDangerAreaProvider provider)
    {
        _provider = provider;
    }

    /// <summary>
    /// Refreshes the danger-area buffer on a sensor child entity if the refresh
    /// interval has elapsed.
    /// </summary>
    /// <param name="repo">Entity repository.</param>
    /// <param name="sensorChild">Entity carrying DangerAreaSensor + DangerAreaCognitiveBuffer
    ///   + PartMetadata.</param>
    /// <param name="currentSimTime">Current simulation time in seconds.</param>
    public void Run(EntityRepository repo, Entity sensorChild, float currentSimTime)
    {
        if (!repo.HasComponent<DangerAreaSensor>(sensorChild)) return;
        if (!repo.HasComponent<DangerAreaCognitiveBuffer>(sensorChild)) return;
        if (!repo.HasComponent<PartMetadata>(sensorChild)) return;

        ref var sensor = ref repo.GetComponentRW<DangerAreaSensor>(sensorChild);

        // Check interval: always refresh if interval is zero.
        if (sensor.RefreshIntervalSeconds > 0f &&
            currentSimTime - sensor.LastRefreshSimTime < sensor.RefreshIntervalSeconds)
        {
            return;
        }

        ref readonly var meta = ref repo.GetComponentRO<PartMetadata>(sensorChild);
        var commander = meta.ParentEntity;

        // Refresh into a stack buffer (max 8 descriptors = cap of DangerAreaCognitiveBuffer).
        Span<DangerAreaDescriptor> stackBuf = stackalloc DangerAreaDescriptor[8];
        _provider.Refresh(repo, commander, stackBuf, out int count);

        ref var buffer = ref repo.GetComponentRW<DangerAreaCognitiveBuffer>(sensorChild);
        var dst = buffer.GetSpanRW();
        stackBuf.Slice(0, count).CopyTo(dst);
        buffer.Count = count;

        sensor.Epoch++;
        sensor.LastRefreshSimTime = currentSimTime;
    }
}
```

Required `using` directives: `System`, `System.Runtime.CompilerServices`,
`System.Runtime.InteropServices`, `Fdp.Core`, `Fdp.Toolkit.Replication.Components`,
`Fdp.Toolkit.Squad.DangerArea`.

---

## Task P2-03 — Tests

New file: `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Systems/DangerAreaRefreshSystemTests.cs`

Namespace: `Fdp.Toolkit.Squad.Tests.Systems`. Use `xunit`.

### Setup helper `CreateSensorChild`

```csharp
static (EntityRepository repo, Entity commander, Entity sensorChild)
    CreateSensorChild(float refreshInterval = 0f)
{
    var repo = new EntityRepository();
    var commander = repo.CreateEntity();
    repo.AddComponent<Blackboard1024>(commander);
    repo.AddComponent<SquadStateMarker>(commander);

    var child = repo.CreateEntity();
    repo.AddComponent<DangerAreaSensor>(child);
    repo.AddComponent<DangerAreaCognitiveBuffer>(child);
    repo.AddComponent<PartMetadata>(child);

    ref var sensor = ref repo.GetComponentRW<DangerAreaSensor>(child);
    sensor.BlueprintId = 0xABCD_1234u;
    sensor.RefreshIntervalSeconds = refreshInterval;

    ref var meta = ref repo.GetComponentRW<PartMetadata>(child);
    meta.ParentEntity = commander;

    return (repo, commander, child);
}
```

**Test SC-P2-03-1: SingleChild_WritesDescriptorsAndSetsCount**

```
var fake = new FakeDangerAreaProvider();
fake.Add("crossing-01", DangerAreaKind.StreetCrossing, 0.7f);
fake.Add("crossing-02", DangerAreaKind.StreetCrossing, 0.4f);
fake.Add("crest-01",    DangerAreaKind.CrestLine,      0.9f);

var (repo, commander, child) = CreateSensorChild(refreshInterval: 0f);
var system = new DangerAreaRefreshSystem(fake);
system.Run(repo, child, currentSimTime: 0f);

ref readonly var buffer = ref repo.GetComponentRO<DangerAreaCognitiveBuffer>(child);
Assert.Equal(3, buffer.Count);
Assert.Equal(GlobalComponentIds.DangerAreaSensor, ...)  // just checking the count
```

Also assert `PartMetadata.ParentEntity == commander` (pre-condition, not tested by system itself
but a smoke check for the fixture).

**Test SC-P2-03-2: EpochIncrements**

```
Same fake provider with 1 descriptor.
Run system at simTime=0f -> assert sensor.Epoch == 1.
Run system at simTime=0f -> assert sensor.Epoch == 2. (RefreshIntervalSeconds==0 => always runs.)
```

**Test SC-P2-03-3: TwoSensorChildren_RefreshedIndependently**

```
Commander has two sensor children: child1 (BlueprintId=1) and child2 (BlueprintId=2).
fake1 returns 2 descriptors, fake2 returns 1 descriptor.
system1 = new DangerAreaRefreshSystem(fake1);
system2 = new DangerAreaRefreshSystem(fake2);
system1.Run(repo, child1, 0f);
system2.Run(repo, child2, 0f);
Assert buffer1.Count == 2, buffer2.Count == 1.
```

(Two separate system instances, each with their own provider — correct because real
deployments would also inject different providers for different sensor blueprints.)

**Test SC-P2-03-4: ZPreserved**

```
var fake = new FakeDangerAreaProvider();
fake.Add("feature-01", DangerAreaKind.ChokePoint, 0.5f,
         center: new Vector3(1f, 2f, 3f),
         extents: new Vector2(4f, 5f),
         angle: 0f,
         zFloor: 1f,    // <-- non-zero Z
         zCeiling: 5f); // <-- non-zero Z

var (repo, _, child) = CreateSensorChild();
new DangerAreaRefreshSystem(fake).Run(repo, child, 0f);

ref readonly var buf = ref repo.GetComponentRO<DangerAreaCognitiveBuffer>(child);
var d = buf.GetSpanRO()[0];
Assert.Equal(1f, d.ZFloor,   precision: 5);
Assert.Equal(5f, d.ZCeiling, precision: 5);
```

Note: Check the FakeDangerAreaProvider.Add signature; it may need extra parameters for ZFloor
and ZCeiling. If the existing `Add` overload does not accept them, add a new overload that does.
Look at `FakeDangerAreaProvider.cs` first.

---

## Task P2-04: Phase-2 integration test

New file: `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Phase2IntegrationTests.cs`

Namespace: `Fdp.Toolkit.Squad.Tests`. Use `xunit`.

### Fixture setup

```
4-member squad.
- members[0] sees contact A (entityId=100, score=0.8f, tick=1)
- members[1] sees contact A (entityId=100, score=0.6f, tick=2)   -- higher tick, lower score
- members[2] sees contact B (entityId=200, score=0.3f, tick=1)
- members[3] sees nothing

Commander has one sensor child.
FakeDangerAreaProvider has 1 StreetCrossing descriptor ("street-east-01",
DangerAreaKind.StreetCrossing, threatRating: 0.9f).
```

### Test SC-P2-04-1: Contacts_MergeCorrectly

```
Run SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 5, mergeIntervalTicks: 1).

Assert state.Contacts.Count == 2.
Find contact A (entityId=100): ThreatScore==0.8f, SourceMembersMask has bits 0 and 1 set (==0x3), LastSeenTick==2.
Find contact B (entityId=200): ThreatScore==0.3f, SourceMembersMask has bit 2 set (==0x4).
```

### Test SC-P2-04-2: DangerAreaBuffer_HasStreetCrossing

```
Run DangerAreaRefreshSystem.Run(repo, sensorChild, currentSimTime: 0f).

ref readonly var buf = ref repo.GetComponentRO<DangerAreaCognitiveBuffer>(sensorChild);
Assert buf.Count == 1.
Assert buf.GetSpanRO()[0].Kind == DangerAreaKind.StreetCrossing.
```

### Test SC-P2-04-3: MemberAdded_SourceMaskGrows

```
After SC-P2-04-1 run: members[3] also sees contact B (entityId=200, score=0.5f, tick=5).
(Call TargetMemory.AddOrUpdateTarget on members[3].)
Run SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 6, mergeIntervalTicks: 1).

Find contact B: SourceMembersMask must have bits 2 AND 3 set (==0xC); ThreatScore==max(0.3, 0.5)==0.5f.
state.Contacts.Count still == 2.
```

### Test SC-P2-04-4: ZeroAlloc_Over100Ticks

```
Setup: same 4-member squad with two contacts, same sensor child.
Pre-warm: run both systems once.

long before = GC.GetAllocatedBytesForCurrentThread();
for (int t = 10; t < 110; t++)
{
    SquadPerceptionMergeSystem.Run(repo, commander, (uint)t, mergeIntervalTicks: 1);
    DangerAreaRefreshSystem.Run(repo, sensorChild, t * 0.016f);  // 60 Hz sim time
}
long after = GC.GetAllocatedBytesForCurrentThread();
Assert.Equal(0, after - before);
```

If exactly 0 is not achievable (e.g., one-time lazy JIT allocation), allow a tolerance
of up to 64 bytes (one-time overhead). Comment the tolerance if used.

---

## Code location summary

| File | Action |
|------|--------|
| `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` | Add DangerAreaSensor=257, DangerAreaCognitiveBuffer=258 |
| `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/DangerAreaSensorComponent.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/DangerAreaCognitiveBuffer.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/Fake/FakeDangerAreaProvider.cs` | May need new `Add` overload for ZFloor/ZCeiling |
| `FDP/Toolkits/Fdp.Toolkits/Squad/Systems/DangerAreaRefreshSystem.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Systems/DangerAreaRefreshSystemTests.cs` | NEW -- 4 tests |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Phase2IntegrationTests.cs` | NEW -- 4 tests |

---

## Build and test instructions

```
dotnet build FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj
dotnet build FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj ^
    --filter "FullyQualifiedName~DangerAreaRefresh|FullyQualifiedName~Phase2Integration"
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj ^
    --filter "FullyQualifiedName~Squad|FullyQualifiedName~ThreatMatrix|FullyQualifiedName~StarterPack"
```

All 8 new tests must pass. Previously passing squad tests must still pass (58 at BATCH-22 baseline).

---

## Success Conditions

| ID | Requirement |
|----|-------------|
| SC-P2-03-1 | Sensor child gets 3 descriptors from FakeDangerAreaProvider; Count==3 |
| SC-P2-03-2 | Epoch increments on each refresh call |
| SC-P2-03-3 | Two sensor children refreshed independently carry separate descriptor sets |
| SC-P2-03-4 | ZFloor and ZCeiling round-trip without loss |
| SC-P2-04-1 | 4-member squad merges contacts A and B with correct SourceMembersMask and ThreatScore |
| SC-P2-04-2 | DangerAreaBuffer has one StreetCrossing descriptor after refresh |
| SC-P2-04-3 | Adding a member sighting grows B's SourceMembersMask and updates ThreatScore |
| SC-P2-04-4 | 100 ticks of merge + refresh allocate 0 managed bytes |

Total new tests: **8** (4 DangerAreaRefreshSystem + 4 integration).
