[BUG] Edit drawing can not be ended
When i create a map drawing entity shape and i select "Edit drawing" from the context menu,
I am able to drag and drop the vertices of the shape using left mouse button but I am not able to finish it.
when i click the right mouse button, it always moves the closest vertex to the right clicked location instead
of committing the edit.


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
