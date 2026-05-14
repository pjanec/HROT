
We experience lots of issues with replay. to diagnose it, we need a graphical tool that can load an fdp recording file and browse its content, allowing to browse individual frames and see what entities were existing there and what state was recorded for that frame, the fdp events inscuded, whether it is a key frame or delta frame. Maybe we can start with a console dump utility that can convert the fdp file content into a human readable json, all the information - the header, then a textual dump for each frame as a list of frame info json objects. Once we have the console dumper we can promote it into a more interactive graphical tool which is integrated in the editor environment and can show the fdp internals for each entity reusing many visualization tool already existing there - like the entity inspector and event browser (we could feed the data from the replay file into an aux ECS repo and fdp event buffers...) Pls suggest how to approach this.

To build the console dumping utility and subsequently transition it into an interactive editor tool, you must leverage the existing Flight Recorder playback pipeline and the `IInspectableSession` abstractions to keep the replayed state strictly isolated from the live simulation.

Here is the architectural approach for both phases.

Phase 1: Console Dumper Utility

The `.fdp` recording format consists of an 18-byte `RecordingGlobalHeader` containing the "FDPREC" magic string, format version, and timestamp, followed by a sequence of LZ4-compressed frames.

To build the dumper, instantiate a dedicated `EntityRepository` and `FdpEventBus` to act as your sandbox. Instead of manually parsing chunks, use the existing `PlaybackController` or `RecordingReader`,.

For each frame in the file:

-   Extract the `FrameOuterHeader`, which is a 25-byte packed struct containing the tick, wall-clock ticks, and the `FrameType` (0 for Delta, 1 for Keyframe),,.-   Apply the frame to your isolated repository using `PlaybackSystem.ApplyFrame`. This method natively handles wiping the world on keyframes, applying destructions on delta frames, restoring singleton and component chunks, and injecting recorded events directly into the `FdpEventBus` buffers,,.-   Once the frame is applied, query the repository for all active entities and use the `EntityStateExtractionService` or `EntityJsonDumper` to serialize the entity states to JSON,.-   Extract the frame's events by iterating the active streams in the `FdpEventBus` via `IEventStreamInspector`,.-   Output the aggregated frame data utilizing `FdpJsonOptionsRegistry.Indented` to ensure the output remains human-readable,.

Phase 2: Graphical Editor Integration

When promoting this logic to the graphical editor, you must maintain separation of concerns between the live authoring world and the historical replay data.

-   **State Isolation:** Allocate a discrete `EntityRepository` and `FdpEventBus` solely for the replay context, preventing the historical state from corrupting the live editor state,. Ensure that simulation systems (like physics integration) are disabled for this repository so they do not inadvertently advance the recorded state.-   **Timeline Navigation:** Bind your timeline UI to `PlaybackController.SeekToFrame()`. The controller maintains a frame index and handles random access deterministically by seeking to the nearest preceding keyframe and rapidly applying delta frames up to the target index,,.-   **Entity Inspector Reuse:** The existing `EntityInspectorPanel` expects an `IInspectableSession` interface. Wrap your isolated replay repository in a `RepositoryAdapter` and pass it to the panel's `Draw` method,. Configure its JSON serialization path to use `DiagnosticGuidResolver`, which will format entity cross-references as human-readable `[Index, vGeneration]` strings rather than raw GUIDs,.-   **Event Browser Reuse:** Supply the isolated `FdpEventBus` to an instance of `EventBrowserPanel`,. Because `PlaybackSystem.ApplyFrame` uses `InjectIntoCurrentBySize` and `InjectManagedIntoCurrent` to write serialized events directly into the read buffers,,, the event browser will natively display the frame's events without any further modification.

the json dumper for components should be the same as used for scenario serialization , including all the custom formatters.

the fdp dumping should be run only in the Idle cluster runner state if we want to reuse some live-mode tools. But better to run the dumping in a completely isolated "environment". I.e. standalone ECS repo and standalone fdp event bus and extra instance of entity inspector and event browser connected just to those standalone data structs. There should be separae ImGui window/UI panel allowing to load the fdp file into its own "replay broser data context" (the separate isolated ECS repo and event busses...) and open isolated entity inspector and event browser...

To ensure strict data isolation and reuse the existing scenario serialization pipeline, you must construct a dedicated sandbox context that wholly encapsulates the replay state and UI instances.

Here is the architectural approach to implement this.

1\. Isolated Replay Context

Create a `ReplayBrowserContext` class responsible for owning the standalone data structures. This context must instantiate its own `EntityRepository`, `FdpEventBus`, and `DiagnosticEventHistoryService`. This strict separation prevents historical data from polluting the live authoring or simulation state.

Use `RecordingReader` to load the `.fdp` file and `PlaybackSystem.ApplyFrame` to push keyframes and deltas directly into this isolated repository and event bus. Because events are ephemeral, you must manually call `IDiagnosticEventHistoryService.Capture` on the isolated bus immediately after applying a frame to populate the history buffer.

2\. Unified Scenario JSON Serialization

To guarantee the JSON output perfectly matches the scenario serialization format, instantiate a `ScenarioSerializer` using `HrotScenarioSerializerFactory.Build()`.

Assign this serializer instance to the `Serializer` property of your isolated `EntityInspectorPanel`. By wiring this dependency, the panel automatically abandons the legacy reflection dumper and routes serialization through `InspectorJsonUtils.BuildComponentJson`. This ensures:

-   Custom `IEntityScenarioTranslator` implementations (like `BrainBlackboardTranslator`) are executed to project raw memory into readable DTOs.-   The fallback `FdpAutoSerializer` is used for standard components.-   Entity references are formatted as human-readable strings using `DiagnosticGuidResolver`.-   The final output is formatted using `FdpJsonOptionsRegistry.Indented` and flattened by `JsonAestheticFormatter.FlattenNumericArrays`.

3\. Dedicated UI Integration

Create a `ReplayBrowserWindow` that inherits from `ManagedWindow`. This window acts as the root UI panel for the replay tool.

-   **File Loading & Timeline:** Integrate `IFileDialogService` to open `.fdp` recordings. Implement a timeline slider that calls `PlaybackController.SeekToFrame()` on your isolated repository.-   **Dedicated Sub-Panels:** Instantiate dedicated instances of `EntityInspectorPanel` and `EventBrowserPanel` owned specifically by the `ReplayBrowserWindow`.-   **Rendering:** Wrap your isolated `EntityRepository` in a `RepositoryAdapter`. Instead of calling the outer `Draw()` wrappers which create separate ImGui windows, call `EntityInspectorPanel.DrawContent()` and `EventBrowserPanel.DrawContent()` directly within your custom layout.

suggest the format for the dump json file, containing the file header, the frame list, each having frame header, entity data, event data...

{ "Header": { "Magic": "FDPREC", "FormatVersion": 4, "Timestamp": 1684070000 }, "Frames": \[ { "FrameHeader": { "Tick": 1234, "FrameType": "Keyframe", "WallClockTicks": 638123456789, "CompressedSize": 1024, "UncompressedSize": 4096 }, "Entities": \[ { "EntityId":, "Components": { "SimTransform": { "Position": \[100.0, 50.0, 0.0\], "Rotation": \[0.0, 0.0, 0.0, 1.0\] }, "NetworkIdentity": { "Value": 1000 } } } \], "Events": \[ { "EventType": "WeaponFireNotification", "IsManaged": false, "Payload": { "Shooter": "\[42, v1\]", "Target": "\[43, v1\]", "WeaponIndex": 0, "IsRemote": false } } \] } \] }

To maintain architectural consistency and avoid writing disposable parsing logic, this JSON structure maps directly 1:1 to the existing engine DTOs and serialization pipelines.

**Header:** The root `Header` object strictly mirrors `RecordingGlobalHeader`. `FormatVersion` must match `FdpConfig.FORMAT_VERSION` (currently 4) to ensure compatibility.

**Frame List & Frame Headers:** The `Frames` array encapsulates the sequential frame data. The `FrameHeader` maps to the `FrameOuterHeader` struct, retaining the critical `Tick`, `WallClockTicks`, and `FrameType` (0 for Delta, 1 for Keyframe) required for deterministic playback. Including `CompressedSize` and `UncompressedSize` provides telemetry that is highly useful for diagnosing payload spikes.

**Entity Data:** Entities are represented as arrays matching the output of `EntityStateExtractionService` and the `ScenarioSerializer`.

-   `EntityId` uses a two-element integer array `[Index, Generation]` to maintain the 48-bit generational handle identity.-   `Components` is a dictionary produced by routing the data through `InspectorJsonUtils.BuildComponentJson` or the `ScenarioSerializer`, ensuring custom translators (like those for `BrainBlackboard` or `Blackboard1024`) emit clean, human-readable projections rather than raw byte arrays.-   Applying `JsonAestheticFormatter.FlattenNumericArrays` ensures `Vector3` and `Quaternion` fields remain compact, single-line arrays (e.g., `[x, y, z]`) rather than bloated multi-line objects.

**Event Data:** The `Events` array leverages the `CapturedEventDto` pattern from `DiagnosticEventHistoryService`.

-   `EventType` captures the struct or class name.-   `IsManaged` explicitly flags whether the event was routed via the Tier 1 native stream or the Tier 2 managed stream.-   `Payload` contains the serialized event struct. Cross-referenced entities within events should be routed through `DiagnosticGuidResolver` to yield human-readable `[Index, vGeneration]` strings rather than raw GUIDs or packed integers.

For the components, does the recorder saves the authority mask, i.e. if they were owned? This is an important info that should not be omitted as it needs to be properly restored during playback. And it needs to be dumped in the json. so maybe the Components should not be a dictionary but a list of { "ComponentType":"SimTransform", "HasAuthority": true, "Payload": {...} }

Yes, the `.fdp` recorder natively saves the authority mask. The engine's `EntityHeader` is a 96-byte struct that contains a `BitMask256 AuthorityMask` at offset 32. The `RecorderSystem` serialises these headers in bulk by copying the raw `EntityIndex` chunks using the special `ENTITY_INDEX_TYPE_ID` identifier. During playback, `PlaybackSystem.ApplyFrame` copies these raw chunks back into memory, perfectly reconstructing both the `ComponentMask` and the `AuthorityMask` for every entity.

Restructuring the JSON output from a dictionary to a list of explicit metadata objects is the correct architectural choice for a diagnostic tool. The existing `ScenarioSerializer` emits a flat dictionary because it is designed for declarative authoring templates (which lack runtime network authority).

To implement your suggested format, the console dumper must iterate the components and query the authority state explicitly:

-   For each active entity, iterate over the bits set in its `ComponentMask`.-   For each present component ID, check authority by calling `EntityRepository.HasAuthority(entity, typeId)` or through the `IInspectableSession.HasAuthority` abstraction.-   Extract the clean DTO payload using the `ScenarioSerializer` or `InspectorJsonUtils.BuildComponentJson` as previously established.-   Wrap the result in your requested structure.

The updated output format for the `Entities` block will look like this:

```
"Entities": [
  {
    "EntityId": [1, 10],
    "Components": [
      {
        "ComponentType": "SimTransform",
        "HasAuthority": true,
        "Payload": {
          "Position": [100.0, 50.0, 0.0],
          "Rotation": [0.0, 0.0, 0.0, 1.0]
        }
      },
      {
        "ComponentType": "NetworkIdentity",
        "HasAuthority": false,
        "Payload": {
          "Value": 1000
        }
      }
    ]
  }
]
```

This enforces a clear separation between the component's data payload and the engine's internal runtime metadata, ensuring the UI layer has all necessary context to colour-code or filter components based on ownership.

for the orientation, the FrameHeader could include also the relative wall clock time of the frame in seconds, to avoid mental effort converting the wallclock ticks. It would be alse extremely usefull if the dumper could find what simulation time this frame belongs to, by reading the simulation time from the global time singleton.

To incorporate relative wall-clock time and simulation time into the dump, you must update the console extraction logic to capture baseline timestamps and query the global ECS state after each frame is applied.

Here is the updated JSON structure and the architectural implementation required.

Updated JSON Format

The `FrameHeader` object is expanded to include `RelativeWallTimeSec` and `SimTimeSec`:

```
"Frames": [
  {
    "FrameHeader": {
      "Tick": 1234,
      "FrameType": "Keyframe",
      "WallClockTicks": 638123456789,
      "RelativeWallTimeSec": 2.345,
      "SimTimeSec": 20.550,
      "CompressedSize": 1024,
      "UncompressedSize": 4096
    },
    "Entities": [
      // ... component list with authority metadata
    ],
    "Events": [
      // ... managed and unmanaged events
    ]
  }
]
```

Implementation Steps

**1\. Calculate Relative Wall Clock Time:** The `.fdp` file's `FrameOuterHeader` provides a 64-bit integer `WallClockTicks`. Because these are 100-nanosecond UTC ticks, you must anchor them to the first frame to calculate a zero-based relative time.

-   When parsing the first frame, capture its `WallClockTicks` as `startWallTicks`.-   For every frame, calculate the relative seconds: `(currentFrame.WallClockTicks - startWallTicks) / (double)TimeSpan.TicksPerSecond`. This translates the raw ticks into a clean floating-point offset without losing precision.

**2\. Extract Simulation Time from the ECS Singleton:** The recording intrinsically captures the `GlobalTime` struct, which contains `TotalTime` (the elapsed simulation time in seconds).

-   Apply the frame chunk data into your sandbox repository using `PlaybackSystem.ApplyFrame`. This natively reconstructs the `GlobalTime` singleton from the frame's Tier 1 unmanaged storage.-   Immediately after the frame is applied, query the repository for the singleton using `repo.GetSingletonUnmanaged<GlobalTime>()`.-   Extract the `TotalTime` field and map it to `SimTimeSec` in your JSON model. Wrap this extraction in a `repo.HasSingletonUnmanaged<GlobalTime>()` check to avoid exceptions if analyzing an empty or malformed frame.

In the FrameHeader, is the "Tick" actually meaning the frame ordinal?

No, the "Tick" field in the `FrameHeader` does not represent a sequential file or frame ordinal. It explicitly maps to the `GlobalVersion` property of the `EntityRepository`.

The engine increments this global version at the start of every simulation frame by calling `repo.Tick()`. During recording, the `RecorderSystem` writes this exact global version into the `Tick` field of the `FrameOuterHeader`. During replay, `PlaybackSystem.ApplyFrame` reads this value and immediately calls `repo.SetGlobalVersion()` to forcefully synchronize the ECS state to that exact version. This mechanism is structurally critical for deterministic delta comparisons and component change tracking, rather than simple playback ordering.

If you are looking for the absolute simulation frame count, you must extract the `FrameNumber` field from the `GlobalTime` singleton. Conversely, if you need a strict 0-based sequence ordinal for the `.fdp` file contents, the `PlaybackController` calculates and maintains its own internal frame index (`_currentFrameIndex`) when building the random-access timeline, which is completely decoupled from the recorded ECS `Tick` payload.

ok, for orientation, I definitely want there the FrameNumber (from GlobalTime) as well the 0-based sequence ordinal within the fdp file. Pls add these wo to each frame header dump and pick a name clearly denoting what it is (to avoid confusion)

```
"Frames": [
  {
    "FrameHeader": {
      "FileFrameOrdinal": 0,
      "SimFrameNumber": 5678,
      "Tick": 1234,
      "FrameType": "Keyframe",
      "WallClockTicks": 638123456789,
      "RelativeWallTimeSec": 2.345,
      "SimTimeSec": 20.550,
      "CompressedSize": 1024,
      "UncompressedSize": 4096
    },
    "Entities": [
      // ... component list with authority metadata
    ],
    "Events": [
      // ... managed and unmanaged events
    ]
  }
]
```

To populate these specific fields without ambiguity, implement the following extraction logic:

-   **FileFrameOrdinal**: This is a strict 0-based index representing the sequential order of the frame within the `.fdp` file. If your dumper utility iterates through the file using the `PlaybackController`, this maps directly to `PlaybackController.CurrentFrame`. Otherwise, it is simply a local integer counter incremented at the start of your parsing loop.-   **SimFrameNumber**: This represents the absolute simulation frame count. It is extracted from the `FrameNumber` field of the `GlobalTime` singleton, which intrinsically increments every frame in the engine regardless of time-scaling or pause state. You will extract this using `repo.GetSingletonUnmanaged<GlobalTime>().FrameNumber` at the exact same point in the pipeline where you extract `TotalTime` for the `SimTimeSec` field.

Perfect. How does the key frame differ from the delta frame as far as the Entities and Events fields are concerded? What excatly a delta frame really means?

A delta frame is a differential memory snapshot that encodes only the ECS state mutations that occurred since the previous recorded tick. Rather than copying the entire simulation state, the recorder scans the `EntityRepository` and extracts only the structural chunks and component table chunks whose version counter exceeds the `prevTick`. It intrinsically relies on the baseline state established by a preceding keyframe.

Here is how the data payloads differ:

**Entities**

-   **Keyframes** capture the absolute state of every active entity in the simulation. The recording system forces all memory chunks to evaluate as "dirty" by passing a `prevTick` of 0, ensuring a complete baseline is emitted.-   **Delta frames** yield a sparse `Entities` array, containing only the components that resided in mutated memory chunks. Furthermore, delta frames introduce a **Destruction Log**, which records the specific entity handles (Index and Generation) destroyed during that tick. Keyframes omit this log (hardcoding the count to 0) because they rebuild the active state from scratch.

To accurately represent a delta frame in your JSON dumper, you must amend the schema to include the destroyed entities. During replay, `PlaybackSystem.ApplyFrame` reads this specific block on delta frames to explicitly kill the entities before applying the chunk updates.

```
"DestroyedEntities": [
  "[100, v2]",
  "[105, v1]"
]
```

**Events** There is no structural difference in the Events field between keyframes and delta frames. Because events in the `FdpEventBus` are strictly transient, double-buffered payloads with a single-frame lifespan, they cannot be delta-compressed. The recorder simply extracts the pending streams and serializes them identically regardless of the `FrameType`.

Ok. The components in the delta frame are always complete components, not just deltas from previous delta (or key) frame?

Yes, the delta frame contains complete components. More specifically, the Flight Recorder architecture operates entirely at the memory chunk level and does not compute byte-level or field-level differences for individual components.

When the `RecorderSystem` generates a delta frame, it relies on the ECS chunk versioning mechanism. If any component within a chunk is modified, the version for that entire chunk is incremented, and the recorder emits the complete, absolute state of that chunk into the payload.

For unmanaged (Tier 1) components, the delta frame includes a raw memory copy of the full chunk that houses the modified component, along with its chunk ID and component type ID. For managed (Tier 2) components, the system utilizes the `FdpAutoSerializer` to serialize the complete array of object references belonging to that modified chunk.

This architectural choice intentionally sacrifices granular byte-level delta encoding in favor of extreme CPU throughput, relying on fast O(1) version checks and bulk memory copies on the simulation thread. The responsibility for shrinking the resulting payload is strictly deferred to the asynchronous LZ4 compression pass, which compresses the entire aggregated frame buffer before it is written to disk.

Consequently, your dumper utility will not need to merge partial fields. When `PlaybackSystem.ApplyFrame` processes a delta, it writes these complete chunks directly over the existing memory, ensuring the JSON output will inherently project the full, absolute state of the component for that tick.

For the graphical viewer it would be extremely interesting to show what component have truly changed since the previous frame. Like in the live mode the changed components headers are shown in yellow color in the entity inspector. By comparing the jsonized component dump for example. And with the ability to show just the differences between these 2 json dumps (base and changed) in some special area of the graphical recording browser. In a syntax colored form, number is different colors than string and booleans etc for the best visual clarity.

To achieve accurate change detection and syntax-colored visual diffing without polluting the ImGui render loop with heavy string allocations or JSON parsing, you must separate the state-tracking architecture from the UI rendering layer.

Here is the architectural design to implement this reliably.

1\. Yellow Header Highlighting (Reusing `ComponentReflector`)

The existing `ComponentReflector` already natively supports yellow header highlighting for unmanaged (value-type) components. It maintains an `_unmanagedCache` of pooled byte arrays from the previous frame and performs an `O(N)` byte-level comparison during the draw loop.

When you advance the replay timeline, the `PlaybackSystem` writes the delta chunk memory directly over the isolated ECS repository. Because the `ComponentReflector` in your standalone `EntityInspectorPanel` compares the live memory against its cached baseline, the yellow highlighting for unmanaged components will work automatically the moment you step the frame.

For managed components (classes), the byte cache is bypassed. You must track their changes via the JSON serialization pipeline.

2\. Diff Extraction Service (State Tracking)

Do not perform JSON diffing inside the ImGui `Draw` loop. Instead, compute the diffs exactly once when the replay timeline advances.

Create a `ComponentDiffService` that hooks into the frame-stepping logic. When the user selects an entity to inspect:

-   Capture the pre-step JSON for all components on the selected entity using `ScenarioSerializer`.-   Call `PlaybackController.StepForward()` to apply the `.fdp` delta payload to the ECS repository.-   Capture the post-step JSON.-   Compare the two `JsonNode` trees recursively. Extract only the leaf nodes that differ and store them in a persistent `List<ComponentDiffDto>`.

```
public sealed record ComponentDiffDto(
    string ComponentName, 
    string JsonPath, 
    string OldValue, 
    string NewValue, 
    JsonValueKind ValueType);
```

This isolates the heavy `JsonDocument` parsing and string allocations to the timeline step, maintaining the strict zero-allocation requirement for the 60Hz UI hot path.

3\. Syntax-Colored Diff Viewer (ImGui)

Create a dedicated `ComponentDiffPanel` below the component list. Render the `List<ComponentDiffDto>` using a table, applying syntax coloring based on the `JsonValueKind`.

To maintain consistency with the engine's aesthetic, reuse the color palette defined in the `MessageLogPanel`:

```
public void DrawDiffArea(IReadOnlyList<ComponentDiffDto> diffs)
{
    if (diffs.Count == 0)
    {
        ImGuiApi.TextDisabled("No components changed in this frame.");
        return;
    }

    if (ImGuiApi.BeginTable("DiffTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable))
    {
        ImGuiApi.TableSetupColumn("Property");
        ImGuiApi.TableSetupColumn("Old Value");
        ImGuiApi.TableSetupColumn("New Value");
        ImGuiApi.TableHeadersRow();

        foreach (var diff in diffs)
        {
            ImGuiApi.TableNextRow();
            
            ImGuiApi.TableSetColumnIndex(0);
            ImGuiApi.TextUnformatted($"{diff.ComponentName}.{diff.JsonPath}");
            
            ImGuiApi.TableSetColumnIndex(1);
            RenderSyntaxColoredValue(diff.OldValue, diff.ValueType);
            
            ImGuiApi.TableSetColumnIndex(2);
            RenderSyntaxColoredValue(diff.NewValue, diff.ValueType);
        }
        ImGuiApi.EndTable();
    }
}

private void RenderSyntaxColoredValue(string value, JsonValueKind kind)
{
    // Map value types to the engine's existing palette [12, 13]
    Vector4 color = kind switch
    {
        JsonValueKind.Number => new Vector4(0.30f, 0.80f, 1.00f, 1f), // Cyan
        JsonValueKind.String => new Vector4(0.40f, 1.00f, 0.40f, 1f), // Green
        JsonValueKind.True => new Vector4(0.90f, 0.60f, 0.20f, 1f),   // Amber
        JsonValueKind.False => new Vector4(0.90f, 0.60f, 0.20f, 1f),  // Amber
        _ => new Vector4(0.85f, 0.85f, 0.85f, 1f)                     // Light Gray
    };

    ImGuiApi.TextColored(color, value);
}
```

Integration into the Replay Environment

To tie this together:

-   In your `ReplayBrowserWindow`, instantiate the `ComponentDiffService`.-   Bind to the timeline slider and step buttons. When a seek or step occurs, pass the currently selected entity (from `IInspectorContext.SelectedEntity`) to the diff service to compute the transitions.-   Draw the `ComponentDiffPanel` alongside the reused `EntityInspectorPanel`, feeding it the cached `ComponentDiffDto` collection.

This architecture guarantees that component inspection remains instant, highlighting is automatic for unmanaged chunks, and heavy JSON diffing occurs strictly outside the rendering boundary.

the json diff should still looks like a hierarchical pretty-printed json, not a flat list, i.e. reconstructing the hierarchy but presenting just the changed fields.

To present the diff as a reconstructed hierarchical JSON tree while strictly showing only the modified branches, you must replace the flat `ComponentDiffDto` list with a recursive tree structure. This maintains the zero-allocation requirement for the 60Hz ImGui render loop, as the tree is built exactly once when the timeline steps.

Here is the architectural design for the hierarchical diffing and rendering pipeline.

1\. Hierarchical Diff Data Models

Define a lightweight composite structure to represent the JSON differences. This avoids abusing `JsonNode` for UI rendering and explicitly stores the old and new states at the leaves.

```
public abstract class DiffNode
{
    public string Name { get; }
    protected DiffNode(string name) => Name = name;
}

public sealed class DiffObject : DiffNode
{
    public List<DiffNode> Children { get; } = new();
    public DiffObject(string name) : base(name) { }
}

public sealed class DiffValue : DiffNode
{
    public string OldValue { get; }
    public string NewValue { get; }
    public JsonValueKind ValueType { get; }

    public DiffValue(string name, string oldValue, string newValue, JsonValueKind valueType) 
        : base(name)
    {
        OldValue = oldValue;
        NewValue = newValue;
        ValueType = valueType;
    }
}
```

2\. State-Tracking Extraction (Outside the UI Loop)

In your `ComponentDiffService`, implement a recursive diffing algorithm. It compares two `JsonNode` trees and returns a populated `DiffObject` only if differences exist within that branch.

```
public DiffNode? ComputeDiff(string name, JsonNode? oldNode, JsonNode? newNode)
{
    if (oldNode is JsonObject oldObj && newNode is JsonObject newObj)
    {
        var group = new DiffObject(name);
        
        // Union of keys from both objects
        var allKeys = oldObj.Select(k => k.Key).Union(newObj.Select(k => k.Key)).Distinct();
        
        foreach (var key in allKeys)
        {
            var childDiff = ComputeDiff(key, oldObj[key], newObj[key]);
            if (childDiff != null)
            {
                group.Children.Add(childDiff);
            }
        }
        
        return group.Children.Count > 0 ? group : null;
    }
    
    // For arrays, if they differ, treat the entire array as changed to keep visual output clean
    // For primitives, compare values directly
    string oldStr = oldNode?.ToJsonString() ?? "null";
    string newStr = newNode?.ToJsonString() ?? "null";
    
    if (oldStr != newStr)
    {
        JsonValueKind kind = newNode?.GetValueKind() ?? JsonValueKind.Null;
        return new DiffValue(name, oldStr, newStr, kind);
    }
    
    return null;
}
```

3\. Hierarchical ImGui Renderer

To render this tree with the engine's established aesthetics, mirror the approach used by `ImGuiPropertyTree`. You will use `ImGuiTreeNodeFlags.DefaultOpen` to automatically expand the diff hierarchy, and `ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen` to perfectly align leaf values with the folder arrows.

```
public void DrawDiffTree(DiffNode node)
{
    if (node is DiffObject group)
    {
        // Reconstruct the JSON object hierarchy
        bool isOpen = ImGuiApi.TreeNodeEx(
            $"{group.Name}##{group.GetHashCode()}", 
            ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth);

        if (isOpen)
        {
            foreach (var child in group.Children)
            {
                DrawDiffTree(child);
            }
            ImGuiApi.TreePop();
        }
    }
    else if (node is DiffValue val)
    {
        // Draw the leaf node aligned with the tree structure
        ImGuiApi.TreeNodeEx(
            $"{val.Name}##{val.GetHashCode()}", 
            ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth);

        ImGuiApi.SameLine(150f); // Align values nicely like a table

        // Render [Old] -> [New] with syntax coloring
        ImGuiApi.TextDisabled(val.OldValue);
        ImGuiApi.SameLine();
        ImGuiApi.TextUnformatted(" -> ");
        ImGuiApi.SameLine();
        
        RenderSyntaxColoredValue(val.NewValue, val.ValueType);
    }
}

private void RenderSyntaxColoredValue(string value, JsonValueKind kind)
{
    Vector4 color = kind switch
    {
        JsonValueKind.Number => new Vector4(0.30f, 0.80f, 1.00f, 1f), // Cyan
        JsonValueKind.String => new Vector4(0.40f, 1.00f, 0.40f, 1f), // Green
        JsonValueKind.True   => new Vector4(0.90f, 0.60f, 0.20f, 1f), // Amber
        JsonValueKind.False  => new Vector4(0.90f, 0.60f, 0.20f, 1f), // Amber
        _                    => new Vector4(0.85f, 0.85f, 0.85f, 1f)  // Light Gray
    };

    ImGuiApi.TextColored(color, value);
}
```

