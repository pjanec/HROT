#!/bin/bash
# =============================================================================
# cloud-setup.sh  -  Anthropic-cloud (Claude Code on the web) ENVIRONMENT SETUP
# =============================================================================
# Paste the CONTENTS of this file into your web environment's "Setup script"
# field (Environment settings -> Setup script). Do NOT use `bash scripts/...`:
# a setup script runs as root BEFORE Claude Code launches and the repository is
# NOT checked out at a known path yet, so repo-relative paths fail (exit 127).
# This script is therefore fully SELF-CONTAINED and references no repo files.
#
# Why the Setup script (and not a SessionStart hook or the agent)?
#   MCP servers in .mcp.json are spawned when the session starts. Only the setup
#   script runs *before* that, so only it can put the binary in place in time for
#   codebase-memory-mcp to connect on SESSION #1.
#
# Installs:
#   1. .NET 8 SDK            -> /usr/local/dotnet  (symlinked onto PATH)
#   2. codebase-memory-mcp   -> /opt/codebase-memory-mcp/codebase-memory-mcp
#      (matches the default path in the repo's .mcp.json)
#
# Requirements:
#   * Network policy = Full (needs github.com/releases + dot.net). Trusted may
#     block those; if the MCP/.NET download fails, switch to Full.
#   * Runs as root on Ubuntu 24.04 (the cloud default). Keep total time < ~5 min
#     so the environment cache can build; the result is cached for later sessions.
# =============================================================================
set -euo pipefail

echo "[cloud-setup] installing .NET 8 SDK + codebase-memory-mcp ..."

# --- 1. .NET 8 SDK ----------------------------------------------------------
if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks 2>/dev/null | grep -q '^8\.'; then
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    bash /tmp/dotnet-install.sh --channel 8.0 --install-dir /usr/local/dotnet --no-path
    ln -sf /usr/local/dotnet/dotnet /usr/local/bin/dotnet
fi
# Persist env for login shells (the muxer self-detects DOTNET_ROOT from the exe,
# but set it explicitly for tools that read it).
cat > /etc/profile.d/dotnet.sh <<'PROF'
export DOTNET_ROOT=/usr/local/dotnet
export PATH="/usr/local/dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
PROF

# --- 2. codebase-memory-mcp -------------------------------------------------
# Official checksum-verified installer. --skip-config keeps it from rewriting
# agent MCP configs (the repo's .mcp.json already declares the server). On Linux
# the installer auto-selects the fully-static "-portable" build (no runtime).
if [ ! -x /opt/codebase-memory-mcp/codebase-memory-mcp ]; then
    curl -fsSL https://raw.githubusercontent.com/DeusData/codebase-memory-mcp/main/install.sh \
        | bash -s -- --skip-config --dir /opt/codebase-memory-mcp
fi

echo "[cloud-setup] dotnet: $(dotnet --version 2>&1 || echo MISSING)"
echo "[cloud-setup] mcp:    $(/opt/codebase-memory-mcp/codebase-memory-mcp --version 2>&1 || echo MISSING)"
echo "[cloud-setup] done. codebase-memory-mcp will connect on session start."
