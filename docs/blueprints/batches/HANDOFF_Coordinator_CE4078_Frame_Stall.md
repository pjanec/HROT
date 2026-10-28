<!--STATUS
state: LIVE — coordinator → the session that owns CE-4078: the design to build
updated: 2026-10-28
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_91_Player_Nodes.md §4-B3 — THE DESIGN (diagrams, decisions S0–S5, acceptance); §4-B2 — the rule it applies (R-4001)
  - ../Blueprint_Issues_Tracker.md CE-4078 — the row (the observation, the log)
-->
# HANDOFF — CE-4078: the 3-D map must not freeze a node (and a catch-up must not fail on its own recording)

**Dispatched at** the commit that adds this file (`coordinator`). The design is
[AQ91 §4-B3](../Architect_Question_91_Player_Nodes.md); it applies the approved rule of §4-B2 (R-4001). Build it as written, and fold any
deviation back into §4-B3.

| step | what | where to start |
|---|---|---|
| **S0** | `FrameStallProbe`: a frame longer than 1 s logs WARN naming the slowest phase/layer and its ms. Reproduce with `scripts/repro-ce4078.sh` (Linux, Xvfb — it already shows the ~37–40 s stall and the ejection when the 3-D map opens) and on Windows, and **name the culprit layer with numbers** before changing anything else | the node frame loop (`NodeBootstrapper` / the host's frame), the map's layer `Draw` loop |
| **S1** | the 3-D map's mesh rebuilds build their vertex data on a worker through ONE shared `PendingMeshBuild<T>`; the GPU upload stays on the frame thread | `Hrot/Engine/Hrot.Presentation/Map3D/TrafficLayer3D.cs:92` (`Rebuild`), `FDP/Engine/Fdp.Presentation/Vis3D/TerrainLayer3D.cs:59` (`Rebuild`) — both already split into a pure static `BuildVertexData` + `UploadMesh`. ⚠ Check the inputs are immutable (`ITerrainRenderGeometry.BuildTagged`, `TrafficNetCache`) |
| **S2 / S3** | the heartbeat stays on the frame thread; the 5 s timeout stays | ⛔ no heartbeat thread, no longer timeout (§4-B3 says why) |
| **S4** | a caught-up node records into its own file; `AsyncRecorder` keeps `CreateNew` | `FDP/Toolkits/Fdp.Toolkits/Replay/RecordingModule.cs`; check that C9's span replay takes several files per node |
| **S5** | a failed catch-up returns the node to `Idle` and the master shows why | `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.InSync.cs` (C7), the R-4004 cancel path |

**Done when** the four acceptance points of §4-B3 hold. For each new test, report what it asserts and the mutation that turned it red.
Tick CE-4078 with the measured culprit named.

_coordinator lane, 2026-10-28_
