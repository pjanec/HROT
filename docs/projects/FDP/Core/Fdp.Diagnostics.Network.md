# Fdp.Diagnostics.Network

**Project file**: `FDP/Diagnostics/Fdp.Diagnostics.Network/Fdp.Diagnostics.Network.csproj`
**Documentation date**: 2026-05-23

---

## README Validation

**Missing** -- no `README.md` exists in the project folder.

---

## Executive Overview

`Fdp.Diagnostics.Network` is the **DDS adapter layer** of the FDP diagnostics subsystem. It is a small
but critical bridge that connects the FDP gizmo stack to CycloneDDS transport without leaking the
CycloneDDS runtime dependency into upstream contracts or gizmo systems.

The project has three responsibilities:

1. **Re-declare the `IDdsWriter<T>` and `IDdsReader<T>` abstractions** in the FDP gizmo namespace
   (`Fdp.Toolkit.Diagnostics.Gizmos.Network`), providing an identical but independently owned
   surface that FDP callers can consume without taking a `GizmoMap.Network` dependency.

2. **Provide concrete CycloneDDS adapters** (`DdsWriterGizmoAdapter<T>`,
   `DdsReaderGizmoAdapter<T>`) that wrap real `CycloneDDS.Runtime.DdsWriter<T>` and
   `CycloneDDS.Runtime.DdsReader<T>` objects behind those interfaces, allowing unit-test
   injection of fakes without a live DDS participant.

3. **Forward the DDS topic types** defined in `GizmoMap.Network` (`DebugPrimitivesBatch`,
   `GizmoInteractionBatch`, `GizmoInteractionEventKind`, `GizmoUiState`) into this assembly's
   scope via global `using` aliases, so callers in this project refer to the canonical CLR types
   from `GizmoMap.Network` rather than duplicates.

### Why a Separate Assembly?

`Fdp.Diagnostics.Contracts` deliberately excludes CycloneDDS so that simulation code, tests, and
lightweight consumers never drag in the DDS runtime. The `GizmoMap.Network` project owns the
concrete DDS topic structs and transport adapters. `Fdp.Diagnostics.Network` is the single seam
point that glues these two worlds together and is the only FDP assembly that carries a
`PackageReference` on `CycloneDDS.NET`.

### Role in the Diagnostics Pipeline

```
+-------------------------+    +-------------------------+
|  Simulation / ECS Sys.  |    |  IG / Remote Viewer     |
|  (writes primitives via  |    |  (receives primitives    |
|   IDebugDrawBuilder)     |    |   from DDS, renders)    |
+-------------------------+    +-------------------------+
           |                              ^
           | DebugPrimitiveBuffer         | DdsReaderGizmoAdapter<T>
           v                              |   (Fdp.Diagnostics.Network)
+---------------------------+    +---------------------------+
|  DdsDebugPrimitive-       |    |  DdsDebugPrimitive-       |
|  Publisher                |    |  Subscriber               |
|  (GizmoMap.Network)       |    |  (GizmoMap.Network)       |
+---------------------------+    +---------------------------+
           |                              ^
           | IDdsWriter<T>                | IDdsReader<T>
           v                              |
+---------------------------+    +---------------------------+
| DdsWriterGizmoAdapter<T>  |    | DdsReaderGizmoAdapter<T>  |
|  (Fdp.Diagnostics.Network)|    |  (Fdp.Diagnostics.Network)|
+---------------------------+    +---------------------------+
           |                              ^
           v                              |
+----------------------------------------------------------+
|              CycloneDDS.Runtime (DDS middleware)         |
|      DdsWriter<T>  ----[UDP/multicast]---> DdsReader<T>  |
+----------------------------------------------------------+
```

---

## Architecture

### Design Decisions

#### 1. Interface Duplication for Dependency Isolation

`GizmoMap.Network` already defines `IDdsWriter<T>` and `IDdsReader<T>` in its own namespace.
`Fdp.Diagnostics.Network` re-declares identical interfaces under
`Fdp.Toolkit.Diagnostics.Gizmos.Network`. This duplication is intentional:

- FDP-side gizmo production code can receive the reader/writer abstractions without depending on
  `GizmoMap.Network` directly.
- Test projects that mock the interfaces import only the FDP-side declarations and never require
  `CycloneDDS.NET` on the test classpath.
- The adapters in this project implement the FDP-side interfaces and delegate to the CycloneDDS
  runtime, so only this one assembly ever binds to CycloneDDS.

#### 2. Disabled CycloneDDS Code Generation

The `.csproj` sets `<CycloneDdsDisableCodeGen>true</CycloneDdsDisableCodeGen>`. The CycloneDDS
source generator requires every struct appearing in a DDS context to carry `[DdsStruct]`.
`DebugPrimitive` (from `Fdp.Diagnostics.Contracts` / `GizmoMap.Contracts`) intentionally lacks
that attribute because it is a pure BCL blittable struct. Disabling code generation in this
project avoids a spurious generator error while still allowing the runtime to be used.

