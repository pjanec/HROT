ReferenceEpisodeLoadHandler uses StartEpisodeOperationId = 20; as number which must match NodeOpType.StartEpisode



[BUG] The IDL codegen has a bug with non-sequential enums — value gap at 3 (where dtGeoSpatialDR was) causes the enum entries after to use `@value()` annotations which confuses the idlc union case generator.
The IDL generator is using the field's position in the struct (0, 1, 2, 3, 4) instead of the actual discriminant values (0, 1, 2, 4, 5). So when it encounters `MapVisualOverlay` at index 3, it's grabbing the wrong discriminant value, and when it gets to `MapRoute` at index 4, it's using the numeric value 5 as a fallback. The gap from removing `dtGeoSpatialDR` is causing the indices and values to misalign.


AttributeRecord in FDP\Toolkits\FDP.Toolkit.Replication\Patching\BinaryInterpreter.cs is from application layer dds struct.
Toolkit must stay generic!



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




[IDEA] - generic Window manager; part of imgui toolkit; to be used by fdp.runner (and cluster runner)

Each subsystem brings /registers own windows (imgui panels)

Each subsystem injects its items into the main menu. It uses "a/b/c/leaf_menu" slash convention and the system builds the final menu from this. Also when defining the menu item it should be possible to define the ordering hints - before and/or after what menu item it should be placed.

Main menu items can be actionable (firing delegate)  or checkable (reading checked state from delegate and firing another delegate on change) or a separator. 

Main menu contains fixed Windows pulldown showing subsystems as submenus and under each the individual windows of the subsystem as check item controlling/monitoring  window visibility. There is also a Help main menu item shown always last (rightmost) containing "About" dialog which shows the version of the software including the git hash (the one available from a .net version info). The Windows main menu is shown before the Help (one before last).
Help menu  contains "Debug" subitem under which where various non-subsystem windows (provided by the Runner itself) are possible to turn on or off (like for example the list of all subsystems currently loaded together with their NodeIds, FPS debug panel etc.) Thse debug windows are NOT affected by any perspective. Perspective affects just subsystem-provided windows.

If a  window is opened (unhidden) from the Windows menu when the window's subsystem's perspective is NOT enabled, the window opens in pinned state. If the window is pinned and closed (hidden via the menu item or the [x] cross icon), the pinned state is turned off. If the window is opened/unhidden when 

The window should likely be an instance of some class; the class might be responsible for rendering the custom title bar with icons and calling some imgui rendering delegate for its "client area". 

Main menu is not affected by currently selected perspective.

