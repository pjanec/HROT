The Hrot.SimHost.Gizmos.EntityRotatorGizmo is not called when i pick Rotate from component inspector menu. There seems to be still the original entity rotator. Pls check what other stuf are not yet replaced with gizmos. I want to erradicate all non-gizmo map control


The reason the new `EntityRotatorGizmo` is not activating is because the context menus across the subsystems are still hardcoded to bypass the gizmo pipeline and push the legacy `IMapTool` onto the canvas stack. 

If you look at the context menu registrations in `SimHostVisualization.cs`, `IgApplication.cs`, `CgfSubsystem.cs`, and `EditorApplication.cs`, you will find variations of this explicit coupling:
```csharp
builder.AddItem("Rotate", () => _map?.PushTool(new Hrot.ScenarioEditor.Tools.EntityRotationTool(entity, _world!)));
```

Furthermore, for the new `EntityRotatorGizmo` to be managed by `DataDrivenGizmoSystem` natively, it requires an ECS activation trigger. Right now, it lacks a `[GizmoProjector]` attribute. 

To properly wire this in the ECS-driven architecture:
1. Define a zero-byte marker component: `public struct ActiveRotationToolRequest { }`
2. Annotate the gizmo: `[GizmoProjector(typeof(SimTransform), typeof(ActiveRotationToolRequest))]`
3. Change the context menu callback to simply add the component: `repo.AddComponent<ActiveRotationToolRequest>(entity);`
4. Inside the gizmo's `_onRemove` callback, remove the component so the `DataDrivenGizmoSystem` handles deterministic teardown.

### Remaining Legacy Map Controls to Erradicate

To achieve a 100% gizmo-based interaction model, we need to purge the entire `IMapTool` ecosystem and the `MapCanvas.PushTool()` paradigm. Based on the sources, the following legacy tools are still active and need to be rewritten as pure stateful/stateless gizmos:

**1. Vertex & Shape Manipulation (`Hrot.ScenarioEditor.Tools`)**
*   `EditTool`: Still used to drag vertices of `EditablePolyline` components. Needs to be replaced by the `VertexEditGizmo` pattern we proved out in the `GizmoMap.Example`.
*   `RouteEditTool`: Handles route waypoint manipulation, including inserting/deleting points via right-click.

**2. Spatial Drag & Drop**
*   `EntityDragTool`: Currently pushed onto the canvas by the `StandardInteractionTool` when a mouse drag exceeds a 5-pixel threshold. This should be an `IEntityStatefulGizmo` reacting to `GizmoDragUpdateEvent`.

**3. Creation & Authoring Tools (`Hrot.Editor.Tools`)**
*   `CreationTool`: The modal entity spawner.
*   `AreaPlacementTool` & `RoutePlacementTool`: Used for tactical graphics.
*   `ObstaclePlacementTool`: Used by the zone editor.
These should be migrated to global `IStatefulGizmo` tools managed by the interaction manager.

**4. Asynchronous Picking & Measurement (`Hrot.Editor.Tools` & `Hrot.ScenarioEditor.Tools`)**
*   `MeasureTool`: Currently, the architecture uses a nasty `MeasureToolGizmoAdapter` that listens to the `GizmoSettingsRegistry` just to push the legacy `MeasureTool` onto the canvas. This needs to be a pure `IStatefulGizmo`.
*   `EntityPickerTool`, `LocationPickerTool`, and `ModalBoxSelectionTool`: Used heavily by the `IMapPickService` async facades. 

**5. The Legacy Input Router**
*   `StandardInteractionTool`: This is the biggest architectural violator remaining. It still sits at the base of the `MapCanvas` and intercepts raw Raylib clicks, hover states, and drag gestures, dispatching events like `OnEntitySelectRequest` and `OnRegionSelected`. 

Because `StandardInteractionTool` is still active, your input pipeline is currently split: some inputs go through the new `GizmoInteractionEgressSystem`, while basic selection and dragging are still trapped in the old canvas tool stack. Eradicating `StandardInteractionTool` entirely and moving selection logic into an ECS system that consumes `GizmoMouseEvent` will unify the pipeline.