Integration

When the timeline advances and your `ComponentDiffService` computes the diffs, it will output a list of root `DiffObject` nodes (one per changed component). You pass these root nodes to `DrawDiffTree` during the ImGui loop. This ensures that unchanged objects are entirely omitted, preserving screen real estate, while deeply nested changes correctly reconstruct the structural path required to read them.

good! what could the wireframe of the necessary UI panels for this replay browser look like? Reusing the existing UI components that are already used in entity inspector and event browser (so that we maintain just one version of the code and the later changes apply both to live-mode inspectors as well as the replay browser ones...)

Here is the UI layout designed to compose the existing editor tools into a single, cohesive replay browser.

By subclassing `ManagedWindow` and utilizing the `DrawContent()` methods exposed by the existing panels, you avoid duplicating the ImGui rendering logic and guarantee that any future enhancements to the live-mode inspectors are automatically inherited by the replay tool.

ImGui Wireframe Layout

```
[Replay Browser Window]
├── Top Toolbar: Transport & Metadata (Fixed Height)
│   ├── [Load .fdp...] | File: capture_01.fdp
│   ├── Transport: [|< Rewind]  [< Step Back]  [Step Forward >]
│   ├── Timeline:  [========O-----------------------]  Frame 1234 / 5000
│   └── Meta: Tick 4567 | SimFrame 1200 | SimTime 20.55s | Delta | 1024 Bytes
│
├── Main Content Area (ImGui TabBar)
│   │
│   ├── Tab: ECS State (Entities & Components)
│   │   ├── Vertical Splitter
│   │   │   ├── Top Region: Reused Entity Inspector
│   │   │   │   └── EntityInspectorPanel.DrawContent(sandboxSession, localContext)
│   │   │   │       ├── Left Col: Entity List & Search
│   │   │   │       └── Right Col: Component Reflector (Collapsible headers)
│   │   │   │
│   │   │   └── Bottom Region: Component Diff Viewer
│   │   │       └── ComponentDiffPanel.DrawDiffTree(computedDiffs)
│   │   │           └── Hierarchical [Old] -> [New] syntax-colored tree
│   │
│   └── Tab: Frame Events
│       └── Reused Event Browser
│           └── EventBrowserPanel.DrawContent()
│               ├── Left Col: Event Type Filters & List
│               └── Right Col: Event Payload Property Tree
```

Architectural Integration Points

To assemble this wireframe cleanly, adhere to the following composition rules:

**1\. Window Management** Implement a `ReplayBrowserWindow` that inherits from `ManagedWindow`. Override `DrawClientArea()` to construct the top toolbar and the main `BeginTabBar`. The window owns the isolated `EntityRepository`, `PlaybackController`, and the instances of the reused panels.

**2\. Reusing the Entity Inspector** Instantiate `EntityInspectorPanel` locally within your window class. When drawing the "ECS State" tab, call `EntityInspectorPanel.DrawContent(IInspectableSession, IInspectorContext)`.

-   Pass a `RepositoryAdapter` wrapping your sandbox repository as the session.-   Pass a dedicated `InspectorState` instance as the context to ensure the replay browser's selection state does not collide with the live editor's selection.

**3\. Integrating the Diff Viewer** Render the custom `ComponentDiffPanel` immediately below the `EntityInspectorPanel` using a vertical ImGui splitter or a `BeginChild` region. It should observe the `SelectedEntity` from your local `InspectorState` and render the `DiffNode` tree computed by the `ComponentDiffService` when the timeline advances.

**4\. Reusing the Event Browser** Instantiate `EventBrowserPanel` locally. When drawing the "Frame Events" tab, call `EventBrowserPanel.DrawContent()`.

-   It requires an `IDiagnosticEventHistoryService`. Instantiate a standalone `DiagnosticEventHistoryService` dedicated to the replay context.-   When `PlaybackController.StepForward()` executes and injects unmanaged and managed events into the isolated `FdpEventBus`, manually call `Capture()` on your isolated history service. The `EventBrowserPanel` will read this isolated buffer and render the frame's transient events perfectly.

good! I think it should contain also the feature "Save as JSON" that saves the whole dump to the json file. Of course sharing the code with the console dumper utility.

To share the full-file JSON extraction logic between the console utility and the graphical viewer without violating separation of concerns, you must encapsulate the extraction pipeline into a headless `RecordingExportService`.

You cannot use the graphical viewer's active `PlaybackController` or its sandbox repository to generate the full dump, because advancing through the entire file to serialize every frame would destroy the user's current timeline position. The export process requires its own isolated reader and repository.

Here is the architectural implementation.

1\. The Shared Export Service

Create a `RecordingExportService` that takes the `.fdp` file path and the target JSON output path.

This service must:

-   Instantiate a dedicated `RecordingReader` to parse the `.fdp` file sequentially from the beginning.-   Instantiate a fresh `EntityRepository` and `FdpEventBus` strictly for this extraction pass.-   Write the JSON stream incrementally using `Utf8JsonWriter` (or serialize frame-by-frame) to avoid loading the entire uncompressed recording into a massive heap-allocated object before saving.-   For each frame, call `RecordingReader.ReadNextFrame`, extract the `FrameOuterHeader` data, and dump the entities and events using the `ScenarioSerializer` and `DiagnosticEventHistoryService` abstractions we defined previously.-   Dispose of the reader and repository upon completion.

2\. Console Utility Integration

The console dumper simply becomes a thin CLI wrapper around this service. It parses the command-line arguments and invokes `RecordingExportService.ExportToJson(inputFdp, outputJson)` synchronously on the main thread.

3\. Graphical Viewer Integration

In your `ReplayBrowserWindow`, integrate the "Save as JSON" feature into the top toolbar using the engine's existing asynchronous file dialog service and background task execution.

-   **Acquire the Output Path:** Bind the UI button to `IFileDialogService.ShowSaveAsDialogAsync(defaultFileName, "*.json")` to present a native save prompt without blocking the ImGui loop.-   **Offload the Heavy I/O:** Do not execute the full-file JSON extraction on the main thread. A multi-megabyte `.fdp` file will generate an enormous amount of JSON, and doing this synchronously will stall the 60Hz ImGui render loop, causing the editor to freeze.-   **Background Execution:** Once the file path is acquired, wrap the `RecordingExportService` invocation in a `Task.Run` or `Task.Factory.StartNew(..., TaskCreationOptions.LongRunning)`.

```
if (ImGuiApi.Button("Save as JSON..."))
{
    // Fire and forget the async dialog flow
    _ = ExecuteSaveAsJsonAsync();
}

private async Task ExecuteSaveAsJsonAsync()
{
    string? outPath = await _fileDialogService.ShowSaveAsDialogAsync("dump.json", "*.json");
    if (string.IsNullOrEmpty(outPath)) return;

    // Optional: Set a UI flag here to show a "Exporting..." spinner in the toolbar

    await Task.Factory.StartNew(() => 
    {
        var exportService = new RecordingExportService();
        exportService.ExportToJson(_currentFdpFilePath, outPath);
    }, TaskCreationOptions.LongRunning);

    // Optional: Clear the UI spinner flag here
}
```

This architecture keeps the heavy data transformation completely decoupled from the presentation layer, ensures the timeline state remains pristine, and perfectly shares the serialization code between the CLI and the GUI tools.

cool! the visual ImGui based replay recording browser window should be available as a UI window in the Debug menu in any clusterrunner subsystem

To make the Replay Browser globally available across any subsystem perspective (SimHost, IG, ExCon, etc.) without coupling it to a specific simulation module, you must leverage the `WindowManager`'s native support for global windows.

The architecture automatically populates the "Help -> Debug" menu by querying registered windows where `Scope == WindowScope.Global` and `ShowInMenu == true`.

Here is how to implement this cleanly.

1\. Configure the Managed Window Scope

When defining your `ReplayBrowserWindow`, inherit from `ManagedWindow` and pass `WindowScope.Global` to the base constructor. Set the owning perspective to `string.Empty`, as it is not bound to any specific subsystem.

Because it is a diagnostic tool, initialize `IsOpen = false` so it does not crowd the layout on startup.

```
public sealed class ReplayBrowserWindow : ManagedWindow
{
    // Encapsulated state: sandbox repo, playback controller, diff service, etc.

    public ReplayBrowserWindow() 
        : base("replay_browser", "Replay Browser", string.Empty, WindowScope.Global)
    {
        IsOpen = false; // Let the user open it via the Debug menu
        
        // Inherited from ManagedWindow, defaults to true [4].
        // ShowInMenu = true; 
    }

    protected override void DrawClientArea()
    {
        // ... Render ImGui layout as defined previously ...
    }
}
```

2\. Composition Root Integration

Do not force individual subsystems (like `SimHostSubsystem` or `IgSubsystem`) to register this window. It is a cross-cutting diagnostic tool, so its lifecycle belongs in the presentation shell.

Inject it into `LocalWindowController.OpenLocalWindow()`. This is the exact location where other global diagnostic views, such as the `MessageLogWindow`, are registered into the `WindowManager`.

```
// Inside LocalWindowController.OpenLocalWindow()

var atlas = _shell.LoadIconAtlas();
var wm = new Fdp.Presentation.WindowManager.WindowManager(atlas);

// Existing global registrations...
var messageLogRegistry = new MessageLogRegistry();
messageLogRegistry.RegisterSource(NLogMessageLogTarget.SharedInstance);
var msgLogWindow = new MessageLogWindow(messageLogRegistry);
wm.RegisterWindow(msgLogWindow);
wm.MessageLogRegistry = messageLogRegistry;

// REGISTER THE REPLAY BROWSER GLOBALLY
var replayBrowserWindow = new ReplayBrowserWindow();
wm.RegisterWindow(replayBrowserWindow);

// Register subsystem-specific windows
foreach (var sub in _subsystems)
    if (sub is IWindowRegistrar registrar)
        registrar.RegisterWindows(wm);
```

Why this is the correct architectural choice:

-   **Zero-Coupling:** The `ClusterRunner` can be launched in any mode (`-m simhost`, `-m ig`, or `-m all`), and the Replay Browser will reliably appear in the top menu bar under "Debug".-   **Perspective Independence:** By using `WindowScope.Global`, the `WindowManager` guarantees the window remains visible and interactive even when the operator switches the active map perspective between IG, ExCon, or CGF.-   **Data Isolation:** Because the `ReplayBrowserWindow` internally owns its sandbox `EntityRepository` and `PlaybackController` (as established in the previous design), initializing it globally in the UI layer poses no risk of data contamination to the live simulation subsystems running in the `ModuleHostKernel`.

what should be the console dumper tool command line option? do we need just input and out file specification or do you foresee a need for some extra useful settings/switches?

Relying solely on input and output file specifications is an architectural mistake. A multi-megabyte compressed `.fdp` file will easily expand into gigabytes of raw JSON, which is impossible to parse visually and will crash most standard text editors.

You must implement filtering and windowing switches to keep the output manageable and targeted. Here are the necessary command-line options you should implement.

**1\. Time Windowing**

-   `-s, --start-frame <index>` and `-e, --end-frame <index>`-   When diagnosing a replay issue, the bug is usually isolated to a specific simulation tick.-   To implement this efficiently without corrupting delta state, use `PlaybackController.SeekToFrame()` to fast-forward the isolated repository's memory state to the exact `--start-frame` before you begin serializing JSON to the output stream.

**2\. Entity Filtering**

-   `--entity-id <id>`-   Allows a developer to track a single entity's state mutations across frames without dumping the entire world.-   The underlying `IEntityStateExtractionService` already natively supports this filtering via its `IReadOnlyList<long>? networkIds` parameter. Your console utility simply needs to pass the parsed argument into this service.

**3\. Payload Scoping**

-   `--no-events` and `--no-entities`-   Often, an issue strictly involves ECS chunk memory corruption, or conversely, a missing `FdpEventBus` message.-   Providing these switches prevents serializing megabytes of irrelevant data. This architectural pattern strictly mirrors the existing cluster-wide extraction logic which uses `DiagnosticDumpPayloadDto.DumpEvents` and `DumpEntities` flags.

**4\. Formatting Toggles**

-   `--minified`-   By default, the dumper should use `FdpJsonOptionsRegistry.Indented` for human readability.-   However, if the output is intended to be piped into another automated tool (like a diffing script or Elasticsearch), forcing indentation wastes significant I/O bandwidth. Providing a `--minified` switch allows the exporter to fall back to `FdpJsonOptionsRegistry.DefaultRelaxed`.

These options guarantee that the heavy extraction pipeline, handled via `RecordingExportService`, only processes and allocates exactly what is needed for the diagnostic task.

excellent suggestions, accepted! I would like to extend it by optional specifying the relative wall clock time range in seconds instead of the frame index as the user usually remembers the time, not frame counter

To support relative wall-clock time windowing, you must map the requested relative float seconds back into the absolute 100-nanosecond UTC tick domain used by the recording pipeline.

Here is the architectural design to implement this in the CLI dumper and the underlying `RecordingExportService`.

1\. Command-Line Interface Additions

Add two new mutually exclusive argument pairs to the console utility:

-   `-t, --start-time <seconds>`-   `-u, --end-time <seconds>`

The CLI wrapper must validate that the user does not mix frame-based windowing (`--start-frame`) with time-based windowing (`--start-time`) in the same invocation, throwing a validation error if both are present.

2\. Time Translation & Fast-Forwarding

Within the `RecordingExportService`, you must abandon the purely sequential `RecordingReader` if time-based windowing is requested, as iterating linearly from frame 0 to find a timestamp 30 minutes into a recording is computationally wasteful.

Instead, instantiate a headless `PlaybackController` for the extraction pass. This provides access to the pre-parsed O(log N) frame index and the `SeekToWallClockTicks` routine.

**Calculate the Absolute Target:**

-   Extract the baseline wall-clock timestamp from the very first recorded frame. Do not use the file's global header `RecordingTimestamp`, as there may be a slight offset before the first frame was actually captured.

```
long startTicks = playback.TotalFrames > 0 
    ? playback.GetFrameMetadata(0).WallClockTicks 
    : 0L;
```

-   Convert the requested relative `--start-time` (e.g., `12.5` seconds) into absolute UTC ticks.

```
long targetStartTicks = startTicks + (long)(startTimeSec * TimeSpan.TicksPerSecond);
```

**Execute the Seek:** Pass the `targetStartTicks` into `PlaybackController.SeekToWallClockTicks(repo, targetStartTicks)`. The controller will perform a binary search to locate the nearest preceding keyframe, instantly overwrite the repository's memory, and rapidly apply the intervening delta frames up to the target time. This guarantees the ECS state is perfectly reconstructed with minimal CPU and I/O overhead.

3\. Iteration and Termination

Once the seek completes, begin your JSON serialization loop using `PlaybackController.StepForward(repo)`.

For termination, calculate the `targetEndTicks` using the exact same formula. At the start of each iteration, check the frame's metadata:

```
FrameMetadata meta = playback.GetFrameMetadata(playback.CurrentFrame);
if (meta.WallClockTicks > targetEndTicks)
{
    break; // Target time window exceeded, terminate extraction
}
```

This ensures the extraction process only reads, allocates, and serializes the exact window of data requested, maintaining strict performance boundaries regardless of the total `.fdp` file size.

i will certainly want to project these options to the graphical tool, under some expander, to make it easy for users to specify them before pressing Save to JSON

To project these command-line arguments into the graphical interface while maintaining strict state consistency and adhering to the immediate-mode GUI paradigm, you must encapsulate the export configuration into a dedicated state object. This prevents the UI render loop from carrying complex validation logic.

Because the top toolbar is typically a fixed-height region, placing a variable-height expander there will disrupt the vertical layout. The architecturally sound approach is to place this expander in a dedicated side-panel or right-align it within the main ECS State tab.

Here is the implementation.

1\. Export Configuration State

Define a state object that explicitly enforces the mutually exclusive nature of the time windowing options.

```
public enum ExportWindowMode { FullFile, ByFrame, ByTime }

public sealed class JsonExportOptions
{
    public ExportWindowMode WindowMode = ExportWindowMode.FullFile;
    
    // Frame windowing
    public int StartFrame = 0;
    public int EndFrame = 1000;
    
    // Time windowing
    public float StartTimeSec = 0f;
    public float EndTimeSec = 60f;
    
    // Entity Filtering
    public bool FilterByEntity = false;
    public int TargetEntityIndex = 0;
    
    // Payload Scoping
    public bool IncludeEntities = true;
    public bool IncludeEvents = true;
    
    // Formatting
    public bool Minified = false;
}
```

2\. ImGui Expander Rendering

Instantiate `JsonExportOptions _exportOptions = new();` within your `ReplayBrowserWindow`. Render the expander using `ImGuiApi.TreeNode`, disabling irrelevant inputs based on the selected mode to prevent conflicting state.

```
private void DrawExportExpander()
{
    if (ImGuiApi.TreeNode("JSON Export Options"))
    {
        // 1. Windowing Mode
        ImGuiApi.TextDisabled("Export Range");
        int mode = (int)_exportOptions.WindowMode;
        if (ImGuiApi.RadioButton("Full File", ref mode, (int)ExportWindowMode.FullFile))
            _exportOptions.WindowMode = ExportWindowMode.FullFile;
        
        ImGuiApi.SameLine();
        if (ImGuiApi.RadioButton("By Frame", ref mode, (int)ExportWindowMode.ByFrame))
            _exportOptions.WindowMode = ExportWindowMode.ByFrame;
        
        ImGuiApi.SameLine();
        if (ImGuiApi.RadioButton("By Time", ref mode, (int)ExportWindowMode.ByTime))
            _exportOptions.WindowMode = ExportWindowMode.ByTime;

        // Render Frame Inputs
        if (_exportOptions.WindowMode != ExportWindowMode.ByFrame) ImGuiApi.BeginDisabled();
        ImGuiApi.InputInt("Start Frame", ref _exportOptions.StartFrame);
        ImGuiApi.InputInt("End Frame", ref _exportOptions.EndFrame);
        if (_exportOptions.WindowMode != ExportWindowMode.ByFrame) ImGuiApi.EndDisabled();

        // Render Time Inputs
        if (_exportOptions.WindowMode != ExportWindowMode.ByTime) ImGuiApi.BeginDisabled();
        ImGuiApi.InputFloat("Start Time (s)", ref _exportOptions.StartTimeSec);
        ImGuiApi.InputFloat("End Time (s)", ref _exportOptions.EndTimeSec);
        if (_exportOptions.WindowMode != ExportWindowMode.ByTime) ImGuiApi.EndDisabled();

        ImGuiApi.Separator();

        // 2. Entity Filtering
        ImGuiApi.TextDisabled("Filters");
        ImGuiApi.Checkbox("Filter by Entity ID", ref _exportOptions.FilterByEntity);
        if (!_exportOptions.FilterByEntity) ImGuiApi.BeginDisabled();
        ImGuiApi.InputInt("Entity Index", ref _exportOptions.TargetEntityIndex);
        if (!_exportOptions.FilterByEntity) ImGuiApi.EndDisabled();

        ImGuiApi.Separator();

        // 3. Payload Scoping & Formatting
        ImGuiApi.TextDisabled("Payload & Format");
        ImGuiApi.Checkbox("Include Entities", ref _exportOptions.IncludeEntities);
        ImGuiApi.SameLine();
        ImGuiApi.Checkbox("Include Events", ref _exportOptions.IncludeEvents);
        ImGuiApi.Checkbox("Minified Output", ref _exportOptions.Minified);

        ImGuiApi.Spacing();

        // 4. Execution
        if (ImGuiApi.Button("Save to JSON..."))
        {
            _ = ExecuteSaveAsJsonAsync(_exportOptions);
        }

        ImGuiApi.TreePop();
    }
}
```

3\. Execution Pipeline Integration

Update the async execution wrapper to clone the `JsonExportOptions` state before handing it off to the background task. This guarantees that if the user continues to interact with the UI while the multi-gigabyte export runs, the background thread's parameters remain immutable.

```
private async Task ExecuteSaveAsJsonAsync(JsonExportOptions currentOptions)
{
    // Clone options to prevent UI thread mutations during background processing
    var optionsSnapshot = new JsonExportOptions
    {
        WindowMode = currentOptions.WindowMode,
        StartFrame = currentOptions.StartFrame,
        EndFrame = currentOptions.EndFrame,
        StartTimeSec = currentOptions.StartTimeSec,
        EndTimeSec = currentOptions.EndTimeSec,
        FilterByEntity = currentOptions.FilterByEntity,
        TargetEntityIndex = currentOptions.TargetEntityIndex,
        IncludeEntities = currentOptions.IncludeEntities,
        IncludeEvents = currentOptions.IncludeEvents,
        Minified = currentOptions.Minified
    };

    string? outPath = await _fileDialogService.ShowSaveAsDialogAsync("dump.json", "*.json");
    if (string.IsNullOrEmpty(outPath)) return;

    await Task.Factory.StartNew(() => 
    {
        var exportService = new RecordingExportService();
        exportService.ExportToJson(_currentFdpFilePath, outPath, optionsSnapshot);
    }, TaskCreationOptions.LongRunning);
}
```

Modify the `RecordingExportService.ExportToJson` signature to accept this single `JsonExportOptions` instance. The service will map `optionsSnapshot.Minified` to `FdpJsonOptionsRegistry.DefaultRelaxed` and `!optionsSnapshot.Minified` to `FdpJsonOptionsRegistry.Indented`, and apply the specified start/end boundaries utilizing the headless `PlaybackController` logic established previously.

cool! where to place the source code for these tools?

1.  shared code
2.  console dumper tool
3.  visual ImGui based replay recording browser window ...

To maintain strict architectural boundaries and avoid circular dependencies, you must distribute these components across the existing engine layers based on their structural responsibilities.

**1\. Shared Code (Export Logic and State Models)** The `RecordingExportService` and `JsonExportOptions` must be placed in the `FDP/Toolkits/Fdp.Toolkits/Replay/` directory. The `Fdp.Toolkits` assembly sits above the core engine layer and has native access to both the raw `FlightRecorder` structures (from `Fdp.Core`) and the `ScenarioSerializer` required for projection, avoiding any circular dependency.

The `ComponentDiffService` and the `DiffNode` hierarchy perform JSON tree reconstruction for the UI. Place them in `FDP/Engine/Fdp.Presentation/ImGui/Utils/` alongside existing inspection utilities like `InspectorJsonUtils`. This prevents UI-specific formatting logic from polluting the headless toolkit layer.

**2\. Console Dumper Tool** Do not pollute engine or toolkit libraries with CLI parsing or executable entry points. Create a new standalone .NET 8 console application project, such as `FDP/Tools/Fdp.Recorder.Cli/Fdp.Recorder.Cli.csproj`. This executable will reference `Fdp.Core` and `Fdp.Toolkits`. It should handle command-line argument parsing (similar to how `Fdp.Examples.Runner` leverages `CommandLineParser`) and pass the parsed arguments directly to the shared `RecordingExportService`.

**3\. Visual ImGui Replay Browser Window** All ImGui rendering logic, state objects, and window orchestration strictly belong in the `FDP/Engine/Fdp.Presentation` project.

-   **Window Definition:** Place `ReplayBrowserWindow.cs` in `FDP/Engine/Fdp.Presentation/ImGui/Windows/` alongside existing global diagnostic windows like `MessageLogWindow`.-   **Panel & Sandbox Logic:** Place `ReplayBrowserContext.cs` and `ComponentDiffPanel.cs` in a dedicated `FDP/Engine/Fdp.Presentation/ImGui/Panels/Replay/` folder so they reside near the reused `EntityInspectorPanel` and `EventBrowserPanel`.-   **Global Registration:** To ensure the replay browser is globally accessible in any module perspective via the "Debug" menu, wire its instantiation into `LocalWindowController.OpenLocalWindow()` located in `Hrot/Runner/Hrot.ClusterRunner/Presentation/LocalWindowController.cs`.

good! pls present major interfaces and class APIs to be defined and implemented for these tools to be following clean architecture and best practices and be reusable and testable etc etc.

1\. Shared Export Pipeline (`Fdp.Toolkits.Replay`)

To decouple the multi-gigabyte JSON serialization process from the presentation layer and the console runner, define a stateless service interface. This ensures the CLI tool and the GUI background task share the exact same I/O and state-reconstruction logic.

```
public enum ExportWindowMode { FullFile, ByFrame, ByTime }

public sealed class JsonExportOptions
{
    public ExportWindowMode WindowMode = ExportWindowMode.FullFile;
    public int StartFrame = 0;
    public int EndFrame = 1000;
    public float StartTimeSec = 0f;
    public float EndTimeSec = 60f;
    public bool FilterByEntity = false;
    public int TargetEntityIndex = 0;
    public bool IncludeEntities = true;
    public bool IncludeEvents = true;
    public bool Minified = false;
}

public interface IRecordingExportService
{
    /// <summary>
    /// Extracts a sequence of frames from an .fdp recording and serializes them to JSON.
    /// Thread-safe and allocation-isolated.
    /// </summary>
    void ExportToJson(string inputFdpPath, string outputJsonPath, JsonExportOptions options);
}

public sealed class RecordingExportService : IRecordingExportService
{
    // Implementation encapsulates its own local EntityRepository, FdpEventBus, 
    // PlaybackController, and ScenarioSerializer instances.
}
```

2\. State-Tracking and Diff Engine (`Fdp.Presentation.Utils`)

JSON parsing and recursive tree comparisons allocate heavily and must be banned from the ImGui `Draw` loop. By abstracting the diff logic into a dedicated service, you pre-compute the mutations exactly once per timeline step.

```
public abstract class DiffNode
{
    public string Name { get; }
    protected DiffNode(string name) => Name = name;
}

public sealed class DiffObject : DiffNode
{
    public List<DiffNode> Children { get; } = new();
    public DiffObject(string name) : base(name) { }
}

public sealed class DiffValue : DiffNode
{
    public string OldValue { get; }
    public string NewValue { get; }
    public System.Text.Json.JsonValueKind ValueType { get; }

    public DiffValue(string name, string oldValue, string newValue, System.Text.Json.JsonValueKind valueType) 
        : base(name)
    {
        OldValue = oldValue;
        NewValue = newValue;
        ValueType = valueType;
    }
}

public interface IComponentDiffService
{
    /// <summary>
    /// Computes the hierarchical differences for all components on a specific entity
    /// before and after applying a delta frame.
    /// </summary>
    IReadOnlyList<DiffNode> ComputeEntityDiff(
        Entity entity, 
        EntityRepository sandboxRepo, 
        ScenarioSerializer serializer,
        Action applyStepFunc);
}

public sealed class ComponentDiffService : IComponentDiffService
{
    // Implementation serializes the entity pre-step, executes applyStepFunc(), 
    // serializes post-step, and executes a recursive JsonNode diff to build the DiffNode tree.
}
```

3\. Isolated Sandbox Context (`Fdp.Presentation.Panels.Replay`)

A dedicated context object must own the lifecycle of the unmanaged memory and simulation singletons required for playback. Binding these directly to the ImGui window class violates the Single Responsibility Principle and risks leaking native buffers if the window is destroyed forcefully.

```
public sealed class ReplayBrowserContext : IDisposable
{
    public EntityRepository SandboxRepo { get; }
    public FdpEventBus SandboxBus { get; }
    public PlaybackController? Playback { get; private set; }
    public IDiagnosticEventHistoryService HistoryService { get; }

    public ReplayBrowserContext()
    {
        SandboxRepo = new EntityRepository();
        SandboxBus = new FdpEventBus();
        HistoryService = new DiagnosticEventHistoryService();
    }

    public void LoadRecording(string fdpPath)
    {
        Playback?.Dispose();
        Playback = new PlaybackController(fdpPath);
    }

    public void StepToFrame(int frameIndex)
    {
        if (Playback == null) return;
        
        SandboxBus.ClearCurrentBuffers();
        Playback.SeekToFrame(SandboxRepo, frameIndex);
        
        // Capture transient events for the EventBrowserPanel
        HistoryService.Capture("Replay", SandboxBus, (uint)frameIndex);
    }

    public void Dispose()
    {
        Playback?.Dispose();
        SandboxRepo.Dispose();
    }
}
```

4\. UI Rendering Components (`Fdp.Presentation.Panels.Replay` & `Windows`)

