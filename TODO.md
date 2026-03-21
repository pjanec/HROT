
[BUG] Vehicle given MoveToLocation mission task does not start moving. Probably missing DoctrineFinished trigger case?
[BUG] still getting 2 identical acks for update entity descriptor request dtGeoSpatial (running each IOS, IG, SimHost standalone)
[BUG] map configuration IOS panel still contains tool selection combo although the tools are no more started via map configuration json
[BUG] on IG no idea how to enable immediate drag mode - no UI for that. I would like to activate the immediate mode when SHIFT is pressed during dragging;
[BUG] Selection & Mission editor does not show any trigger selection UI - there should be a combo for trigger and textbox for trigger parameters. With button for insering some valid default parameter json so that the user can easily change it. 
[BUG] Buttons for Mission Task Up/Down/Delete operations should contain normal text, now unreadable symbol only.
[BUG] When map layer with ground vehicles is turned off, now invisible entities still can be selected . Also currently selected but now invisible entity remains selected, still showing selection indicator on the 2d.
[BUG] I still can not see any road graph rendered on simhost, even if standalone ig app started from proper Bagira.runner project folder - maybe the roadmap file not found or failed to load or something
[BUG] This might also prevent the road picking when mission task FollowRoute is selected in IOS mission task editor, preventing to use this kind of task. The road picker should show specific cursor/indicator.
[BUG] Measure tool should show specific cursor/inidcator when waiting for first click - now not clear that the tool is active.
[BUG] ENtity inspector UI should support in its context menu a field for deleting the entity (using proper networked way using ELM, no shortcuts)
[BUG] GeoSpatialDR descriptor not disposed when entity deleted. Maybe this issue is there for other entity descriptors as well?
[BUG] ORBAT Tree in IOS does not indent the subordinates, they appear at the same level as their commander - not good UX
[BUG] IOS Mission editor does not seem to handle the version conflict - no warning that information is obsolete, no possibility to forget user changes and update to latest
[BUG] There are similar ECS components Health and HealthData - are they needed both?
[BUG] The DELETE context menu item sent from IOS does nothing when clicked on IOS - entity not deletes. Should use proper netwroked ELM mechanism.






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