lets detail the erradication process. pls write an outline of the transformation docuemnt

Here is the architectural transformation outline for completely eradicating the legacy `IMapTool` ecosystem and transitioning to the pure, ECS-driven GizmoMap framework. 

### 1. Phase 1: Context Menu Decoupling & Marker Components
We must sever the hardcoded UI-to-tool instantiation links currently embedded in the context menus.

*   **Current State:** Subsystems (Editor, IG, SimHost) push tools directly to the canvas stack (e.g., `_canvas?.PushTool(new EntityRotationTool(...))`). 
*   **Action:** 
    *   Introduce zero-byte ECS marker components (e.g., `ActiveRotationToolRequest`, `ActiveVertexEditRequest`).
    *   Refactor all context menu lambdas (`LambdaEntityContextMenuHandler` and `JsonEntityContextMenuHandler`) to simply add the corresponding marker component to the target entity.
    *   Update `DataDrivenGizmoSystem` to automatically instantiate the matching `IEntityStatefulGizmo` when the marker component appears, and remove the marker when the gizmo is disposed.

### 2. Phase 2: Purging the Geometry Manipulation Tools
The vertex and waypoint editors hold state and evaluate input, which violates the stateless presentation tier.

*   **Current State:** `EditTool` manages a ghost vertex list and handles drag/commit logic for `EditablePolyline`. `RouteEditTool` duplicates this pattern for `RoutePlan` waypoints.
*   **Action:**
    *   Implement `VertexEditGizmo` and `RouteWaypointGizmo` as `IEntityStatefulGizmo` instances.
    *   Use `GizmoPickToken.SubElementId` to uniquely identify the dragged vertex/waypoint without allocating ghost lists.
    *   On `GizmoInteractionCommitEvent`, dispatch an `UpdateEntityCommand` to flush the new geometry to the network, and self-destruct if the interaction is complete.

### 3. Phase 3: Migrating Creation & Authoring Tools
Entity placement and tactical graphics authoring are global interactions not bound to an existing entity.

*   **Current State:** `CreationTool`, `AreaPlacementTool`, `RoutePlacementTool`, and `ObstaclePlacementTool` are pushed onto the canvas to intercept raw clicks and generate `SpawnEntityCommand` events.
*   **Action:**
    *   Implement these as global `IStatefulGizmo` tools managed by the `GizmoInteractionManager`.
    *   Request exclusive focus (`RequiresExclusiveFocus = true`) so the terminal routes all clicks directly to them.
    *   Upon completion, emit the spawn command to the ECS bus and invoke `Dispose()`.

### 4. Phase 4: Refactoring Asynchronous Picking Services
The `IMapPickService` relies heavily on pushing temporary modal tools to intercept coordinates and entity IDs.

*   **Current State:** `CanvasMapPickAdapter` pushes `LocationPickerTool`, `EntityPickerTool`, and `ModalBoxSelectionTool` which wrap `TaskCompletionSource`.
*   **Action:**
    *   Convert the pickers into transient `IStatefulGizmo` instances. 
    *   The tools will hold exclusive focus, intercept the next `GizmoMouseEvent` or `GizmoInteractionStartedEvent`, resolve the `TaskCompletionSource`, and self-dispose.

### 5. Phase 5: Eradicating the Input Router (`StandardInteractionTool`)
This is the central pillar of the legacy design, intercepting raw inputs to dispatch selection and drag events.

*   **Current State:** `StandardInteractionTool` parses raw Raylib input, manages selection state, and spawns `EntityDragTool`. This splits the input pipeline between the canvas layer and the new `GizmoInteractionIngressSystem`.
*   **Action:**
    *   Delete `StandardInteractionTool` entirely.
    *   Move selection logic (clicking, shift-clicking, box selection) into a standard ECS system (`SelectionInteractionSystem`) that consumes `GizmoMouseEvent` from the event bus.
    *   Convert spatial dragging (`EntityDragTool`) into an `IEntityStatefulGizmo` that reacts strictly to `GizmoDragUpdateEvent`.

