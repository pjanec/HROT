# GizmoMap.Network

| Field     | Value                                                              |
|-----------|--------------------------------------------------------------------|
| Project   | GizmoMap.Network                                                   |
| Path      | `FDP/ExtDeps/GizmoMap/GizmoMap.Network/GizmoMap.Network.csproj`   |
| Namespace | `GizmoMap.Network`                                                 |
| Target    | `net8.0`                                                           |
| Date      | 2026-05-23                                                         |

---

## README Validation

**Status: Missing** -- No `README.md` exists in the `GizmoMap.Network/` folder or in the
GizmoMap root. All findings are derived directly from source code and inline comments.

---

## Executive Overview

`GizmoMap.Network` is the DDS transport tier of the GizmoMap subsystem. Its sole
responsibility is to carry `DebugPrimitive` frames, interaction events, and interned
strings between simulation nodes (SimHosts) and remote viewer processes over a DDS
publish-subscribe bus.

The assembly introduces no business logic. It defines the on-wire DDS topic structs,
implements stateless publisher/subscriber adapter pairs for each topic, and exposes a pair
of thin abstractions (`IDdsWriter<T>`, `IDdsReader<T>`) that decouple production code from
the concrete CycloneDDS runtime so unit tests can inject stubs without a live DDS
participant.

The design enforces a strict dependency rule: GizmoMap.Network depends on
GizmoMap.Contracts (for `DebugPrimitive`, `GizmoPrimitiveBuffer`, `StringInternMap`)
and CycloneDDS.NET (for the topic attribute schema and the runtime reader/writer types).
Nothing in GizmoMap.Network depends on rendering, ECS, or simulation logic.

---

## Architecture

### Component Overview

```
+----------------------------------------------------------+
|  Simulation Node / SimHost                               |
|                                                          |
|  [GizmoPrimitiveBuffer]                                  |
|       |                                                  |
|       v                                                  |
|  [DdsDebugPrimitivePublisher]   [DdsStringInternPublisher]
|       |                              |                   |
+-------|------------------------------|-------------------+
        |  DDS domain (CycloneDDS)     |
        |  Topic: DebugPrimitivesBatch |  Topic: StringInternEntry
        |  QoS: BestEffort, KeepLast=1 |  QoS: Reliable, TransientLocal
        v                              v
+----------------------------------------------------------+
|  Viewer / GizmoMap.Viewer or GizmoMap.Example            |
|                                                          |
|  [DdsDebugPrimitiveSubscriber]  [DdsStringInternSubscriber]
|       |                              |                   |
|       v                              v                   |
|  [GizmoPrimitiveBuffer (render)]                         |
+----------------------------------------------------------+

Bidirectional interaction channel:
+------------------+           +------------------+
|  Viewer          |           |  SimHost         |
|  [DdsGizmoInteractionPublisher] -> Topic: GizmoInteractionBatch
|                  |           |  [DdsGizmoInteractionSubscriber]
+------------------+           +------------------+
```

### DDS Topic Map

```
+-------------------------+----------+------------+---------------+---------+
| Topic Name              | QoS      | Durability | Direction     | Key     |
+-------------------------+----------+------------+---------------+---------+
| DebugPrimitivesBatch    | BestEffort| Volatile  | SimHost->View | FrameNumber+NodeId |
| StringInternEntry       | Reliable | TransientLocal | SimHost->View | NodeId+Hash |
| GizmoInteractionBatch   | Reliable | Volatile   | View->SimHost | SourceNodeId+SeqNum |
| GizmoUiState            | Reliable | TransientLocal | SimHost->View | GizmoInstanceId |
| EntityAttributeSchema   | Reliable | TransientLocal | SimHost->View | NodeId |
+-------------------------+----------+------------+---------------+---------+
```

### Serialization Strategy

`DebugPrimitivesBatch.PrimitivesData` is a raw `byte[]` that encodes the entire frame as
`MemoryMarshal.AsBytes(span<DebugPrimitive>)`. This is a zero-copy reinterpretation cast
on the publish side and an equally cheap cast on the subscribe side. CycloneDDS serializes
the byte array as a DDS sequence, adding only the 4-byte sequence length header.

