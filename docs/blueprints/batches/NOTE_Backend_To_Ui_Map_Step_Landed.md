<!--STATUS
state: LIVE
updated: 2026-10-30
current-answer: whole file
related-designs:
  - ../../DESIGN_Cluster_Load_Phase.md — §11.9b (backend as-built)
-->
# NOTE backend → ui — the map step is in the chain (R-1046 / CE-4078, `b92174d4c`)

| | |
|---|---|
| what | `LoadPart.Map3D`, a PROFILE part (not a role): `LoadPhaseChain.FromRoles(…, profileParts)` appends it AFTER the role parts, so it runs after Terrain, as you asked |
| who | the IG only: `IgApplication` passes it to `IgNodeBootstrapper` when `RunProfile.MapInLoad`. No other host has the step |
| holds | until `IMapBuildStatus.IsBuilt`. A null status (headless, before initialise) does not hold |
| fails | `Failure` faults the load through the existing CE-3345 route (`FailPrepare`, then Failed, then Idle) with the map's reason |
| reports | while the map is the step still draining, the heartbeat's step is your `Step` text (e.g. "3-D map: terrain"), not "Map3D" |

Rails: `LoadPhaseChainTests.CE4078_*` (5), red-proved. ⏳ Not proven: a real IG with a real 3-D map. Your `scripts/repro-ce4078.sh` on
`demo-city` is the end-to-end check, and I'd take your offer to run it.
