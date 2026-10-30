<!--STATUS
state: LIVE
updated: 2026-10-30
current-answer: whole file
related-designs:
  - ../../DESIGN_Cluster_Load_Phase.md — §11.8a (the player's waiting screen) · §11.9a (the map step)
-->
# NOTE ui → backend — the load is observable over HTTP now (CE-1451) · the mesh is built once (CE-1452) · merged backend@`ad1e7118f`

| | |
|---|---|
| ✅ `/status` (every host, one `DebugApiService`) | `load` (`localPhase`, `localStep`, `localReason`, `clusterProgress`, `playReady` — from `ClusterView`), `runGate` (`open`, `reason`, `loading`, `progress`, `inError`), `loadingScreen` (`visible`, `text`). `/view`: `pending3D`, `pending3DFailure`, `map.{built,step}` |
| ✅ the player too | the debug provider now carries the host's run choice independently of the time drive (`ISubsystemDebugProvider.RunChoice`) — the IG still has no drive (Q54) |
| 📐 live, `WITH_IG=1` | the IG's `localStep` / `loadingScreen.text`: *the load is starting* → *SimHost (node 1): Terrain* → *Terrain* → **"3-D map: terrain"** (~20 s — your `MapBuildLoadStep`'s `Step`, on the wire) → *Loading…* → PlayReady → gate open ~0.5 s later (the in-sync set). ℹ that run the IG was a late joiner (C7 catch-up into `OperatingLive`, no ejection) |
| ✅ CE-1452 | `TerrainWorldMesh.Build` memoised per `TerrainWorld` — `demo-city` 24.3 s → 8.5 s. ⚠ `RecastNavmeshFactory` reads the same shared arrays now (it copies them; nothing mutates) |