### 6. Phase 6: Dismantling the MapCanvas Tool Stack
Once all tools are migrated, the canvas abstraction can be stripped down to a pure rendering surface.

*   **Current State:** `MapCanvas` manages `_toolStack`, `ActiveTool`, and routes inputs sequentially through them. `IMapTool` defines the lifecycle `OnEnter`/`OnExit`.
*   **Action:**
    *   Delete the `IMapTool` interface completely.
    *   Remove `PushTool`, `PopTool`, `SwitchTool`, and `ActiveTool` from `MapCanvas`.
    *   The `MapCanvas` will now only process pure panning/zooming via its internal camera, leaving all other interaction strictly to the Gizmo network transport layer.


phase 1

To decouple the presentation UI from imperative tool instantiation, we must transition to a declarative, data-oriented pipeline. Currently, context menu handlers like `LambdaEntityContextMenuHandler` explicitly allocate and push tools onto the presentation canvas, seen where "Rotate" triggers `_map?.PushTool(new Hrot.ScenarioEditor.Tools.EntityRotationTool(entity, _repo!))`. This creates an unacceptable direct dependency between the UI layout and the interactive map tools.

Here is the exact mechanical breakdown for Phase 1 to route this through the data-driven gizmo architecture:

**1. Introduce Zero-Byte Marker Components**
We define pure ECS marker structs to represent an operator's intent to interact with a specific tool, such as `ActiveRotationToolRequest` or `ActiveVertexEditRequest`. This shifts the activation trigger entirely into the simulation's component model.

**2. Refactor Context Menu Handlers**
We strip the `PushTool` logic out of the `SharedContextMenuPopulator` and all subsystem-specific menu handlers. Instead of instantiating a tool, the callbacks provided to the `IContextMenuBuilder` will strictly mutate ECS state by adding the corresponding marker component to the target entity. 

**3. Enforce Invariants via Gizmo Definitions**
Stateful gizmos like `EntityRotatorGizmo` implement `IEntityStatefulGizmo`. To bind them to the ECS, we define an `IGizmoDefinition` rule that explicitly demands the presence of both the target domain data (e.g., `SimTransform`) and the new marker component (`ActiveRotationToolRequest`). This definition is registered with the `GizmoRegistry` at application startup.

**4. Data-Driven Instantiation and Deterministic Teardown**
The `DataDrivenGizmoSystem` already monitors the ECS for structural changes and evaluates entity component masks against registered rules. 
*   **Activation:** When the context menu adds the marker component, the system observes the change and automatically instantiates the `IEntityStatefulGizmo`. 
*   **Teardown:** When the interaction is committed or canceled, the gizmo simply removes the marker component from the entity. The `DataDrivenGizmoSystem` observes that the component mask no longer satisfies the rule and deterministically invokes `Dispose()` to tear down the state machine.

By converting tool activation from an imperative UI-driven call stack into a data-driven state change, the presentation layer remains completely stateless and oblivious to the interaction logic.




phase 2

The fundamental flaw with the legacy `EditTool` and `RouteEditTool` is that they violate the stateless presentation boundary by allocating in-memory `_ghostPoints` lists to hold transient geometry during an edit session. This traps the domain state in the UI layer and splits the input routing logic. We eradicate this temporal coupling by pushing the vertex manipulation entirely into the ECS-driven gizmo pipeline. 

Here is the mechanical breakdown for Phase 2:

**1. Stateless Hit-Testing via Sub-Elements**
Instead of the presentation tool hoarding ghost geometry and doing distance checks against the mouse, we implement `VertexEditGizmo` and `RouteWaypointGizmo` as `IEntityStatefulGizmo` implementations. Inside the `UpdateAndDraw` loop, the gizmo emits `DebugPrimitiveShape.Box2D` primitives for each vertex. 

Crucially, we assign the `SubElementId` of each primitive to `vertexIndex + 1` (where 0 is reserved for non-interactive elements). This delegates the hit-testing entirely to the terminal's `DebugGizmoLayer`. When the operator clicks a vertex, the terminal generates a `GizmoPickToken` containing both the entity `AnchorId` and the specific vertex `SubElementId`. 

