#!/usr/bin/env bash
#
# cloud-bootstrap.sh
# -----------------------------------------------------------------------------
# One-shot bootstrap for Anthropic-cloud (Claude Code on the web) sessions.
#
# Installs, idempotently:
#   1. The .NET 8 SDK           -> $HOME/.dotnet         (for building/testing the solution)
#   2. codebase-memory-mcp      -> /opt/codebase-memory-mcp/codebase-memory-mcp
#      (the single static "portable" Linux binary from the official installer)
#
# The path in step 2 is the SAME path that .mcp.json points at by default:
#     "command": "${CODEBASE_MEMORY_MCP_BIN:-/opt/codebase-memory-mcp/codebase-memory-mcp}"
# so once this script has run, the codebase-memory-mcp MCP server connects.
#
# WHERE TO RUN THIS (read docs/cloud-codebase-memory-mcp.md for the full story):
#   * BEST  - paste `bash scripts/cloud-bootstrap.sh` into your web environment's
#             *Setup script* field. It runs BEFORE the session, so the MCP server
#             is present and connects on session #1.
#   * OK    - let the agent run it during a session (via the /cloud-bootstrap
#             skill or the CLAUDE.md pointer). The binary is cached, but the MCP
#             server only connects on the NEXT session (MCP servers are spawned
#             at session start, before the agent can install anything).
#
# Network: the codebase-memory-mcp download comes from github.com/releases, so the
#          environment network policy must allow GitHub (a "Full" policy does).
#          raw.githubusercontent.com + dot.net must also be reachable.
#
# Safe to run repeatedly. Set CBM_FORCE=1 to re-download the MCP binary even if
# it is already present (to pick up a newer release).
# -----------------------------------------------------------------------------
set -euo pipefail

log() { printf '[cloud-bootstrap] %s\n' "$*"; }

# Single scratch dir for the whole run, cleaned up once on exit.
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# --- resolve paths ----------------------------------------------------------
PROJECT_DIR="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
CBM_DIR="/opt/codebase-memory-mcp"
CBM_BIN="$CBM_DIR/codebase-memory-mcp"

# Fall back to a user-writable dir if /opt is not writable (non-root VMs).
if ! mkdir -p "$CBM_DIR" 2>/dev/null && ! { command -v sudo >/dev/null 2>&1 && sudo mkdir -p "$CBM_DIR" 2>/dev/null; }; then
    CBM_DIR="$HOME/.local/share/codebase-memory-mcp"
    CBM_BIN="$CBM_DIR/codebase-memory-mcp"
    log "WARNING: /opt not writable; using $CBM_BIN instead."
    log "         Set CODEBASE_MEMORY_MCP_BIN=$CBM_BIN in the env, or edit .mcp.json."
    mkdir -p "$CBM_DIR"
fi

# --- helper: persist an env line for later shells + this Claude session -----
persist_env() {
    local line="$1"
    # In-session Bash (Claude reads this file after the SessionStart hook).
    [ -n "${CLAUDE_ENV_FILE:-}" ] && printf '%s\n' "$line" >> "$CLAUDE_ENV_FILE"
    # Later interactive/login shells (covers the Setup-script path).
    grep -qxF "$line" "$HOME/.bashrc" 2>/dev/null || printf '%s\n' "$line" >> "$HOME/.bashrc"
    # ...and NON-interactive shells. ~/.bashrc opens with `[ -z "$PS1" ] && return`,
    # so anything appended there is invisible to the shells Claude's Bash tool spawns —
    # `dotnet` came back "command not found" in every tool call despite a good install.
    # ~/.profile has no such guard (it sources ~/.bashrc, then keeps going), so persist
    # there too. Idempotent: skip if the exact line is already present.
    grep -qxF "$line" "$HOME/.profile" 2>/dev/null || printf '%s\n' "$line" >> "$HOME/.profile"
}

# ============================================================================
# 1. .NET 8 SDK
# ============================================================================
install_dotnet() {
    if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^8\.'; then
        log ".NET 8 SDK already on PATH ($(dotnet --version)); skipping."
    elif [ -x "$DOTNET_ROOT/dotnet" ] && "$DOTNET_ROOT/dotnet" --list-sdks 2>/dev/null | grep -q '^8\.'; then
        log ".NET 8 SDK already at $DOTNET_ROOT; skipping."
    else
        log "Installing .NET 8 SDK into $DOTNET_ROOT ..."
        curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$WORK/dotnet-install.sh"
        bash "$WORK/dotnet-install.sh" --channel 8.0 --install-dir "$DOTNET_ROOT" --no-path
        log ".NET 8 SDK installed: $("$DOTNET_ROOT/dotnet" --version)"
    fi

    # Make dotnet usable in this session and in future shells.
    export DOTNET_ROOT PATH="$DOTNET_ROOT:$PATH"
    persist_env "export DOTNET_ROOT=\"$DOTNET_ROOT\""
    persist_env "export PATH=\"$DOTNET_ROOT:\$PATH\""

    # Belt and braces: Claude's Bash tool spawns plain non-interactive shells, which source
    # NEITHER ~/.bashrc (guarded by `[ -z "$PS1" ] && return`) NOR ~/.profile, and
    # CLAUDE_ENV_FILE is not always set. So a perfectly good install can still leave every
    # tool call reporting `dotnet: command not found`. /usr/local/bin *is* on the default
    # PATH, so drop a wrapper there. It must be a wrapper, not a symlink: the dotnet muxer
    # infers its root from argv[0]'s directory, which a symlink would resolve wrongly.
    if [ -d /usr/local/bin ] && [ ! -e /usr/local/bin/dotnet ] && [ -w /usr/local/bin ]; then
        cat > /usr/local/bin/dotnet <<WRAP
#!/bin/sh
# Generated by scripts/cloud-bootstrap.sh — puts the SDK on the default PATH.
export DOTNET_ROOT="$DOTNET_ROOT"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
exec "\$DOTNET_ROOT/dotnet" "\$@"
WRAP
        chmod +x /usr/local/bin/dotnet
        log "Wrapper installed: /usr/local/bin/dotnet -> $DOTNET_ROOT/dotnet"
    fi
    persist_env "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
    persist_env "export DOTNET_NOLOGO=1"
}

