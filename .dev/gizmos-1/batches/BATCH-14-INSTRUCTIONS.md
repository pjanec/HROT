# BATCH-14 Implementation Instructions

**Tasks:** GZ037, GZ038  
**Agent:** Claude Sonnet 4.6  
**Task details:** `.dev/gizmos-1/TASK-DETAIL.md` (GZ037, GZ038)  
**Tracker:** `.dev/gizmos-1/TASK-TRACKER.md`

---

## MANDATORY READING BEFORE STARTING

1. Read `.dev/gizmos-1/TASK-DETAIL.md` sections for GZ037 and GZ038.
2. Read `AGENTS.md` at workspace root for coding standards.
3. Read `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Events/GizmoInteractionEvents.cs` to understand event types.
4. Read `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/IDdsWriter.cs` for the existing writer interface pattern.
5. Read `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/DebugPrimitiveBuffer.cs` to understand the buffer structure.
6. Read `Hrot/Network/Hrot.Network.NED/IG/WeaponFireIngressTranslator.cs` for the existing NED translator pattern.

---

## Pre-existing Failures (Do NOT count against your work)

Known pre-existing failures (ignore):
- ~26 tests in `Fdp.Toolkits.Tests` (AimAndFire, MissionDirector, etc.)
- ~4 tests in `Hrot.IG.Tests` (CS011_ EntityInfoTranslator)
- ~3 tests in `Fdp.Presentation.Tests` (EntityInspectorPanelTests)
- ~20 tests in `Hrot.SimHost.Tests` (pre-existing integration failures)

---

## GZ037 — Networked GizmoInteractionEvent DDS Translators

### Step 1 — IDdsReader<T> interface

**File to create:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/IDdsReader.cs`

```csharp
namespace Fdp.Toolkit.Diagnostics.Gizmos.Network
{
    /// <summary>
    /// Minimal read-only DDS subscriber abstraction for gizmo network components.
    /// Production code uses <c>CycloneDDS.Runtime.DdsReader&lt;T&gt;</c> wrapped in an adapter;
    /// unit tests supply a fake.
    /// </summary>
    public interface IDdsReader<T>
    {
        /// <summary>
        /// Attempts to read one pending sample. Returns <c>true</c> if a sample was available,
        /// <c>false</c> when the reader is empty or no-op.
        /// </summary>
        bool TryRead(out T sample);
    }
}
```

### Step 2 — GizmoInteractionEventKind enum

**File to create:** `Hrot/Network/Hrot.Network.NED/Gizmos/GizmoInteractionEventKind.cs`

```csharp
namespace Hrot.Network.NED.Gizmos
{
    public enum GizmoInteractionEventKind : byte
    {
        Started    = 0,
        DragUpdate = 1,
        Commit     = 2,
        Cancel     = 3,
    }
}
```

### Step 3 — GizmoInteractionBatch DDS topic

**File to create:** `Hrot/Network/Hrot.Network.NED/Gizmos/GizmoInteractionBatch.cs`

```csharp
using CycloneDDS.Schema;

namespace Hrot.Network.NED.Gizmos
{
    /// <summary>
    /// DDS topic that carries a single gizmo interaction event across the network.
    /// One record per event (not batched) because interactions are low-frequency.
    /// </summary>
    [DdsTopic("GizmoInteractionBatch")]
    [DdsQos(Reliability = DdsReliability.Reliable,
            Durability  = DdsDurability.Volatile,
            HistoryKind = DdsHistoryKind.KeepLast,
            HistoryDepth = 10)]
    public partial struct GizmoInteractionBatch
    {
        [DdsKey] public byte   SourceNodeId;
        [DdsKey] public uint   SequenceNumber;

        public GizmoInteractionEventKind Kind;

        // PickToken fields (blittable breakdown of Entity + SubElementId)
        public int    PickEntityIndex;
        public ushort PickEntityGeneration;
        public ushort PickSubElementId;