**2. Shared-Focus Event Routing**
Because vertex editing does not require intercepting every keystroke, these gizmos declare `RequiresExclusiveFocus = false`. The `DataDrivenGizmoSystem` routes strictly typed `GizmoDragUpdateEvent` and `GizmoInteractionCommitEvent` structs via the event bus directly to the active gizmo using O(1) token matching. The gizmo's `OnDragUpdate` and `OnCommit` callbacks receive the exact sub-element ID and a strictly typed `Vector3` world position.

**3. Deterministic Data Mutation and Egress**
When the `OnCommit` callback fires, the gizmo writes the final geometry straight into the ECS component, preserving the zero-allocation requirement for hot-path systems. The network synchronisation relies strictly on data-driven triggers rather than imperative UI callbacks:
*   **Routes (`RoutePlan`):** The gizmo calls the `Mutate()` method on the component, which automatically increments the `Version` stamp. Downstream, the `MapRouteEgressTranslator` detects this version bump and seamlessly publishes the `dtMapRoute` DDS topic.
*   **Shapes (`EditablePolyline`):** The gizmo modifies the points and publishes an `UpdateEntityCommand` to the local bus. The `UpdateEntityCommandEgressTranslator` intercepts this, translates the relative Cartesian offsets to geodetic coordinates, and serialises the `dtMapVisualOverlay` DDS payload.

By moving to this architecture, we completely eliminate the `MapCanvas` tool stack for geometric edits, relying solely on network-stable pick tokens and data-oriented state propagation.


phase 3

Phase 3 completely decouples our global authoring interactions from the presentation canvas. Currently, tools like `CreationTool`, `AreaPlacementTool`, `RoutePlacementTool`, and `ObstaclePlacementTool` rely on the imperative `IMapTool` interface and manipulate the `MapCanvas` tool stack directly. This forces the UI layer to govern domain-creation logic. 

We will eradicate this by migrating these authoring mechanics into pure `IStatefulGizmo` implementations managed by the `GizmoInteractionManager`. 

Here is the mechanical breakdown of the transformation:

**1. Transition to Exclusive Focus FSMs**
Authoring tools operate globally on the map rather than manipulating an existing entity. To achieve this, the new gizmos (e.g., `EntityPlacementGizmo`, `ZoneObstacleGizmo`) will implement `IStatefulGizmo` and explicitly declare `RequiresExclusiveFocus => true`. 

When the UI requests placement mode, the host instantiates the gizmo and adds it to the `GizmoInteractionManager`. The manager automatically emits an `InputCaptureBinding(exclusive: true)` meta-primitive. This guarantees the stateless presentation terminal will bypass spatial hit-testing and blindly route all `RawInput` events directly to the gizmo's `OnMouseEvent` and `OnKeyEvent` handlers.

**2. Stateless Visual Previews**
Instead of the legacy tool tracking mouse state to draw a preview, the gizmo implements `UpdateAndDraw(float deltaTime, IGizmoDrawBuilder draw)`. The gizmo caches the latest cursor position received from `GizmoDragUpdateEvent` or raw mouse movements. During the draw pass, it emits immediate-mode primitives—like a ghost circle for entity placement or a cylinder for obstacle placement—directly into the builder, keeping the presentation layer ignorant of the tool's intent.

**3. Deterministic Egress and Commit**
The legacy `CreationTool` intercepts a click, fires an injected delegate to dispatch a `SpawnEntityCommand`, and manually calls `_canvas?.PopTool()` to tear itself down. 

In the new architecture, the gizmo's `OnMouseEvent` evaluates the left-click. Upon commit, it publishes the exact same `SpawnEntityCommand` (carrying the TKB type and Cartesian world position) straight to the local ECS event bus. This seamlessly feeds the existing `NetworkSpawningSystem` without requiring any callbacks injected by the UI layer.

**4. Lifecycle Teardown via `IDisposable`**
We completely abandon the `OnEnter`, `OnExit`, `PushTool`, and `PopTool` canvas mechanics. 
*   **Success:** After publishing the spawn command, the gizmo executes its teardown logic and invokes an injected `_onRemove` action that tells the `GizmoInteractionManager` to drop the instance.
*   **Cancellation:** If `OnKeyEvent` receives an `Escape` key press, or `OnMouseEvent` receives a right-click, the gizmo safely aborts the operation and invokes the same removal action.

