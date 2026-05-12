
How we send initial values to a struct inspector gizmo? As interned lig string, yogethet with schema?

Actually, no—initial values for a `StructInspector` gizmo are **not** sent over the network as an interned string or JSON payload alongside the primitive.

Unlike the `ContextMenuBinding` (which explicitly uses the `StringInternMap` to pass JSON), the `StructInspector` relies entirely on a shared, pre-registered schema hash.

Here is exactly how the data flow works for the Struct Inspector gizmo:

**1\. The Primitive Payload (Host -> Terminal)** When your backend gizmo (e.g., `LayerControlGizmo`) wants to show an inspector, it emits a `DebugPrimitive` of shape `StructInspector`. This primitive is extremely lightweight and does not carry string payloads. Instead, it sends:

-   **StructSchemaHash** (`uint`): An FNV-1a hash identifying the structure.-   **StructNetworkId** (`long`): The stable anchor ID for the entity.-   Formatting/Layout fields: `StructAnchor`, `StructOffsetX`, `StructOffsetY`, `StructIsReadOnly`.

**2\. Local Schema Lookup (Terminal Side)** When the Image Generator (IG) or terminal receives this primitive, the `ImGuiPropertyTreeAdapter` looks up the `StructSchemaHash` in its local `GizmoSchemaRegistry`. If found, the registry returns a pre-built `EditDocument` (the `StructEdit` tree).

**3\. Initial Values** The initial values are **not transmitted** by the gizmo primitive. They are pulled directly from the `IValueBinding` implementations inside the terminal's local `EditDocument`. For example, in the `LayerControlSchemaFactory`, the document is built natively on the client side with `new BoolBinding(true)`, meaning the initial values are hardcoded locally.

**4\. Submitting Changes (Terminal -> Host)** When the operator makes a change and clicks "Apply" in the ImGui panel, the terminal serializes the `EditDocument`'s current binding values into a JSON string. It then sends this JSON _back_ to the host via a `GizmoStructUpdateEvent` (transmitted over DDS as a `GizmoInteractionBatch` with `Kind = StructUpdate` and the `PayloadJson` populated). The backend gizmo receives this via `OnStructUpdate(string payloadJson)` and deserializes it to update its internal state.

**What if the host needs to dynamically update the terminal's values?** While the `DebugPrimitive` itself doesn't carry initial values, the architecture includes a dedicated DDS topic called `GizmoUiState`. It contains a `GizmoInstanceId` and an `EditDocumentJson` string. This topic is designed to publish the current UI state to remote editors via `IGizmoUiStatePublisher`, but it operates out-of-band from the `DebugPrimitive` stream. For basic structural inspectors, you must ensure the terminal has the `EditDocument` registered in its `GizmoSchemaRegistry` ahead of time.

It does not make sense the initial values for struct edit are hardcoded on dumb terminal client side. It knows nothing about the structs to edit. Initial value MUST go to client before structinspector gizmo primitive is issued. How the gizmo ui state topic identifies the structinspector instance and what format it uses for the data?

You are completely correct from an architectural standpoint—a dumb terminal should not rely on hardcoded initial values for dynamic structs. The framework is indeed designed to solve this via the **GizmoUiState** DDS topic, which operates out-of-band from the display primitives to synchronize this state.

Here is how the topic identifies the instance and formats the data:

**1\. Identification (****GizmoInstanceId****)** The `GizmoUiState` topic identifies the specific gizmo or inspector instance using a 32-bit unsigned integer key: `[DdsKey] public uint GizmoInstanceId`.

**2\. Data Format (****EditDocumentJson****)** The payload itself is transmitted as a standard string: `[DdsManaged] public string EditDocumentJson`.

This JSON string is tightly coupled to the `StructEdit` library and is formatted according to the `StructEdit.Json.EditDocumentJsonSerializer` schema (version 1.0). The JSON schema looks like this:

```
{
  "structedit_version": "1.0",
  "rootTypeName": "My.NS.MyType, MyAssembly, ...",
  "scope": "$",
  "nodes": [ ... ]
}
```

The `nodes` array contains a flat list of serializable leaf entries (e.g., scalars, booleans, strings). Container nodes (like classes or structs) are flattened down to their leaf descendants, keeping the payload compact.

