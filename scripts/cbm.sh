#!/usr/bin/env bash
# codebase-memory, CLI ONLY (user, 2026-10-20: the MCP server is disabled — its permission prompts blocked autonomous work).
#
#   scripts/cbm.sh <tool> ['<json args>']
#   scripts/cbm.sh search_graph '{"name_pattern":".*Terrain.*","label":"Class"}'
#   scripts/cbm.sh search_code  '{"pattern":"TerrainPlan2D","limit":50}'
#   scripts/cbm.sh trace_path   '{"function_name":"<qualified name>","direction":"inbound","depth":3}'
#   scripts/cbm.sh query_graph  '{"query":"MATCH (c:Class) RETURN c.name LIMIT 5"}'
#   scripts/cbm.sh <tool> --help
#
# - finds the binary ($CODEBASE_MEMORY_MCP_BIN, PATH, /opt/codebase-memory-mcp, ~/.local/bin);
# - indexes this repo first when no project is indexed (a fresh cloud VM; tens of seconds);
# - adds "project" when the args name none;
# - passes the args on STDIN (raw-JSON argv is deprecated by the CLI).
# ⚠ check_index_coverage is NOT available through the CLI — say so in any negative or exhaustive claim.
set -euo pipefail

BIN="${CODEBASE_MEMORY_MCP_BIN:-}"
[ -z "$BIN" ] && BIN="$(command -v codebase-memory-mcp 2>/dev/null || true)"
[ -z "$BIN" ] && [ -x /opt/codebase-memory-mcp/codebase-memory-mcp ] && BIN=/opt/codebase-memory-mcp/codebase-memory-mcp
[ -z "$BIN" ] && [ -x "$HOME/.local/bin/codebase-memory-mcp" ] && BIN="$HOME/.local/bin/codebase-memory-mcp"
if [ -z "$BIN" ]; then
  echo "codebase-memory: UNAVAILABLE — binary not found; run: bash scripts/cloud-bootstrap.sh" >&2
  exit 2
fi

TOOL="${1:?usage: scripts/cbm.sh <tool> ['<json args>'] | <tool> --help}"
shift
if [ "${1:-}" = "--help" ]; then exec "$BIN" cli "$TOOL" --help; fi
ARGS="${1:-{\}}"
ROOT="$(git -C "$(dirname "$0")/.." rev-parse --show-toplevel)"

project_of_root() {
  "$BIN" cli list_projects 2>/dev/null | awk -v root="$ROOT" '$2 == root { print $1; exit }'
}

PROJECT="$(project_of_root)"
if [ -z "$PROJECT" ] && [ "$TOOL" != "index_repository" ] && [ "$TOOL" != "list_projects" ]; then
  echo "codebase-memory: no index for $ROOT — indexing first (tens of seconds) ..." >&2
  printf '{"repo_path":"%s"}' "$ROOT" | "$BIN" cli index_repository >&2
  PROJECT="$(project_of_root)"
fi

if [ -n "$PROJECT" ]; then
  ARGS="$(python3 -c 'import json,sys; a=json.loads(sys.argv[1] or "{}"); a.setdefault("project", sys.argv[2]); print(json.dumps(a))' "$ARGS" "$PROJECT")"
fi
printf '%s' "$ARGS" | "$BIN" cli "$TOOL"
