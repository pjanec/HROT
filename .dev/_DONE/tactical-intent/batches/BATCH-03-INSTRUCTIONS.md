# BATCH-03 Instructions

**Batch:** BATCH-03  
**Status:** Ready for implementation  
**Tasks:** TASK-TI007, TASK-TI008, TASK-TI009  
**Goal:** Add the DDS wire format for tactical intents and the egress/ingress translator pair.

---

## Mandatory Reading

Before writing any code, read:

1. `.dev/tactical-intent/DESIGN.md` §5 — Network Transport design
2. `.dev/tactical-intent/TASK-DETAIL.md` — detailed success conditions for TI007, TI008, TI009
3. `Hrot/Network/Hrot.Network.NED/AllDescriptors.cs` — EDescriptorType enum to extend
4. `Hrot/Network/Hrot.Network.NED/MissionDescriptors.cs` — pattern for `[DdsStruct][DdsIdlFile][DdsManaged]`
5. `Hrot/Network/Hrot.Network.NED/SimHost/MissionControlAckEgressTranslator.cs` — egress pattern
6. `Hrot/Network/Hrot.Network.NED/SimHost/MissionControlIngressTranslator.cs` — ingress pattern
7. `Hrot/Network/Hrot.Network.NED/SimHost/WeaponFireIntentEgressTranslator.cs` — egress with NetworkEntityMap + authority
8. `Hrot/Network/Hrot.Network.NED/SimHost/WeaponFireRequestIngressTranslator.cs` — ingress with ProcessSample
9. `Hrot/Network/Hrot.Network.NED/SimHost/SimHostAuxiliaryTranslatorPack.cs` — registration site
10. `Hrot/Subsystems/Hrot.SimHost.Tests/WeaponFireIntentEgressTranslatorTests.cs` — egress test pattern

---

## Sequence

Implement tasks in this order. Fix any build or test failures before continuing.

1. TASK-TI007 — DDS struct + enum value (no behavioral code, good baseline check)
2. TASK-TI008 — Egress translator
3. TASK-TI009 — Ingress translator

---

## TASK-TI007 — TacticalIntentRequest DDS Struct and EDescriptorType

### Step 1 — Add descriptor ordinal

**File:** `Hrot/Network/Hrot.Network.NED/AllDescriptors.cs`

Add `dtTacticalIntentRequest = 92` immediately after `dtMissionControlAck = 91`:

```csharp
        // Mission control
        dtMissionControlRequest = 90,
        dtMissionControlAck     = 91,
        // Tactical intent (Brain-to-Brain)
        dtTacticalIntentRequest = 92,
```

No other values may be changed.

### Step 2 — Define TacticalIntentRequest struct

**File:** `Hrot/Network/Hrot.Network.NED/TacticalIntentMessages.cs` (new file)

```csharp
using System.Collections.Generic;
using CycloneDDS.Schema;

namespace Hrot.NED.Messages
{
    /// <summary>
    /// DDS wire message for broadcasting a tactical intent from a Commander Brain node
    /// to a subordinate Brain node.
    ///
    /// <para>
    /// Transported on the <c>"TacticalIntentRequest"</c> DDS topic
    /// (ordinal <see cref="Hrot.NED.Descriptors.EDescriptorType.dtTacticalIntentRequest"/> = 92).
    /// </para>
    ///
    /// <para>
    /// The receiver (<see cref="TacticalIntentIngressTranslator"/>) resolves
    /// <see cref="TargetEntityId"/> to a local ECS <c>Entity</c> via
    /// <c>NetworkEntityMap</c> and publishes an <c>AssignTacticalIntentEvent</c>
    /// on the local bus for <c>TacticalIntentResolutionSystem</c> to process.
    /// </para>
    /// </summary>
    [DdsStruct]
    [DdsIdlFile("hrot-tactical-intent")]
    [DdsManaged]
    public partial struct TacticalIntentRequest
    {
        /// <summary>Network entity ID of the subordinate entity receiving the intent.</summary>
        public long TargetEntityId;

        /// <summary>Generic intent identifier, e.g. <c>"DefendArea"</c>.</summary>
        public string IntentId;

        /// <summary>JSON-serialized intent parameters matching the target DTO.</summary>
        public string JsonParams;
    }
}
```

> **Why `partial`?** CycloneDDS source-generators extend DDS structs with marshaling code in a `partial` class. All other DDS structs in the codebase use `partial`.

### Tests for TI007

