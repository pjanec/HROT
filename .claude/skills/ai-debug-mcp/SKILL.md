---
name: ai-debug-mcp
description: Drive, author, test, or inspect HROT over the ai-debug MCP server — scenarios, AI assets (BTree/HSM/Blueprint graphs), entities, missions, behaviors, blueprints, the running editor or the --mode all cluster. Triggers whenever a task means using MCP to run the editor headless, author/edit a scenario or asset or mission over MCP, spawn/attach/run entities, read back runtime state, or decide whether the MCP surface CAN do something. Always read tools/ai-debug-mcp/SKILL.md FIRST; never derive MCP capabilities from engine source.
---

# Using the ai-debug MCP server

The ai-debug MCP server is the intended surface for driving HROT programmatically:
authoring scenarios and AI assets, spawning and attaching entities, editing missions,
running/previewing, and reading runtime state — on the plain editor or the `--mode all`
cluster.

## ⭐ FIRST MOVE — read the server's own agent guide, before anything else

```
Read tools/ai-debug-mcp/SKILL.md
```

It is the authoritative, generated agent guide and it answers most questions directly:
- **§1 Mental model** — one-process editor vs cluster, the four run-states (Edit/Live/Preview/Replay),
  the two load modes, wait-gating, the response envelope. Most "why did nothing happen" is a run-state
  mistake and is explained here.
- **§3 Canonical workflows** — load+inspect, drive+observe, run-until-condition, experiment+revert,
  record/replay, inspect AI behavior, author a scenario. Compose these; they are the point of the API.
- **§4 Full command reference** — every tool, grouped, with its params, return shape, and *why/boundary*
  notes (e.g. a tool's note tells you whether it is a runtime shortcut or an authoring edit).
- **§ Capabilities & boundaries** — what is exposed over MCP and what is engine-only / not-yet-exposed.
- **§5 Gotchas** and **§6 Discover before you guess** — `list_*`/`get_*_schema` discovery before mutation.

## ⛔ Do NOT derive MCP capabilities from engine source

If you find yourself reading engine `.cs` to answer *"can the MCP do X?"* — stop. That question is
the SKILL's job. Read `tools/ai-debug-mcp/SKILL.md` (and the per-tool notes in §4).

**If the SKILL cannot answer whether something is possible, that is a SKILL GAP — file it, do not spelunk.**
A capability that is absent has no tool entry, so absence must be stated explicitly in the SKILL's
boundaries section, not inferred from source. Note the gap (which question the SKILL failed to answer)
so the doc can be improved, then proceed with what the SKILL does support.

## Discovery-first, always

The API is self-describing. Before guessing an id or a payload, ask the API:
`list_entity_types` before `spawn_entity` · `list_behaviors` before authoring a mission task ·
`list_blueprints` before `attach_blueprint` · `list_node_kinds` before `add_graph_node` ·
`get_attributes_schema` before `patch_attribute` · `list_commands` before `send_entity_command` ·
`get_capabilities` / `get_sim_state` whenever a call "did nothing" (you are probably in the wrong
run-state or perspective).
