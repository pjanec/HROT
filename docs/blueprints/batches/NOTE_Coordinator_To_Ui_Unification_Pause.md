<!--STATUS
state: LIVE — coordinator → ui: R-3066 (user) pauses development until the run-modes duplicates are unified; ui's share of it
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - NOTE_Backend_To_Coordinator_Unification_Pause.md — backend's note (on `backend`, `f31fb2712`): what is paused, the order
  - ../../DESIGN_Cluster_View.md — CE-1436, V1–V5 approved (R-3066)
  - ../Architect_Question_108_Run_Mode_Picker.md — §2.6 (CE-4096's ClusterSlave follow-fields), §2.8a (CE-4097's unsaved query)
-->
# NOTE — coordinator → ui: development paused for unification (R-3066) — your share

🔒 **User, `2026-10-29`** (relayed by backend): *"We need to pause any development until we enforce the unification and sharing."* — and
*"Approved both"* (the `ClusterView` design V1–V5 and backend's order). ⇒ ⛔ **CE-4083 (remove the status-bar time control) waits**; no new
feature work until the items below are done.

## ⭐ ui's share — one Sonnet agent at a time (CLAUDE.md, user `2026-10-29`)

| # | what | why |
|---|---|---|
| 1 | ⭐⭐ **Audit your own run-modes work for duplicates**, the same question the user put to backend — *"Where else in this effort did we divert from the general sharing and unification directive?"* — over CE-4081/4084/4085/4096/4097/4098 and CE-4054. Graph + grep; a row per finding in your area (as backend's CE-3340), fixed in the same pass when small | backend found five in its own; ui has written as much code |
| 2 | `ClusterUnsavedChangesQuery.Deadline` — a fixed 15 s (`Hrot/Engine/Hrot.Presentation/RunMode/RunModeTypes.cs:126`) mirroring the HTTP gate's 15 s, which backend's CE-3340 ⑤ removes under R-4001 (the heartbeat is the only time limit). ⭐ Follow the SAME rule as backend's fix — agree one shared answer with backend, ⛔ not two timeouts | one timeout rule, not two |
| 3 | ⏳ when backend lands `ClusterView` (CE-1436): your CE-4096 follow-fields on `ClusterSlave` (`FollowedClusterState` / `MasterGone`) and `ClusterRestReader`'s fold move onto it — backend does the move (V3); you review that the start gate and `RunModeChoice` read the one view and that the per-host rails still pass | V3: one owner of the cluster's facts |

Then the run-modes tasks resume. Report the audit's rows (ids from your block) in your next push.

_coordinator lane, 2026-10-29_
