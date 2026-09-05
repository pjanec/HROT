---

## 1. Mental model (read this first — most mistakes come from skipping it)

**Usually one process, one world.** In the editor, brain (AI) and muscle (physics/spawning) share one ECS
`EntityRepository` and there is no cluster to reason about. Entities are identified by a stable `networkId`
(a long, e.g. `1000`).

**But check which host you are talking to — `get_capabilities` tells you.** The same API is also served by a
multi-subsystem cluster host (`mode: "all"` = orchestrator + simhost + ig + excon + cgf). There:

- commands act **in the context of the currently selected perspective** (`switch_perspective`), because that
  is how an operator would drive it — so a read answers for *that node*, not for "the world";
- capabilities differ per perspective. A call the active perspective cannot serve answers **HTTP 501
  `NOT_SUPPORTED_HERE`** with the capability key that is missing (e.g. `time.drive`, `world.read`,
  `scenario.load`). That is a truthful "not here", **not** a bug and not a crash — ask `get_capabilities` and
  either switch perspective or use a different endpoint;
- `networkId`s are **not portable between hosts**: the editor and a cluster allocate them from different
  authorities, so the same scenario yields different ids in each. Match entities by name across hosts.

**Four run states** — almost every "why didn't that work" is a run-state mistake:

| State | What it means | Time advances? | How you got here |
|-------|---------------|----------------|------------------|
| **Edit** | Authoring; world is static | No | `load_scenario_edit` |
| **Live** | A real run on every node | Yes — **once you `play`** | `load_scenario_live` |
| **Preview** | A revertible run from a RAM snapshot | Only when **unpaused** | `enter_preview`, `play`, `checkpoint`, or `start_recording{preview}` |
| **Replay** | Read-only playback of a `.fdp` in an isolated sandbox | N/A (you seek frames) | `load_replay` |

**Two load modes, and they are not the same operation.** `load_scenario_edit` freezes time for authoring;
`load_scenario_live` starts an exercise run. Both are cluster-wide two-phase-commit transitions — the editor
is not special, it is a one-node cluster. ⚠ In `mode: "all"` an *edit* load is currently partial (CGF has no
edit-load handler yet), so use **live** when every node must hold the world.

- **Time only advances when `inPreview == true` AND `isPaused == false`.** In Edit state the sim is frozen —
  `step`/commands that need ticks won't progress until you enter preview and unpause. Always check
  `get_sim_state` / `get_status` if unsure.
- ⭐ **A CLUSTER (`mode:"all"`) BOOTS PAUSED, DELIBERATELY, AND A LIVE LOAD DOES NOT START IT.** `simTime`
  stays at 0 with `isPaused:true` until you `play` — so *"nothing is happening"* is the **expected** state,
  not a fault, and it is the first thing to check before diagnosing anything as stuck. ⚠ It did not always
  behave this way: the clock used to start itself ~2 s after boot and run with no scenario loaded, which
  also meant every `step` was silently refused. If you meet an old build whose `simTime` climbs on its own
  with `clusterState:Idle`, that is the old behaviour — and any pause/step measurement taken on it is
  unreliable.
- **Preview is revertible.** Entering preview snapshots the world; exiting (`stop_preview` /
  `restore_checkpoint`) rewinds to that snapshot. This is the basis of checkpoint/restore.
- **Replay is isolated.** Seeking a replay never touches the live world — they are independent.

**Single preview slot.** `checkpoint`, `enter_preview`, and `start_recording{preview}` all use the *same one*
snapshot slot. You cannot nest them. Restore/stop/exit before starting another. Recording and checkpoint are
mutually exclusive.

**The envelope.** Every tool returns `{ ok, data, error, awaited }`. On success read `data`. On failure
`ok:false` and `error` explains why (and the tool result is flagged as an MCP error). `awaited` relates to
wait-gating (below). The server passes this through verbatim — it never hides a failure as success.

**On a 500, read `fault` — it says WHERE the exception happened** (`CE-190`). An unhandled server-side
exception now reports its origin, not just its message:

```
"error": "System.NullReferenceException: Object reference not set… @ DebugApi/DebugApiService.cs:1234 in DebugApiService.DumpEntity",
"fault": { "type": …, "message": …, "site": "<file>:<line> in <Type.Method>", "frames": [ … ], "inner": [ … ] }
```

`error` carries the type and site inline (some callers only ever see that string); `fault` carries the frame
list and the inner-exception chain. **Report the `site` when you escalate a 500** — it is the difference
between "the API broke" and a file and line someone can open. Two caveats: a fault raised on the main thread
arrives wrapped in an `AggregateException`, and `site` deliberately names the *inner* throw rather than the
await (`wrappedIn` records the wrapper); and file/line need PDBs beside the assembly — without them `site`
degrades to `Type.Method +IL_0042`, never to nothing.

**Wait-gating (why you sometimes see `awaited:false`).** Commands that *could* wait for a result only do so
when time is advancing. If you send a command with `wait:true` while the sim is paused/in Edit, you get
`{awaited:false, reason:"sim not running"}` immediately instead of a hang. That is expected — pause-step-inspect
is the intended flow (see Workflow B).