        // WorldPos (present for Started/DragUpdate/Commit; zero for Cancel)
        public float WorldX;
        public float WorldY;
        public float WorldZ;
    }
}
```

### Step 4 — GizmoInteractionEgressSystem (IG side)

**File to create:** `Hrot/Network/Hrot.Network.NED/Gizmos/GizmoInteractionEgressSystem.cs`

```csharp
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using System;

namespace Hrot.Network.NED.Gizmos
{
    /// <summary>
    /// IG-side ECS system that drains all gizmo interaction events from the local bus
    /// and forwards each as a <see cref="GizmoInteractionBatch"/> DDS record.
    /// Runs in PreSimulation so events generated in the UI thread are forwarded
    /// before the next ECS tick begins.
    /// </summary>
    [UpdateInPhase(SystemPhase.PreSimulation)]
    public sealed class GizmoInteractionEgressSystem : IEcsModuleSystem
    {
        private readonly byte _nodeId;
        private readonly IDdsWriter<GizmoInteractionBatch>? _writer;
        private uint _sequenceNumber;

        public GizmoInteractionEgressSystem(
            byte nodeId,
            IDdsWriter<GizmoInteractionBatch>? writer = null)
        {
            _nodeId = nodeId;
            _writer = writer;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (_writer == null) return;

            // Drain all four interaction event types.
            foreach (ref readonly var evt in view.ReadEvents<GizmoInteractionStartedEvent>())
                WriteRecord(GizmoInteractionEventKind.Started, evt.Token, evt.WorldPos);

            foreach (ref readonly var evt in view.ReadEvents<GizmoDragUpdateEvent>())
                WriteRecord(GizmoInteractionEventKind.DragUpdate, evt.Token, evt.WorldPos);

            foreach (ref readonly var evt in view.ReadEvents<GizmoInteractionCommitEvent>())
                WriteRecord(GizmoInteractionEventKind.Commit, evt.Token, evt.WorldPos);

            foreach (ref readonly var evt in view.ReadEvents<GizmoInteractionCancelEvent>())
                WriteRecord(GizmoInteractionEventKind.Cancel, evt.Token, System.Numerics.Vector3.Zero);
        }

        private void WriteRecord(
            GizmoInteractionEventKind kind,
            PickToken token,
            System.Numerics.Vector3 worldPos)
        {
            _writer!.Write(new GizmoInteractionBatch
            {
                SourceNodeId       = _nodeId,
                SequenceNumber     = _sequenceNumber++,
                Kind               = kind,
                PickEntityIndex    = token.Target.Index,
                PickEntityGeneration = (ushort)token.Target.Generation,
                PickSubElementId   = (ushort)token.SubElementId,
                WorldX             = worldPos.X,
                WorldY             = worldPos.Y,
                WorldZ             = worldPos.Z,
            });
        }
    }
}
```

**Check:** `PickToken` is in namespace `Fdp.Toolkit.Diagnostics.Gizmos`. Add:
```csharp
using Fdp.Toolkit.Diagnostics.Gizmos;
```

**Check `Entity` constructor:** Verify `new Entity(index, generation)` is the correct constructor  
by reading `FDP/Engine/Fdp.Core/Entity.cs`. Adapt if needed.

### Step 5 — GizmoInteractionIngressSystem (SimHost side)

**File to create:** `Hrot/Network/Hrot.Network.NED/Gizmos/GizmoInteractionIngressSystem.cs`

```csharp
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using System.Numerics;

namespace Hrot.Network.NED.Gizmos
{
    /// <summary>
    /// SimHost-side ECS system that reads pending <see cref="GizmoInteractionBatch"/> DDS
    /// records and publishes the appropriate typed interaction events to the local ECS bus.
    /// Runs in PreSimulation so gizmo systems see the events in the same frame.
    /// </summary>
    [UpdateInPhase(SystemPhase.PreSimulation)]
    public sealed class GizmoInteractionIngressSystem : IEcsModuleSystem
    {
        private readonly IDdsReader<GizmoInteractionBatch>? _reader;

