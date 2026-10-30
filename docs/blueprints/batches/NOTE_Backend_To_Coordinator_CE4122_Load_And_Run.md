<!--STATUS
state: LIVE — backend → coordinator: CE-4122 fixed by CE-3354 — load-and-run, the filming node's 3-D map in its load, node names, CLI loads
updated: 2026-10-30
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Cluster_Load_Phase.md — §11.10 (design, R-3075), §11.10a (measured), §11.10b (names + CLI), §11.10c (as built + live acceptance)
  - ../../RUNBOOK_Filming.md — §0b (the one-call form and the map-in-load setting)
  - NOTE_Backend_To_Coordinator_CE4118_Combat_Events_Recorded.md — its startPaused line, corrected
-->
# NOTE — backend → coordinator: CE-4122 fixed (and what the user ruled around it)

**Dispatched at** the commit that adds this file (`backend`).

| | |
|---|---|
| **the cause** | `startPaused:false` armed a resume in `GlobalContextProcessManager` that fired on the announced cluster state; the master advances that state at the round's START and re-announces it mid-round (readiness), and a re-announce dropped the resume |
| **the fix (R-3075, the user)** | **load-and-run is the master's**: the clock runs when the load ROUND succeeded on EVERY node (a node acks only when its load chain resolved) — `ClusterMaster.RunsWhenLoaded` / `RunOnSuccess`; the old resume is retired |
| 📐 live | `POST /scenario/load/live {name, waitForReady:true, startPaused:false}` → runs by itself; `waitForReady` alone → `GET /status.load.phase = Loaded`, paused at 0 until `/sim/play` |

⭐ **New for filming** (the user's design, R-3075; RUNBOOK §0b updated):
- **the filming node's 3-D map in its load**: `POST /run-settings {"mapInLoad":true}` on that node's port (or `{"mapInLoadNodes":["film-cam"]}`) — then `waitForReady` returns only once that node's 3-D map is built. Other nodes never wait.
- **node names**: `--node-name film-cam` (or `--node-names IG=film-cam,…` in `-m all`); `GET /cluster/roster` lists `name` + `mapInLoad`; `GET /status.node`.
- **"loaded" is a fact**: `GET /status.load` → `requestId · scenario · phase (Loading│Loaded│Failed) · reason` — the master's answer for that load, never the announced state.
- **the command line**: `-m editor --node-name film-cam -s <scenario> --play --map-in-load` (also `-m all` / `-m server`; `-s <scenario>` alone opens it for editing in the editor, live-paused elsewhere).

⚠ My CE-4118 note's *"send startPaused:false … to have it run at once"* was unmeasured and wrong at the time — corrected there; it is true now.

_backend lane, 2026-10-30_
