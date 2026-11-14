Could the UI editor for the blueprint be made as Entity inspector component editor? Maybe using StructEditor or custom ui components, but allowing for both monitor+author modes? Or rather leave the Entity inspector as pure monitoring tool with just default (structEdit based) editing capabilities and dedicate a separate UI panel for blueprint-on-entity manipulation?
---
You should absolutely leave the Entity Inspector as a pure monitoring tool for these components and build a dedicated "Entity Blueprints" panel for authoring. Attempting to wedge the blueprint assignment workflow into the standard Entity Inspector via `StructEdit` or custom component drawers will severely fight the engine's architecture.

Here is the architectural breakdown of why blueprint-on-entity manipulation requires a dedicated panel, and how the Entity Inspector should be configured to safely monitor the live state.

### Why the Entity Inspector is the Wrong Place for Authoring

**1. The Multi-Tier Abstraction Leak**
Instance Blueprints do not live in a single ECS component. Depending on the aggregate state size, they reside in `BlueprintBlackboard1024`, `4096`, or `16384`. The Entity Inspector maps UI headers 1:1 with ECS component types. 
If you build a component editor for these, the designer would see their blueprints arbitrarily vanish from the "BlueprintBlackboard1024" header and reappear under "BlueprintBlackboard4096" when a new script causes a tier upgrade. A dedicated panel abstracts the ECS storage entirely, presenting a single, unified "Entity Blueprints" list to the designer regardless of which tier component is actively holding the bytes.

**2. Incompatibility with `StructEdit`**
The `StructEdit` framework is designed to map UI fields directly to CLR properties, blittable structs, and fixed buffers, committing changes via an `IEditSession`. 
The `BlueprintBlackboard*` components are unmanaged memory blobs containing a custom partition allocator, a dense slot table, and internal execution metadata (like `InstanceVersion` and latent cursors). `StructEdit` cannot natively or safely repack this dense slot table, coalesce freed memory, or orchestrate the `CopyToLargerTier` logic required when an addition exceeds the current tier's capacity.

**3. The View-Model "Staged Commit" Mismatch**
As we established in the scenario design, authoring requires a detached view-model to separate the live "Reality" (the unmanaged slot table) from the designer's "Intent" (the uncommitted additions/removals). The UI must render diffs (e.g., green for added, strikethrough for removed) and defer the actual structural memory mutation until an explicit "Apply" button is clicked. 
The Entity Inspector's `ComponentEditDrawer` is fundamentally built around immediate, in-place `IsDirty` commits. Forcing a staged, transactional diffing pattern into a standard component drawer violates its design intent.

### The Recommended Implementation Strategy

To implement this cleanly, you should separate the two concerns across the two UI surfaces:

#### 1. The Entity Inspector (Pure Monitoring)
You should implement a custom `IEntityAwareImGuiRenderer` for the three `BlueprintBlackboard*` components, explicitly designed as a read-only monitoring view. This perfectly mirrors how the engine currently handles `BrainBlackboard` and `Blackboard1024` via `BrainBlackboardRenderer` and `Blackboard1024Renderer`.

When the Entity Inspector encounters a `BlueprintBlackboard1024` component on an entity, your custom renderer should:
1.  Read the unmanaged memory and invoke `BlueprintBlackboardPartitions.GetSlotCount`.
2.  Iterate the dense slot table.
3.  Query the `BlueprintRegistry` using the slot's `BlueprintId` to resolve the human-readable blueprint name.
4.  Render a read-only, real-time list of the executing scripts, displaying their `InstanceVersion`, `TickCount`, and latent cursor status. 
5.  Return `true` from `RenderValue` to completely replace the default byte-dump tree rendering.

By doing this, the Entity Inspector provides perfect, real-time visibility into the exact execution state of the partition allocator without ever tempting the user or the `StructEdit` system to corrupt the unmanaged bytes.

#### 2. The Dedicated "Entity Blueprints" Panel (Authoring)
You should create a separate panel (e.g., `EntityBlueprintsPanel`) that registers alongside the existing tooling. 
When an entity is selected in the map, this panel:
1.  **Reads Reality:** Scans across all three `BlueprintBlackboard*` tiers to build the initial list of attached blueprints.
2.  **Stages Intent:** Allows the designer to use the `BlueprintPickerSource` to select new blueprints to add, or mark existing ones for removal, accumulating these in a local `List<BlueprintAssignmentDto>`.
3.  **Commits Safely:** Provides the "Apply" button, which executes the synchronous tier-provisioning logic (if the simulation is paused) or drops the `AttachInstanceBlueprintEvent` / `RemoveInstanceBlueprintEvent` unmanaged structs onto the `FdpEventBus` (if the simulation is running).

This strict separation guarantees that live simulation memory is protected, the complex tier-upgrade logic is encapsulated in the commit phase, and the designer receives a coherent, unified authoring UX that isn't fragmented by ECS component boundaries.
---
what the UI of such a "Entity Blueprints" UI panel could look like - pls wireframe and what functional description
---
Here is the wireframe and functional description for the dedicated "Entity Blueprints" authoring panel. This design adheres to the detached view-model pattern, separating the unmanaged "Reality" (the live blackboard memory) from the designer's "Intent" (staged additions and removals), while providing clear visual diffs and safe commit pathways.

