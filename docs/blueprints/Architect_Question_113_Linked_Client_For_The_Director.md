<!--STATUS
state: LIVE
updated: 2026-11-21
current-answer: §0 the ruling (R-11009: A–J approved as recommended, the concept named LINKED CLIENT) · §2 the sub-questions as approved
stale-below: nothing
related-designs:
  - ../designs/IG/DESIGN_Linked_Client_Director.md — OWNS the design this question gates (diagrams, items H-1 … H-8, acceptance)
  - ../DESIGN_Film_Director.md — OWNS IFrameSink, IDirectorSound, the runner and capture; A, B, E change what a sink is told
  - ../DESIGN_Director_Timeline_Editor.md — OWNS the render queue (§11u) whose job gets a client and a sound source
  - ../DESIGN_Film_Production.md — OWNS F-1 (films from recordings in the Replay Browser), which C is about
  - DESIGN_Time_Architecture.md — OWNS the anchors; D is a change there
  - ../DESIGN_Node_Extensions.md — OWNS INodeExtension / INodeCapability; A adds what an extension may offer
-->

# Architect Question 113 — an linked client for the director (DD-022's HROT half)

**Asked by:** `ui` lane (the linked-client programme), `2026-11-21`. **Decides:** how a node extension (the SimNetGateway today; HROT does not know what is on its other side) becomes the director's frame sink and sound source, and what HROT changes for it. The wire side is approved in the gateway
(DD-022 rev 4, R-138 there). ⚠ Blast radius: the director's sink seam (both director hosts), `Hrot.Core`'s public surface, the master clock
(every cluster node), the Replay Browser's composition.

## 0. ✅ RULED `2026-11-21` (R-11009)

🔒 User, verbatim: *"I do not want it to be called external renderer, because the gateway is just a gateway and you do not know what is on the
other side basically, so we need to find some more generic name. No external renderer, no Unreal. Otherwise approved."* — A, B, D, E, F, G, H, I
and J approved as recommended (C was ruled before, R-11008); the concept is named **linked client** (`ILinkedClient`, `LinkedClients`,
`LinkedClientLoadStep` / `LoadPart.LinkedClient`, `frames: auto | map`, `sound: client | hrot`) — HROT knows a client linked through a node
extension, never what it is.

## 1. Why this is a question, not a build

🔒 User, `2026-11-21`: *"when the Unreal is connected via the gateway, the director will not take pictures of internal HROT 3D map, but will use
the Unreal over the gateway"* — the WHAT is ruled (gateway R-136 .. R-138). The HOW crosses four owning designs and two lanes' code.

### INVENTORY

The queries and their totals are in the design's [§1 INVENTORY](../designs/IG/DESIGN_Linked_Client_Director.md): `.*FrameSink.*` (14; one
interface, one implementation), `.*DirectorSound.*` (10; one implementation), `.*DirectorPack.*` (44; ONE sink built in `Attach`, two hosts),
`DirectorRenders` (22), `.*NodeExtension.*` (31) and `.*NodeCapabilit.*` (39; an extension offers the director nothing today),
`.*CameraSuggest.*` (10), `SwitchTimeModeEvent` (22 + 6 publish sites), grep for the bootstrap users (the Replay Browser composes no
extension). ⚠ Coverage could not be checked (the CLI has no `check_index_coverage`).

