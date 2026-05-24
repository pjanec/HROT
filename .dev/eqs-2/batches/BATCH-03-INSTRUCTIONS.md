# BATCH-03 INSTRUCTIONS — EQS DDS Translators + Core Query Interfaces

**Batch:** BATCH-03  
**Depends on:** BATCH-02 (committed as a8656301)  
**Targets:** TASK-EQS-007 + TASK-EQS-008  

---

## Mandatory Reading

Before implementing, read these files in full:

1. `.dev/eqs-2/TASK-DETAIL.md` — sections TASK-EQS-007 and TASK-EQS-008
2. `.dev/eqs-2/IMPLEM_DETAILS.md` — L:2960–3390 (translator reference pseudocode)
3. `.dev/eqs-2/reviews/BATCH-02-REVIEW.md` — understand what exists
4. `Hrot/Network/Hrot.Network.NED/SimHost/PerceptionTranslators.cs` — real egress pattern with SmartEgressUtil
5. `Hrot/Network/Hrot.Network.NED/Replication/Map/Ingress/EntityMissionIngressTranslator.cs` — real ingress pattern with NotAliveDisposed
6. `Hrot/Network/Hrot.Network.NED/SimHost/SimHostAuxiliaryTranslatorPack.cs` — where translators are registered
7. `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsDdsTopics.cs` — actual DDS topic types (use these, NOT the pseudocode names)
8. `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsResultPool.cs` — EqsResultEvent, EqsResultPool
9. `Hrot/Subsystems/Hrot.SimHost/Systems/EqsResultUpdateEvent.cs` — EqsResultUpdateEvent (managed)
10. `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/HrotRunnerHarness.cs` — harness API

---

## TASK-EQS-007 — Full DDS Translator Implementations

### Context

Four translator stubs were created in BATCH-01. Their `ScanAndPublish` / `PollIngress` bodies throw `NotImplementedException`. This task replaces those throw stubs with real logic.

Files to modify (already exist):
- `Hrot/Network/Hrot.Network.NED/SimHost/EqsSensorConfigEgressTranslator.cs` (Brain→Muscle egress)
- `Hrot/Network/Hrot.Network.NED/SimHost/EqsSensorConfigIngressTranslator.cs` (Muscle←Brain ingress)
- `Hrot/Network/Hrot.Network.NED/SimHost/EqsResultEventEgressTranslator.cs` (Muscle→Brain egress)
- `Hrot/Network/Hrot.Network.NED/CGF/EqsResultIngressTranslator.cs` (Brain←Muscle ingress)
- `Hrot/Network/Hrot.Network.NED/SimHost/SimHostAuxiliaryTranslatorPack.cs` (registration)

**CRITICAL: The DDS topic types defined in BATCH-01 are `EqsSensorConfigTopic` and `EqsResultTopic`. Do NOT use `DdsEqsSensorConfig` or `DdsEqsResult` — those are names from the design pseudocode only. The actual types are in `Fdp.Toolkit.Spatial.Eqs.Topics`.**

### 1. EqsSensorConfigEgressTranslator (Brain → Muscle)

Brain-side. Replaces `throw new NotImplementedException(...)` in `ScanAndPublish`.

Implementation pattern (mirrors `SensorConfigEgressTranslator` in PerceptionTranslators.cs):

```csharp
public void ScanAndPublish(ISimulationView view)
{
    if (_writer is null) return;

    var query = view.Query()
        .With<EqsSensor>()
        .With<NetworkIdentity>()
        .Build();

    foreach (var entity in query)
    {
        if (!view.HasAuthority(entity, DescriptorOrdinal)) continue;

        if (!SmartEgressUtil.ShouldPublish(view, entity, DescriptorOrdinal, isUnreliable: false))
            continue;

        ref readonly var sensor = ref view.GetComponentRO<EqsSensor>(entity);
        ref readonly var netId  = ref view.GetComponentRO<NetworkIdentity>(entity);

        _writer.Write(new EqsSensorConfigTopic   // actual type from EqsDdsTopics.cs
        {
            EntityId        = netId.Value,
            BlueprintId     = sensor.BlueprintId,
            Epoch           = sensor.Epoch,
            SearchRadius    = sensor.SearchRadius,
            FactionFilter   = sensor.FactionFilter,
            ThreatThreshold = sensor.ThreatThreshold,
            PublishPolicy   = sensor.PublishPolicy,
            Priority        = sensor.Priority,
        });

        SentSampleCount++;
        SmartEgressUtil.MarkPublished(view, entity, DescriptorOrdinal);
    }
}
```

