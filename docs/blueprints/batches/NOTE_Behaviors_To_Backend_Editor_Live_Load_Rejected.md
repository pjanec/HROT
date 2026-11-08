<!--STATUS
state: LIVE — behaviors → backend: the editor's headless Live load (RUNBOOK_Filming §0b) is REJECTED on behaviors@HEAD
updated: 2026-11-08
current-answer: the whole note
related-designs:
  - ../../RUNBOOK_Filming.md — §0b, the sequence that fails
  - ../../RUNBOOK_Cluster_Debugging_Over_Http.md — the HTTP API
-->
# NOTE — behaviors → backend: **`POST /scenario/load/live` in a headless editor is REJECTED — the edit load's transition stays "parked waiting for its scenario files"**

Found while measuring CE-4137 (overnight O-4), three fresh editors, behaviors at `8bb540b85` (merged ui + backend that evening),
`xvfb-run -a dotnet Hrot.ClusterRunner.dll -m editor --no-wait`, `HROT_DEBUG_API_PORT=8120`, with and without `FDP_STAGING_ROOT`:

| call | answer |
|---|---|
| `POST /scenario/load/edit {"name":"cc-platoon-bound","waitForReady":true}` | ✅ `ok`, `OperatingEdit`, 59 entities |
| `POST /run-mode {"armed":"live"}` | ⛔ *"the scenario has unsaved changes (nodes: 1)"* — right after a clean load (cf. ui's CE-4199); with `"discardUnsaved":true` ✅ armed |
| `POST /scenario/load/live {"name":"cc-platoon-bound","waitForReady":true,"startPaused":false,"discardUnsaved":true}` | ⛔ *"Scenario 'cc-platoon-bound' failed to load: Rejected"* |
| editor log | `[Orchestrator] L8: transition <live> REJECTED — transition <edit> is parked waiting for its scenario files.` |

⇒ ⭐ the edit load's transition never leaves its "waiting for scenario files" park, and every later transition is refused. ⚠ The edit load
itself answered `ok` / `OperatingEdit`, so the park is invisible to the caller. ⭐ Ask: is the park expected with no Replay Browser / no
storage master, and if so what call releases it? RUNBOOK_Filming §0b's sequence (edit → arm live → load live) is the one that fails.
Repro script: the behaviors session's `edlive2.sh` shape — launch, edit load, arm, live load, read the log.

_behaviors lane, 2026-11-08 (overnight)_
