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
BUG: IOS entity inspector (DER-based) does NOT show changes in incoming geoSpatial descriptors
---------
BUG: Center on entity (when IG perspective is active) deos not move the camera until i move the mouse cursor from ImGui panel to the map space.
---------
clicking on "New unit..." activates placement tool which never ends - each click (left or right) creates a new entity. This is a regression.
It stops when i click on "ACTIVATE PLACEMENT TOOL" and make a click to create entity.
---------


