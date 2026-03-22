[BUG] IOS "DRAW ROUTE" activate point tool but when confirmed no entity gets created. No route seen on IG.
"Runnign Bagira.Runner -m all"

[BUG] Do we have map layer for routes? I do not see it on IOS layer checkboxes.

[BUG] tactical shape authoring runs a point tool but when comitted, the shape saved and shown is different from the one
clicked by the tool! Maybe some centroin entity timing issue?

[BUG] When i author a tactical drawing, it shows on the IG and in IG entity inspector and in IOS entity inspector.
When I delete the entity using the context menu in the IG entity inspector, the entity stays shown
in the IOS entity inspector (like if the entity deletion info did not reach the IOS)


[BUG] When i delete tank platoon unit entity, the subordinate (physical) units are not deleted. Shouldn't they?


[BUG] When i delete the entity which is shown in the orbat tree on IOS, the entity stays shown. The IOS entity inspector
is still showing this entity as existing. Maybe the DER library is not catching the disposal of EntityMaster
and not deleting the entity from its repository?





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

[BUG] Entity health calculated on all systems from hit events
Entity damage might be complex and should be calculated on a single node only (not in parallel on each node indepedently)
The resulting health update should be published over network.
TODO: THIS NEEDS MORE DETAILS - DO NOT IMPLEMENT UNTIL CLARIFIED







----------
# Scene tree in ECS 
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
# sample.Data does not throw from disposal samples
Some older implementation of cyclone dds had a bug where sample.Data was throwing exception
if sample.IsValid==fasle

---------

We have two identical components
 - EntityMissionholder component
 - IgMissionHolder component
why? can't we unify them?

If entity is tasked via Mission panel to make a Move To task, the Ig shows the destination point but the entity does not stat moving