You need to update the constructor to store the writer:
```csharp
private readonly DdsWriter<EqsSensorConfigTopic>? _writer;

public EqsSensorConfigEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
{
    if (participant == null) throw new ArgumentNullException(nameof(participant));
    _writer = new DdsWriter<EqsSensorConfigTopic>(participant, DdsTopicName);
}
```

Also update `Dispose` to properly dispose the writer instance:
```csharp
public void Dispose(long networkEntityId)
{
    _writer?.DisposeInstance(new EqsSensorConfigTopic { EntityId = networkEntityId });
}
```

Required usings to add: `Fdp.Toolkit.Replication.Utilities`, `Fdp.Toolkit.Spatial.Eqs`, `Fdp.Toolkit.Spatial.Eqs.Topics`.

### 2. EqsSensorConfigIngressTranslator (Muscle ← Brain)

Muscle-side. Replaces `throw new NotImplementedException(...)` in `PollIngress`.

Updates needed:
- Store `_reader` in constructor
- Implement PollIngress

```csharp
private readonly DdsReader<EqsSensorConfigTopic>? _reader;
private readonly NetworkEntityMap _entityMap;

public EqsSensorConfigIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
{
    if (entityMap == null) throw new ArgumentNullException(nameof(entityMap));
    _entityMap = entityMap;
    _reader = participant != null ? new DdsReader<EqsSensorConfigTopic>(participant, DdsTopicName) : null;
}

public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
{
    if (_reader is null) return;

    using var loan = _reader.Take();
    foreach (var sample in loan)
    {
        long entityId = sample.IsValid
            ? sample.Data.EntityId
            : sample.Info.InstanceHandle; // use handle for disposed samples

        if (!_entityMap.TryGetEntity(entityId, out var entity)) continue;

        if (sample.IsValid)
        {
            ReceivedSampleCount++;
            cmd.SetComponent(entity, new EqsSensor
            {
                BlueprintId     = sample.Data.BlueprintId,
                Epoch           = sample.Data.Epoch,
                SearchRadius    = sample.Data.SearchRadius,
                FactionFilter   = sample.Data.FactionFilter,
                ThreatThreshold = sample.Data.ThreatThreshold,
                PublishPolicy   = sample.Data.PublishPolicy,
                Priority        = sample.Data.Priority,
            });
        }
        else if (sample.Info.InstanceState == DdsInstanceState.NotAliveDisposed)
        {
            cmd.RemoveComponent<EqsSensor>(entity);
        }
    }
}
```

**NOTE:** For the `entityId` extraction from disposed samples, look at how `EntityMissionIngressTranslator` does it. Copy the exact approach from that file since the DDS instance handle extraction pattern may vary.

Required usings: `Fdp.Toolkit.Spatial.Eqs`, `Fdp.Toolkit.Spatial.Eqs.Topics`.

### 3. EqsResultEventEgressTranslator (Muscle → Brain)

Muscle-side. Reads `EqsResultEvent` from local bus, dereferences `EqsResultPool`, builds `List<EqsResultEntry>` payload, writes `EqsResultTopic`.

Updates needed:
- Store writer in constructor
- Implement ScanAndPublish

```csharp
private readonly DdsWriter<EqsResultTopic>? _writer;
private readonly NetworkEntityMap _entityMap;

public EqsResultEventEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
{
    if (participant == null) throw new ArgumentNullException(nameof(participant));
    if (entityMap == null) throw new ArgumentNullException(nameof(entityMap));
    _writer = new DdsWriter<EqsResultTopic>(participant, DdsTopicName);
    _entityMap = entityMap;
}

public void ScanAndPublish(ISimulationView view)
{
    if (_writer is null) return;
    if (view is not EntityRepository repo) return;
    if (!repo.HasSingletonUnmanaged<EqsResultPool>()) return;

    var events = view.ReadEvents<EqsResultEvent>();
    if (events.IsEmpty) return;

    ref readonly var pool = ref repo.GetSingletonUnmanaged<EqsResultPool>();

    for (int ei = 0; ei < events.Length; ei++)
    {
        ref readonly var evt = ref events[ei];

        // Build the managed DDS payload from the unmanaged pool slice.
        var entries = new List<EqsResultEntry>(evt.EntryCount);
        for (int i = 0; i < evt.EntryCount; i++)
        {
            ref readonly var r = ref pool.Results[evt.ResultHandle + i];

            // For entity-shaped results, translate local EntityId -> NetworkId.
            // EntityId = 0 means positional candidate (no translation needed).
            // EntityId = -1L means rejected (should never appear in the pool).
            long resolvedNetId = 0L;
            if (r.EntityId != 0L && r.EntityId != -1L)
            {
                var targetEntity = new Entity((ulong)r.EntityId);
                _entityMap.TryGetNetworkId(targetEntity, out resolvedNetId);
            }

            entries.Add(new EqsResultEntry
            {
                EntityId  = resolvedNetId,
                PositionX = r.PositionX,
                PositionY = r.PositionY,
                Score     = r.Score,
                Flags     = (ushort)r.Flags,
            });
        }

        _writer.Write(new EqsResultTopic
        {
            SensorNetworkId = evt.SensorNetworkId,
            Epoch           = evt.Epoch,
            RefreshTick     = evt.RefreshTick,
            Results         = entries,
        });

        SentSampleCount++;
    }
}
```

