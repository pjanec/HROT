---

## 1. Mental model (read this first — most mistakes come from skipping it)

**Usually one process, one world.** In the editor, brain (AI) and muscle (physics/spawning) share one ECS
`EntityRepository` and there is no cluster to reason about. Entities are identified by a stable `networkId`
(a long, e.g. `1000`).

**But check which host you are talking to — `GET /capabilities` tells you.** The same API is also served by a
multi-subsystem cluster host (`mode: "all"` = orchestrator + simhost + ig + excon + cgf). There:

- ⭐ **load first, then switch.** A cluster that rests in Idle (nothing loaded — at boot, after an unload) shows the **`Start`**
  perspective on every host; while it shows, `POST /perspective` answers **HTTP 409** with the reason, and `GET /status` reports
  `perspective: "Start"` with `routedTo` = the perspective the world reads are still answered from. Load a scenario (or open a
  recording) first — leaving Idle restores the host's remembered perspective by itself — and only then switch. Never script
  *"switch perspective at boot"*;
- commands act **in the context of the currently selected perspective** (`POST /perspective`), because that
  is how an operator would drive it — so a read answers for *that node*, not for "the world";
- capabilities differ per perspective. A call the active perspective cannot serve answers **HTTP 501
  `NOT_SUPPORTED_HERE`** with the capability key that is missing (e.g. `time.drive`, `world.read`,
  `scenario.load`). That is a truthful "not here", **not** a bug and not a crash — ask `GET /capabilities` and
  either switch perspective or use a different endpoint;
- `networkId`s are **not portable between hosts**: the editor and a cluster allocate them from different
  authorities, so the same scenario yields different ids in each. Match entities by name across hosts.

**Four run states** — almost every "why didn't that work" is a run-state mistake:

| State | What it means | Time advances? | How you got here |
|-------|---------------|----------------|------------------|
| **Edit** | Authoring; world is static | No | `POST /scenario/load/edit` |
| **Live** | A real run on every node | Yes — **once you `POST /sim/play`** | `POST /scenario/load/live` |
| **Preview** | The cluster's dry run, rewound on exit; **never recorded** | Only when **unpaused** | `POST /preview/enter` (from Edit) |
| **Replay** | Every node replays its OWN recording of a past live run | `POST /sim/play`/`POST /sim/pause`/`POST /sim/step` drive the replay clock; `POST /replay/seek` jumps | `POST /replay` (from ANY state) |

**Two load modes, and they are not the same operation.** `POST /scenario/load/edit` freezes time for authoring;
`POST /scenario/load/live` starts an exercise run. Both are cluster-wide two-phase-commit transitions — the editor
is not special, it is a one-node cluster. ⚠ In `mode: "all"` an *edit* load is currently partial (CGF has no
edit-load handler yet), so use **live** when every node must hold the world.

- **Time only advances when `inPreview == true` AND `isPaused == false`.** In Edit state the sim is frozen —
  `POST /sim/step`/commands that need ticks won't progress until you enter preview and unpause. Always check
  `GET /sim/state` / `GET /status` if unsure.
- ⭐ **A CLUSTER (`mode:"all"`) BOOTS PAUSED, DELIBERATELY, AND A LIVE LOAD DOES NOT START IT.** `simTime`
  stays at 0 with `isPaused:true` until you `POST /sim/play` — so *"nothing is happening"* is the **expected** state,
  not a fault, and it is the first thing to check before diagnosing anything as stuck. ⚠ It did not always
  behave this way: the clock used to start itself ~2 s after boot and run with no scenario loaded, which
  also meant every `POST /sim/step` was silently refused. If you meet an old build whose `simTime` climbs on its own
  with `clusterState:Idle`, that is the old behaviour — and any POST /sim/pause/POST /sim/step measurement taken on it is
  unreliable.
- **Preview is revertible.** Entering preview snapshots the world; `POST /preview/exit` rewinds to it. It is never
  recorded — only a LIVE run records, on every node, by itself.
- **Every route that changes the cluster goes through the cluster master, on every host** — the editor is a
  one-node cluster. There is no editor shortcut and no `cluster/` prefix to remember: `POST /preview/enter`,
  `POST /checkpoint`, `POST /replay`, `POST /replay/seek`, `POST /scenario/save` are each one master intent.
- **A POST /checkpoint is part of the LIVE recording** — a named keyframe on every node — not a preview snapshot.
  `POST /checkpoint/restore {label}` continues the live run from it.
- **Live does not imply recording** (AQ108 D14). The cluster-wide setting *Record live runs* (default ON;
  `POST /run-settings {recordLiveRuns}`) applies from the NEXT live load; `GET /status → recording` says whether the run
  on screen is recorded. A live run loaded with it OFF writes no recording: `POST /checkpoint`, `POST /checkpoint/restore`,
  `POST /review`, `POST /exercises/keep` and `POST /replay` (of the last run) answer **409 "this run was not recorded"**.
  `POST /review` needs its start (`atSeconds` or `atSimSeconds`). `POST /scenario/save` answers 409 in a live run and in a
  replay / review — a live world is not the scenario. `DELETE /exercises/{id} {id}` discards an unarchived recording.
- **A replay is the cluster's, not a sandbox.** `POST /replay` (one request from any state — the master plans
  the way) puts every node into its own recording; the ORDINARY readers (`GET /entities`, `GET /entities/{networkId}`,
  `GET /entities/{networkId}/brain`, `GET /entities/{networkId}/journal`) then read the replayed world. `POST /replay/stop` goes to Idle.

**The envelope.** Every route returns `{ ok, data, error, awaited }`. On success read `data`. On failure
`ok:false` and `error` explains why. `awaited` relates to wait-gating (below). A failure is never hidden as success.

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

**A malformed field is now a 400, not a silently-substituted value** (`CE-191`). The API used to answer
`ok:true` after quietly discarding input it could not parse. It no longer does:

| you send | you used to get | you now get |
|---|---|---|
| `spawn` with an unparseable `transform` | the entity **at the origin**, `ok:true` | 400, nothing spawned |
| `spawn` with a **typo'd** component type | the entity **without that component**, `ok:true` | 400 naming the unknown type |
| a graph command with `"x": "left-ish"` | the node at **0**, `ok:true` | 400 naming the field |
| `remove` with one malformed id among five | **the other four deleted**, `ok:true` | 400, nothing removed |

⚠ **Absent optional fields are still legal** — only *unparseable* ones changed. And on reads, an
unserializable row is no longer dropped: it stays in the array carrying `payloadError` /
`serializationError`, so a count is never quietly short.

**Wait-gating (why you sometimes see `awaited:false`).** Commands that *could* wait for a result only do so
when time is advancing. If you send a command with `wait:true` while the sim is paused/in Edit, you get
`{awaited:false, reason:"sim not running"}` immediately instead of a hang. That is expected — POST /sim/pause-POST /sim/step-inspect
is the intended flow (see Workflow B).
