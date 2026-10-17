#!/usr/bin/env bash
# sync-sumosharp.sh — vendor SumoSharp's sources into FDP/ExtDeps/SumoSharp (read-only copy, pinned to a commit).
#
#   scripts/sync-sumosharp.sh <ref>        copy the projects HROT needs from upstream <ref> (branch, tag or sha)
#   scripts/sync-sumosharp.sh --check      report whether upstream's default branch has moved past UPSTREAM.md
#   scripts/sync-sumosharp.sh --verify     fail if the vendored copy differs from upstream at the recorded commit
#
# 📄 docs/blueprints/Architect_Question_93_Sumo_Traffic_Node.md §11 (V1–V3, R-4046/R-4048).
# ⛔ The vendored copy is READ-ONLY in HROT: a change HROT needs is made upstream, then synced here.
# ⚠ The scratch clone lives OUTSIDE the HROT tree — an MSBuild project inside it would import HROT's
#   Directory.Build.* files (measured: a build hook in the tree re-entered itself).
set -euo pipefail

REPO_URL="${SUMOSHARP_REPO:-https://github.com/pjanec/SumoSharp}"
ROOT="$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
DEST="$ROOT/FDP/ExtDeps/SumoSharp"
SCRATCH="${SUMOSHARP_SCRATCH:-${TMPDIR:-/tmp}/sumosharp-sync}"

# The projects HROT needs (AQ93 §11 V1) + what they reference: the shared polyfill and the package bundle
# project that Sim.Replication.Dds references.
PROJECTS=(Sim.Core Sim.Ingest Sim.Replication Sim.Replication.Dds Sim.Pedestrians Sim.LiveCity Sim.Evac Sim.Viewer.Motion Sim.Host Sim.Scene)
EXTRA=(src/Shared packaging/SumoSharp)

recorded_sha() { sed -n 's/^- commit: `\([0-9a-f]*\)`.*/\1/p' "$DEST/UPSTREAM.md" 2>/dev/null | head -1; }

fetch() { # $1 = ref ; leaves $SCRATCH checked out at it
  if [ ! -d "$SCRATCH/.git" ]; then
    git clone -q --filter=blob:none "$REPO_URL" "$SCRATCH"
  fi
  git -C "$SCRATCH" fetch -q origin "$1" 2>/dev/null || git -C "$SCRATCH" fetch -q origin
  git -C "$SCRATCH" checkout -q --detach FETCH_HEAD 2>/dev/null || git -C "$SCRATCH" checkout -q --detach "$1"
}

copy_dir() { # $1 = source dir, $2 = target dir — a copy without build outputs (no rsync needed)
  mkdir -p "$2"
  (cd "$1" && tar cf - --exclude=./bin --exclude=./obj --exclude='*/bin' --exclude='*/obj' .) | (cd "$2" && tar xf -)
}

copy_tree() { # $1 = source checkout, $2 = target dir
  local src="$1" dst="$2"
  rm -rf "$dst/src" "$dst/packaging"
  for p in "${PROJECTS[@]}"; do
    if [ -d "$src/src/$p" ]; then
      copy_dir "$src/src/$p" "$dst/src/$p"
    else
      echo "warning: upstream has no src/$p" >&2
    fi
  done
  for e in "${EXTRA[@]}"; do
    copy_dir "$src/$e" "$dst/$e"
  done
}

case "${1:-}" in
  --check)
    have="$(recorded_sha)"
    head="$(git ls-remote "$REPO_URL" HEAD | cut -f1)"
    if [ "${head:0:${#have}}" = "$have" ]; then echo "up to date ($have)"; else echo "upstream moved: vendored $have, upstream HEAD ${head:0:12}"; fi
    ;;
  --verify)
    have="$(recorded_sha)"; [ -n "$have" ] || { echo "no UPSTREAM.md commit recorded" >&2; exit 2; }
    fetch "$have"
    tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
    copy_tree "$SCRATCH" "$tmp"
    if diff -r -q "$tmp/src" "$DEST/src" >/dev/null && diff -r -q "$tmp/packaging" "$DEST/packaging" >/dev/null; then
      echo "verified: FDP/ExtDeps/SumoSharp equals upstream at $have"
    else
      echo "MISMATCH: the vendored copy differs from upstream at $have:" >&2
      diff -r -q "$tmp/src" "$DEST/src" >&2 || true; diff -r -q "$tmp/packaging" "$DEST/packaging" >&2 || true
      exit 1
    fi
    ;;
  ""|-h|--help)
    sed -n '2,12p' "$0"; exit 0 ;;
  *)
    ref="$1"
    fetch "$ref"
    sha="$(git -C "$SCRATCH" rev-parse HEAD)"
    mkdir -p "$DEST"
    copy_tree "$SCRATCH" "$DEST"
    cat > "$DEST/UPSTREAM.md" <<EOF
# SumoSharp — vendored sources (READ-ONLY in HROT)

- repository: $REPO_URL
- ref: \`$ref\`
- commit: \`$sha\`
- synced: $(date -u +%Y-%m-%d)
- projects: ${PROJECTS[*]} (+ ${EXTRA[*]})

⛔ Do not edit these files in HROT. Change SumoSharp upstream, then run \`scripts/sync-sumosharp.sh <ref>\`;
\`scripts/sync-sumosharp.sh --verify\` fails if this copy differs from upstream at the commit above.
📄 docs/blueprints/Architect_Question_93_Sumo_Traffic_Node.md §11 (R-4046, R-4048).
EOF
    echo "vendored $REPO_URL @ ${sha:0:12} ($ref) into FDP/ExtDeps/SumoSharp"
    ;;
esac
