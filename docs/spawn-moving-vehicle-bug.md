The issue is that when running in Bagira.Runner with all 3 subsystems (SimHost, IG, IOS) using "-x all" CLI args, clicking the "Spawn moving vehicle" sometimes creates moving entity and many more times it creates just a static entity (not moving). 


# Why the "Spawn moving vehicle" should start the movement correctly.

Short answer: because the full spawn+mission round‑trip now reliably delivers the CreateEntityAck and the WanderMilitary mission to SimHost, SimHost activates the doctrine and its kinematics update the entity’s SimTransform each tick, and the egress path now marks geo state dirty so position updates are published immediately to IG. The new integration test verifies the end‑to‑end movement.

Flow (concise causal chain):

- IG: MiniIosPanelState.SubmitWithWanderMissionViaGateway(...) does:

    - send CreateEntityRequest via the BdcCommandGateway
    - await the CreateEntityAck to get the network ID
    - send MissionControlRequest with CMD_REPLACE_MISSION containing a single-task MissionPlan whose task BehaviorId = "WanderMilitary"
- SimHost:

    - CreateEntityRequestSystem receives the request, spawns the authoritative entity, assigns a NetworkIdentity, and publishes CreateEntityAck.
    - MissionControlRequestSystem receives the mission request and enqueues the mission into the entity’s MissionPlanQueue (resolving `"WanderMilitary"` to the correct doctrine ID).
    - `MissionDirectorSystem`/doctrine activation sees MissionPlanQueue.PhaseCount > 0 and activates the doctrine instance (the WanderMilitary behavior tree).
    - The WanderMilitary behavior drives movement via `CarKinematicsSystem`/`LinearKinematicsSystem`, which update the entity’s SimTransform every simulation tick.
- Egress → IG:

    - `SimTransformEgressDirtySystem` (fix added earlier) calls `SmartEgressUtil.MarkDirty` for moving, locally‑owned entities so the geo descriptor is considered changed each tick.
    - `GeoSpatialEgressTranslator` then publishes GeoSpatial updates immediately (not waiting for the 600‑tick heartbeat).
    - IG receives the GeoSpatial updates and updates the ghost entity SimTransform so the vehicle visibly moves on the IG map.
- Mission ACK / reliability piece:

    - The gateway and IOS now get mission ACKs because MissionControlAckIngressHandler feeds a missionAckQueue and MissionEditorService (constructed with that ackQueue and added to ingressHandlers) polls it to complete pending commits. This ensures the mission is actually applied on SimHost before we expect movement.

Why this fixes the previous failure:

- Previously either the mission never reached/was not applied (so no doctrine active) or geo updates were withheld by the rolling heartbeat (so IG saw a frozen position). Now both are addressed: mission commit is delivered/applied, and geo state is marked dirty so updates are published immediately.

Evidence:

- The new integration test IG_SpawnMovingVehicleViaGateway_EntityMovesOnIg exercises TestHook_SubmitMiniIosSpawnWithWanderMission and asserts that IG SimTransform changes within the movement timeout — and it passed.




# Running from Bagira.Runner oin 

## success case
when it works, the console output looks like follows:

