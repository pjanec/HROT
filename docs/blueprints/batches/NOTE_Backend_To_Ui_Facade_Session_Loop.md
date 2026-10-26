<!--STATUS
state: LIVE — backend → ui: what your timeline menu and toolbar can call now (rows 21, 32, 45, the loop), and one stale check of yours
updated: 2026-10-26
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — OWNS it: §12m (one facade, the session layer), §12n (the loop)
  - NOTE_Ui_To_Coordinator_Review_Timeline_Built.md — your report: what waited on backend
  - NOTE_Coordinator_To_Backend_After_DAR.md — the coordinator's order (items 1–3 answered here)
-->
# NOTE — backend → ui: the facade, the session layer and the loop are built

**Dispatched at** the commit that adds this file (`backend`).

## 1. What you can call now — ONE facade on every host

🔒 User: *"One facade."* `ClusterTimeTransportAdapter` is the time facade on EVERY host, the editor included; `EditorTimeTransportFacade`
is deleted (design §12m). Your menu and toolbar call only `ITimeTransportFacade`:

| member | meaning |
|---|---|
| `Mode` | Idle · Edit · Preview · Live · Review · Replay |
| `Axis` / `SetAxis(axis)` | the session's axis (R-3058) |
| `EnterReview(t?)` | "Review from here…" — `t` on the active axis, null = the live point |
| `ExitReview()` | back to live; ⭐ `Stop()` in a review does the same (R-3060) |
| `LivePoint` | the review's live point on the active axis (null outside a review) — "Seek to the live point" = `Seek(LivePoint)` |
| `Seek(t)` | on the active axis |
| `SetLoop(from, to)` / `ClearLoop()` / `Loop` | ⭐ "Loop range" — the MASTER's loop (§12n): every host draws the same bracket. ⚠ building as this note is written; it lands in the next backend commit |

⚠ Play from EDIT is now the cluster's dry run on every host (the editor's too); Stop leaves a dry run back to edit.

## 2. The session layer for the ruler

`ReplaySessionTopic` reaches every host: `ClusterUiCache.ReplaySession` (and the facade's `Session` on a node) — the zero, the length,
the axis, the cluster's `ReplayTimeMap`, the checkpoint marks, the review's id and live point, the loop. It is the same on every host and
inactive outside a replay; match it to the cluster state by `SessionId` / `ExerciseId` (§9g).

## 3. One stale check of yours

`Hrot.ReplayBrowser.Tests` `CE3330_Node_TheReplayBrowserAndTheEditor_ComposeTheShippedCatalogue` fails: it requires the TEXT
`ShippedTimelineLanes.Catalog()` in `EditorSubsystem.cs`, which your own `629adc0db` ("the editor's live timeline is its node context's —
one owner") moved into `HrotNodeContext`. Already absent at `coordinator@7d576b229`; not touched by backend. ⭐ Lean: point the check at
`HrotNodeContext.cs` (or assert the editor's catalogue's lane ids, as its first half already does).

_backend lane, 2026-10-26_
