---
name: cloud-bootstrap
description: Bootstrap an Anthropic-cloud (Claude Code on the web) session for this repo - install the .NET 8 SDK and the codebase-memory binary (used through its CLI, scripts/cbm.sh). Use at the start of a cloud session when `dotnet` is missing or `scripts/cbm.sh` reports UNAVAILABLE.
---

# Cloud session bootstrap

Prepares a fresh Claude-Code-on-the-web VM to work on this repo: installs the
**.NET 8 SDK** (to build/test the solution) and the **codebase-memory** binary
(the static Linux binary the graph queries need).

⛔ **The codebase-memory MCP server is DISABLED** (user, 2026-10-20 — its permission
prompts blocked autonomous work). The binary is used through its **CLI only**:
`scripts/cbm.sh <tool> '<json args>'`. There is no MCP timing issue any more — the
CLI works the moment the binary is installed.

## When to run
- On a fresh cloud session where `dotnet` is not installed, OR
- `scripts/cbm.sh list_projects` reports `UNAVAILABLE` (binary missing).
- Skip on local Windows/VS Code sessions that already have both.

## Steps
1. Run the bootstrap script (idempotent, safe to re-run):
   ```bash
   bash scripts/cloud-bootstrap.sh
   ```
2. Confirm the results printed by the script:
   - `.NET 8 SDK installed` (or "already ... skipping").
   - `codebase-memory-mcp installed` at `/opt/codebase-memory-mcp/codebase-memory-mcp`.
3. Verify:
   ```bash
   dotnet --version                 # expect 8.x
   scripts/cbm.sh list_projects     # indexes the repo first if nothing is indexed
   ```

## If the install fails
- "install failed / github blocked": the environment network policy must allow
  github.com releases. Switch the policy to **Full** and re-run.
- To force a re-download of a newer release: `CBM_FORCE=1 bash scripts/cloud-bootstrap.sh`.

## Indexing
The graph DB is not carried across cloud sessions. `scripts/cbm.sh` indexes the repo
automatically when no project is indexed (tens of seconds). By hand:
`scripts/cbm.sh index_repository '{"repo_path":"'"$PWD"'"}'`.
