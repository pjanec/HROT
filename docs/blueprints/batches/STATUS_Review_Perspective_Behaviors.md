<!--STATUS state: LIVE · updated: 2026-11-08 · current-answer: the table -->
# STATUS — behaviors, Review perspective ([handoff](HANDOFF_Coordinator_To_Behaviors_Review_Perspective.md), design [DESIGN_Review_Perspective.md](../../UX/DESIGN_Review_Perspective.md))

Started at `cf2bbb727` (all lanes merged); marker pushed `70456551a`.

| item | state | sha | notes |
|---|---|---|---|
| R-1 (V1) `ReviewBundle` + `ReviewHostServices`; the Replay Browser composes it | DONE | (this commit) | Frame diff · Replay search · Recording info · JSON export from ONE bundle (`Hrot.Editor.AiShared/Review/`); the RB's four hand registrations deleted. V2's host-services members are optional and unread. The RB window-set golden was stale-red at HEAD and re-baselined first (design §4 AS BUILT — V1) |
| R-2 (V2) the editor composes `ReviewBundle` over its own-part AAR | DONE | (this commit) | `OwnPartReplay` = the ONE seek/reader/file (extracted from `DebugApiService`, which now delegates); `FileReview` builds the diff + search panels from D3's members; a hit seeks + selects; 0 B (diff panel's per-frame allocations fixed); windows closed until opened, in every layout; outside AAR they say so. Recording info / JSON export say *no recording* until V6 (design §4 AS BUILT — V2) |
| R-3 (V3) the ONE AAR transport: `IReplayTransport` under the RB's drawer; the editor's AAR segment | DONE | (this commit) | `ReplayTransportDrawer` (moved out of `ReplayTimelinePanel`, calls only the seam) with two profiles — the RB `All` (look unchanged), the editor `AarSegment` (history · step back with scrub · seek bar) as its OWN toolbar entry `AarTransport`, only in AAR, over `OwnPartReplayTransport` (step back = `OwnPartReplay.SeekToFrame`); ui's time control untouched; 0 B; a jump through the seam is a history waypoint (the RB's bar / rewind now too). Design §4 AS BUILT — V3 |
| R-4 … R-6 | not started | — | |

## Gates
| gate | command | result |
|---|---|---|
| bundle rail + rules bundle | `Hrot.Editor.AiShared.Tests --no-build --filter Review\|Rules` | 46 pass / 0 fail |
| RB window-set + frame rails | `xvfb-run -a dotnet test Hrot.ReplayBrowser.Tests --no-build` | 79 pass / 2 fail / 8 skip — the 2 reds are pre-existing at base (below) |
| builds | `Hrot.ReplayBrowser.Tests`, `Hrot.Editor.AiShared.Tests`, `Hrot.Editor` (`--no-restore`) | 0 errors |
| red-proofs | `ReplaySearch` dropped from `RegisterInto` | `TheReviewBundleTests` red; RB `RegisterWindowsCore_*` 2 red; restored |

**V3 gates:** `Hrot.ReplayBrowser.Tests` whole under `xvfb-run -a` 81 pass / 0 fail / 8 skip (display-only skips; `RBUX1_*` and `CE4261_*` ran) · `Fdp.Presentation.Tests` ReplayBrowser classes 94 / 0 · `Hrot.Editor.AiShared.Tests` Review 32 / 0 · `Hrot.Editor.Tests` Review 35 / 0 (5 new) · builds `Hrot.Editor`, `Hrot.ReplayBrowser` 0 errors. Red-proofs: segment profile `All` ⇒ 2 red; `Step` not seeking ⇒ 1 red.

Pre-existing reds (identical at base without my change, measured by stash): `CE1302_TheEventTimeline_FollowsThePrimaryRecording_AndSeeksThroughTheManager` (lane count expected 9, actual 12) and `CE3330_Node_TheReplayBrowser_ComposesTheShippedCatalogue` (three objective lanes `objective.state` / `rules.fired` / `objective.score` not in the expected list). The two `RegisterWindowsCore_*` rails were also red at base (stale golden) and are now green.
