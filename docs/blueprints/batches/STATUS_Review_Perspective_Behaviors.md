<!--STATUS state: LIVE · updated: 2026-11-08 · current-answer: the table -->
# STATUS — behaviors, Review perspective ([handoff](HANDOFF_Coordinator_To_Behaviors_Review_Perspective.md), design [DESIGN_Review_Perspective.md](../../UX/DESIGN_Review_Perspective.md))

Started at `cf2bbb727` (all lanes merged); marker pushed `70456551a`.

| item | state | sha | notes |
|---|---|---|---|
| R-1 (V1) `ReviewBundle` + `ReviewHostServices`; the Replay Browser composes it | DONE | (this commit) | Frame diff · Replay search · Recording info · JSON export from ONE bundle (`Hrot.Editor.AiShared/Review/`); the RB's four hand registrations deleted. V2's host-services members are optional and unread. The RB window-set golden was stale-red at HEAD and re-baselined first (design §4 AS BUILT — V1) |
| R-2 … R-6 | not started | — | |

## Gates
| gate | command | result |
|---|---|---|
| bundle rail + rules bundle | `Hrot.Editor.AiShared.Tests --no-build --filter Review\|Rules` | 46 pass / 0 fail |
| RB window-set + frame rails | `xvfb-run -a dotnet test Hrot.ReplayBrowser.Tests --no-build` | 79 pass / 2 fail / 8 skip — the 2 reds are pre-existing at base (below) |
| builds | `Hrot.ReplayBrowser.Tests`, `Hrot.Editor.AiShared.Tests`, `Hrot.Editor` (`--no-restore`) | 0 errors |
| red-proofs | `ReplaySearch` dropped from `RegisterInto` | `TheReviewBundleTests` red; RB `RegisterWindowsCore_*` 2 red; restored |

Pre-existing reds (identical at base without my change, measured by stash): `CE1302_TheEventTimeline_FollowsThePrimaryRecording_AndSeeksThroughTheManager` (lane count expected 9, actual 12) and `CE3330_Node_TheReplayBrowser_ComposesTheShippedCatalogue` (three objective lanes `objective.state` / `rules.fired` / `objective.score` not in the expected list). The two `RegisterWindowsCore_*` rails were also red at base (stale golden) and are now green.
