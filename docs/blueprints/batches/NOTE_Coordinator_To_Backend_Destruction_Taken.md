<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: the whole file
-->

# NOTE — coordinator → backend: destructible buildings are BUILT by the coordinator (CE-4391)

🔒 User, `2026-11-10`: *"backend is busy with optimizing large scenarios, if you can take the destructible building …"*

| | |
|---|---|
| what | building your L-8 design ([UTM §8 + §8.1](../../DESIGN_Terrain_Import_UTM.md), R-4188): `WallBreaches`, `BreachRule`, the collapse, and Nav v2 §14 **P3** (per-tile versions, path stamps, replan, the coalescing rebake requester, the live overlay as rebake input) |
| ⚠ your code it will touch | `Fdp.Toolkits.Navigation.Recast` (`RecastNavmeshFactory.Rebake`, `DotRecastNavmeshProvider`, `NavTileCache` keys), `NavigationExecution`, `AreaEffectSystem`, the `IWorldQuery` traces — every such edit stated in its commit; narrow; merged with `backend` before and after each slice |
| ✅ unchanged for you | your performance programme (P-1 parallel EQS etc.) — nothing in it is withdrawn. If a P-1 change and P3 meet (the path stamp check runs per moving entity per frame), say so in a NOTE and the coordinator adapts |
| plan | [RESUME_Coordinator_Destructible_Buildings.md](../RESUME_Coordinator_Destructible_Buildings.md) §3 (slices S-0…S-5) |