#### 3. Global Type Forwarding via `TypeForwards.cs`

Four GizmoMap topic types are aliased into this assembly:

| Alias                     | Actual CLR Type                              |
|---------------------------|----------------------------------------------|
| `DebugPrimitivesBatch`    | `GizmoMap.Network.DebugPrimitivesBatch`      |
| `GizmoInteractionBatch`   | `GizmoMap.Network.GizmoInteractionBatch`     |
| `GizmoInteractionEventKind`| `GizmoMap.Network.GizmoInteractionEventKind`|
| `GizmoUiState`            | `GizmoMap.Network.GizmoUiState`              |

`StringInternBatch` is commented out with a note for future restoration.

These forwarded aliases guarantee that the FDP assembly and `GizmoMap.Network` share a single
CLR identity for each type, preventing type-mismatch failures at the CycloneDDS layer.

#### 4. Adapter Pattern with Dispose Guard

Both adapter classes (`DdsWriterGizmoAdapter<T>`, `DdsReaderGizmoAdapter<T>`) implement
`IDisposable` and use a `_disposed` flag with a guard in every method call. This prevents
silent data loss or native resource corruption if an adapter is called after disposal.

#### 5. Single-Sample Poll Model

`DdsReaderGizmoAdapter<T>.TryRead` reads at most one sample per call using `Take(maxSamples: 1)`.
This matches the pull-based integration model used by `DdsDebugPrimitiveSubscriber` and
`DdsGizmoInteractionSubscriber` in `GizmoMap.Network`, which poll on every simulation tick.
Callers that need to drain all pending samples simply loop until `TryRead` returns `false`.

---

## ASCII Block Diagrams

### Diagram 1 -- Full Network Diagnostics Data Flow

```
  SimHost / FDP Simulation Process
  +---------------------------------------------+
  |                                             |
  |  ECS Gizmo Systems                          |
  |  +----------------------------------+       |
  |  | writes via IDebugDrawBuilder     |       |
  |  +----------------------------------+       |
  |             |                               |
  |             v                               |
  |  DebugPrimitiveBuffer                       |
  |  (Fdp.Diagnostics.Contracts)                |
  |  +----------------------------------+       |
  |  | GetFrame() -> Span<DebugPrimitive>|       |
  |  +----------------------------------+       |
  |             |                               |
  |             v                               |
  |  DdsDebugPrimitivePublisher                 |
  |  (GizmoMap.Network)                         |
  |  +----------------------------------+       |
  |  | packs primitives as byte[]       |       |
  |  | writes DebugPrimitivesBatch      |       |
  |  +----------------------------------+       |
  |             |                               |
  |             | IDdsWriter<DebugPrimitivesBatch>
  |             v                               |
  |  DdsWriterGizmoAdapter<DebugPrimitivesBatch>|
  |  (Fdp.Diagnostics.Network)  <-- THIS LAYER  |
  |  +----------------------------------+       |
  |  | wraps CycloneDDS DdsWriter<T>    |       |
  |  +----------------------------------+       |
  |             |                               |
  +-------------|-------------------------------+
                |  CycloneDDS (UDP multicast)
                v
  +---------------------------------------------+
  |  Hrot IG Process                            |
  |                                             |
  |  DdsReaderGizmoAdapter<DebugPrimitivesBatch>|
  |  (Fdp.Diagnostics.Network)  <-- THIS LAYER  |
  |  +----------------------------------+       |
  |  | wraps CycloneDDS DdsReader<T>    |       |
  |  +----------------------------------+       |
  |             |                               |
  |             | IDdsReader<DebugPrimitivesBatch>
  |             v                               |
  |  DdsDebugPrimitiveSubscriber                |
  |  (GizmoMap.Network)                         |
  |  +----------------------------------+       |
  |  | unpacks byte[] -> DebugPrimitive |       |
  |  | appends to local buffer          |       |
  |  +----------------------------------+       |
  |             |                               |
  |             v                               |
  |  Local DebugPrimitiveBuffer (remote copy)   |
  |  -> Hrot.IG rendering                       |
  +---------------------------------------------+
```

### Diagram 2 -- Adapter Class Hierarchy and Interface Relationships

