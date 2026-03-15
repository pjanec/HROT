BUGS after implementing "docs\modularizing\MOD1-DESIGN.md"
- Vehicle entities no longer avoid ieach other. For example the "Collision test".
  maybe the new navigationIntent method has broke it.
- Shift+Right click in SimHost perspective no longer adds to planned vehicle trajectory.
  It looks like the point is added and drawn on the map for the first frame, but then disappear
  and the vehicle seems to act as id just right click was used (no shift) - new mission with moveToLocation
  gets generated..


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


