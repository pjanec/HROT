<!--STATUS state: LIVE · updated: 2026-11-08 · current-answer: the table -->
# NOTE — behaviors → ui, backend: the ONE branch where a recording opens (Review perspective V6)

Design: [DESIGN_Review_Perspective.md](../../UX/DESIGN_Review_Perspective.md) §3 D6, §4 AS BUILT — V6. Handoff COLLISION RULES row 2 asked for this note before the commit. FYI only — nothing is asked of you.

| | |
|---|---|
| **what changed** | ONE event on the editor's session, raised as an open goes out: `EditorScenarioSession.RecordingOpened` (`Hrot/Editor/Hrot.Editor.AiShared/Scenarios/EditorScenarioSession.cs:290` raise, `:298` declaration; forwarded by `EditorApplication.cs:55`). The editor's `ReviewSource` listens (`EditorSubsystem.cs` `reviewApp.RecordingOpened += …`): this node has a part of the exercise ⇒ nothing happens (today's AAR); a participant group with NO part of this node ⇒ the source fills the editor's world MERGED |
| **where it is NOT** | ⛔ `ScenarioDoor.OpenRecording` and `RunModeChoice` are **untouched** — every route (the door's, the run choice's raw verbs, HTTP's) already ends in `IRunScenarioControl.OpenRecording`, so the raw verb is the one place all converge. The replay intent still goes out first, unchanged: the cluster enters AAR as it does today |
| **ui — please do not undo** | the raised event after `PublishManaged(EnterReplay…)`. A recording opened by a path that skips `IRunScenarioControl.OpenRecording` would skip the branch (none exists today) |
| **backend — please do not undo / know** | (1) `PlaybackTickSystem.Listen` clears the world ONCE on entering listening and then leaves it alone — the source fills only after that (`ReplayState.IsActive && !IsRestoring`); if that changes (a clear per seek), the Merged world would be wiped by a cluster seek. (2) `EcsRecordReplayController` / `ReplayModule` / the recorder are untouched. (3) the merge reads `RecordingGroupInventory.Scan` — a change to where archived recordings land (CE-4330 ④) is picked up there |
| **ui — one gap, a smallest seam** | in Merged the toolbar's own ▶ (`MainToolbarTimeControlSection`, ui's) is still enabled and drives the cluster clock, which does nothing for the merged world. The AAR segment draws its own greyed ▶ with the reason; to grey THE ▶, `RunModeChoice.RunPlayDisabledReason` would take a host-supplied reason delegate. Not built — your file |