        public GizmoInteractionIngressSystem(
            IDdsReader<GizmoInteractionBatch>? reader = null)
        {
            _reader = reader;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (_reader == null) return;
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(GizmoInteractionIngressSystem)} requires direct EntityRepository access.");

            while (_reader.TryRead(out var batch))
                Translate(repo, batch);
        }

        private static void Translate(EntityRepository repo, in GizmoInteractionBatch batch)
        {
            var entity = new Entity(batch.PickEntityIndex, batch.PickEntityGeneration);
            var worldPos = new Vector3(batch.WorldX, batch.WorldY, batch.WorldZ);
            var token = new PickToken
            {
                Target       = entity,
                SubElementId = batch.PickSubElementId,
            };

            bool alive = repo.IsAlive(entity);

            switch (batch.Kind)
            {
                case GizmoInteractionEventKind.Started:
                    repo.Bus.Publish(new GizmoInteractionStartedEvent { Token = token, WorldPos = worldPos });
                    break;

                case GizmoInteractionEventKind.DragUpdate:
                    if (!alive)
                        // Entity gone during drag — substitute cancel for safety.
                        repo.Bus.Publish(new GizmoInteractionCancelEvent { Token = token });
                    else
                        repo.Bus.Publish(new GizmoDragUpdateEvent { Token = token, WorldPos = worldPos });
                    break;

                case GizmoInteractionEventKind.Commit:
                    if (!alive)
                        repo.Bus.Publish(new GizmoInteractionCancelEvent { Token = token });
                    else
                        repo.Bus.Publish(new GizmoInteractionCommitEvent { Token = token, WorldPos = worldPos });
                    break;

                case GizmoInteractionEventKind.Cancel:
                    // Always forward cancel regardless of entity liveness.
                    repo.Bus.Publish(new GizmoInteractionCancelEvent { Token = token });
                    break;
            }
        }
    }
}
```

**Check `Entity` struct:** Look at `FDP/Engine/Fdp.Core/Entity.cs` for the correct constructor  
signature (may be `Entity(int index, int generation)` or `Entity(int index, ushort generation)`).  
Adapt the cast `(ushort)batch.PickEntityGeneration` accordingly.

### Tests for GZ037

**File to create:** `Hrot/Network/Hrot.Network.NED.Tests/GizmoInteractionTranslatorTests.cs`

First, read the existing test helpers in `Hrot.Network.NED.Tests/` to understand the test style.

**Test helpers needed:**
```csharp
private sealed class CapturingWriter : IDdsWriter<GizmoInteractionBatch>
{
    public List<GizmoInteractionBatch> Written = new();
    public void Write(GizmoInteractionBatch sample) => Written.Add(sample);
}

