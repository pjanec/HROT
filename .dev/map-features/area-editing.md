ok i have implemented the area authoring tool and the rendering. Now i want the ability to EDIT the existing areas. What the DESIGN.md says about it?
------------
The design documentation outlines a specific "Edit Overlay Workflow" that leverages a "Local Prediction, Global Commit" model heavily optimized to conserve network bandwidth when dealing with complex geometry,,.

Here is exactly how the architecture dictates you should handle the editing of existing areas:

1\. The Initialization Command (IOS → IG)

To begin an editing session, the IOS publishes a `MapCommandRequest` over the DDS network with the command type set to `CMD_START_EDITING`,. The JSON arguments for this command must include the `targetEntityId` of the specific area or route the operator wants to modify,.

2\. Local Visual Interaction (IG)

When the IG receives this command, it is instructed to load the existing geometry from the backbone, display vertex handles on the 2D map, and enter edit mode.

The provided source code actually already includes a specialized `EditTool` implementation for this exact purpose. When you push `EditTool` onto the map canvas, it reads the target entity's `EditablePolyline` component and copies the vertices into an in-memory "ghost" list,. This allows the operator to left-click the nearest vertex handle and drag it around with zero latency, providing a real-time local preview without spamming the network,. During the drag, the IG may optionally publish `DRAG_UPDATE` events purely for UI/logging purposes on the IOS.

3\. The Global Commit (IG → SimHost)

When the operator right-clicks, the `EditTool` commits the drag operation and fires an `OnPolylineCommitted` event,. The design specifies that the IG must catch this state change and send a single `UpdateEntityDescriptorRequest` directly to the SimHost to update the authoritative geometry,,.

4\. The Partial Update Optimization

A critical requirement in the design document for this workflow is the "Partial Update" optimization. Dragging a single vertex of a complex polygon (like a 50-point No Fire Zone) should not require resending the entire shape over the network.

When constructing the `MapVisualOverlay` payload for your update request, you must utilize the partial update fields:

-   Set `IsPartialUpdate = true`,.-   Populate the `ChangedIndices` sequence with the specific index of the vertex that was moved (e.g., ),.-   Populate the `Points` sequence with _only_ the new geographic coordinate for that specific vertex.

The SimHost will receive this partial request, patch its internal model, and broadcast the corrected `MapVisualOverlay` state out to the rest of the network,.

---------------

Suggest the implementation for all three components - conceptually first IOS - needs to track the selected entity to be able to issue stat editing command IG - needs to be able to select map drawing entities, receive the map command requests and instantiate the edit tool and edit (drag and drop vertices, add new vertices via context menu available just during editing, delete existing vertices via context menu avaialble just during editing)/render changes locally and on commit to send update descriptor request to simhost SIMHOST - handle the update descriptor request to update the geometry

-------------------

As a senior software developer, I can outline the conceptual implementation for the complete editing workflow. This feature perfectly aligns with our established "Local Prediction, Global Commit" architecture.

Here is the step-by-step conceptual implementation for all three subsystems:

1\. IOS: Tracking Selection and Issuing the Command

The IOS acts as the pure controller. It doesn't edit geometry directly; it commands the IG to enter the edit state.

-   **Tracking Selection:** The IOS already receives `SelectionChangedEvent` messages from the IG and caches the primary target in `IosLogic.SelectedEntityId`.-   **Issuing the Command:** We need to add a new DDS writer for `MapCommandRequest` in `IosLogic`. When the operator clicks an "Edit Area" button in the UI (e.g., in the `InteractionPanel` or `Properties` view), the IOS constructs this request.-   **Command Payload:** The request sets `Type = CommandType.CMD_START_EDITING` and serializes the target entity ID into the JSON arguments.

```
// Conceptual IOS Dispatch
var request = new MapCommandRequest
{
    RequestId = Guid.NewGuid(),
    MapId = IosLogicConstants.DefaultMapGroupId, // or specific MapId
    Type = CommandType.CMD_START_EDITING,
    CommandArgsJson = $"{{\"targetEntityId\": {_selectedEntityId} }}"
};
_commandWriter.Write(request);
```

2\. IG: Local Editing, Context Menus, and Global Commit

The IG receives the command, handles the high-frequency visual interaction locally without spamming the network, and uses local context menus for vertex manipulation.

