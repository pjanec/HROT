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