private sealed class SingleItemReader : IDdsReader<GizmoInteractionBatch>
{
    private readonly Queue<GizmoInteractionBatch> _items;
    public SingleItemReader(params GizmoInteractionBatch[] items) 
        => _items = new Queue<GizmoInteractionBatch>(items);
    public bool TryRead(out GizmoInteractionBatch sample)
    {
        if (_items.TryDequeue(out sample)) return true;
        sample = default;
        return false;
    }
}
```

**SC-GZ037-1**: `GizmoInteractionBatch` DDS schema compiles:
```csharp
[Fact]
public void SC_GZ037_1_GizmoInteractionBatch_HasDdsTopicAttribute()
{
    var attr = (DdsTopicAttribute?)Attribute.GetCustomAttribute(
        typeof(GizmoInteractionBatch), typeof(DdsTopicAttribute));
    Assert.NotNull(attr);
    Assert.Equal("GizmoInteractionBatch", attr!.TopicName);
}
```

**SC-GZ037-2**: Egress system writes `DragUpdate` record with correct fields:
```csharp
[Fact]
public void SC_GZ037_2_EgressSystem_Writes_DragUpdate_Correctly()
{
    using var repo = new EntityRepository();
    repo.RegisterEvent<GizmoInteractionStartedEvent>();
    repo.RegisterEvent<GizmoDragUpdateEvent>();
    repo.RegisterEvent<GizmoInteractionCommitEvent>();
    repo.RegisterEvent<GizmoInteractionCancelEvent>();

    var entity = repo.CreateEntity();
    var writer = new CapturingWriter();
    var sys = new GizmoInteractionEgressSystem(nodeId: 7, writer: writer);

    repo.Bus.Publish(new GizmoDragUpdateEvent
    {
        Token = new PickToken { Target = entity, SubElementId = 3 },
        WorldPos = new System.Numerics.Vector3(1f, 2f, 3f),
    });
    repo.Bus.SwapBuffers();
    sys.Execute(repo, 0f);

    Assert.Single(writer.Written);
    var record = writer.Written[0];
    Assert.Equal(GizmoInteractionEventKind.DragUpdate, record.Kind);
    Assert.Equal(7, record.SourceNodeId);
    Assert.Equal(entity.Index, record.PickEntityIndex);
    Assert.Equal(3u, record.PickSubElementId);
    Assert.Equal(1f, record.WorldX, precision: 4);
    Assert.Equal(2f, record.WorldY, precision: 4);
    Assert.Equal(3f, record.WorldZ, precision: 4);
}
```

**SC-GZ037-3**: Ingress translates `Commit` batch to `GizmoInteractionCommitEvent`:
```csharp
[Fact]
public void SC_GZ037_3_IngressSystem_Translates_Commit()
{
    using var repo = new EntityRepository();
    repo.RegisterEvent<GizmoInteractionStartedEvent>();
    repo.RegisterEvent<GizmoDragUpdateEvent>();
    repo.RegisterEvent<GizmoInteractionCommitEvent>();
    repo.RegisterEvent<GizmoInteractionCancelEvent>();
    var entity = repo.CreateEntity();

    var batch = new GizmoInteractionBatch
    {
        Kind = GizmoInteractionEventKind.Commit,
        PickEntityIndex = entity.Index,
        PickEntityGeneration = (ushort)entity.Generation,
        PickSubElementId = 5,
        WorldX = 10f, WorldY = 20f, WorldZ = 30f,
    };
    var reader = new SingleItemReader(batch);
    var sys = new GizmoInteractionIngressSystem(reader: reader);
    sys.Execute(repo, 0f);
    repo.Bus.SwapBuffers();

    var commits = repo.Bus.ReadEvents<GizmoInteractionCommitEvent>(); // NOTE: use the correct ReadEvents API
    // If ReadEvents requires ISimulationView pattern, adapt accordingly.
    // Alternative: call sys.Execute then SwapBuffers, then check via view.ReadEvents in another Execute.
}
```

**NOTE:** If `repo.Bus.ReadEvents<T>()` doesn't exist (the bus doesn't expose that directly), use
the ISimulationView pattern instead: publish to bus, swap, then read in next Execute call.
Look at the existing test patterns in `GizmosSystemTests.cs` to understand the correct approach:
- publish event → `repo.Bus.SwapBuffers()` → call system `Execute` → check side effects.

For ingress tests, the pattern is reversed: ingress system runs and publishes events to the bus → 
swap → create a consuming system or check directly.

**SC-GZ037-4**: Dead entity `DragUpdate` → cancel substitution:
```csharp
[Fact]
public void SC_GZ037_4_IngressSystem_DeadEntity_DragUpdate_YieldsCancelEvent()
{
    using var repo = new EntityRepository();
    repo.RegisterEvent<GizmoDragUpdateEvent>();
    repo.RegisterEvent<GizmoInteractionCancelEvent>();
    
    // Create entity then destroy it.
    var entity = repo.CreateEntity();
    var index = entity.Index;
    var gen = entity.Generation;
    repo.DestroyEntity(entity);
    repo.Bus.SwapBuffers(); // process destruction

    var batch = new GizmoInteractionBatch
    {
        Kind = GizmoInteractionEventKind.DragUpdate,
        PickEntityIndex = index,
        PickEntityGeneration = (ushort)gen,
    };
    var reader = new SingleItemReader(batch);
    var sys = new GizmoInteractionIngressSystem(reader: reader);
    sys.Execute(repo, 0f);

    // Verify: cancel was published instead of drag update.
    repo.Bus.SwapBuffers();
    // The next frame's ReadEvents should have CancelEvent.
    // Use a capturing approach matching existing test patterns.
}
```

Adapt the assertion approach based on `GizmosSystemTests.cs` patterns.

**SC-GZ037-5**: Cancel always forwarded:  
Write a test similar to SC-GZ037-4 but with `Kind = Cancel`. Even if entity is dead, cancel should
appear in the bus (not substituted).

**SC-GZ037-6**: Round-trip test — field preservation:
```csharp
[Fact]
public void SC_GZ037_6_GizmoInteractionBatch_FieldsPreserved()
{
    var batch = new GizmoInteractionBatch
    {
        SourceNodeId       = 3,
        SequenceNumber     = 42,
        Kind               = GizmoInteractionEventKind.DragUpdate,
        PickEntityIndex    = 100,
        PickEntityGeneration = 2,
        PickSubElementId   = 7,
        WorldX = 1.5f, WorldY = 2.5f, WorldZ = 3.5f,
    };

    Assert.Equal(3, batch.SourceNodeId);
    Assert.Equal(42u, batch.SequenceNumber);
    Assert.Equal(GizmoInteractionEventKind.DragUpdate, batch.Kind);
    Assert.Equal(100, batch.PickEntityIndex);
    Assert.Equal(2, batch.PickEntityGeneration);
    Assert.Equal(7, batch.PickSubElementId);
    Assert.Equal(1.5f, batch.WorldX);
    Assert.Equal(2.5f, batch.WorldY);
    Assert.Equal(3.5f, batch.WorldZ);
}
```

**SC-GZ037-7**: Null writer → egress returns without exception:
```csharp
[Fact]
public void SC_GZ037_7_EgressSystem_NullWriter_NoOp()
{
    using var repo = new EntityRepository();
    repo.RegisterEvent<GizmoDragUpdateEvent>();
    var sys = new GizmoInteractionEgressSystem(nodeId: 1, writer: null);
    repo.Bus.SwapBuffers();
    sys.Execute(repo, 0f); // must not throw
}
```

**SC-GZ037-8**: Null reader → ingress returns without exception:
```csharp
[Fact]
public void SC_GZ037_8_IngressSystem_NullReader_NoOp()
{
    using var repo = new EntityRepository();
    var sys = new GizmoInteractionIngressSystem(reader: null);
    sys.Execute(repo, 0f); // must not throw
}
```

---

## GZ038 — IG Dumb Terminal Ingress

### Step 1 — Add AppendRaw to DebugPrimitiveBuffer

**File to modify:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/DebugPrimitiveBuffer.cs`

