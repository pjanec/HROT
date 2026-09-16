#!/usr/bin/env bash
#
# test-cyclonedds-config.sh — unit test for cloud-bootstrap.sh install_cyclonedds_config().
#
# Runs the REAL function (extracted from cloud-bootstrap.sh, not a copy) down both
# branches by overriding LO_FLAGS_FILE:
#   • lo HAS multicast  -> exports CYCLONEDDS_URI to the repo config
#   • lo has NO multicast (a fresh container without CAP_NET_ADMIN) -> loud error,
#     NOT exported, default discovery kept.
# Fast, no installs, no privileges. Exit 0 = both branches behave.
set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SCRIPT="$REPO/scripts/cloud-bootstrap.sh"
PROJECT_DIR="$REPO"
log()         { printf '[test] %s\n' "$*"; }
persist_env() { :; }

# Pull the real function definition out of the bootstrap (def line .. closing brace).
eval "$(awk '/^install_cyclonedds_config\(\) \{/{f=1} f{print} f&&/^\}/{exit}' "$SCRIPT")"

fail=0
tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT

printf '0x1009' > "$tmp/on"
unset CYCLONEDDS_URI
LO_FLAGS_FILE="$tmp/on" install_cyclonedds_config >/dev/null
if [ "${CYCLONEDDS_URI:-}" = "file://$PROJECT_DIR/config/cyclonedds-container.xml" ]; then
    echo "PASS export-path: CYCLONEDDS_URI exported when lo has multicast"
else
    echo "FAIL export-path: URI=${CYCLONEDDS_URI:-<unset>}"; fail=1
fi

printf '0x9' > "$tmp/off"
unset CYCLONEDDS_URI
LO_FLAGS_FILE="$tmp/off" install_cyclonedds_config >/dev/null
if [ -z "${CYCLONEDDS_URI:-}" ]; then
    echo "PASS error-path: URI NOT exported when lo lacks multicast (default discovery kept)"
else
    echo "FAIL error-path: exported a broken URI=${CYCLONEDDS_URI}"; fail=1
fi

[ "$fail" = 0 ] && echo "OK" || echo "FAILURES"
exit "$fail"