The approach bypasses the CycloneDDS requirement for `[DdsStruct]` on nested types, which
would otherwise demand that every `DebugPrimitive` field be independently generated into
an IDL schema. Because `DebugPrimitive` is a pure BCL type in Contracts, this strategy
preserves the clean contract boundary.

---

## Source Structure

```
GizmoMap.Network/
+-- GizmoMap.Network.csproj
+-- GizmoInteractionEventKind.cs     Enum: interaction event discriminant
|
+-- Topics/
|   +-- DebugPrimitivesBatch.cs      DDS topic: raw primitive frame bytes
|   +-- EntityAttributeSchema.cs     DDS topic: JSON attribute schema per node
|   +-- GizmoInteractionBatch.cs     DDS topic: single interaction event
|   +-- GizmoUiState.cs              DDS topic: StructEdit UI state JSON
|   +-- StringInternEntry.cs         DDS topic: single interned string
|
+-- Transport/
    +-- DdsDebugPrimitivePublisher.cs   Packs buffer -> DDS batch
    +-- DdsDebugPrimitiveSubscriber.cs  Unpacks DDS batch -> buffer
    +-- DdsGizmoInteractionPublisher.cs Serializes GizmoPickToken interaction events
    +-- DdsGizmoInteractionSubscriber.cs Polls interaction batch samples
    +-- DdsStringInternPublisher.cs     Delta-publishes new intern entries
    +-- DdsStringInternSubscriber.cs    Drains intern entries into buffer
    +-- IDdsReader.cs                   Minimal subscriber abstraction
    +-- IDdsWriter.cs                   Minimal publisher abstraction
```

---

## Public API Reference

### Topic Types

All topic types are `partial struct` decorated with CycloneDDS schema attributes.
CycloneDDS.NET generates the IDL and serialization glue at build time.

---

#### `DebugPrimitivesBatch`

```csharp
[DdsTopic("DebugPrimitivesBatch")]
[DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.Volatile,
        HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
public partial struct DebugPrimitivesBatch
{
    [DdsKey] public uint FrameNumber;
    [DdsKey] public byte NodeId;
    [DdsManaged] public byte[] PrimitivesData;
}
```

BestEffort + KeepLast=1 means the viewer always sees the newest frame; stale frames are
discarded automatically. The `byte[]` payload is a raw `MemoryMarshal.AsBytes` projection
of the `DebugPrimitive` array.

---

#### `StringInternEntry`

```csharp
[DdsTopic("StringInternEntry")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal,
        HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
public partial struct StringInternEntry
{
    [DdsKey] public byte NodeId;
    [DdsKey] public uint Hash;
    [DdsManaged] public string Text;
}
```

TransientLocal with KeepLast=1 per hash ensures that a viewer joining mid-session
immediately receives the full string dictionary for each node. This guarantees that
long-text strings (those exceeding 31 chars) are resolvable as soon as the viewer starts.

---

#### `GizmoInteractionBatch`

```csharp
[DdsTopic("GizmoInteractionBatch")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile,
        HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 10)]
public partial struct GizmoInteractionBatch
{
    [DdsKey] public byte   SourceNodeId;
    [DdsKey] public uint   SequenceNumber;
    public GizmoInteractionEventKind Kind;
    public long   PickAnchorId;
    public uint   PickSubElementId;
    public uint   PickStreamId;
    public uint   PickGizmoTypeId;
    public float  WorldX;
    public float  WorldY;
    public float  WorldZ;
    public byte   Space;
    public int    ActionId;
    [DdsManaged] public string? PayloadJson;
}
```

Reliable + KeepLast=10 ensures that interaction events are delivered even under brief
network congestion; the depth of 10 provides a small replay buffer for the SimHost
subscriber.

---

#### `GizmoUiState`

```csharp
[DdsTopic("GizmoUiState")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal,
        HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
public partial struct GizmoUiState
{
    [DdsKey] public uint GizmoInstanceId;
    [DdsManaged] public string EditDocumentJson;
}
```