# ============================================================================
# 2. codebase-memory-mcp (portable static Linux binary, no runtime needed)
# ============================================================================
install_cbm() {
    if [ "${CBM_FORCE:-0}" != "1" ] && [ -x "$CBM_BIN" ] && "$CBM_BIN" --version >/dev/null 2>&1; then
        log "codebase-memory-mcp already installed ($("$CBM_BIN" --version 2>&1)); skipping. (CBM_FORCE=1 to refresh.)"
        return 0
    fi

    log "Installing codebase-memory-mcp into $CBM_DIR ..."
    # Use the official, checksum-verified installer. --skip-config keeps it from
    # rewriting agent MCP configs (we manage .mcp.json ourselves). --dir sets the
    # target; on Linux the installer auto-selects the fully-static -portable build.
    curl -fsSL https://raw.githubusercontent.com/DeusData/codebase-memory-mcp/main/install.sh \
        | bash -s -- --skip-config --dir "$CBM_DIR" || true

    # The installer's --dir has meant different things across releases: <=0.8 put the
    # binary there, 0.9 puts only the updater there and drops the binary in
    # ~/.local/bin. It can also exit non-zero from a bad self-check while having
    # installed a perfectly good binary. So trust the filesystem, not the exit code:
    # find the real binary and make $CBM_BIN resolve to it.
    if [ ! -x "$CBM_BIN" ] || ! "$CBM_BIN" --version >/dev/null 2>&1; then
        local found=""
        for cand in "$HOME/.local/bin/codebase-memory-mcp" \
                    /root/.local/bin/codebase-memory-mcp \
                    /usr/local/bin/codebase-memory-mcp; do
            if [ -x "$cand" ] && "$cand" --version >/dev/null 2>&1; then found="$cand"; break; fi
        done
        if [ -n "$found" ]; then
            mkdir -p "$CBM_DIR" 2>/dev/null || true
            ln -sf "$found" "$CBM_BIN" 2>/dev/null || cp -f "$found" "$CBM_BIN" 2>/dev/null || true
            log "Linked $CBM_BIN -> $found (installer used its own location)."
        fi
    fi

    if [ -x "$CBM_BIN" ] && "$CBM_BIN" --version >/dev/null 2>&1; then
        log "codebase-memory-mcp installed: $("$CBM_BIN" --version 2>&1)"
    else
        log "ERROR: codebase-memory-mcp not runnable at $CBM_BIN."
        log "       If the download itself failed, the network policy may block"
        log "       github.com releases - switch the policy to Full and re-run."
        log "       Otherwise set CODEBASE_MEMORY_MCP_BIN to the real binary path."
        return 1
    fi
}

# ============================================================================
# 3. Index this repository (best effort; the graph query is what benefits)
# ============================================================================
index_repo() {
    [ -x "$CBM_BIN" ] || return 0
    log "Indexing repository ($PROJECT_DIR) into the knowledge graph (best effort) ..."
    # CLI form: `codebase-memory-mcp cli index_repository '{"repo_path":"..."}'`
    local json; json=$(printf '{"repo_path": "%s"}' "$PROJECT_DIR")
    if "$CBM_BIN" cli index_repository "$json" >/dev/null 2>&1; then
        log "Indexed. Use list_projects to see the project name."
    else
        log "Automatic indexing skipped (the agent can call index_repository via MCP)."
    fi
}

main() {
    log "Starting. project=$PROJECT_DIR  mcp-bin=$CBM_BIN"
    install_dotnet
    local cbm_ok=0
    install_cbm && cbm_ok=1 || true
    [ "$cbm_ok" = 1 ] && index_repo || true
    log "Done. dotnet=$(command -v dotnet || echo "$DOTNET_ROOT/dotnet")  mcp=$([ -x "$CBM_BIN" ] && echo "$CBM_BIN" || echo MISSING)"
    if [ "$cbm_ok" != 1 ]; then
        log "NOTE: codebase-memory-mcp was NOT installed; the MCP server will show 'failed to connect'."
        return 1
    fi
}

main "$@"