| the lean rests on | code — how it IS | design — how it was MEANT |
|---|---|---|
| one sink, built once for every host | ✅ `DirectorPack.cs:95-101`; callers `EditorSubsystem.cs:1805`, `ReplayBrowserSubsystem.Director.cs:44` | ✅ Film Production F-2 "one director, every host" |
| a headless host cannot capture today | ✅ `DirectorPack.cs:101` (`Headless ? null`), `DirectorRunner.cs:357`, `DirectorRenders.cs:168` | ✅ Film Director §8 B8 (the sink is the map) |
| the gateway cannot see `Hrot.Presentation` | ✅ SimNetGateway `SimNetGateway.HrotHost.csproj` references | ✅ Look Rules D-1 (R-11000: the camera suggestion went to Hrot.Core for the same reason) |
| an extension reaches the node through `HrotNodeContext` | ✅ `NodeCapability.cs:142-217` (`PopulateSystems(HrotNodeContext, …)`); `HrotNodeContext.TimeController` `:183`; registry precedent `TerrainContent` `:192` | ✅ Node Extensions §2 |
| every anchor and step is on the node bus | ✅ `MasterSyncController.cs:130, 383, 457, 497, 537, 579, 605` | ✅ Time Architecture §9 (the bus is the one time-command path) |
| a running scale change publishes no anchor | ✅ `MasterSyncController.cs:196-225` (`SetTimeScale` sets the field only) | ⛔ searched Time Architecture for a scale-anchor rule: none found (§9b covers pause only) |
| the Replay Browser composes no node extension | ✅ no `SharedApplicationBootstrapper` / `NodeExtensions` in `Hrot/Subsystems/Hrot.ReplayBrowser` | ✅ Node Extensions §1 ("hosts without it never run one") |
| the proven solution for this problem class | — | an offline image generator driven frame by frame by the simulation (a render farm's "frame server": the master sends frame + camera, the client acknowledges) — adopted: it is the director's own stepping rule (R-3038) with a different sink |

## 2. The sub-questions — each with a recommended answer

| # | question | ⭐ recommended | why | rejected |
|---|---|---|---|---|
| **A** | how does an extension offer a linked client, and where does the contract live? | **`ILinkedClient` + `FilmFrame` / `FilmJob` / `FilmTrack` in `Hrot.Core`, offered through an `LinkedClients` registry on `HrotNodeContext`** (one current client; offer / withdraw) | below both the director and the gateway; node state, not world state; the `TerrainContent` registry is the precedent | a world singleton holding a live object (it is not world state; recording and save would need opting out) · a direct `Hrot.Presentation` reference from the gateway (pulls the whole UI into a node extension) |
| **B** | where are a job's frames routed? | **one `RoutingFrameSink` built in `DirectorPack.Attach`, choosing at `IFrameSink.Begin(job)`** (new default-no-op members `Begin` / `End`); `frames: auto \| map` on the capture request; cards always the map's | one place for both hosts (F-2); a job never switches client mid-way | the choice in each caller (runner, stills, queue): three places that can disagree |
| **C** | renders from recordings (Film Production F-1) — where? | ✅ **RULED `2026-11-21` (R-11008): the editor only.** 🔒 User: *"If we can do the director in the editor with the gateway connected, fully functional, then we do not need any integration with gateway and replay browser basically. So that point might be skipped."* — the editor's replay (AAR) hosts the Director over its own recording ([Review Perspective](../UX/DESIGN_Review_Perspective.md) §1 row 1) and the gateway is one of the editor node's extensions; a job that names no recording renders the shown source (`DirectorRenders.cs:17`). ⛔ **No Replay Browser integration** (CE-11020 dropped). What stays out, accepted: a job or reel item that NAMES another recording (the editor has no opener, `DirectorRenders.cs:243` — refused with its reason) and multi-node exercises (Federation) | the editor already hosts the director over a recording | the Replay Browser composing node extensions — not needed (the user) |
| **D** | F-H1 — a running scale change publishes no anchor | **fix in the master: `SetTimeScale` while Continuous publishes a Continuous re-anchor** (`BarrierWallTicks` = now, `SimTimeSnapshot` = the time, the new scale), gated by `SimTimeSyncIntegrationTests`; a cross-lane edit in backend's time code, said | the cluster's slaves need it as much as the gateway | the gateway re-anchoring on its own only (slaves keep the bug) |
| **E** | the sound of a render job | **`sound: client \| hrot` per job**; with `client` the encode muxes the client's `FilmTrack` and refuses a sample total that does not match the frames; `IDirectorSound` unchanged for map renders | the user: *"take the audio track from the Unreal"* | dropping HROT's track (map renders would lose sound) |
| **F** | the camera of a frame | **the runner passes the film camera (pose, field of view), the film rate and the step in `FilmFrame`** | exact by construction | the client reading `CameraSuggestion` (written in `MapViewModeLayer`'s update — possibly a frame off; not measured) |

> ⭐ **F as built-to-be (measured `2026-11-21`, after the ruling — a refinement, not a change of F):** the director holds a shot SPEC (`FilmStudio.Shot`); the pose is resolved per frame by the map's `FilmCaptureLayer` (`Film.cs:296-320`). So "the runner passes the film camera" is realised as: the linked-client frame goes through the same capture mailbox, and the map's layer reads the pose it would have rendered and hands it over instead of rendering. A 2-D shot cannot go to a client (refused with the reason; `frames: map` renders it).
| **G** | a job or reel item that NAMES another recording — the editor refuses it (`DirectorRenders.cs:243`, no opener) | **the editor passes its ONE recording opener to the director**: `DirectorPackHost.Recordings` = an `IRecordingOpener` over the editor's `ScenarioDoor.OpenRecording(exerciseId, atSim)` (`ScenarioDoor.cs:353`, the start screen's and the menu's door into AAR); a job then opens its recording at its turn and waits for the load, as the Replay Browser's does | 🔒 user `2026-11-21`: *"I can open any recording for a replay in the start screen of the editor"* — the editor HAS the opener and does not pass it (the silent-default pattern); `IRecordingOpener`'s comment *"a host that opens no recordings (the editor …)"* is stale | a second, director-only opener (a duplicate of the door) |
| **H** | `Impact` in replay — the recorded effect entity of an impact has no impact kind and no shooter | **`EffectCause` gains `Kind` (entry / exit / stop) and `Shooter`, written by the spawner from `RoundImpactNotification` (which carries both, `RoundImpactNotification.cs:18, 33`)**; the gateway then makes `Impact` from the effect entities, live and in replay alike (R-11007: from state, never from re-raised events) | 🔒 user `2026-11-21`: *"not as components, as little entities, affect parts. So the gateway can freely check for them and generate whatever events it needs"* — the entities are there; only two fields are dropped on the way | recording the impact event (a second, event-based path, against R-11007) |
| **I** | who waits for the linked client on a load (a new scenario or recording, often a new terrain)? | **the cluster LOAD PHASE, by the SAME mechanism as the 3-D map: an `LinkedClientLoadStep : ILoadPartProvider` (a new `LoadPart`) beside `MapBuildLoadStep` on the station node** — `IsResolved` polls `LinkedClients.Current.ReadyFor(worldEpoch)`, `Failure` fails the load with the client's reason, `ProgressStep` shows its step on the heartbeat, placed after the terrain part; **required automatically while a linked client is offered (the gateway connected)**, as the user put it. The chain resolves only when every part is resolved ⇒ the node is play-ready only after BOTH HROT's own 3-D map (when its `map3d.in-load` says so) AND the client's world are loaded; the master's *load and run* (R-3075) and the director's `ScenarioReadyProbe` wait through it. 🔒 user `2026-11-21`: *"it needs to apply for any terrain load, not just when director wants it or in the replay. It must work also in the live"* ⇒ **EVERY load that changes the world waits for it**: a live / edit load runs the chain (`EcsHostClusterHandlers.cs` step 7) — covered; ⚠ **a replay open does NOT run the chain** (`LoadPhaseChain.cs:160`: *"does NOT claim PrepareState(OperatingReplay\|Idle) — those belong to the replay handler"*; `ReferenceReplayLoadHandler`, the recording's terrain by name) ⇒ the replay handler's prepare must resolve the SAME providers (the client's, and the 3-D map's where its `map3d.in-load` says so) before it reports ready. To measure with the build: how a replay open makes its terrain resident on the editor node, and whether HROT's own map step waits there today | 🔒 user `2026-11-21`: *"Is it the same mechanism the 3D map is actually using? … if gateway is connected, the system needs to wait for both the 3D map … as well as the gateway's … map. Only that it will make sense."* — `MapBuildLoadStep` (`Hrot.Core/Services/LoadPhase/MapBuildLoadStep.cs`) is exactly that shape: polls a status, fails with its reason, reports its step | a per-node opt-in like `MapInLoad` (this answer's first version, same day — superseded: connected means wait) · the director's runner waiting on its own (a second readiness path the master and live play cannot see) |
| **J** | F-H3 (measured `2026-11-21`): **the editor's replay open makes NO terrain resident** — the replay plan is state steps + a seek (`TransitionPlanner.cs:140-157`), the prepare installs the replay module only (`EcsRecordReplayController.cs:387-409`), the chain does not run for a replay (`LoadPhaseChain.cs:160`), `TerrainWorld` / `TerrainDefinition` are `NoReplay` (re-derived from the asset on a load), and the recording's terrain name reaches only the geo origin (`ClusterMaster.GeoReference.cs:79-84`); only the Replay Browser loads it (`ApplyRecordingTerrain`, CE-3118). A recording of another terrain replays over the wrong one (or none) and no map or client step runs | **a replay open runs the load chain's SHARED world-ready parts for the recording's terrain**: the replay handler's prepare resolves the chain's `Terrain` part with `RecordingMetadata.TerrainName` (and its overlay hash, AQ112's versions), then `Map3D` (when `map3d.in-load` says so) and the linked client (when offered) — the same providers, one readiness; the replay's entities then come from the recording as today. The Replay Browser's `ApplyRecordingTerrain` moves onto the same parts later (one way to make a recording's terrain resident) | 🔒 user `2026-11-21`: *"it needs to apply for any terrain load … It must work also in the live"* and *"Please measure now. The design must be complete."* — one terrain load for every world change, live or replay, so every map waits the same way; ⚠ the load phase is backend's (a cross-lane change, its owner design `DESIGN_Cluster_Load_Phase.md`) | a replay-only terrain loader in the editor (a third copy beside the chain and the Replay Browser's) · leaving it (a start-screen recording of another terrain replays wrong, with no map or client wait) |

**Build order once approved:** A + B + F with their rails (CE-11018) → E (CE-11021) → D (CE-11019, with the integration suite) → the docs
(CE-11022); G (CE-11023) and H (CE-11024) are independent and small; J (CE-11025) before I's replay half. (C slice 2, CE-11020, dropped by R-11008.) The gateway's DD-022 work (its topics and `DpsFrameSink`) follows A + B + F. Blast radius: `Hrot.Core`'s
public surface (new types), the director (`DirectorPack`, `DirectorRunner`, `DirectorStills`, `DirectorRenders`), `MasterSyncController`
(every cluster node), later the Replay Browser.

## 3. What would change the recommendation

A requirement for reels spanning several recordings, or multi-node exercises, from the start (C's slice 2 would move first); a measured frame lag of `CameraSuggestion` of zero
(F would not need the explicit camera, though explicit stays simpler); a reason the scale change must stay local to the master (D).