There are also  quick  'perspective' switcher buttons (perspective = what subsystem's UI is currently shown) in the main menu acting as radiobuttons, showing one button per subsystem. When selected, the system shows the UI of selected subsystem while suppressing the ui of the others. But still the user can pin a concrete window so it is is always displayed no matter what perpective is currently selected. The ImGui Windows should show custom 2-state "pin" icon HeadlessTestExecutor (pinned = window stays shown no matter what perspective unless closed, unpinned = window shown only if enabled and if corresponding perspective is selected). Next to it (on the very right) there should be custom [x] cross icon which causes the window to disappear (hide). 

The layout of the windows should be auto-saved - maybe to standard imgui.ini But it needs to remember the "hidden" state (if the window is not visible at all - which is different from the minimized state when just the window title is shown)

The window manager should be implemented as part of some FDP toolkit and used by fdp.runner to manage the subsystem's UI.

[IDEA] suport for colored icons
The ImGui framework should also support rendering of colored icons from a texture like the one fromo famfamfam-silk https://github.com/legacy-icons/famfamfam-silk
The function would take the string coordinates like on a checkeboard (like 'b12' means second row and 12-th colum).
The ImGui toolkit should support rendering if colored icons. There should be a function that renders the icon at current position (and moves the X position behind it), or at given coordinate without affecting the position. Best if there is alo a public function for clickable icon button (that responds to mouse hover and to click by visually indicating both hovered/pressed/depressed state). And a toggle icon that takes its toggle state as input parameter.

The framework should support rendering a status bar. The clients (like the subsystem if used by the clusterrunner) can register for rendering a section of the status bar, with possibility to respond to user clicks. clients can render whatever they wish in their reserved section - inclusing the icons etc.


[BUG] The Time Control section of the Orchestrator UI (as well as in the Cluster Control panel of the IOS) is missing the exercise (scenario) time,
it shows just the wall clock time.

It is not clear in the exercise time is running or not.

The Time speed slider stays as value 1.0 and can not be changed (always returns back to 1.0)


If [pause] button is pressed, the console is flooded with messages like the following:
20:21:25.0789 | INFO  | SteppedMasterController | [DEBUG-MASTER] Frame 6633. Sent Order. Waiting for: 1,300,500,400
20:21:25.0789 | INFO  | DistributedTimeCoordinator | [Master] Barrier reached (TotalWallTicks=1515222517). Swapping to SteppedMasterController.
20:21:25.0789 | INFO  | ModuleHostKernel | [TimeController] Swapped to SteppedMasterController, TotalTime=151,522s, Frame=6633
20:21:25.0955 | INFO  | SteppedMasterController | [DEBUG-MASTER] Frame 6634. Sent Order. Waiting for: 1,300,500,400
20:21:25.0955 | INFO  | DistributedTimeCoordinator | [Master] Barrier reached (TotalWallTicks=1515389183). Swapping to SteppedMasterController.
20:21:25.0955 | INFO  | ModuleHostKernel | [TimeController] Swapped to SteppedMasterController, TotalTime=151,539s, Frame=6634
20:21:25.1122 | INFO  | SteppedMasterController | [DEBUG-MASTER] Frame 6635. Sent Order. Waiting for: 1,300,500,400
20:21:25.1122 | INFO  | DistributedTimeCoordinator | [Master] Barrier reached (TotalWallTicks=1515555849). Swapping to SteppedMasterController.
20:21:25.1122 | INFO  | ModuleHostKernel | [TimeController] Swapped to SteppedMasterController, TotalTime=151,556s, Frame=6635
20:21:25.1289 | INFO  | SteppedMasterController | [DEBUG-MASTER] Frame 6636. Sent Order. Waiting for: 1,300,500,400
20:21:25.1289 | INFO  | DistributedTimeCoordinator | [Master] Barrier reached (TotalWallTicks=1515722515). Swapping to SteppedMas



When "Step" button is pressed console is flooded with

20:24:19.1340 | INFO  | DistributedTimeCoordinator | [Master] Barrier reached (TotalWallTicks=3013549857). Swapping to SteppedMasterController.
20:24:19.1340 | INFO  | ModuleHostKernel | [TimeController] Swapped to SteppedMasterController, TotalTime=540,091s, Frame=15623
20:24:19.1504 | INFO  | SteppedMasterController | [DEBUG-MASTER] Frame 15624. Sent Order. Waiting for: 1,300,500,400
20:24:19.1504 | INFO  | ModuleHostKernel | [TimeController] Swapped to MasterTimeController, TotalTime=540,139s, Frame=15624
20:24:19.1504 | INFO  | DistributedTimeCoordinator | [Master] Switched to Continuous Mode.
20:24:19.1504 | INFO  | DistributedTimeCoordinator | [Master] Barrier reached (TotalWallTicks=3013716523). Swapping to SteppedMasterController.
20:24:19.1504 | INFO  | ModuleHostKernel | [TimeController] Swapped to SteppedMasterController, TotalTime=540,139s, Frame=15624
20:24:19.1670 | INFO  | SteppedMasterController | [DEBUG-MASTER] Frame 15625. Sent Order. Waiting for: 1,300,500,400
20:24:19.1670 | INFO  | ModuleHostKernel | [TimeController] Swapped to MasterTimeController, TotalTime=540,187s, Frame=15625
20:24:19.1670 | INFO  | DistributedTimeCoordinator | [Master] Switched to Continuous Mode.
20:24:19.1670 | INFO  | DistributedTimeCoordinator | [Master] Barrier reached (TotalWallTicks=3013883189). Swapping to SteppedMasterController.



[BUG?] When in operatingLive and switched to unloadingLive, the UnloadingLive lasts forever. The Orchestrator should automatically go to Idle
one all nodes are finished with the unloadingLive.
Similar situation if at Idle and switched to loadingLive, orgestrator should automatically issue the transition to OperatingLive
once all nodes are finished loading.

[BUG] before OperatingLive is entered (during loadingLive), the exercise clock should be initialized to scenario-specified time in paused state.
Depending on the jsonPayload of the cluster transition request {"StartPaused": true/false} the clock should be unpaused when OperatingLive
transition is confirmed by all nodes. Another field in json payload {"DeterministicStepping":true/false} should
determine the exercise clock mode.




