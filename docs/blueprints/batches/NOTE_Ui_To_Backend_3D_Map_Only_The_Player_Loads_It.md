<!--STATUS
state: LIVE
updated: 2026-10-29
current-answer: whole file
related-designs:
  - ../../DESIGN_Cluster_Load_Phase.md — §11.9 (R-1046, this note's design) · §11.8 (the NodeRunProfile it extends)
  - ../Architect_Question_110_Readiness_Protocol.md — §4 R3, narrowed by R-1046
-->
# NOTE ui → backend — R3 narrowed: only the PLAYER loads the 3-D map in its chain (R-1046, CE-4078)

🔒 **User, `2026-10-29`:** *"I think it is just the player node who needs to include the 3D map into the load chain. The other nodes like CGF
and SimHost do not need to load 3D map. Actually, they should start in 2D map as now and load the map on the background when first request for
map switching is received. that player-specific behavior needs to be just a parameter of a unified shared code as usual."*

| | |
|---|---|
| ⛔ superseded | AQ110 R3 *"the 3-D map rebuild as a load step on **map hosts**"* — CGF / SimHost / editor no longer wait for it |
| ⭐ the shape | ONE shared 3-D map builder (geometry on a worker, GPU upload on the frame — AQ91 §4-B3, removes CE-4078's ~40 s stall everywhere); the node's profile (`NodeRunProfile.MapInLoad`, §11.8) decides whether the load chain waits for it — true for the Player only |
| ⭐ ui builds | the builder, `MapViewSwitch`'s background build on the first 2-D → 3-D request, the profile flag |
| ⭐ backend builds | the load-chain step "3-D map" that resolves when the node's builder reports done — added only when the profile says so; skipped on a node with no 3-D map |

⏳ Ask: confirm the chain-step seam you want ui's builder to report into (an `ILoadProgressSource`-style "busy / done" the step polls, or a
completion the step awaits) — ui will build against your answer. Design: [§11.9](../../DESIGN_Cluster_Load_Phase.md).
