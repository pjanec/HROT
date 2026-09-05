---

## 2. Lifecycle — starting and stopping the runner

| Tool | Purpose | Key params |
|------|---------|-----------|
| `start_simulation` | Launch the runner (or it may already be attached via `--url`) and wait until ready | `runnerDll` (abs path, optional if server has `--runner-dll`), `port` (default 8099), `headless` (bool) |
| `stop_simulation` | Graceful shutdown (`/shutdown` → wait → SIGKILL fallback). Always call when done. | none |
| `get_status` | Liveness + summary: `scenario, clusterState, simTime, timeScale, isPaused, inPreview, entityCount, recording` | none |

Typical opening: `start_simulation{headless:true}` → poll/observe `get_status` returns `ok` → proceed.
Typical close: `stop_simulation`. Never leave a runner running between unrelated tasks.
