# GizmoMap.Viewer

| Field     | Value                                                              |
|-----------|--------------------------------------------------------------------|
| Project   | GizmoMap.Viewer                                                    |
| Path      | `FDP/ExtDeps/GizmoMap/GizmoMap.Viewer/GizmoMap.Viewer.csproj`     |
| Namespace | `GizmoMap.Viewer`                                                  |
| Target    | `net8.0`                                                           |
| Output    | Executable (`<OutputType>Exe</OutputType>`)                        |
| Date      | 2026-05-23                                                         |

---

## README Validation

**Status: Missing** -- No `README.md` exists in `GizmoMap.Viewer/` or in the GizmoMap
root folder. All findings are derived from source code and inline comments.

---

## Executive Overview

`GizmoMap.Viewer` is the standalone executable that acts as the "dumb terminal" in the
GizmoMap architecture. It receives `DebugPrimitive` frames published over DDS by one or
more SimHost nodes and renders them using the shared Raylib + ImGui frontend provided by
`GizmoMap.Presentation`.

The application does not run any simulation logic. Its entire job is:

1. Subscribe to the DDS `DebugPrimitivesBatch` and `StringInternEntry` topics for a
   chosen target node.
2. Subscribe to `GizmoUiState` to keep StructInspector panels synchronized.
3. On each tick, copy the latest primitive batch into a `GizmoPrimitiveBuffer`.
4. Delegate rendering and input handling to `GizmoViewerFrontend`.
5. Forward user interaction events (drag, menu, StructEdit mutations) back to the
   SimHost via the `GizmoInteractionBatch` DDS topic.

The source is a single file: `Program.cs`. There are no additional classes or modules;
the application is intentionally thin, placing all logic in the infrastructure assemblies
it references.

---

## Architecture

### Process Role in the GizmoMap Network

```
+--------------------------+        DDS domain        +---------------------------+
|  SimHost node            |                          |  GizmoMap.Viewer          |
|  (GizmoMap.Example or    |                          |  (this project)           |
|   FDP simulation)        |                          |                           |
|                          |  DebugPrimitivesBatch    |                           |
|  DdsDebugPrimitive-      +------------------------->+  DdsReader<DebugPrimitives|
|  Publisher               |  (BestEffort,KeepLast=1) |  Batch>                   |
|                          |                          |                           |
|  DdsStringInternPublisher+------------------------->+  DdsReader<StringIntern   |
|                          |  StringInternEntry       |  Entry>                   |
|                          |  (Reliable,TransientLocal|                           |
|                          |                          |                           |
|  (StructEdit service)    +=========================>+  DdsReader<GizmoUiState>  |
|                          |  GizmoUiState            |                           |
|                          |  (Reliable,TransientLocal|                           |
|                          |                          |                           |
|  DdsGizmoInteraction-    |<-------------------------+  DdsWriter<GizmoInteraction
|  Subscriber              |  GizmoInteractionBatch   |  Batch>                   |
|                          |  (Reliable,KeepLast=10)  |                           |
+--------------------------+                          +---------------------------+
```

### Internal Data Flow Per Frame

```
+----------------------------------------------+
|  onUpdateTick(dt)                            |
|                                              |
|  renderBuffer.Clear()                        |
|       |                                      |
|       v                                      |
|  Drain StringInternEntry samples             |
|  --> renderBuffer.InternMap.Intern(h, text)  |
|       |                                      |
|       v                                      |
|  Drain DebugPrimitivesBatch samples          |
|  --> keep only samples with NodeId == target |
|  --> keep only latest batch                  |
|       |                                      |
|       v                                      |
|  Drain GizmoUiState samples                  |
|  --> adapter.ReceiveUiState(sample)          |
|       |                                      |
|       v                                      |
|  MemoryMarshal.Cast byte[] -> DebugPrimitive |
|  --> renderBuffer.AppendRaw(prim) each       |
+----------------------------------------------+
                     |
                     v
+----------------------------------------------+
|  GizmoViewerFrontend handles:                |
|  - Render (Raylib 2D)                        |
|  - HandleInput -> onInteraction callback     |
|  - DrawMainMenu / DrawContextMenu (ImGui)    |
|  - DrawScheduled property panels (ImGui)     |
+----------------------------------------------+
                     |
                     v
+----------------------------------------------+
|  onInteraction / onMenuAction                |
|  --> interactionWriter.Write(                |
|      GizmoInteractionBatch { ... })          |
+----------------------------------------------+
```

