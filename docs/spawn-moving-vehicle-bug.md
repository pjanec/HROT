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