```
  Fdp.Diagnostics.Network (this project)
  +--------------------------------------------------+
  |                                                  |
  |  namespace Fdp.Toolkit.Diagnostics.Gizmos.Network|
  |                                                  |
  |  <<interface>>           <<interface>>           |
  |  IDdsWriter<T>           IDdsReader<T>           |
  |  +-------------+         +-------------+         |
  |  | Write(T)    |         | TryRead(    |         |
  |  +-------------+         |   out T)    |         |
  |        ^                 +-------------+         |
  |        |                       ^                 |
  |        |                       |                 |
  |  +---------------------------+ |                 |
  |  | DdsWriterGizmoAdapter<T>  | |                 |
  |  | - _writer: DdsWriter<T>   | |                 |
  |  | - _disposed: bool         | |                 |
  |  | + Write(T)                | |                 |
  |  | + Dispose()               | |                 |
  |  +---------------------------+ |                 |
  |                          +---------------------------+
  |                          | DdsReaderGizmoAdapter<T>  |
  |                          | - _reader: DdsReader<T>   |
  |                          | - _disposed: bool         |
  |                          | + TryRead(out T)          |
  |                          | + Dispose()               |
  |                          +---------------------------+
  |                                                  |
  +--------------------------------------------------+
          |                         |
          | delegates to            | delegates to
          v                         v
  CycloneDDS.Runtime.DdsWriter<T>  CycloneDDS.Runtime.DdsReader<T>
```

### Diagram 3 -- Dependency Graph

```
  +----------------------------+
  |  Fdp.Diagnostics.Network   |  <-- THIS PROJECT
  |  (Fdp.Toolkit.Diagnostics. |
  |   Gizmos.Network ns)       |
  +----------------------------+
       |              |
       | ProjectRef   | ProjectRef
       v              v
  +------------------+  +--------------------------+
  | Fdp.Diagnostics. |  | GizmoMap.Network         |
  | Contracts        |  | (DDS topic structs,      |
  | (IDebugDrawBuilder|  |  publisher/subscriber    |
  |  DebugPrimitive- |  |  adapters, own           |
  |  Buffer, etc.)   |  |  IDdsReader/IDdsWriter)  |
  +------------------+  +--------------------------+
       |                        |
       | ProjectRef             | ProjectRef
       v                        v
  +------------------+  +--------------------------+
  | Fdp.Core         |  | GizmoMap.Contracts       |
  | (Entity,         |  | (DebugPrimitive struct,  |
  |  FixedString32,  |  |  GizmoPrimitiveBuffer,   |
  |  etc.)           |  |  CoordinateSpace, etc.)  |
  +------------------+  +--------------------------+
                                |
                                | PackageRef
                                v
                         +--------------------------+
                         | CycloneDDS.NET v0.2.2    |
                         | (DdsParticipant,         |
                         |  DdsWriter<T>,           |
                         |  DdsReader<T>)           |
                         +--------------------------+
```

### Diagram 4 -- DDS Topic Channel Summary

```
  +---------------------------------------------------+
  |  DDS Domain (CycloneDDS UDP transport)            |
  |                                                   |
  |  Topic: DebugPrimitivesBatch                      |
  |  QoS: BestEffort, Volatile, KeepLast(1)           |
  |  Key: {FrameNumber, NodeId}                       |
  |  Publisher: SimHost  Subscriber: Hrot.IG          |
  |                                                   |
  |  Topic: GizmoInteractionBatch                     |
  |  QoS: Reliable, Volatile, KeepLast(10)            |
  |  Key: {SourceNodeId, SequenceNumber}              |
  |  Publisher: Hrot.IG  Subscriber: SimHost          |
  |                                                   |
  |  Topic: GizmoUiState                              |
  |  QoS: Reliable, TransientLocal, KeepLast(1)       |
  |  Key: {GizmoInstanceId}                           |
  |  Publisher: SimHost  Subscriber: Hrot.IG          |
  |                                                   |
  |  Topic: StringInternEntry                         |
  |  QoS: Reliable, TransientLocal, KeepLast(1)       |
  |  Key: {NodeId, Hash}                              |
  |  Publisher: SimHost  Subscriber: Hrot.IG          |
  +---------------------------------------------------+
```

---

## Source Structure

### Namespace

All types in `Fdp.Diagnostics.Network` live in:

```
Fdp.Toolkit.Diagnostics.Gizmos.Network
```

Note: This namespace does not match the assembly name (`Fdp.Diagnostics.Network`) or the
`RootNamespace` element in the `.csproj`. The longer `Gizmos.Network` suffix aligns this
library with the `Fdp.Toolkit.Diagnostics.Gizmos` sub-hierarchy used by gizmo callers.

### File Inventory

| File                    | Contents                                                        |
|-------------------------|-----------------------------------------------------------------|
| `IDdsWriter.cs`         | `IDdsWriter<T>` interface -- thin write abstraction              |
| `IDdsReader.cs`         | `IDdsReader<T>` interface -- thin read abstraction               |
| `DdsGizmoAdapters.cs`   | `DdsWriterGizmoAdapter<T>`, `DdsReaderGizmoAdapter<T>` classes   |
| `TypeForwards.cs`       | Global `using` aliases forwarding GizmoMap.Network topic types   |

---

## Public API Reference

### `IDdsWriter<T>` Interface

```
namespace Fdp.Toolkit.Diagnostics.Gizmos.Network
```

Thin abstraction over a DDS writer. Decouples production code from the concrete
`CycloneDDS.Runtime.DdsWriter<T>` so that unit tests can inject a capturing stub without a live
DDS participant.