---

## Source Structure

```
GizmoMap.Viewer/
+-- GizmoMap.Viewer.csproj
+-- Program.cs              Full application (single file)
```

The application is intentionally minimal. All complexity lives in:
- `GizmoMap.Presentation.GizmoViewerFrontend` (rendering loop)
- `GizmoMap.Presentation.ImGuiPropertyTreeAdapter` (StructEdit panels)
- `GizmoMap.Network` (DDS transport adapters)

---

## Public API Reference

The project exposes no public library API -- it is an executable. The observable interface
is the command-line argument surface.

### Command-Line Arguments

| Argument              | Default | Description                                          |
|-----------------------|---------|------------------------------------------------------|
| `--domain <uint>`     | `0`     | DDS domain ID to join                                |
| `--node-id <byte>`    | `1`     | Target SimHost node ID to subscribe to               |
| `--viewer-node-id <byte>` | `250` | Node ID stamped on outgoing interaction events     |
| `--help` / `-h`       | --      | Print usage and exit                                 |

Multiple SimHost nodes can coexist on the same DDS domain with different node IDs. The
viewer subscribes to all nodes' topics but filters incoming samples by
`sample.Data.NodeId == targetNodeId`, so it tracks exactly one node's gizmo stream.

The `--viewer-node-id` is written into outgoing `GizmoInteractionBatch.SourceNodeId` so
the SimHost knows which viewer sent the event.

### Interaction Event Encoding

The viewer writes `GizmoInteractionBatch` samples for all interaction events. The field
mapping is:

| `GizmoInteractionBatch` field | Source                                                  |
|-------------------------------|---------------------------------------------------------|
| `SourceNodeId`                | `--viewer-node-id` (default 250)                        |
| `SequenceNumber`              | Monotonically increasing local counter                  |
| `Kind`                        | From `onInteraction` or `onMenuAction` callback         |
| `PickAnchorId`                | `token.AnchorId`                                        |
| `PickSubElementId`            | `token.SubElementId`                                    |
| `PickStreamId`                | `token.StreamId`                                        |
| `PickGizmoTypeId`             | `token.GizmoTypeId`                                     |
| `WorldX/Y/Z`                  | `pos.X/Y/Z`                                             |
| `Space`                       | `stateFlags` (repurposed; encodes coordinate space/raw-input flags) |
| `ActionId`                    | Menu item ID or raw HW key code                         |
| `PayloadJson`                 | StructEdit mutation JSON for `StructUpdate` events      |

---

## Dependencies

```
+-----------------------------+
|  GizmoMap.Viewer            |
|  (Executable)               |
|                             |
|  Project references:        |
|    GizmoMap.Contracts       |
|    GizmoMap.Network         |
|    GizmoMap.Presentation    |
|                             |
|  Package references:        |
|    CycloneDDS.NET 0.2.2     |
+-----------------------------+
          |
          v
+----------------------------------+
| GizmoMap.Presentation            |
| (GizmoViewerFrontend,            |
|  DebugGizmoLayer,                |
|  ImGuiPropertyTreeAdapter, ...)  |
+----------------------------------+
          |
          v
+----------------------------------+
| GizmoMap.Network                 |
| (GizmoInteractionBatch,          |
|  DebugPrimitivesBatch,           |
|  StringInternEntry, GizmoUiState)|
+----------------------------------+
          |
          v
+----------------------------------+
| GizmoMap.Contracts               |
| (DebugPrimitive, GizmoPrimitive  |
|  Buffer, GizmoPickToken, ...)    |
+----------------------------------+
          |
          v
+----------------------------------+
| CycloneDDS.NET 0.2.2             |
| (DdsParticipant, DdsReader<T>,   |
|  DdsWriter<T>)                   |
+----------------------------------+
```