Required usings: `System.Collections.Generic`, `Fdp.Toolkit.Spatial.Eqs`, `Fdp.Toolkit.Spatial.Eqs.Topics`.

**Note:** `EqsResultEntry` in the `EqsResultTopic.Results` list is `Fdp.Toolkit.Spatial.Eqs.Topics.EqsResultEntry`. This is separate from `Fdp.Toolkit.Spatial.Eqs.EqsResult` (the internal struct). Check field names match.

### 4. EqsResultIngressTranslator (Brain ← Muscle)

Brain-side. Reads `EqsResultTopic` from DDS, maps SensorNetworkId to local Brain entity, publishes `EqsResultUpdateEvent` on the managed bus.

Updates needed:
- Store reader and bus in constructor
- Implement PollIngress

```csharp
private readonly DdsReader<EqsResultTopic>? _reader;
private readonly NetworkEntityMap _entityMap;

// The bus is needed to publish EqsResultUpdateEvent (managed event).
// Get it from the EntityRepository during PollIngress via view.
// Do NOT store the bus in the constructor — rely on the view's repository.

public EqsResultIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
{
    if (entityMap == null) throw new ArgumentNullException(nameof(entityMap));
    _entityMap = entityMap;
    _reader = participant != null ? new DdsReader<EqsResultTopic>(participant, DdsTopicName) : null;
}

public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
{
    if (_reader is null) return;
    if (view is not EntityRepository repo) return;

    using var loan = _reader.Take();
    foreach (var sample in loan)
    {
        if (!sample.IsValid) continue;

        ReceivedSampleCount++;
        var data = sample.Data;

        if (!_entityMap.TryGetEntity(data.SensorNetworkId, out var observer)) continue;

        // Bridge to the managed event bus so EqsResultUpdateSystem can consume it.
        // EqsResultTopic.Results is List<EqsResultEntry> (from Fdp.Toolkit.Spatial.Eqs.Topics).
        // EqsResultUpdateEvent.Results is List<EqsResultEntry> (same type).
        repo.Bus.PublishManaged(new EqsResultUpdateEvent
        {
            Observer    = observer,
            Epoch       = data.Epoch,
            RefreshTick = data.RefreshTick,
            Results     = data.Results,  // same type, direct assign
        });
    }
}
```

Required usings: `Fdp.Toolkit.Spatial.Eqs.Topics`, `Hrot.SimHost.Systems`.

### 5. Registration in SimHostAuxiliaryTranslatorPack

Add the four EQS translators to `SimHostAuxiliaryTranslatorPack.Create(...)`. Follow the existing AreaQuery role-gating pattern:
- Brain role: `EqsSensorConfigEgressTranslator` (Brain sends config) + `EqsResultIngressTranslator` (Brain receives results)
- Muscle role: `EqsSensorConfigIngressTranslator` (Muscle receives config) + `EqsResultEventEgressTranslator` (Muscle sends results)

In the Brain block (where `AreaQueryBrainEgressTranslator` is):
```csharp
// EQS pipeline — Brain side.
translators.Add(new EqsSensorConfigEgressTranslator(participant, entityMap));
translators.Add(new EqsResultIngressTranslator(participant, entityMap));
```

In the Muscle block (where `AreaQueryMuscleIngressTranslator` is):
```csharp
// EQS pipeline — Muscle side.
translators.Add(new EqsSensorConfigIngressTranslator(participant, entityMap));
translators.Add(new EqsResultEventEgressTranslator(participant, entityMap));
```

### Tests for TASK-EQS-007

Create `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsTranslatorTests.cs`.

Use a **private static atomic counter starting at 300** (not the harness default counter) to avoid domain conflicts:
```csharp
private static int _domainBase = 299;
private static int NextDomain() => System.Threading.Interlocked.Increment(ref _domainBase);
```