| Member                 | Signature            | Description                              |
|------------------------|----------------------|------------------------------------------|
| `Write`                | `void Write(T sample)` | Publishes one DDS sample to the topic. |

**Type parameter**: `T` -- the DDS topic struct type (e.g., `DebugPrimitivesBatch`).

---

### `IDdsReader<T>` Interface

```
namespace Fdp.Toolkit.Diagnostics.Gizmos.Network
```

Minimal read-only DDS subscriber abstraction. Production code uses
`CycloneDDS.Runtime.DdsReader<T>` wrapped in an adapter; unit tests supply a fake.

| Member      | Signature                     | Description                                           |
|-------------|-------------------------------|-------------------------------------------------------|
| `TryRead`   | `bool TryRead(out T sample)`  | Reads one pending sample. Returns `true` if a sample  |
|             |                               | was available; `false` when the reader queue is empty. |

**Type parameter**: `T` -- the DDS topic struct type, constrained to `new()`.

---

### `DdsWriterGizmoAdapter<T>` Class

```
namespace Fdp.Toolkit.Diagnostics.Gizmos.Network
public sealed class DdsWriterGizmoAdapter<T> : IDdsWriter<T>, IDisposable
    where T : new()
```

Wraps a `CycloneDDS.Runtime.DdsWriter<T>` so that gizmo production code can receive it through
the `IDdsWriter<T>` abstraction without depending on CycloneDDS directly.

#### Constructor

| Signature                                       | Description                                   |
|-------------------------------------------------|-----------------------------------------------|
| `DdsWriterGizmoAdapter(DdsParticipant participant)` | Creates the underlying `DdsWriter<T>` on the supplied participant. Throws `ArgumentNullException` if `participant` is null. |

#### Methods

| Member    | Signature            | Description                                                      |
|-----------|----------------------|------------------------------------------------------------------|
| `Write`   | `void Write(T sample)` | Forwards the sample to the underlying `DdsWriter<T>.Write()`. Throws `ObjectDisposedException` if the adapter has been disposed. |
| `Dispose` | `void Dispose()`     | Disposes the underlying `DdsWriter<T>` and sets the disposed flag. Idempotent. |

#### Fields (private)

| Field       | Type              | Description                          |
|-------------|-------------------|--------------------------------------|
| `_writer`   | `DdsWriter<T>`    | The wrapped CycloneDDS writer.       |
| `_disposed` | `bool`            | Dispose guard flag.                  |

---

### `DdsReaderGizmoAdapter<T>` Class

```
namespace Fdp.Toolkit.Diagnostics.Gizmos.Network
public sealed class DdsReaderGizmoAdapter<T> : IDdsReader<T>, IDisposable
    where T : new()
```

Wraps a `CycloneDDS.Runtime.DdsReader<T>` so that gizmo production code can receive it through
the `IDdsReader<T>` abstraction without depending on CycloneDDS directly.

#### Constructor

| Signature                                       | Description                                   |
|-------------------------------------------------|-----------------------------------------------|
| `DdsReaderGizmoAdapter(DdsParticipant participant)` | Creates the underlying `DdsReader<T>` on the supplied participant. Throws `ArgumentNullException` if `participant` is null. |

#### Methods

| Member     | Signature                    | Description                                                      |
|------------|------------------------------|------------------------------------------------------------------|
| `TryRead`  | `bool TryRead(out T sample)` | Calls `_reader.Take(maxSamples: 1)`. If the loan contains at least one sample the first element is copied to `sample` and `true` is returned; otherwise `sample` is set to `default!` and `false` is returned. Throws `ObjectDisposedException` if disposed. |
| `Dispose`  | `void Dispose()`             | Disposes the underlying `DdsReader<T>` and sets the disposed flag. Idempotent. |

#### Fields (private)

| Field       | Type              | Description                          |
|-------------|-------------------|--------------------------------------|
| `_reader`   | `DdsReader<T>`    | The wrapped CycloneDDS reader.       |
| `_disposed` | `bool`            | Dispose guard flag.                  |

---

### Global Type Aliases (TypeForwards.cs)

`TypeForwards.cs` uses `global using` to forward the following GizmoMap.Network types into every
file in this assembly. They are not re-exported as public symbols -- callers importing this
assembly still reference the types by their `GizmoMap.Network.*` qualified names.

| Alias                       | Maps to                                        |
|-----------------------------|------------------------------------------------|
| `DebugPrimitivesBatch`      | `GizmoMap.Network.DebugPrimitivesBatch`        |
| `GizmoInteractionBatch`     | `GizmoMap.Network.GizmoInteractionBatch`       |
| `GizmoInteractionEventKind` | `GizmoMap.Network.GizmoInteractionEventKind`   |
| `GizmoUiState`              | `GizmoMap.Network.GizmoUiState`                |

`StringInternBatch` is commented out pending future restoration.

---

## Dependencies

### Project References