**How it connects to the client (and what you need to wire up):** While the `GizmoUiState` topic is explicitly defined for "enabling remote editors to display and modify gizmo configuration", the current `ImGuiPropertyTreeAdapter` primitive renderer in `GizmoMap.Presentation` only looks up the `StructSchemaHash` in its local `GizmoSchemaRegistry` and renders whatever `EditDocument` it finds.

To fully realize the dynamic data flow where the host provides the initial values, the terminal application must subscribe to the `GizmoUiState` topic and execute the following upon receiving a sample:

-   Extract the `EditDocumentJson` payload.-   Find the corresponding `EditDocument` in the terminal's `GizmoSchemaRegistry`.-   Call `EditDocumentJsonSerializer.Deserialize(json, document)`.

This `Deserialize` call will traverse the flattened JSON nodes and inject the host's initial values directly into the terminal's local `IValueBinding` objects, ensuring the `StructInspector` UI is perfectly populated with the backend's live state before the operator makes any edits.

How the dumb terminal knows to pause receiving new updates of the structinspector content while the fields are being edited by the user?

In the current v177 framework implementation, the dumb terminal **does not** actually know how to pause updates. Because the `GizmoUiState` DDS ingress is not yet actively wired into the `ImGuiPropertyTreeAdapter`'s render loop, the terminal isn't receiving live network updates that would overwrite the user's edits in the first place.

When you fully wire up the `DdsReader<GizmoUiState>` on the terminal side, you will immediately face the exact race condition you are describing: incoming network state calling `EditDocumentJsonSerializer.Deserialize()` will clobber the user's active ImGui widget states.

Because the terminal operates as a lightweight client, it relies directly on the `EditDocument` tree and custom value bindings (like `SimpleBoolBinding`) rather than the full `IEditSession` wrapper that the host uses. This means the terminal does not have an automatic `IsDirty` tracking flag.

To implement the pause mechanism safely, you must rely on ImGui's native interaction states within the terminal's adapter. Here is how you should handle it:

**1\. Track ImGui Window Focus** Inside `ImGuiPropertyTreeAdapter.DrawScheduled`, the struct inspector window is created with a stable ID using the network ID: `"Title###StructInsp_{item.NetworkId}"`. You should check if the operator is currently interacting with this specific window by calling `ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)` or `ImGui.IsWindowHovered()`.

**2\. Gate the Deserialization** In your `GizmoUiState` subscriber loop, before you call `EditDocumentJsonSerializer.Deserialize(json, document)`, check the focus state of the corresponding gizmo window.

-   **If the window is focused (editing is in progress):** Discard or cache the incoming `GizmoUiState` sample. Do not deserialize it into the `EditDocument`.-   **If the window is out of focus:** Safely pass the `GizmoUiState` JSON into the deserializer to update the local `IValueBinding`s with the host's latest state.

**3\. Resume on Apply** When the operator clicks "Apply", the terminal serializes the edited document and sends the `GizmoStructUpdateEvent` (transmitted as `GizmoInteractionBatch` with `Kind = StructUpdate`) back to the host. Because the user has clicked a button, the interaction is complete, focus can be yielded, and the terminal is free to resume accepting network updates.

**Host-Side Best Practice:** To further minimize update conflicts, your backend gizmos (like `LayerControlGizmo`) should not publish `GizmoUiState` continuously every frame. Because the `GizmoUiState` topic is configured with `DdsDurability.TransientLocal` and `DdsHistoryKind.KeepLast` with a depth of 1, the terminal is guaranteed to receive the latest state upon opening the gizmo. The host only needs to publish a new `GizmoUiState` sample if the configuration is changed independently on the backend (e.g., via an ExCon command or automated script).

I think the client needs a state machine per structinspector. It should be showing fresh data read only until the user decides to edit (by Focusing the panel). Until the focus is lost, efit fields shall not be updated from the gizmo ui state input. On focus lost or Apply pressed the apply operation shall be made.

Your proposal for a per-inspector state machine is exactly the right architectural pattern to solve the data synchronization conflict. By maintaining an explicit interaction state for each `StructInspector` instance, you can safely isolate the user's local edits from the live incoming `GizmoUiState` network traffic.

To implement this state machine, you will need to upgrade the currently stateless `ImGuiPropertyTreeAdapter` so that it persists the interaction state of each inspector across frames, using the `StructNetworkId` as the unique key.