Test T8 — Config replication (Brain → Muscle):
- Create `HrotRunnerHarness("simhost,cgf", NextDomain())`
- Cgf creates an entity with `NetworkIdentity` (use existing entity creation pattern)
- Add `EqsSensor { BlueprintId=1, Epoch=1, SearchRadius=25f }` to the Brain entity
- `PumpUntil` (timeout 10s): Muscle side — find ghost entity by networkId, check it has `EqsSensor` component with matching `SearchRadius`
- Assert sensor was replicated

Test T9 — Result round-trip (offline stub → Brain IsReady):
- Same harness (or new one), create entity with sensor
- `PumpUntil` (timeout 10s): Brain entity has `EqsCognitiveBuffer` with `IsReady == true`
- This tests that: config egress → solver emits EqsResultEvent → result egress → result ingress publishes EqsResultUpdateEvent → EqsResultUpdateSystem → buffer

Test T10 — NotAliveDisposed (Brain removes sensor → Muscle loses it):
- After T8 is satisfied, remove `EqsSensor` from Brain entity
- `PumpUntil` (timeout 10s): Muscle ghost entity no longer has `EqsSensor`
- This verifies the `NotAliveDisposed` path

**To find the Muscle ghost entity from a Brain networkId, use `SimHost.SimHostApp` or look at what test hook exists. Look at existing AreaQueryTranslatorTests.cs for the pattern used in this project.**

---

## TASK-EQS-008 — Core Query Interfaces

### Context

Define the abstraction layer that future generator and test implementations will target. All types go in `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/`. Create a single new file: `EqsQueryTemplate.cs`.

### New File: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsQueryTemplate.cs`

```csharp
using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Explicit execution phases for EQS tests.
    /// Tests execute in enum order; top-K reduction occurs between FilterExpensive and ScoreCheap.
    /// </summary>
    public enum EqsTestPhase : byte
    {
        /// <summary>Fast data-driven filters (faction, FOV). No allocations. Reject with EntityId = -1L.</summary>
        FilterCheap = 0,
        /// <summary>Slow filters (navmesh reachability). Reject with EntityId = -1L.</summary>
        FilterExpensive = 1,
        /// <summary>Fast scoring (distance falloff). Additive to EqsResult.Score.</summary>
        ScoreCheap = 2,
        /// <summary>Slow scoring (accurate LOS, path cost). Additive to EqsResult.Score.</summary>
        ScoreExpensive = 3,
    }

    /// <summary>
    /// Generates the initial set of EQS candidates (entity-shaped or positional).
    /// Must operate on the provided span with zero heap allocation.
    /// </summary>
    public interface IEqsGenerator
    {
        /// <summary>
        /// Fills <paramref name="candidates"/> with initial results and returns the valid count.
        /// Entity-shaped results store <c>entity.PackedValue</c> in EntityId.
        /// Positional results set EntityId = 0.
        /// </summary>
        int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates);
    }

    /// <summary>
    /// Filters or scores a batch of EQS candidates in-place.
    /// All operations must be zero-allocation.
    /// </summary>
    public interface IEqsTest
    {
        /// <summary>The phase in which this test executes.</summary>
        EqsTestPhase Phase { get; }

        /// <summary>
        /// Executes the test over <paramref name="candidates"/>.
        /// Filters reject by setting EntityId = -1L.
        /// Scorers accumulate into EqsResult.Score additively.
        /// </summary>
        void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates);
    }

    /// <summary>
    /// Compiled representation of an EQS query blueprint. Struct to allow stack allocation.
    /// Tests are split by phase; null arrays are treated as empty (no tests in that phase).
    /// </summary>
    public struct EqsQueryTemplate
    {
        /// <summary>FNV-1a 32-bit hash of the template AssetId GUID.</summary>
        public uint BlueprintId;

        /// <summary>Produces the initial candidate span.</summary>
        public IEqsGenerator Generator;

        /// <summary>Fast filter tests. Run before FilterExpensive.</summary>
        public IEqsTest[]? FilterCheap;

        /// <summary>Slow filter tests. Run before top-K reduction.</summary>
        public IEqsTest[]? FilterExpensive;

        /// <summary>Fast scoring tests. Run after top-K reduction.</summary>
        public IEqsTest[]? ScoreCheap;

        /// <summary>Slow scoring tests. Run last.</summary>
        public IEqsTest[]? ScoreExpensive;

        /// <summary>Maximum candidates the generator may populate. Must be <= EqsResultPool.MaxTopK * some factor.</summary>
        public int MaxCandidates;
    }

    /// <summary>
    /// Registry allowing the solver to look up a compiled template by BlueprintId.
    /// </summary>
    public interface IEqsTemplateRegistry
    {
        /// <summary>
        /// Returns true and sets <paramref name="template"/> if a template with
        /// the given <paramref name="blueprintId"/> is registered.
        /// </summary>
        bool TryGetTemplate(uint blueprintId, out EqsQueryTemplate template);
    }

    /// <summary>
    /// Attribute marking a class as an EQS query template for the source generator.
    /// The <c>AssetId</c> GUID is hashed to produce the <c>BlueprintId</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class EqsTemplateAttribute : Attribute
    {
        /// <summary>GUID string of the template asset (used to compute BlueprintId).</summary>
        public string AssetId { get; }

        public EqsTemplateAttribute(string assetId)
        {
            AssetId = assetId ?? throw new ArgumentNullException(nameof(assetId));
        }
    }

    /// <summary>
    /// Optional abstract base for EQS templates. Provides no runtime behavior;
    /// templates may directly implement the <c>Build</c> pattern without inheriting this.
    /// </summary>
    public abstract class EqsTemplateBase
    {
        // Subclasses should provide: public static EqsQueryTemplate Build() { ... }
        // The purity analyzer (Phase 6, TASK-EQS-020) will enforce this at compile time.
    }
}
```