Carries the serialized `EditDocument` JSON for a StructInspector panel. TransientLocal
delivery guarantees the viewer receives the current UI state even after a late join.

---

#### `EntityAttributeSchema`

```csharp
[DdsTopic("EntityAttributeSchema")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal,
        HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
public partial struct EntityAttributeSchema
{
    [DdsKey] public int NodeId;
    [DdsManaged] public string SchemaJson;
}
```

Published once per SimHost at startup. The JSON Schema document describes the full entity
attribute set supported by the node's `JsonAttributeCompiler`. Used by remote editors to
build attribute query UIs.

---

### `GizmoInteractionEventKind` (enum, `byte`)

| Value          | Numeric | Description                                                   |
|----------------|---------|---------------------------------------------------------------|
| `Started`      | 0       | Initial left-click on an interactive primitive                |
| `DragUpdate`   | 1       | Mouse moved while drag is active                              |
| `Commit`       | 2       | Mouse released; drag confirmed                                |
| `Cancel`       | 3       | ESC or right-click; drag rolled back                          |
| `MenuAction`   | 4       | Context menu item clicked; `ActionId` carries the item ID     |
| `RawInput`     | 5       | Raw HW event (exclusive capture); see stateFlags encoding     |
| `StructUpdate` | 6       | StructEdit panel committed; `PayloadJson` carries the mutation|

**`RawInput` encoding:** `ActionId` = `(int)MapMouseButton` or `(int)MapKeyboardKey`;
`Space` field (repurposed as `stateFlags`): bit 7 = 1 mouse / 0 keyboard; bit 0 = 1 pressed / 0 released.

---

### Transport Adapters

#### `IDdsWriter<T>` (interface)

```csharp
public interface IDdsWriter<T>
{
    void Write(T sample);
}
```

Thin abstraction over `DdsWriter<T>`. Decouples publisher adapters from the concrete
CycloneDDS writer, enabling stub injection in unit tests without a live DDS participant.

---

#### `IDdsReader<T>` (interface)

```csharp
public interface IDdsReader<T>
{
    bool TryRead(out T sample);
}
```

Single-sample read. Returns `true` if a sample was available.

---

#### `DdsDebugPrimitivePublisher` (sealed class)

| Member                                                 | Description                              |
|--------------------------------------------------------|------------------------------------------|
| `DdsDebugPrimitivePublisher(IDdsWriter<DebugPrimitivesBatch>)` | Constructor                    |
| `void Publish(GizmoPrimitiveBuffer, uint frameNumber, byte nodeId)` | Pack frame and write  |

Packs the current frame via `MemoryMarshal.AsBytes` (zero-copy reinterpret) and writes one
`DebugPrimitivesBatch` sample. The heap allocation for `PrimitivesData` is unavoidable
because CycloneDDS requires a managed `byte[]`.

---

#### `DdsDebugPrimitiveSubscriber` (sealed class)

| Member                                                   | Description                            |
|----------------------------------------------------------|----------------------------------------|
| `DdsDebugPrimitiveSubscriber(IDdsReader<DebugPrimitivesBatch>)` | Constructor                   |
| `bool PollAndApply(GizmoPrimitiveBuffer)`                | Read one batch and append to buffer    |

On success, casts `PrimitivesData` back to `ReadOnlySpan<DebugPrimitive>` via
`MemoryMarshal.Cast` and appends each primitive via `GizmoPrimitiveBuffer.AppendRaw`.

---

#### `DdsGizmoInteractionPublisher` (sealed class)

| Member                                                     | Description                           |
|------------------------------------------------------------|---------------------------------------|
| `DdsGizmoInteractionPublisher(IDdsWriter<GizmoInteractionBatch>)` | Constructor                  |
| `void Publish(GizmoPickToken, CoordinateSpace, Vector3, GizmoInteractionEventKind, byte)` | Publish one event |

Maintains an internal monotonically-increasing `_sequenceNumber` so the SimHost subscriber
can detect missed events.

---

#### `DdsGizmoInteractionSubscriber` (sealed class)

