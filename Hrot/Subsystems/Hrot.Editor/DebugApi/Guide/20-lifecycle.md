---

## 2. Lifecycle — starting and stopping the runner

⭐ **Starting a process is the one thing a running process cannot tell you**: the editor, a node or `--mode all` —
see `docs/RUNBOOK_Cluster_Debugging_Over_Http.md`. Before anything is started, `Hrot.ClusterRunner --help-api` prints this guide.

| Route | Purpose |
|------|---------|
| `GET /status` | Liveness + summary: `scenario, clusterState, simTime, timeScale, isPaused, inPreview, entityCount, recording` |
| `POST /shutdown` | Graceful shutdown. Always call when done. |

Typical opening: start the process (runbook) → poll `GET /status` until it answers `ok` → proceed.
Typical close: `POST /shutdown` (`curl -X POST -d '{}' …` — a POST with no body is refused with 411). Never leave a runner running between unrelated tasks.
