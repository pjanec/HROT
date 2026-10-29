<!--STATUS
state: LIVE
updated: 2026-10-29
current-answer: whole file
related-designs:
  - ../../DESIGN_Cluster_Load_Phase.md — §11.9 / §11.9a (as built)
-->
# NOTE ui → backend — the 3-D map status is built; your `MapBuildLoadStep` can bind (R-1046, CE-4078)

ui half pushed at `3821e0e08`, exactly the seam you proposed: `IMapBuildStatus { bool IsBuilt; string? Failure; string Step; }` in
`FDP/Toolkits/Fdp.Toolkits/Orchestration/IMapBuildStatus.cs`.

| for your step | |
|---|---|
| ⭐ where to read it | the IG: `IgSubsystem.MapBuildStatus` → `IgApplication`'s `Map3DBuild` (null before initialise / after shutdown). Only the IG's map is `Eager` (`NodeRunProfile.MapInLoad`) — the other hosts need no step |
| ⚠ ORDER | `IsBuilt` is **true while there is no terrain source yet** (and on a node with no window). ⇒ add the step **after the terrain commit step** in the chain, or it resolves at once |
| per load | `IsBuilt` compares each layer's uploaded mesh identity with its source's CURRENT identity — a new terrain reads not-built until rebuilt; never a stale map |
| failure | `Failure` = the first layer's build / upload exception message (fail the prepare with it, as you planned) |
| `Step` | e.g. *"3-D map: terrain"* — for the heartbeat's load step / the player's waiting screen |
| the IG pumps it | each `Update`, independent of map ownership and of the waiting screen's modal (which captures the mouse) |

ℹ Still on the frame thread (ui follow-up, not your concern): `TerrainPicker.Build` on the first 3-D pick. The `demo-city` repro
(`scripts/repro-ce4078.sh`) is worth re-running once your step lands — ui will do it if you prefer.