| Project                      | Path (relative to solution root)                           | Purpose                                                           |
|------------------------------|------------------------------------------------------------|-------------------------------------------------------------------|
| `Fdp.Diagnostics.Contracts`  | `FDP/Diagnostics/Fdp.Diagnostics.Contracts/`              | Provides `DebugPrimitive`, `IDebugDrawBuilder`, `DebugPrimitiveBuffer`, `GizmoPrimitiveBuffer`, gizmo enums. |
| `GizmoMap.Network`           | `FDP/ExtDeps/GizmoMap/GizmoMap.Network/`                  | Provides DDS topic structs (`DebugPrimitivesBatch`, etc.), publisher/subscriber adapters, and the mirrored `IDdsWriter<T>` / `IDdsReader<T>`. |

`GizmoMap.Network` itself depends on:
- `GizmoMap.Contracts` -- `DebugPrimitive`, `GizmoPrimitiveBuffer`, `CoordinateSpace`, etc.
- `CycloneDDS.NET` -- DDS runtime.

### NuGet Packages

| Package          | Version | Purpose                                                                        |
|------------------|---------|--------------------------------------------------------------------------------|
| `CycloneDDS.NET` | 0.2.2   | DDS runtime: `DdsParticipant`, `DdsWriter<T>`, `DdsReader<T>`, schema attributes. |

### MSBuild Properties

| Property                      | Value  | Effect                                                                               |
|-------------------------------|--------|--------------------------------------------------------------------------------------|
| `CycloneDdsDisableCodeGen`    | `true` | Suppresses the CycloneDDS IDL source generator for this project. Required because `DebugPrimitive` lacks `[DdsStruct]`. |
| `AllowUnsafeBlocks`           | `true` | Enabled for consistency with sibling projects; no unsafe code appears in this project directly. |
| `TreatWarningsAsErrors`       | `true` | All compiler warnings are treated as errors.                                          |
| `Nullable`                    | `enable` | Nullable reference types enforced.                                                  |
| `LangVersion`                 | `12.0` | Allows C# 12 features such as primary constructors.                                  |
| `TargetFramework`             | `net8.0` | .NET 8 runtime.                                                                    |

---

## Usage Examples

### Example 1 -- Publishing Debug Primitives over DDS (Simulation Side)

This is the typical setup on the simulation/SimHost side. A `DdsParticipant` is created once per
process and shared across all readers and writers.

```csharp
using CycloneDDS.Runtime;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using GizmoMap.Network;

// --- Setup (once per process, during initialization) ---

var participant = new DdsParticipant();

// Create the adapter: wraps a real CycloneDDS writer behind IDdsWriter<T>.
var writerAdapter = new DdsWriterGizmoAdapter<DebugPrimitivesBatch>(participant);

// Create the GizmoMap.Network publisher that uses the adapter.
var primitivePublisher = new DdsDebugPrimitivePublisher(writerAdapter);

// Create the buffer that gizmo systems write into.
var buffer = new DebugPrimitiveBuffer(capacity: 8192);

// --- Per-frame update loop ---

uint frameNumber = 0;
byte nodeId = 1;

while (true)
{
    buffer.Clear();

    // Gizmo systems draw into the buffer.
    buffer.DrawLine(new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Rgba32(255, 0, 0, 255));

    // Publish all primitives to remote subscribers.
    primitivePublisher.Publish(buffer, frameNumber, nodeId);

    frameNumber++;
}

// --- Teardown ---
writerAdapter.Dispose();
participant.Dispose();
```

---

### Example 2 -- Receiving Debug Primitives over DDS (IG Side)

This is the setup on the `Hrot.IG` (image-generator) subscriber side. The subscriber drains
the DDS queue every frame and appends received primitives into a local buffer for rendering.

```csharp
using CycloneDDS.Runtime;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using GizmoMap.Network;

// --- Setup (once per process) ---

var participant = new DdsParticipant();

// Create the adapter: wraps a real CycloneDDS reader behind IDdsReader<T>.
var readerAdapter = new DdsReaderGizmoAdapter<DebugPrimitivesBatch>(participant);

// Create the GizmoMap.Network subscriber that uses the adapter.
var primitiveSubscriber = new DdsDebugPrimitiveSubscriber(readerAdapter);

// Local buffer that receives the unpacked primitives.
var localBuffer = new DebugPrimitiveBuffer(capacity: 8192);

// --- Per-frame update loop ---

while (true)
{
    localBuffer.Clear();

    // Drain all pending batches from DDS into the local buffer.
    while (primitiveSubscriber.PollAndApply(localBuffer))
    {
        // PollAndApply returns false when the queue is empty.
    }

    // Render localBuffer.GetFrame() using Hrot.IG rendering pipeline.
    RenderPrimitives(localBuffer.GetFrame());
}

// --- Teardown ---
readerAdapter.Dispose();
participant.Dispose();
```

---

### Example 3 -- Unit Testing with a Fake Writer (No DDS Required)

