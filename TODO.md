# sample.IsValid issue

Some places processing dds samples check sample.IsValid even before testing the instance state.
because disposal sample have sample.IsValid==false, the disposal migh not be detected at all!

# sample.Data does not throw from disposal samples

Some older implementation of cyclone dds had a bug where sample.Data was throwing exception
if sample.IsValid==fasle

# SetComponent vs. AddComponent
At many places there is code like "if Hascomponent then SetComponent else AddComponent".
SetComponent should automatically add it if it does not exist.

# Duplicated component registration
subsystems like SimHost, IG, IOS use different registration paths if they run inside bagira runner or not;
we should unify this as mauch as possible for maintainability.


# SimHost's IdAlloc fails for the first allocation
First entity creation request fails on SimHost failing to allocate an id.

When i click "Spawn" for the first time, the entity is not created and the console says:

07:27:44.5861 | DEBUG | BdcCommandGateway | [TRACE-GW] Sending CreateEntityRequest ID=89939fdb-d67c-49f9-ab52-c6852d3fc1e6
07:27:48.0195 | ERROR | CreateEntityRequestSystem | [SimHost] CreateEntity failed for request 89939fdb-d67c-49f9-ab52-c6852d3fc1e6: ID pool exhausted and no response from server.
07:27:48.0195 | DEBUG | BdcCommandGateway | [TRACE-GW] CreateEntityAck ID=89939fdb-d67c-49f9-ab52-c6852d3fc1e6 Entity=0 Error=500

Next Spawn is ok. For the first time, in DdsIdAllocator ProcessResponses() the condition "if (response.ClientId != _clientId && !string.IsNullOrEmpty(response.ClientId)) " is fullfilled because response.ClientId=="IG_300" and _clientId=="SimHostAllocator". On second try this is response.ClientId=="SimHostAllocator".

# Drop event does not move the entity immediately
On entity drag and drop, after the drop the entity on the IG does not jump immediately, but but only after some time,
when the rolling window-triggered geoSpatial update arrives.





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
UI. Pls explain what is needed to show the entity in "Selection & Mission" panel.

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

When i select "Hostile" on Entity Spawner UI panel and activate the placement tool, the entity is not created as hostile.
I need the creation request to carry the entity info descriptor (maybe alread done).
SimHost should convert it to related ECS managed component IgEntityData.
On change of that ECS descriptor there should be entity info egress translator publishing the entity info descriptor to the IG.

----
