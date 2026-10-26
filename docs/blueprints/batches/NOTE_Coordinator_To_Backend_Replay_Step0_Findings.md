<!--STATUS
state: LIVE — coordinator → backend: four replay findings from the harness's step 0 (CE-4051…CE-4054); FYI beside HANDOFF_Coordinator_To_Backend_Replay_Browser_Http
updated: 2026-10-26
current-answer: the whole note
stale-below: nothing
related-designs:
  - docs/DESIGN_Scenario_Regression.md — §10-0 the measurement; §10 the harness (R-4084) that will assert these once fixed
  - docs/DESIGN_Http_Control_Through_The_Cluster_Master.md — your route table the findings are measured against
-->

# NOTE — coordinator → backend: the replay through the master, measured in the editor (CE-4051…CE-4054)

⭐ Not a handoff — your call how to schedule them. The replay harness (CE-4045, approved R-4084) measured them before building H1; it will
assert each once fixed. Reproduce (`--mode editor --headless`, `HROT_DEBUG_API_PORT` set):

```
POST /scenario/load/live {"name":"ua-tank-ammo","waitForReady":true}   → OperatingLive, recording: true, paused
POST /sim/step {"count":600}                                          → sim clock 10.0 s
POST /replay {"atSeconds":0,"waitForReady":true}                       → OperatingReplay
POST /sim/step {"count":60}                                           → CE-4051
POST /replay/seek {"atSeconds":5}                                     → CE-4052
```

| row | finding | against |
|---|---|---|
| [CE-4051](../Blueprint_Issues_Tracker.md) | `/sim/step` in a replay never lands — *"the master never entered the step barrier"* | your §3: *"during a replay they drive the replay clock"* |
| [CE-4052](../Blueprint_Issues_Tracker.md) | `atSeconds: 0` enters at 3.18 s; a seek to 5 s lands at 1.20 s (not one constant offset) | §3 `POST /replay {atSeconds}`, `/replay/seek` |
| [CE-4053](../Blueprint_Issues_Tracker.md) | a paused / stepped LIVE run records every rendered frame on the wall clock: 2 230 frames for 600 steps, gaps 2–19 ms, a 25.16 s span for 10 s of sim | ❓ a design question — should a paused frame be recorded, and should a stepped run's recording clock advance by the step? |
| [CE-4054](../Blueprint_Issues_Tracker.md) | `GET /exercises` empty while the run records (yet `/replay` finds "the run in progress"); `/status` says `inPreview: true` in live and replay | §3 `GET /exercises` |
| [CE-4050](../Blueprint_Issues_Tracker.md) | the official `fdp-recording-dumper` fails on the same recording — *"Component type ID 118 not found"* | the census's CE-4050 |

---
_coordinator lane, 2026-10-26_