One of the core purposes of `IDdsWriter<T>` / `IDdsReader<T>` is to make the gizmo stack fully
testable without a live DDS participant. A simple capturing stub satisfies the interface:

```csharp
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using GizmoMap.Network;
using System.Collections.Generic;
using Xunit;

// --- Fake writer captures all published samples ---
private sealed class CapturingWriter<T> : IDdsWriter<T>
{
    public readonly List<T> Published = new();
    public void Write(T sample) => Published.Add(sample);
}

// --- Test ---
[Fact]
public void PublishEncodesPrimitivesAsBytes()
{
    var fakeWriter = new CapturingWriter<DebugPrimitivesBatch>();
    var publisher  = new DdsDebugPrimitivePublisher(fakeWriter);
    var buffer     = new DebugPrimitiveBuffer(capacity: 64);

    buffer.DrawLine(
        new Vector3(0, 0, 0), new Vector3(1, 0, 0),
        new Rgba32(255, 255, 0, 255));

    publisher.Publish(buffer, frameNumber: 1, nodeId: 0);

    Assert.Single(fakeWriter.Published);
    var batch = fakeWriter.Published[0];
    Assert.Equal(1u, batch.FrameNumber);
    Assert.NotEmpty(batch.PrimitivesData);
}
```

---

### Example 4 -- Publishing and Subscribing Gizmo Interaction Events

Interaction events travel in the **reverse direction**: Hrot.IG publishes them (operator click)
and SimHost subscribes (simulation responds to the interaction).

```csharp
using CycloneDDS.Runtime;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using GizmoMap.Network;

// --- IG side: publish an interaction ---

var participant = new DdsParticipant();
var interactionWriterAdapter =
    new DdsWriterGizmoAdapter<GizmoInteractionBatch>(participant);
var interactionPublisher =
    new DdsGizmoInteractionPublisher(interactionWriterAdapter);

var token = new GizmoPickToken
{
    AnchorId     = 42,
    SubElementId = 0,
    StreamId     = 1,
    GizmoTypeId  = 0xDEAD,
};
interactionPublisher.Publish(
    token,
    CoordinateSpace.World,
    new Vector3(100f, 0f, 50f),
    GizmoInteractionEventKind.Started,
    sourceNodeId: 2);

// --- SimHost side: poll for incoming interactions ---

var interactionReaderAdapter =
    new DdsReaderGizmoAdapter<GizmoInteractionBatch>(participant);
var interactionSubscriber =
    new DdsGizmoInteractionSubscriber(interactionReaderAdapter);

GizmoInteractionBatch? evt = interactionSubscriber.PollAndRead();
if (evt.HasValue)
{
    Console.WriteLine($"Interaction kind: {evt.Value.Kind}");
    Console.WriteLine($"World pos: ({evt.Value.WorldX}, {evt.Value.WorldY}, {evt.Value.WorldZ})");
}

// Teardown
interactionWriterAdapter.Dispose();
interactionReaderAdapter.Dispose();
participant.Dispose();
```

---

### Example 5 -- Fake Reader for Subscriber Tests

```csharp
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using GizmoMap.Network;
using System.Collections.Generic;
using Xunit;

private sealed class QueueReader<T> : IDdsReader<T>
{
    private readonly Queue<T> _queue;
    public QueueReader(IEnumerable<T> samples) => _queue = new Queue<T>(samples);
    public bool TryRead(out T sample)
    {
        if (_queue.Count == 0) { sample = default!; return false; }
        sample = _queue.Dequeue();
        return true;
    }
}

[Fact]
public void SubscriberAppendsAllReceivedPrimitivesToBuffer()
{
    // Build a fake batch with two zero-value primitives.
    var primitive  = new DebugPrimitive();
    var batchBytes = System.Runtime.InteropServices.MemoryMarshal
        .AsBytes(new[] { primitive, primitive }.AsSpan()).ToArray();

    var fakeBatch = new DebugPrimitivesBatch
    {
        FrameNumber    = 5,
        NodeId         = 1,
        PrimitivesData = batchBytes,
    };

    var reader     = new QueueReader<DebugPrimitivesBatch>(new[] { fakeBatch });
    var subscriber = new DdsDebugPrimitiveSubscriber(reader);
    var target     = new DebugPrimitiveBuffer(capacity: 64);

    bool consumed = subscriber.PollAndApply(target);

    Assert.True(consumed);
    Assert.Equal(2, target.Count);
}
```

---

## Best Practices

### 1. Always Dispose Adapters Before the Participant

`DdsWriterGizmoAdapter<T>` and `DdsReaderGizmoAdapter<T>` own a `DdsWriter<T>` or `DdsReader<T>`
which must be disposed before the `DdsParticipant` that owns the DDS session is torn down.
Failing to do so may trigger native resource errors inside the CycloneDDS runtime.

```csharp
// Correct teardown order:
writerAdapter.Dispose();     // first dispose all writers/readers
readerAdapter.Dispose();
participant.Dispose();       // then dispose the participant
```

