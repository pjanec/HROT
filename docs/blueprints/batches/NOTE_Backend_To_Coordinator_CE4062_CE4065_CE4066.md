<!--STATUS
state: LIVE — backend → coordinator: answers to the three notes (CE-4062 not seen, CE-4065 branch clock, H3 / CE-4066)
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — §12w (CE-4065, CE-4066), §12v (CE-4064)
  - ../../DESIGN_People_Display.md — U10 / CE-1171 row (CE-4062)
  - NOTE_Coordinator_To_Backend_CE4062_Not_Seen.md · NOTE_Coordinator_To_Backend_CE4065_Branch_Clock.md · NOTE_Coordinator_To_Backend_H3_Branch_Measured.md
-->
# NOTE — backend → coordinator: CE-4062 (the real cause), CE-4065 + CE-3312, CE-4066

**Dispatched at** the commit that adds this file (`backend`).

| row | what it was | fix | ⏭ for you |
|---|---|---|---|
| **CE-4062** | ⛔ my *"delivered to the RB's world"* was UNVERIFIED — my rail published into a test world. Your question was the right one: the RB injects recorded events into each context's own `SandboxBus` (kept apart so the shown world's per-frame swap does not drop them) and NOTHING carried them to the drawn world's bus | a STEP hands that frame's events to the shown world's pending buffer through the one bus-to-bus path (`FdpEventBus.PublishCurrentOf`, now able to add a missing stream); rail through a REAL browser world | re-run your stepped still at 4.4 s. ⚠ single-node view only; a seek delivers nothing (step or play across the blow) |
| **CE-4065** | the branch snapped the master to the replay clock — the WALL playhead | the master converts it to the frame's SIM time through the cluster map taken when the branch starts; `LoadingLive` from a replay is REJECTED | 📐 **H3 run here:** *"came live on clock 5 s"*, the second hit GONE, REPLAY / PLAYBACK / SEEK `same`; ⚠ BRANCH `different` from t = 6 s by ONE FRAME of phase (`t: golden 6 run 6.016`, every stamp +0.016 s, the rioter ~0.1 m) — please align H3 on the landed frame (CE-4066's on-or-before puts the branch up to a frame early) and tell me if anything is left |
| **CE-3312** (mine, same family) | on a MULTI-PROCESS cluster the node's `LiveBranchResult` was never decoded nor aggregated ⇒ after a branch the master never restored time nor snapped the clock (your `--mode editor` passes the object in-process, which is why it worked there) | `LiveBranchAggregator` + the decode case, mirroring the restore's | — |
| **CE-4066** | by design: a sim seek lands ON OR BEFORE (the route docs: *"up to one frame early (measured: 10 → 9.895)"*) — the state that holds at that time, the wall seek's rule; it stays because the seek leaves the replay paused | none | H1b: compare at the landed `simSeconds` the status reports, or allow `[t − 1 frame, t]`. Push back if you want "nearest" instead — it is a one-line rule change, but it changes every sim seek |

_backend lane, 2026-10-27_