Once the manager drops the tool, it calls `Dispose()` on the gizmo and clears the exclusive focus lock, cleanly restoring normal map interaction without the presentation layer ever knowing a tool was active.


phase 4

To eradicate the legacy canvas tool stack from our asynchronous picking services, we must transition `CanvasMapPickAdapter` and `EditorMapPickAdapter` away from pushing modal `IMapTool` instances like `LocationPickerTool` and `EntityPickerTool` onto the `MapCanvas`. Instead, we will bind the `TaskCompletionSource` lifecycle directly to transient, exclusive-focus `IStatefulGizmo` implementations.

Here is the exact mechanical breakdown for Phase 4:

**1. Implement Transient Picker Gizmos**
We replace the legacy tools with new implementations (e.g., `LocationPickerGizmo`, `EntityPickerGizmo`, and `AreaPickerGizmo`) that strictly implement the `IStatefulGizmo` contract. 
*   These gizmos must declare `RequiresExclusiveFocus => true` so the `GizmoInteractionManager` automatically emits the `InputCaptureBinding` meta-primitive, routing all raw hardware events directly to the gizmo and bypassing spatial hit-testing on the terminal.
*   We inject the `TaskCompletionSource<T>` and an `Action _onRemove` delegate directly into the gizmo's constructor.

**2. Task Resolution via Explicit Input Semantics**
We eliminate the canvas event-routing boilerplate and evaluate input strictly through the `IGizmoInteractionHandler` interface.
*   **Success Path:** Inside `OnMouseEvent`, we evaluate `MapMouseButton.Left` and `isPressed == false` (release). We resolve the `TaskCompletionSource` using the provided `worldPos`, and immediately invoke `_onRemove()` to drop the focus lock and tear down the FSM.
*   **Cancellation Path:** We evaluate `MapMouseButton.Right` in `OnMouseEvent` or `MapKeyboardKey.Escape` in `OnKeyEvent`. Upon detection, we invoke `tcs.TrySetCanceled()` and call `_onRemove()`.

**3. Refactoring the Pick Adapters (`GizmoMapPickAdapter`)**
We rewrite the map pick adapters to completely decouple from `MapCanvas.PushTool` and `MapCanvas.PopTool`.
*   When `PickLocationAsync` or `PickEntityAsync` is called, the adapter generates a transient stable `AnchorId` (e.g., using `Guid.NewGuid().GetHashCode()`).
*   It instantiates the specific picker gizmo and registers it via `GizmoInteractionManager.AddTool(anchorId, gizmo)`.
*   The `CancellationTokenRegistration` is updated to invoke `manager.RemoveTool(anchorId)` instead of popping the canvas stack, guaranteeing deterministic teardown if the task is cancelled externally.

**4. Stateless Visual Feedback**
Legacy tools relied on tracking `_mouseWorldPos` during `HandleHover` to draw their crosshairs. In the gizmo architecture, we eliminate this state.
*   The picker gizmo implements `UpdateAndDraw(float deltaTime, IGizmoDrawBuilder draw)`.
*   It uses the last known cursor position (captured from raw input streams or `GizmoDragUpdateEvent`) and calls `draw.DrawLine` and `draw.DrawSphere` to emit the crosshair primitives directly into the transient buffer. This guarantees the presentation layer has zero awareness of the picking context.


phase 5

Phase 5 dismantles the `StandardInteractionTool`, which currently acts as a monolithic input router trapped in the presentation layer. Right now, it intercepts raw hardware inputs to manage selection state and imperatively spawns transient canvas tools like `EntityDragTool` or `BoxSelectionTool` when drag thresholds are crossed. This creates a split brain in our input pipeline.

Here is the mechanical breakdown to unify the input architecture:

**1. Delete the God Class**
We entirely delete `StandardInteractionTool`. The canvas abstraction must stop intercepting raw Raylib clicks, hover states, and drag gestures to emit events like `OnEntitySelectRequest` and `OnRegionSelected`. All input will flow exclusively through the network-agnostic `GizmoInteractionBatch` pipeline.

**2. ECS-Driven Selection**
We introduce a standard `SelectionInteractionSystem` running in the execution pipeline. Instead of UI callbacks, this system simply drains strongly-typed `GizmoMouseEvent` and `GizmoKeyEvent` structs from the event bus. When a left-click `GizmoMouseEvent` arrives with a valid `PickToken`, the system executes the selection logic by mutating the `SelectionState` component directly, handling modifiers like Shift and Ctrl for multi-selection.

**3. Migrate Spatial Dragging to Gizmos**
The legacy `EntityDragTool` is currently pushed onto the canvas stack when a drag exceeds a 5-pixel threshold. We convert this into a pure `IEntityStatefulGizmo`. It will react strictly to `GizmoDragUpdateEvent` to update its visual preview, and `GizmoInteractionCommitEvent` to finalise the drop. The gizmo writes the new Cartesian coordinates straight into the `SimTransform` component and lets the `GeoSpatialEgressTranslator` detect the delta and publish the network update without any UI layer involvement.

**4. Re-architect Box Selection**
The `BoxSelectionTool` becomes a global `IStatefulGizmo` managed by the `GizmoInteractionManager`. It evaluates `GizmoDragUpdateEvent` to dynamically draw the 2D selection rectangle using immediate-mode primitives via `IDebugDrawBuilder`. Upon `GizmoInteractionCommitEvent`, it executes the spatial query and emits the selected entity IDs to the new selection system.

**5. Decouple Global Hotkeys**
Currently, pressing the Delete key triggers an `OnDeleteRequested` C# event routed through the canvas tool. We move this into the ECS. A dedicated system will evaluate `GizmoKeyEvent` for `MapKeyboardKey.Delete`. Upon detection, it queries all entities where `SelectionState.IsSelected` is true and publishes a `DestroyEntityCommand` for each to the local bus, fully decoupling deletion logic from the presentation layer.



phase 6

Phase 6 is the final architectural purge where we strip the `MapCanvas` down to a pure, stateless rendering surface. Now that interaction logic is entirely managed by the ECS gizmo pipeline, the canvas should no longer act as an imperative finite state machine for input routing.

Here is the exact mechanical breakdown:

**1. Delete the `IMapTool` Contract**
We completely eradicate the `IMapTool` interface. The presentation layer no longer needs to define stateful lifecycle methods like `OnEnter` and `OnExit`, nor imperative input callbacks like `HandleHover`, `HandleClick`, or `HandleDrag`. 

**2. Gut the Canvas State Machine**
We remove the `_toolStack` collection and the `ActiveTool` property from the `MapCanvas`. Consequently, the imperative UI mutation methods `PushTool`, `PopTool`, and `SwitchTool` must be deleted entirely. Interaction state is now exclusively tracked by ECS FSMs and the `GizmoInteractionManager`.

**3. Sever Tool Input Routing**
In `MapCanvas.ProcessInputPipeline()`, we strip out all tool-priority routing blocks. Currently, this pipeline explicitly drains the keyboard queue and routes hover, press, drag, and click events sequentially to the `ActiveTool` before it ever evaluates the camera or underlying layers. By removing this, we stop the canvas from intercepting and consuming raw hardware input that belongs in the network-agnostic interaction boundary.

**4. Reduce to a Pure Rendering Surface**
Once the tool stack is removed, `MapCanvas` achieves a single, cohesive responsibility. Its `Update` and `ProcessInputPipeline` methods will strictly handle camera pan/zoom interpolation (`Camera.HandleInput`) and coordinate mapping. Its `Draw` method will only evaluate the `ActiveLayerMask` to iterate the `_layers` list from bottom to top, emitting pure visual primitives through the injected `IDebugDrawBuilder`.

This yields a strictly unidirectional data flow. The canvas knows nothing about interaction intent; it merely projects the current visual state of the ECS, while hardware input flows seamlessly up to the domain via the `GizmoInteractionBatch` transport pipeline.