### 2. Never Call Write or TryRead After Dispose

Both adapters throw `ObjectDisposedException` on any post-dispose call. Callers that hold the
adapter in long-lived fields should nullify the reference after disposal or use a wrapper that
tracks the lifecycle state.

### 3. Use Fake Implementations in Tests, Not Real Participants

Creating a `DdsParticipant` in unit tests introduces network setup overhead and external
dependencies. Supply a `CapturingWriter` or `QueueReader` (see Examples 3 and 5) and test the
transport adapters in `GizmoMap.Network` independently.

### 4. Loop TryRead to Drain the Queue

`TryRead` returns at most one sample. When multiple frames have been published before a subscriber
ticks (e.g., during a slow frame or after a hitch) call in a loop:

```csharp
while (subscriber.PollAndApply(localBuffer))
{
    // continue until queue is empty
}
```

Missing this loop causes the DDS receive queue to grow unboundedly, eventually consuming heap.

### 5. Prefer the GizmoMap.Network Transport Classes for Business Logic

`DdsWriterGizmoAdapter<T>` and `DdsReaderGizmoAdapter<T>` are deliberately unaware of primitive
packing or interaction semantics. All higher-level logic (byte packing, sequence numbering,
string interning) lives in `GizmoMap.Network` transport classes. Do not duplicate that logic
in callers -- use `DdsDebugPrimitivePublisher`, `DdsGizmoInteractionPublisher`, etc.

### 6. Keep `TypeForwards.cs` in Sync with GizmoMap.Network

When new DDS topic structs are added to `GizmoMap.Network`, add a matching `global using` alias
in `TypeForwards.cs` to ensure that files in this assembly see the same CLR type identity.

### 7. CycloneDDS Code Generation Must Stay Disabled

Do not remove `<CycloneDdsDisableCodeGen>true</CycloneDdsDisableCodeGen>` from the `.csproj`.
If it is removed the CycloneDDS source generator will attempt to process `DebugPrimitive` and
fail because that struct lacks `[DdsStruct]`. The actual DDS struct definitions and generated
serializers live in `GizmoMap.Network` (which uses the default codegen setting).

---

## DDS Topic Reference

The topics used by this project are defined in `GizmoMap.Network/Topics/` and forwarded here
via `TypeForwards.cs`. The table below summarises the wire contracts.

### `DebugPrimitivesBatch`

DDS topic for per-frame debug primitive streaming. Published by SimHost, subscribed by Hrot.IG.

| Field           | Type     | DDS Role | Notes                                              |
|-----------------|----------|----------|----------------------------------------------------|
| `FrameNumber`   | `uint`   | Key      | Monotonically increasing simulation frame counter. |
| `NodeId`        | `byte`   | Key      | Multi-node index (SimHost may run multiple nodes). |
| `PrimitivesData`| `byte[]` | Data     | Raw memory of `DebugPrimitive[]` array; decoded via `MemoryMarshal.Cast<byte, DebugPrimitive>`. |

QoS: BestEffort / Volatile / KeepLast(1). Frame data is ephemeral; missing a frame is
acceptable.

### `GizmoInteractionBatch`

DDS topic for user interaction events. Published by Hrot.IG, subscribed by SimHost.

| Field             | Type     | DDS Role | Notes                                                         |
|-------------------|----------|----------|---------------------------------------------------------------|
| `SourceNodeId`    | `byte`   | Key      | Identifies the IG terminal that originated the event.         |
| `SequenceNumber`  | `uint`   | Key      | Monotonically increasing per-source counter for ordering.     |
| `Kind`            | `GizmoInteractionEventKind` | Data | Event type (Started, DragUpdate, Commit, Cancel, MenuAction, RawInput, StructUpdate). |
| `PickAnchorId`    | `long`   | Data     | Network entity ID extracted from `PickToken`.                 |
| `PickSubElementId`| `uint`   | Data     | Sub-element within the picked entity.                         |
| `PickStreamId`    | `uint`   | Data     | Stream routing key.                                           |
| `PickGizmoTypeId` | `uint`   | Data     | FNV-1a hash of the gizmo class; 0 = legacy.                   |
| `WorldX/Y/Z`      | `float`  | Data     | World-space position at the time of interaction.              |
| `Space`           | `byte`   | Data     | `CoordinateSpace` cast to byte (avoids `[DdsStruct]` on enum).|
| `ActionId`        | `int`    | Data     | Context-menu item ID for `MenuAction`; raw HW button for `RawInput`; 0 otherwise. |
| `PayloadJson`     | `string?`| Data     | JSON payload for `StructUpdate` events; null otherwise.       |

QoS: Reliable / Volatile / KeepLast(10). Interactions must not be silently dropped.

### `GizmoUiState`

DDS topic for gizmo configuration state. Published by SimHost, subscribed by Hrot.IG.

