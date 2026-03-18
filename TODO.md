
BUGS
[BUG] No RVO avoidance
 after implementing "docs\modularizing\MOD1-DESIGN.md"
- Vehicle entities no longer avoid ieach other. For example the "Collision test".
  maybe the new navigationIntent method has broke it.
- Shift+Right click in SimHost perspective no longer adds to planned vehicle trajectory.
  It looks like the point is added and drawn on the map for the first frame, but then disappear
  and the vehicle seems to act as id just right click was used (no shift) - new mission with moveToLocation
  gets generated..

[BUG] DisType in entity master DDS topic struct is represented a plain long. It should be a @final structure with fields
to be easily readable in DDS monitoring tool. Inside the engine in the entity header the DIS type should be stores as
FDP-specific fixed-layout DDS struct (for easy display during debug). But for quick filtering it might support
fast comparison in the entity queries using the struct cast to long or something performance effective.


[BUG] allocation on hot path

Bagira.SimHost\Systems\CreateEntityRequestSystem.cs creates lambda every tick

        public void Execute(ISimulationView view, float deltaTime)
        {
            // ── Phase 1: Drain all incoming requests this frame ───────────────
            // The callback fires synchronously for each valid DDS sample so no
            // List<CreateEntityRequest> is ever allocated on ingress (GC03).
            // Each valid request is validated, ID-allocated, ACK'd, and enqueued
            // on the same frame it arrives, giving the requester minimum latency (GC04).
            _requestSource.ProcessRequests(request => // <====== ALLOCATION here
            {

Such "hidden" allocations need to be eliminated on the hot path!


[BUG]  loan object is created every tick on the hot path!

    internal sealed class DdsCreateEntityRequestSource : ICreateEntityRequestSource
    {
        private readonly DdsReader<CreateEntityRequest> _reader;

        public DdsCreateEntityRequestSource(DdsParticipant participant)
            => _reader = new DdsReader<CreateEntityRequest>(participant);

        public void ProcessRequests(Action<CreateEntityRequest> processor)
        {
            using var loan = _reader.Take(); // ALLOCATION
            foreach (var sample in loan)
                if (sample.IsValid)
                    processor(sample.Data);

            // AUTO-DISPOSE
        }
    }

[BUG] Entities created from "SimHost Controls" panel are not moving. 
It happened after splitting the direct Navigation component into NagivationIntent in the 'brain'
and the kinematics in the 'muscle'
to be able to run the brain on different machine than the SimHost 'muscle".
To solve similar issue with IOS entity spawning, I had to send a new mission plan to the entities,
containing a single MoveToLocation task.  It looks like the entity behavior infrastructure
might be constantly overwriting the navigation intent although there is no real behavior driving it.



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