| Member                                                       | Description                         |
|--------------------------------------------------------------|-------------------------------------|
| `DdsGizmoInteractionSubscriber(IDdsReader<GizmoInteractionBatch>)` | Constructor                  |
| `GizmoInteractionBatch? PollAndRead()`                       | Return one sample or null           |

---

#### `DdsStringInternPublisher` (sealed class)

| Member                                                   | Description                            |
|----------------------------------------------------------|----------------------------------------|
| `DdsStringInternPublisher(IDdsWriter<StringInternEntry>, byte nodeId)` | Constructor      |
| `void Publish(StringInternMap)`                          | Delta-publish newly observed entries    |

Maintains a local `HashSet<uint>` of already-published hashes. Only hashes seen for the
first time are written to DDS, preventing redundant TransientLocal history growth.

---

#### `DdsStringInternSubscriber` (sealed class)

| Member                                                   | Description                            |
|----------------------------------------------------------|----------------------------------------|
| `DdsStringInternSubscriber(IDdsReader<StringInternEntry>)` | Constructor                          |
| `void PollAndApply(GizmoPrimitiveBuffer)`                | Drain all pending entries into buffer's intern map |

---

## Dependencies

```
+-----------------------------+
| GizmoMap.Network            |
|                             |
| Project references:         |
|   GizmoMap.Contracts        |
|                             |
| Package references:         |
|   CycloneDDS.NET 0.2.2      |
+-----------------------------+
        |
        v
+-----------------------------+
| GizmoMap.Contracts          |
| (DebugPrimitive,            |
|  GizmoPrimitiveBuffer,      |
|  StringInternMap,           |
|  IGizmoTransport, ...)      |
+-----------------------------+
```

---

## Usage Examples

### Example 1: Publishing Primitives from a SimHost

```csharp
using CycloneDDS.Runtime;
using Fdp.Toolkit.Diagnostics.Gizmos;
using GizmoMap.Network;

// Setup once at startup.
using var participant = new DdsParticipant(domainId: 0);
var rawWriter = new DdsWriter<DebugPrimitivesBatch>(participant);
// Wrap in the IDdsWriter<T> adapter expected by the publisher.
var writer = new LiveDdsWriterAdapter(rawWriter);
var publisher = new DdsDebugPrimitivePublisher(writer);

var stringWriter = new DdsWriter<StringInternEntry>(participant);
var stringPub = new DdsStringInternPublisher(new LiveStringWriterAdapter(stringWriter), nodeId: 1);

var buffer = new GizmoPrimitiveBuffer(capacity: 4096);
uint frameNumber = 0;

void OnFrameEnd(float dt)
{
    buffer.EndFrame(dt);

    // --- simulation draw phase fills 'buffer' here ---

    // Publish interned strings first so the viewer can resolve them.
    stringPub.Publish(buffer.InternMap);

    // Publish the primitive frame.
    publisher.Publish(buffer, ++frameNumber, nodeId: 1);
}
```

### Example 2: Subscribing in a Viewer Process

```csharp
using CycloneDDS.Runtime;
using Fdp.Toolkit.Diagnostics.Gizmos;
using GizmoMap.Network;

using var participant = new DdsParticipant(domainId: 0);
var rawReader  = new DdsReader<DebugPrimitivesBatch>(participant);
var strReader  = new DdsReader<StringInternEntry>(participant);
var intxWriter = new DdsWriter<GizmoInteractionBatch>(participant);

var primSub    = new DdsDebugPrimitiveSubscriber(new LiveDdsReaderAdapter(rawReader));
var strSub     = new DdsStringInternSubscriber(new LiveStrReaderAdapter(strReader));
var intxPub    = new DdsGizmoInteractionPublisher(new LiveDdsWriterAdapter(intxWriter));

var renderBuffer = new GizmoPrimitiveBuffer();

void OnViewerTick()
{
    renderBuffer.Clear();

    // Drain interned strings so long-text labels resolve this frame.
    strSub.PollAndApply(renderBuffer);

    // Drain up to N primitive batches (keeps latest when BestEffort is fast).
    while (primSub.PollAndApply(renderBuffer)) { }
}

void OnUserClickedEntity(GizmoPickToken token, System.Numerics.Vector3 worldPos)
{
    intxPub.Publish(token, CoordinateSpace.World, worldPos,
        GizmoInteractionEventKind.Started, sourceNodeId: 250);
}
```

