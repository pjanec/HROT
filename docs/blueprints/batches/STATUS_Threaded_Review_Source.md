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

### DONE

| item | tracker | commit | what |
|---|---|---|---|
| R-1 | CE-4495 | `105c18cef` | `ReviewReadModel` + `ReviewViewRequest` + `ReviewModelBuilder` (`FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/ReviewReadModel.cs`): ONE builder for every host; the same model instance (0 B) while its version key is unchanged |
| R-2 | CE-4496 | *(this commit)* | `IReviewCommands` / `IReviewView` / `IReviewChannel` + `ReviewRequestCollector` + `InlineReviewChannel` + `ManagerReviewHost` (`ReviewChannels.cs`). Re-pointed onto them: `FederationPanel`, `ReplayTimelinePanel`, `FederatedReplayCursor`, `ByNodeSection`, `ReplicaWatchLane`, the federated search (`FederatedSearchSource`, via `FileReview`), the editor's Source menu / face / toolbar combo / status section / Offsets window host (now over `ReviewUi`). `ReviewSource` implements `IReviewHost`; the Replay Browser's review is a `ManagerReviewHost` |

### GATES

| item | command (filter) | result |
|---|---|---|
| R-1 | `dotnet test Fdp.Toolkits.Tests --no-build --filter ReviewModelBuilderTests` | 4/4 |
| R-2 | same, + the collector and inline-channel rails | **6/6** |
| R-2 | `xvfb-run -a dotnet test Fdp.Presentation.Tests --no-build --filter "FullyQualifiedName~ReplayBrowser\|FullyQualifiedName~Timeline"` (the Replay Browser's Federation / timeline / window rails) | **141/141** |
| R-2 | `xvfb-run -a dotnet test Hrot.Editor.AiShared.Tests --no-build --filter FullyQualifiedName~Review` (the watch lane, file review, federated search) | **45/45** |
| R-2 | `xvfb-run -a dotnet test Hrot.Editor.Tests --no-build --filter FullyQualifiedName~Hrot.Editor.Tests.Review` (the review source, By node, the Source menu, + the new acceptance ④ rail `ReviewUiHoldsNoManagerTests` 2/2) | **42/42** after BUG-1 (41/42 before it) |
| R-2 | `xvfb-run -a dotnet test Hrot.ReplayBrowser.Tests --no-build` (the standalone Replay Browser's own suite) | **96 passed, 8 skipped, 0 failed** — the 8 skips are the suite's existing `SkippableFact`s |

### DECISIONS

| # | decision | why |
|---|---|---|
| D-1 | the model, builder and channels live in **Fdp.Toolkits** (`Fdp.Toolkit.ReplayBrowser.Federation`), beside the manager | the panels are in Fdp.Presentation and cannot see `ReviewSource` (Hrot.Editor); one home both hosts reach ⇒ ONE builder by construction |
| D-2 | `IReviewHost` (facts + commands) is what the builder reads; `ReviewSource` and `ManagerReviewHost` (the standalone Replay Browser) implement it | the design's `Build(source, …)` needs a seam both hosts satisfy; the Replay Browser has no `ReviewSource` |
| D-3 | panels **ask** for By-node / watch views through a `ReviewRequestCollector`; `EndFrame` makes a new request only when the asked set changed; a key unasked for 60 frames is dropped | §2 *"the request rides up only when it changes"*; 0 B steady. A view appears one model after it is first asked for (inline: next read after the frame's `EndFrame`) — the same lag mode 3 has by nature |
| D-4 | the editor's ONE `FederationPanel` moved off `ReviewSource` into a UI-side `ReviewUi` (with the `Offsets…` event); the panel reads the model, so it serves every group without being rebuilt | in mode 3 the source runs on the sim thread; a UI object and a UI event cannot live there. ⚠ consequence: the Offsets window is registered on its first `Offsets…` (not at group open) — it is listed in the Window menu from then on |
| D-5 | the Replay Browser HOST keeps the manager (merge, play clock, debug surface); only UI types moved | the design's INVENTORY: *"R-2 re-points only the UI readers and leaves those hosts on the live manager"* |

### BUGS FIXED

| # | found by | defect | fix |
|---|---|---|---|
| BUG-1 | the Source menu's 0 B steady-draw rail (`G7S5_TheSourceMenu_IsMockup3…`: 1 776 B per frame) | reading the model asked the UI's time facade (`IsPaused`) every frame — a UI dependency on the read path, and allocating behind a mock | the source's facts carry the ▶ state its last `Update` saw (`ReviewSource._playing`); a model read touches no facade. Re-ran: 2/2, 0 B |

### OPEN

none yet