Read the existing `internal void Append(DebugPrimitive p)` method (around line 219).  
Add a `public` version that skips persistence side-effects:

```csharp
/// <summary>
/// Appends a primitive directly into the transient buffer without persistence tracking.
/// Used by network ingress (<see cref="DebugPrimitivesIngressTranslator"/>) to restore
/// received primitives. Thread-safe (uses Interlocked).
/// </summary>
public void AppendRaw(in DebugPrimitive primitive)
{
    int slot = Interlocked.Increment(ref _count) - 1;
    if ((uint)slot < (uint)_primitives.Length)
        _primitives[slot] = primitive;
    else
        Interlocked.Increment(ref _droppedCount);
}
```

Place this method immediately after `Clear()` (in the "public API" section, before `EndFrame`).

**Critical:** Use `ref _droppedCount` with `Interlocked.Increment` — NOT `_droppedCount++` (race condition).

### Step 2 — Create DebugPrimitivesIngressTranslator

**File to create:** `Hrot/Network/Hrot.Network.NED/Gizmos/DebugPrimitivesIngressTranslator.cs`

```csharp
using System;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;

namespace Hrot.Network.NED.Gizmos
{
    /// <summary>
    /// Polls the DDS <see cref="DebugPrimitivesBatch"/> topic and applies the most recent
    /// batch to the local <see cref="DebugPrimitiveBuffer"/>, replacing its contents.
    /// Called from the Raylib render-loop thread (not the ECS thread).
    /// </summary>
    public sealed class DebugPrimitivesIngressTranslator
    {
        private readonly DebugPrimitiveBuffer _buffer;
        private readonly IDdsReader<DebugPrimitivesBatch>? _reader;
        private readonly byte? _filterNodeId;

        /// <param name="buffer">Target buffer to populate.</param>
        /// <param name="reader">DDS reader; null disables network ingress (local-only mode).</param>
        /// <param name="filterNodeId">When set, only batches with matching NodeId are applied.</param>
        public DebugPrimitivesIngressTranslator(
            DebugPrimitiveBuffer buffer,
            IDdsReader<DebugPrimitivesBatch>? reader = null,
            byte? filterNodeId = null)
        {
            _buffer       = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _reader       = reader;
            _filterNodeId = filterNodeId;
        }

        /// <summary>
        /// Drains all pending DDS samples, selects the latest matching one, and replaces the
        /// buffer contents with its primitives. Called every render tick.
        /// </summary>
        public void PollAndApply()
        {
            if (_reader == null) return;

            DebugPrimitivesBatch? latest = null;
            while (_reader.TryRead(out var batch))
            {
                if (_filterNodeId.HasValue && batch.NodeId != _filterNodeId.Value)
                    continue;
                latest = batch;
            }

            if (!latest.HasValue) return;

            _buffer.Clear();
            var primitives = latest.Value.Primitives;
            if (primitives == null) return;

            for (int i = 0; i < primitives.Length; i++)
                _buffer.AppendRaw(in primitives[i]);
        }
    }
}
```

