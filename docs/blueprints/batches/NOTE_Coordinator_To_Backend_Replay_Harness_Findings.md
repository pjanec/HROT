<!--STATUS
state: LIVE — coordinator → backend: what the replay harness (H1b, H5) found in the product's replay and review, and one cross-lane edit in your debug service
updated: 2026-10-26
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Regression.md — §10-3a (H5) and §10-4 (H1b): the measurements
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — yours: the review and the sim axis these measure
  - NOTE_Coordinator_To_Backend_After_DAR.md — the earlier list (still open items there)
-->
# NOTE — coordinator → backend: what the replay harness found (CE-4056, CE-4057)

**Dispatched at** the commit that adds this file (`coordinator`). FYI: nothing here changes a batch in flight.

⭐ The good news first: on `ua-civ-melee`, **the product's REVIEW and its cluster REPLAY both show the live run** — every recorded component
equal at every second they stood at (H5: 1 … 10 s; H1b: 1 … 40 s), each sought by `atSimSeconds` through the master.

| # | found | where | ⏭ asked of you |
|---|---|---|---|
| **CE-4056** | ⛔ an ENTRY with a sim target never stands there: `TransitionState` to `OperatingReview` OR `OperatingReplay` with `TargetSimSeconds: 0` lands the master on the right recorded frame (frame 4, sim 0), yet the editor's `SimClock` reads 4.92 / 4.6 / 4.0 s (review, three runs) and 26.6 s (replay) for 30 s. Every later `SeekReplay` with a sim target stands at its target within a frame | [tracker](../Blueprint_Issues_Tracker.md), Scenario Regression §10-3a, §10-4 | what sets the clock after the entry's seek round, versus after a `SeekReplay` |
| **CE-4057** | ⛔ the review's RETURN drops `PendingNetworkAck`: present on both units at L, gone after `ExitReview`, while the straight run keeps it (11 s). Not in the recording's manifest ⇒ not in the full checkpoint | [tracker](../Blueprint_Issues_Tracker.md), §10-3a | carry it in the review's checkpoint (R-3063: the return is exact), or rule it transient — then the harness lists it apart |
| — | ⚠ **a cross-lane edit in your file:** `DebugApiService.Master.cs` — `EnterReplay`, `SeekReplayTo` and `EnterReview` now build their intents through `ReplaySessionIntents` (I added `EnterReplay(exerciseId, wall?, sim?)` there); they had inline copies beside it, though §12m says the routes build them there. Same field values; the unused private `SecondsToTicks` went with them | `b97d9bcec` | read the diff; say if a value differs from what you meant |
| — | ⓘ the editor puts nothing on the wire (`OfflineNetworkFactory` — no DDS), so the zombie check at scenario scale (§12e) moves to the cluster harness (H4) | Scenario Regression §10-2 ④ | nothing |

_coordinator lane, 2026-10-26_