---

## Usage Examples

### Example 1: Running the Viewer Against a Local SimHost

```
# Start the SimHost (e.g. GizmoMap.Example in DDS mode):
GizmoMap.Example.exe --mode dds

# Start the Viewer, targeting node 1 on domain 0:
GizmoMap.Viewer.exe --domain 0 --node-id 1 --viewer-node-id 250
```

The viewer opens a 640x480 Raylib window titled "GizmoMap Viewer - Node 1". All gizmos
published by the SimHost at `NodeId=1` appear on the canvas. Left-clicking an interactive
entity box starts a drag; the drag events are sent back to the SimHost via DDS.

### Example 2: Watching a Different Node in a Cluster

```
# Node 2 of a multi-node simulation cluster:
GizmoMap.Viewer.exe --domain 0 --node-id 2 --viewer-node-id 251
```

The `--viewer-node-id 251` ensures the interaction events emitted by this viewer instance
are distinguishable from the first viewer (250) on the SimHost side.

### Example 3: Extending the Viewer with a Custom Input Handler

The viewer does not expose hooks for extension without code modification, but the
`GizmoViewerFrontend.Run` method (which Program.cs calls) accepts an `onCustomInput`
callback. To add custom Raylib key bindings, pass a modified `Program.cs` or embed the
viewer in a custom executable that calls `GizmoViewerFrontend.Run` directly:

```csharp
using CycloneDDS.Runtime;
using Fdp.Toolkit.Diagnostics.Gizmos;
using GizmoMap.Network;
using GizmoMap.Presentation;
using Raylib_cs;
using System.Runtime.InteropServices;

using var participant = new DdsParticipant(domainId: 0);
using var primReader  = new DdsReader<DebugPrimitivesBatch>(participant);
using var strReader   = new DdsReader<StringInternEntry>(participant);
using var intxWriter  = new DdsWriter<GizmoInteractionBatch>(participant);

var renderBuffer   = new GizmoPrimitiveBuffer();
var schemaRegistry = new GizmoSchemaRegistry();
var adapter        = new ImGuiPropertyTreeAdapter(schemaRegistry);
uint seqNum        = 0;

GizmoViewerFrontend.Run(
    "My Extended Viewer",
    renderBuffer,
    schemaRegistry,
    onUpdateTick: _ =>
    {
        renderBuffer.Clear();
        using var strLoan  = strReader.Take();
        foreach (var s in strLoan)
            if (s.IsValid) renderBuffer.InternMap.Intern(s.Data.Hash, s.Data.Text);

        using var primLoan = primReader.Take();
        DebugPrimitivesBatch? latest = null;
        foreach (var p in primLoan)
            if (p.IsValid) latest = p.Data;

        if (latest.HasValue && latest.Value.PrimitivesData != null)
        {
            var span = MemoryMarshal.Cast<byte, DebugPrimitive>(latest.Value.PrimitivesData.AsSpan());
            foreach (ref readonly var pr in span)
                renderBuffer.AppendRaw(in pr);
        }
    },
    onInteraction: (token, kind, pos, actionId, flags, payloadJson) =>
    {
        intxWriter.Write(new GizmoInteractionBatch
        {
            SourceNodeId   = 250,
            SequenceNumber = ++seqNum,
            Kind           = kind,
            PickAnchorId   = token.AnchorId,
            WorldX = pos.X, WorldY = pos.Y, WorldZ = pos.Z,
            ActionId = actionId, PayloadJson = payloadJson,
        });
    },
    onMenuAction: (token, actionId) =>
    {
        intxWriter.Write(new GizmoInteractionBatch
        {
            SourceNodeId   = 250,
            SequenceNumber = ++seqNum,
            Kind           = GizmoInteractionEventKind.MenuAction,
            PickAnchorId   = token.AnchorId,
            ActionId       = actionId,
        });
    },
    // Custom: press 'C' to print current frame count.
    onCustomInput: () =>
    {
        if (Raylib.IsKeyPressed(KeyboardKey.C))
            System.Console.WriteLine($"Frame primitives: {renderBuffer.GetFrame().Length}");
    },
    externalAdapter: adapter);
```