### Step 3 — Modify IgApplication.cs (Remove ECS system registrations)

**File to modify:** `Hrot/Subsystems/Hrot.IG/IgApplication.cs`

**IMPORTANT:** Do NOT remove the `_gizmoRegistry` field or the `public GizmoRegistry? GizmoRegistry` property.  
These are used by `Hrot.IG.Tests` and would cause test failures. Only remove the ECS system registrations.

**Find and remove this block (around lines 1234-1244):**
```csharp
        // Gizmo system (GZ020) — must be registered before kernel.Initialize().
        if (_gizmoRegistry != null && _gizmoBuffer != null)
        {
            _kernel.RegisterGlobalSystem(new DataDrivenGizmoSystem(
                _gizmoRegistry,
                _gizmoBuffer,
                isSelectedPredicate: static (view, entity) =>
                    view.HasComponent<SelectionState>(entity) &&
                    view.GetComponentRO<SelectionState>(entity).IsSelected));
        }
```

Replace with:
```csharp
        // DataDrivenGizmoSystem is NOT registered in IG. IG is a dumb terminal.
        // Primitives arrive via DebugPrimitivesIngressTranslator (see _ingressTranslator).
        // GZ038: removed DataDrivenGizmoSystem registration.
```

**Find and remove this block (around lines 1246-1255):**
```csharp
        // Stateless gizmo system (GZ022) — runs projectors for each matching entity.
        if (_statelessGizmoRegistry != null && _gizmoBuffer != null)
        {
            _kernel.RegisterGlobalSystem(new StatelessGizmoSystem(
                _statelessGizmoRegistry,
                _gizmoBuffer,
                isSelectedPredicate: static (view, entity) =>
                    view.HasComponent<SelectionState>(entity) &&
                    view.GetComponentRO<SelectionState>(entity).IsSelected));
        }
```

