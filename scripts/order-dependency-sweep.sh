#!/bin/bash
# ─────────────────────────────────────────────────────────────────────────────
# Batch 52 §1.4 — find tests that pass only because of what else ran first.
#
# ⭐ The instrument. The full Blueprints suite is green; this runs every test CLASS
#    ALONE, so any failure here is an ORDER DEPENDENCY, not a broken test.
#
# ⛔ Class granularity UNDER-REPORTS, by construction. Batch 52 measured
#    `Stage8Tests.Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb` green when its own
#    class ran and red when the single test did — a sibling in the same class had
#    loaded the assembly first. Per-test would catch that, and costs ~5 hours for
#    3518 tests. ⇒ run this first, then isolate per test inside anything it names,
#    and inside any class you have reason to suspect.
#
# Usage:  scripts/order-dependency-sweep.sh [output-file]
# Cost:   ~50 min for 370 classes. Needs a prior `dotnet build`; runs --no-build.
# ─────────────────────────────────────────────────────────────────────────────
set -u

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj"
OUT="${1:-$ROOT/order-dependency-sweep.txt}"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

echo "Enumerating test classes…"
dotnet test "$PROJ" --no-build --list-tests 2>/dev/null \
  | sed -n '/The following Tests are available/,$p' | tail -n +2 \
  | sed 's/^ *//; s/(.*//; s/\.[^.]*$//' | sort -u > "$WORK/classes.txt"

total=$(wc -l < "$WORK/classes.txt")
echo "$total classes. Running each alone…"
: > "$OUT"
n=0
while read -r cls; do
  n=$((n + 1))
  printf '\r%d/%d %-70.70s' "$n" "$total" "$cls" >&2
  res=$(timeout 300 dotnet test "$PROJ" --no-build --filter "FullyQualifiedName~$cls" 2>&1 \
        | grep -E 'Failed!|\[FAIL\]')          # ⚠ keep \[FAIL\] — without it a failure loses its name
  if [ -n "$res" ]; then
    { echo "### $cls"; echo "$res"; } >> "$OUT"
    printf '\n  ORDER-DEPENDENT: %s\n' "$cls" >&2
  fi
done < "$WORK/classes.txt"

printf '\nDone. Findings in %s\n' "$OUT" >&2
grep -c '^###' "$OUT" 2>/dev/null || true