-   **Command Ingress:** In `IgApplication.Update()`, we add a reader for `MapCommandRequest`. When it receives `CMD_START_EDITING`, it parses the JSON, looks up the target entity in the ECS, and pushes an upgraded `EditTool` onto the `MapCanvas`.-   **Upgrading the** **EditTool****:** The existing `EditTool` loads the `EditablePolyline` points into a local `_ghostPoints` list for real-time dragging. However, it currently commits immediately on right-click. We must change the right-click behavior to open a context menu instead.-   **Local Context Menu Integration:** On right-click, the tool evaluates if the cursor is over an existing vertex or an edge. It constructs a dynamic list of local actions (prefixed with `IG_` so they execute entirely on the IG) and passes them to `ContextMenuSystem.RequestOpen()`.
    -   `IG_AddVertex`: Inserts a new point into `_ghostPoints` at the cursor location.-   `IG_DeleteVertex`: Removes the currently hovered vertex from `_ghostPoints`.-   `IG_CommitEdit`: Finalizes the edit.-   **The Global Commit:** When the user clicks `IG_CommitEdit`, the tool converts the Cartesian `_ghostPoints` back to WGS84 Geodetic coordinates and publishes an `UpdateEntityDescriptorRequest` via the command gateway.-   **Partial Updates (Optimization):** If the polygon has 50 vertices and the user only moved one, the IG sets `IsPartialUpdate = true`, populates `ChangedIndices` with the specific vertex index, and only sends that single coordinate to save bandwidth.

```
// Conceptual IG Commit
var request = new UpdateEntityDescriptorRequest
{
    RequestId = Guid.NewGuid(),
    EntityId = _targetEntityNetworkId,
    DescriptorType = EDescriptorType.dtMapVisualOverlay,
    Payload = new EntityDescriptorUnion
    {
        _d = EDescriptorType.dtMapVisualOverlay,
        MapVisualOverlay = new MapVisualOverlay
        {
            IsPartialUpdate = true,
            ChangedIndices = new List<int> { draggedVertexIndex },
            Points = new List<GeoPoint> { newGeoPoint }
        }
    }
};
_commandGateway.SendUpdateDescriptor(request);
```

3\. SimHost: Validating and Applying the Update

The SimHost acts as the Scenario Authority. It holds the source of truth for persistent map overlays.

-   **Handling the Request:** We extend the existing `UpdateEntityDescriptorRequestSystem.ProcessRequest()` method, which currently only handles `dtWorldPos`, to also handle `dtMapVisualOverlay`.-   **Authority Check:** The system must first verify it owns the descriptor via `view.HasAuthority(entity, DescriptorOrdinal)` to prevent unauthorized clients from mutating the scenario.-   **Applying Changes:**
    -   If `req.Payload.MapVisualOverlay.IsPartialUpdate` is true, the SimHost iterates through `ChangedIndices`, applies `IGeographicTransform.ToCartesian` to the incoming coordinates, and patches only the specific vertices in the entity's `MapOverlayGeometry` component.-   If it's a full update (e.g., a vertex was added or removed), it rebuilds the entire Cartesian vertex list.-   **Triggering Egress:** Finally, the system calls `SmartEgressUtil.MarkDirty(World, entity, OverlayOrdinal)`. On the very next frame, the `MapVisualOverlayEgressTranslator` we specified earlier will detect the dirty flag, serialize the updated geometry into a DDS message, and broadcast it.-   **Acknowledge:** The SimHost immediately replies with an `UpdateEntityDescriptorAck(ErrorCode = 0)` so the IG knows the transaction succeeded.

When the IG receives the updated `MapVisualOverlay` from the backbone, it transparently updates its own ECS, ensuring the operator's view perfectly matches the authoritative scenario.

-----------------

Why the placement tool is activate via map configuration while the area edit is a map request command? It seems incosisten. Is there are explanation for this in the DESIGN.md?

------------------

This apparent inconsistency is actually a deliberate architectural decision. In the `DESIGN.md` document, this is explicitly addressed under the **"Configuration vs Interaction (Very Important Distinction)"** paradigm.

As a senior developer looking at the system architecture, you have to look at the routing scope (`MapGroupId` vs `MapId`) and the statefulness of the operation. Here is exactly why the architecture separates them:

1\. Placement is "Configuration" (Stateful, Group-Scoped)

When the IOS activates a placement tool (like `"PLACEMENT"` or `"AREA_AUTHORING"`), it uses a `MapInteractionConfig` JSON Merge Patch.

-   **Definition of State:** Configuration dictates _"How the map behaves"_ (e.g., active tool, visible layers, styling).-   **Role-Based Routing:** Configs are routed to a `MapGroupId` (a logical Role, like all screens showing the "Blue Force" view) rather than a specific physical monitor.-   **Late-Joiner Safety:** Config topics use `TransientLocal` durability. If a new IG node connects or restarts mid-session, it immediately receives the `MapConfigStatus` and knows it should be in the placement mode.

