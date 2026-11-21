<!--STATUS
state: LIVE
updated: 2026-11-13
current-answer: the REPORT block at the top
stale-below: nothing
related-designs:
  - ../../DESIGN_Threaded_Review_Source.md — the design this batch builds (R-4220)
  - HANDOFF_Coordinator_To_Backend_Threaded_Review_Source.md — the dispatch (scope frozen at bdefd8917)
-->

# STATUS — threaded review source (CE-4495 … CE-4500), branch `exp/threaded-review`

## REPORT

⭐ **All six items done (R-1 … R-6); one check OPEN (OPEN-1: a live cluster session — no debug route drives the review's actions).** Three bugs found by the rails and fixed (BUG-1…3, all steady-frame allocation or a UI read on the model path).

### DONE

| item | tracker | commit | what |
|---|---|---|---|
| R-1 | CE-4495 | `105c18cef` | `ReviewReadModel` + `ReviewViewRequest` + `ReviewModelBuilder` (`FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/ReviewReadModel.cs`): ONE builder for every host; the same model instance (0 B) while its version key is unchanged |
| R-2 | CE-4496 | `8336cccc9` | `IReviewCommands` / `IReviewView` / `IReviewChannel` + `ReviewRequestCollector` + `InlineReviewChannel` + `ManagerReviewHost` (`ReviewChannels.cs`). Re-pointed onto them: `FederationPanel`, `ReplayTimelinePanel`, `FederatedReplayCursor`, `ByNodeSection`, `ReplicaWatchLane`, the federated search (`FederatedSearchSource`, via `FileReview`), the editor's Source menu / face / toolbar combo / status section / Offsets window host (now over `ReviewUi`). `ReviewSource` implements `IReviewHost`; the Replay Browser's review is a `ManagerReviewHost` |

| R-3 | CE-4497 | `78426b2c3` | the review source is SIMULATION-side: it fills the node's world (`ReviewWorld` = S in mode 3, the one world with the switch off), its boundary is `NewScenario(S)`, its clock is the sim side's transport (`_simTimeTransport`), and it is stepped from ONE call site — the end of `TickNodeFrame`, after the kernel tick, in both kernel modes. The UI frame overlay that stepped it is removed. Mode 3: the UI hands the selected network id over as an int each frame; a re-select after a refill is posted to the UI with the next publish. `OnRecordingOpened` stays subscribed on `_editorApp.RecordingOpened` — in mode 3 that editor logic runs on the sim thread (D-7), so the call is already sim-side |
| R-4 | CE-4498 | `a51ce64f1` | `ReviewModelPublisher` (sim side: the request the UI last sent, the model last published; `PublishIfChanged` returns only a NEW model) + `ThreadedReviewChannel` (a command is a sim job; `Apply` takes the published model on the UI thread; the request rides up only when it changed). Editor, mode 3: composed instead of the inline channel; `SimStep` publishes after the node frame and the staging carries the model to the UI (cached delegate, 0 B unchanged). Off the source onto the model, in BOTH modes (one implementation): the AAR transport (`ReviewSourceTransport` now over the channel; owned by `ReviewUi`), the transport selector, the AAR segment's *in AAR* / *merged*, the file review's files / frame / seek, By node's names, Details' *"local to &lt;node&gt;"* (the model carries a snapshot of the merger's local origins: `MergeTarget.LocalVersion` + `CopyLocalOrigins`), the Recording-info timeline (bound once). The model also carries the primary's per-frame sim seconds (the transport's tooltip). `IReviewHost` now extends `IReviewCommands` |
| R-5 | CE-4499 | `7acfaab14` | mode 3 end to end, in-process on the REAL machinery (`SimJobQueue` carrying the commands, `StagingWorlds` publishing S + carrying the model, A pulled on the UI thread, the source on a dedicated sim thread): every acceptance action checked on A and on the model — Merged · step · scrub · ▶ at 2× · part ↔ Merged · an offset · a locals toggle · By node · a watch item · a search hit's seek; ⭐ A holds the merged entities after 10 pulls; 0 B per steady paused frame on BOTH threads; strict guard silent. And the REAL editor in mode 3 (`HROT_THREADED_KERNEL=3` in the test process) composes the threaded channel and its `SimStep` steps the source |
| R-6 | CE-4500 | *(this commit)* | [DESIGN_Threaded_Review_Source](../../DESIGN_Threaded_Review_Source.md): `build-state: BUILT`, §1.1 / §1.2 / §1.3 redrawn to the as-built, §4 *what the build changed* (six rows), the pre-build class list moved to HISTORY; [Threaded Presentation §14.8](../../DESIGN_Threaded_Simulation_Presentation.md): the review source row is now as-built (the OPEN row keeps only the film director); [Review Perspective](../../UX/DESIGN_Review_Perspective.md) V6: a note that every review surface reads the model through a channel |

### GATES

| item | command (filter) | result |
|---|---|---|
| R-1 | `dotnet test Fdp.Toolkits.Tests --no-build --filter ReviewModelBuilderTests` | 4/4 |
| R-2 | same, + the collector and inline-channel rails | **6/6** |
| R-2 | `xvfb-run -a dotnet test Fdp.Presentation.Tests --no-build --filter "FullyQualifiedName~ReplayBrowser\|FullyQualifiedName~Timeline"` (the Replay Browser's Federation / timeline / window rails) | **141/141** |
| R-2 | `xvfb-run -a dotnet test Hrot.Editor.AiShared.Tests --no-build --filter FullyQualifiedName~Review` (the watch lane, file review, federated search) | **45/45** |
| R-2 | `xvfb-run -a dotnet test Hrot.Editor.Tests --no-build --filter FullyQualifiedName~Hrot.Editor.Tests.Review` (the review source, By node, the Source menu, + the new acceptance ④ rail `ReviewUiHoldsNoManagerTests` 2/2) | **42/42** after BUG-1 (41/42 before it) |
| R-2 | `xvfb-run -a dotnet test Hrot.ReplayBrowser.Tests --no-build` (the standalone Replay Browser's own suite) | **96 passed, 8 skipped, 0 failed** — the 8 skips are the suite's existing `SkippableFact`s |
| R-4 | `xvfb-run -a dotnet test Hrot.Editor.Tests --no-build --filter FullyQualifiedName~Hrot.Editor.Tests.Review` (+ `R4_TheThreadedChannel_RunsCommandsOnTheSimThread_AndTheModelReachesTheUiOnlyAfterThePull`: commands run on a dedicated sim thread, the model reaches the UI only after the pull, a By-node ask rides up once, a steady paused step publishes nothing and allocates 0 B) | **44 passed, 1 skipped** (the guard rail needs the env var — next row) |
| R-4 | `HROT_WORLD_GUARD=1 dotnet test Hrot.Editor.Tests --no-build --filter FullyQualifiedName~R4_` (`R4_TheStrictWorldGuard_StaysSilent_WhileTheUiDrivesTheThreadedReview`: strict guard armed on S / A / the sim thread; the UI steps, toggles locals, asks By node, switches part ↔ merged and reads the transport / files / status / Federation dump) | **2/2 — 0 new guard hits** |
| R-4 | the toolkit change's dependants, rebuilt: `Fdp.Toolkits.Tests` (Federation + review, 79/79) · `Fdp.Presentation.Tests` ReplayBrowser + Timeline (141/141) · `Hrot.Editor.AiShared.Tests` Review (45/45) · `Hrot.ReplayBrowser.Tests` (96 passed, 8 skipped) | all green |
| R-5 | `dotnet test Hrot.Editor.Tests --no-build --filter FullyQualifiedName~R5_` (`R5_Mode3_TheReviewOverTheRealStagingAndJobQueue_EveryActionReachesA_AndTheMergeSurvivesThePulls`) | **1/1** (red before BUG-3: UI frame 32 B) |
| R-5 | `HROT_WORLD_GUARD=1 dotnet test Hrot.Editor.Tests --no-build --filter "FullyQualifiedName~R5_\|FullyQualifiedName~R4_"` (the strict guard armed on S / A / the sim thread through every action) | **3/3 — 0 new guard hits** |
| R-5 | `HROT_THREADED_KERNEL=3 dotnet test Hrot.Editor.Tests --no-build --filter "FullyQualifiedName~R4_InMode3\|FullyQualifiedName~R3_TheReviewSource"` (the real `EditorSubsystem`, headless, mode 3) | **2/2** |
| R-5 | switch OFF: `xvfb-run -a dotnet test Hrot.Editor.Tests --no-build --filter FullyQualifiedName~Hrot.Editor.Tests.Review` · `Fdp.Toolkits.Tests` review rails | **45 passed, 2 skipped** (the guard / mode-3 rails, each run green with its env var above) · **6/6** |
| R-6 | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs docs/DESIGN_Threaded_Review_Source.md` | 3/3 blocks parse |
| R-6 | `python3 scripts/tracker-counts.py --check` | OK (3058 rows) |
| R-6 | `python3 scripts/design-digest.py --check` | ⚠ exit 1 — **pre-existing**: identical output with my changes stashed (5 other design docs have no INVENTORY block; none is touched here) |
| R-3 | `xvfb-run -a dotnet test Hrot.Editor.Tests --no-build --filter FullyQualifiedName~Hrot.Editor.Tests.Review` (switch OFF: the review's own rails, behaviour unchanged) + the new `R3_TheReviewSource_IsSteppedByTheNodeFrame_OncePerFrame_NotByAFrameOverlay` | **43/43**. The new rail is red by construction if the call leaves `TickNodeFrame` (no other code steps the source in a headless frame); not mutation-run |

### DECISIONS

| # | decision | why |
|---|---|---|
| D-1 | the model, builder and channels live in **Fdp.Toolkits** (`Fdp.Toolkit.ReplayBrowser.Federation`), beside the manager | the panels are in Fdp.Presentation and cannot see `ReviewSource` (Hrot.Editor); one home both hosts reach ⇒ ONE builder by construction |
| D-2 | `IReviewHost` (facts + commands) is what the builder reads; `ReviewSource` and `ManagerReviewHost` (the standalone Replay Browser) implement it | the design's `Build(source, …)` needs a seam both hosts satisfy; the Replay Browser has no `ReviewSource` |
| D-3 | panels **ask** for By-node / watch views through a `ReviewRequestCollector`; `EndFrame` makes a new request only when the asked set changed; a key unasked for 60 frames is dropped | §2 *"the request rides up only when it changes"*; 0 B steady. A view appears one model after it is first asked for (inline: next read after the frame's `EndFrame`) — the same lag mode 3 has by nature |
| D-4 | the editor's ONE `FederationPanel` moved off `ReviewSource` into a UI-side `ReviewUi` (with the `Offsets…` event); the panel reads the model, so it serves every group without being rebuilt | in mode 3 the source runs on the sim thread; a UI object and a UI event cannot live there. ⚠ consequence: the Offsets window is registered on its first `Offsets…` (not at group open) — it is listed in the Window menu from then on |
| D-6 | `ReviewUi` (UI side) owns the transport, the file list, the *local to* note and the Federation panel; `ReviewSource` keeps only sim-side state | the D-9 split: everything the UI draws comes from the model; the source is touched only by jobs |
| D-7 | the transport's frame clock comes from the model's primary frame table; in a part the part IS the primary (`SelectPart` sets it) | one table in the model, no second per-part array |
| D-5 | the Replay Browser HOST keeps the manager (merge, play clock, debug surface); only UI types moved | the design's INVENTORY: *"R-2 re-points only the UI readers and leaves those hosts on the live manager"* |

### BUGS FIXED

| # | found by | defect | fix |
|---|---|---|---|
| BUG-3 | the R-5 rail's 0 B UI-frame measurement (split by piece: `EndFrame` 32 B per frame) | `ThreadedReviewChannel.EndFrame`'s lambda captured `request`, so the compiler allocated the closure at the method's ENTRY — every UI frame in mode 3, posted or not | the post moved into its own method (`SendRequest`); 0 B |
| BUG-2 | the R-4 steady-step 0 B rail (632 B per step) | `ReviewSource.Update` read the time facade twice per step (once for the facts, once inside `AdvanceMerged` / `AdvancePart`) | read once per step and passed down. The rail measures with no facade (paused): a mock facade allocates per call (Moq), which is not the editor's |
| BUG-1 | the Source menu's 0 B steady-draw rail (`G7S5_TheSourceMenu_IsMockup3…`: 1 776 B per frame) | reading the model asked the UI's time facade (`IsPaused`) every frame — a UI dependency on the read path, and allocating behind a mock | the source's facts carry the ▶ state its last `Update` saw (`ReviewSource._playing`); a model read touches no facade. Re-ran: 2/2, 0 B |

### OPEN

| # | what | why not done | what would settle it |
|---|---|---|---|
| OPEN-1 | a LIVE mode-3 editor session on a real cluster recording (the cluster in AAR, the node a listener) driven through every action | the editor has no debug route for the review's Source switch / offsets / locals / By node (only `POST /replay` opens), so a live run could open the group but not drive the actions; approach 1 (the in-process rail on the real staging + job queue) and approach 2 (the real `EditorSubsystem` composed in mode 3) cover the mechanism and the composition | `GET` / `POST /review/source` routes over `ReviewUi` (the channel's commands and the model as JSON), then one `--mode all` run with `HROT_THREADED_KERNEL=3 HROT_WORLD_GUARD=1` |
| OPEN-2 | the merge into S relies on the kernel ticking S every node frame (`ModuleHostKernel.cs:596`, unconditional) so each merge carries a new chunk version the staging copies | measured in the rail (without the tick, a merge after the first never reached A); true of the kernel today | keep: a rail in the staging's own suite would pin it if the kernel ever skipped `Tick()` while paused |