Here is how you can implement the state machine directly into the terminal's adapter:

**1\. Define the State Machine per Instance** Add a persistent dictionary to `ImGuiPropertyTreeAdapter` (e.g., `Dictionary<long, InspectorState>`) keyed by the `NetworkId` \[i\]. The state machine should have two states:

-   **Viewing:** The baseline state. In this state, the network subscriber is allowed to deserialize incoming `GizmoUiState` payloads directly into the `EditDocument`, ensuring the UI reflects fresh host data.-   **Editing:** The locked state. When in this state, incoming `GizmoUiState` payloads are ignored or discarded to prevent clobbering the user's active changes.

**2\. Handle the "Focus" Transition (Viewing → Editing)** Inside `ImGuiPropertyTreeAdapter.DrawScheduled`, after calling `ImGui.Begin(windowTitle)`, you can check if the user has taken focus of the panel:

```
bool isFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
```

If the state is currently **Viewing** and `isFocused` becomes `true`, transition the state machine to **Editing**.

_Note on your "Read-Only" requirement:_ If you literally pass `isReadOnly = true` to `DrawEditNode` while unfocused \[i\], ImGui will gray out the widgets and might prevent the user from clicking them to gain focus. Instead, keep the widgets interactive, but use the `isFocused` flag to visually style the window (e.g., changing border colors) and, most importantly, to gate your `GizmoUiState` deserializer.

**3\. Handle the Commit Transition (Editing → Viewing)** While in the **Editing** state, monitor for two conditions to trigger the apply operation:

-   The user explicitly clicks the "Apply" button.-   The window loses focus (`!isFocused`).

When either occurs, execute the existing serialization logic and transmit it back to the host, then revert the state machine to **Viewing**:

```
// Inside DrawScheduled:
if (currentState == Editing && (!isFocused || applyButtonClicked))
{
    string json = SerializeDocumentToJson(doc!);
    onStructUpdate.Invoke(item.NetworkId, json);
    
    // Return to viewing, unfreezing network updates
    currentState = Viewing; 
}
```

This leverages the existing `SerializeDocumentToJson` helper and `onStructUpdate` delegate already present in `ImGuiPropertyTreeAdapter` \[i\], cleanly committing the state the moment the user clicks away.

How the unique id for the structinspector is allocated? How it stays unique across different gizmos not aware of each other?

The unique ID for a `StructInspector` (transmitted as the **StructNetworkId** or **InspNetworkId** in the `DebugPrimitive` payload) is a 64-bit integer (`long`). The framework guarantees its uniqueness across different, unaware gizmos using three distinct allocation strategies depending on the gizmo's lifecycle:

**1\. Atomic Counter Allocation (****GlobalGizmoManager.NewId****)** For dynamically spawned, non-entity-bound gizmos (such as placement tools, area selection boxes, or measure tools), the ID is allocated by calling **GlobalGizmoManager.NewId()**. This method uses a thread-safe atomic increment (`Interlocked.Increment`) on a static counter. This guarantees that every time a temporary gizmo is created, it receives a strictly unique, stable anchor ID across the entire process, ensuring it never collides with another gizmo.

**2\. Network Entity IDs** For gizmos that are bound to a specific ECS entity in the world (like an entity rotator, entity property inspector, or a sensor ring), the `StructNetworkId` is simply assigned the entity's globally unique **Network ID**. Because the underlying replication layer guarantees that every entity has a unique `NetworkIdentity.Value`, the gizmo system safely relies on this ID to isolate the inspector instances from one another.

**3\. Hardcoded Well-Known Constants** For global singleton settings panels that are always present and never duplicated, the ID can be a hardcoded constant. For example, the `LayerControlGizmo` statically assigns its anchor ID as `9999L`. Because only one instance of this control panel will ever exist in the registry, it stays unique by design.

Combining hardcoded constants wirh dynamic allocation does not look reliable. Is The Newid called every frame or is it stable per gizmo instance?

You are absolutely right to question the reliability of this design. Mixing a simple incrementing counter, hardcoded constants, and entity IDs in the same key space is a collision risk.

To answer your question directly: **GlobalGizmoManager.NewId()** **is called exactly once per gizmo instance, not every frame.**

Here is how it works and why your concern about reliability is completely valid:

**1\. ID Stability (Once Per Instance)** The `NewId()` method is an atomic increment on a static counter (`_nextId`). It is designed to generate a "stable key" for the entire lifecycle of a temporary gizmo. For example, when a user clicks the "Measure" tool in the UI, the application calls `GlobalGizmoManager.NewId()` exactly once, assigns that ID to the new `MeasureGizmo`, and calls `Register(id, gizmo)`. The ID remains perfectly stable across all frames until the user finishes measuring and the tool calls `Unregister(id)`.

**2\. The Collision Risk (Why it is unreliable)** Because the `GlobalGizmoManager` uses a single flat `Dictionary<long, IEntityStatefulGizmo>` to store all active gizmos, combining these three ID sources creates a genuine vulnerability:

-   **The Counter:** The dynamic allocator starts at `0` and increments by 1 every time a temporary tool is used.-   **The Hardcoded Constant:** The `LayerControlGizmo` hardcodes its ID as `9999L`.-   **The Overwrite Behavior:** The `Register(long id, IEntityStatefulGizmo gizmo)` method does not prevent collisions; it simply executes `_activeGizmos[id] = gizmo;`, overwriting whatever was previously there.

This means if an operator activates temporary placement or measurement tools 9,999 times during a long session, the dynamic counter will reach `9999L`. The moment they activate the 9,999th tool, it will silently overwrite and destroy the global `LayerControlGizmo` in the registry.