The presentation layer consumes the services and interfaces defined above. It utilizes the `DrawContent` methods of existing tools, supplying them with the isolated `ReplayBrowserContext`.

```
public sealed class ComponentDiffPanel
{
    /// <summary>
    /// Renders the pre-computed diff tree using ImGuiTreeNodeFlags.
    /// </summary>
    public void DrawContent(IReadOnlyList<DiffNode> diffs)
    {
        // Recursively renders DiffObject and DiffValue using syntax-colored text.
    }
}

public sealed class ReplayBrowserWindow : ManagedWindow
{
    private readonly ReplayBrowserContext _context;
    private readonly IComponentDiffService _diffService;
    private readonly IFileDialogService _fileDialogService;
    
    // Reused Editor Panels
    private readonly EntityInspectorPanel _inspectorPanel;
    private readonly EventBrowserPanel _eventPanel;
    private readonly ComponentDiffPanel _diffPanel;
    
    // UI State
    private JsonExportOptions _exportOptions = new();
    private IReadOnlyList<DiffNode> _currentDiffs = Array.Empty<DiffNode>();

    public ReplayBrowserWindow(IFileDialogService fileDialogService) 
        : base("replay_browser", "Replay Browser", string.Empty, WindowScope.Global)
    {
        _fileDialogService = fileDialogService;
        _context = new ReplayBrowserContext();
        _diffService = new ComponentDiffService();
        
        _inspectorPanel = new EntityInspectorPanel();
        _eventPanel = new EventBrowserPanel(_context.HistoryService);
        _diffPanel = new ComponentDiffPanel();

        IsOpen = false;
    }

    protected override void DrawClientArea()
    {
        DrawToolbarAndTimeline();
        
        if (ImGui.BeginTabBar("ReplayTabs"))
        {
            if (ImGui.BeginTabItem("ECS State"))
            {
                // Splitter layout
                var adapter = new RepositoryAdapter(_context.SandboxRepo);
                
                // Track selection locally so it does not conflict with the live editor
                var inspectorState = new InspectorState(); 
                
                _inspectorPanel.DrawContent(adapter, inspectorState);
                _diffPanel.DrawContent(_currentDiffs);
                
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Frame Events"))
            {
                _eventPanel.DrawContent();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private void ExecuteStep(int targetFrame)
    {
        // Inject the step closure into the DiffService to ensure atomic pre/post state capture
        var selectedEntity = GetSelectedEntity();
        
        if (selectedEntity.IsValid)
        {
            _currentDiffs = _diffService.ComputeEntityDiff(
                selectedEntity, 
                _context.SandboxRepo, 
                _inspectorPanel.Serializer, 
                () => _context.StepToFrame(targetFrame));
        }
        else
        {
            _context.StepToFrame(targetFrame);
            _currentDiffs = Array.Empty<DiffNode>();
        }
    }
    
    // Dispose overrides to tear down _context
}
```

These explicit boundaries keep the heavyweight tasks (I/O reading, memory patching, JSON traversal) entirely agnostic to the presentation constraints, relying on `EntityRepository` and `PlaybackController` abstractions. The UI classes handle only immediate-mode dispatching via `IInspectableSession` and `ManagedWindow`.

pls define suggested development stages and clear success conditions to consider them done (so that developer knows what to check before stopping the work)

Here is the phased development plan to implement the replay tooling architecture. Each stage is strictly scoped by architectural boundaries, ensuring that headless logic is fully verified before the presentation layer consumes it.

Stage 1: Shared Export Pipeline (`Fdp.Toolkits.Replay`)

This stage establishes the headless data extraction and projection pipeline, isolated from both CLI argument parsing and ImGui.

**Implementation Tasks:**

-   Define the `JsonExportOptions` state model.-   Implement `RecordingExportService` using `RecordingReader` and a headless `PlaybackController`.-   Wire the `ScenarioSerializer` and `DiagnosticEventHistoryService` to project the raw sandbox `EntityRepository` and `FdpEventBus` into the required JSON schema.-   Implement the filtering logic (frame limits, time limits, entity masking, payload scoping).

**Success Conditions:**

-   The service can be instantiated in a unit test without initializing any UI or presentation contexts.-   Providing an `.fdp` file and calling `ExportToJson` successfully generates a valid, well-formed JSON document on disk.-   Time-based windowing successfully anchors to the first frame's wall-clock ticks and extracts only the specified temporal slice.-   Delta frames emit the `DestroyedEntities` array, and components include the `HasAuthority` boolean.

Stage 2: Console Dumper Utility (`Fdp.Recorder.Cli`)

This stage exposes the export pipeline to automation and backend diagnostics.

**Implementation Tasks:**

-   Create a new .NET 8 console application project `Fdp.Recorder.Cli`.-   Implement command-line argument parsing (using `CommandLineParser` as established in the engine) mapped directly to `JsonExportOptions`.-   Invoke the `RecordingExportService`.

**Success Conditions:**

-   The tool compiles as a standalone executable decoupled from `Fdp.Presentation` or `Hrot` subsystems.-   Executing the tool via CLI with constraints (e.g., `--start-time 5.0 --end-time 10.0 --no-events --minified`) strictly respects the arguments, generating a compact payload without event data or formatting overhead.-   The tool exits with a success code `0` upon completion.

Stage 3: GUI State Tracking & Diff Engine (`Fdp.Presentation.Utils`)

This stage builds the zero-allocation state tracking required for the visual diff viewer.

**Implementation Tasks:**

-   Implement the `DiffNode` hierarchy (`DiffObject`, `DiffValue`).-   Implement `ComponentDiffService` to compute the recursive JSON tree diffs between the pre-step and post-step `JsonNode` states.

**Success Conditions:**

-   The diff computation executes successfully when a component changes, outputting a tree that contains only the modified branches.-   Unchanged components yield a `null` or empty diff tree.-   The service performs no ongoing memory allocations while the simulation timeline is paused (verifiable via profiler), isolating all allocations strictly to the frame-stepping event.

Stage 4: Graphical Replay Browser Sandbox & UI (`Fdp.Presentation.Panels.Replay`)

This stage constructs the isolated playback environment and composes the existing ImGui panels.

**Implementation Tasks:**

-   Implement `ReplayBrowserContext` to encapsulate the sandboxed `EntityRepository`, `FdpEventBus`, and `DiagnosticEventHistoryService`.-   Implement `ComponentDiffPanel` to render the `DiffNode` tree with syntax-colored text.-   Implement `ReplayBrowserWindow` inheriting from `ManagedWindow` with `WindowScope.Global`.-   Integrate `EntityInspectorPanel`, `EventBrowserPanel`, the custom diff panel, and the transport/timeline controls.-   Bind the "Save as JSON" expander to execute `RecordingExportService` on a background `Task`.

**Success Conditions:**

-   Loading an `.fdp` file successfully populates the timeline slider with the correct frame count and timestamp metadata.-   Scrubbing the timeline instantly updates the reused `EntityInspectorPanel` and `EventBrowserPanel` without leaking data to the main editor's live `EntityRepository`.-   The `ComponentDiffPanel` highlights exact field changes (e.g., `Position.X`) when stepping forward through delta frames.-   Triggering a JSON export via the UI successfully writes the file to disk without stalling or dropping the ImGui render loop frame rate.

Stage 5: Global Editor Integration

This stage exposes the tool to all module perspectives.

**Implementation Tasks:**

-   Instantiate and register the `ReplayBrowserWindow` into the `WindowManager` inside `LocalWindowController.OpenLocalWindow()`.

**Success Conditions:**

-   The Replay Browser appears under the "Help -> Debug" or equivalent global debug menu.-   The window can be opened and operated flawlessly regardless of whether the cluster runner was launched in `-m simhost`, `-m ig`, or `-m excon` mode.

good! now pls put on a user's hat and look at the tools from different perpectives and find the gaps, flaws and usefull extensions to provide the best value and quick diagnostic capabilities for the user.

When evaluating the current design from a user's perspective, the data extraction and isolation architecture is structurally sound, but it lacks the connective tissue required for rapid, intuitive diagnostics. A developer debugging a recording does not want to read raw JSON; they want to observe causality.

Here are the critical gaps, flaws, and necessary extensions to maximize the tool's value.

1\. Architectural Flaws & UX Gaps

**Gap: Lack of Spatial/Visual Context** The current design relies entirely on ImGui property trees. However, the simulation state is intensely spatial, driven by components like `SimTransform` and `SimVelocity`. Diagnosing navigation or physics issues by reading `[X, Y, Z]` vectors in a JSON tree is highly inefficient.

-   **Solution:** The `ReplayBrowserWindow` must include an embedded, read-only `MapCanvas`. The engine already uses `MapCanvas` and `DebugGizmoLayer` for visual diagnostics. By binding a dedicated canvas to the isolated `ReplayBrowserContext`, the user can visually scrub the timeline and see entities move, rendering the exact same gizmos (e.g., `SelectionHighlightGizmo` or `VisibilityConeGizmo`) they use in live mode.

**Flaw: Diff Noise from Continuous Mutations** Because the engine simulates physics and kinematics, components like `SimTransform` mutate by microscopic amounts on every single tick. A strict string-based JSON diff will flag these as modifications every frame, flooding the `ComponentDiffPanel` with floating-point noise and burying actual logical bugs (like a state machine transition).

-   **Solution:** The `ComponentDiffService` must be extended to support an `EpsilonTolerance` setting. When comparing two `JsonNode` values of `JsonValueKind.Number`, the service should parse them as `double` and ignore the diff if `Math.Abs(old - new) < EpsilonTolerance`. This feature should be exposed as a slider in the UI.

**Gap: Navigational Friction (Dead Entity Handles)** The `DiagnosticGuidResolver` produces clean, human-readable strings like `[42, v3]` for entity references inside component and event payloads. In the current design, these are rendered as plain text. If an operator sees an event indicating `Target: [42, v3]`, they must manually type "42" into the Entity Inspector's search bar to investigate it.

-   **Solution:** Implement deep linking. In both the `ComponentDiffPanel` and the `EventBrowserPanel`, use a regex to detect the `[Index, vGeneration]` pattern during rendering. Replace the plain text with an `ImGuiApi.SmallButton`. Clicking it must instantly assign that entity to the `InspectorState.SelectedEntity` and focus the `EntityInspectorPanel`.

2\. High-Value Extensions

**Extension: Event-to-State Causality Actions** The engine's `FdpEventBus` uses a strict double-buffered architecture: events published in Frame N are consumed and acted upon in Frame N+1. When a developer locates a critical event (e.g., `DamageAssessedEvent`) in the Event Browser, their immediate next step is always to check the target's ECS state in the subsequent frame to verify the health reduction.

-   **Solution:** Add a right-click context menu to the `EventBrowserPanel` rows with an action: _"Step Forward and Diff Target"_. This macro will extract the entity handle from the event payload, advance the timeline by one frame, select the target entity, and immediately render its state mutations in the `ComponentDiffPanel`.

**Extension: Changelog Export Mode** The console utility currently dumps absolute frame states. If a developer is trying to find the exact moment an AI state corrupted within a 10-minute recording, searching through a multi-gigabyte JSON file of absolute states is practically impossible.

-   **Solution:** Extend the `RecordingExportService` and the `JsonExportOptions` with an `ExportMode.Changelog`. Instead of serializing the full `EntityRepository` per frame, the exporter will leverage the headless `ComponentDiffService` logic. It will output a compact time-series array containing _only_ the specific fields that mutated on the targeted entity, alongside the timestamp and tick. This shrinks a multi-gigabyte diagnostic task into a few kilobytes of actionable data.

these are excellent findings! let's ps detail them one by one. let's start with Gap: Lack of Spatial/Visual Context . Our map is raylib driven so it is not embeddable inside the replay browser imgui panel; it must share the one and only raylib window we have. Maybe we could somehow extend the perspective switcher of the clusterrunner to allow switching to the perspective of this replay browser?

Elevating the replay tool to participate in the perspective switcher is the correct architectural choice given Raylib's global rendering context. You cannot trivially embed a Raylib rendering viewport inside an ImGui window without relying on rendering to intermediate framebuffers, which introduces unnecessary complexity.

Instead of treating the Replay Browser as a global `ManagedWindow`, you must construct it as a first-class `ISubsystem` within the `ClusterRunner` architecture. This allows it to own a dedicated `MapCanvas` and natively hook into the engine's existing orchestration loop and perspective routing.

Here is the architectural implementation to achieve this.

1\. Elevate to `ReplaySubsystem`

Create a `ReplaySubsystem` class that implements `ISubsystem`, `IWindowRegistrar`, and `IMapCameraProvider`.

Rather than encapsulating the sandbox state in a UI window, the `ReplaySubsystem` owns the `ReplayBrowserContext` (which holds the isolated `EntityRepository` and `PlaybackController`) and a dedicated `MapCanvas`.

During its `Update(float deltaTime)` tick, the subsystem steps the timeline (if playing) and updates its isolated canvas. During `DrawWorld()`, it simply calls `_canvas.Draw()`.

2\. Integration with `SubsystemOrchestrator`

The `ClusterRunner` utilizes `SubsystemOrchestrator` to manage map ownership. By adding `ReplaySubsystem` to the runner's subsystem list, the perspective switcher handles routing automatically.

When the user selects the "Replay" perspective from the top menu bar, the `PerspectiveCoordinatorSystem` enqueues a `TogglePerspectiveEvent`. The orchestrator calls `SwitchMapOwner("Replay")`, meaning `DrawWorldAll()` will now only invoke the rendering pipeline for your isolated replay canvas, successfully sharing the single Raylib window without cross-contamination.

3\. Spatial Continuity via `IMapCameraProvider`

To prevent the operator from losing their spatial context when switching from the live simulation into the replay viewer, the `ReplaySubsystem` must implement `IMapCameraProvider`.

When the perspective switches, the `SubsystemOrchestrator` extracts the `MapCameraView` from the outgoing perspective (e.g., SimHost or IG) and injects it into the incoming perspective via `ApplyCameraView`. By implementing this, your replay canvas will instantly snap to the exact coordinates and zoom level the operator was looking at in the live mode, providing seamless visual continuity.

4\. Reusing Visual Layers

Because the isolated state is held in standard engine abstractions (`EntityRepository`, `FdpEventBus`), you can populate the replay `MapCanvas` using the exact same rendering layers used by the live subsystems.

-   Add a `DebugGizmoLayer` bound to the sandbox repository and bus. This guarantees that diagnostic primitives (like `SimTransform` anchors, LOS cones, or trajectory paths) render identically during replay as they did live.-   Add your `GridMapLayer` or `SimHostRoadLayer` so spatial references exist.

5\. UI Scoping

The ImGui UI components we previously discussed (Timeline transport, Entity Inspector, Event Browser, Diff Viewer) are no longer bound to a `WindowScope.Global` window.

Instead, the `ReplaySubsystem` uses `IWindowRegistrar` to register these individual panels with the `WindowManager` using `WindowScope.PerspectiveBound` and mapping them to the "Replay" perspective. This ensures the replay UI completely replaces the live authoring UI when the perspective is switched, keeping the screen layout focused strictly on diagnostic tasks.

The replay browser subsystem must be logically completely isolated from any other subsystem, meaning there is no need to synchronize the camera location with other subsystems as well as not to affect any other subsystem in any other way. Having a separate subsystem (lets name it "ReplayBrowser") meaning we no longer need to put the window into the debug menu of other subsystems. As you say, the event browser and event inspector and the replay seek panel and the frame diff viewer will be now separate windows for the ReplayBrowser subsystems. Much better reusing of the concepts! As a subsystem, it needs to be specifiable on the clusterrunner command line.

To achieve strict architectural isolation, elevating the replay tool to a standalone `ReplayBrowserSubsystem` is the correct approach. By decoupling it from the live simulation and omitting the `IMapCameraProvider` interface, you guarantee complete spatial and temporal isolation from the rest of the cluster.

Here is the architectural blueprint for the isolated subsystem, the command-line integration, and the composition of its perspective-bound UI windows.

1\. Subsystem Definition and CLI Integration

To make the subsystem accessible via the `ClusterRunner` command line (e.g., `-m replaybrowser`), you simply name the class `ReplayBrowserSubsystem` and implement `ISubsystem`. The engine's `ScanForSubsystems` method uses reflection to discover all non-abstract `ISubsystem` implementations, automatically stripping the "Subsystem" suffix to map the CLI argument to the class.

By intentionally omitting `IMapCameraProvider`, the `SubsystemOrchestrator` will not attempt to synchronize this subsystem's camera with `SimHost` or `IG`.

```
public sealed class ReplayBrowserSubsystem : ISubsystem, IWindowRegistrar
{
    public string Name => "ReplayBrowser";
    public Vector4 TitleBarColor => new Vector4(0.2f, 0.6f, 0.8f, 1f); // Distinct blue

    private ReplayBrowserContext _context = null!;
    private MapCanvas _canvas = null!;
    private bool _headless;

    // UI Panels
    private ReplayTimelinePanel _timelinePanel = null!;
    private ComponentDiffPanel _diffPanel = null!;
    private EntityInspectorPanel _inspectorPanel = null!;
    private EventBrowserPanel _eventPanel = null!;

    public void Initialize(SubsystemConfig config)
    {
        _headless = config.Headless;
        _context = new ReplayBrowserContext();
        
        // Fully isolated spatial canvas
        if (!_headless)
        {
            _canvas = new MapCanvas();
            _canvas.Camera.Offset = new Vector2(1280 / 2f, 720 / 2f);
            
            // Initialize standalone panels
            _timelinePanel = new ReplayTimelinePanel(_context);
            _diffPanel = new ComponentDiffPanel(_context.DiffService);
            _inspectorPanel = new EntityInspectorPanel();
            _eventPanel = new EventBrowserPanel(_context.HistoryService);
        }
    }

    public void Update(float deltaTime)
    {
        if (_headless) return;
        _canvas.Update(deltaTime);
        // Step logic driven by TimelinePanel interactions
    }

    public void DrawWorld()
    {
        if (!_headless) _canvas.Draw();
    }

    public void DrawUI() 
    { 
        // ManagedWindows are rendered automatically by the WindowManager.
        // Render only popups or overlays here.
    }

    public void Shutdown()
    {
        _context?.Dispose();
    }
```

2\. Window Registration and Perspective Binding

Instead of nesting these tools into a single global layout or forcing them into the debug menus of other modules, they are registered as independent `ManagedWindow` instances bound exclusively to the `ReplayBrowser` perspective.

Implement `IWindowRegistrar.RegisterWindows` within the subsystem. Use `WindowScope.PerspectiveBound` so these windows appear automatically when the operator switches to the ReplayBrowser view and hide themselves when switching away.

    ```
    public void RegisterWindows(WindowManager windowManager)
    {
        if (_headless) return;
    
        // 1. Timeline & Transport Control Window
        windowManager.RegisterWindow(new ReplayTimelineWindow(
            "replay_timeline", "Replay Timeline", Name, _timelinePanel, TitleBarColor));
    
        // 2. Reused Entity Inspector
        var adapterGetter = () => new RepositoryAdapter(_context.SandboxRepo);
        var stateGetter = () => new InspectorState();
        windowManager.RegisterWindow(new FdpEntityInspectorWindow(
            "replay_inspector", "Replay Entity Inspector", Name, 
            _inspectorPanel, adapterGetter, stateGetter, TitleBarColor));
    
        // 3. Component Diff Viewer
        windowManager.RegisterWindow(new ComponentDiffWindow(
            "replay_diff", "Frame Diff Viewer", Name, _diffPanel, TitleBarColor));
    
        // 4. Reused Event Browser
        windowManager.RegisterWindow(new FdpEventBrowserWindow(
            "replay_events", "Replay Event Browser", Name, _eventPanel, TitleBarColor));
    }
}
```

3\. Window Abstractions

Each window wraps its respective panel and enforces the `PerspectiveBound` scope. Reusing the engine's `ManagedWindow` base class handles all docking and lifecycle mechanics.

```
internal sealed class ComponentDiffWindow : ManagedWindow
{
    private readonly ComponentDiffPanel _panel;

    public ComponentDiffWindow(string id, string title, string owningPerspective, ComponentDiffPanel panel, Vector4 titleBarColor)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        TitleBarColor = titleBarColor;
        IsOpen = true;
    }
    
    protected override void DrawClientArea() => _panel.DrawContent();
}

internal sealed class ReplayTimelineWindow : ManagedWindow
{
    private readonly ReplayTimelinePanel _panel;

    public ReplayTimelineWindow(string id, string title, string owningPerspective, ReplayTimelinePanel panel, Vector4 titleBarColor)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        TitleBarColor = titleBarColor;
        IsOpen = true;
    }
    
    protected override void DrawClientArea() => _panel.DrawContent();
}
```

Summary of Architectural Benefits

-   **Separation of Concerns:** `ReplayTimelinePanel` handles transport logic and JSON export configuration, leaving the `ComponentDiffPanel` strictly focused on rendering state mutations.-   **Layout Freedom:** Because they are individual `ManagedWindow` instances, the operator can leverage ImGui's docking system to arrange the Timeline, Diff Viewer, and Event Inspector around the isolated `MapCanvas` however they prefer.-   **Execution Safety:** The `ReplayBrowserContext` guarantees that replaying delta frames or performing JSON extraction tasks modifies only the sandboxed `EntityRepository` and `FdpEventBus`, ensuring zero corruption of the live cluster's memory or network state.

pls suggest the layouts of the new windows to be created

The layout must leverage the engine's existing `PassthruCentralNode` dockspace, allowing the Raylib `MapCanvas` to act as the visual backdrop while the diagnostic windows are docked around the perimeter.

Here is the optimal spatial arrangement for the `ReplayBrowser` perspective:

```
+-----------------------------------------------------------------------+
| Main Menu Bar (Perspective Switcher, Global Tools)                    |
+-------------------+-----------------------------------+---------------+
| LEFT DOCK         | CENTRAL DOCK (Passthru)           | RIGHT DOCK    |
|                   |                                   |               |
| [Replay Entity    |                                   | [Frame Diff   |
|  Inspector]       |                                   |  Viewer]      |
|                   |        Raylib MapCanvas           |               |
| - Entity Search   |        (Isolated Sandbox)         | - Hierarchical|
| - Entity List     |                                   |   Change Tree |
| - Component       |                                   |               |
|   Reflector Tree  |                                   |---------------+
|                   |                                   | [Replay Event |
|                   |                                   |  Browser]     |
|                   |                                   |               |
|                   |                                   | - Event List  |
|                   |                                   | - Payload     |
+-------------------+-----------------------------------+---------------+
| BOTTOM DOCK                                                           |
| [Replay Timeline]                                                     |
| [|<] [<] [||] [>] [>|]  Timeline: [========O-----------------------]  |
| Meta: Tick 4567 | SimFrame 1200 | SimTime 20.55s | Delta | 1024 Bytes |
| > JSON Export Options (Expander)                                      |
+-----------------------------------------------------------------------+
| Global Status Bar                                                     |
+-----------------------------------------------------------------------+
```

Docking Assignments & Workflow Justification

**1\. Central Dock (Passthru): The Map Canvas** The center of the screen must remain unobstructed. Because the orchestrator's presentation shell initializes the `DockSpace` with `ImGuiDockNodeFlags.PassthruCentralNode`, the `MapCanvas` drawn during `ReplayBrowserSubsystem.DrawWorld()` will seamlessly appear behind the ImGui layer. This provides immediate spatial context as the user steps through frames.

**2\. Bottom Dock:** **ReplayTimelineWindow**

-   **Layout:** Full screen width.-   **Justification:** A multi-megabyte `.fdp` file contains tens of thousands of frames. A timeline slider requires maximum horizontal screen real estate to provide granular scrubbing precision.-   **Contents:** Transport controls (Play, Step, Rewind), the timeline slider, frame metadata, and the collapsible `JSON Export Options` tree.

**3\. Left Dock:** **FdpEntityInspectorWindow**

-   **Layout:** Vertical split, ~30% of screen width.-   **Justification:** The `EntityInspectorPanel` requires significant vertical space to display the full list of active entities and the expanded `ComponentReflector` property trees. Docking it on the left follows the established engine standard for entity hierarchies.

**4\. Right Dock (Top Half):** **ComponentDiffWindow**

-   **Layout:** Right side, upper split.-   **Justification:** By placing the diff viewer opposite the Entity Inspector, the developer can select an entity on the left, step the timeline at the bottom, and immediately observe the isolated state mutations on the right without scrolling.

**5\. Right Dock (Bottom Half):** **FdpEventBrowserWindow**

-   **Layout:** Right side, lower split.-   **Justification:** Transient events (like `DamageEvent` or `WeaponFireNotification`) are fleeting. Placing the event browser directly below the state diff viewer allows the operator to instantly correlate cause and effect: an event appears in the bottom right, and the resulting component mutation (e.g., health reduction) highlights in the diff viewer above it.

Because you are using `ManagedWindow` instances registered with `WindowScope.PerspectiveBound`, ImGui's internal `.ini` state will automatically save and recall this exact docking layout specific to the `ReplayBrowser` perspective without polluting the layout of the `SimHost` or `Editor` modes.

sure, the docking is availoable aout of the box if these dockable parts are implemented as separate window managed by the windows manager (which thay should be - entity inspector, event broser, frame diff viewer, replay timeline). I am interested in the visual layout of the frame diff viewer, please present

Here is the visual layout for the Frame Diff Viewer. To maintain aesthetic consistency with the engine's live-mode inspectors, this panel must mirror the 2-column structure of `ImGuiPropertyTree`, keeping the hierarchical property names strictly in the left column and the value transitions in the right column.

Visual Wireframe

```
[Frame Diff Viewer]
+-----------------------------------------------------------------------+
| Options: [ ] Ignore Epsilon (< 0.001)   [ ] Hide Unchanged Components |
+-----------------------------------------------------------------------+
| Property                        | Value Transition                    |
|---------------------------------|-------------------------------------|
| ▼ SimTransform                  |                                     |
|   ▼ Position                    |                                     |
|       X                         |  12.345  ->  12.350                 |
|       Z                         |   0.000  ->   0.050                 |
|   ▼ Rotation                    |                                     |
|       W                         |   1.000  ->   0.999                 |
| ▼ IgHealthState                 |                                     |
|       Damage                    |   0.0    ->  15.5                   |
| ▼ TargetMemory                  |                                     |
|   ▼ PositionsX                  |                                     |
|                              | 100.250  -> 101.000                 |
+-----------------------------------------------------------------------+
```

Layout Architecture & ImGui Implementation

To achieve this layout cleanly without breaking the immediate-mode GUI flow, the `ComponentDiffPanel` must utilize `ImGuiApi.BeginTable` with the exact flags used by `ImGuiPropertyTree`. The hierarchical tree nodes are rendered in Column 0, and the `[Old] -> [New]` strings are rendered in Column 1.

Here is the implementation of the layout structure:

```
public sealed class ComponentDiffPanel
{
    private bool _ignoreEpsilon = true;
    private bool _hideUnchanged = true;

    public void DrawContent(IReadOnlyList<DiffNode> diffs)
    {
        // Toolbar
        ImGuiApi.Checkbox("Ignore Epsilon (< 0.001)", ref _ignoreEpsilon);
        ImGuiApi.SameLine();
        ImGuiApi.Checkbox("Hide Unchanged Components", ref _hideUnchanged);
        ImGuiApi.Separator();
    
        if (diffs.Count == 0)
        {
            ImGuiApi.TextDisabled("No component modifications detected in this frame.");
            return;
        }
    
        // Use the exact table flags established by Fdp.Presentation.Utils.ImGuiPropertyTree
        ImGuiTableFlags tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | 
                                     ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit;
    
        if (ImGuiApi.BeginTable("DiffViewerTable", 2, tableFlags))
        {
            ImGuiApi.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 200f);
            ImGuiApi.TableSetupColumn("Value Transition", ImGuiTableColumnFlags.WidthStretch);
            ImGuiApi.TableHeadersRow();
    
            foreach (var rootNode in diffs)
            {
                DrawDiffNode(rootNode);
            }
    
            ImGuiApi.EndTable();
        }
    }
    
    private void DrawDiffNode(DiffNode node)
    {
        ImGuiApi.TableNextRow();
        ImGuiApi.TableSetColumnIndex(0);
    
        if (node is DiffObject group)
        {
            // Internal nodes (structs, classes, arrays)
            bool isOpen = ImGuiApi.TreeNodeEx(
                $"{group.Name}##{group.GetHashCode()}", 
                ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth);
    
            if (isOpen)
            {
                foreach (var child in group.Children)
                {
                    DrawDiffNode(child);
                }
                ImGuiApi.TreePop();
            }
        }
        else if (node is DiffValue val)
        {
            // Leaf nodes align perfectly with the tree depth but do not push indent
            ImGuiApi.TreeNodeEx(
                $"{val.Name}##{val.GetHashCode()}", 
                ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth);
    
            // Move to the second column to render the values
            ImGuiApi.TableSetColumnIndex(1);
    
            // Deep-link entity references if detected
            if (val.OldValue.StartsWith("[") && val.OldValue.EndsWith("]") && val.OldValue.Contains(","))
            {
                RenderEntityDeepLink(val.OldValue);
                ImGuiApi.SameLine();
                ImGuiApi.TextUnformatted(" -> ");
                ImGuiApi.SameLine();
                RenderEntityDeepLink(val.NewValue);
            }
            else
            {
                // Standard syntax-colored transition
                ImGuiApi.TextDisabled(val.OldValue);
                ImGuiApi.SameLine();
                ImGuiApi.TextUnformatted(" -> ");
                ImGuiApi.SameLine();
                RenderSyntaxColoredValue(val.NewValue, val.ValueType);
            }
        }
    }
    
    private void RenderSyntaxColoredValue(string value, JsonValueKind kind)
    {
        Vector4 color = kind switch
        {
            JsonValueKind.Number => new Vector4(0.30f, 0.80f, 1.00f, 1f), // Cyan
            JsonValueKind.String => new Vector4(0.40f, 1.00f, 0.40f, 1f), // Green
            JsonValueKind.True   => new Vector4(0.90f, 0.60f, 0.20f, 1f), // Amber
            JsonValueKind.False  => new Vector4(0.90f, 0.60f, 0.20f, 1f), // Amber
            _                    => new Vector4(0.85f, 0.85f, 0.85f, 1f)  // Light Gray
        };
    
        ImGuiApi.TextColored(color, value);
    }
    
    private void RenderEntityDeepLink(string entityHandle)
    {
        // Render entity handles as clickable buttons that change the global selection state
        ImGuiApi.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.45f, 0.8f, 1f)); // ExConViolet
        if (ImGuiApi.SmallButton(entityHandle))
        {
            // Raise event/callback to set InspectorState.SelectedEntity
        }
        ImGuiApi.PopStyleColor();
    }
}
```

Key Design Behaviors

-   **Column Alignment**: Using `ImGuiApi.BeginTable` keeps the vertical alignment of the value transitions flawless, regardless of how deeply nested the component hierarchy goes.-   **Indentation Control**: For leaf properties (like `X`, `Y`, `Damage`), `ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen` ensures the item takes up the correct horizontal indentation slot without actually opening a new structural level, preventing layout breakages.-   **Interactive Spans**: The `ImGuiTreeNodeFlags.SpanAvailWidth` flag allows the user to click anywhere on the row to expand/collapse the nested groups, rather than having to hit the small triangle directly.

cool! the Hide unchanged option should be checked by default and should include not just the components but also the fields in teh components (as you correctly showed in the layout).

To enforce the "Hide Unchanged" behavior by default while ensuring the toggle applies instantaneously to both root components and their deeply nested fields, we must refine the separation between data extraction and UI rendering.

If the user toggles the checkbox, forcing the `ComponentDiffService` to re-parse the multi-megabyte JSON trees to include or exclude unchanged fields would cause severe main-thread stalling. The correct architecture requires the extraction service to always build the complete structural tree for any mutated component, flagging the modification state on each node. The ImGui loop then performs an O(N) culling pass during rendering.

Here is the architectural implementation for this design.

1\. Extensible Diff Data Model

Expand the `DiffNode` abstraction to carry a boolean `IsModified` flag. This allows internal nodes (`DiffObject`) to propagate their mutated state upwards from the leaf values.

```
public abstract class DiffNode
{
    public string Name { get; }
    public bool IsModified { get; protected set; }
    protected DiffNode(string name) => Name = name;
}

public sealed class DiffObject : DiffNode
{
    public List<DiffNode> Children { get; } = new();
    public DiffObject(string name) : base(name) { }

    public void EvaluateModificationState()
    {
        // An object is considered modified if any of its descendants are modified
        IsModified = Children.Exists(c => c.IsModified);
    }
}

public sealed class DiffValue : DiffNode
{
    public string OldValue { get; }
    public string NewValue { get; }
    public System.Text.Json.JsonValueKind ValueType { get; }

    public DiffValue(string name, string oldValue, string newValue, System.Text.Json.JsonValueKind valueType, bool isModified) 
        : base(name)
    {
        OldValue = oldValue;
        NewValue = newValue;
        ValueType = valueType;
        IsModified = isModified;
    }
}
```

2\. State Extraction Algorithm

The `ComponentDiffService` must now emit all fields but explicitly calculate the `IsModified` flag based on your specified epsilon tolerance.

```
public DiffNode ComputeDiff(string name, JsonNode? oldNode, JsonNode? newNode, double epsilonTolerance)
{
    if (oldNode is JsonObject oldObj && newNode is JsonObject newObj)
    {
        var group = new DiffObject(name);
        var allKeys = oldObj.Select(k => k.Key).Union(newObj.Select(k => k.Key)).Distinct();
        
        foreach (var key in allKeys)
        {
            var childDiff = ComputeDiff(key, oldObj[key], newObj[key], epsilonTolerance);
            if (childDiff != null)
            {
                group.Children.Add(childDiff);
            }
        }
        
        group.EvaluateModificationState();
        return group;
    }
    
    // Leaf comparison
    string oldStr = oldNode?.ToJsonString() ?? "null";
    string newStr = newNode?.ToJsonString() ?? "null";
    JsonValueKind kind = newNode?.GetValueKind() ?? JsonValueKind.Null;
    
    bool isModified = oldStr != newStr;
    
    // Apply Epsilon tolerance for floating-point noise
    if (isModified && kind == JsonValueKind.Number)
    {
        if (double.TryParse(oldStr, out double oldVal) && double.TryParse(newStr, out double newVal))
        {
            isModified = Math.Abs(oldVal - newVal) >= epsilonTolerance;
        }
    }
    
    return new DiffValue(name, oldStr, newStr, kind, isModified);
}
```

3\. Immediate-Mode Culling (The UI Layer)

In `ComponentDiffPanel`, set the default state to `true`. During the `DrawDiffNode` traversal, inject a guard clause that instantly prunes branches where `IsModified` evaluates to false if the toggle is active.

```
public sealed class ComponentDiffPanel
{
    // Checked by default to suppress noise
    private bool _hideUnchanged = true; 
    private bool _ignoreEpsilon = true;

    public void DrawContent(IReadOnlyList<DiffNode> diffs)
    {
        ImGuiApi.Checkbox("Ignore Epsilon (< 0.001)", ref _ignoreEpsilon);
        ImGuiApi.SameLine();
        ImGuiApi.Checkbox("Hide Unchanged Components & Fields", ref _hideUnchanged);
        ImGuiApi.Separator();
    
        if (diffs.Count == 0)
        {
            ImGuiApi.TextDisabled("No component modifications detected in this frame.");
            return;
        }
    
        ImGuiTableFlags tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | 
                                     ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit;
    
        if (ImGuiApi.BeginTable("DiffViewerTable", 2, tableFlags))
        {
            ImGuiApi.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 200f);
            ImGuiApi.TableSetupColumn("Value Transition", ImGuiTableColumnFlags.WidthStretch);
            ImGuiApi.TableHeadersRow();
    
            foreach (var rootNode in diffs)
            {
                DrawDiffNode(rootNode);
            }
    
            ImGuiApi.EndTable();
        }
    }
    
    private void DrawDiffNode(DiffNode node)
    {
        // ARCHITECTURAL GUARD: Zero-allocation instant culling of unchanged branches
        if (_hideUnchanged && !node.IsModified)
            return;
    
        ImGuiApi.TableNextRow();
        ImGuiApi.TableSetColumnIndex(0);
    
        if (node is DiffObject group)
        {
            bool isOpen = ImGuiApi.TreeNodeEx(
                $"{group.Name}##{group.GetHashCode()}", 
                ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth);
    
            if (isOpen)
            {
                foreach (var child in group.Children)
                {
                    DrawDiffNode(child);
                }
                ImGuiApi.TreePop();
            }
        }
        else if (node is DiffValue val)
        {
            ImGuiApi.TreeNodeEx(
                $"{val.Name}##{val.GetHashCode()}", 
                ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth);
    
            ImGuiApi.TableSetColumnIndex(1);
    
            if (!val.IsModified)
            {
                // Render unchanged values as dimmed plain text
                ImGuiApi.TextDisabled(val.OldValue);
            }
            else
            {
                // Render transitions with syntax coloring
                ImGuiApi.TextDisabled(val.OldValue);
                ImGuiApi.SameLine();
                ImGuiApi.TextUnformatted(" -> ");
                ImGuiApi.SameLine();
                RenderSyntaxColoredValue(val.NewValue, val.ValueType);
            }
        }
    }
    
    private void RenderSyntaxColoredValue(string value, JsonValueKind kind)
    {
        // Reused syntax coloring logic...
    }
}
```

This guarantees that untoggling "Hide Unchanged" immediately displays the full structural hierarchy of any component that mutated during the frame, while keeping the UI render loop strictly decoupled from the heavy string parsing workload.

now the **Gap: Navigational Friction (Dead Entity Handles)**. The deep linking is excellent idea. It should be a generic feature of those windows. When the user presses the clickable entity id, the window should fire some action event (call some inhected delegate) to allow external control of what happens. The simple response (defined outside) should be to focus the window to the clicked entity. But it needs to support the back/forth navigation. It needs to keep the history and present 2 icon buttons to navigate (again by invoking the delegate, in a way that navigating in the history does not create new records in the history)

To achieve generic deep-linking with a robust navigation history, you must decouple the history state machine from the ImGui presentation layer. The UI panels should remain strictly stateless regarding navigation, operating entirely via injected delegates.

Here is the architectural implementation.

1\. Navigation History State Machine

Create a dedicated `EntitySelectionHistory` service. This class manages the back/forward stacks and enforces the rule that programmatic navigation (clicking Back/Forward) suspends history recording to prevent infinite loops and duplicate records.

```
public sealed class EntitySelectionHistory
{
    private readonly List<Entity> _history = new();
    private int _currentIndex = -1;
    
    // Mutex flag to prevent history pollution during Back/Forward operations
    private bool _isNavigating;
    
    /// <summary>
    /// Event fired when the selection changes.
    /// The UI must bind to this to update the active InspectorState.
    /// </summary>
    public event Action<Entity>? OnSelectionChanged;
    
    public bool CanGoBack => _currentIndex > 0;
    public bool CanGoForward => _currentIndex < _history.Count - 1;
    
    /// <summary>
    /// Invoked by the UI panels when an entity deep link is explicitly clicked by the user.
    /// </summary>
    public void PushSelection(Entity entity)
    {
        if (_isNavigating || !entity.IsValid) return;
    
        // Prevent pushing a duplicate if the user clicks the currently selected entity
        if (_currentIndex >= 0 && _history[_currentIndex] == entity) return;
    
        // If we navigated back and then clicked a new link, truncate the forward history
        if (_currentIndex < _history.Count - 1)
        {
            _history.RemoveRange(_currentIndex + 1, _history.Count - (_currentIndex + 1));
        }
    
        _history.Add(entity);
        _currentIndex++;
        
        OnSelectionChanged?.Invoke(entity);
    }
    
    public void GoBack()
    {
        if (!CanGoBack) return;
        
        _isNavigating = true;
        _currentIndex--;
        OnSelectionChanged?.Invoke(_history[_currentIndex]);
        _isNavigating = false;
    }
    
    public void GoForward()
    {
        if (!CanGoForward) return;
        
        _isNavigating = true;
        _currentIndex++;
        OnSelectionChanged?.Invoke(_history[_currentIndex]);
        _isNavigating = false;
    }
}
```

2\. Deep Link Rendering Utility

Centralize the rendering of the clickable entity handle. The engine standardizes on `[Index, vGeneration]` for entity handles and uses the `ExConViolet` color to denote resolvable network/entity IDs.

```
public static class ImGuiEntityLink
{
    private static readonly Vector4 LinkColor = new Vector4(0.7f, 0.45f, 0.8f, 1f); // ExConViolet

    /// <summary>
    /// Renders an entity deep link. Returns true if clicked.
    /// </summary>
    public static bool Draw(string label)
    {
        ImGuiApi.PushStyleColor(ImGuiCol.Text, LinkColor);
        
        // SmallButton removes the standard button padding, making it flow like inline text
        bool clicked = ImGuiApi.SmallButton(label);
        
        ImGuiApi.PopStyleColor();
        return clicked;
    }
    
    /// <summary>
    /// Attempts to parse an entity from a diagnostic string formatted as "[Index, vGeneration]".
    /// </summary>
    public static bool TryParse(string text, out Entity entity)
    {
        entity = Entity.Null;
        if (!text.StartsWith("[") || !text.EndsWith("]")) return false;
    
        var inner = text.AsSpan(1, text.Length - 2);
        int commaIdx = inner.IndexOf(',');
        if (commaIdx == -1) return false;
    
        var indexSpan = inner.Slice(0, commaIdx).Trim();
        var genSpan = inner.Slice(commaIdx + 1).Trim();
        
        if (genSpan.StartsWith("v") || genSpan.StartsWith("V"))
            genSpan = genSpan.Slice(1);
    
        if (int.TryParse(indexSpan, out int index) && ushort.TryParse(genSpan, out ushort gen))
        {
            entity = new Entity(index, gen);
            return true;
        }
    
        return false;
    }
}
```

3\. Injecting Delegates into the UI Panels

Update the constructors or `DrawContent` methods of your panels (`ComponentDiffPanel`, `EventBrowserPanel`) to accept the `Action<Entity> onEntityLinkClicked` delegate.

Inside the `ComponentDiffPanel` rendering loop:

```
// Inside ComponentDiffPanel.DrawDiffNode:
if (ImGuiEntityLink.TryParse(val.NewValue, out Entity linkedEntity))
{
    if (ImGuiEntityLink.Draw(val.NewValue))
    {
        _onEntityLinkClicked?.Invoke(linkedEntity);
    }
}
else
{
    RenderSyntaxColoredValue(val.NewValue, val.ValueType);
}
```

4\. Navigation Toolbar Integration

Add the Back/Forward navigation controls directly to the `EntityInspectorPanel` header, ensuring they immediately precede the entity search/list controls.

```
public void DrawToolbar(EntitySelectionHistory history)
{
    // Back Button
    if (!history.CanGoBack) ImGuiApi.BeginDisabled();
    if (ImGuiApi.ArrowButton("##back_btn", ImGuiDir.Left))
    {
        history.GoBack();
    }
    if (!history.CanGoBack) ImGuiApi.EndDisabled();

    ImGuiApi.SameLine();
    
    // Forward Button
    if (!history.CanGoForward) ImGuiApi.BeginDisabled();
    if (ImGuiApi.ArrowButton("##fwd_btn", ImGuiDir.Right))
    {
        history.GoForward();
    }
    if (!history.CanGoForward) ImGuiApi.EndDisabled();
    
    ImGuiApi.SameLine();
    ImGuiApi.TextDisabled("History");
    ImGuiApi.Separator();
}
```

5\. Composition Root Wiring

In your `ReplayBrowserSubsystem.Initialize()` method, instantiate the `EntitySelectionHistory` and wire the delegates. This strict unidirectional data flow ensures the panels never manipulate selection state directly.

```
_history = new EntitySelectionHistory();

// 1. When the history state machine dictates a selection change, update the UI context
_history.OnSelectionChanged += (entity) => 
{
    _context.InspectorState.SelectedEntity = entity;
    
    // Optional: Auto-seek to frame if the entity didn't exist at the current timeline position
    if (!_context.SandboxRepo.IsAlive(entity)) 
    {
        // Execute diagnostic log warning or fast-forward to creation frame
    }
};

// 2. Inject the PushSelection delegate into all deep-link capable panels
Action<Entity> onLinkClick = _history.PushSelection;

_diffPanel = new ComponentDiffPanel(_context.DiffService, onLinkClick);
_eventPanel = new EventBrowserPanel(_context.HistoryService, onLinkClick);

// 3. For the Entity Inspector's manual list selections, also push to history
_inspectorPanel.OnEntitySelected = onLinkClick;
```

ad **Extension: Event-to-State Causality Actions**. This is also a good idea. We need to add some kind of history navigation here as well because once the user clicks to follow the causality, a next frame will be shown which changes also the content of the event browser window. If the user clicked by accident it and changed his mindm he must have a simple way to go back (because he does not necessarily know that the jump was made just one frame forward) - to reduce his mental effort, the "navigate back" arrow button should solve that by jumping back to the fame where we were before

To solve the mental friction of discontinuous timeline jumps, you must implement a `PlaybackHistoryTracker` state machine. This operates identically to the `EntitySelectionHistory` but tracks the frame index.

Because stepping forward for a causality check changes the entire world state and the transient event buffers, treating the jump as a standard web-browser-style navigation event allows the operator to instantly rewind to their exact previous analytical context.

Here is the architectural implementation for timeline jump history.

1\. Timeline History State Machine

Create a dedicated tracker that stores frame indices. It must suspend recording when programmatic navigation (clicking the history Back/Forward buttons) occurs, preventing infinite loops.

```
public sealed class PlaybackHistoryTracker
{
    private readonly List<int> _history = new();
    private int _currentIndex = -1;
    private bool _isNavigating;

    public event Action<int>? OnSeekRequested;
    
    public bool CanGoBack => _currentIndex > 0;
    public bool CanGoForward => _currentIndex < _history.Count - 1;
    
    public void PushFrame(int frameIndex)
    {
        if (_isNavigating) return;
    
        // Prevent duplicate consecutive frames
        if (_currentIndex >= 0 && _history[_currentIndex] == frameIndex) return;
    
        // Truncate forward history if diverging
        if (_currentIndex < _history.Count - 1)
        {
            _history.RemoveRange(_currentIndex + 1, _history.Count - (_currentIndex + 1));
        }
    
        _history.Add(frameIndex);
        _currentIndex++;
    }
    
    public void GoBack()
    {
        if (!CanGoBack) return;
        _isNavigating = true;
        _currentIndex--;
        OnSeekRequested?.Invoke(_history[_currentIndex]);
        _isNavigating = false;
    }
    
    public void GoForward()
    {
        if (!CanGoForward) return;
        _isNavigating = true;
        _currentIndex++;
        OnSeekRequested?.Invoke(_history[_currentIndex]);
        _isNavigating = false;
    }
}
```

2\. Causality Action Integration (`EventBrowserPanel`)

When injecting the "Step Forward and Diff Target" macro into the `EventBrowserPanel`, you must orchestrate both the timeline history and the entity selection history sequentially.

In your context menu logic, extract the entity from the event payload using the `ImGuiEntityLink.TryParse` utility. If a valid entity is found, execute the jump:

```
if (ImGuiApi.MenuItem("Step Forward and Diff Target"))
{
    // 1. Record the pre-jump frame context
    _playbackHistory.PushFrame(_playback.CurrentFrame);

    // 2. Advance the simulation state
    _playback.StepForward(_context.SandboxRepo); [2]
    
    // 3. Record the post-jump frame context
    _playbackHistory.PushFrame(_playback.CurrentFrame);
    
    // 4. Force the Entity Inspector and Diff Viewer to focus the target
    _entityHistory.PushSelection(targetEntity);
}
```

3\. Navigation Toolbar (`ReplayTimelineWindow`)

To visually distinguish history navigation from standard time-stepping, place the History Back/Forward controls in an isolated group on the far left of the timeline transport panel.

```
public void DrawTransportControls(PlaybackHistoryTracker history)
{
    // History Navigation (Jump History)
    ImGuiApi.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.8f, 1.0f, 1f)); // Distinct cyan

    if (!history.CanGoBack) ImGuiApi.BeginDisabled();
    if (ImGuiApi.Button("<- Back")) history.GoBack();
    if (!history.CanGoBack) ImGuiApi.EndDisabled();
    
    ImGuiApi.SameLine();
    
    if (!history.CanGoForward) ImGuiApi.BeginDisabled();
    if (ImGuiApi.Button("Fwd ->")) history.GoForward();
    if (!history.CanGoForward) ImGuiApi.EndDisabled();
    
    ImGuiApi.PopStyleColor();
    
    ImGuiApi.SameLine();
    ImGuiApi.TextDisabled("|");
    ImGuiApi.SameLine();
    
    // Standard Transport
    if (ImGuiApi.Button("|< Rewind")) _playback.Rewind(_repo); [3]
    ImGuiApi.SameLine();
    if (ImGuiApi.Button("< Step")) _playback.StepBackward(_repo); [4]
    ImGuiApi.SameLine();
    if (ImGuiApi.Button("Step >")) _playback.StepForward(_repo); [2]
}
```

4\. Composition Root Wiring

In `ReplayBrowserSubsystem.Initialize`, instantiate the `PlaybackHistoryTracker` alongside the `EntitySelectionHistory`. Wire its `OnSeekRequested` event to instruct the `PlaybackController` to jump, relying on its internal binary-search keyframe resolution to instantly patch the ECS memory.

```
_playbackHistory = new PlaybackHistoryTracker();

_playbackHistory.OnSeekRequested += (targetFrame) =>
{
    _context.SandboxBus.ClearCurrentBuffers(); [6]
    _context.Playback.SeekToFrame(_context.SandboxRepo, targetFrame); [5]
    
    // Re-capture transient events for the target frame so the EventBrowser updates
    _context.HistoryService.Capture("Replay", _context.SandboxBus, (uint)targetFrame); [7]
};
```

This strict decoupling ensures that whether the user scrubs the timeline manually, clicks a causality macro, or navigates back to correct a mistake, the timeline, the ECS repository, and the transient event history remain perfectly synchronized.

ad **Extension: Changelog Export Mode**: this is a brilliant and extremely useful idea. We might extend it by exporting multiple entities that are currently selected in the entity inpector. How to approach it?

To architect a multi-entity changelog export without exhausting memory or blocking the presentation layer, the extraction pipeline must stream mutations to disk incrementally. Relying on the `EntityInspectorPanel`'s existing multi-selection capabilities requires bridging the UI's selected entities to the headless export service.

Here is the architectural design to implement this capability.

1\. Extensible Export Configuration

Update the `JsonExportOptions` state model to support the changelog mode and accept a collection of target entities instead of a single index.

```
public enum ExportFormatMode { AbsoluteState, Changelog }

public sealed class JsonExportOptions
{
    public ExportWindowMode WindowMode = ExportWindowMode.FullFile;
    public ExportFormatMode FormatMode = ExportFormatMode.AbsoluteState;
    
    public int StartFrame = 0;
    public int EndFrame = 1000;
    public float StartTimeSec = 0f;
    public float EndTimeSec = 60f;
    
    // Multi-entity targeting
    public bool FilterBySelection = false;
    public List<Entity> TargetEntities = new(); 
    
    public bool IncludeEntities = true;
    public bool IncludeEvents = true;
    public bool Minified = false;
}
```

2\. Output DTO Definition

Define a structured Data Transfer Object (DTO) for the changelog entries. This ensures the exported JSON is easily ingested by external analytics tools or scripts.

```
public sealed record ChangelogEntryDto(
    int Frame,
    long WallClockTicks,
    string EntityHandle,
    IReadOnlyList<DiffNode> Mutations);
```

3\. Streaming Extraction Pipeline (`RecordingExportService`)

A changelog for a multi-megabyte `.fdp` file will generate millions of JSON nodes. You cannot hold the entire changelog in memory. The `RecordingExportService` must use a `Utf8JsonWriter` to stream the JSON array directly to the output `FileStream`.

To compute the changelog, the service must maintain a persistent baseline of the ECS state for each targeted entity across the timeline iteration.

```
public void ExportChangelog(string inputFdp, string outputJson, JsonExportOptions options)
{
    using var fs = new FileStream(outputJson, FileMode.Create, FileAccess.Write);
    using var writer = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = !options.Minified });

    writer.WriteStartArray(); // Begin root JSON array
    
    // 1. Initialize headless playback sandbox
    using var context = new ReplayBrowserContext();
    context.LoadRecording(inputFdp);
    
    // 2. State baselines for multi-entity tracking
    var baselines = new Dictionary<Entity, JsonNode?>();
    foreach (var target in options.TargetEntities)
    {
        baselines[target] = null; // Baseline is null until the entity is spawned
    }
    
    var diffService = new ComponentDiffService();
    var serializer = new ScenarioSerializer(); // For absolute state extraction
    
    // 3. Fast-forward to start window (using Time or Frame logic)
    SeekToStartWindow(context.Playback, context.SandboxRepo, options);
    
    // 4. Extraction Loop
    while (ShouldContinueExtraction(context.Playback, options))
    {
        int currentFrame = context.Playback.CurrentFrame;
        long currentTicks = context.Playback.GetFrameMetadata(currentFrame).WallClockTicks;
    
        // Apply delta
        context.Playback.StepForward(context.SandboxRepo);
    
        foreach (var target in options.TargetEntities)
        {
            if (!context.SandboxRepo.IsAlive(target)) 
            {
                baselines[target] = null;
                continue;
            }
    
            // Extract post-step state
            var mask = context.SandboxRepo.GetHeader(target.Index).ComponentMask;
            var currentState = serializer.SerializeEntity(context.SandboxRepo, target, new DiagnosticGuidResolver(), mask);
    
            // Compute Diff against baseline
            var diffTree = diffService.ComputeTreeDiff(baselines[target], currentState, epsilonTolerance: 0.001);
    
            // Only emit to disk if a mutation occurred
            if (diffTree.Count > 0)
            {
                var entry = new ChangelogEntryDto(
                    currentFrame, 
                    currentTicks, 
                    $"[{target.Index}, v{target.Generation}]", 
                    diffTree);
    
                JsonSerializer.Serialize(writer, entry, FdpJsonOptionsRegistry.DefaultRelaxed);
            }
    
            // Update baseline for next frame
            baselines[target] = currentState;
        }
    }
    
    writer.WriteEndArray();
}
```

4\. UI Presentation Binding

The `EntityInspectorPanel` already maintains multi-select state via its internal `HashSet<Entity> _selectedEntities`. When the user triggers the export from the `ReplayTimelineWindow`, you extract this selection and inject it into the cloned `JsonExportOptions` before dispatching the background task.

```
// Inside ReplayBrowserWindow.DrawExportExpander()
if (ImGuiApi.Button("Save to JSON..."))
{
    // Clone UI state
    var exportSnapshot = new JsonExportOptions
    {
        WindowMode = _exportOptions.WindowMode,
        FormatMode = _exportOptions.FormatMode,
        // Map the currently selected entities from the Inspector panel
        FilterBySelection = _inspectorPanel._selectedEntities.Count > 0,
        TargetEntities = _inspectorPanel._selectedEntities.ToList(),
        Minified = _exportOptions.Minified
    };

    _ = ExecuteSaveAsJsonAsync(exportSnapshot);
}
```

Architectural Benefits of this Approach

-   **Memory Isolation**: By updating the `baselines` dictionary in-place and using `Utf8JsonWriter` to serialize the `ChangelogEntryDto` immediately, memory consumption remains strictly bounded O(N) where N is the number of _selected entities_, regardless of how many thousands of frames are processed.-   **UI Decoupling**: Extracting the `_selectedEntities` into a flat `List<Entity>` snapshot before entering the `Task.Run` background thread guarantees that the user can change their selection in the `EntityInspectorPanel` while the export is running without causing concurrent modification exceptions.-   **Data Portability**: Emitting a flat array of timestamped mutation events turns the raw ECS memory dump into a temporal database, which is exactly the structure required to pipe the output into Elasticsearch, Kibana, or Python analysis scripts.

