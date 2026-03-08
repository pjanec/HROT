# sample.IsValid issue

Some places processing dds samples check sample.IsValid even before testing the instance state.
because disposal sample have sample.IsValid==false, the disposal migh not be detected at all!

# sample.Data does not throw from disposal samples

Some older implementation of cyclone dds had a bug where sample.Data was throwing exception
if sample.IsValid==fasle




# Mission commit times out

"Selection & Mission" panel is empty no matter what entity i select on the map. It is not empty if i create the entity via the "ORBAT tree"
UI. Pls explain what is needed to show the entity in "Selection & Mission" panel.

The entity created via Add New Unit is shown in the IOS map but not shown in the SimHost map (but the entity exists in SimHost's ECS)
Probably because it is missing the components making it a vehicle. And missing a component making it able to execute missions.

Pressing "Commit" does not display any line on the console. Either no command is sent as a response to hitting the commit button
or there are FdpLog prints missing? Update: The request times out...



----


# Task 41: Entity inspector 
In SimHost/IG entity inspector, i need the following
 - By default all component headers collapsed so I see just the headers, forming a plain list of components the entity is having - good for overview.
 - Icon that collapses all components or expands all (toggle)
 - The component header should show the most important stuff from the component via a custom renderer that can be provided
   for any component type. The renderer should be derived from some custom interface and/or marked by a custom attribute
   to be auto-discovered using reflection anywhere in the code. The automatic registration
   mechanism can attach it automatically to a components and use it in the entity inspector. Maybe if the renderer take the component type
   reference as parameter or something (not via specifying component type name as string - this is fragile)

  - another kind of custom renderer for ECS component should allow replacing the default one in the details pane
    - showing the hierarchical dump of properties a tree-like table (see descriptiron for EventBrowser)
       - this hierarchical ImGui reflection-based property renderer should be a shared tool in the Vis2d toolkit, reused at amny places (see below)

For example:
  - special renderer for TargetMemory component able to decode EntityIds, PositionsX etc.


The custom renderer must be registrable per any csharp type, not just the ECS component type, to be used for rendering the values of the properties.
For example:
  - special renderer for vector2, verctors showing inlined numbers like  [x, y, z] for brevity
  - special renderer for Quaternion will show values in euler angles on degress (yaw, pitch, roll)
     - could also show a sub-tree table in the value cell if needed
  - the attribute should allow limit the use of such a renderer just in some condition (for example jut inside some concrete ECS component)
     to avoid beining used globally in wrong context (where the quaternion might mean something different than yaw pitch roll) 

# task 42: Event Browser
In SimHost/IG event browser, i need the following 
 - Be able to disable showing some types of events (especialy the very frequent ones) that are flooding the view.
     - in the first line of the event browser, there should be a button that open a sub-window or pull-down with check-box list of all available events
     - if I uncheck some, it should NOT be shown in the list (a filter) but the events should keep to be received to be shown (the historical ones) when checked again
 - On the right side, when the details are shown, there is a table woith "Property" and "Value". it is not showing anything now for the TimePulseDescriptor event
    - it should be showing a tree-ized reflaction-based dump of the event parameters
        -each line one property,
        - if the property is nested, in left column (the property) the property name indented by the nesting level
        - if a property has children, is should be foldable (showing the expand/collapse triangle)
        - if some properties on the same level are foldable and some are not, the property name start should be aligned (non-foldable must add some indent to match the indentation of the foldable one)
        - if the property is struct/class, the value field should be shown empty
        - ig the property is a collection, the value field should show the number of elements in square brackets, like "[12]"
 - IOn the left after the event name there is now the event field dump shown in gray. This is good. But i need it to be customizable by 
     similar (same?) custom renderer mechanism as requested for the Entity Inspector components (see above). Reflection-discovered custom imgui renderer.
     The default as it is now should stay for those not having custom renderer.

# Task 43: Selected entity in entity inspector
 - if we select an entity on the map, the selection changed event should focus that entity in the Entity inspector.
 - This needs to work independently for simHost and for IG.
    -  If SimHost perspective active (from the main menu), selecting entity on SimHost 2d map should focus the entity just in the SimHost Entity Inspector.
    -  If IG perspective active (from the main menu), selecting entity on IG 2d map should focus the entity just in the IG Entity Inspector.

# Task 44: IOS Data Monitor panel
The panel It shoudl have details sub pane on the right, showing more detail on the record. Similarly to the Event browser
  - a property tree dump shown in a table (one line per property, same as described above for the event browser, reusing same reflection based rendering mechanism)



task 44 seems not to to be implemented in Bagira.Runner - i see no changes in the "Data Panel" visualization - still showing 3 column table (Time, topic, Details) as before, rows are non-clickable, no bigger detail shown. pls re-check. 

Task 45
The Entity class (index and generation) requires its custom renderer so it shows in the tree-dump immediastely as "[<index>, v<generation>] (like "[12, v1]" ) . Now i need to expand the Entity field and look at two lines Index and Generation. The expandability should remain but the value of the Entity struct row whould show the [index, generation] immediately

Task 46
The Entity Inspector, when i click on an entity (and there is no entity on the map shown), for one frame
 i see the content on the detail panel, but it immediately disappaer so the right part shows just empty space.
 This is not happening if there is an entity on the map (at least one) and i click it. Then all good, detail is shown and is stable.

Maybe it is fighting with the map selection but this link should be one-directional: selecting an ewntity on
map focuses the entity in the entity inspector. But cchanging the focus on the entity inspector to another entoty
should not select another entity on the map (or at least not by default - there could by some togglable "chain" icon
 that enables this inspector-to-map propagation of selection).

Task 47
when talking about browser-to-map direction. I would like the entity browser to support customizable
context menu - so the user app could register a handler that adds one or more items to the context menu
(including items with submenus), based on the entity clickes (the handler needs to get the Entity identification)
No handler -> no menu. Multiple handlers -> the menu gets composed from all these together. This should be generic
support in the toolkit, customizable by the hosting app (like the subsystems in Bagira.Runner)

For both the IG and SimHost subsystems in the Bagira.Runner, these menu should contain the "Center on entity" item,
also "Select entity" item.

In case of the IG "Select entity", the menu item handler should properly send the selection changed event to IOS,
reusing exactly the same path as when user clicks there.

task 48
On entity inspector When i click on "Expand all", there might be some recursion or something causing the list growing indefinitely
(the scrollbar handle is shrinking for quite a long time).

The table for individual component seems to show few valid rows - propertise,
but after those there is a lot of "empty rows", making each table very hight but showing empty smace mostly.



task 50
Center on entity (when IG perspective is active) deos not move the camera until i move the mouse cursor from ImGui panel to the map space.


task 51
clicking on "New unit..." activates placement tool which never ends - each click (left or right) creates a new entity. This is a regression.
It stops when i click on "ACTIVATE PLACEMENT TOOL" and make a click to create entity.

---------------------
Task 52
IOS needs a custom DER repositoty inspector, implemented in the DER toolkit as ImGui panel.
It should show the entities currently present in the repo and their descriptors.
It should look and work similarly to the Entity Inspector panel used to inspect the ECS, using same
customizations for title bar, context menu and value renderers - to provide same UX just working in top or DER.
Best if some parts of the Imgui related code could be shared instead of duplicated.
---------------------


--------------------------------
Task 53
The mechanism that creates a ghost entity when a network descriptor arrives and ECS entity is not yet existing should be implemented into all ingress translators as it is a generic rule valid for any descriptor (they can come before the master).

DDS transmits each descriptor on an independent topic over UDP, there is no guaranteed ordering between them. Any descriptor (like position, damage, or mission state) can easily arrive milliseconds before the reliable `EntityMaster` packet. 

Therefore, creating a placeholder ghost entity to stash this early data should absolutely be a generic, universally applied rule across all ingress translators.

However **the current source code does not implement this consistently.** We actually have a significant architectural discrepancy where some translators handle this correctly, and others silently drop the data.

Here is exactly what the code is doing right now:

### The Good: Translators doing it right
Several custom translators correctly inject the `GhostCreationSystem` and act as safety nets. When they receive a packet for an unknown `EntityId`, they immediately spin up a ghost entity to anchor the data:
*   `GeoSpatialIngressTranslator` creates a ghost and applies the position.
*   `EntityDamageIngressTranslator` creates a ghost and applies the damage.
*   `EntityInfoIngressTranslator` creates a ghost and applies the metadata.
*   `MapEntitySymbolIngressTranslator` creates a ghost and applies the styling.

### The Bad: Translators dropping the data
Unfortunately, several other ingress translators fail to implement this rule and just drop out-of-order packets entirely:
*   `EntityMissionIngressTranslator` explicitly bails out with the comment `if (!_entityMap.TryGetEntity(entityId, out var entity)) continue; // Entity not yet known — skip safely`.
*   The generic `AutoCycloneTranslator` (used for standard structs) simply checks `if (_entityMap.TryGetEntity(netId, out Entity entity))` and skips execution if it returns false, making no attempt to create a ghost.
*   The generic `ManagedAutoCycloneTranslator` does exactly the same thing, quietly dropping the payload if the entity isn't in the map yet.
*   The `MultiInstanceCycloneTranslator` also lacks ghost creation, simply returning if the root entity is unknown.

### Why this is a critical bug (The "Take" Trap)
The reason your intuition is so important here comes down to how DDS reads work. In all of these translators, the data is pulled from the network using `_reader.Take()`,,,. 

`Take()` is a destructive read; it removes the sample from the local DDS history cache. If a translator like `EntityMissionIngressTranslator` pulls a mission packet from the queue, realizes the `EntityMaster` hasn't arrived yet, and just executes `continue;`, **that mission data is permanently destroyed**. When the `EntityMaster` finally arrives a millisecond later, the entity will spawn, but it will be missing its mission plan.

### Conclusion
To make the system completely robust, the `GhostCreationSystem` dependency needs to be pushed down into the base classes of the generic auto-translators, and the explicit `continue;` skips in the mission and multi-instance translators need to be replaced with the standard `_ghostCreationSystem.CreateGhost(repo, netId)` fallback mechanism.

**Implementation Gap to Add:**
* **Delete `BinaryGhostStore`:** Because we are now creating ghosts immediately and applying real ECS components to them natively (e.g., `cmd.SetComponent(entity, data)`), **the entire `BinaryGhostStore` stashing mechanic is obsolete**. You can completely delete the `BinaryGhostStore` component and remove the `InternalStashGhostData` byte-copying hacks from `AutoCycloneTranslator.cs`. The ghost will simply accumulate real components in ECS memory until it is promoted.


-----------------
Task 54

The current architecture suffers from a semantic abuse where the `NetworkSpawnRequest` component is forced to act as both a transient state-machine trigger and a permanent data storage container. This specification outlines the complete removal of this component in favor of a pure ECS state-machine approach, decoupling network logic from the entity lifecycle.

### The Architectural Problem (The "Why")

Right now, the `NetworkSpawnRequest` is used in two conflicting ways:
1. **As a trigger (Receiver/IG):** When an `EntityMaster` packet arrives, the `EntityMasterIngressTranslator` creates a Ghost entity and attaches the `NetworkSpawnRequest`. The `GhostPromotionSystem` sees this, promotes the entity, and explicitly deletes the request. 
2. **As persistent state (Authority/SimHost):** When the `NetworkSpawningSystem` spawns an entity locally, it attaches the `NetworkSpawnRequest` but skips the ghost phase, meaning it is never deleted. It remains permanently attached so the `EntityMasterEgressTranslator` can query it every frame to reconstruct the outbound `EntityMaster` DDS topic.

Using a "request" struct as permanent memory is an anti-pattern. Furthermore, we must strictly avoid replacing it with an FDP Event or a transient Tag component to trigger ghost promotion. Because DDS packets arrive out-of-order over UDP, an edge-triggered event would be permanently lost if the `EntityMaster` arrives before the `GeoSpatial` packet. 

**The Solution:** We will introduce a permanent `TkbIdentity` state component and rely purely on the ECS `EntityLifecycle` transitions to act as our level-triggered state machine.


**Implementation Gaps to Add:**
* **Writing the `DisType`:** Your specification notes that `DisType` is stored natively inside the 96-byte `EntityHeader`. However, the ECS does not set this automatically. When you remove `NetworkSpawnRequest`, you must explicitly write the `DisType` to the header.
  * In `NetworkSpawningSystem.ProcessSpawn`: Add `world.SetDisType(entity, new DISEntityType { Value = cmd.DisType });`
  * In `EntityMasterIngressTranslator.ProcessSample`: Add `repo.SetDisType(entity, new DISEntityType { Value = master.DisTypeValue });`
* **Reading the `DisType`:** In `EntityMasterEgressTranslator.ScanAndPublish`, the `ISimulationView` interface does not expose `GetHeader()`. You will need to safely cast the view to access the header: 
  ```csharp
  var repo = (EntityRepository)view;
  ulong disType = repo.GetHeader(entity.Index).DisType.Value;
  ```

---

### Implementation Specification (The "How")

#### 1. Define the `TkbIdentity` Component
Create a permanent, read-only component whose sole responsibility is holding the blueprint type. 
*Note:* We do not need to store `DisType` in this component. The `DisType` is already stored natively inside the 96-byte `EntityHeader` via the `DISEntityType` struct, meaning it is accessible globally without taking up a component slot.
```csharp
[ComponentId(GlobalComponentIds.TkbIdentity)]
public struct TkbIdentity
{
    public long TkbType;
}
```

#### 2. Update `NetworkSpawningSystem` (Local Spawning)
When the authoritative node spawns a new entity via a `SpawnEntityCommand`, it currently attaches the `NetworkSpawnRequest`. 
*   **Change:** Modify `ProcessSpawn` to attach the new `TkbIdentity` component instead.
*   **Change:** Continue to set the lifecycle state to `EntityLifecycle.Constructing`.

#### 3. Update `EntityMasterIngressTranslator` (Remote Ingress)
When a remote node announces a new entity, the ingress translator builds the ghost.
*   **Change:** Inside `ProcessSample`, replace the `cmd.AddComponent(entity, new NetworkSpawnRequest...)` call with the new `TkbIdentity` component.

#### 4. Refactor `GhostPromotionSystem` (The State Machine)
This is where the natural ECS state machine replaces triggers and events.
*   **Change:** Update `EnsureQueriesInitialized` so `_readyGhostQuery` requires `TkbIdentity` instead of `NetworkSpawnRequest`. 
    ```csharp
    _readyGhostQuery = repo.Query()
        .With<TkbIdentity>()
        .WithLifecycle(EntityLifecycle.Ghost)
        .Build();
    ```
*   **Change:** Update `PromoteGhost`. Look up the template using `TkbIdentity.TkbType`. Cross-reference the template's required components against the entity's current ECS `ComponentMask`. 
*   **Change:** If all mandatory components are present, simply call `_world!.SetLifecycleState(entity, EntityLifecycle.Constructing);`. Remove the `_world!.RemoveComponent<NetworkSpawnRequest>(entity);` line entirely. 
*   *Why this works:* Because the entity transitions from `EntityLifecycle.Ghost` to `Constructing`, it naturally falls out of the `_readyGhostQuery` on the next frame. The "trigger" is consumed by the lifecycle transition itself, leaving the `TkbIdentity` safely intact.

#### 5. Update `EntityMasterEgressTranslator` (Outbound Network)
The egress translator currently relies on the permanent `NetworkSpawnRequest` to build its packets.
*   **Change:** Update the query in `ScanAndPublish` to require `TkbIdentity` instead of `NetworkSpawnRequest`.
*   **Change:** Inside the loop, read `TkbType` from `TkbIdentity`. Extract the `DisType` directly from the entity header using `view.GetHeader(entity.Index).DisType.Value`.
*   *Why this works:* The translator will still rely on `SmartEgressUtil.ShouldPublish(view, entity, DescriptorOrdinal, isUnreliable: false)`. This utility ensures that the `EntityMaster` DDS packet is reliably published exactly once upon creation (or when explicitly marked dirty), regardless of the fact that `TkbIdentity` sits on the entity forever.

#### 6. Update the IG User Interface
The UI currently reads the obsolete component to display metadata to the operator.
*   **Change:** In `EntityInspectorPanel.cs` (`Refresh` method), replace the query for `NetworkSpawnRequest` with `TkbIdentity` to extract and display the correct TKB Type ID.

By executing these steps, you will eliminate the `NetworkSpawnRequest` struct, decouple the network layer from the ghost promotion logic, and respect the strict level-triggered ECS state-machine paradigm.
-----------------
Task 55
### Addendum Specification: Migrating to `MandatoryComponents`

This addendum details the architectural shift from network-coupled `MandatoryDescriptors` to ECS-native `MandatoryComponents`. It seamlessly integrates with our previous removal of the `NetworkSpawnRequest` abuse.

### The Architectural Problem (The "Why")

Currently, the definition of what an entity requires before it can be spawned is modeled around network concepts. The `TkbTemplate` uses a list of `MandatoryDescriptor` structs, which identify requirements using a `PackedKey` (a bitwise combination of a DDS Descriptor Ordinal and an Instance ID). 

This causes three critical architectural flaws:
1. **Domain Leakage:** The ECS blueprint layer (`TkbTemplate`) is polluted with DDS network terminology. The ECS should not know what a "Descriptor" or a "DDS Ordinal" is.
2. **Obsolete Mechanics:** In the current pipeline, ingress translators no longer stash binary descriptor blobs. Instead, they decode the DDS packets immediately and write standard ECS components (like `SimTransform` or `IgHealthState`) directly onto the `Ghost` entity.
3. **Dead Code & Danger:** Because the stashing mechanism was bypassed, the `GhostPromotionSystem` currently completely ignores the `MandatoryDescriptors` list. As soon as the master descriptor arrives, it promotes the ghost to `Constructing`. If a UDP packet containing position (`GeoSpatial`) was dropped or delayed, local systems (like Physics) will crash during the `Constructing` handshake because they expect the `SimTransform` to be in memory.

**The Solution:** We must replace `MandatoryDescriptors` with `MandatoryComponents`. Since the ghost entity accumulates real ECS components, the `GhostPromotionSystem` can simply check the entity's highly optimized `ComponentMask` to verify that all required data has physically arrived in memory before allowing promotion.

---

### Implementation Specification (The "How")

#### 1. Define the `MandatoryComponent` Struct
Create a new unmanaged struct to replace the old `MandatoryDescriptor`. It retains the hard/soft requirement logic but targets an ECS Component Type ID instead of a network key.
```csharp
public struct MandatoryComponent
{
    public int ComponentTypeId;
    public bool IsHard;
    public uint SoftTimeoutFrames;
}
```

#### 2. Update the Blueprint (`TkbTemplate`)
Modify the `TkbTemplate` to use the new requirement struct. We will provide a generic helper to make blueprint definitions clean and safe.
*   **Change:** Replace `public List<MandatoryDescriptor> MandatoryDescriptors` with `public List<MandatoryComponent> MandatoryComponents`.
*   **Change:** Add a helper method to easily register requirements using the type system:
    ```csharp
    public void AddMandatoryComponent<T>(bool isHard = true, uint softTimeoutFrames = 0) where T : unmanaged
    {
        MandatoryComponents.Add(new MandatoryComponent 
        { 
            ComponentTypeId = ComponentType<T>.ID, 
            IsHard = isHard, 
            SoftTimeoutFrames = softTimeoutFrames 
        });
    }
    ```

#### 3. Introduce `GhostStateTracker` Component
To support the `SoftTimeoutFrames` feature (where the system gives up waiting for a dropped UDP packet after a certain time), we need to know exactly when the ghost was created.
*   **Change:** Define a new unmanaged component:
    ```csharp
    [ComponentId(GlobalComponentIds.GhostStateTracker)] // Define in GlobalComponentIds
    public struct GhostStateTracker
    {
        public uint FirstSeenFrame;
    }
    ```
*   **Change:** Inside `GhostCreationSystem.CreateGhost()`, attach this new component to the ghost and stamp it with the current simulation tick (`view.Tick`).

#### 4. Refactor `GhostPromotionSystem` (The Core Logic)
Now we rewrite the promotion evaluation to check the live ECS memory layout.
*   **Change:** In `PromoteGhost`, after resolving the `TkbTemplate` using the permanent `TkbIdentity.TkbType` (from our previous specification), fetch the entity's structural header to access the `ComponentMask`:
    ```csharp
    ref var header = ref _world.GetHeader(entity.Index);
    var tracker = _world.GetComponentRO<GhostStateTracker>(entity);
    ```
*   **Change:** Iterate over `template.MandatoryComponents` and check if the bit is set:
    ```csharp
    foreach (var req in template.MandatoryComponents)
    {
        bool hasComponent = header.ComponentMask.IsSet(req.ComponentTypeId);
        
        if (!hasComponent)
        {
            if (req.IsHard) 
                return; // Abort promotion immediately.

            if (tick - tracker.FirstSeenFrame <= req.SoftTimeoutFrames)
                return; // Soft requirement hasn't timed out yet, keep waiting.
        }
    }
    ```
*   **Change:** If the loop completes successfully, apply the template defaults (`preserveExisting: true`), transition the lifecycle to `Constructing`, and finally remove the `GhostStateTracker` component. 

By implementing this, the entire ghost promotion logic operates purely on O(1) bitmask checks natively within the ECS domain, perfectly restoring the safety net for out-of-order DDS packets while severing the final tie between the ECS blueprint layer and the network layer.


**Note**:
 * the TkbIdentity component must be considered a default part on mandatory components even if NOT listed in the tkb template!

**Implementation Gaps to Add:**
* **The Generic Constraint Trap:** You proposed the helper:
  `public void AddMandatoryComponent<T>(...) where T : unmanaged`
  Because `EntityInfo` is mapped to the managed `IgEntityData` class component, restricting `T` to `unmanaged` will prevent you from making managed components mandatory. 
  **Fix:** Drop the `where T : unmanaged` constraint. `ComponentTypeRegistry.GetId(typeof(T))` safely returns the correct Type ID for both unmanaged structs and managed classes.
* **Update `GhostTimeoutSystem`:** Currently, `GhostTimeoutSystem.cs` queries for `BinaryGhostStore` to find and destroy orphaned ghosts that never received an `EntityMaster` packet. Since we are deleting `BinaryGhostStore`, you must update this system's query to use your new tracker:
  ```csharp
  var query = World.Query()
      .With<GhostStateTracker>()
      .WithLifecycle(EntityLifecycle.Ghost)
      .Build();
  ```
* **Promotion Abort Logic:** In `GhostPromotionSystem`, when you evaluate the `template.MandatoryComponents` and execute `return; // Abort promotion immediately`, the system safely leaves the entity in the `EntityLifecycle.Ghost` state. Because the entity remains a ghost, your query will pick it up again on the next frame to re-evaluate it. This is exactly how a level-triggered state machine should work.

-----------------