**File:** `Hrot/Network/Hrot.Network.NED.Tests/TacticalIntentMessageTests.cs` (new file)

```csharp
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Xunit;

namespace Hrot.DDS.DataModel.Tests
{
    public class TacticalIntentMessageTests
    {
        // SC-1: Enum value is 92
        [Fact]
        public void EDescriptorType_TacticalIntentRequest_Value_Is92()
        {
            Assert.Equal(92, (int)EDescriptorType.dtTacticalIntentRequest);
        }

        // SC-2: Struct can be instantiated and fields accessed
        [Fact]
        public void TacticalIntentRequest_CanBeInstantiated_FieldsAccessible()
        {
            var msg = new TacticalIntentRequest
            {
                TargetEntityId = 42L,
                IntentId       = "DefendArea",
                JsonParams     = "{\"radius\":100}",
            };
            Assert.Equal(42L, msg.TargetEntityId);
            Assert.Equal("DefendArea", msg.IntentId);
            Assert.Equal("{\"radius\":100}", msg.JsonParams);
        }
    }
}
```

Test project: `Hrot/Network/Hrot.Network.NED.Tests/Hrot.Network.NED.Tests.csproj`

---

## TASK-TI008 — TacticalIntentEgressTranslator

### Overview

The egress translator runs on a Commander Brain node. It:
1. Reads all `AssignTacticalIntentEvent` managed events from the local bus.
2. For each event, checks `!repo.HasAuthority<BehaviorState>(evt.Entity)`.
   - `true` (no local authority) → the entity's brain is on another node — write to DDS.
   - `false` (has local authority) → `TacticalIntentResolutionSystem` handles it locally — skip.
3. Resolves the local `Entity` to its `TargetEntityId` via `NetworkEntityMap.TryGetNetworkId`.
4. Writes `TacticalIntentRequest` to DDS.

### Key design differences from WeaponFireIntentEgressTranslator

| Aspect | WeaponFireIntentEgressTranslator | TacticalIntentEgressTranslator |
|--------|---|---|
| Event type | Struct event (`WeaponFireIntent`) | Managed event (`AssignTacticalIntentEvent`) |
| Read API | `view.ReadEvents<T>()` | cast to `EntityRepository`, then `repo.Bus.ReadManaged<T>()` |
| Authority component | `NetworkAuthority` | `BehaviorState` |
| Authority API | `view.HasAuthority(entity)` | `repo.HasAuthority<BehaviorState>(entity)` |

### File to create

**`Hrot/Network/Hrot.Network.NED/SimHost/TacticalIntentEgressTranslator.cs`**

```csharp
using System;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Replication.Services;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// Commander Brain egress translator: reads <see cref="AssignTacticalIntentEvent"/>
    /// managed events from the local bus and writes a <see cref="TacticalIntentRequest"/>
    /// DDS message for each event whose target entity is NOT owned by the local Brain node.
    ///
    /// <para>
    /// <b>Authority gate:</b> Only publishes when
    /// <c>!repo.HasAuthority&lt;BehaviorState&gt;(evt.Entity)</c>.
    /// Locally-owned entities are handled by <c>TacticalIntentResolutionSystem</c>
    /// in the same frame; no DDS traffic needed.
    /// </para>
    /// </summary>
    public sealed class TacticalIntentEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "TacticalIntentRequest";

        private readonly IDdsWriter<TacticalIntentRequest> _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtTacticalIntentRequest;
        public long   ReceivedSampleCount { get; private set; }
        public long   SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        /// <summary>Production constructor — creates a live DDS writer.</summary>
        public TacticalIntentEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
            : this(new DdsWriterAdapter<TacticalIntentRequest>(participant, DdsTopicName), entityMap)
        {
        }

        /// <summary>Internal test constructor — accepts a stub writer.</summary>
        internal TacticalIntentEgressTranslator(
            IDdsWriter<TacticalIntentRequest> writer,
            NetworkEntityMap entityMap)
        {
            _writer    = writer    ?? throw new ArgumentNullException(nameof(writer));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view)
        {
            if (view is not EntityRepository repo) return;

            var events = repo.Bus.ReadManaged<AssignTacticalIntentEvent>();

            foreach (var evt in events)
            {
                if (evt is null) continue;

                // Authority gate: locally-owned entities are resolved by
                // TacticalIntentResolutionSystem in the same frame.
                if (repo.HasAuthority<BehaviorState>(evt.Entity)) continue;

                if (!_entityMap.TryGetNetworkId(evt.Entity, out long networkId))
                {
                    FdpLog<TacticalIntentEgressTranslator>.Warn(
                        "[TacticalIntentEgress] Entity #{0} not in NetworkEntityMap — skipping intent.",
                        evt.Entity.Index);
                    continue;
                }

                _writer.Write(new TacticalIntentRequest
                {
                    TargetEntityId = networkId,
                    IntentId       = evt.IntentId,
                    JsonParams     = evt.JsonParams,
                });
                SentSampleCount++;
            }
        }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}
```