### Tests for TASK-EQS-008

Create `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/EqsQueryTemplateTests.cs`.

Tests:
1. `EqsTestPhase_ValuesAreCorrect` — assert `FilterCheap=0, FilterExpensive=1, ScoreCheap=2, ScoreExpensive=3`
2. `EqsQueryTemplate_CanBeComposedWithTrivialGeneratorAndTest` — create a trivial IEqsGenerator that hardcodes 2 results, a trivial IEqsTest (FilterCheap) that rejects index 0, compose into EqsQueryTemplate, call Generate then ExecuteBatch manually, assert only 1 candidate remains. This is a pure unit test with no ECS/harness.
3. `IEqsTemplateRegistry_TryGetTemplate_ReturnsFalseForUnknownId` — create a minimal registry implementation (dictionary-backed), try to get an unregistered ID, assert returns false.
4. `EqsTemplateAttribute_StoresAssetId` — instantiate `[EqsTemplate("test-guid")]`, assert AssetId == "test-guid".

---

## Build and Test Verification

After implementing all changes:

1. `dotnet build IOS-IG-SimHost.sln` — must succeed with 0 errors
2. `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/ --filter "FullyQualifiedName~EqsQueryTemplate"` — 4 tests must pass
3. `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --filter "FullyQualifiedName~EqsTranslator"` — 3 tests must pass (T8, T9, T10)
4. `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --filter "FullyQualifiedName~Eqs"` — all previous 7 EQS tests must still pass

---

## Key Constraints

- **Do NOT use `DdsEqsSensorConfig` / `DdsEqsResult`** — use `EqsSensorConfigTopic` / `EqsResultTopic` from `Fdp.Toolkit.Spatial.Eqs.Topics`.
- **Rejection sentinel is `-1L` (not `0`)** — `0` is a valid positional EntityId.
- `EqsResultTopic.Results` is `List<EqsResultEntry>` where `EqsResultEntry` is from `Fdp.Toolkit.Spatial.Eqs.Topics`.
- `EqsResultUpdateEvent.Results` is also `List<EqsResultEntry>` from the same namespace — direct assignment works.
- SmartEgressUtil: `ShouldPublish` must be called before writing; `MarkPublished` must be called after writing.
- Authority check: `if (!view.HasAuthority(entity, DescriptorOrdinal)) continue;` on the egress translator.
- `EqsSensor`, `EqsCognitiveBuffer`, `EqsResultPool` — all in `Fdp.Toolkit.Spatial.Eqs` namespace.
- DDS writer/reader is nullable; guard all calls with `if (_writer is null) return;` / `if (_reader is null) return;`.
- For the result egress translator: pool access is `ref readonly var pool = ref repo.GetSingletonUnmanaged<EqsResultPool>();`.
- `HrotRunnerHarness` integration tests use domain IDs starting at 300 (private static counter in the test class).

---

## Report

Write the report to `.dev/eqs-2/reports/BATCH-03-REPORT.md` with:
1. Summary per task
2. All test results (pass/fail, count)
3. Issues and resolutions
4. Design decisions (especially for ingress entity lookup and NotAliveDisposed handling)
5. Suggested commit message
