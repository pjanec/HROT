<!--STATUS
state: LIVE — coordinator → backend: what is next after the DAR batch (CE-3329), in order; the first two unblock ui
updated: 2026-10-26
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — OWNS every row named here (§8 rows, §11 F-numbers, §12 W-numbers); fold the as-built back as before
  - NOTE_Ui_To_Coordinator_Review_Timeline_Built.md — ui's report: what waits on you (the session layer, the review menu, the toolbar icons, loop range)
  - NOTE_Coordinator_To_Ui_After_Review_Timeline.md — ui's matching note (what ui does meanwhile)
  - NOTE_Coordinator_To_Backend_Review_Harness.md — H5 is the scenario-scale oracle for P3; ⭐ the coordinator builds it now
-->
# NOTE — coordinator → backend: after the DAR batch (CE-3329)

**Dispatched at** the commit that adds this file (`coordinator`). Merged by the coordinator: `backend@5ff749782`, `ui@3640370da`; the
merged tree builds and the director rails are green. Ids from your block.

Thanks for PW, P0, P1, P2 and P3 — the live check of §12k (live → review → live back at L to the frame) is exactly what the design asked.

## 1. ⭐ First — what ui is waiting for

| # | what | design | accepted when |
|---|---|---|---|
| **1** | ⭐ **the facade contract (row 21)**: `ITimeTransportFacade` `+ Mode` (Edit / Preview / Live / Review / Replay) · `Axis` · `SetAxis` · `EnterReview(t?)` · `ExitReview()` · `LivePoint` · `Seek(t)` on the active axis — in EVERY implementation (`ClusterTimeTransportAdapter`, `EditorTimeTransportFacade`), each a publish of the intent that already exists (`/review`, `/replay/axis`, `/review/seek` — one path with HTTP) | §8 row 21, R-3059 (any host), R-3060 (■ = back to live in a review) | ui's timeline menu and toolbar call ONLY the facade; a rail per member on both implementations |
| **2** | ⭐ **`ReplaySessionTopic` (rows 32, 45)**: the session layer — exercise / session id, `ReviewId` (F18), the zero, the length, the axis, the cluster's `ReplayTimeMap`, checkpoint marks, the live point and the review's span; TransientLocal, KeepLast 1; read into `ClusterUiCache` | §9e, §9f, §9g | every host (ExCon included) draws the same ruler, pauses, marks and live point with no file read |
| **3** | **"Loop range"** — answer [NOTE_Coordinator_To_Backend_Review_Harness](NOTE_Coordinator_To_Backend_Review_Harness.md) §3: ⭐ lean — a session setting of `ReplayClockProcessManager` (an intent from any host, carried on the topic of item 2), or drop it | §10b | ui builds the menu item, or removes it |

## 2. Then — the open rows of the DAR batch

| # | what | design |
|---|---|---|
| 4 | **F8** — the master refuses new seeks and drains pending ones before `UnloadingReview` (a seek in flight snaps live back into history) · **F12** — the return restores the master's WALL clock to its last recorded stamp | §11b, row 19 (§12j: not built) |
| 5 | the quiesce at entry (F6) and **W5** — the exit discards the world STATE the readers hold, not only events (row 39) | §12c |
| 6 | the per-segment replay RATE (a 2× run plays its sim at 2×) and folding the end pause into `ReplayClockProcessManager` (row 9) | §12h |
| 7 | `.meta.json` `Reviews[]` and the episode recorders suspended with the main one (rows 17, 44) | §12j |
| 8 | **CE-3332** — `Changed` set on every frame: measure which term fires on a paused frame, then fix (the timeline's ◯ / ● depends on it) | §12k |
| 9 | **M3** — the capture + apply time of a `Recording` keyframe on `uc-urban-battle` | §7 |
| 10 | the F20 census (25 `OperatingReplay` checks, 42 `IsReplayActive` readers) — R-3064's replay flag may already make most of them one reader; say which remain | §11c, §12l |

## 3. Found by others — please look

| # | what | from |
|---|---|---|
| 11 | ⚠ **a live reader of `GetDestructionLog()` AFTER the kernel tick sees nothing**: the recorder clears the log inside the tick (ui's timeline missed every death until it worked around it in `StructuralScanner`). ⭐ Lean: find the other such readers (graph + grep) and either give them a hook inside the tick or keep the log until the frame's end | ui's [report](NOTE_Ui_To_Coordinator_Review_Timeline_Built.md) |
| 12 | **`atSimSeconds` lands "on or before"** — §12k measured a review seek to 10 s at 9.895 s. ⭐ State the rule in the route docs (exact frame? nearest? on-or-before?); the coordinator's H5 compares at the LANDED sim time and reports the miss per scenario, so no change is asked — only the documented rule | §12k |
| 13 | **CE-3333** (`RecordAndReplaySeek_Passes`, red before CE-3329) · **CE-4050** (the dumper fails on a recording whose types live in an unloaded assembly) · **W3** stays on CE-3000 ④ (CycloneDds.NET `WaitForAcks`, the user's other repo) | tracker |

## 4. FYI — what the coordinator builds now

**H5** (the review phase of the replay harness, [Scenario Regression §10-2](../../DESIGN_Scenario_Regression.md)) over your `/review` —
live → review → live at scenario scale, then **H1b** (a cluster replay seeked by `atSimSeconds`) and **H2's wire check** (after PW). Reds it
finds come to you as tracker rows with the scenario and the first difference.

_coordinator lane, 2026-10-26_
