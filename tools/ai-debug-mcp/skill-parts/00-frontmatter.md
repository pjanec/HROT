---
name: ai-debug-sim
description: Drive and inspect a running Hrot ECS simulation (the FDP editor) over the ai-debug MCP server — load scenarios, query/mutate entities, set breakpoints, checkpoint/diff, record/replay, trace AI behaviors. Use when asked to test, debug, reproduce, or author simulation state autonomously.
---

# AI Debug & Test API — Agent Guide

> ⛔⛔ **THIS FILE (`SKILL.md`) IS GENERATED — DO NOT HAND-EDIT IT.** The command reference below is built
> from the C# route table, so a manual edit is silently overwritten on the next regen. To CHANGE what a
> command does or how it is documented, edit the **ground truth** and regenerate:
> 1. **Endpoint behaviour + params + notes** → the `RouteDoc`/`RouteParam` records in
>    `Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiRouteDocs.cs` *(these flow out through `GET /capabilities`)*.
>    *(C# XML `<summary>` comments on the handler do NOT feed the docs — they are for code readers only.)*
> 2. **Tools with no HTTP endpoint** *(e.g. `start_simulation`)* → `tools/ai-debug-mcp/catalog-supplement.mjs`.
> 3. **This guide's prose sections** *(mental model, workflows, gotchas)* → `tools/ai-debug-mcp/skill-parts/*.md`.
>
> Then regenerate: `node tools/ai-debug-mcp/gen-catalog.mjs` *(runs the built runner to dump `GET /capabilities` →
> writes `tool-catalog.mjs`)* then `node tools/ai-debug-mcp/generate-skill.mjs` *(assembles `SKILL.md`)*.
> `generate-skill.mjs --check` / `gen-catalog.mjs --dump <m> --check` fail if the committed output is stale.

You are driving a **single-process FDP simulation** (the ClusterRunner in `-m editor` mode) through the
`ai-debug` MCP server. Every tool is a thin 1:1 proxy onto an HTTP endpoint; the simulation owns all the
real logic. This guide teaches the mental model, the canonical workflows, and every command.
