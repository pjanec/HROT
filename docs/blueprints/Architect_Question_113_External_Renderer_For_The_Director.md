<!--STATUS
state: LIVE
updated: 2026-11-21
current-answer: §2 the sub-questions with their recommended answers (C RULED, R-11008; A, B, D, E, F OPEN — awaiting the user)
stale-below: nothing
related-designs:
  - ../designs/IG/DESIGN_External_Renderer_Director.md — OWNS the design this question gates (diagrams, items H-1 … H-8, acceptance)
  - ../DESIGN_Film_Director.md — OWNS IFrameSink, IDirectorSound, the runner and capture; A, B, E change what a sink is told
  - ../DESIGN_Director_Timeline_Editor.md — OWNS the render queue (§11u) whose job gets a renderer and a sound source
  - ../DESIGN_Film_Production.md — OWNS F-1 (films from recordings in the Replay Browser), which C is about
  - DESIGN_Time_Architecture.md — OWNS the anchors; D is a change there
  - ../DESIGN_Node_Extensions.md — OWNS INodeExtension / INodeCapability; A adds what an extension may offer
-->

# Architect Question 113 — an external renderer for the director (DD-022's HROT half)

**Asked by:** `ui` lane (the external-renderer programme), `2026-11-21`. **Decides:** how a node extension (the SimNetGateway, for the Unreal
image generator) becomes the director's frame sink and sound source, and what HROT changes for it. The wire side is approved in the gateway
(DD-022 rev 4, R-138 there). ⚠ Blast radius: the director's sink seam (both director hosts), `Hrot.Core`'s public surface, the master clock
(every cluster node), the Replay Browser's composition.

## 1. Why this is a question, not a build

🔒 User, `2026-11-21`: *"when the Unreal is connected via the gateway, the director will not take pictures of internal HROT 3D map, but will use
the Unreal over the gateway"* — the WHAT is ruled (gateway R-136 .. R-138). The HOW crosses four owning designs and two lanes' code.

### INVENTORY

The queries and their totals are in the design's [§1 INVENTORY](../designs/IG/DESIGN_External_Renderer_Director.md): `.*FrameSink.*` (14; one
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
| the proven solution for this problem class | — | an offline renderer driven frame by frame by the simulation (a render farm's "frame server": the master sends frame + camera, the renderer acknowledges) — adopted: it is the director's own stepping rule (R-3038) with a different sink |

## 2. The sub-questions — each with a recommended answer

| # | question | ⭐ recommended | why | rejected |
|---|---|---|---|---|
| **A** | how does an extension offer a renderer, and where does the contract live? | **`IExternalFrameRenderer` + `FilmFrame` / `FilmJob` / `FilmTrack` in `Hrot.Core`, offered through an `ExternalRenderers` registry on `HrotNodeContext`** (one current renderer; offer / withdraw) | below both the director and the gateway; node state, not world state; the `TerrainContent` registry is the precedent | a world singleton holding a live object (it is not world state; recording and save would need opting out) · a direct `Hrot.Presentation` reference from the gateway (pulls the whole UI into a node extension) |
| **B** | where is the renderer chosen? | **one `RoutingFrameSink` built in `DirectorPack.Attach`, choosing at `IFrameSink.Begin(job)`** (new default-no-op members `Begin` / `End`); `renderer: auto \| map` on the capture request; cards always the map's | one place for both hosts (F-2); a job never switches renderer mid-way | the choice in each caller (runner, stills, queue): three places that can disagree |
| **C** | renders from recordings (Film Production F-1) — where? | ✅ **RULED `2026-11-21` (R-11008): the editor only.** 🔒 User: *"If we can do the director in the editor with the gateway connected, fully functional, then we do not need any integration with gateway and replay browser basically. So that point might be skipped."* — the editor's replay (AAR) hosts the Director over its own recording ([Review Perspective](../UX/DESIGN_Review_Perspective.md) §1 row 1) and the gateway is one of the editor node's extensions; a job that names no recording renders the shown source (`DirectorRenders.cs:17`). ⛔ **No Replay Browser integration** (CE-11020 dropped). What stays out, accepted: a job or reel item that NAMES another recording (the editor has no opener, `DirectorRenders.cs:243` — refused with its reason) and multi-node exercises (Federation) | the editor already hosts the director over a recording | the Replay Browser composing node extensions — not needed (the user) |
| **D** | F-H1 — a running scale change publishes no anchor | **fix in the master: `SetTimeScale` while Continuous publishes a Continuous re-anchor** (`BarrierWallTicks` = now, `SimTimeSnapshot` = the time, the new scale), gated by `SimTimeSyncIntegrationTests`; a cross-lane edit in backend's time code, said | the cluster's slaves need it as much as the gateway | the gateway re-anchoring on its own only (slaves keep the bug) |
| **E** | the sound of a render job | **`sound: renderer \| hrot` per job**; with `renderer` the encode muxes the renderer's `FilmTrack` and refuses a sample total that does not match the frames; `IDirectorSound` unchanged for map renders | the user: *"take the audio track from the Unreal"* | dropping HROT's track (map renders would lose sound) |
| **F** | the camera of a frame | **the runner passes the film camera (pose, field of view), the film rate and the step in `FilmFrame`** | exact by construction | the renderer reading `CameraSuggestion` (written in `MapViewModeLayer`'s update — possibly a frame off; not measured) |

**Build order once approved:** A + B + F with their rails (CE-11018) → E (CE-11021) → D (CE-11019, with the integration suite) → the docs
(CE-11022). (C slice 2, CE-11020, dropped by R-11008.) The gateway's DD-022 work (its topics and `DpsFrameSink`) follows A + B + F. Blast radius: `Hrot.Core`'s
public surface (new types), the director (`DirectorPack`, `DirectorRunner`, `DirectorStills`, `DirectorRenders`), `MasterSyncController`
(every cluster node), later the Replay Browser.

## 3. What would change the recommendation

A requirement for reels spanning several recordings, or multi-node exercises, from the start (C's slice 2 would move first); a measured frame lag of `CameraSuggestion` of zero
(F would not need the explicit camera, though explicit stays simpler); a reason the scale change must stay local to the master (D).