---

## Best Practices

1. **Keep `--node-id` and `--viewer-node-id` distinct.** The viewer uses
   `--viewer-node-id` to stamp outgoing interaction events. If you run two viewers, give
   each a unique `--viewer-node-id` so the SimHost can distinguish their events.

2. **The viewer always renders the latest batch.** Because `DebugPrimitivesBatch` uses
   BestEffort + KeepLast=1, the `primReader.Take()` loop in `onUpdateTick` iterates all
   buffered samples and keeps only the last valid one. This is intentional: there is no
   value in replaying stale frames.

3. **String interns arrive reliably.** `StringInternEntry` uses TransientLocal delivery.
   A newly started viewer receives the full intern dictionary immediately. Drain
   `strReader` before draining `primReader` each tick so long-text labels resolve
   correctly in the first rendered frame.

4. **Do not build simulation logic into the viewer.** The viewer is a dumb terminal.
   All interaction business logic (e.g. what to do when the operator selects "Attack")
   lives in the SimHost. The viewer merely forwards the action ID.

5. **Use `MemoryMarshal.Cast` for zero-copy deserialization.** The `PrimitivesData`
   byte array is the raw memory of the `DebugPrimitive` array. Using
   `MemoryMarshal.Cast<byte, DebugPrimitive>` avoids any deserialization overhead.

---

## Program.cs Annotated Walkthrough

This section walks through the key decisions in `Program.cs` to aid maintenance.

### Argument Parsing

```csharp
uint domainId      = 0;
byte targetNodeId  = 1;
byte viewerNodeId  = 250;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--domain"          && i + 1 < args.Length)
        domainId      = uint.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (args[i] == "--node-id"    && i + 1 < args.Length)
        targetNodeId  = byte.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (args[i] == "--viewer-node-id" && i + 1 < args.Length)
        viewerNodeId  = byte.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (args[i] == "--help" || args[i] == "-h")
    {
        Console.WriteLine("Usage: GizmoMap.Viewer [--domain <id>] [--node-id <id>] [--viewer-node-id <id>]");
        return 0;
    }
}
```

`CultureInfo.InvariantCulture` is used for all numeric parses to avoid locale-dependent
decimal separator issues on non-English systems.

---

### DDS Participant and Reader/Writer Lifecycle

```csharp
using var participant      = new DdsParticipant(domainId);
using var primitivesReader = new DdsReader<DebugPrimitivesBatch>(participant);
using var stringsReader    = new DdsReader<StringInternEntry>(participant);
using var interactionWriter= new DdsWriter<GizmoInteractionBatch>(participant);
using var uiStateReader    = new DdsReader<GizmoUiState>(participant);
```

All DDS objects are owned by `using` declarations so they are disposed when `Main`
returns. The `DdsParticipant` disposes all child readers and writers when it is disposed.
The interleaved `using` declarations ensure the participant outlives its children.

The viewer does NOT create a `DdsWriter<DebugPrimitivesBatch>` or a
`DdsWriter<StringInternEntry>` -- it is a pure subscriber on those topics.

---

### onUpdateTick: Draining Topic Samples

The `onUpdateTick` closure demonstrates the correct multi-topic drain order:

```csharp
onUpdateTick: _ =>
{
    renderBuffer.Clear();

    // 1. String interns: drain ALL pending samples.
    using var stringLoan = stringsReader.Take();
    foreach (var sample in stringLoan)
    {
        if (sample.IsValid && sample.Data.NodeId == targetNodeId)
            renderBuffer.InternMap.Intern(sample.Data.Hash, sample.Data.Text);
    }

    // 2. Primitive batches: scan ALL; keep only the LATEST valid sample.
    DebugPrimitivesBatch? latestBatch = null;
    using var primitiveLoan = primitivesReader.Take();
    foreach (var sample in primitiveLoan)
    {
        if (sample.IsValid && sample.Data.NodeId == targetNodeId)
            latestBatch = sample.Data;
    }

    // 3. UI state (StructEdit documents): drain ALL.
    using var uiStateLoan = uiStateReader.Take();
    foreach (var sample in uiStateLoan)
    {
        if (sample.IsValid)
            adapter.ReceiveUiState(sample.Data);
    }

    // 4. Deserialize primitives from the latest batch.
    if (!latestBatch.HasValue || latestBatch.Value.PrimitivesData == null)
        return;
    var primitives = MemoryMarshal.Cast<byte, DebugPrimitive>(
        latestBatch.Value.PrimitivesData.AsSpan());
    foreach (ref readonly var primitive in primitives)
        renderBuffer.AppendRaw(in primitive);
},
```

