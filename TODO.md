
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


I saw default NodeId assignment by subsystem name being inside FDP engine. Concrete Subsystem names are application layer stuff thas must not be inside FDP folder but in the application layer.




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






------------------

I am running "Bagira.Runner -m all"

Orchestrator's ImGui panel should have beige color (now that color is used INSIDE the panel, but it should be used JUST for the TITLE of the panel) Now there is an ImGui window missing and all the stuff are stacked into DrawUI(). Pls add it into a single ImGui window with beige title bar (similarly to what other panels are made).


I saw default NodeId assignment by subsystem name being inside FDP engine. Concrete Subsystem names are application layer stuff thas must not be inside FDP folder but in the application layer.


The 2PC History should show full GUID and should show also the Json payload in the table (on hover over the json payload column the pretty formatted json tooltip window should pop up)

The 2PC History table should NOT grow indefinitely; it should show max 10 lines and support scrolling. Each transaction line should be expandable showing the responses from the nodes.

The "Drill Control" panel should not take shortcut directly to the handler, it should send the SysOpRequest messages!

Some buttons in the Orchestrator ImGui are no-op just with TODO comments, needs to be fully implemented.

Pressing the State machine state button does NOT result in sending NodeOpRequest - pressing those buttons do nothing in the system.

The Orchestrator in the Drill Control should indicate the most recent drill SM state confirmed (if all nodes completed) or it should show "Old State -> New State" transition (if not all nodes completed the transition yet).

The Scenario control (where loading is possible) should support selecting an existing scenario from a combo when loading a scenario - the system should support enumerating available scenario ids.

The same shoudl apply to the Drill ID for replay - the system should support enumerating existing drill ids (available locally - not those archived).


I do not understand why in the "Stories" section there is ScenarioId and not just SotryId. I thought the story is just another json file saved in different folder that the scenario files. 

I would like to have a combo-based selection of existing stories - the Inject will use the one selected from the combo.

There should be another section for archive management - the system should support enumerating the archived drills, archiving current (locally stored) drills and restoring a drill from archive. Same should be working for scenarios and stories - saving current scenarios to archive and restoring current scenarios from archive.


Orchestrator UI should show the time control section. The should be current master time (wall clock time) as well as the drill time. And should have button for making a single step. The Pause should enter (using the future barrier) the deterministic stepping mode where pressing the "Step" button makes a short deterministic step. Pressing "Resume" should resume continuous time flow. There should be a slider for controlling the time flow speed in range 0.1 till 10times faster that real time. "Step" button must be disabled if not paused. Pause button should turn into Resume if time is paused (no need for 2 separate buttons, no need for extra Play button). Just two buttons: Pause/Resume and Step. The stepping should work both in live as well as in replay modes. In replay mode this shoudl be capped to the end time of the recording.

While in replay, there should be a slider showing current relative time position in a recording (drill time). If the slider is moved by the user, after a debounce period the system should execute the Jump-to-time operation and enter paused state once finished by all nodes.