### Wireframe: Entity Blueprints Panel

```text
┌─────────────────────────────────────────────────────────────┐
│ Entity Blueprints                                       [X] │
├─────────────────────────────────────────────────────────────┤
│ Target: [42, v1] (OrcGuard)           Sim: [ RUNNING ]      │
│ Active Tier: BlueprintBlackboard1024                        │
│ Projected Usage: 3 / 4 Slots  |  650 / 928 Bytes            │
├─────────────────────────────────────────────────────────────┤
│  [ + Add Blueprint... ▾ ]                                   │
├─────────────────────────────────────────────────────────────┤
│ Assignments (Intent vs Reality):                            │
│                                                             │
│  Blueprint             Status      Size   Action            │
│ ----------------------------------------------------------- │
│  HealthRegen           Active      150 B  [ Remove ]        │
│  ~PatrolBehavior~      Removed     200 B  [ Restore ]       │
│  + SquadCombat         Added       300 B  [ Cancel ]        │
│                                                             │
├─────────────────────────────────────────────────────────────┤
│ ⚠ Pending Changes: 1 to add, 1 to remove.                   │
│                                                             │
│                 [   Apply   ]   [ Revert All ]              │
└─────────────────────────────────────────────────────────────┘
```

### Functional Description

#### 1. Header & Capacity Projection
*   **Target & Sim State:** Displays the currently selected entity and whether the engine's time controller is paused or running. This context is vital because it dictates the underlying commit pathway when the user clicks "Apply".
*   **Active Tier:** Displays the current `BlueprintBlackboard*` component hosting the scripts (e.g., 1024, 4096, or 16384). 
*   **Projected Usage:** Calculates the aggregate slot count and state payload byte size of the *intended* state (Reality + Adds - Removes) against the strict tier limits. For example, the 1024 tier holds up to 4 slots and 928 bytes of payload. If the staged additions push the projected usage over the current tier's capacity, this indicator highlights in yellow to warn the designer that a tier upgrade will occur on Apply. If the projection exceeds the absolute maximum 16384 tier limit (16 slots, 16096 bytes), the indicator turns red, and the "Apply" button is disabled to prevent an unhandled absolute capacity overflow.

#### 2. The View-Model List (Intent vs. Reality)
This list is the core of the staging UX. It reads the live ECS unmanaged memory every frame to ground the "Reality", but overlays the local mutable `List<BlueprintAssignmentDto>` "Intent".
*   **Active (Standard Text):** Blueprints currently executing in the live slot table that the user has not modified.
*   **Removed (Strikethrough / Red Text):** Blueprints currently executing in the live slot table that the user has marked for detachment. The "Remove" button toggles to "Restore", allowing the user to undo the staged removal.
*   **Added (Green Text / '+' Prefix):** Blueprints selected from the picker that are staged to be attached but have not yet been committed to memory. The "Cancel" button discards the staged addition.

#### 3. Blueprint Picker ("+ Add Blueprint...")
Clicking this opens the shared `BlueprintPickerSource` filtered to blueprints with the `Instance` dispatch kind. When a designer picks an asset, it is not immediately attached to the entity. Instead, it is appended to the local "Intent" list, marked as "Added", and the Projected Usage footprint recalculates immediately.

#### 4. The Commit Pathway (Apply & Revert)
The "Revert All" button simply clears the local Intent list, instantly snapping the UI back to mirror the live ECS slot table reality.

The "Apply" button resolves the diff between the Intent and Reality and executes the structural mutations. Because direct memory manipulation from the UI loop during a live simulation would cause race conditions with the `BlueprintTickSystem`, the commit pathway branches based on the engine's time controller state:

*   **When Paused (Authoring Mode):** The editor synchronously evaluates the aggregate capacity. If a tier upgrade is required, it uses `repo.AddComponent` to attach the new, larger blackboard tier component and calls `CopyToLargerTier` to migrate existing slots thread-safely. It then invokes `BlueprintBlackboardPartitions.TryDetach` for removals and `BlueprintAttachService.AttachToEntity` for additions, instantly re-packing the dense slot table.
*   **When Running (Live Mode):** The editor drops down to the event bus boundary. It constructs unmanaged `RemoveInstanceBlueprintEvent` and `AttachInstanceBlueprintEvent` structs for each diffed assignment and publishes them to `world.Bus`. These events sit in the double-buffer until the frame ends, and the dedicated assignment system consumes them during the next frame's `Input` phase, ensuring the changes are structurally finalized before the `Simulation` phase begins.

### Relationship to the Entity Inspector
It is important to note that this dedicated panel fully abstracts the multi-tier ECS component architecture from the designer. The standard "Entity Inspector" will continue to serve as a pure, read-only monitoring tool—using a custom `IEntityAwareImGuiRenderer` to show the live execution state, `InstanceVersion`, and latent cursors of the `BlueprintBlackboard*` memory. This dedicated "Entity Blueprints" panel serves as the transactional, safe authoring surface.
---
