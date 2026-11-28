<!--STATUS
state: LIVE
updated: 2026-11-21
current-answer: the whole file — backend's report on HANDOFF_Ui_To_Backend_Linked_Client.md (dispatched at 710b16f47)
related-designs:
  - HANDOFF_Ui_To_Backend_Linked_Client.md — the dispatch this answers
  - NOTE_Backend_To_Ui_Linked_Client_Frames.md — the contract (CE-11018), the first commit after the start marker
  - ../../designs/IG/DESIGN_Linked_Client_Director.md — §3a (CE-11018) and §2e-1 (CE-11025) hold the as-built
  - ../DESIGN_Time_Architecture.md — §21 (CE-11019, CE-11022)
-->

# STATUS — backend: the director renders through the linked client, plus four smaller items

**Started** `d944819b9` on `backend` at `b0fd99eb5`, merged `ui@7d0ea98f9`. **Scope frozen at `710b16f47`.** No later document changed a task.
⛔ `Hrot.Gameplay` and the possession code were not touched (ui's CE-11027).

## 1. Per task

| # | id | state | commits | design folded |
|---|---|---|---|---|
| 1 | [CE-11018](../Blueprint_Issues_Tracker.md) the director renders through the linked client | ✅ built | `f647e9d96` (contract + NOTE) · `3b69d64d3` · `bbfc78244` | [linked-client design](../../designs/IG/DESIGN_Linked_Client_Director.md) §3a |
| 2 | [CE-11019](../Blueprint_Issues_Tracker.md) a rate change while running re-anchors every slave · [CE-11022](../Blueprint_Issues_Tracker.md) the time docs describe the anchor model | ✅ built | `220de5187` | [Time Architecture](../DESIGN_Time_Architecture.md) §21; `FDP.Toolkit.Time.md` top section; time-ctrl-unif §4.6 superseded |
| 3 | [CE-11025](../Blueprint_Issues_Tracker.md) a replay open makes the recording's terrain resident | ✅ built | `5f9afcf9a` | linked-client design §2e-1; `DESIGN_Cluster_Load_Phase.md` pointer |
| 4 | [CE-11023](../Blueprint_Issues_Tracker.md) the editor's director gets the editor's recording opener + the load wait | ✅ built | (this commit) | linked-client design §3b |
| 5 | [CE-11024](../Blueprint_Issues_Tracker.md) an impact's effect carries its kind and shooter | ✅ built | `ec0748db6` | `DESIGN_Look_Rules_For_Renderers.md` RR-10 row |

## 2. Lane decisions (and what would change each)

| decision | why | would change if |
|---|---|---|
| CE-11018: a scene frame for the client goes through the SAME capture mailbox; the map's film layer places the camera and hands back the pose, without rendering (`FilmCaptureRequest.PoseOnly`) | one camera path: the client gets exactly the map's camera (D-3), and no second camera solver exists | the client needs frames while no map is drawn (a headless node) — then the pose needs a path off the film layer |
| CE-11018: every film-frame index reaches the client, in order — a card as `Silence`, a slow-motion repeat as `Repeat` | the gateway's track check counts frames; a gap reads as a lost frame | the gateway prefers card frames as images (D-4 says cards are the map's) |
| CE-11018: `BeginFilm` / `EndFilm` (not `Begin` / `End`) and a camera as forward + up, with `Fade` | names that do not collide on a client that implements other `Begin`s; forward + up is what the map's camera holds (no quaternion conversion) | the gateway's DD-022 §3h wire freezes other names — the NOTE maps them |
| CE-11019: the re-anchor is published only when the rate CHANGES while running (Continuous, no pending barrier) | a paused / mid-barrier master's next resume already carries the rate; the same rate twice changes nothing | slaves must also learn a rate change made while paused (today they get it at the resume) |
| CE-11022: the six unused `TimeConfig` fields are NAMED, not deleted | they are public config surface; a removal is its own decision | the owner of `TimeConfig` asks for the removal |
| CE-11025: the replay handler drives the chain through `IReplayWorldLoad` (set by the one composition), not the chain claiming the replay op | the terrain name exists only after `PrepareReplay` opened the file; the handler is in Fdp.Toolkits and cannot see the chain | the planner ever sends the recording's terrain on the replay op's payload — then the chain could claim it directly |
| CE-11025: a replay's missing additions give the base terrain with a warning (a live load stays strict) | the same as the Replay Browser (CE-4420 O-8); a recording made elsewhere still plays | a replay must refuse rather than show a different terrain |
| CE-11024: the field is `Impact` = `ImpactKind` + 1, 0 = not a round's impact | a detonation's effect has no impact kind, and `Entry` is 0 in `ImpactKind` — a plain copy would make every detonation read "entered" | the gateway would rather read a separate "is an impact" flag |

**Named, not built (CE-11025):** a listener node (no recording of its own) keeps the resident terrain — the master's aggregated terrain name
could serve it; the node shows no `Loading` step during `LoadingReplay` (the replay handler is not an `ILoadProgressSource`); whether
`MapBuildLoadStep`'s `Request3D()` is a no-op for an unchanged terrain is not measured.

## 3. CE-11023 — measured first, then built

