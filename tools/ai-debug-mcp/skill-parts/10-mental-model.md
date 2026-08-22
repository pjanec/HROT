---

## 1. Mental model (read this first — most mistakes come from skipping it)

**One process, one world.** Brain (AI) and muscle (physics/spawning) share one ECS `EntityRepository`. There
is no network/cluster to reason about. Entities are identified by a stable `networkId` (a long, e.g. `1000`).

**Three run states** — almost every "why didn't that work" is a run-state mistake:

| State | What it means | Time advances? | How you got here |
|-------|---------------|----------------|------------------|
| **Edit** | Authoring; world is static | No | After `load_scenario` |
| **Preview** | A revertible run from a RAM snapshot | Only when **unpaused** | `enter_preview`, `play`, `checkpoint`, or `start_recording{preview}` |
| **Replay** | Read-only playback of a `.fdp` in an isolated sandbox | N/A (you seek frames) | `load_replay` |

- **Time only advances when `inPreview == true` AND `isPaused == false`.** In Edit state the sim is frozen —
  `step`/commands that need ticks won't progress until you enter preview and unpause. Always check
  `get_sim_state` / `get_status` if unsure.
- **Preview is revertible.** Entering preview snapshots the world; exiting (`stop_preview` /
  `restore_checkpoint`) rewinds to that snapshot. This is the basis of checkpoint/restore.
- **Replay is isolated.** Seeking a replay never touches the live world — they are independent.

**Single preview slot.** `checkpoint`, `enter_preview`, and `start_recording{preview}` all use the *same one*
snapshot slot. You cannot nest them. Restore/stop/exit before starting another. Recording and checkpoint are
mutually exclusive.

**The envelope.** Every tool returns `{ ok, data, error, awaited }`. On success read `data`. On failure
`ok:false` and `error` explains why (and the tool result is flagged as an MCP error). `awaited` relates to
wait-gating (below). The server passes this through verbatim — it never hides a failure as success.

**Wait-gating (why you sometimes see `awaited:false`).** Commands that *could* wait for a result only do so
when time is advancing. If you send a command with `wait:true` while the sim is paused/in Edit, you get
`{awaited:false, reason:"sim not running"}` immediately instead of a hang. That is expected — pause-step-inspect
is the intended flow (see Workflow B).