### Registration in SimHostAuxiliaryTranslatorPack.cs

Inside the `if (role.HasFlag(NodeRole.Brain))` block, after the existing `MissionControlAckEgressTranslator` registration, add both egress and ingress:

```csharp
if (role.HasFlag(NodeRole.Brain))
{
    // PACK-P001: mission control ingress polls DDS, egress writes ACKs.
    translators.Add(new MissionControlIngressTranslator(participant));
    translators.Add(new MissionControlAckEgressTranslator(participant));
    // Tactical intent: egress from Commander Brain, ingress on subordinate Brain.
    translators.Add(new TacticalIntentEgressTranslator(participant, entityMap));
    translators.Add(new TacticalIntentIngressTranslator(participant, entityMap));
}
```

> **Note:** Both translators are added unconditionally inside the Brain block. A Brain node that is also a Commander will write egress. A Brain node that is also a subordinate will read ingress. A Brain that is neither will have empty event reads / empty DDS samples — harmless.

### Tests for TI008

**File:** `Hrot/Subsystems/Hrot.SimHost.Tests/TacticalIntentEgressTranslatorTests.cs` (new file)

Model this closely on `WeaponFireIntentEgressTranslatorTests.cs`. Key differences:
- Use `BehaviorState` (not `NetworkAuthority`) for authority
- Use `repo.SetAuthority<BehaviorState>(entity, true/false)` to control authority
- Use `repo.Bus.PublishManaged(new AssignTacticalIntentEvent {...})` + `SwapBuffers()` then call `ScanAndPublish(repo)`
- The `CapturingWriter<TacticalIntentRequest>` stub (same pattern, different type)

```csharp
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Replication.Services;
using Hrot.NED.Messages;
using Hrot.Network.NED.SimHost;
using Xunit;

namespace Hrot.SimHost.Tests
{
    public class TacticalIntentEgressTranslatorTests : IDisposable
    {
        private sealed class CapturingWriter<T> : IDdsWriter<T>
        {
            public List<T> Written { get; } = new();
            public void Write(T sample) => Written.Add(sample);
            public void DisposeInstance(T key) { }
        }

        private readonly EntityRepository _world;
        private readonly NetworkEntityMap _entityMap;

        public TacticalIntentEgressTranslatorTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<BehaviorState>();
            _entityMap = new NetworkEntityMap();
        }

        public void Dispose() => _world.Dispose();

        private (TacticalIntentEgressTranslator translator, CapturingWriter<TacticalIntentRequest> writer)
            BuildTranslator()
        {
            var writer     = new CapturingWriter<TacticalIntentRequest>();
            var translator = new TacticalIntentEgressTranslator(writer, _entityMap);
            return (translator, writer);
        }

        // SC-1: Entity in map, no BehaviorState authority → DDS write happens
        [Fact]
        public void ScanAndPublish_NoAuthority_WritesDdsSample()
        {
            var (translator, writer) = BuildTranslator();

            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new BehaviorState());
            // Authority NOT set (remote entity)
            _entityMap.Register(42L, entity);

            _world.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = entity,
                IntentId   = "DefendArea",
                JsonParams = "{}",
            });
            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            Assert.Equal(1, writer.Written.Count);
            Assert.Equal(42L, writer.Written[0].TargetEntityId);
            Assert.Equal("DefendArea", writer.Written[0].IntentId);
            Assert.Equal(1, translator.SentSampleCount);
        }

        // SC-2: Entity NOT in NetworkEntityMap → no DDS write
        [Fact]
        public void ScanAndPublish_EntityNotInMap_NoDdsWrite()
        {
            var (translator, writer) = BuildTranslator();

            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new BehaviorState());
            // Entity not registered in entityMap

            _world.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = entity,
                IntentId   = "DefendArea",
                JsonParams = "{}",
            });
            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            Assert.Empty(writer.Written);
            Assert.Equal(0, translator.SentSampleCount);
        }

        // SC-3: Two events, no authority for either → two DDS writes
        [Fact]
        public void ScanAndPublish_TwoEvents_NoAuthority_TwoWrites()
        {
            var (translator, writer) = BuildTranslator();

            var e1 = _world.CreateEntity();
            _world.AddComponent(e1, new BehaviorState());
            _entityMap.Register(1L, e1);

            var e2 = _world.CreateEntity();
            _world.AddComponent(e2, new BehaviorState());
            _entityMap.Register(2L, e2);

            _world.Bus.PublishManaged(new AssignTacticalIntentEvent { Entity = e1, IntentId = "DefendArea", JsonParams = "{}" });
            _world.Bus.PublishManaged(new AssignTacticalIntentEvent { Entity = e2, IntentId = "ConvoyEscort", JsonParams = "{}" });
            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            Assert.Equal(2, writer.Written.Count);
            Assert.Equal(2, translator.SentSampleCount);
        }

        // SC-4: Entity HAS BehaviorState authority → no DDS write
        [Fact]
        public void ScanAndPublish_HasAuthority_NoDdsWrite()
        {
            var (translator, writer) = BuildTranslator();

            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new BehaviorState());
            _world.SetAuthority<BehaviorState>(entity, true);  // locally owned
            _entityMap.Register(99L, entity);

            _world.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = entity,
                IntentId   = "DefendArea",
                JsonParams = "{}",
            });
            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            Assert.Empty(writer.Written);
            Assert.Equal(0, translator.SentSampleCount);
        }
    }
}
```