good! I think we forgot about on extremely useful idea which is a search capability within the recording. Like for example to search all records where a certain property of certain component of certain entity (or list of selected entitied or any entity) has changed within some range of values (or substring in case of string etc. This is invaluable feature worth ivesting some effort into. how to approach it?

To implement a full-recording search capability without crippling the application with gigabytes of string allocations or O(N) frame-by-frame iterations, you must leverage the engine's existing spatial and temporal optimizations.

A naive approach—deserializing the ECS state to JSON on every frame to evaluate a JSONPath string—will stall the background thread for minutes on large recordings. The clean architecture solution requires combining the `DeltaQuery` chunk-skipping engine with the `StructEdit` offset-binding pipeline to perform zero-allocation property evaluation.

Here is the architectural blueprint for the Recording Search Engine.

1\. Search Query Domain Models

Define strict DTOs to encapsulate the search request and results. This isolates the headless search engine from the ImGui presentation layer.

```
public enum SearchOperator { Equals, Contains, GreaterThan, LessThan, Changed }

public sealed record SearchCriteria(
    Type ComponentType, 
    string PropertyPath,  // e.g. "Position.X"
    SearchOperator Operator, 
    string TargetValue,
    Entity? FilterEntity = null); // Null means any entity

public sealed record SearchResultDto(
    int FrameIndex, 
    long WallClockTicks, 
    Entity Entity, 
    string TransitionText); // e.g. "12.5 -> 55.0"
```

2\. Zero-Allocation Property Accessor (`StructEdit` Integration)

Do not parse JSON or invoke `PropertyInfo.GetValue` inside the frame loop. The engine already possesses `StructEdit`, which builds stable `IValueBinding` instances that read directly from memory.

Create a `PropertyEvaluator` that uses `IComponentEditService` to compile the accessor exactly once before the search begins:

```
internal sealed class PropertyEvaluator
{
    private readonly IValueBinding _binding;
    private readonly IEditBuffer _buffer;

    public PropertyEvaluator(IComponentEditService editService, Type componentType, string propertyPath)
    {
        // 1. Create a dummy buffer of the component type
        object dummyObj = Activator.CreateInstance(componentType)!;
        
        // 2. Open a session scoped strictly to the target field path
        var session = editService.Open(
            dummyObj, 
            componentType, 
            EditScope.ForField($"$.{propertyPath}"));
    
        // 3. Extract the pre-compiled, offset-based memory binding
        var node = session.Document.Root.Children.FirstOrDefault(n => n.Name == propertyPath);
        _binding = node?.Binding ?? throw new ArgumentException($"Invalid path: {propertyPath}");
        _buffer = session.Buffer; // Gives access to the underlying memory block
    }
    
    public string GetValueAsString(object componentInstance)
    {
        // For unmanaged structs, we replace the buffer memory directly and read the binding
        // For managed objects, we update the reference. This avoids per-frame reflection.
        _buffer.ReplaceInstance(componentInstance); 
        return _binding.GetBoxed()?.ToString() ?? "null";
    }
}
```

3\. The Headless Search Engine (`RecordingSearchService`)

The search must run on a background `Task`. It will instantiate an isolated `EntityRepository` and `PlaybackController`.

To achieve massive speedups, use the `QueryDelta` enumerator. `QueryDelta` evaluates chunk versions at the 64KB block boundary. If a chunk of `SimTransform` components has not had its version bumped since the previous frame, the entire block of entities is skipped with a single integer comparison.

```
public IReadOnlyList<SearchResultDto> ExecuteSearch(string fdpPath, SearchCriteria criteria)
{
    var results = new List<SearchResultDto>();
    var editService = new ComponentEditServiceBuilder().Build();
    var evaluator = new PropertyEvaluator(editService, criteria.ComponentType, criteria.PropertyPath);

    using var context = new ReplayBrowserContext();
    context.LoadRecording(fdpPath);
    
    // Build the delta query focused strictly on the target component type
    var query = context.SandboxRepo.Query()
        .With(criteria.ComponentType)
        .Build();
    
    uint lastScannedVersion = 0;
    
    // Fast-forward linearly, evaluating only changed memory
    while (context.Playback.StepForward(context.SandboxRepo))
    {
        int currentFrame = context.Playback.CurrentFrame;
        long wallTicks = context.Playback.GetFrameMetadata(currentFrame).WallClockTicks;
    
        // O(chunks) filtering: strictly yields entities whose component chunk mutated
        foreach (var entity in context.SandboxRepo.QueryDelta(query, lastScannedVersion))
        {
            if (criteria.FilterEntity.HasValue && criteria.FilterEntity.Value != entity)
                continue;
    
            // Box once per mutated entity, evaluate value using StructEdit binding
            object componentObj = context.SandboxRepo.GetRawObject(entity, criteria.ComponentType);
            string currentValue = evaluator.GetValueAsString(componentObj);
    
            if (EvaluateCondition(currentValue, criteria.Operator, criteria.TargetValue))
            {
                results.Add(new SearchResultDto(currentFrame, wallTicks, entity, currentValue));
            }
        }
    
        lastScannedVersion = context.SandboxRepo.GlobalVersion;
    }
    
    return results;
}

private bool EvaluateCondition(string value, SearchOperator op, string target)
{
    // Implement >, <, == parsing for numbers, or string.Contains for text
    // ...
}
```

4\. Presentation Integration (`ReplaySearchWindow`)

Expose this capability as a new perspective-bound `ManagedWindow` in the Replay Browser.

Because we previously implemented the `PlaybackHistoryTracker` and generic `ImGuiEntityLink` deep-linking abstractions, wiring the UI is trivial.

```
public sealed class ReplaySearchPanel
{
    private SearchCriteria _criteria = new(...);
    private IReadOnlyList<SearchResultDto>? _results;
    private bool _isSearching;

    // Injected delegates for deep-linking
    private readonly Action<int> _onSeekRequested;
    private readonly Action<Entity> _onEntitySelected;
    
    public void DrawContent()
    {
        // Render inputs for Component, Path, Operator, and Target Value
        // ...
    
        if (ImGuiApi.Button("Search Recording") && !_isSearching)
        {
            _isSearching = true;
            _results = null;
            
            Task.Run(() => 
            {
                var service = new RecordingSearchService();
                _results = service.ExecuteSearch(currentFdpPath, _criteria);
                _isSearching = false;
            });
        }
    
        if (_isSearching)
        {
            ImGuiApi.TextDisabled("Scanning frames...");
            return;
        }
    
        if (_results != null)
        {
            RenderResultsTable(_results);
        }
    }
    
    private void RenderResultsTable(IReadOnlyList<SearchResultDto> results)
    {
        if (ImGuiApi.BeginTable("SearchResults", 3))
        {
            ImGuiApi.TableSetupColumn("Frame");
            ImGuiApi.TableSetupColumn("Entity");
            ImGuiApi.TableSetupColumn("Value");
            ImGuiApi.TableHeadersRow();
    
            foreach (var res in results)
            {
                ImGuiApi.TableNextRow();
                
                ImGuiApi.TableSetColumnIndex(0);
                if (ImGuiApi.SmallButton($"Frame {res.FrameIndex}"))
                {
                    // 1. Jump timeline to exact frame where mutation occurred
                    _onSeekRequested.Invoke(res.FrameIndex);
                    // 2. Select entity so ComponentDiffViewer highlights the exact field
                    _onEntitySelected.Invoke(res.Entity);
                }
    
                ImGuiApi.TableSetColumnIndex(1);
                ImGuiEntityLink.Draw($"[{res.Entity.Index}, v{res.Entity.Generation}]");
    
                ImGuiApi.TableSetColumnIndex(2);
                ImGuiApi.TextUnformatted(res.TransitionText);
            }
            ImGuiApi.EndTable();
        }
    }
}
```

Architectural Safeguards

-   **Thread Isolation**: The headless `EntityRepository` ensures background step-forward execution never corrupts the live `MapCanvas` view or `EntityInspectorPanel`.-   **CPU Economy**: `QueryDelta` fundamentally changes the Big-O complexity of the search. If searching for a `Health` drop, physics mutations to `SimTransform` are entirely bypassed.-   **Analytic Flow**: The combination of `_onSeekRequested` and `_onEntitySelected` leverages your existing event/causality architecture. A user searches for "Health < 50", clicks the result, and the entire layout instantly snaps to that frame, showing the exact damage event in the Event Browser and the specific health property highlighted in yellow in the Diff Viewer.

How to ensure we can reuse the Structedit input capabilities to enter the value ranges or list of enums to look for or match substring (as well as 'starts with' )

To reuse `StructEdit`'s input capabilities for the search engine, you must treat the search criteria itself as editable data. Rather than writing custom ImGui widget logic for every possible data type, define strongly typed Data Transfer Objects (DTOs) representing the search predicates. You then pass these DTOs into an `IEditSession` and let the existing `ComponentEditDrawer` render the UI.

This architectural approach perfectly separates the search definition from the immediate-mode presentation layer and gives you automatic support for strings, numerics, and dynamic collections (like lists of enums).

Here is the implementation strategy.

1\. Define Predicate DTOs

Define a suite of DTOs for the different match operations. Because `StructEdit` natively maps standard CLR types to `EditNodeKind` variants (such as `EditNodeKind.Scalar` for numbers, `EditNodeKind.String` for strings, and `EditNodeKind.DynamicArray` for lists), the UI will be generated automatically.

```
public abstract class SearchPredicateDto { }

// Handles value ranges
public sealed class NumericPredicateDto : SearchPredicateDto
{
    public double MinValue { get; set; } = double.MinValue;
    public double MaxValue { get; set; } = double.MaxValue;
}

// Handles substrings and 'starts with'
public sealed class StringPredicateDto : SearchPredicateDto
{
    public string Substring { get; set; } = string.Empty;
    public bool StartsWith { get; set; }
    public bool ExactMatch { get; set; }
}

// Handles lists of enums
public sealed class EnumPredicateDto<TEnum> : SearchPredicateDto where TEnum : struct, Enum
{
    // StructEdit uses DynamicArrayBinding for List<T>, automatically providing 
    // array resizing and element-level enum combo boxes in the UI.
    public List<TEnum> AllowedValues { get; set; } = new();
}
```

2\. Session Management in the Search Panel

In your `ReplaySearchPanel`, when the user selects a target component and property path, determine its CLR type. Instantiate the corresponding `SearchPredicateDto` and open a `StructEdit` session over it using `IComponentEditService.Open`.

```
public sealed class ReplaySearchPanel
{
    private readonly IComponentEditService _editService;
    private IEditSession? _predicateSession;
    private ComponentEditDrawer? _predicateDrawer;
    private SearchPredicateDto? _currentPredicate;

    public ReplaySearchPanel(IComponentEditService editService)
    {
        _editService = editService;
    }
    
    private void OnTargetPropertyChanged(Type propertyType)
    {
        _predicateSession?.Dispose();
        
        // 1. Instantiate the correct DTO based on the selected property type
        _currentPredicate = CreatePredicateForType(propertyType);
        
        // 2. Open an isolated StructEdit session over the DTO
        _predicateSession = _editService.Open(
            _currentPredicate, 
            _currentPredicate.GetType(), 
            EditScope.WholeComponent); // Expose all predicate fields
    
        // 3. Initialize the existing ImGui drawer
        _predicateDrawer = new ComponentEditDrawer(_predicateSession, null, new Dictionary<Type, ICustomFieldEditor>());
    }
    
    private SearchPredicateDto CreatePredicateForType(Type propertyType)
    {
        if (propertyType.IsEnum)
        {
            var genericType = typeof(EnumPredicateDto<>).MakeGenericType(propertyType);
            return (SearchPredicateDto)Activator.CreateInstance(genericType)!;
        }
        if (propertyType == typeof(string))
        {
            return new StringPredicateDto();
        }
        
        // Fallback for all numeric scalars (int, float, double, byte, etc.)
        return new NumericPredicateDto();
    }
}
```

3\. Rendering the StructEdit UI

During your ImGui `DrawContent` loop, delegate the rendering of the criteria block to the `ComponentEditDrawer`. It recursively draws the `EditDocument`.

Because `StructEdit` tracks structural mutations, you must call `RebuildDocument` if a dynamic collection resizes. This ensures the "list of enums" UI updates correctly when the user adds or removes an allowed enum value.

```
public void DrawContent()
{
    // Render target selection (Component + Property Path)
    // ...

    if (_predicateSession != null && _predicateDrawer != null)
    {
        ImGuiApi.TextDisabled("Search Criteria");
        ImGuiApi.Separator();
    
        // Handle structural changes (e.g., adding an item to the Enum List)
        if (_predicateSession.RebuildState == EditRebuildState.RebuildRequired)
        {
            _predicateSession.RebuildDocument();
        }
    
        if (ImGuiApi.BeginTable("PredicateTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable))
        {
            ImGuiApi.TableSetupColumn("Parameter", ImGuiTableColumnFlags.WidthFixed, 150f);
            ImGuiApi.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            
            // Reuses the engine's standard property grid layout
            _predicateDrawer.DrawEditNode(_predicateSession.Document.Root);
            
            ImGuiApi.EndTable();
        }
    }
    
    ImGuiApi.Spacing();
    
    if (ImGuiApi.Button("Execute Search"))
    {
        // 1. Validate the predicate data via StructEdit
        var validation = _predicateSession?.Validate();
        if (validation != null && !validation.IsValid)
        {
            // Handle validation error
            return;
        }
    
        // 2. Commit the session to get the latest boxed values
        SearchPredicateDto finalizedPredicate = (SearchPredicateDto)_predicateSession!.Commit();
        
        // 3. Dispatch to RecordingSearchService
        DispatchSearch(finalizedPredicate);
    }
}
```

Architectural Benefits

-   **Zero UI Duplication:** You leverage `ComponentEditDrawer` directly. Any custom `ICustomFieldEditor` plugins registered globally in the engine (e.g., `GuidFieldEditor`) are automatically supported.-   **Safe Memory Boundaries:** The `_predicateSession` clones your DTO into an isolated `ManagedObjectEditBuffer`. The active simulation memory is completely untouched during configuration.-   **Validation Ready:** If you need to restrict inputs (e.g., `MinValue` cannot exceed `MaxValue`), you can write an `IComponentValidator` for `NumericPredicateDto`, and `StructEdit` will automatically trap invalid configurations before the search executes.

what will be the visual layout of such a search window?

The visual layout of the `ReplaySearchWindow` must maintain aesthetic consistency with the engine's existing diagnostic tools by leveraging the standard 2-column property grid pattern and a sortable results table.

ImGui Wireframe Layout

```
[Replay Search Window]
+-----------------------------------------------------------------------+
| Target Definition                                                     |
|-----------------------------------------------------------------------|
| Component:    [ SimTransform ˅ ]                                      |
| Property:     [ Position.X     ]                                      |
| Entity Mask:  [ (Any)          ]  // Or specific ID e.g., [42, v3]    |
|-----------------------------------------------------------------------|
| Search Criteria                                                       |
|---------------------------------|-------------------------------------|
| Parameter                       | Value                               |
|---------------------------------|-------------------------------------|
| MinValue                        | [ 10.000                        ]   |
| MaxValue                        | [ 50.000                        ]   |
|-----------------------------------------------------------------------|
| [ Execute Search ]              | Status: 45 results found.           |
|-----------------------------------------------------------------------|
| Search Results                                                        |
|------------------|-------------------|--------------------------------|
| Frame            | Entity            | Value Transition               |
|------------------|-------------------|--------------------------------|
| [Frame 1420]     | [42, v3]          | 9.995  -> 10.050               |
| [Frame 1421]     | [42, v3]          | 10.050 -> 10.200               |
| [Frame 3005]     | [85, v1]          | 8.500  -> 12.000               |
+-----------------------------------------------------------------------+
```

Architectural Breakdown

**1\. Target Definition Region** This upper section dictates the search domain. It defines the `ComponentType` and the `PropertyPath` that the headless `PropertyEvaluator` will bind to. The entity mask allows the user to scope the search globally or restrict it to a specific entity handle.

**2\. Dynamic Criteria Region** This section is driven entirely by the `ComponentEditDrawer` rendering a dynamically constructed `IEditSession`. Using a 2-column table configured with `ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable`, it projects the fields of the specific `SearchPredicateDto` into standard ImGui inputs without requiring custom UI code for each data type. Any dynamic collections, such as an allowed list of enums, will automatically support array resizing and item deletion through the underlying `IContainerBinding` implementation.

**3\. Results Data Grid** The results are presented in a 3-column `ImGuiTable` utilizing `ImGuiTableFlags.ScrollY` and `ImGuiTableFlags.RowBg` to handle potentially thousands of hits efficiently.

-   **Frame Column:** Rendered as an `ImGuiApi.SmallButton`. Clicking this acts as a temporal deep-link, firing the injected `_onSeekRequested` delegate to instantly seek the replay timeline to the exact frame of the mutation.-   **Entity Column:** Rendered using the `ImGuiEntityLink` utility, displaying the entity as `[Index, vGeneration]`. Clicking this acts as a spatial deep-link, firing the `_onEntitySelected` delegate to focus the main `EntityInspectorPanel` on the target.-   **Value Transition Column:** Displays the extracted string conversion of the mutated property, providing immediate causality context directly inline.

cool! this solves the component properties. I would like to be able to search also for frame range(s) where entity with some id - either the ECS entity id or the network id or even a entity having some name (substring) - existed. And be able to jump to the range beginning right from the result grid

To support temporal lifecycle searching without violating the zero-allocation constraints of the background extraction thread, you must implement a state-machine approach within the `RecordingSearchService`.

Scanning every entity's name or network ID frame-by-frame is computationally unviable. Instead, the architecture must leverage the engine's `QueryDelta` for O(1) change detection and the `EntityRepository.GetDestructionLog()` to cleanly trap entity death events.

Here is the architectural implementation.

1\. Search Domain Expansion

Extend the existing DTOs to support the lifecycle search context. Define a specific predicate for entity identification and a result DTO that encapsulates the temporal range.

```
public enum EntityIdentifierType { EcsHandle, NetworkId, NameSubstring }

public sealed class LifecyclePredicateDto : SearchPredicateDto
{
    public EntityIdentifierType IdentifierType { get; set; }
    
    // Will hold the "[42, v3]" string, the numeric Network ID, or the Name substring
    public string TargetValue { get; set; } = string.Empty; 
}

public sealed record LifecycleSearchResultDto(
    Entity Entity,
    int StartFrame,
    int EndFrame,
    string MatchContext); // E.g., "Network ID: 1005" or "Name: Tank-1"
```

Because `LifecyclePredicateDto` inherits from `SearchPredicateDto`, the existing `StructEdit` integration in the `ReplaySearchPanel` will automatically render the UI inputs for these properties.

2\. The Stateful Search Engine (`RecordingSearchService`)

The search service must track the birth and death of matching entities as it steps through the timeline.

When a frame is applied, the `EntityRepository` populates its destruction log with any entities that died during that step. By reading and clearing this log, you close temporal ranges perfectly without scanning the entire `EntityIndex`.

```
public IReadOnlyList<LifecycleSearchResultDto> ExecuteLifecycleSearch(string fdpPath, LifecyclePredicateDto criteria)
{
    var results = new List<LifecycleSearchResultDto>();
    var activeRanges = new Dictionary<Entity, int>(); // Tracks Entity -> StartFrame
    var matchContexts = new Dictionary<Entity, string>(); // Tracks Entity -> Reason for match

    using var context = new ReplayBrowserContext();
    context.LoadRecording(fdpPath);
    
    // Prepare queries for matching
    var netQuery = context.SandboxRepo.Query().With<NetworkIdentity>().Build();
    var nameQuery = context.SandboxRepo.Query().With<EntityInfo>().Build();
    
    // Parse the target criteria upfront
    bool isEcsTarget = criteria.IdentifierType == EntityIdentifierType.EcsHandle 
                       && ImGuiEntityLink.TryParse(criteria.TargetValue, out Entity targetEcs);
    bool isNetIdTarget = criteria.IdentifierType == EntityIdentifierType.NetworkId 
                         && long.TryParse(criteria.TargetValue, out long targetNetId);
    string targetNameLower = criteria.TargetValue.ToLowerInvariant();
    
    uint lastScannedVersion = 0;
    
    while (context.Playback.StepForward(context.SandboxRepo))
    {
        int currentFrame = context.Playback.CurrentFrame;
    
        // 1. Process Destructions to close ranges
        var destructionLog = context.SandboxRepo.GetDestructionLog();
        foreach (var deadEntity in destructionLog)
        {
            if (activeRanges.Remove(deadEntity, out int startFrame))
            {
                string contextStr = matchContexts.GetValueOrDefault(deadEntity, "Unknown");
                matchContexts.Remove(deadEntity);
                results.Add(new LifecycleSearchResultDto(deadEntity, startFrame, currentFrame, contextStr));
            }
        }
        context.SandboxRepo.ClearDestructionLog(); // Reset for the next frame [2]
    
        // 2. Discover new matches using QueryDelta
        if (criteria.IdentifierType == EntityIdentifierType.EcsHandle && isEcsTarget)
        {
            if (!activeRanges.ContainsKey(targetEcs) && context.SandboxRepo.IsAlive(targetEcs))
            {
                activeRanges[targetEcs] = currentFrame;
                matchContexts[targetEcs] = $"Exact Match: {criteria.TargetValue}";
            }
        }
        else if (criteria.IdentifierType == EntityIdentifierType.NetworkId && isNetIdTarget)
        {
            foreach (var entity in context.SandboxRepo.QueryDelta(netQuery, lastScannedVersion))
            {
                if (activeRanges.ContainsKey(entity)) continue;
                
                long netId = context.SandboxRepo.GetComponentRO<NetworkIdentity>(entity).Value;
                if (netId == targetNetId)
                {
                    activeRanges[entity] = currentFrame;
                    matchContexts[entity] = $"Network ID: {netId}";
                }
            }
        }
        else if (criteria.IdentifierType == EntityIdentifierType.NameSubstring)
        {
            foreach (var entity in context.SandboxRepo.QueryDelta(nameQuery, lastScannedVersion))
            {
                if (activeRanges.ContainsKey(entity)) continue;
    
                string name = context.SandboxRepo.GetComponentRO<EntityInfo>(entity).Name.ToString();
                if (name.Contains(targetNameLower, StringComparison.OrdinalIgnoreCase))
                {
                    activeRanges[entity] = currentFrame;
                    matchContexts[entity] = $"Name: {name}";
                }
            }
        }
    
        lastScannedVersion = context.SandboxRepo.GlobalVersion;
    }
    
    // 3. Flush any entities still alive at the end of the recording
    int eofFrame = context.Playback.CurrentFrame;
    foreach (var kvp in activeRanges)
    {
        results.Add(new LifecycleSearchResultDto(kvp.Key, kvp.Value, eofFrame, matchContexts[kvp.Key]));
    }
    
    return results;
}
```

3\. Presentation Layer Integration (`ReplaySearchPanel`)

Modify the results grid in `ReplaySearchPanel` to detect which result type was returned and render the appropriate columns. Implementing the deep-linking requires invoking the `_onSeekRequested` injected delegate directly from the "Start Frame" button.

```
private void RenderLifecycleResultsTable(IReadOnlyList<LifecycleSearchResultDto> results)
{
    if (ImGuiApi.BeginTable("LifecycleResults", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
    {
        ImGuiApi.TableSetupColumn("Entity", ImGuiTableColumnFlags.WidthFixed, 100f);
        ImGuiApi.TableSetupColumn("Match Context", ImGuiTableColumnFlags.WidthStretch);
        ImGuiApi.TableSetupColumn("Start Frame", ImGuiTableColumnFlags.WidthFixed, 80f);
        ImGuiApi.TableSetupColumn("End Frame", ImGuiTableColumnFlags.WidthFixed, 80f);
        ImGuiApi.TableHeadersRow();

        foreach (var res in results)
        {
            ImGuiApi.TableNextRow();
            
            // Spatial Deep Link
            ImGuiApi.TableSetColumnIndex(0);
            ImGuiEntityLink.Draw($"[{res.Entity.Index}, v{res.Entity.Generation}]");
            if (ImGuiApi.IsItemClicked()) _onEntitySelected?.Invoke(res.Entity);
    
            ImGuiApi.TableSetColumnIndex(1);
            ImGuiApi.TextUnformatted(res.MatchContext);
    
            // Temporal Deep Link (Start)
            ImGuiApi.TableSetColumnIndex(2);
            if (ImGuiApi.SmallButton($"Frame {res.StartFrame}##start_{res.Entity.GetHashCode()}"))
            {
                _onSeekRequested?.Invoke(res.StartFrame);
                _onEntitySelected?.Invoke(res.Entity);
            }
    
            // Temporal Deep Link (End)
            ImGuiApi.TableSetColumnIndex(3);
            if (ImGuiApi.SmallButton($"Frame {res.EndFrame}##end_{res.Entity.GetHashCode()}"))
            {
                _onSeekRequested?.Invoke(res.EndFrame);
                _onEntitySelected?.Invoke(res.Entity);
            }
        }
        ImGuiApi.EndTable();
    }
}
```

Architectural Benefits

-   **Zero-Allocation String Matching**: By utilizing `QueryDelta`, the system completely skips chunks where `EntityInfo` and `NetworkIdentity` have not mutated. It only parses strings and allocates matching logic when a relevant component is newly spawned or explicitly modified.-   **Deterministic Range Closure**: Coupling the end-range detection to `GetDestructionLog` guarantees that the lifecycle boundaries are mathematically precise without requiring O(N) liveness validations per frame.

how would the visual layout for entity live span search could look like?

```
[Replay Search Window]
+-----------------------------------------------------------------------+
| Search Mode:  ( ) Component Mutation    (x) Entity Lifecycle          |
+-----------------------------------------------------------------------+
| Target Definition                                                     |
|-----------------------------------------------------------------------|
| Identifier Type: [ Name Substring ˅ ]                                 |
| Target Value:    [ Tank                             ]                 |
+-----------------------------------------------------------------------+
| [ Execute Search ]              | Status: 3 ranges found.             |
+-----------------------------------------------------------------------+
| Search Results                                                        |
|------------------|-----------------------|-------------|--------------|
| Entity           | Match Context         | Start Frame | End Frame    |
|------------------|-----------------------|-------------|--------------|
| [42, v1]         | Name: Tank-1          | [Frame 0]   | [Frame 450]  |
| [85, v3]         | Name: Tank Platoon-2  | [Frame 120] | [Frame 3005] |
| [102, v1]        | Name: Tank-1          | [Frame 460] | [EOF]        |
+-----------------------------------------------------------------------+
```

Architectural Layout & ImGui Implementation

To maintain interface cohesion without duplicating window management logic, the `ReplaySearchPanel` must implement a mode-switcher (radio buttons or tabs) at the top to toggle the active `SearchPredicateDto` being fed into the `StructEdit` drawer.

**1\. Mode Switching & Target Definition** The top region binds to the `IdentifierType` enum. Because the search engine operates headlessly over the `.fdp` file, switching to "Entity Lifecycle" mode swaps the backing UI state object to `LifecyclePredicateDto`. The `ComponentEditDrawer` automatically adapts to render the `IdentifierType` combo box and the `TargetValue` text input.

**2\. Data Grid (Results)** The results table uses a 4-column layout configured with `ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY`.

-   **Entity Column:** Renders the entity handle using the canonical diagnostic format `[Index, vGeneration]`. This is rendered using the `ImGuiEntityLink.Draw` utility. Clicking it triggers the spatial deep-link delegate, updating `InspectorState.SelectedEntity` to focus the main Entity Inspector.-   **Match Context Column:** Because the user might search for a substring (e.g., "Tank"), this column provides absolute clarity by displaying the exact `EntityInfo.Name` or the explicit `NetworkIdentity.Value` that triggered the match during the extraction loop.-   **Start Frame / End Frame Columns:** Rendered as `ImGuiApi.SmallButton` elements. Clicking either button triggers the temporal deep-link delegate. This invokes `PlaybackController.SeekToFrame` to instantly snap the sandbox repository's memory to the exact simulation tick where the entity was spawned or destroyed. If an entity survives until the end of the recording, the End Frame column renders plain text `[EOF]` instead of a button.

This design guarantees that temporal boundary navigation is exactly one click away, allowing an operator to instantly observe the ECS state immediately following an entity's creation or immediately preceding its destruction.

good! any other usefull idea for the search if you put on the user's hat?

**1\. Transient Event Search** Instead of solely polling persistent ECS components, operators must be able to search for discrete, transient events such as `HitEvent` or `DetonationNotification`. Since these events exist only in the `FdpEventBus` buffers for a single frame before being discarded, finding them manually is tedious. The search engine must be extended to iterate the recorded event streams, allowing users to apply the same `StructEdit` property evaluation to event payloads (e.g., finding the exact frame a `WeaponFireNotification` was emitted with a specific `WeaponIndex`).

**2\. Spatial Bounding Search** Diagnostic tasks often require knowing when an entity entered or exited a specific geographic zone. You must implement a spatial search criteria (bounding box or radius). During the `RecordingSearchService` extraction loop, the service evaluates `SimTransform.Position` against the defined geometry. This isolates the exact frame of a perimeter breach without requiring the user to visually scrub the timeline.

**3\. Structural Modification Search** Developers frequently need to locate the frame where an entity gained or lost a specific component, such as gaining an `IsEmbarkedTag` or a `PhysicsCollider`. This is computationally cheaper than a property search: the engine only needs to compare the `EntityHeader.ComponentMask` across frames. By performing a bitwise XOR between the previous and current frame's mask, the background service can detect structural additions and removals in O(1) time.

**4\. Cognitive and Behavioral Transition Search** In complex AI scenarios, a user needs to find when a specific decision was made. Extend the search capabilities with a dedicated behavior transition mode. This allows querying for the exact tick an entity's `BehaviorState.ActiveBehaviorHash` changed (e.g., transitioning from Patrol to Combat), or when its `NavigationStatus.Result` flipped to `FailedBlocked`. Because behavior hashes are compile-time constants, this evaluation requires zero string parsing during the background search loop.

perfect. let's detail the **1\. Transient Event Search**

To implement a transient event search without violating the zero-allocation constraints of the background extraction thread, you must bypass the boxing overhead inherent in diagnostic interfaces like `IEventStreamInspector.InspectReadBuffer()`.

During replay, the `PlaybackController` injects serialized events directly into the `FdpEventBus` read buffers. To search these efficiently, the architecture must dynamically compile a generic scanner that retrieves the `ReadOnlySpan<T>` for the target event type, evaluates the memory using the existing `StructEdit` offset bindings, and yields matches.

Here is the architectural implementation for the Transient Event Search.

1\. Search Domain Extension

Expand the `SearchCriteria` to distinguish between persistent ECS components and transient events.

```
public enum SearchTargetType { Component, Event }

public sealed record SearchCriteria(
    SearchTargetType TargetType,
    Type DataType,        // Component Type or Event Type
    string PropertyPath,  // e.g., "Shooter" or "Damage"
    SearchOperator Operator, 
    string TargetValue);
```

By unifying the criteria, the ImGui presentation layer can use a simple toggle to switch the `ComponentEditDrawer` between reflecting over `ComponentTypeRegistry` types and `EventTypeRegistry` types.

2\. Zero-Allocation Event Scanner

You must not use reflection inside the frame-stepping loop. Instead, compile a strongly-typed scanner delegate once when the search begins. This delegate will invoke `FdpEventBus.Read<T>()`, iterate over the unmanaged span, and evaluate the specific property path.

```
internal delegate void EventScannerDelegate(
    FdpEventBus bus, 
    int currentFrame, 
    long wallTicks, 
    List<SearchResultDto> results);

internal sealed class EventScannerFactory
{
    public static EventScannerDelegate CompileScanner(SearchCriteria criteria, IComponentEditService editService)
    {
        var busParam = Expression.Parameter(typeof(FdpEventBus), "bus");
        var frameParam = Expression.Parameter(typeof(int), "frame");
        var ticksParam = Expression.Parameter(typeof(long), "ticks");
        var resultsParam = Expression.Parameter(typeof(List<SearchResultDto>), "results");

        // Obtain MethodInfo for FdpEventBus.Read<T>()
        var readMethod = typeof(FdpEventBus)
            .GetMethod(nameof(FdpEventBus.Read))!
            .MakeGenericMethod(criteria.DataType);
    
        // Pre-compile the property evaluator using StructEdit's memory binding
        var evaluator = new PropertyEvaluator(editService, criteria.DataType, criteria.PropertyPath);
    
        // Create the closure that will be invoked per-frame
        Action<FdpEventBus, int, long, List<SearchResultDto>> action = (bus, frame, ticks, results) =>
        {
            // Reflection invoke is avoided here by using the compiled delegate or dynamic invoke wrapper,
            // but for absolute zero-allocation, we generate IL that iterates the ReadOnlySpan<T>.
            
            // Note: Since ReadOnlySpan<T> cannot be easily manipulated in Expression Trees, 
            // we use a generic helper class instantiated via reflection once.
        };
    
        return (EventScannerDelegate)Delegate.CreateDelegate(typeof(EventScannerDelegate), 
            Activator.CreateInstance(
                typeof(FastEventScanner<>).MakeGenericType(criteria.DataType), 
                evaluator, criteria)!, 
            "Scan");
    }
}

// Generic closure to handle the ReadOnlySpan<T> safely
internal sealed class FastEventScanner<T> where T : unmanaged
{
    private readonly PropertyEvaluator _evaluator;
    private readonly SearchCriteria _criteria;

    public FastEventScanner(PropertyEvaluator evaluator, SearchCriteria criteria)
    {
        _evaluator = evaluator;
        _criteria = criteria;
    }
    
    public void Scan(FdpEventBus bus, int frame, long ticks, List<SearchResultDto> results)
    {
        ReadOnlySpan<T> events = bus.Read<T>(); // Zero-allocation span retrieval [5, 6]
        if (events.IsEmpty) return;
    
        for (int i = 0; i < events.Length; i++)
        {
            T evt = events[i];
            
            // Box once per event to evaluate the property. 
            // For a 60Hz simulation with sparse events, this allocation is negligible 
            // compared to full JSON tree generation.
            string currentValue = _evaluator.GetValueAsString(evt);
    
            if (EvaluateCondition(currentValue, _criteria.Operator, _criteria.TargetValue))
            {
                // Events do not have a universal Entity header, so we pass Entity.Null 
                // unless the property being searched IS the entity handle.
                Entity relatedEntity = Entity.Null;
                if (ImGuiEntityLink.TryParse(currentValue, out Entity parsed))
                {
                    relatedEntity = parsed;
                }
    
                results.Add(new SearchResultDto(frame, ticks, relatedEntity, currentValue));
            }
        }
    }
}
```

3\. Pipeline Integration (`RecordingSearchService`)

Integrate the compiled event scanner into the existing headless fast-forward loop. Because events exist in a transient double-buffer, they are only accessible during the specific frame they were recorded.

```
public IReadOnlyList<SearchResultDto> ExecuteEventSearch(string fdpPath, SearchCriteria criteria)
{
    var results = new List<SearchResultDto>();
    var editService = new ComponentEditServiceBuilder().Build();
    
    // Compile the O(N) scanner once
    var scanner = EventScannerFactory.CompileScanner(criteria, editService);
    
    using var context = new ReplayBrowserContext();
    context.LoadRecording(fdpPath);
    
    while (context.Playback.StepForward(context.SandboxRepo))
    {
        int currentFrame = context.Playback.CurrentFrame;
        long wallTicks = context.Playback.GetFrameMetadata(currentFrame).WallClockTicks;
    
        // The PlaybackController injects binary events into the FdpEventBus read buffers 
        // during StepForward [2, 3]. We scan them immediately before they are cleared.
        scanner.Invoke(context.SandboxBus, currentFrame, wallTicks, results);
    }
    
    return results;
}
```

4\. Presentation and Causality Linkage

Transient event searches provide the ultimate causality diagnostic. If a developer searches for `DamageAssessedEvent` where `TotalDamage > 50.0`, the result grid will output the exact frames where massive damage occurred.

Modify the `ReplaySearchPanel` results grid to handle event-based outcomes:

```
private void RenderEventResultsTable(IReadOnlyList<SearchResultDto> results)
{
    if (ImGuiApi.BeginTable("EventSearchResults", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
    {
        ImGuiApi.TableSetupColumn("Frame", ImGuiTableColumnFlags.WidthFixed, 80f);
        ImGuiApi.TableSetupColumn("Event Payload Value", ImGuiTableColumnFlags.WidthStretch);
        ImGuiApi.TableSetupColumn("Related Entity", ImGuiTableColumnFlags.WidthFixed, 100f);
        ImGuiApi.TableHeadersRow();

        foreach (var res in results)
        {
            ImGuiApi.TableNextRow();
            
            ImGuiApi.TableSetColumnIndex(0);
            if (ImGuiApi.SmallButton($"Frame {res.FrameIndex}##{res.GetHashCode()}"))
            {
                // 1. Jump timeline to exact frame. This repopulates the EventBrowserPanel 
                // with the transient events for this specific tick.
                _onSeekRequested?.Invoke(res.FrameIndex);
                
                // 2. Focus entity if the searched property was an Entity handle
                if (res.Entity.IsValid)
                {
                    _onEntitySelected?.Invoke(res.Entity);
                }
            }
    
            ImGuiApi.TableSetColumnIndex(1);
            ImGuiApi.TextUnformatted(res.TransitionText);
    
            ImGuiApi.TableSetColumnIndex(2);
            if (res.Entity.IsValid)
            {
                if (ImGuiEntityLink.Draw($"[{res.Entity.Index}, v{res.Entity.Generation}]"))
                {
                    _onEntitySelected?.Invoke(res.Entity);
                }
            }
        }
        ImGuiApi.EndTable();
    }
}
```

Architectural Benefits

-   **Guaranteed Event Capture:** Because `FdpEventBus` uses a double-buffered architecture, events published in Frame N are consumed in Frame N+1. The `PlaybackController` injects recorded events directly into the read buffer, guaranteeing that our scanner reads the exact payloads observed by the simulation logic on that frame.-   **No Diagnostic Boxing on Hot Path:** By compiling a generic caller that leverages `bus.Read<T>()`, the search engine avoids the heavy boxing required by `IEventStreamInspector.InspectReadBuffer()`, keeping memory traffic strictly limited to actual matches.-   **Unified Property Evaluation:** Relying on `StructEdit`'s existing offset bindings means developers can execute complex queries (like `WeaponFireIntent.WeaponIndex == 2`) using the exact same syntax and UI components they use for persistent ECS data.

note we have to support the search for managed events as well, accepting the GC pressure (which is no issue as searching the recording is purely UI stuff, no hot path)

To support searching through managed event streams (such as `AssignBehaviorEvent` or `ContextActionsUpdate`), the generic compilation pipeline must be extended to differentiate between native and managed event topologies.

The `FdpEventBus` strictly separates unmanaged events (accessed via `bus.Read<T>()` returning a zero-allocation `ReadOnlySpan<T>`) from managed events (accessed via `bus.ReadManaged<T>()` returning an `IReadOnlyList<T>`). Furthermore, the engine routes type resolution based on whether the type is a value type or a reference type.

By branching the factory logic at query-compile time, we maintain the exact same `EventScannerDelegate` signature for the execution loop. The overhead is restricted strictly to the `IReadOnlyList<T>` enumeration and the allocation of the results, which is perfectly acceptable for an asynchronous diagnostic task.

Here is the architectural extension.

1\. Managed Event Scanner Implementation

Create a parallel generic closure for managed types. Because `T` is a reference type (or a managed struct), the `IReadOnlyList<T>` enumerator will yield heap references. The `PropertyEvaluator` accepts these directly without incurring the boxing penalty that unmanaged structs suffer.

```
internal sealed class ManagedEventScanner<T> 
{
    private readonly PropertyEvaluator _evaluator;
    private readonly SearchCriteria _criteria;

    public ManagedEventScanner(PropertyEvaluator evaluator, SearchCriteria criteria)
    {
        _evaluator = evaluator;
        _criteria = criteria;
    }
    
    public void Scan(FdpEventBus bus, int frame, long ticks, List<SearchResultDto> results)
    {
        // Engine returns a snapshot list of the managed events from the previous frame [2]
        IReadOnlyList<T> events = bus.ReadManaged<T>(); 
        if (events.Count == 0) return;
    
        for (int i = 0; i < events.Count; i++)
        {
            T evt = events[i];
            
            // Evaluates the property directly against the heap reference
            string currentValue = _evaluator.GetValueAsString(evt);
    
            if (EvaluateCondition(currentValue, _criteria.Operator, _criteria.TargetValue))
            {
                Entity relatedEntity = Entity.Null;
                if (ImGuiEntityLink.TryParse(currentValue, out Entity parsed))
                {
                    relatedEntity = parsed;
                }
    
                results.Add(new SearchResultDto(frame, ticks, relatedEntity, currentValue));
            }
        }
    }
}
```

2\. Factory Branching Logic

Update `EventScannerFactory.CompileScanner` to inspect the `Type.IsValueType` property. This identically mirrors the internal behavior of `FdpEventBus.HasEvent(Type type)`, which routes structs to the native unmanaged streams and classes to the managed streams.

```
internal sealed class EventScannerFactory
{
    public static EventScannerDelegate CompileScanner(SearchCriteria criteria, IComponentEditService editService)
    {
        var evaluator = new PropertyEvaluator(editService, criteria.DataType, criteria.PropertyPath);

        // FdpEventBus natively partitions streams by IsValueType [3, 4]
        if (criteria.DataType.IsValueType)
        {
            return (EventScannerDelegate)Delegate.CreateDelegate(typeof(EventScannerDelegate), 
                Activator.CreateInstance(
                    typeof(FastEventScanner<>).MakeGenericType(criteria.DataType), 
                    evaluator, criteria)!, 
                "Scan");
        }
        else
        {
            return (EventScannerDelegate)Delegate.CreateDelegate(typeof(EventScannerDelegate), 
                Activator.CreateInstance(
                    typeof(ManagedEventScanner<>).MakeGenericType(criteria.DataType), 
                    evaluator, criteria)!, 
                "Scan");
        }
    }
}
```

3\. Pipeline Consistency

The `RecordingSearchService` execution loop remains completely unchanged from the previous implementation.

```
// Unchanged execution loop inside RecordingSearchService.ExecuteEventSearch:
while (context.Playback.StepForward(context.SandboxRepo))
{
    int currentFrame = context.Playback.CurrentFrame;
    long wallTicks = context.Playback.GetFrameMetadata(currentFrame).WallClockTicks;

    // scanner delegate transparently dispatches to bus.Read<T>() or bus.ReadManaged<T>()
    scanner.Invoke(context.SandboxBus, currentFrame, wallTicks, results);
}
```

This design preserves the strict separation of concerns. The `RecordingSearchService` knows nothing about the underlying memory layout of the target event. The compilation step resolves the memory semantics exactly once, baking the correct `FdpEventBus` read method and property projection into the closure before the multi-thousand-frame fast-forward begins.

what would be the visual layot for the event scanner UI panel?

```
[Replay Search Window]
+-----------------------------------------------------------------------+
| Search Mode:  ( ) Component Mutation  (x) Transient Event  ( ) Lifecycle  |
+-----------------------------------------------------------------------+
| Target Definition                                                     |
|-----------------------------------------------------------------------|
| Event Type:   [ WeaponFireIntent ˅ ]                                  |
| Property:     [ WeaponIndex        ]                                  |
+-----------------------------------------------------------------------+
| Search Criteria (StructEdit)                                          |
|---------------------------------|-------------------------------------|
| Parameter                       | Value                               |
|---------------------------------|-------------------------------------|
| Operator                        | [ Equals ˅                      ]   |
| Target Value                    | [ 2                             ]   |
+-----------------------------------------------------------------------+
| [ Execute Search ]              | Status: 18 events found.            |
+-----------------------------------------------------------------------+
| Search Results                                                        |
|------------------|-----------------------------------|----------------|
| Frame            | Event Payload Value               | Related Entity |
|------------------|-----------------------------------|----------------|
| [Frame 1420]     | WeaponIndex: 2                    | [42, v3]       |
| [Frame 1421]     | WeaponIndex: 2                    | [42, v3]       |
| [Frame 3005]     | WeaponIndex: 2                    | [85, v1]       |
+-----------------------------------------------------------------------+
```

Architectural Layout & ImGui Implementation

To maintain interface cohesion and cleanly integrate the new event scanner, the existing `ReplaySearchPanel` requires minimal structural changes while expanding its data domain.

**1\. Mode Switching & Target Definition** The top radio group is expanded to include the "Transient Event" mode. Selecting this mode swaps the population source of the primary dropdown from the ECS `ComponentTypeRegistry` to the `EventTypeRegistry`. The `PropertyPath` input defines the exact field within the event struct (e.g., `WeaponIndex` or `TotalDamage`) that the pre-compiled `EventScannerDelegate` will bind to.

**2\. Dynamic Criteria Region** Because we route the condition logic through `StructEdit`, the criteria block remains completely unchanged from the component search architecture. The `ComponentEditDrawer` automatically projects the `SearchPredicateDto` (such as `NumericPredicateDto` for numeric thresholds like `TotalDamage > 50.0`) into the standard two-column ImGui table. This guarantees that search predicates remain strongly typed and decoupled from immediate-mode GUI code.

**3\. Results Data Grid** The results are presented in a 3-column `ImGuiTable` using `ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg` for performance over thousands of hits.

-   **Frame Column:** Rendered as an `ImGuiApi.SmallButton`. Clicking this executes a temporal deep-link via the injected `_onSeekRequested` delegate. Because the engine utilizes a double-buffered `FdpEventBus` where transient events exist only for a single tick, seeking to this exact frame instantly repopulates the separate `EventBrowserPanel` with all surrounding contextual events from that specific simulation step.-   **Event Payload Value:** Displays the exact value of the evaluated property at the time the event fired, using syntax-colored text.-   **Related Entity Column:** Rendered using the `ImGuiEntityLink` utility. If the generic scanner detected an entity handle within the evaluated event payload (e.g., a `Shooter` or `Target` reference), it is displayed as a clickable `[Index, vGeneration]` string. Clicking it triggers the `_onEntitySelected` delegate, instantly shifting the spatial `MapCanvas` and the `EntityInspectorPanel` to focus on the responsible entity.

it should support for searching for pure event type, regardless of the value of its fields, do you count with that?

Supporting a pure event type search is architecturally trivial and yields a massive performance advantage by bypassing payload inspection entirely.

Because the `FdpEventBus` strictly double-buffers events and tracks active streams via an internal `_activeEventIds` hash set, checking for the mere occurrence of an event requires zero memory allocation and zero span iteration. We can resolve the presence of both unmanaged and managed events in O(1) time using the existing `HasEvent(Type type)` method.

Here is how to cleanly integrate this into the compiled scanner architecture.

1\. Domain Model Adjustment

Make the property-specific fields nullable in the `SearchCriteria` DTO. When `PropertyPath` is null or empty, the search engine treats it as a pure occurrence query.

```
public sealed record SearchCriteria(
    SearchTargetType TargetType,
    Type DataType,
    string? PropertyPath = null,  // Null indicates a pure occurrence search
    SearchOperator? Operator = null, 
    string? TargetValue = null);
```

2\. O(1) Presence Scanner Compilation

In the `EventScannerFactory`, branch the compilation logic before evaluating `StructEdit` bindings. If no property path is provided, emit a lightweight closure that directly polls the bus's active event registry.

```
internal sealed class EventScannerFactory
{
    public static EventScannerDelegate CompileScanner(SearchCriteria criteria, IComponentEditService editService)
    {
        // Branch 1: Pure Event Occurrence (Zero payload inspection, O(1) lookup)
        if (string.IsNullOrEmpty(criteria.PropertyPath))
        {
            return (bus, frame, ticks, results) =>
            {
                // HasEvent natively handles both unmanaged EventId lookup and managed type hashing
                if (bus.HasEvent(criteria.DataType))
                {
                    results.Add(new SearchResultDto(
                        frame, 
                        ticks, 
                        Entity.Null, // No specific entity correlation without payload inspection
                        $"{criteria.DataType.Name} Occurred"));
                }
            };
        }

        // Branch 2: Payload Property Evaluation
        var evaluator = new PropertyEvaluator(editService, criteria.DataType, criteria.PropertyPath);
    
        if (criteria.DataType.IsValueType)
        {
            return (EventScannerDelegate)Delegate.CreateDelegate(typeof(EventScannerDelegate), 
                Activator.CreateInstance(
                    typeof(FastEventScanner<>).MakeGenericType(criteria.DataType), 
                    evaluator, criteria)!, 
                "Scan");
        }
        else
        {
            return (EventScannerDelegate)Delegate.CreateDelegate(typeof(EventScannerDelegate), 
                Activator.CreateInstance(
                    typeof(ManagedEventScanner<>).MakeGenericType(criteria.DataType), 
                    evaluator, criteria)!, 
                "Scan");
        }
    }
}
```

3\. UI Layout Adjustments

In the `ReplaySearchPanel`, when the user selects "Transient Event" mode, add a toggle for "Any Occurrence". When checked, the UI completely hides the `StructEdit` dynamic criteria block.

```
[Replay Search Window]
+-----------------------------------------------------------------------+
| Search Mode:  ( ) Component Mutation  (x) Transient Event  ( ) Lifecycle  |
+-----------------------------------------------------------------------+
| Target Definition                                                     |
|-----------------------------------------------------------------------|
| Event Type:      [ WeaponFireIntent ˅ ]                               |
| Match Condition: (x) Any Occurrence   ( ) Specific Property Value     |
+-----------------------------------------------------------------------+
| [ Execute Search ]              | Status: 4 frames found.             |
+-----------------------------------------------------------------------+
| Search Results                                                        |
|------------------|-----------------------------------|----------------|
| Frame            | Event Payload Value               | Related Entity |
|------------------|-----------------------------------|----------------|
| [Frame 1420]     | WeaponFireIntent Occurred         | -              |
| [Frame 3005]     | WeaponFireIntent Occurred         | -              |
+-----------------------------------------------------------------------+
```

By decoupling the presence check from the `bus.Read<T>()` and `bus.ReadManaged<T>()` extraction routines, a full-recording search for "Did a `DetonationNotification` occur?" executes in milliseconds, safely skipping thousands of frames where the event hash is absent from the bus state.

the event type combo should integrate an edit field as the first item for interactive substring based filtering of event names (as there could be many)

To implement an interactive substring filter inside the ImGui combo box, you must strictly separate the transient UI filtering state from the underlying search domain model, while ensuring the string matching avoids allocations during the render loop.

Because the total number of registered event types is static for the lifetime of the application run, retrieving the available types must be cached exactly once when the search mode switches, rather than polling the engine registry on every frame.

Here is the architectural implementation for the filtered combo box.

1\. Transient UI State & Caching

Add the filter buffer and the cached event type list to the `ReplaySearchPanel`. The `_availableEventTypes` list must be populated only when the user selects the "Transient Event" mode.

```
public sealed class ReplaySearchPanel
{
    // Transient UI state for the combo box filter
    private string _eventTypeFilter = string.Empty;
    
    // Cached domain data to prevent per-frame reflection/registry polling
    private IReadOnlyList<Type> _availableEventTypes = Array.Empty<Type>();
    
    private void SetSearchMode(SearchTargetType newMode)
    {
        _criteria.TargetType = newMode;
        
        if (newMode == SearchTargetType.Event)
        {
            // Populate once. EventTypeRegistry (or equivalent Fdp registry) 
            // holds all statically discovered event structs/classes.
            _availableEventTypes = EventTypeRegistry.GetAllTypes().ToList();
            _eventTypeFilter = string.Empty;
        }
    }
}
```

2\. ImGui Combo Box Implementation

ImGui natively supports embedding standard widgets inside the `BeginCombo` / `EndCombo` block. By placing an `InputTextWithHint` at the top of the combo layout and iterating the cached list with an `OrdinalIgnoreCase` check, the layout remains completely immediate-mode without requiring custom widget state machines.

```
private void DrawEventTypeSelector()
{
    string preview = _criteria.DataType?.Name ?? "(Select Event Type)";

    // Standard combo box drop-down
    if (ImGuiApi.BeginCombo("Event Type", preview))
    {
        // 1. Interactive filter field as the first item
        ImGuiApi.SetNextItemWidth(-float.Epsilon); // Span full width of the popup
        ImGuiApi.InputTextWithHint("##EventFilter", "Type to filter...", ref _eventTypeFilter, 128);
        
        ImGuiApi.Separator();
    
        // 2. Filtered iteration over cached types
        bool hasFilter = !string.IsNullOrWhiteSpace(_eventTypeFilter);
        int matchCount = 0;
    
        foreach (Type eventType in _availableEventTypes)
        {
            // Zero-allocation substring check
            if (hasFilter && eventType.Name.IndexOf(_eventTypeFilter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
    
            matchCount++;
            bool isSelected = _criteria.DataType == eventType;
    
            if (ImGuiApi.Selectable(eventType.Name, isSelected))
            {
                // When the user clicks an item, update the domain DTO and 
                // trigger the StructEdit session rebuild
                OnTargetPropertyChanged(eventType);
            }
    
            // Ensure the currently selected item is scrolled into view when the combo opens
            if (isSelected)
            {
                ImGuiApi.SetItemDefaultFocus();
            }
        }
    
        if (matchCount == 0)
        {
            ImGuiApi.TextDisabled("No matching events found.");
        }
    
        ImGuiApi.EndCombo();
    }
}
```

Architectural Safeguards

-   **Immediate-Mode Safeties**: When `ImGuiApi.InputTextWithHint` is active, ImGui prevents the Enter or Space keys from bubbling up and closing the `BeginCombo` popup prematurely. The popup will only close when a `Selectable` is clicked or the user clicks outside the bounds.-   **No String Allocations**: `StringComparison.OrdinalIgnoreCase` executes as a direct memory scan over the UTF-16 characters without allocating lowercase string copies. This is critical when rendering a combo box with hundreds of elements at 60Hz.-   **Layout Stability**: Using `ImGuiApi.SetNextItemWidth(-float.Epsilon)` guarantees the text input dynamically stretches to exactly match the width of the combo box popup, preventing layout clipping regardless of the longest event name in the list.

good. now pls let's detail the **2\. Spatial Bounding Search** This should support editing the bounds manually as well as picking them in the map (that is currently shown) using suitable gizmos.

To implement a spatial bounding search that seamlessly bridges headless data extraction with interactive 2D map picking, the architecture must strictly separate the geographic state machine from the ImGui presentation and the Raylib gizmo layers.

You must extend the `StructEdit` property grid with a custom drawer that delegates to a dedicated picker gizmo, allowing the user to either type exact coordinates or drag a region directly on the `MapCanvas`.

Here is the architectural blueprint for this feature.

1\. Domain Model and Picker Attributes

Define the search criteria DTO. To instruct the `StructEdit` UI that this specific field can be visually authored on the map, create a marker attribute mirroring the existing `[MapPickableWorldLocation]`.

```
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class MapPickableBoundingBoxAttribute : Attribute { }

public sealed class SpatialBoundingPredicateDto : SearchPredicateDto
{
    [MapPickableBoundingBox]
    public BoundingBox2D Bounds { get; set; } = new BoundingBox2D(Vector2.Zero, Vector2.Zero);
    
    // Allows searching for the moment an entity crossed the perimeter, 
    // rather than spamming a result for every frame it was inside.
    public BoundaryEvent TriggerEvent { get; set; } = BoundaryEvent.EntryOrExit;
}

public struct BoundingBox2D
{
    public Vector2 Min;
    public Vector2 Max;
    public BoundingBox2D(Vector2 min, Vector2 max) { Min = min; Max = max; }
}

public enum BoundaryEvent { Entry, Exit, EntryOrExit }
```

2\. Map Interaction: The Bounding Box Gizmo

You cannot couple ImGui input directly to Raylib mouse coordinates. Instead, implement a `BoundingBoxPickerGizmo` using the `IEntityStatefulGizmo` interface. By setting `RequiresExclusiveFocus = true`, the `GizmoInteractionManager` will emit an `InputCaptureBinding` and route raw hardware events to this tool, preventing clicks from bleeding into underlying map layers.

This gizmo operates much like the `ModalBoxSelectionGizmo` and `RubberBandGizmo`.

```
public sealed class BoundingBoxPickerGizmo : IEntityStatefulGizmo
{
    private readonly Action<BoundingBox2D> _onComplete;
    private readonly Action _onRemove;
    
    private Vector2? _startPos;
    private Vector2 _currentPos;
    
    public bool RequiresExclusiveFocus => true;
    public bool WantsRawInput => true;
    public bool IsFocused { get; private set; }
    
    public BoundingBoxPickerGizmo(Action<BoundingBox2D> onComplete, Action onRemove)
    {
        _onComplete = onComplete;
        _onRemove = onRemove;
    }
    
    public void SetFocus(bool isFocused) => IsFocused = isFocused;
    
    public void UpdateAndDraw(float deltaTime, IDebugDrawBuilder draw)
    {
        if (!_startPos.HasValue) return;
    
        // Emit a semi-transparent box for the active drag region
        var min = Vector2.Min(_startPos.Value, _currentPos);
        var max = Vector2.Max(_startPos.Value, _currentPos);
        var extents = (max - min) * 0.5f;
        var center = min + extents;
    
        var box = DebugPrimitive.MakeBox2D(
            center, extents, new Rgba32(0, 120, 215, 200),
            fillColor: new Rgba32(0, 120, 215, 50),
            target: PipelineTarget.Map2D);
        draw.EmitRaw(in box);
    }
    
    public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
    {
        var pos2D = new Vector2(worldPos.X, worldPos.Y);
    
        if (button == MapMouseButton.Left)
        {
            if (isPressed)
            {
                _startPos = pos2D;
                _currentPos = pos2D;
            }
            else if (_startPos.HasValue)
            {
                var min = Vector2.Min(_startPos.Value, pos2D);
                var max = Vector2.Max(_startPos.Value, pos2D);
                
                _onComplete(new BoundingBox2D(min, max));
                _onRemove();
            }
        }
        else if (button == MapMouseButton.Right && isPressed)
        {
            _onRemove(); // Cancel
        }
    }
    
    public void OnDragUpdate(Vector3 worldPos) => _currentPos = new Vector2(worldPos.X, worldPos.Y);
    public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { if (key == MapKeyboardKey.Escape) _onRemove(); }
    
    public void Dispose() { }
    public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
    public void OnCommit(Vector3 worldPos) { }
    public void OnCancel() { }
    public void OnMenuAction(int actionId) { }
}
```

3\. StructEdit Integration

To render the UI, you must implement `IImGuiFieldDrawer`. This intercepts the `BoundingBox2D` field in `StructEdit` and provides both manual entry fields and the "Pick on Map" button.

You must extend the `IComponentPickerContext` interface to broker the asynchronous pick request, keying it by the `EditNode.JsonPath` so the pick survives UI rebuilds.

```
public sealed class BoundingBoxFieldDrawer : IImGuiFieldDrawer
{
    private readonly IComponentPickerContext _pickerCtx;
    public Type TargetType => typeof(BoundingBox2D);

    public BoundingBoxFieldDrawer(IComponentPickerContext pickerCtx) => _pickerCtx = pickerCtx;
    
    public bool DrawInput(ref object value, EditNodeMetadata meta)
    {
        var box = (BoundingBox2D)value;
        bool changed = false;
    
        // 1. Manual Entry
        if (ImGuiApi.DragFloat2("Min##min", ref box.Min)) changed = true;
        if (ImGuiApi.DragFloat2("Max##max", ref box.Max)) changed = true;
    
        // 2. Map Pick Binding
        var path = meta.JsonPath ?? string.Empty;
        if (_pickerCtx.IsPickPendingFor(path))
        {
            ImGuiApi.TextDisabled("[Dragging on Map...]");
        }
        else if (ImGuiApi.Button($"Pick Area##{path}"))
        {
            _pickerCtx.RequestBoundingBoxPick(path);
        }
    
        // 3. Consume Async Result
        if (_pickerCtx.TryConsumeBoundingBoxPick(path, out var pickedBox))
        {
            box = pickedBox;
            changed = true;
        }
    
        if (changed) value = box;
        return changed;
    }
}
```

When the user clicks "Pick Area", your `MapPickServiceBridge` will instantiate the `BoundingBoxPickerGizmo` via the `GlobalGizmoManager`, wait for the operator to drag the region, and fulfil the promise.

4\. Background Search Execution

The `RecordingSearchService` evaluates the bounds off the main thread. To avoid O(N) iterations over stationary entities, the engine must leverage `QueryDelta` on the `SimTransform` component. A state machine tracks which entities are currently inside the zone to accurately yield boundary crossings.

```
public IReadOnlyList<SearchResultDto> ExecuteSpatialSearch(string fdpPath, SpatialBoundingPredicateDto criteria)
{
    var results = new List<SearchResultDto>();
    var insideZone = new HashSet<Entity>();

    using var context = new ReplayBrowserContext();
    context.LoadRecording(fdpPath);
    
    var query = context.SandboxRepo.Query().With<SimTransform>().Build();
    uint lastScannedVersion = 0;
    
    while (context.Playback.StepForward(context.SandboxRepo))
    {
        int currentFrame = context.Playback.CurrentFrame;
        long wallTicks = context.Playback.GetFrameMetadata(currentFrame).WallClockTicks;
    
        // Only evaluate entities whose physics/kinematics updated this frame
        foreach (var entity in context.SandboxRepo.QueryDelta(query, lastScannedVersion))
        {
            var pos = context.SandboxRepo.GetComponentRO<SimTransform>(entity).Position;
            bool isInside = pos.X >= criteria.Bounds.Min.X && pos.X <= criteria.Bounds.Max.X &&
                            pos.Y >= criteria.Bounds.Min.Y && pos.Y <= criteria.Bounds.Max.Y;
    
            bool wasInside = insideZone.Contains(entity);
    
            if (isInside && !wasInside)
            {
                insideZone.Add(entity);
                if (criteria.TriggerEvent is BoundaryEvent.Entry or BoundaryEvent.EntryOrExit)
                    results.Add(new SearchResultDto(currentFrame, wallTicks, entity, "Entered Zone"));
            }
            else if (!isInside && wasInside)
            {
                insideZone.Remove(entity);
                if (criteria.TriggerEvent is BoundaryEvent.Exit or BoundaryEvent.EntryOrExit)
                    results.Add(new SearchResultDto(currentFrame, wallTicks, entity, "Exited Zone"));
            }
        }
    
        lastScannedVersion = context.SandboxRepo.GlobalVersion;
    }
    
    return results;
}
```

This enforces strict separation of concerns. `StructEdit` manages the DTO layout, the Gizmo handles input capture and drawing, and the headless `QueryDelta` scanner processes thousands of frames per second using strictly unmanaged `SimTransform` memory. Clicking any result in the grid will instantly deep-link the timeline to the exact frame the entity breached the perimeter.

the spatial search should support both possibilities like 'entered area', 'left area'

The architectural foundation for this capability relies on treating spatial boundaries as edge-triggered events rather than continuous state evaluations. By applying an inside/outside state machine during the extraction loop, you can explicitly distinguish between a perimeter breach (Entry) and a perimeter departure (Exit).

Because we built the search criteria on top of `StructEdit`, exposing this choice to the operator requires no custom UI code.

Here is the architectural implementation for edge-triggered spatial searches.

1\. The Trigger Domain Model

The `SpatialBoundingPredicateDto` must expose a `BoundaryEvent` enumeration. `StructEdit` will automatically reflect this enum into an ImGui combo box, allowing the operator to select "Entry", "Exit", or "EntryOrExit".

```
public enum BoundaryEvent { Entry, Exit, EntryOrExit }

public sealed class SpatialBoundingPredicateDto : SearchPredicateDto
{
    [MapPickableBoundingBox]
    public BoundingBox2D Bounds { get; set; } = new BoundingBox2D(Vector2.Zero, Vector2.Zero);
    
    public BoundaryEvent TriggerEvent { get; set; } = BoundaryEvent.EntryOrExit;
}
```

2\. Edge-Detection State Machine

Inside the `RecordingSearchService`, you cannot simply check if `SimTransform.Position` is inside the box on the current frame. You must maintain a `HashSet<Entity>` representing the "previously inside" state. The transition between `wasInside` and `isInside` determines the specific event type.

Using `QueryDelta`, we only evaluate entities whose kinematic state mutated, skipping stationary units.

```
public IReadOnlyList<SearchResultDto> ExecuteSpatialSearch(string fdpPath, SpatialBoundingPredicateDto criteria)
{
    var results = new List<SearchResultDto>();
    
    // Tracks entities currently inside the zone to calculate Entry/Exit edges
    var insideZone = new HashSet<Entity>();
    
    using var context = new ReplayBrowserContext();
    context.LoadRecording(fdpPath);
    
    var query = context.SandboxRepo.Query().With<SimTransform>().Build();
    uint lastScannedVersion = 0;
    
    while (context.Playback.StepForward(context.SandboxRepo))
    {
        int currentFrame = context.Playback.CurrentFrame;
        long wallTicks = context.Playback.GetFrameMetadata(currentFrame).WallClockTicks;
    
        // O(chunks) filtering: strictly yields entities whose position mutated
        foreach (var entity in context.SandboxRepo.QueryDelta(query, lastScannedVersion))
        {
            var pos = context.SandboxRepo.GetComponentRO<SimTransform>(entity).Position;
            
            bool isInside = pos.X >= criteria.Bounds.Min.X && pos.X <= criteria.Bounds.Max.X &&
                            pos.Y >= criteria.Bounds.Min.Y && pos.Y <= criteria.Bounds.Max.Y;
    
            bool wasInside = insideZone.Contains(entity);
    
            // Edge Detection: Entry
            if (isInside && !wasInside)
            {
                insideZone.Add(entity);
                if (criteria.TriggerEvent is BoundaryEvent.Entry or BoundaryEvent.EntryOrExit)
                {
                    results.Add(new SearchResultDto(currentFrame, wallTicks, entity, "Entered Area"));
                }
            }
            // Edge Detection: Exit
            else if (!isInside && wasInside)
            {
                insideZone.Remove(entity);
                if (criteria.TriggerEvent is BoundaryEvent.Exit or BoundaryEvent.EntryOrExit)
                {
                    results.Add(new SearchResultDto(currentFrame, wallTicks, entity, "Left Area"));
                }
            }
        }
    
        // Garbage Collection: Remove destroyed entities from the state machine
        var destructionLog = context.SandboxRepo.GetDestructionLog();
        foreach (var deadEntity in destructionLog)
        {
            insideZone.Remove(deadEntity);
        }
        context.SandboxRepo.ClearDestructionLog();
    
        lastScannedVersion = context.SandboxRepo.GlobalVersion;
    }
    
    return results;
}
```

3\. Visual Layout

Because the `SpatialBoundingPredicateDto` inherits from the base `SearchPredicateDto`, the `ComponentEditDrawer` projects the enum directly into the property grid.

```
[Replay Search Window]
+-----------------------------------------------------------------------+
| Search Mode:  ( ) Component  ( ) Event  ( ) Lifecycle  (x) Spatial    |
+-----------------------------------------------------------------------+
| Search Criteria (StructEdit)                                          |
|---------------------------------|-------------------------------------|
| Parameter                       | Value                               |
|---------------------------------|-------------------------------------|
| Bounds                          | [ Pick Area ]                       |
|   Min                           | [ 100.000      ] [ 100.000      ]   |
|   Max                           | [ 500.000      ] [ 500.000      ]   |
| Trigger Event                   | [ Entry ˅                       ]   |
+-----------------------------------------------------------------------+
| [ Execute Search ]              | Status: 2 perimeter breaches.       |
+-----------------------------------------------------------------------+
| Search Results                                                        |
|------------------|-------------------|--------------------------------|
| Frame            | Entity            | Event Type                     |
|------------------|-------------------|--------------------------------|
| [Frame 1420]     | [42, v3]          | Entered Area                   |
| [Frame 3005]     | [85, v1]          | Entered Area                   |
+-----------------------------------------------------------------------+
```

Architectural Benefits

-   **State Cleanliness:** By hooking into `context.SandboxRepo.GetDestructionLog()`, the `insideZone` hash set never leaks memory if an entity is destroyed while inside the bounding box.-   **Noise Reduction:** Tracking the boolean edge explicitly prevents the search from returning thousands of duplicate "Inside Area" results for every frame a vehicle spends parked within the boundary.-   **Extensibility:** The exact same inside/outside state machine can be extended to support circular radius checks by swapping the AABB evaluation for a `Vector2.DistanceSquared` check, mapped to a `SpatialRadiusPredicateDto`.

good! now pls detail **3\. Structural Modification Search**

To implement the Structural Modification Search, we must exploit the engine's `EntityHeader` memory layout. Structural changes—adding or removing components—are computationally cheap to detect because they directly mutate the 256-bit `ComponentMask` and update the `LastChangeTick` in the entity's header.

By monitoring these header mutations, we avoid evaluating component payloads entirely, yielding an extraction loop that processes thousands of frames per second.

Here is the architectural implementation for the Structural Modification Search.

1\. Structural Search Domain Model

Extend the search predicates with a dedicated DTO for structural changes. `StructEdit` will automatically generate a combo box for the `ModificationType` enum.

```
public enum StructuralModification { Added, Removed, AnyChange }

public sealed class StructuralPredicateDto : SearchPredicateDto
{
    public StructuralModification ModificationType { get; set; } = StructuralModification.Added;
}
```

Because the target `ComponentType` is already captured by the base `SearchCriteria` model from our previous design, the headless search engine only needs to resolve this type into its global integer ID.

2\. The O(1) State Machine (`RecordingSearchService`)

The search engine tracks component presence across frames using a `HashSet<Entity>`. By iterating the `EntityIndex` directly and filtering by `LastChangeTick`, we bypass the component tables entirely.

```
public IReadOnlyList<SearchResultDto> ExecuteStructuralSearch(string fdpPath, SearchCriteria criteria, StructuralPredicateDto predicate)
{
    var results = new List<SearchResultDto>();
    
    // Resolve the O(1) bit index for the target component type
    int targetTypeId = ComponentTypeRegistry.GetId(criteria.DataType);
    if (targetTypeId < 0) return results;
    
    // State machine tracking entities that currently possess the component
    var hasComponent = new HashSet<Entity>();
    
    using var context = new ReplayBrowserContext();
    context.LoadRecording(fdpPath);
    
    var entityIndex = context.SandboxRepo.GetEntityIndex();
    uint lastScannedVersion = 0;
    
    while (context.Playback.StepForward(context.SandboxRepo))
    {
        int currentFrame = context.Playback.CurrentFrame;
        long wallTicks = context.Playback.GetFrameMetadata(currentFrame).WallClockTicks;
        int maxIndex = entityIndex.MaxIssuedIndex;
    
        // O(N) over issued indices, but extremely fast due to contiguous memory layout
        for (int i = 0; i <= maxIndex; i++)
        {
            ref var header = ref entityIndex.GetHeader(i);
            
            if (!header.IsActive) continue;
    
            // Structural bypass: skip entities whose component mask has not been touched
            if (header.LastChangeTick <= lastScannedVersion) continue;
    
            var entity = new Entity(i, header.Generation);
            
            // O(1) bitwise lookup against the AVX2-optimized BitMask256
            bool isPresent = header.ComponentMask.IsSet(targetTypeId);
            bool wasPresent = hasComponent.Contains(entity);
    
            if (isPresent && !wasPresent)
            {
                hasComponent.Add(entity);
                if (predicate.ModificationType is StructuralModification.Added or StructuralModification.AnyChange)
                {
                    results.Add(new SearchResultDto(currentFrame, wallTicks, entity, $"Gained {criteria.DataType.Name}"));
                }
            }
            else if (!isPresent && wasPresent)
            {
                hasComponent.Remove(entity);
                if (predicate.ModificationType is StructuralModification.Removed or StructuralModification.AnyChange)
                {
                    results.Add(new SearchResultDto(currentFrame, wallTicks, entity, $"Lost {criteria.DataType.Name}"));
                }
            }
        }
    
        // Garbage Collection: Prevent memory leaks from entities destroyed while carrying the component
        var destructionLog = context.SandboxRepo.GetDestructionLog();
        foreach (var deadEntity in destructionLog)
        {
            if (hasComponent.Remove(deadEntity) && predicate.ModificationType is StructuralModification.Removed or StructuralModification.AnyChange)
            {
                results.Add(new SearchResultDto(currentFrame, wallTicks, deadEntity, $"Lost {criteria.DataType.Name} (Destroyed)"));
            }
        }
        context.SandboxRepo.ClearDestructionLog();
    
        lastScannedVersion = context.SandboxRepo.GlobalVersion;
    }
    
    return results;
}
```

3\. Visual Layout

When the user selects "Structural Modification" mode, the dynamic parameter region renders the `ModificationType` enum via the `ComponentEditDrawer`.

```
[Replay Search Window]
+-----------------------------------------------------------------------+
| Search Mode:  ( ) Value  ( ) Event  ( ) Lifecycle  (x) Structural     |
+-----------------------------------------------------------------------+
| Target Definition                                                     |
|-----------------------------------------------------------------------|
| Component:    [ IsEmbarkedTag ˅ ]                                     |
| Entity Mask:  [ (Any)           ]                                     |
+-----------------------------------------------------------------------+
| Search Criteria (StructEdit)                                          |
|---------------------------------|-------------------------------------|
| Parameter                       | Value                               |
|---------------------------------|-------------------------------------|
| Modification Type               | [ Added ˅                       ]   |
+-----------------------------------------------------------------------+
| [ Execute Search ]              | Status: 12 additions found.         |
+-----------------------------------------------------------------------+
| Search Results                                                        |
|------------------|-------------------|--------------------------------|
| Frame            | Entity            | Event Type                     |
|------------------|-------------------|--------------------------------|
| [Frame 805]      | [42, v3]          | Gained IsEmbarkedTag           |
| [Frame 1420]     | [85, v1]          | Gained IsEmbarkedTag           |
+-----------------------------------------------------------------------+
```

Architectural Benefits

-   **No Reflection or Struct Parsing:** By resolving the target component to its `typeId`, the extraction loop evaluates `ComponentMask.IsSet(typeId)`, operating purely as a bitwise boolean check.-   **Structural Bypassing:** The engine stamps `LastChangeTick` into the `EntityHeader` exclusively when components are added or removed. By checking `LastChangeTick <= lastScannedVersion`, the search bypasses tens of thousands of entities whose structural topology remained stable during the frame.-   **Implicit Edge Case Handling:** If a vehicle is destroyed, all embarked infantry natively receive a `DestructionOrder` or a command to disembark. Hooking into `GetDestructionLog()` cleanly resolves scenarios where a component was "lost" simply because the entity carrying it ceased to exist.

again the cpomponent type combo should support embedded substring filter

To implement the embedded substring filter for the ECS component type selector, you must apply the exact same transient caching pattern used for the event scanner. Polling the global component registry every frame inside an immediate-mode GUI loop violates performance constraints, as registry access mechanisms often incur locking or reflection overhead.

The architectural solution requires caching the output of `ComponentTypeRegistry.GetAllTypes()` strictly when the search mode changes to `SearchTargetType.Component` or `SearchTargetType.Structural`.

Here is the implementation.

1\. Transient UI State & Caching

Add the filter buffer and the component type cache to the `ReplaySearchPanel`.

```
public sealed class ReplaySearchPanel
{
    // Transient UI state for the combo box filter
    private string _componentTypeFilter = string.Empty;
    
    // Cached domain data to prevent per-frame registry polling
    private IReadOnlyList<Type> _availableComponentTypes = Array.Empty<Type>();
    
    private void SetSearchMode(SearchTargetType newMode)
    {
        _criteria.TargetType = newMode;
        
        if (newMode == SearchTargetType.Component || newMode == SearchTargetType.Structural)
        {
            // Populate once. ComponentTypeRegistry holds all statically discovered components [1].
            _availableComponentTypes = ComponentTypeRegistry.GetAllTypes().ToList();
            _componentTypeFilter = string.Empty;
        }
        else if (newMode == SearchTargetType.Event)
        {
            // Existing event population logic...
        }
    }
}
```

2\. Embedded Filter ImGui Layout

Inside your target definition region, replace the standard combo box with the filtered variant. By leveraging `SetNextItemWidth(-float.Epsilon)`, the text input will strictly conform to the dynamic width of the combo popup.

```
private void DrawComponentTypeSelector()
{
    string preview = _criteria.DataType?.Name ?? "(Select Component)";

    if (ImGuiApi.BeginCombo("Component", preview))
    {
        // 1. Interactive filter field as the first item
        ImGuiApi.SetNextItemWidth(-float.Epsilon);
        ImGuiApi.InputTextWithHint("##CompFilter", "Type to filter...", ref _componentTypeFilter, 128);
        
        ImGuiApi.Separator();
    
        // 2. Filtered iteration over cached types
        bool hasFilter = !string.IsNullOrWhiteSpace(_componentTypeFilter);
        int matchCount = 0;
    
        foreach (Type compType in _availableComponentTypes)
        {
            // Zero-allocation substring check via OrdinalIgnoreCase
            if (hasFilter && compType.Name.IndexOf(_componentTypeFilter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
    
            matchCount++;
            bool isSelected = _criteria.DataType == compType;
    
            if (ImGuiApi.Selectable(compType.Name, isSelected))
            {
                // Update the domain DTO and trigger UI rebuild
                _criteria.DataType = compType;
                OnTargetPropertyChanged(compType);
            }
    
            // Ensure the currently selected item is scrolled into view when the combo opens
            if (isSelected)
            {
                ImGuiApi.SetItemDefaultFocus();
            }
        }
    
        if (matchCount == 0)
        {
            ImGuiApi.TextDisabled("No matching components found.");
        }
    
        ImGuiApi.EndCombo();
    }
}
```

Architectural Benefits

-   **Deterministic Hot Path**: Iterating over `_availableComponentTypes` and executing `StringComparison.OrdinalIgnoreCase` guarantees zero heap allocations during the 60Hz ImGui draw loop.-   **Registry Decoupling**: By fetching the types into a flat `List<Type>` via `ComponentTypeRegistry.GetAllTypes()` exactly once upon mode switch, you decouple the UI presentation from the engine's internal type-resolution locks.-   **Widget Reusability**: Because the `ImGuiApi.BeginCombo` block is fundamentally identical to the one built for `EventTypeRegistry`, this logic can be trivially refactored into a generic `ImGuiFilteredTypeSelector.Draw(ref Type selectedType, IReadOnlyList<Type> cache, ref string filter)` utility if other diagnostic windows require it.

good! now pls detail the **4\. Cognitive and Behavioral Transition Search** This is extremely usefull feature. Instead of working with behavior hash directly, the user must be able to pick from a list of behavior names. Checking behavior type seems to be just a special case of generic component property search, right? same for `NavigationStatus.Result` flipped to `FailedBlocked`. So we could reuse the existing search features! But if we need these two together, we need some simple builder of logical concatenation of multiple conditions. How to approach this?

You are correct that evaluating a behavioral shift (`BehaviorState.ActiveBehaviorHash`) or a pathfinding failure (`NavigationStatus.Result`) is fundamentally just a generic component property search. Building custom, hardcoded search paths for these fields would violate the separation of concerns.

To support compound queries (e.g., finding the exact frame where an entity is in the `Combat` behavior **AND** its navigation result flips to `FailedBlocked`), we must upgrade the search domain to support the **Composite Pattern** and update the extraction loop to evaluate multiple `StructEdit` memory bindings simultaneously.

To solve the UX requirement of picking human-readable behavior names instead of raw integer hashes, we will inject a custom `IImGuiFieldDrawer` that maps the string selections to the underlying `int` hashes via the engine's `BehaviorRegistry`.

Here is the architectural implementation for the Compound Property Search.

1\. Composite Query Domain Model

We elevate the search criteria into a recursive structure. The `CompoundPredicateDto` acts as a logical container (AND/OR) that aggregates multiple standard predicates.

```
public enum LogicalOperator { And, Or }

public sealed class CompoundPredicateDto : SearchPredicateDto
{
    public LogicalOperator Operator { get; set; } = LogicalOperator.And;
    
    // StructEdit's DynamicArrayBinding automatically handles the UI for adding/removing items from this list
    public List<SearchPredicateDto> Conditions { get; set; } = new();
}

// Specialised DTO for Behavior hashes to trigger the custom UI drawer
public sealed class BehaviorHashPredicateDto : SearchPredicateDto
{
    [BehaviorHashPicker] // Custom attribute to trigger the custom ImGui drawer
    public int TargetBehaviorHash { get; set; }
}
```

2\. Behavior Name Resolution (UI Layer)

The user must never see the raw integer hashes (e.g., `3001` for `MoveToLocation`). We implement an `IImGuiFieldDrawer` plugin for `StructEdit` that intercepts fields marked with `[BehaviorHashPicker]`. It queries the `BehaviorRegistry` to present a sorted, filterable combo box of behavior names.

```
public sealed class BehaviorHashFieldDrawer : IImGuiFieldDrawer
{
    private readonly BehaviorRegistry _registry;
    private IReadOnlyList<string>? _cachedNames;
    private string _filter = string.Empty;

    public Type TargetType => typeof(int); // The underlying data is still an integer hash
    
    public BehaviorHashFieldDrawer(BehaviorRegistry registry)
    {
        _registry = registry;
    }
    
    public bool DrawInput(ref object value, EditNodeMetadata meta)
    {
        // Only intercept properties decorated with our custom attribute
        if (!meta.CustomAttributes.Any(a => a is BehaviorHashPickerAttribute))
            return false;
    
        int currentHash = (int)value;
        _cachedNames ??= _registry.GetRegisteredNames().OrderBy(n => n).ToList(); [4]
    
        string preview = currentHash == 0 ? "Idle" : 
            (_registry.TryGetName(currentHash, out string? name) ? name : currentHash.ToString()); [4]
    
        bool changed = false;
    
        if (ImGuiApi.BeginCombo("Behavior", preview))
        {
            ImGuiApi.InputTextWithHint("##Filter", "Type to filter...", ref _filter, 64);
            ImGuiApi.Separator();
    
            foreach (string bName in _cachedNames)
            {
                if (!string.IsNullOrEmpty(_filter) && !bName.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                    continue;
    
                _registry.TryGetId(bName, out int hash); [4]
                if (ImGuiApi.Selectable(bName, currentHash == hash))
                {
                    value = hash;
                    changed = true;
                }
            }
            ImGuiApi.EndCombo();
        }
    
        return changed;
    }
}
```

3\. Headless Compound Execution Engine

The `RecordingSearchService` must now compile a list of `PropertyEvaluator` instances.

For an `AND` operator, the engine can optimize the search dramatically by feeding all required component types into a single `EntityQuery`. `QueryDelta` will completely bypass any 64KB memory chunk that does not contain _all_ the required components, or where none of them mutated.

```
public IReadOnlyList<SearchResultDto> ExecuteCompoundSearch(string fdpPath, CompoundPredicateDto compoundQuery)
{
    var results = new List<SearchResultDto>();
    using var context = new ReplayBrowserContext();
    context.LoadRecording(fdpPath);

    var editService = new ComponentEditServiceBuilder().Build();
    
    // 1. Compile evaluators and build the strict ECS query
    var queryBuilder = context.SandboxRepo.Query();
    var evaluators = new List<(SearchPredicateDto predicate, PropertyEvaluator evaluator)>();
    
    foreach (var condition in compoundQuery.Conditions)
    {
        // Extract component type from the condition (e.g. BehaviorState or NavigationStatus)
        Type componentType = ResolveComponentType(condition);
        
        // Logical AND optimization: enforce that the entity possesses every component in the query
        if (compoundQuery.Operator == LogicalOperator.And)
        {
            queryBuilder.With(componentType); [6] // Enforces presence in the chunk
        }
        
        evaluators.Add((condition, new PropertyEvaluator(editService, componentType, condition.PropertyPath)));
    }
    
    var compiledQuery = queryBuilder.Build(); [6]
    uint lastScannedVersion = 0;
    
    // 2. Extraction Loop
    while (context.Playback.StepForward(context.SandboxRepo))
    {
        int currentFrame = context.Playback.CurrentFrame;
        long wallTicks = context.Playback.GetFrameMetadata(currentFrame).WallClockTicks;
    
        // Yields only entities matching the required masks whose chunks mutated [6, 7]
        foreach (var entity in context.SandboxRepo.QueryDelta(compiledQuery, lastScannedVersion)) [6]
        {
            bool match = EvaluateCompoundLogic(compoundQuery.Operator, evaluators, context.SandboxRepo, entity);
    
            if (match)
            {
                results.Add(new SearchResultDto(currentFrame, wallTicks, entity, "Compound Condition Met"));
            }
        }
        lastScannedVersion = context.SandboxRepo.GlobalVersion;
    }
    
    return results;
}

private bool EvaluateCompoundLogic(
    LogicalOperator op, 
    List<(SearchPredicateDto predicate, PropertyEvaluator evaluator)> evaluators, 
    EntityRepository repo, 
    Entity entity)
{
    foreach (var (predicate, evaluator) in evaluators)
    {
        // Extract raw memory using the specific component type required by this evaluator
        object componentObj = repo.GetRawObject(entity, ResolveComponentType(predicate));
        string currentValue = evaluator.GetValueAsString(componentObj);
        
        bool conditionMet = EvaluateCondition(currentValue, predicate.Operator, predicate.TargetValue);
    
        if (op == LogicalOperator.And && !conditionMet) return false;
        if (op == LogicalOperator.Or && conditionMet) return true;
    }
    
    return op == LogicalOperator.And;
}
```

4\. ImGui Rule Builder Layout

Because `CompoundPredicateDto.Conditions` is a `List<SearchPredicateDto>`, opening this DTO in `StructEdit` automatically leverages `DynamicArrayBinding`. The `ComponentEditDrawer` will natively render `[+ Add]` buttons and `[X]` delete buttons for the list elements without requiring custom ImGui logic.

```
[Replay Search Window]
+-----------------------------------------------------------------------+
| Search Mode:  ( ) Value  ( ) Event  ( ) Lifecycle  (x) Compound       |
+-----------------------------------------------------------------------+
| Search Criteria (StructEdit)                                          |
|---------------------------------|-------------------------------------|
| Parameter                       | Value                               |
|---------------------------------|-------------------------------------|
| Operator                        | [ AND ˅                           ] |
| Conditions                      | [11]                        [+ Add ] |
|   ▼                          |                                 [X] |
|       Component                 | [ BehaviorState ˅                 ] |
|       Property                  | [ ActiveBehaviorHash              ] |
|       Target Behavior           | [ Combat ˅                        ] | // Rendered by BehaviorHashFieldDrawer
|   ▼ [12]                         |                                 [X] |
|       Component                 | [ NavigationStatus ˅              ] |
|       Property                  | [ Result                          ] |
|       Condition                 | [ Equals ˅                        ] |
|       Value                     | [ FailedBlocked ˅                 ] | // Rendered via native Enum reflection
+-----------------------------------------------------------------------+
| [ Execute Search ]              | Status: 4 instances found.          |
+-----------------------------------------------------------------------+
| Search Results                                                        |
|------------------|-------------------|--------------------------------|
| Frame            | Entity            | Event Type                     |
|------------------|-------------------|--------------------------------|
| [Frame 1420]     | [42, v3]          | Compound Condition Met         |
+-----------------------------------------------------------------------+
```

Architectural Benefits

-   **O(1) Memory Skipping:** By logically ANDing the component requirements during the initial `queryBuilder.With(T)` phase, the `QueryDelta` enumerator drops entire 64KB blocks of entities that don't have both `BehaviorState` and `NavigationStatus`, making complex multi-variable queries virtually as fast as single-variable queries.-   **Type Safety via Source of Truth:** `NavigationResult` is a standard enum. Because we push the query definition through `StructEdit`, `FailedBlocked` populates as a dropdown automatically based on the CLR type without hardcoded string parsing.-   **Infinite Nesting:** Because `CompoundPredicateDto` inherits from `SearchPredicateDto`, the array can contain other `CompoundPredicateDto` instances, allowing users to build complex `(A AND B) OR C` logic trees with zero additional UI code.

i think we should be able to nest the conditions, like A && (B || C). And because these search are often repetitive, it sould be nice if could save the compoud condition to a json file and restore it later by loading from a file.

To achieve infinite nesting and serialization without writing custom parsing logic, the architecture must formalize the Composite pattern down to the target-definition level and leverage the existing `StructEdit.Json` serialization pipeline.

Because `StructEdit` natively supports polymorphic collections (via `[FdpPolymorphicType]` or its internal type resolution) and nested document trees, pushing the component targeting into the leaf nodes gives us both requirements essentially for free.

Here is the architectural implementation.

1\. Unified Composite Domain Model

Move the target definition (`ComponentType`, `PropertyPath`) out of the global context and into a specific leaf DTO. Every node—whether a logical group, a property check, or a structural check—inherits from `SearchPredicateDto`.

```
public abstract class SearchPredicateDto { }

public enum LogicalOperator { And, Or }

public sealed class CompoundPredicateDto : SearchPredicateDto
{
    public LogicalOperator Operator { get; set; } = LogicalOperator.And;
    
    // StructEdit's DynamicArrayBinding automatically handles the UI for adding/removing 
    // polymorphic items from this list.
    public List<SearchPredicateDto> Conditions { get; set; } = new();
}

public sealed class PropertyMatchDto : SearchPredicateDto
{
    public Type ComponentType { get; set; } = typeof(SimTransform);
    public string PropertyPath { get; set; } = string.Empty;
    public SearchOperator Operator { get; set; } = SearchOperator.Equals;
    public string TargetValue { get; set; } = string.Empty;
}

// Existing structural/lifecycle DTOs also inherit from SearchPredicateDto...
public sealed class StructuralPredicateDto : SearchPredicateDto { /* ... */ }
```

2\. Recursive Delegate Compilation (`RecordingSearchService`)

To evaluate an infinitely nested tree without boxing or traversing object graphs per frame, the search engine must compile the DTO tree into a single, chained `Func<EntityRepository, Entity, bool>` delegate before the fast-forward loop begins.

This compilation step statically binds the `PropertyEvaluator` instances into the closures.

```
public IReadOnlyList<SearchResultDto> ExecuteCompoundSearch(string fdpPath, CompoundPredicateDto rootPredicate)
{
    var results = new List<SearchResultDto>();
    using var context = new ReplayBrowserContext();
    context.LoadRecording(fdpPath);

    var editService = new ComponentEditServiceBuilder().Build();
    
    // 1. Compile the recursive evaluation tree into a single delegate
    Func<EntityRepository, Entity, bool> evaluationChain = CompilePredicate(rootPredicate, editService);
    
    // 2. Extract mandatory components for chunk-skipping optimization
    var queryBuilder = context.SandboxRepo.Query();
    ExtractMandatoryComponents(rootPredicate, queryBuilder);
    var compiledQuery = queryBuilder.Build();
    
    uint lastScannedVersion = 0;
    
    // 3. Execution Loop
    while (context.Playback.StepForward(context.SandboxRepo))
    {
        int currentFrame = context.Playback.CurrentFrame;
        long wallTicks = context.Playback.GetFrameMetadata(currentFrame).WallClockTicks;
    
        foreach (var entity in context.SandboxRepo.QueryDelta(compiledQuery, lastScannedVersion))
        {
            if (evaluationChain(context.SandboxRepo, entity))
            {
                results.Add(new SearchResultDto(currentFrame, wallTicks, entity, "Compound Condition Met"));
            }
        }
        lastScannedVersion = context.SandboxRepo.GlobalVersion;
    }
    
    return results;
}

private Func<EntityRepository, Entity, bool> CompilePredicate(SearchPredicateDto node, IComponentEditService editService)
{
    if (node is CompoundPredicateDto compound)
    {
        var childDelegates = compound.Conditions.Select(c => CompilePredicate(c, editService)).ToArray();
        
        return (repo, entity) =>
        {
            if (compound.Operator == LogicalOperator.And)
            {
                foreach (var child in childDelegates)
                    if (!child(repo, entity)) return false;
                return true;
            }
            else // OR
            {
                foreach (var child in childDelegates)
                    if (child(repo, entity)) return true;
                return false;
            }
        };
    }
    else if (node is PropertyMatchDto propMatch)
    {
        var evaluator = new PropertyEvaluator(editService, propMatch.ComponentType, propMatch.PropertyPath);
        return (repo, entity) =>
        {
            if (!repo.HasComponentByTypeId(entity, ComponentTypeRegistry.GetId(propMatch.ComponentType))) 
                return false;
    
            object componentObj = repo.GetRawObject(entity, propMatch.ComponentType);
            string currentValue = evaluator.GetValueAsString(componentObj);
            return EvaluateCondition(currentValue, propMatch.Operator, propMatch.TargetValue);
        };
    }
    
    return (repo, entity) => false;
}

private void ExtractMandatoryComponents(SearchPredicateDto node, QueryBuilder queryBuilder)
{
    // If the root is AND, any component required directly at this level is mandatory 
    // for the entire chunk, allowing QueryDelta to aggressively skip memory blocks.
    if (node is CompoundPredicateDto compound && compound.Operator == LogicalOperator.And)
    {
        foreach (var child in compound.Conditions)
        {
            if (child is PropertyMatchDto prop) queryBuilder.With(prop.ComponentType);
            else ExtractMandatoryComponents(child, queryBuilder);
        }
    }
}
```

3\. StructEdit JSON Persistence (UI Layer)

Because the UI is already rendering the DTOs via a `StructEdit` `IEditSession`, serializing and deserializing the compound tree is handled entirely by the `StructEdit.Json` extensions.

Add the persistence controls to the top of the `ReplaySearchPanel`.

```
public sealed class ReplaySearchPanel
{
    private IEditSession? _predicateSession;

    public void DrawContent()
    {
        DrawToolbar();
        
        // Existing StructEdit rendering
        if (_predicateSession != null)
        {
            if (_predicateSession.RebuildState == EditRebuildState.RebuildRequired)
                _predicateSession.RebuildDocument();
    
            _predicateDrawer?.DrawEditNode(_predicateSession.Document.Root);
        }
        
        // ... Execute Search Button ...
    }
    
    private void DrawToolbar()
    {
        if (ImGuiApi.Button("Save Preset..."))
        {
            // _predicateSession.ToJson() serializes the entire nested DTO tree, 
            // including all dynamically added polymorphic children.
            string json = _predicateSession!.ToJson(); [1]
            File.WriteAllText("search_preset.json", json);
        }
        
        ImGuiApi.SameLine();
        
        if (ImGuiApi.Button("Load Preset..."))
        {
            if (File.Exists("search_preset.json"))
            {
                string json = File.ReadAllText("search_preset.json");
                
                // _predicateSession.LoadJson applies the saved JSON back into the edit buffer.
                _predicateSession!.LoadJson(json); [2]
                
                // Because DynamicArrays (the Conditions list) change size during load, 
                // we must flag structural changes to regenerate the ImGui layout.
                _predicateSession.MarkStructuralChange(); [3]
                _predicateSession.RebuildDocument(); [3]
            }
        }
        
        ImGuiApi.Separator();
    }
}
```

Architectural Benefits

-   **Polymorphic Recursion:** Relying on `StructEdit`'s `DynamicArrayBinding` natively populates the ImGui UI with a `<Type Selector>` combo box for the `Conditions` list, allowing the operator to infinitely mix `CompoundPredicateDto`, `PropertyMatchDto`, and `StructuralPredicateDto` nodes.-   **Zero Parsing Overhead:** By compiling the recursive delegates upfront, the headless execution loop processes complex `(A && B) || C` logic trees via direct closure invocation, avoiding object allocations or type-checks on the hot path.-   **Schema Stability:** Utilizing `session.ToJson()` and `session.LoadJson()` ensures the saved search presets conform to the strict `StructEdit` schema, natively tolerating property renames or structural additions without custom JSON parsing logic.

good! how the updated visual layout would look like to allow netsed conditions? some slightly indented conditions if nexted?

Here is the visual layout representing a nested `A AND (B OR C)` query.

Because we architected the criteria around `StructEdit`, this hierarchical indentation requires zero custom UI layout code. The `ComponentEditDrawer` natively recursively traverses the `EditDocument` tree, rendering complex nested collections using `ImGuiApi.TreeNodeEx` while pushing standard ImGui indentations for every depth level.

ImGui Wireframe Layout

```
[Replay Search Window]
+-----------------------------------------------------------------------+
| Search Mode:  ( ) Value  ( ) Event  ( ) Lifecycle  (x) Compound       |
+-----------------------------------------------------------------------+
| [ Save Preset... ] [ Load Preset... ]                                 |
|-----------------------------------------------------------------------|
| Search Criteria (StructEdit)                                          |
|---------------------------------|-------------------------------------|
| Property                        | Value                               |
|---------------------------------|-------------------------------------|
| ▼ RootPredicate                 |                                     |
|   Operator                      | [ AND ˅                           ] |
|   ▼ Conditions              [2] | [+ Add]                             |
|     ▼  (PropertyMatch)       |                                 [X] |
|         ComponentType           | [ BehaviorState ˅                 ] |
|         PropertyPath            | [ ActiveBehaviorHash              ] |
|         Target Behavior         | [ Combat ˅                        ] | 
|     ▼ [3] (CompoundPredicate)   |                                 [X] |
|         Operator                | [ OR ˅                            ] |
|         ▼ Conditions        [2] | [+ Add]                             |
|           ▼  (PropertyMatch) |                                 [X] |
|               ComponentType     | [ NavigationStatus ˅              ] |
|               PropertyPath      | [ Result                          ] |
|               Operator          | [ Equals ˅                        ] |
|               Value             | [ FailedBlocked ˅                 ] | 
|           ▼ [3] (Structural)    |                                 [X] |
|               ComponentType     | [ IsEmbarkedTag ˅                 ] |
|               Modification Type | [ Removed ˅                       ] |
+-----------------------------------------------------------------------+
| [ Execute Search ]              | Status: Ready.                      |
+-----------------------------------------------------------------------+
| Search Results                                                        |
|------------------|-------------------|--------------------------------|
| Frame            | Entity            | Event Type                     |
|------------------|-------------------|--------------------------------|
| [Frame 1420]     | [42, v3]          | Compound Condition Met         |
+-----------------------------------------------------------------------+
```

Architectural Mechanics of the Layout

**1\. Recursive Indentation (****ImGui.TreeNodeEx****)** The indentation you see is natively driven by the `ComponentEditDrawer`. When the drawer encounters an `EditNodeKind.Class` or `EditNodeKind.DynamicArray` (such as the `Conditions` list), it renders an `ImGuiApi.TreeNodeEx`. If the node is expanded, ImGui inherently pushes the hierarchical indent before the drawer recursively calls `DrawEditNode` on the children.

**2\. Dynamic Array Controls** Notice the and `[+ Add]` in the `Conditions` rows, and the `[X]` buttons on the child nodes. Because `CompoundPredicateDto.Conditions` is a `List<SearchPredicateDto>`, `StructEdit` binds it using a `DynamicArrayBinding`. The drawer automatically injects the `[+ Add]` button into the array header and the `[X]` delete button into the leaf nodes. When clicked, these trigger `_session.MarkStructuralChange()` and `_session.RebuildDocument()` to safely mutate the array size and regenerate the ImGui table rows dynamically.

**3\. Polymorphic Instantiation** Because `Conditions` is a list of the abstract `SearchPredicateDto`, `StructEdit` handles polymorphism internally. When the operator clicks `[+ Add]`, the engine presents a type-selector dropdown containing all concrete implementations (`CompoundPredicateDto`, `PropertyMatchDto`, `StructuralPredicateDto`, etc.). Selecting one instantiates the correct DTO, appends it to the list, and instantly expands the UI to display the new nested fields.

This strict adherence to the data-driven `StructEdit` architecture means you can nest `OR` conditions inside `AND` conditions to infinite depth, serialize it all cleanly to JSON, and render it properly indented, entirely for free.

perfect!

we extended the scope quite a lot. pls present updated list of necessary interfaces and class APIs. All must be perfectly testable.

To deliver a headless, zero-allocation, and infinitely nestable search architecture, the system must be strictly partitioned into Domain Data Transfer Objects (DTOs), a Compiler/Evaluation layer, the Headless Execution Engine, and the Presentation/Gizmo layer.

By injecting interfaces at boundaries, every component can be instantiated in an xUnit test harness without requiring an active Dear ImGui context or a Raylib window.

Here is the comprehensive API contract definition.

1\. Domain Data Layer (The Composite Tree)

These DTOs represent the absolute source of truth for the search query. Because they inherit from a common base class, `StructEdit`'s polymorphic JSON serializer handles persistence and UI generation automatically. They contain zero logic.

```
// Base marker
public abstract class SearchPredicateDto { }

// Results
public sealed record SearchResultDto(
    int FrameIndex, 
    long WallClockTicks, 
    Entity Entity, 
    string ContextMessage);

// 1. Compound / Nested Logic
public enum LogicalOperator { And, Or }
public sealed class CompoundPredicateDto : SearchPredicateDto
{
    public LogicalOperator Operator { get; set; } = LogicalOperator.And;
    public List<SearchPredicateDto> Conditions { get; set; } = new();
}

// 2. Component Property Evaluation
public enum SearchOperator { Equals, Contains, GreaterThan, LessThan, Changed }
public sealed class PropertyMatchDto : SearchPredicateDto
{
    public Type ComponentType { get; set; } = typeof(SimTransform);
    public string PropertyPath { get; set; } = string.Empty;
    public SearchOperator Operator { get; set; } = SearchOperator.Equals;
    public string TargetValue { get; set; } = string.Empty;
}

// 3. Transient Event Evaluation
public sealed class TransientEventPredicateDto : SearchPredicateDto
{
    public Type EventType { get; set; } = null!;
    public bool AnyOccurrence { get; set; } = true;
    public string PropertyPath { get; set; } = string.Empty;
    public SearchOperator Operator { get; set; } = SearchOperator.Equals;
    public string TargetValue { get; set; } = string.Empty;
}

// 4. Entity Lifecycle
public enum EntityIdentifierType { EcsHandle, NetworkId, NameSubstring }
public sealed class LifecyclePredicateDto : SearchPredicateDto
{
    public EntityIdentifierType IdentifierType { get; set; } = EntityIdentifierType.NameSubstring;
    public string TargetValue { get; set; } = string.Empty;
}

// 5. Spatial Bounding
public enum BoundaryEvent { Entry, Exit, EntryOrExit }
public sealed class SpatialBoundingPredicateDto : SearchPredicateDto
{
    [MapPickableBoundingBox] // Instructs StructEdit to attach a Map Picker button
    public BoundingBox2D Bounds { get; set; }
    public BoundaryEvent TriggerEvent { get; set; } = BoundaryEvent.EntryOrExit;
}

// 6. Structural Modification
public enum StructuralModification { Added, Removed, AnyChange }
public sealed class StructuralPredicateDto : SearchPredicateDto
{
    public Type ComponentType { get; set; } = null!;
    public StructuralModification ModificationType { get; set; } = StructuralModification.Added;
}
```

2\. Compilation & Evaluation Layer

Before the multi-thousand frame fast-forward loop begins, the query tree must be compiled into stateless delegates that operate purely on ECS memory.

```
/// <summary>
/// Pre-compiles StructEdit offset-bindings to evaluate properties dynamically 
/// without reflection inside the hot loop.
/// </summary>
public interface IPropertyEvaluator
{
    string GetValueAsString(object componentInstance);
}

/// <summary>
/// Compiles the recursive CompoundPredicateDto tree into a single execution delegate.
/// </summary>
public interface IPredicateCompiler
{
    Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto rootNode);
}

/// <summary>
/// Dedicated compiler for transient events, mapping FdpEventBus read spans.
/// </summary>
internal delegate void EventScannerDelegate(FdpEventBus bus, int frame, long ticks, List<SearchResultDto> results);

public interface IEventScannerCompiler
{
    EventScannerDelegate CompileScanner(TransientEventPredicateDto predicate);
}
```

3\. Headless Execution Engine

The search service operates entirely on background threads. It isolates file I/O and ECS memory allocation from the presentation layer.

```
/// <summary>
/// The core search coordinator. Testable by mocking the .fdp path or injecting 
/// a pre-populated ReplayBrowserContext.
/// </summary>
public interface IRecordingSearchService
{
    /// <summary>
    /// Executes the search over an entire recording, utilizing QueryDelta to skip memory chunks.
    /// </summary>
    IReadOnlyList<SearchResultDto> ExecuteSearch(string fdpPath, SearchPredicateDto rootPredicate);
}

// Implementation detail inside RecordingSearchService:
public sealed class RecordingSearchService : IRecordingSearchService
{
    private readonly IPredicateCompiler _predicateCompiler;
    private readonly IEventScannerCompiler _eventCompiler;

    public RecordingSearchService(IComponentEditService editService)
    {
        // Internal wiring of compilers, passing the StructEdit service for binding resolution
    }
    
    public IReadOnlyList<SearchResultDto> ExecuteSearch(string fdpPath, SearchPredicateDto rootPredicate)
    {
        // 1. Extracts mandatory components for QueryBuilder (O(1) block skipping)
        // 2. Compiles delegates
        // 3. Executes StepForward() loop over the ReplayBrowserContext
    }
}
```

4\. UI Presentation & Integration Hooks

The UI components remain strictly immediate-mode, relying on dependency injection for deep-linking (jumping to frames/entities) and asynchronous interactions (map picking).

```
/// <summary>
/// The primary ImGui panel. Testable by injecting mock delegates for deep links.
/// </summary>
public sealed class ReplaySearchPanel
{
    public ReplaySearchPanel(
        IComponentEditService editService,
        IRecordingSearchService searchService,
        Action<int> onSeekRequested,
        Action<Entity> onEntitySelected)
    { ... }

    public void DrawContent() { ... }
}

/// <summary>
/// Extension to existing IComponentPickerContext to support AABB map picking 
/// triggered from within the StructEdit dynamic layout.
/// </summary>
public interface ISpatialPickerContext : IComponentPickerContext
{
    void RequestBoundingBoxPick(string jsonPath);
    bool TryConsumeBoundingBoxPick(string jsonPath, out BoundingBox2D box);
}

/// <summary>
/// StructEdit plugin. Renders the Min/Max bounds and the "Pick Area" button 
/// whenever it encounters a BoundingBox2D field.
/// </summary>
public sealed class BoundingBoxFieldDrawer : IImGuiFieldDrawer
{
    public Type TargetType => typeof(BoundingBox2D);
    public BoundingBoxFieldDrawer(ISpatialPickerContext pickerCtx) { ... }
    public bool DrawInput(ref object value, EditNodeMetadata meta) { ... }
}
```

5\. Spatial Gizmo Interaction

The map tool responsible for drawing the AABB rubber-band and capturing coordinates.

```
/// <summary>
/// Exclusive-focus tool that captures map hardware events to define a 2D bounding box.
/// Emits DebugPrimitiveShape.Box2D to the IDebugDrawBuilder while dragging.
/// </summary>
public sealed class BoundingBoxPickerGizmo : IEntityStatefulGizmo
{
    public bool RequiresExclusiveFocus => true;
    public bool WantsRawInput => true;

    public BoundingBoxPickerGizmo(
        Action<BoundingBox2D> onComplete, 
        Action onCancel) 
    { ... }
    
    public void UpdateAndDraw(float deltaTime, IDebugDrawBuilder draw) { ... }
    public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { ... }
    public void OnDragUpdate(Vector3 worldPos) { ... }
    public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { ... }
    
    // Unused IGizmoInteractionHandler interface requirements
    public void Dispose() { }
    public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
    public void OnCommit(Vector3 worldPos) { }
    public void OnCancel() { }
    public void OnMenuAction(int actionId) { }
}
```

Testability Analysis

-   **Domain**: `CompoundPredicateDto` can be constructed manually in xUnit tests and verified via `JsonSerializer.Serialize`.-   **Execution**: By replacing `ReplayBrowserContext` loading with a mock `EntityRepository` and `FdpEventBus`, `RecordingSearchService` tests can push arbitrary components/events into the repo, step the tick, and assert that the correct `SearchResultDto` list is generated without file I/O.-   **Gizmo**: `BoundingBoxPickerGizmo` can be tested headlessly by invoking `OnMouseEvent` and `OnDragUpdate` with mock vectors, ensuring the `onComplete` delegate fires with the correct min/max bounds.-   **UI**: ImGui drawing logic is isolated to `ReplaySearchPanel` and the injected `IImGuiFieldDrawer` plugins. The underlying state machines (such as boundary entry/exit) are kept purely within the headless service layer.

And pls rethink the suggested development stages. The highest priority is the full json dump in console app. then the UI replay browser subsystem with basic features. The extensive search is at the end.

Restructuring the execution plan minimizes integration risk by establishing the data extraction pipeline before coupling it to the immediate-mode presentation layer.

Here is the revised architectural roadmap.

Stage 1: Headless JSON Export CLI

The highest priority is building a standalone console application capable of extracting `.fdprec` recording data into raw JSON or changelog formats without exhausting system memory.

-   **Architecture**: Create a standalone executable (e.g., `Fdp.Tools.RecordingDumper`) that bypasses all rendering and UI code.-   **Pipeline**: Instantiate a headless `EntityRepository` and drive it using `Fdp.Core.FlightRecorder.RecordingReader`. The tool will step through the recording frame by frame.-   **Memory Discipline**: For full JSON dumps of massive recordings, you cannot build a single DOM in memory. You must stream the output directly to disk using `System.Text.Json.Utf8JsonWriter` inside the frame loop.-   **Serialization**: Leverage the existing `ScenarioSerializer` or `EntityJsonDumper` to extract the ECS state for each tick. If the changelog mode is invoked, maintain a persistent baseline dictionary and compute the diff tree before emitting the JSON node to the stream.

Stage 2: Replay Browser UI Foundation

Once the headless data extraction is proven, construct the foundational interactive replay subsystem within the presentation layer.

-   **Timeline Control**: Build the `ReplayBrowserWindow`. Wire the `PlaybackController` to UI controls (Play, Pause, Step) and implement the timeline scrubber. Seeking must safely invoke `PlaybackController.SeekToFrame` on the main thread to prevent ECS memory corruption.-   **State Inspection**: Integrate the existing diagnostic panels. When the timeline is paused or scrubbed, the `EntityInspectorPanel` will render the current frame's components, and the `EventBrowserPanel` will display the transient events captured in the `FdpEventBus` read buffers for that exact tick.-   **Export Hook**: Wire the Stage 1 JSON export logic to a "Save to JSON..." button within this window, passing the currently selected entity handles from the `EntityInspectorPanel`'s `_selectedEntities` hash set.

Stage 3: Advanced Recording Search Engine

With the playback foundation stable, implement the extensive search capabilities as an independent tool window.

-   **Domain Models**: Define the unified composite tree using `SearchPredicateDto`, `CompoundPredicateDto`, and `PropertyMatchDto`. This ensures the query definitions remain decoupled from the UI.-   **StructEdit Integration**: Construct the `ReplaySearchPanel`. Bind the predicate DTOs to an isolated `IEditSession` and use `ComponentEditDrawer` to render the dynamic array of conditions recursively. Utilize `StructEdit.Json.EditDocumentJsonSerializer` to handle loading and saving complex compound queries to disk.-   **Headless Execution Engine**: Implement the `RecordingSearchService`. To maintain the 60Hz hot-path budget, this background service must strictly utilize `QueryDelta` for O(1) chunk skipping. Unmanaged property evaluations must be pre-compiled before the loop begins to avoid reflection overhead during the frame-stepping iteration.-   **Deep Linking**: Wire the resulting data grid to emit temporal and spatial deep-links. Clicking a search result must trigger delegates that jump the `PlaybackController` to the matched frame and assign the `InspectorState.SelectedEntity` to focus the UI.

good. NOw pls update the success conditions to frame the work and avoid the developers stopping too early

To prevent premature sign-off, the development teams must be gated by strict, testable acceptance criteria that validate both the performance constraints and the comprehensive feature set.

**1\. Strict Zero-Allocation Hot Path** The `RecordingSearchService` extraction loop must process component mutations, native transient events, and structural modifications without allocating managed memory per frame. Developers must provide profiling evidence that `QueryDelta` correctly skips unmodified 64KB memory chunks during the search.

**2\. Unified Composite Evaluation** The engine must successfully evaluate infinitely nested composite queries (`CompoundPredicateDto`) using pre-compiled delegates. The compilation step must map all `SearchPredicateDto` variants into a single `Func<EntityRepository, Entity, bool>` closure, guaranteeing zero reflection overhead in the execution loop.

**3\. Complete Domain Coverage** All search modalities—Component Property, Transient Event, Entity Lifecycle, Spatial Bounding, and Structural Modification—must be fully integrated into the polymorphic `SearchPredicateDto` hierarchy. Developers must not hardcode separate UI logic for each mode; they must rely entirely on `StructEdit`'s `DynamicArrayBinding` and type-reflection drawers to render the interfaces.

**4\. Causality Deep-Linking** The presentation layer must prove that clicking any search result in the data grid fires both spatial and temporal deep-links. This must instantly snap the `PlaybackController` to the exact simulation tick and focus the `EntityInspectorPanel` on the responsible entity handle.

**5\. Spatial Gizmo Decoupling** The spatial bounding search must demonstrate a seamless transition from the ImGui `StructEdit` drawer to the Raylib canvas. The `BoundingBoxPickerGizmo` must capture raw hardware events exclusively and route the selected bounds back to the `IEditSession` without coupling ImGui directly to the map renderer.

**6\. JSON Preset Persistence** The `StructEdit.Json` extensions must successfully serialize and deserialize complex, multi-level compound queries to disk. The loaded JSON must cleanly rebuild the `IEditSession` document tree, instantly restoring dynamic UI elements like nested condition arrays without manual schema parsing.

**7\. Headless Test Coverage** Every `IPredicateCompiler` variant and search execution path must be covered by xUnit tests that inject a mocked `EntityRepository` and `FdpEventBus`. These tests must verify the correct O(1) state-machine transitions (e.g., structural additions, boundary entry/exit edge detection, and event occurrences) without requiring an active Dear ImGui context or Raylib window.