14:49:14.5497 | DEBUG | BdcCommandGateway | [TRACE-GW] Sending CreateEntityRequest ID=3a22e954-81ee-49d4-a34c-1090948fdc55
14:49:14.5703 | INFO  | CreateEntityRequestSystem | [SimHost] Spawned entity 1 (TkbType=101) for request 3a22e954-81ee-49d4-a34c-1090948fdc55.
14:49:14.5795 | DEBUG | BdcCommandGateway | [TRACE-GW] CreateEntityAck ID=3a22e954-81ee-49d4-a34c-1090948fdc55 Entity=1 Error=0
14:49:14.5795 | DEBUG | BdcCommandGateway | [TRACE-GW] Sending MissionControlRequest ID=81abb74e-a43b-4f79-a05d-e200991d22fa Entity=1
14:49:14.5795 | DEBUG | NetworkSpawningSystem | [TRACE-SH] ProcessSpawn: NetworkId=1 TkbType=101
14:49:14.6191 | DEBUG | GeoSpatialEgressTranslator | [TRACE-SH] Egress: Writing GeoSpatial for NetID=1 pos=(52,51569224574931,13,406180705870085)
14:49:14.6247 | DEBUG | EntityMasterEgressTranslator | [TRACE-SH] Egress: Writing EntityMaster for NetID=1
14:49:14.6247 | DEBUG | EntityMasterIngressTranslator | [TRACE-IG] Ingress: EntityMaster NetID=1 -> Ghost spawn
14:49:14.6247 | DEBUG | GeoSpatialIngressTranslator | [TRACE-IG] Ingress: GeoSpatial Entity=1 Lat=52,51569224574931 Lon=13,406180705870085
14:49:14.6247 | DEBUG | StyleResolutionSystem | [TRACE-IG] Style: Resolved Entity=1 Texture=
14:49:14.6247 | DEBUG | 0, Culture=neutral, PublicKeyToken=null]] | [TRACE-IOS] DER: Received EntityMaster for NetID 1. Storing in Repo.
14:49:14.6602 | DEBUG | NetworkGatewaySystem | Entity 0: Reliable mode. Peers:
14:49:14.6602 | DEBUG | NetworkGatewaySystem | Entity 0: No peers. ACKing.
14:49:14.6602 | DEBUG | BdcCommandGateway | [TRACE-GW] MissionControlAck ID=81abb74e-a43b-4f79-a05d-e200991d22fa Error=0
14:49:14.6816 | DEBUG | EntityLifecycleModule | [TRACE-SH] ELM: Entity 0 received all ACKs. Promoting to Active.
14:49:14.6816 | DEBUG | EntityLifecycleModule | [TRACE-SH] ELM: Entity 0 promoted to Active
14:49:14.6837 | DEBUG | NetworkGatewaySystem | Entity 1 missing PendingNetworkAck. ACKing.
14:49:14.7054 | DEBUG | EntityLifecycleModule | [TRACE-SH] ELM: Entity 1 received all ACKs. Promoting to Active.
14:49:14.7054 | DEBUG | EntityLifecycleModule | [TRACE-SH] ELM: Entity 1 promoted to Active
14:49:14.7817 | DEBUG | GeoSpatialIngressTranslator | [TRACE-IG] Ingress: GeoSpatial Entity=1 Lat=52,51569224571263 Lon=13,40618089232308
14:49:14.8147 | DEBUG | GeoSpatialIngressTranslator | [TRACE-IG] Ingress: GeoSpatial Entity=1 Lat=52,5156922455784 Lon=13,406181050537873
14:49:14.8481 | DEBUG | GeoSpatialIngressTranslator | [TRACE-IG] Ingress: GeoSpatial Entity=1 Lat=52,51569224601349 Lon=13,406181259554469
14:49:14.8813 | DEBUG | GeoSpatialIngressTranslator | [TRACE-IG] Ingress: GeoSpatial Entity=1 Lat=52,51569224665466 Lon=13,406181517196591
14:49:14.9144 | DEBUG | GeoSpatialIngressTranslator | [TRACE-IG] Ingress: GeoSpatial Entity=1 Lat=52,51569224740663 Lon=13,40618182379731

## failure case
when it does not work (entity stays static), the console output looks like follows:

14:51:28.5415 | DEBUG | BdcCommandGateway | [TRACE-GW] Sending CreateEntityRequest ID=e8fabcff-91c7-494c-847f-50660b394cbe
14:51:28.5683 | INFO  | CreateEntityRequestSystem | [SimHost] Spawned entity 1 (TkbType=101) for request e8fabcff-91c7-494c-847f-50660b394cbe.
14:51:28.5683 | DEBUG | BdcCommandGateway | [TRACE-GW] CreateEntityAck ID=e8fabcff-91c7-494c-847f-50660b394cbe Entity=1 Error=0
14:51:28.5732 | DEBUG | BdcCommandGateway | [TRACE-GW] Sending MissionControlRequest ID=230a1039-1b74-464c-821b-dbd36e0e818a Entity=1
14:51:28.5732 | DEBUG | NetworkSpawningSystem | [TRACE-SH] ProcessSpawn: NetworkId=1 TkbType=101
14:51:28.5915 | DEBUG | BdcCommandGateway | [TRACE-GW] MissionControlAck ID=230a1039-1b74-464c-821b-dbd36e0e818a Error=2
14:51:28.5915 | WARN  | MiniIosPanelState | [IG] MissionControlAck returned error 2 for entity 1.
14:51:28.6125 | DEBUG | GeoSpatialEgressTranslator | [TRACE-SH] Egress: Writing GeoSpatial for NetID=1 pos=(52,51479331662542,13,413806305033889)
14:51:28.6125 | DEBUG | EntityMasterEgressTranslator | [TRACE-SH] Egress: Writing EntityMaster for NetID=1
14:51:28.6125 | DEBUG | EntityMasterIngressTranslator | [TRACE-IG] Ingress: EntityMaster NetID=1 -> Ghost spawn
14:51:28.6125 | DEBUG | GeoSpatialIngressTranslator | [TRACE-IG] Ingress: GeoSpatial Entity=1 Lat=52,51479331662542 Lon=13,413806305033889
14:51:28.6236 | DEBUG | StyleResolutionSystem | [TRACE-IG] Style: Resolved Entity=1 Texture=
14:51:28.6236 | DEBUG | 0, Culture=neutral, PublicKeyToken=null]] | [TRACE-IOS] DER: Received EntityMaster for NetID 1. Storing in Repo.
14:51:28.6236 | DEBUG | NetworkGatewaySystem | Entity 0: Reliable mode. Peers:
14:51:28.6236 | DEBUG | NetworkGatewaySystem | Entity 0: No peers. ACKing.
14:51:28.6477 | DEBUG | EntityLifecycleModule | [TRACE-SH] ELM: Entity 0 received all ACKs. Promoting to Active.
14:51:28.6477 | DEBUG | EntityLifecycleModule | [TRACE-SH] ELM: Entity 0 promoted to Active
14:51:28.6477 | DEBUG | NetworkGatewaySystem | Entity 1 missing PendingNetworkAck. ACKing.
14:51:28.6729 | DEBUG | EntityLifecycleModule | [TRACE-SH] ELM: Entity 1 received all ACKs. Promoting to Active.
14:51:28.6729 | DEBUG | EntityLifecycleModule | [TRACE-SH] ELM: Entity 1 promoted to Active
14:51:35.9616 | DEBUG | GeoSpatialIngressTranslator | [TRACE-IG] Ingress: GeoSpatial Entity=1 Lat=52,51479331662542 Lon=13,413806305033889
14:51:45.9810 | DEBUG | GeoSpatialIngressTranslator | [TRACE-IG] Ingress: GeoSpatial Entity=1 Lat=52,51479331662542 Lon=13,413806305033889
14:51:56.0075 | DEBUG | GeoSpatialIngressTranslator | [TRACE-IG] Ingress: GeoSpatial Entity=1 Lat=52,51479331662542 Lon=13,413806305033889


# Analysios

The bug is clear: MissionControlAck Error=2 in the failure case. Let me dig into the error codes and the MissionControlRequestSystem to find the race condition.

Root cause identified: MissionControlAck Error=2 = `ERR_ENTITY_NOT_FOUND`. The MissionControlRequest arrives on SimHost and is processed **before NetworkSpawningSystem.ProcessSpawn has had a chance to register the entity in NetworkEntityMap** — a race between the DDS round-trip latency and the system execution order within a single tick. Let me verify the system phases and read the full file.

Precise root cause: MissionControlRequest arrives on SimHost **before NetworkSpawningSystem.ProcessSpawn has registered the entity in NetworkEntityMap**, producing `ERR_ENTITY_NOT_FOUND (Error=2)`. Let me read the gateway and the failing path to confirm.

Now I have the full picture. The race condition is:

1. CreateEntityRequestSystem (Input phase of _kernel.Update()) publishes SpawnEntityCommand to the event bus's **write buffer** and immediately sends CreateEntityAck via DDS.
2. SwapBuffers() is called only at the **end** of SimHostSubsystem.Update(), so NetworkSpawningSystem (BeforeSync phase, same _kernel.Update()) reads the **old** read buffer and does NOT see the SpawnEntityCommand yet.
3. The entity is registered in NetworkEntityMap only in the **next frame's** _kernel.Update() (BeforeSync).
4. Between those frames, DdsCommandClient's background listener thread (polling every 10ms) picks up the CreateEntityAck and fires the continuation on the ThreadPool, which sends MissionControlRequest to DDS.
5. That request arrives at SimHost and is processed by MissionControlRequestSystem in _kernelGroup.Run(), which runs **before** _kernel.Update(). Entity not in map → Error=2.

**Fix**: MissionControlRequestSystem should queue requests for not-yet-spawned entities and retry them for a few frames instead of immediately sending `ERR_ENTITY_NOT_FOUND`.

Implementing the retry queue in MissionControlRequestSystem.cs