Replace with:
```csharp
        // StatelessGizmoSystem is NOT registered in IG. IG is a dumb terminal.
        // GZ038: removed StatelessGizmoSystem registration.
```

**Note:** `GizmoRegistrar.Register` call at line ~1129 should remain — it populates `_gizmoRegistry`  
which is still exposed via the public property.

### Tests for GZ038

**File to create:** `Hrot/Network/Hrot.Network.NED.Tests/GizmoIngressTranslatorTests.cs`  
(or add to `GizmoInteractionTranslatorTests.cs` created for GZ037)

Also add a test for `AppendRaw` to  
`FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/DebugPrimitiveBufferPersistenceTests.cs`.

**SC-GZ038-1**: Most recent batch replaces buffer contents:
```csharp
private sealed class QueuedReader : IDdsReader<DebugPrimitivesBatch>
{
    private readonly Queue<DebugPrimitivesBatch> _items;
    public QueuedReader(params DebugPrimitivesBatch[] items)
        => _items = new Queue<DebugPrimitivesBatch>(items);
    public bool TryRead(out DebugPrimitivesBatch sample)
    {
        if (_items.TryDequeue(out sample)) return true;
        sample = default;
        return false;
    }
}

[Fact]
public void SC_GZ038_1_PollAndApply_UsesLatestBatch()
{
    var buffer = new DebugPrimitiveBuffer(capacity: 64);
    
    // Create two batches with different primitive counts.
    var batch1 = new DebugPrimitivesBatch { NodeId = 1, FrameNumber = 1, 
        Primitives = new DebugPrimitive[1] };
    var batch2 = new DebugPrimitivesBatch { NodeId = 1, FrameNumber = 2, 
        Primitives = new DebugPrimitive[3] };

    var reader = new QueuedReader(batch1, batch2);
    var translator = new DebugPrimitivesIngressTranslator(buffer, reader);
    translator.PollAndApply();

    // Buffer should contain 3 primitives from batch2, not 1 from batch1.
    Assert.Equal(3, buffer.GetFrame().Length);
}
```

**SC-GZ038-2**: This is a build test — verified during compilation.  
Verify by building: `dotnet build IOS-IG-SimHost.sln --no-incremental -clp:ErrorsOnly`.

**SC-GZ038-3**: Null reader → no-op:
```csharp
[Fact]
public void SC_GZ038_3_NullReader_NoOp()
{
    var buffer = new DebugPrimitiveBuffer(capacity: 64);
    var translator = new DebugPrimitivesIngressTranslator(buffer, reader: null);
    translator.PollAndApply(); // must not throw; buffer unchanged
    Assert.Equal(0, buffer.GetFrame().Length);
}
```

**SC-GZ038-4**: Filter by NodeId:
```csharp
[Fact]
public void SC_GZ038_4_FilterNodeId_SkipsOtherNodes()
{
    var buffer = new DebugPrimitiveBuffer(capacity: 64);

    var fromNode5 = new DebugPrimitivesBatch { NodeId = 5, FrameNumber = 1,
        Primitives = new DebugPrimitive[2] };
    var fromNode9 = new DebugPrimitivesBatch { NodeId = 9, FrameNumber = 2,
        Primitives = new DebugPrimitive[4] };

    var reader = new QueuedReader(fromNode5, fromNode9);
    var translator = new DebugPrimitivesIngressTranslator(buffer, reader, filterNodeId: 9);
    translator.PollAndApply();

    // Only node 9's batch (4 primitives) should be applied.
    Assert.Equal(4, buffer.GetFrame().Length);
}
```

