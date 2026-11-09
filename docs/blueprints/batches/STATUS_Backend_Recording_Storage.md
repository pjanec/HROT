# STATUS — backend: recording storage + autosave ([handoff](HANDOFF_Coordinator_To_Backend_Recording_Storage.md))

| # | id | state | commit | note |
|---|---|---|---|---|
| W-1 | CE-4332 | DONE | `680c3b30c` | the Player's node-id offset arm |
| W-2 | CE-4099 | DONE (before this handoff) | `aecbad03e` `afaaaecba` | built on the amended capture in the previous batch (Y-5): AQ108 §4c.2–§4c.3; A8 not needed; hot-reload race discards the round's extraction (revision check) |
| W-3 | CE-4330 | DONE except ⑤'s MEASUREMENT — ①–④ ⑥–⑩ built; ⑤ the throttle built and rail-proven | ① `0ac09947f` · ②⑤⑦ this commit | ① manifest-last (§10) · ② the job queue: join / one at a time / cancel / refusals; failed node rounds and import failures now answer · ⑤ give-way at 20 MB/s while Live (archive copy only; import not throttled yet) · ⑦ delete from archive = a queued job (`ArchiveDeleter`, wired with ⑩) · ③ fact ① on every ClusterView (`d950ac7b3`, NOTE to ui) · ③ facts ② ③, ⑥ model + `POST /session/close`, ⑩ HTTP, and the storage asks now cross the wire whole (this commit) · ④ import to who lacks it + ⑧ auto-resume (`44105ab4c`), ④ the local cache (this commit) · ⑨ the quit guard (this commit) · ⚠ ⑤'s acceptance (a Live run's frame time and DDS latency with a transfer = without) is NOT measured: on this one cloud VM the transfer is a local-disk copy (no SMB link, no separate NAS) and the DDS traffic is loopback, so the numbers would not describe the deployment — it needs the lab cluster (filed below) |
| W-4 | CE-4331 | DONE — ① metadata (`fb1901bf4`) · ⑥ DONE (`3bd985f40` + this commit) · ② placement (this commit; ⑤'s case-table rails with it) · ③ the job names what is not played / who listens, ⑥ Go Live refused (this commit) · W-4 DONE | `fb1901bf4` | `NodeIdRanges` (the ONE offsets table, the runner uses it too) + `NodeIdAllocator` grants the lowest free id of the kind's range; next: the request carries the subsystem (wire) and the remembered PreferredId, then ①–⑤ |

---
_backend lane, 2026-11-08_

**[HANDOFF_Coordinator_To_Backend_After_Recording_Storage](HANDOFF_Coordinator_To_Backend_After_Recording_Storage.md)** (received at `cfcbe0fbd`):

| # | id | state | note |
|---|---|---|---|
| V-1 | CE-4341 | DONE | 📐 measured: every sim height query already answered the band (CE-4122); the one that did not was the 3-D PICK — `PickGrid.Build` read `Build` (no apron) while the layer draws `BuildTagged` ⇒ a click/drag on the band fell to the look-at plane (z ≈ 0). Now reads `BuildTagged`. Rail `Map3DPickingTests.CE4341_*` (red on `Build`); `Map3DPickingTests` + `HoverPickVolume` 14/14. Design §6ad-1 updated |
| V-2 | CE-4284 | DONE | a non-owner's route edit → `UpdateEntityDescriptorRequest(dtMapRoute)` → the owner applies + acks + publishes; one encoding `MapRouteWire`. Rail `MapRouteTranslatorTests.CE4284_*` (two nodes, round trip). Design `DESIGN_Details_Forms.md` §1 updated |

Gates (V): `Hrot.Map.Common.Tests` 88/88 · `Hrot.SimHost.Tests` (`UpdateEntityDescriptorRequestSystemTests` + `SimHostAppTests`) 8/8 · `Hrot.Presentation.Tests` Map3D under Xvfb 565/567 — the 2 reds (`FilmFrameRail.CE1380_A2DFilmFrame_*`: "the window shows the unit's symbol"; `Map3DFrameRail.CE1227_V_DemoCity_*`: 14283/18949 samples agree) are PRE-EXISTING: red at base `d688c6cc2` with `Picking.cs` reverted (2-D drawing, not picking — ui's area).
**[HANDOFF_Coordinator_To_Backend_Queue_After_V](HANDOFF_Coordinator_To_Backend_Queue_After_V.md)** (received at `560565730`):

| # | id | state | note |
|---|---|---|---|
| X-1 | CE-4345 | DONE | one steer limit in the one integrator (`BicycleModel.SteerLimit`, radius ≥ v²/`MaxLatAccel`); the possessed drive now integrates through `BicycleModel.Integrate`; the AI's cornering cap is the inverse. Rails `CarKinematicsSystemTests.CE4345_*` possessed + AI (red with the limit off); CarKinem 274/274; `Hrot.SimHost.Tests` Possession 12/12. Design Motion Domains §5i |
| X-2 | CE-4344 | FILED on behaviors (measured) | the latch is `OperatorControlBehavior.OperateWeapon` (behaviors), not the weapon: [NOTE](NOTE_Backend_To_Behaviors_Possessed_Shot_Through_Reload.md) with the proposed patch and rail shape |
| X-3 CE-4346 | Start editable · *Local time offset* · weather layers whole to the owner (dtWeather request) | ✅ backend half | NOTE to ui; Details forms Part 2 as-built |
| — (behaviors note) | `/run-mode` from Edit waits for the live load it starts (the next load was rejected inside its park) | ✅ | NOTE to behaviors; RUNBOOK_Filming §0b |
| X-4 CE-2153 | the SimHost integration harness paths (solver + engine-backed nav module + flat navmesh) | ✅ | `EntityMission_MovesEntity` green, project 50/50 |
| X-5…X-6 | | next | |

**Reds seen in `Hrot.ClusterRunner.Tests` (full run, `2026-11-08`), not from this batch** — none touches a storage file; owners named: `EveryReplayWriterIsGatedTests` (`EditorPerceptionSetupSystem`, ui/editor) · `EverySystemForgetsTheLastWorldTests` (`RuleEditSystem`, `RuleLeafSampler` — behaviors; `MacroExpansionSystem` — macros) · `EverySystemStateFieldIsClassifiedTests` ×2 (185 unclassified fields / 45 class-B translators — mixed, mostly replication) · `CgfSubsystemTests.CE1446_TheKernelRunsItsSystemsInTheSharedBootstrappersOrder` (`MacroExpansionSystem` newly in the order) · `HrotNodeBuilderTests.CE1436_CgfShapedHost_*` · `TheStartGateIsComposedOnceTests.AnOrchestratorOnlyProcess_*` (looks for a "New Scenario…" button no code builds any more — ui's Start-screen rework). ⚠ Not checked against a base sha.

**⑤ measurement — open, needs the lab cluster:** run a Live exercise on the real cluster, take frame time (every node) and DDS latency for 2 min with no transfer, then 2 min while an earlier exercise is archived (`POST /exercises/{id}/archive`); acceptance = the two equal within noise. If not equal, lower `StorageGatewayModule.GiveWayBytesPerSecond` (20 MB/s today) — ⚠ the import's push is not throttled yet (it goes through the transfer provider).
