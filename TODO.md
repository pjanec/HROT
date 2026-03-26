
[BUG] When i delete the entity using its context menu on IG, it gets deleted just from IG but not from SimHost - DeleteEntityRequest is necessary!

[BUG] Entity inspector when deleting entititie must use the entity deletion request message so it always reached the entity
owner and the entity is deleted properly (the owner performs the ELM-based entity deletion procedure)

[BUG] on IG, 'Edit personal route' entity context menu does nothing


[BUG] in IOS ORBAT panel, the JUMP TO seems to do nothing.

[BUG] in IOS ORBAT panel, vehicle entity context menu 'Edit route' starts authoring a route entity (OK)
When committed, the route entity is created as a subordinate of the vehicle (EntiyInfo.CommanderId=vehicle id)

[BUG] The ECS component EntityInfo contains CommanderId = network entity id (int).
Should be CommanderId = local entity id (Entity struct)

[BUG] When i delete tank platoon unit entity, the subordinate (physical) units are not deleted. Shouldn't they?

[BUG] ContextMenuRequest not seen to be sent if clicked entity not yet configured with context menu from IOS.

[BUG] MapClickEvent does not recognize lef/right/middlele click!





[BUG] MissionTrigger.ReachedDestination  trigger (brain) checks NavState which lives in another node (muscle)
and will never be set on the brain node

In the current architecture, `MissionTrigger.ReachedDestination` directly polls the `NavState.HasArrived` field in the ECS. 

If your navigation (Muscle tier) and brain (Cognitive tier) run on the same node (e.g., `NodeRole.AllInOne`), `NavState` is updated natively by the `CarKinematicsSystem` during the physics update. 

However, if the navigation runs on a different node, **this trigger will fail and hang the mission indefinitely.**

Here is why this happens, and why it is an architectural flaw:
1. **Lack of Replication:** `NavState` is an internal kinematic component. It is never replicated over the DDS network.
2. **CQRS Violation:** The engine uses a strict CQRS contract for distributed movement. The Brain node publishes a `NavigationIntent` command, and the Muscle node (running `NavigationExecutionSystem`) executes the physics and replies with a `NavigationStatus`. 
3. Because `NavState` is never updated on the Brain node by the network ingress layer, `MissionDirectorSystem` will never see `HasArrived == 1`. It is a leaky abstraction that couples the cognitive tier directly to a local kinematic component.

**The Correct Architectural Approach**
To fix this in a distributed topology, you must stop using `MissionTrigger.ReachedDestination` and instead use **`MissionTrigger.DoctrineFinished`**.

This fully respects the network boundary and leverages the CQRS pipeline correctly:
1. The remote Muscle node determines that the vehicle is within the arrival radius and broadcasts `NavigationStatus.Result = Arrived` over DDS.
2. The Brain node receives this via the `NavigationStatusIngressTranslator`.
3. The `MoveToExecutor` (running on the Brain) reads the replicated `NavigationStatus` and returns `NodeStatus.Success`.
4. The `BTreeTickSystem` observes that the doctrine's root node has reached a terminal state and publishes a `DoctrineFinishedEvent`.
5. `MissionDirectorSystem` consumes the event and cleanly advances the mission phase without ever needing to touch `NavState`.

--------

[BUG] Entity health now calculated on all systems from hit events.
Entity damage might be complex and should be calculated dedicated node(s), not in parallel on each node indepedently.
Different entity types might need different damage calculators located at different nodes. In our current situation, the SimHost
should be calulating the hit damage for all entitities. But the infrastructure should allow distributing this per entity type
to multiple nodes.
The resulting health update should be published over network.








----------
# Scene tree graph in ECS?
Invent ECS components for scene graph implementation in ECS
 - parent component (contains parent entity id)
 - child component (contains entity id of first child)
 - sibling component (containd entity id of prev and next sibling)
Queue for structural change commands
 - reparent command
Optimized recalculation of transforms every frame if something changes
(in case we need to calculate the transforms of child entities - like aircraft on board of a carrier)
etc.
----------
# sample.IsValid issue
Some places processing dds samples check sample.IsValid even before testing the instance state.
because disposal sample have sample.IsValid==false, the disposal migh not be detected at all!
-----------

We have two identical components
 - EntityMissionholder component
 - IgMissionHolder component
why? can't we unify them?

