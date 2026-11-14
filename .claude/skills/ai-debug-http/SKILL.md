---
name: ai-debug-http
description: Drive, author, test, or inspect HROT over its HTTP debug API — scenarios, AI assets (BTree/HSM/Blueprint graphs), entities, missions, behaviors, blueprints, the running editor or the --mode all cluster. Triggers whenever a task means running the editor headless, authoring/editing a scenario or asset or mission over HTTP, spawning/attaching/running entities, reading back runtime state, or deciding whether the API CAN do something. Start the process per the runbook, then read GET /help FIRST; never derive API capabilities from engine source.
---

# Using the HROT debug API (plain HTTP)

The debug API is the intended surface for driving HROT programmatically. It is plain HTTP on the process's port — every agent
here has a shell, so `curl` is enough. ⭐ It describes itself; this skill only says where to look.

## ① Start a process — the one thing a running process cannot tell you

`docs/RUNBOOK_Cluster_Debugging_Over_Http.md` — the editor, a node, or `--mode all`; ports; the traps (`localhost`, not
`127.0.0.1`; a cluster boots paused; one node per port). The performance switches (`HROT_PARALLEL_SIM`, `HROT_THREADED_KERNEL`,
`HROT_SIM_THREADS`) are set when you START the process — they are read once.

## ② Read the guide the process serves — before anything else

```bash
curl -s --noproxy '*' http://localhost:8099/help          # the guide: mental model, workflows, gotchas, EVERY route
dotnet Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll --help-api   # the same, with nothing started
curl -s --noproxy '*' http://localhost:8099/capabilities  # JSON: the routes + what THIS host can do now
```

It is rendered from the route table of the build you are talking to, so it is always current: every route as `METHOD /path`, where each
parameter goes (`{path}`, `?query` for GET / DELETE, a JSON body for POST / PUT / PATCH), its return shape and its gotchas.

## ⛔ Do NOT derive API capabilities from engine source

If you find yourself reading engine `.cs` to answer *"can the API do X?"* — stop; that is `GET /help`'s job. If it cannot answer, that
is a GAP in the route docs (`Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiRouteDocs.cs`) — note it so the doc is improved, then proceed
with what the API does support.

## Discovery first, always

Before guessing an id or a payload, ask the API (`GET /help` names each route): the entity types before spawning, the behaviours before
authoring a mission task, the blueprints before attaching one, the node kinds before adding a graph node, the attributes schema before
patching, the commands before sending one; `GET /status` / `GET /capabilities` whenever a call "did nothing" — you are probably in the
wrong run state or perspective.