> **Note on `IDdsWriter<T>`:** Use the same `using CycloneDDS.Runtime;` import as `WeaponFireIntentEgressTranslatorTests`. The `IDdsWriter<T>` interface is in `CycloneDDS.Runtime`.

---

## TASK-TI009 — TacticalIntentIngressTranslator

### Overview

The ingress translator runs on a subordinate Brain node. It:
1. Polls `TacticalIntentRequest` DDS samples.
2. For each valid sample, looks up the local `Entity` via `NetworkEntityMap.TryGetEntity(request.TargetEntityId, out entity)`.
3. If found, calls `repo.Bus.PublishManaged(new AssignTacticalIntentEvent { Entity, IntentId, JsonParams })`.
4. If the entity is not in the map, skips silently.

> **No authority check in ingress:** The ingress translator does not check `HasAuthority<BehaviorState>`. Authority verification happens downstream in `TacticalIntentResolutionSystem`. The ingress translator's job is only to transfer the wire message to the local bus.

### Pattern to follow

`WeaponFireRequestIngressTranslator` for the `ProcessSample` + `PollIngress` split.
`MissionControlIngressTranslator` for the `repo.Bus.PublishManaged` call pattern.

### File to create

**`Hrot/Network/Hrot.Network.NED/SimHost/TacticalIntentIngressTranslator.cs`**

```csharp
using System;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Replication.Services;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// Subordinate Brain ingress translator: polls <see cref="TacticalIntentRequest"/>
    /// DDS samples and publishes each as an <see cref="AssignTacticalIntentEvent"/> on
    /// the local bus for <c>TacticalIntentResolutionSystem</c> to process.
    ///
    /// <para>Entity ID mapping: the <c>long</c> ID in the DDS message is resolved to a
    /// local <see cref="Entity"/> handle via <see cref="NetworkEntityMap"/>.
    /// If the entity is absent the sample is silently skipped.</para>
    ///
    /// <para>No authority check: authority verification is handled downstream by
    /// <c>TacticalIntentResolutionSystem</c>.</para>
    /// </summary>
    public sealed class TacticalIntentIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "TacticalIntentRequest";

        private readonly DdsReader<TacticalIntentRequest>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtTacticalIntentRequest;
        public long   ReceivedSampleCount { get; private set; }
        public long   SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        /// <summary>
        /// Production constructor. Pass <c>null</c> for <paramref name="participant"/>
        /// in unit tests; <see cref="PollIngress"/> becomes a no-op.
        /// </summary>
        public TacticalIntentIngressTranslator(
            DdsParticipant? participant,
            NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _reader    = participant is not null
                ? new DdsReader<TacticalIntentRequest>(participant, DdsTopicName)
                : null;
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;

            var repo = view as EntityRepository;
            if (repo is null) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                var data = sample.Data;
                ProcessSample(in data, repo);
            }
        }

        /// <summary>
        /// Processes a single <see cref="TacticalIntentRequest"/> sample.
        /// Exposed as <c>internal</c> so unit tests can inject samples without a live DDS stack.
        /// </summary>
        internal void ProcessSample(in TacticalIntentRequest request, EntityRepository repo)
        {
            if (!_entityMap.TryGetEntity(request.TargetEntityId, out var entity)) return;

            repo.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = entity,
                IntentId   = request.IntentId   ?? string.Empty,
                JsonParams = request.JsonParams  ?? string.Empty,
            });
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}
```

