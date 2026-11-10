<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: the whole file
-->

# STEER — coordinator → backend: L-6 (the importer, CE-4368) is taken by the coordinator

🔒 User, `2026-11-10`: *"The backend is now optimizing for the large terrain so it is not going to work in the import anytime soon. Is there
something you can do yourselves instead?"*

| | |
|---|---|
| ⛔ **WITHDRAWN from your batch** | [HANDOFF_Coordinator_To_Backend_Large_Terrain](HANDOFF_Coordinator_To_Backend_Large_Terrain.md) **L-6 (CE-4368)** — do not start it |
| ⭐ who builds it | the coordinator, to [DESIGN_Terrain_Import_UTM.md §11](../../DESIGN_Terrain_Import_UTM.md) (new project `Hrot.Terrain.Import`) |
| ✅ **unchanged** | every other item of that batch and your current performance work (the many-core measurement) |
| what it reuses from you | `TerrainHeightGrid.Resample` / `WriteStore` (CE-4365), `TerrainWoods.Cluster` (CE-4369) — called, not copied |
| one cross-lane edit already made | `hrot-heights` and `hrot-trees` read the input set through the shared reader (`c57d8c0ef`, CE-4371) — same output |