### Example 3: Unit Testing with Stub Adapters

```csharp
using GizmoMap.Network;
using Fdp.Toolkit.Diagnostics.Gizmos;
using System.Collections.Generic;

// Stub writer that captures written samples.
sealed class CapturingWriter<T> : IDdsWriter<T>
{
    public readonly List<T> Written = new();
    public void Write(T sample) => Written.Add(sample);
}

// Stub reader backed by a pre-loaded queue.
sealed class QueuingReader<T> : IDdsReader<T>
{
    private readonly Queue<T> _queue;
    public QueuingReader(IEnumerable<T> items) => _queue = new Queue<T>(items);
    public bool TryRead(out T sample)
    {
        if (_queue.Count == 0) { sample = default!; return false; }
        sample = _queue.Dequeue();
        return true;
    }
}

// Test: round-trip one frame through publisher -> subscriber.
void TestRoundTrip()
{
    var writer = new CapturingWriter<DebugPrimitivesBatch>();
    var pub    = new DdsDebugPrimitivePublisher(writer);

    var src = new GizmoPrimitiveBuffer();
    src.DrawArrow(System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitX,
                  Rgba32.Red);

    pub.Publish(src, frameNumber: 1, nodeId: 1);

    // Feed written sample to subscriber.
    var reader = new QueuingReader<DebugPrimitivesBatch>(writer.Written);
    var sub    = new DdsDebugPrimitiveSubscriber(reader);
    var dst    = new GizmoPrimitiveBuffer();
    sub.PollAndApply(dst);

    System.Console.WriteLine($"Round-trip: {dst.GetFrame().Length} primitive(s).");
}
```

---

## Best Practices

1. **Publish string interns before primitives.** The viewer may process DDS samples in
   arrival order. If `DebugPrimitivesBatch` arrives before `StringInternEntry`, the renderer
   will fall back to the 31-char inline text for that frame. Publish interns first to
   minimize such gaps.

2. **Use BestEffort for the primitive stream.** Simulation frames are produced at 30+ Hz.
   Retransmitting a stale frame is wasteful; the viewer should always render the most
   recent frame. The KeepLast=1 history enforces this automatically.

3. **Use Reliable + TransientLocal for interns.** String intern entries are permanent and
   must survive viewer restarts. TransientLocal ensures a late-joining viewer receives the
   complete dictionary immediately.

4. **Use the `IDdsWriter<T>` / `IDdsReader<T>` abstractions in all production code.**
   Never reference `DdsWriter<T>` or `DdsReader<T>` directly in adapters. This keeps the
   test seam clean and avoids linking the CycloneDDS runtime socket layer in unit tests.

5. **Delta-publish string interns.** `DdsStringInternPublisher` tracks published hashes
   in a `HashSet<uint>`. Do not bypass this class and publish directly; doing so would
   flood TransientLocal history with redundant samples.

6. **NodeId is the multi-SimHost discriminator.** In a cluster with multiple simulation
   nodes, each node should use a unique `NodeId` byte (1-249 by convention; 250+ reserved
   for viewer nodes). The viewer filters incoming batches by `sample.Data.NodeId == targetNodeId`.

---

## Related Projects

| Project                  | Relationship                                                     |
|--------------------------|------------------------------------------------------------------|
| `GizmoMap.Contracts`     | Upstream; provides all shared types consumed by this assembly    |
| `GizmoMap.Presentation`  | Consumes this assembly; renders primitives received via DDS      |
| `GizmoMap.Viewer`        | Application that wires Network subscribers to Presentation       |
| `GizmoMap.Example`       | Shows both local and DDS transport modes side-by-side            |
| `CycloneDDS.NET`         | External DDS runtime; provides `DdsParticipant`, `DdsReader<T>`, `DdsWriter<T>` |