**SC-GZ038-5**: `AppendRaw` overflow increments `DroppedCount`:
```csharp
[Fact]
public void SC_GZ038_5_AppendRaw_OverflowIncrements_DroppedCount()
{
    var buffer = new DebugPrimitiveBuffer(capacity: 2); // very small
    var p = new DebugPrimitive();

    buffer.AppendRaw(in p);
    buffer.AppendRaw(in p);
    buffer.AppendRaw(in p); // this one should overflow

    Assert.Equal(1, buffer.DroppedCount);
    Assert.Equal(2, buffer.GetFrame().Length);
}
```

**SC-GZ038-7 (regression)**: Verify `DebugGizmoLayer.Draw` works with `AppendRaw`-populated buffer:
```csharp
[Fact]
public void SC_GZ038_7_DebugGizmoLayer_RendersAppendRawPrimitives()
{
    // Populate buffer via AppendRaw instead of draw methods.
    var buffer = new DebugPrimitiveBuffer(capacity: 64);
    var primitive = DebugPrimitive.MakeLine(
        System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitX,
        new Rgba32(255, 0, 0, 255));
    buffer.AppendRaw(in primitive);

    // Verify buffer has content.
    Assert.Equal(1, buffer.GetFrame().Length);
    Assert.Equal(buffer.GetFrame()[0].Type, primitive.Type);
}
```

If `DebugPrimitive.MakeLine` constructor isn't accessible in `Hrot.Network.NED.Tests`, adapt to  
use the test infrastructure from `Fdp.Toolkits.Tests` instead (add it there).

---

## Build & Test Validation

```
dotnet build IOS-IG-SimHost.sln --no-incremental -clp:ErrorsOnly
```
→ **Must show 0 errors.**

```
dotnet test Hrot/Network/Hrot.Network.NED.Tests/Hrot.Network.NED.Tests.csproj --no-build
```
→ SC-GZ037-1 through SC-GZ037-8 pass.

```
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build --filter "SC_GZ038"
```
→ SC-GZ038-1/3/4/5/7 pass.

---

## Commit Instructions

**Step 1 — FDP submodule (IDdsReader + AppendRaw):**
```
cd FDP
git add -A
git commit -m "GZ037/GZ038: IDdsReader interface and AppendRaw on DebugPrimitiveBuffer"
```

**Step 2 — Root repo (NED translators + IgApplication):**
```
cd ..
git add -A
git commit -m "GZ037/GZ038: GizmoInteraction DDS translators, IG dumb terminal ingress"
```

---

## Batch Report

Create `.dev/gizmos-1/reports/BATCH-14-REPORT.md` documenting:
- Files created/modified
- Test counts (SC-GZ037-x, SC-GZ038-x)
- Build result
- Deviations from spec (e.g., GizmoInteractionIngressSystem uses repo.Bus not injected FdpEventBus)
- Note: `DataDrivenGizmoSystem` and `StatelessGizmoSystem` removed from IgApplication registration;  
  `_gizmoRegistry` field kept for API compatibility with `Hrot.IG.Tests`

Update `.dev/gizmos-1/TASK-TRACKER.md`:
- Mark GZ037, GZ038 as `[x]` done

---

## Summary Table

| Task | Key Files | New Tests |
|------|-----------|-----------|
| GZ037 | `IDdsReader.cs`, `GizmoInteractionEventKind.cs`, `GizmoInteractionBatch.cs`, `GizmoInteractionEgressSystem.cs`, `GizmoInteractionIngressSystem.cs` | SC-GZ037-1..8 |
| GZ038 | `DebugPrimitivesIngressTranslator.cs`, `DebugPrimitiveBuffer.cs` (AppendRaw), `IgApplication.cs` (removed registrations) | SC-GZ038-1/3/4/5/7 |