| Field              | Type     | DDS Role | Notes                                         |
|--------------------|----------|----------|-----------------------------------------------|
| `GizmoInstanceId`  | `uint`   | Key      | Unique ID per gizmo instance.                 |
| `EditDocumentJson` | `string` | Data     | Full JSON document of the gizmo's UI state.   |

QoS: Reliable / TransientLocal / KeepLast(1). Late-joining IG terminals receive the last known
state immediately on subscription.

### `StringInternEntry`

DDS topic for the string interning dictionary. Published by SimHost, subscribed by Hrot.IG.

| Field    | Type     | DDS Role | Notes                                                            |
|----------|----------|----------|------------------------------------------------------------------|
| `NodeId` | `byte`   | Key      | Source node to prevent hash collisions across nodes.             |
| `Hash`   | `uint`   | Key      | FNV-1a hash of the string, matching the inline hash in `DebugPrimitive.StringHash`. |
| `Text`   | `string` | Data     | The full managed string value.                                   |

QoS: Reliable / TransientLocal / KeepLast(1). TransientLocal ensures late-joining IG terminals
can reconstruct the full dictionary without the SimHost needing to re-publish old entries.

---

## Related Projects

| Project                         | Relationship                                                                 |
|---------------------------------|------------------------------------------------------------------------------|
| `Fdp.Diagnostics.Contracts`     | **Direct dependency (sibling).** Provides `IDebugDrawBuilder`, `DebugPrimitiveBuffer`, `PickToken`, and the `DebugPrimitive` type used as the serialization unit. |
| `GizmoMap.Network`              | **Direct dependency (ExtDep).** Provides all DDS topic structs, publisher/subscriber adapters, and the mirrored `IDdsWriter<T>` / `IDdsReader<T>` interfaces that this project forwards into the FDP namespace. |
| `GizmoMap.Contracts`            | **Transitive dependency (via GizmoMap.Network).** Provides `DebugPrimitive`, `GizmoPrimitiveBuffer`, `CoordinateSpace`, and all primitive enum types. |
| `Fdp.Core`                      | **Transitive dependency (via Fdp.Diagnostics.Contracts).** Provides `Entity`, `FixedString32`, `Rgba32` on the FDP side. |
| `Fdp.Presentation`              | **Consumer.** Uses `DebugPrimitiveBuffer` and `IDebugDrawBuilder` to render gizmo primitives in-process. Does not depend on this project. |
| `Hrot.IG`                       | **Consumer.** Subscribes to DDS topics via the adapters defined here to receive and render primitives from the simulation. |
| `Fdp.Diagnostics.Contracts.Tests` | **Test project for sibling.** Tests `DebugPrimitiveBuffer` in isolation using fake `IDdsWriter<T>` / `IDdsReader<T>` implementations (avoids taking this project as a dependency). |
| `Fdp.Toolkits`                  | **Consumer.** Gizmo toolkit systems use `IDebugDrawBuilder` from Contracts; network transport is wired up by the composition root using this project's adapters. |

---

## Architectural Notes

### Namespace vs. Assembly Name Mismatch

The assembly is named `Fdp.Diagnostics.Network` (matching the `.csproj` and folder), but all
source files declare the namespace `Fdp.Toolkit.Diagnostics.Gizmos.Network`. This inconsistency
is historical: the project was originally part of the `Fdp.Toolkit` namespace family. The
`RootNamespace` in the `.csproj` is set to `Fdp.Diagnostics.Network` but is unused in practice
because every source file explicitly declares its namespace.

### StringInternBatch Removal

The commented-out alias in `TypeForwards.cs` refers to `StringInternBatch`, which was removed
from `GizmoMap.Network`. The corresponding DDS topic was renamed to `StringInternEntry`. The
comment is a breadcrumb for future restoration if the batch-level type is reintroduced.

### GizmoPickToken vs. PickToken

`GizmoMap.Network` uses `GizmoPickToken` (from `GizmoMap.Contracts`) while `Fdp.Diagnostics.Contracts`
defines its own `PickToken` (using `Fdp.Core.Entity` as the anchor). The two types are parallel
representations: `GizmoPickToken` carries a raw `long AnchorId` (the network-stable entity ID),
while `PickToken` carries a live `Entity` handle valid only within the ECS session. The
`DdsGizmoInteractionPublisher` in `GizmoMap.Network` accepts `GizmoPickToken`; the ECS
interaction manager bridges between the two at the ECS boundary.

### Zero-Copy Primitive Serialization

`DdsDebugPrimitivePublisher` (in `GizmoMap.Network`) uses `MemoryMarshal.AsBytes` to project the
`DebugPrimitive` span as a `byte[]` without per-element marshalling. The subscriber uses
`MemoryMarshal.Cast<byte, DebugPrimitive>` for the reverse. This works because `DebugPrimitive`
is a blittable struct. The one allocation is the `.ToArray()` call that materialises the byte
array required by the CycloneDDS managed layer.