> **Note on `DdsParticipant?` nullable constructor:** This is identical to `WeaponFireRequestIngressTranslator`. Pass `null` in tests; the reader is null; `PollIngress` returns immediately. Use `ProcessSample` to inject test samples directly.

> **Note on `TacticalIntentRequest` as DDS reader type:** `DdsReader<TacticalIntentRequest>` requires `TacticalIntentRequest` to be a `partial struct` with `[DdsManaged]`. This is satisfied by the definition in TASK-TI007.

### Registration

Already handled in TASK-TI008 above — both translators are added in the same `if (role.HasFlag(NodeRole.Brain))` block.

### Tests for TI009

**File:** `Hrot/Subsystems/Hrot.SimHost.Tests/TacticalIntentIngressTranslatorTests.cs` (new file)

```csharp
using Fdp.Core;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Replication.Services;
using Hrot.NED.Messages;
using Hrot.Network.NED.SimHost;
using Xunit;

namespace Hrot.SimHost.Tests
{
    public class TacticalIntentIngressTranslatorTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly NetworkEntityMap _entityMap;

        public TacticalIntentIngressTranslatorTests()
        {
            _world = new EntityRepository();
            _entityMap = new NetworkEntityMap();
        }

        public void Dispose() => _world.Dispose();

        // SC-1: Entity in map → AssignTacticalIntentEvent published
        [Fact]
        public void ProcessSample_EntityInMap_PublishesAssignTacticalIntentEvent()
        {
            var translator = new TacticalIntentIngressTranslator(null, _entityMap);

            var entity = _world.CreateEntity();
            _entityMap.Register(42L, entity);

            var sample = new TacticalIntentRequest
            {
                TargetEntityId = 42L,
                IntentId       = "DefendArea",
                JsonParams     = "{\"radius\":100}",
            };

            translator.ProcessSample(in sample, _world);
            _world.Bus.SwapBuffers();

            var events = _world.Bus.ReadManaged<AssignTacticalIntentEvent>();
            Assert.Single(events);
            Assert.Equal(entity, events[0].Entity);
            Assert.Equal("DefendArea", events[0].IntentId);
            Assert.Equal("{\"radius\":100}", events[0].JsonParams);
        }

        // SC-2: Entity NOT in map → no event published, no exception
        [Fact]
        public void ProcessSample_EntityNotInMap_NoEventPublished()
        {
            var translator = new TacticalIntentIngressTranslator(null, _entityMap);
            // No entities registered in _entityMap

            var sample = new TacticalIntentRequest
            {
                TargetEntityId = 99L,
                IntentId       = "DefendArea",
                JsonParams     = "{}",
            };

            var ex = Record.Exception(() =>
            {
                translator.ProcessSample(in sample, _world);
            });
            Assert.Null(ex);

            _world.Bus.SwapBuffers();
            var events = _world.Bus.ReadManaged<AssignTacticalIntentEvent>();
            Assert.Empty(events);
        }
    }
}
```

> **Note on `AssignTacticalIntentEvent` bus registration:** Check whether `AssignTacticalIntentEvent` needs to be registered with the bus before `ReadManaged<T>()` works. Look at `TacticalIntentResolutionSystemTests.cs` (BATCH-01) for the exact registration pattern. Do the same in `TacticalIntentIngressTranslatorTests`.

---

## Build and Test Sequence

After each task:

```
dotnet build IOS-IG-SimHost.sln --no-restore -v quiet 2>&1 | Select-String "error CS|Build succeeded|FAILED"
```

After all tasks:

```
dotnet test Hrot/Network/Hrot.Network.NED.Tests/Hrot.Network.NED.Tests.csproj --no-build --nologo 2>&1 | Select-String "Passed!|Failed!"
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build --nologo 2>&1 | Select-String "Passed!|Failed!"
```

Expected: 2 new in NED.Tests, 6 new in SimHost.Tests. Pre-existing failures (2 MissionPlanTranslator + 14 Toolkits) are OK.

---

## Report

Write report to: `.dev/tactical-intent/reports/BATCH-03-REPORT.md`

Follow the same format as BATCH-01-REPORT.md.
