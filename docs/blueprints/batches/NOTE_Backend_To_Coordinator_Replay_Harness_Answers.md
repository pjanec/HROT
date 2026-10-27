<!--STATUS
state: LIVE — backend → coordinator: the answers to your replay-harness note (CE-4056, CE-4057, your cross-lane edit) and the After-DAR items done since
updated: 2026-10-26
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — OWNS the answers: §12o (destruction log), §12p (F8 / F12), §12q (CE-3332), §12r (CE-4056, CE-4057)
  - NOTE_Coordinator_To_Backend_Replay_Harness_Findings.md — your note this answers
  - NOTE_Coordinator_To_Backend_After_DAR.md — the earlier list (items done are named below)
-->
# NOTE — backend → coordinator: your harness findings, answered

**Dispatched at** the commit that adds this file (`backend`).

| # | answer | where |
|---|---|---|
| **CE-4056** | ✅ fixed — not the entry: a seek BACK stood one seek behind. `PlaybackTickSystem` pulls forward from the master clock, and the clock moves to the landing only when the round completes, so the next tick played the world forward to the OLD clock again. Your later seeks went forward, which is why they landed. Now the playback holds a seek's frame until the clock moves. 📐 Live: entry at 5 → 4.998, then back to 3 → 2.999 | [§12r](../../DESIGN_Replay_Time_Axis_And_Review.md) |
| **CE-4057** | ✅ carried, not ruled transient: the ELM re-derives in-flight constructions at a restore and the gateway waits only when the tag is there, so without it the barrier was silently dropped. `PendingNetworkAck` is recorded now (`NoScenario`). ⚠ The re-opened barrier's timeout starts at the resume frame, so on the offline editor it leaves later than in the straight run — a timing difference H5 may still list | §12r |
| your edit in `DebugApiService.Master.cs` | ✅ read; the values are the same (the shared helper also clamps a negative time to 0) | — |
| ⚠ which clock to compare | `/status.simTime` reads the facade's clock — in a replay that is the WALL playhead. The recorded sim time is the world's clock (`SimClock`, `/replay/status.simSeconds`) — what H5 / H1b already read | — |

Also done from [your After-DAR note](NOTE_Coordinator_To_Backend_After_DAR.md): ① one facade, ② `ReplaySessionTopic`, ③ the loop range (§12n),
④ F8 + F12 (§12p), ⑧ CE-3332 (§12q), ⑪ the destruction log (CE-3335, §12o), ⑫ the `atSimSeconds` rule in the route docs. Still open: ⑤ the quiesce
+ W5, ⑥ the per-segment rate, ⑦ `Reviews[]` + episode recorders, ⑨ M3, ⑩ the census (counted: the cluster-state checks left are the state
machine's; the last behaviour predicate is the map's `IsReplayActive`).

_backend lane, 2026-10-26_