The order matters:
- Interns are drained first so that any long-text labels in the batch resolve correctly.
- Only the latest primitive batch is rendered; earlier samples in the reader cache are
  discarded. This is consistent with the BestEffort + KeepLast=1 QoS of
  `DebugPrimitivesBatch`.
- `GizmoUiState` is drained before rendering so StructInspector panels reflect the
  current server-side document state.

---

### onInteraction: Forwarding Events Back to SimHost

All interaction events (Started, DragUpdate, Commit, Cancel, RawInput, StructUpdate) are
forwarded over DDS in a single `GizmoInteractionBatch`:

```csharp
onInteraction: (token, kind, pos, actionId, stateFlags, payloadJson) =>
{
    interactionWriter.Write(new GizmoInteractionBatch
    {
        SourceNodeId   = viewerNodeId,
        SequenceNumber = ++sequenceNumber,
        Kind           = kind,
        PickAnchorId   = token.AnchorId,
        PickSubElementId = token.SubElementId,
        PickStreamId   = token.StreamId,
        PickGizmoTypeId = token.GizmoTypeId,
        WorldX = pos.X,
        WorldY = pos.Y,
        WorldZ = pos.Z,
        Space  = stateFlags,     // repurposed: encodes CoordinateSpace for normal events;
                                 // RawInput stateFlags for raw events
        ActionId = actionId,
        PayloadJson = payloadJson,
    });
},
```

Note that `Space` in `GizmoInteractionBatch` is repurposed as a general `stateFlags`
field. For normal drag events it encodes `(byte)CoordinateSpace`. For `RawInput` events
it encodes bit 7 = mouse/keyboard and bit 0 = pressed/released.

---

## Diagnostics and Troubleshooting

### No primitives appear in the window

1. Verify the SimHost is running and publishing to the same domain ID.
2. Check that `--node-id` matches the `NodeId` field in the `DebugPrimitivesBatch`
   samples published by the SimHost.
3. Confirm that `PipelineTarget.Map2D` is set on the primitives being published. The
   renderer filters out any primitive where `(TargetView & Map2D) == 0`.

### Long-text labels show as truncated

The viewer window title includes the target node ID. If labels show truncated (31 chars),
the `StringInternEntry` samples have not yet arrived. TransientLocal delivery should replay
the intern history immediately on join; check that the SimHost's
`DdsStringInternPublisher` is publishing and that both nodes are on the same domain.

### Interaction events are not received by the SimHost

Verify that the SimHost has a `DdsGizmoInteractionSubscriber` polling the
`GizmoInteractionBatch` topic, and that `SourceNodeId` in outgoing events matches a node
ID the SimHost is configured to accept.

### StructInspector panels are empty

The StructEdit UI document must be published on the `GizmoUiState` topic by the SimHost.
If the SimHost does not publish `GizmoUiState`, `adapter.ReceiveUiState` is never called
and the panel falls back to a stub label.

---

## Related Projects

| Project                  | Relationship                                                         |
|--------------------------|----------------------------------------------------------------------|
| `GizmoMap.Contracts`     | Provides shared types (`DebugPrimitive`, `GizmoPrimitiveBuffer`, ...) |
| `GizmoMap.Network`       | Provides DDS topic types and transport adapters                      |
| `GizmoMap.Presentation`  | Provides `GizmoViewerFrontend` (the rendering loop)                  |
| `GizmoMap.Example`       | Reference SimHost that can be used to exercise the viewer            |
| `CycloneDDS.NET`         | DDS runtime; provides `DdsParticipant`, `DdsReader<T>`, `DdsWriter<T>` |