| the "to measure" point | answer |
|---|---|
| the file → exercise id lookup | the door opens by exercise id (`ScenarioDoor.OpenRecording(Guid, …)`, an Empty id is ignored); a recording's id is in its `.meta.json` (`RecordingMetadata.ExerciseId`); `RecordingName` carries an id when named by one |
| a replay open while a live run plays | a scenario load does NOT stop a run the user started: the director's asker answers `StopAndLoad` as a WAIT and the take fails at the cap with the reason (`EditorDirector.DirectorAsker`); the recording open takes the same answer |
| the load wall | `RunGate` already carries the load's progress text and the cluster's error reason (`RunGate.From`): the stall rule reads those |

## 4. Gates (rule 8 — verbatim commands; every red confirmed or none)

| gate | command | result |
|---|---|---|
| T-1 director suites (CE-11018) | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj --no-build --filter "FullyQualifiedName~Director"` | 85 passed / 1 skipped |
| film / capture / director pack | `dotnet test Hrot/Engine/Hrot.Presentation.Tests/Hrot.Presentation.Tests.csproj --no-build --filter "FullyQualifiedName~Director\|FullyQualifiedName~Film"` | 108 passed / 7 skipped (display) |
| the display rails | `xvfb-run -a dotnet test Hrot/Engine/Hrot.Presentation.Tests/Hrot.Presentation.Tests.csproj --no-build --filter "FullyQualifiedName~FilmFrameRail\|FullyQualifiedName~FilmTests"` | 17 / 17 |
| CE-11019 unit | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build --filter "FullyQualifiedName~MasterSyncControllerTests"` | 52 / 52 |
| CE-11019 row-8 integration (isolated) | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj --no-build --filter "FullyQualifiedName~SimTimeSyncIntegrationTests"` | 7 / 7 (TC-SYNC-7 new) |
| CE-11025 handler | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build --filter "FullyQualifiedName~ReferenceHandlerTests\|FullyQualifiedName~MasterSyncControllerTests"` | 79 / 79 |
| CE-11025 chain, terrain, composition, replay (T-1: the load-phase and replay suites) | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build --filter "FullyQualifiedName~LoadPhaseChainTests\|FullyQualifiedName~TerrainLoadStepTests\|FullyQualifiedName~EveryEcsHostComposes\|FullyQualifiedName~ReplayLoadClusterOpHandlerTests\|FullyQualifiedName~EcsRecordReplayControllerTests\|FullyQualifiedName~NodeBootstrapperReplayTests\|FullyQualifiedName~LiveFromReplayTests"` | 139 passed / 3 skipped |
| CE-11024 | `dotnet test Hrot/Engine/Hrot.Look.Tests/Hrot.Look.Tests.csproj --no-build` | 55 / 55 |
| CE-11023 director suites (after the change) | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj --no-build --filter "FullyQualifiedName~Director"` | 87 passed / 1 skipped (+2 new) |
| CE-11023 film / director / recording name (Presentation) | `dotnet test Hrot/Engine/Hrot.Presentation.Tests/Hrot.Presentation.Tests.csproj --no-build --filter "FullyQualifiedName~Director\|FullyQualifiedName~Film\|FullyQualifiedName~RecordingName"` | 108 passed / 7 skipped (display) |
| tracker | `python3 scripts/tracker-counts.py --check` | OK |

**Red-proofs (each a compiling mutation, the rail then red):** CE-11018 L1–L7 all red (the client ignored at Begin · a default camera · no card
silence · `frames: map` ignored · no fault check · no 2-D check · `Attach` with a plain map sink). CE-11019 / CE-11025 / CE-11024: §5.

**Not run:** the full `ClusterRunner.Integration.Tests` suite (DEBT-TIME-001, un-gateable; the time suite ran in isolation); no director
feature scenario exists in `scripts/scenario-regression.sh`'s set — ⭐ the one to add: `showcases/player-station` with a promoted take
rendered twice (map, then the gateway's reference client), the acceptance in the linked-client design §3.

## 5. Red-proofs, batch 2 — 7 / 7 red

| mutation (each compiles) | rail that went red |
|---|---|
| T1 the re-anchor gated on Stepping instead of Continuous | `MasterSyncControllerTests.CE11019_*` |
| R1 the handler asks for the world only when NO terrain is named | `ReferenceHandlerTests.CE11025_*` |
| R2 the knowledge base among the replay's world parts | `LoadPhaseChainTests.CE11025_*` |
| R3 a replay reads the staged scenario's overlay | `TerrainLoadStepTests.CE11025_*` |
| R4 the composition never hands the chain to the replay handler | `EveryEcsHostComposesTheLoadPhaseChainRails.CE11025_*` |
| R5 the per-frame drain never checks the replay's world | `LoadPhaseChainTests.CE11025_*` |
| E1 the impact kind not stamped | `EffectStateTests.CE11024_*` |

⚠ The first try of T1 and R1 used `if (false)`, which does not compile (warnings are errors). They were re-run with conditions that compile,
and only those runs count.

**CE-11023 — 3 / 3 red:** C1 the stall clock never resets ⇒ the advancing load fails · C2 `Opening` ignores which exercise is replayed ⇒ another recording reads as ready · C3 the runner ignores the cluster error ⇒ the error case fails as a stall.

## 6. Ids allocated

None — every row is ui's (rule 6). No new backend row was needed.