Furthermore, because network Entity IDs are also passed into this exact same ID space (e.g., when attaching a `RouteWaypointGizmo` to a specific entity's Network ID), a dynamic tool ID could eventually increment high enough to collide with a live entity's ID, clobbering its active gizmo.

How to make it reliable? Can we unify to use the new id call only in all cases?

You cannot reliably unify everything to use `GlobalGizmoManager.NewId()` in all cases. Doing so would break a fundamental design advantage of the ECS-based gizmos.

Here is why you should not unify them, and how you can easily make the system completely reliable by partitioning the ID space.

Why we cannot use `NewId()` for everything

The `AnchorId` (transmitted over the network as `StructNetworkId` or `InspNetworkId` inside `GizmoPickToken`) serves as the primary routing key from the dumb terminal back to the host.

If you forced entity-bound gizmos (like an entity property inspector or rotator) to use a dynamic `NewId()`, the host would be forced to maintain a complex reverse-lookup dictionary mapping every random dynamic ID back to its specific ECS `Entity`. Currently, the framework avoids this overhead by directly using the entity's globally unique Network ID as the `AnchorId`, allowing systems like `DataDrivenGizmoSystem` to naturally route network interactions straight to the target entity.

How to make the ID space reliable

The collision risk exists because `GlobalGizmoManager`, `DataDrivenGizmoSystem`, and hardcoded singletons all share the exact same 64-bit integer space (`long AnchorId`) over the network layer.

Because the framework already cleanly separates the storage of these gizmos (entity-bound gizmos live in `DataDrivenGizmoSystem`'s `Dictionary<Entity, ...>`, while standalone tools live in `GlobalGizmoManager`'s `Dictionary<long, ...>`), you only need to prevent collisions at the transport level.

You can make this 100% reliable by **partitioning the 64-bit ID space**:

**1\. Reserve Positive IDs for Network Entities** The `DdsIdAllocatorServer` natively starts allocating network IDs at `1` and increments upwards. Let all entity-bound gizmos continue using these positive Network IDs.

**2\. Shift** **NewId()** **to the Negative Space (or High Bits)** Currently, `GlobalGizmoManager` initializes its counter at `0`. You should change this to start at a safely distinct range. Because `_nextId` is a signed 64-bit `long`, you can simply start dynamic allocation in the negative space. Change:

```
private static long _nextId = 0;
public static long NewId() => Interlocked.Increment(ref _nextId);
```

To:

```
// Dynamic IDs start at -100,000 and count downwards
private static long _nextId = -100000L; 
public static long NewId() => Interlocked.Decrement(ref _nextId);
```

**3\. Assign a Reserved Block for Hardcoded Constants** Move your hardcoded singleton panels into a tightly defined reserved block (e.g., `-1` to `-9999`). For example, `LayerControlGizmo.AnchorId = 9999L;` can safely be changed to `-1L`.

By partitioning the ID space mathematically (Network Entities > 0, Constants = -1 to -9999, Dynamic Tools < -100000), you completely eliminate the risk of a dynamic interaction tool overwriting an entity inspector or clobbering the layer control panel in the registry, without having to change any network topics or complex dictionary routing logic.

Why not using newid call always? Even network entity can have multiple gizmos, each requiring own uniquie id. We have one global gizmo manager. No risk.

You are operating under an incorrect assumption about the framework's architecture: **we do not have just one global gizmo manager.**

The framework explicitly splits gizmo management into two completely different systems to handle different lifecycles, which is why `NewId()` cannot be used universally:

**1\. We have two managers, not one**

-   **GlobalGizmoManager****:** Manages standalone, temporary tools (like the measure tool or location picker) using a `Dictionary<long, IEntityStatefulGizmo>`. This system uses `NewId()` because these tools are not attached to anything in the game world.-   **DataDrivenGizmoSystem****:** Manages entity-bound gizmos using a `Dictionary<Entity, List<CompiledGizmoInstance>>`.

**2\. How multiple gizmos on one entity actually work** You are correct that a single network entity can have multiple gizmos. However, they do not need their own unique `AnchorId`. Because `DataDrivenGizmoSystem` maintains a `List` of gizmos per entity, multiple gizmos can share the exact same `AnchorId`. If a specific gizmo needs to distinguish between multiple interactive parts of itself (like individual vertices on a polygon), it uses the `SubElementId` field provided in the `GizmoPickToken`.

**3\. Why** **NewId()** **would break network routing** The most critical reason we cannot use `NewId()` for entity gizmos is network routing performance. When the dumb terminal sends an interaction event (like clicking a gizmo) over DDS, it sends a `GizmoInteractionBatch`.

When the host receives this batch in the `GizmoInteractionIngressTranslator`, it must figure out which ECS entity was clicked. Because the `AnchorId` is literally the ECS `Entity.Index`, the translator can reconstruct the target entity instantly with zero allocations and zero lookups:

```
var entity = new Entity((int)batch.PickAnchorId, (ushort)batch.PickStreamId);
```

If you forced all entity-bound gizmos to use a random `NewId()`, this O(1) instant routing would be destroyed. The `PickAnchorId` coming from the network would no longer match the `Entity.Index`. To fix it, you would be forced to build and maintain a massive reverse-lookup dictionary mapping every random `long` ID back to its specific ECS `Entity`.

For a simulation with tens of thousands of entities constantly spawning and destroying gizmos based on ECS component rules, maintaining this reverse-lookup map would introduce severe memory allocations, dictionary overhead, and garbage collection pressure on the 60 FPS hot path. Relying on the native `Entity.Index` as the `AnchorId` entirely bypasses this cost.

Ok that makes sense. Explain How a single entity can uniquely define many structinspector gizmos and many gizmos requiring drah and drop boxes and many data driven gizmos without the risk of id overlaps

To achieve this, the framework relies on a combination of nested identifiers and list-based rendering, though there are actually some strict limitations in the current architecture regarding interactions and ImGui window IDs.

Here is exactly how the framework handles multiple gizmos on a single entity, and where the risks of overlap currently lie:

**1\. Many Drag-and-Drop Boxes (Solved via** **SubElementId****)** If a single gizmo needs to project dozens of interactive drag-and-drop handles (like the vertices of a polygon or waypoints on a route), it relies on the **SubElementId** field. When the gizmo emits its `DebugPrimitiveShape.Box2D` primitives, it assigns the entity's network ID to the `BoxAnchorId`, but assigns a unique, 1-based index to each box's `SubElementId`. When the operator clicks one of those boxes, the dumb terminal sends back a `GizmoPickToken` containing both the `AnchorId` and the `SubElementId`. The `GizmoInteractionManager` routes the event to the gizmo using the `AnchorId`, and the gizmo's `OnInteractionStarted` method simply reads the `SubElementId` to know exactly which vertex was grabbed without any ID collisions.

**2\. Many Data-Driven Gizmos (Visually Safe, but Interaction overlaps)** An entity can successfully host multiple data-driven gizmos because the `DataDrivenGizmoSystem` maintains a `Dictionary<Entity, List<CompiledGizmoInstance>>`. During the `Execute` phase, it iterates through the entire list and calls `UpdateAndDraw` on **all** active gizmo instances for that entity, allowing them to draw their visuals simultaneously. **The Catch:** While visual overlap is avoided, **interaction overlap is a real risk**. When an interaction event arrives for an entity, the `DataDrivenGizmoSystem.FindGizmo(Entity entity)` routing method is hardcoded to return `list.Instance`. This means if an entity has multiple interactive data-driven gizmos, only the first one in the list (or an injected on-demand tool, which takes strict priority) will ever receive the interaction events.

**3\. Many StructInspector Gizmos (High Risk of Overlap)** When a gizmo requests a StructInspector, the primitive transmits the `StructNetworkId` (to route updates back) and a `StructSchemaHash`. The hash allows the terminal's `GizmoSchemaRegistry` to look up the correct UI layout (e.g., a Layer Control layout vs a Unit Properties layout). **The Catch:** If a single entity attempts to open two different StructInspectors simultaneously, **they will collide and clobber each other**. In the terminal's `ImGuiPropertyTreeAdapter.DrawScheduled` method, the ImGui window ID is hardcoded to end with `###StructInsp_{item.NetworkId}`. Because the `StructSchemaHash` is not appended to the ImGui stable ID string, ImGui will treat both inspectors as the exact same window and merge their rendering contexts. To fix this, you would need to modify the adapter to append the hash: `###StructInsp_{item.NetworkId}_{item.SchemaHash}`.

And if many unrelated gizmos per sane entity? How they solve mutual subelementId overlap if they are not aware of each other?

They **do not** solve it. The framework avoids `SubElementId` overlaps on the same entity by imposing a strict "first-one-wins" routing bottleneck that completely starves the other gizmos of input.

If multiple unrelated data-driven gizmos are attached to the same entity, here is exactly what happens:

**1\. Visuals overlap safely** All of the gizmos will successfully emit their primitives. If Gizmo A and Gizmo B both emit a `Box2D` primitive with `SubElementId = 1`, both boxes will appear on the dumb terminal's screen.

**2\. Interaction is hijacked by the router** When the operator clicks one of those boxes, the terminal sends a `GizmoPickToken` over the network containing the entity's network ID and the clicked `SubElementId = 1`. When the event arrives, `DataDrivenGizmoSystem` uses its internal `FindGizmo(Entity entity)` method to decide who gets the click. This router is hardcoded with strict priority:

-   It checks for an injected on-demand gizmo first (like a Rotator tool). If one exists, it intercepts the event.-   Otherwise, it looks up the list of active data-driven gizmos for that entity and **unconditionally returns** **list.Instance**.

**3\. The consequence** Because the router only ever returns one gizmo instance, the "winning" gizmo receives the `OnInteractionStarted` event, while the other gizmos are entirely dead to input. Even if the user clicked Gizmo B's box, Gizmo A will receive the event for `SubElementId = 1` and process it incorrectly.

**How developers actually solve this (The Workaround):** If a developer genuinely needs multiple unrelated, interactive tools to coexist without blocking each other, they cannot use the standard entity-bound `DataDrivenGizmoSystem`.

Instead, they must bypass the entity's ID entirely using the **BoxAnchorId** field:

-   The tool registers itself as a standalone tool in the `GlobalGizmoManager` using a unique, dynamic `GlobalGizmoManager.NewId()`.-   When the tool emits its `Box2D` interactive primitives, it explicitly assigns its unique dynamic ID to the `BoxAnchorId` payload.-   When the dumb terminal sees a non-zero `BoxAnchorId`, it ignores the entity's actual network ID and instead uses the tool's unique ID as the `AnchorId` in the `GizmoPickToken`.

This forces the network interaction to bypass the ECS entity completely and route straight to the standalone tool's isolated slot in the global manager, making `SubElementId` collisions impossible.

How could we resolve this alltogether? With No workaround and bypasses? How to truly uniquely identify gizmo and subelement id and structinspector id in all the cases? I need reliable and elegant solution. Like using gizmo instance id or hash or something. and subelementids unique jyst per gizmo instance.

To solve this fundamentally and elegantly without ID-space partitioning workarounds, we must move away from using a single, flat `AnchorId` to route everything. The framework currently suffers from collisions because it conflates _Entity Identity_ with _Gizmo Identity_.

To achieve true isolation where `SubElementId`s are unique per gizmo instance and multiple gizmos can coexist on a single entity, you must implement a **Composite Key Routing Architecture**: `[EntityId] + [GizmoTypeId] + [SubElementId]`.

Here is the permanent, structural solution:

1\. Introduce a `GizmoTypeId` (Hash)

Instead of treating all primitives on an entity as a single flat pool, every gizmo definition must have a unique identifier.

-   Generate a stable `uint GizmoTypeId` for each gizmo (e.g., an FNV-1a hash of the gizmo's class name, similar to how `StructSchemaHash` currently works).-   Add this `uint GizmoTypeId` to the `DebugPrimitive` struct. There is currently unused padding in the 64-byte `DebugPrimitive` layout (e.g., bytes 48–51, right before `SubElementId` at offset 52) where this can safely fit without expanding the struct size.

2\. Upgrade the Interaction Tokens and Network Payload

The pick tokens must carry this new composite key back from the dumb terminal to the host.

-   Update `GizmoPickToken` in `GizmoMap.Contracts` to include `uint GizmoTypeId` alongside `AnchorId`, `SubElementId`, and `StreamId`.-   Update the DDS transport payload `GizmoInteractionBatch` to transmit this new field (e.g., `uint PickGizmoTypeId`) over the network.

3\. Contextual Primitive Emission

You do not want developers to manually assign the `GizmoTypeId` every time they draw a line or box.

-   Update the `IDebugDrawBuilder` interface or its concrete implementation (`GizmoPrimitiveBuffer`) to be context-aware.-   When `DataDrivenGizmoSystem` or `GlobalGizmoManager` iterates through active gizmos and calls `IEntityStatefulGizmo.UpdateAndDraw(deltaTime, drawBuilder)`, the system should inject the current gizmo's `GizmoTypeId` into the builder's state.-   Every primitive emitted during that call will automatically receive the correct `GizmoTypeId`.

4\. Fix the Router Bottleneck in `DataDrivenGizmoSystem`

This completely eliminates the "first-one-wins" interaction bug. Currently, `DataDrivenGizmoSystem.FindGizmo(Entity entity)` blindly returns the first gizmo in the list (`return list.Instance;`), which causes unrelated gizmos on the same entity to steal each other's clicks.

-   Update the routing method to accept the `GizmoTypeId` from the incoming token: `FindGizmo(Entity entity, uint gizmoTypeId)`.-   The method will now look up the entity's active gizmos and return exactly the one matching the hash: `list.FirstOrDefault(gi => gi.Definition.GizmoTypeId == gizmoTypeId)`.-   **Result:** Gizmo A and Gizmo B attached to the exact same entity can now both emit `SubElementId = 1`. The terminal will send back the click with the respective `GizmoTypeId`, and the router will deliver it perfectly to the correct gizmo. `SubElementId` is now elegantly isolated per gizmo instance.

5\. Fix `StructInspector` ImGui Collisions

With the composite identity established, fixing the ImGui window collision is trivial. In `ImGuiPropertyTreeAdapter.DrawScheduled` on the terminal side, the window title generation currently looks like this: `$"...###StructInsp_{item.NetworkId}"`.

Simply append the schema hash (which acts as the type ID for the struct) or the new `GizmoTypeId` to the ImGui stable ID:

```
string windowTitle = hasSchema
    ? $"{doc!.Root.Name} ({item.NetworkId})###StructInsp_{item.NetworkId}_{item.SchemaHash}"
    : $"Inspector {item.NetworkId} (0x{item.SchemaHash:X})###StructInsp_{item.NetworkId}_{item.SchemaHash}";
```

**Result:** Because the `SchemaHash` (or `GizmoTypeId`) is appended to the ID after the `###`, ImGui will treat multiple `StructInspectors` opened for the same entity as completely separate windows, allowing them to render side-by-side without their UI contexts bleeding into each other.

The current structinspector imgui renderer shows one top level tree item which neef to be expanded to see inner fiels. This top level item seems reduntant. Can it be avoided in this structinspector case? And show inner field right away?

Yes, this redundant top-level tree item can be easily avoided.

The issue occurs because the ImGui window itself is already given the name of the root struct (e.g., "LayerControl"), but the `DrawScheduled` method passes the entire root `EditNode` into the recursive `DrawEditNode` function, which blindly wraps any node with children inside an `ImGui.TreeNode`.

To fix this, you just need to bypass the root node and directly render its children in the **GizmoMap.Presentation.ImGuiPropertyTreeAdapter** class.

Here is how you can implement this gracefully in **ImGuiPropertyTreeAdapter.DrawScheduled**:

**Current Code:**

```
if (ImGui.Begin(windowTitle))
{
    if (hasSchema)
    {
        // This draws the redundant top-level tree node
        DrawEditNode(doc!.Root, item.IsReadOnly); 

        if (!item.IsReadOnly && onStructUpdate != null)
        {
//...
```

**Fixed Code:**

```
if (ImGui.Begin(windowTitle))
{
    if (hasSchema)
    {
        // Bypass the root node and render its children right away
        foreach (var child in doc!.Root.Children)
        {
            DrawEditNode(child, item.IsReadOnly);
        }

        if (!item.IsReadOnly && onStructUpdate != null)
        {
//...
```

Why this works:

Because of how `DrawEditNode` is designed, it evaluates whether to draw a `TreeNode` or a leaf widget (like a Checkbox or Text) based on whether the passed node has children.

By unpacking the root and feeding `doc!.Root.Children` directly into the loop, leaf fields (such as `BaseLayer` or `UnitsLayer`) will immediately render as top-level Checkboxes inside the panel without the extra tree hierarchy. If any of those children happen to be nested structs themselves, they will still correctly render as their own collapsible `TreeNode`s, preserving the proper layout for deeper data structures.

I updated sources. Re read. Check if all ideas still apply with thr nee code. We need to make sure the composite ids are properly documented as comments on proper places and it is used properly by all call sited.

I have reviewed the updated v183 codebase.

The ideas we discussed regarding the **Composite Key Routing Architecture** (`[AnchorId] + [GizmoTypeId] + [SubElementId]`) are absolutely correct and still perfectly apply to the v183 architecture.

However, looking at the newly provided code, **the composite IDs have not yet been implemented.** The codebase is still using the flat ID routing system, which means the collision risks still exist.

Here is exactly what is still missing in v183 and where you need to apply the code changes and documentation comments to properly implement composite IDs:

1\. `GizmoPickToken` and `GizmoInteractionBatch` are missing the new field

You need to update the transport layer to carry the `GizmoTypeId`.

-   **Where to fix:** `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/Sources/GizmoPickToken.cs`.-   **What to add & document:**-   **DDS Transport:** You must also add `public uint PickGizmoTypeId;` to `GizmoInteractionBatch` in `GizmoMap.Network/Topics/GizmoInteractionBatch.cs` to ensure it passes over the network.

2\. `DataDrivenGizmoSystem` still has the "first-one-wins" routing bottleneck

Currently, `FindGizmo` completely ignores which gizmo was clicked and just grabs the first one in the list.

-   **Where to fix:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs`.-   **What to add & document:** Change the signature from `FindGizmo(Entity entity)` to `FindGizmo(Entity entity, uint gizmoTypeId)` and update the routing logic.-   _(Note: You will also need to update the event routing calls in_ _RouteInteractionEvents_ _to pass_ _evt.Token.GizmoTypeId_ _into this method)._

3\. `DebugPrimitive` is missing the `GizmoTypeId` payload

To route events back, the primitives drawn on the screen must know their `GizmoTypeId`.

-   **Where to fix:** `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/Primitives/DebugPrimitive.cs`.-   **What to add:** You must add `[FieldOffset(48)] public uint GizmoTypeId;` to the struct layout. (Offset 48-51 is currently free padding before `SubElementId` at 52).

4\. `ImGuiPropertyTreeAdapter` still suffers from Window ID Collisions

Currently, multiple `StructInspectors` on the same entity will bleed into each other because their ImGui window IDs are identical.

-   **Where to fix:** `FDP/ExtDeps/GizmoMap/GizmoMap.Presentation/UI/ImGuiPropertyTreeAdapter.cs`.-   **What to add & document:** You need to append the `SchemaHash` to the stable ImGui ID string so ImGui treats them as distinct windows.

**Summary:** The foundational logic for your requested `ClusterRunner` generic subsystems is great in v183, but to achieve the "reliable and elegant" isolation you requested for Gizmo interactions, you still need to physically implement the `GizmoTypeId` into the networking contracts and ECS routing methods listed above.