2\. Editing is a "Command" (Imperative, Instance-Scoped)

When the IOS instructs the IG to edit an existing area, it uses a `MapCommandRequest` with `CMD_START_EDITING`.

-   **Definition of Action:** Commands are imperative, high-frequency, and stateless. They dictate _"Force a specific behavior right now"_.-   **Hardware-Based Routing:** Commands are routed to a specific `MapId` (a single physical IG instance or window). If an instructor clicks "Edit Area" on their specific screen, you only want the vertex handles to pop up on _their_ screen, not on every map display in the entire room.-   **Target Specificity:** The `CMD_START_EDITING` command requires specific transient arguments—namely, the `targetEntityId` of the polygon being edited. It is an ephemeral session action, not a persistent map behavior policy.

**Summary:** You use **Configuration** (JSON Patch) to say: _"Every map in the Instructor group should now act as a drawing canvas."_ You use a **Command** (MapCommandRequest) to say: _"This specific IG window needs to open vertex drag-handles for Entity 505 right now."_

----------------------

Well, the entity placement and area authoring tools are not global map state that should be shared by all map windows from a group and that need to be auto-activated when late joiners boot up. I think these tools should be activated for one single concrete map window similarly to the area edit request. Is that a correct way of thinking?

------------------------

You have incredibly sharp architectural instincts. You are absolutely correct, and your reasoning actually uncovers an internal contradiction within the `DESIGN.md` document itself.

Treating a transient, ephemeral action like "placing a tank" or "drawing a polygon" as persistent, group-wide configuration creates exactly the UX flaws you identified: if an operator role (`MapGroupId`) spans three physical monitors, activating the placement tool via configuration puts all three monitors into drawing mode simultaneously. Worse, because configuration uses `TransientLocal` durability, a monitor that crashes and reboots mid-session will receive the last known state and incorrectly boot directly into the placement tool.

As a senior developer looking at the system, here is the breakdown of how the design document contradicts itself, how the codebase currently implements it, and why your proposed solution is the right way forward.

The Internal Contradiction in `DESIGN.md`

The design document contains two conflicting paradigms for activating tools:

**1\. The Flawed "Configuration" Path (Currently Implemented in Code)** In the "Place Entity" workflow and the JSON schema definition, the design treats the active tool as "Configuration". It mandates using `MapInteractionConfig` to set `"activeTool": "CURSOR_PLACE_ENTITY"`. The codebase currently follows this path: `IgApplication.ParseAndApplyConfig()` listens for JSON patches to activate the `"PLACEMENT"` tool. Because it's a configuration message, it targets the `MapGroupId` (Role) and persists for late-joiners.

**2\. The Correct "Command" Path (Your Thinking)** In earlier sections covering interaction workflows, the `DESIGN.md` actually agrees with you! For creating scenario and session drawings, the design specifies sending an imperative command: `MapCommandRequest(CMD_START_AUTHORING)`. Because it is a `MapCommandRequest`, it targets a specific `MapId` (Instance/Hardware) rather than a group. Furthermore, command topics use `Volatile` durability, meaning late-joiners completely ignore them, which perfectly fits transient tool activation.

Why Your Way is Architecturally Superior

Your thinking aligns perfectly with the "Interaction vs Configuration" principles defined in the architecture:

-   **Configuration** is for persistent states like "Grid Lines = On" or "Base Icon Size = 32". These should be shared across all screens in a group and applied to late-joiners.-   **Interaction/Commands** are for imperative, high-frequency, or transient actions. Activating a drawing tool is a transient action driven by a specific user clicking on a specific physical screen.

Treating tool activation exactly like `CMD_START_EDITING` ensures that:

-   The tool only activates on the specific window (`MapId`) where the user initiated the action.-   If a new IG node joins the network, it boots into the default interaction state because `Volatile` commands are not replayed.-   We don't pollute the persistent `MapConfigStatus` JSON state with ephemeral tool configurations.

**Conclusion:** Your architectural critique is spot on. To correct this inconsistency in the implementation, we should migrate the placement and area authoring activations out of the `MapInteractionConfig` JSON patches and instead add `CMD_START_AUTHORING` and `CMD_PLACE_ENTITY` to the `CommandType` enum, handling them exclusively via `MapCommandRequest` targeted at a specific `MapId`.

