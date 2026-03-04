# sample.IsValid issue

Some places processing dds samples check sample.IsValid even before testing the instance state.
because disposal sample have sample.IsValid==false, the disposal migh not be detected at all!

# sample.Data does not throw from disposal samples

Some older implementation of cyclone dds had a bug where sample.Data was throwing exception
if sample.IsValid==fasle


----
Drop does not move the entity on the map.

Example for entity #2

08:56:41.1731 | DEBUG | GeoSpatialIngressTranslator | [TRACE-IG] Ingress: GeoSpatial Entity=2 Lat=52,5203594355974 Lon=13,402466073833486
08:56:46.9194 | INFO  | BdcCommandGateway | [GW] Sent UpdateEntityDescriptorRequest for Entity 2 (dtGeoSpatial)
08:56:46.9194 | INFO  | IgApplication | [IG] Drag end: sent UpdateEntityDescriptorRequest for NetID 2 to (52,52170°, 13,39813°).
08:56:46.9355 | INFO  | UpdateEntityDescriptorRequestSystem | [UpdDescReq] Applied GeoSpatial move for NetID 2 ␦ (-466,0, 189,0, 0,0) Cartesian.
08:56:51.6044 | DEBUG | GeoSpatialIngressTranslator | [TRACE-IG] Ingress: GeoSpatial Entity=2 Lat=52,521698261901456 Lon=13,398134618801565

Simhost moves the entity (ig0ingress sees new position) but the map does not show it, entity remains on original place.


----
"Selection & Mission" panel is empty no matter what entity i select on the map. It is not empty if i create the entity via the "ORBAT tree"
UI. Pls explain.

Pressing "Commit" does not display any line on the console. Either no command is sent as a response to hitting the commit button
or there are FdpLog prints missing?

----
I need the UI panels from IG to have green title bar and those from simhost to have red title bar. IOS ones should stay at violet color.

----
"Spawn moving vehicle" spawns an entity but it does not move at all.

The ingress console lines show all the time the same coordinate so simhost probably does not send anything

09:02:31.7053 | DEBUG | GeoSpatialIngressTranslator | [TRACE-IG] Ingress: GeoSpatial Entity=3 Lat=52,52 Lon=13,405000000000003
----

If running combined mode of bagira runner with all three components (-x all) the simhost is now run in headless mode to avoid
colliding with input handling of IOS and/or IG. But that effectively hides all diagnostics UI of simhost. In this case, simhost
should not run fully headless. Just its map toolkit should be disabled so click on the map or entities are not enabled. But its
UI panels must be shown. Alternatively (preferrably) the bagira runner combined UI might show a visual switch (in the main menu bar)
specifying whose subsystem's map toolkit should be currently enables (IG or SimHost) - colored dual state buttons, same color as
subsystems color; currently active subsystem should show its button in brigh color while the inactive one should show dark shade.

-----

SimHost and IG subsystems should be showing their respective entity inspector and event inspector - nothing custom and simplified,
but the standard and fully fledged one from the FDP toolkits. At the moment i have no clue what the states of SimHost and IG ECS is.
If is fine if they are shown at the same time in combined bode of bagira.runner as the title bars are colored so i fill know what
panel belongs to what subsystem. The generic ones then needs to support coloring their title bar in the
same way as the custom panels of SimHost and IG in the bagira runer are doing - perhaps via some ctor parameter.

----

Upon clicking "New unit" the placement tool activates. I click on map, entity gets created and placement tool indicator disappers.
I click again and new enity gets created. i right click again and new entity gets created. Only the clicks when the placement tool
is active should create new entities. When i then activate placement tool usiong "ACTIVATE PLACEMENT TOOL" and click to create a new
entity, the click behavior resets to normal (no more underied creations on click)

-----



