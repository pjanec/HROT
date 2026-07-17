---
name: cloud-bootstrap
description: Bootstrap an Anthropic-cloud (Claude Code on the web) session for this repo - install the .NET 8 SDK and the codebase-memory-mcp server. Use at the start of a cloud session when `dotnet` is missing or the codebase-memory-mcp MCP tools are not connected.
---

# Cloud session bootstrap

Prepares a fresh Claude-Code-on-the-web VM to work on this repo: installs the
**.NET 8 SDK** (to build/test the solution) and the **codebase-memory-mcp** server
(the static Linux binary the graph tools need).

## When to run
- On a fresh cloud session where `dotnet` is not installed, OR
- the `mcp__codebase-memory-mcp__*` tools are not available (server "failed to connect").
- Skip on local Windows/VS Code sessions - those already have the tools.

## Steps
1. Run the bootstrap script (idempotent, safe to re-run):
   ```bash
   bash scripts/cloud-bootstrap.sh
   ```
2. Confirm the results printed by the script:
   - `.NET 8 SDK installed` (or "already ... skipping").
   - `codebase-memory-mcp installed` at `/opt/codebase-memory-mcp/codebase-memory-mcp`.
3. Verify .NET is usable this session:
   ```bash
   dotnet --version   # expect 8.x
   ```

## IMPORTANT - MCP server timing
MCP servers listed in `.mcp.json` are spawned when the session **starts**, before
this skill can run. So if the binary was installed *during this session*, the
`codebase-memory-mcp` tools connect only on the **next** session (the binary is
cached). To have the graph tools on session #1, put `bash scripts/cloud-bootstrap.sh`
in the environment's **Setup script** field instead (runs before the session).
See `docs/cloud-codebase-memory-mcp.md`.

## If the install fails
- "install failed / github blocked": the environment network policy must allow
  github.com releases. Switch the policy to **Full** and re-run.
- To force a re-download of a newer MCP release: `CBM_FORCE=1 bash scripts/cloud-bootstrap.sh`.

## After the server is connected
Follow the normal graph-first workflow in `.claude/CLAUDE.md`:
`list_projects` -> `get_architecture` -> `search_graph` / `trace_call_path` -> `get_code_snippet`.
If the graph is empty, index this repo once via the `index_repository` MCP tool.
