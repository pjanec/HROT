#!/usr/bin/env bash
# scenario-regression.sh — the NIGHTLY scenario regression (CE-4042, docs/DESIGN_Scenario_Regression.md).
#
#   scripts/scenario-regression.sh                    # every scenario with a checks.json
#   scripts/scenario-regression.sh ua-tank-ammo       # one (or a,b,c)
#   OUT=artifacts/regression NO_BUILD=1 scripts/scenario-regression.sh
#   TIER=cluster scripts/scenario-regression.sh farm-under-fire   # ⭐ RT-5: the CLUSTER tier (--mode all, no golden, replication sanity)
#   JOBS=2 scripts/scenario-regression.sh             # ⭐ R-2141: N runner processes side by side, the list split round-robin (NIGHTLY only)
#
# ⭐⭐ R-2141 (user, 2026-11-21): after a change run ONLY the changed features' own scenarios (docs/CAPABILITIES.md); the whole set runs
# overnight, several at once — "only overnight, not after every little change in every behavior".
#
# Each scenario runs in a FRESH editor process (headless, no window, no Xvfb) — a reload still leaks state (CE-3294) — judged
# by its checks.json and compared with its golden.json. Writes to $OUT: <scenario>.log, <scenario>.json, summary.json, junit.xml.
# Exit code 0 = every scenario passes. A CI job runs exactly this (template: docs/ci/scenario-regression.workflow.yml).
set -euo pipefail
cd "$(dirname "$0")/.."

WHAT="${1:-all}"
OUT="${OUT:-artifacts/scenario-regression/$(date -u +%Y%m%d-%H%M%S)}"
RUNNER=Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll

if [[ "${NO_BUILD:-0}" != "1" ]]; then
  # ⚠ restore first: --no-restore after a merge that adds a package runs without it (CLAUDE.md, the third stale-binary trap)
  dotnet restore Hrot/Runner/Hrot.ClusterRunner >/dev/null
  dotnet build Hrot/Runner/Hrot.ClusterRunner --no-restore -v q -nologo
fi

mkdir -p "$OUT"
echo "scenario regression: $WHAT → $OUT"
set +e
JOBS="${JOBS:-1}"
if [[ "$JOBS" -gt 1 ]]; then
  # each shard is its own runner process (each verify child already has a PRIVATE staging root — DESIGN_Scenario_Regression §8, so shards cannot collide)
  if [[ "$WHAT" == "all" ]]; then
    mapfile -t LIST < <(find scenarios -name checks.json -printf '%h\n' | xargs -n1 basename | sort -u)
  else
    IFS=',' read -r -a LIST <<< "$WHAT"
  fi
  pids=()
  for ((k = 0; k < JOBS; k++)); do
    part=""
    for ((i = k; i < ${#LIST[@]}; i += JOBS)); do part+="${part:+,}${LIST[$i]}"; done
    [[ -z "$part" ]] && continue
    OUT="$OUT/shard-$k" NO_BUILD=1 JOBS=1 TIER="${TIER:-editor}" "$0" "$part" > "$OUT/shard-$k.txt" 2>&1 &
    pids+=($!)
  done
  code=0
  for p in "${pids[@]}"; do wait "$p" || code=1; done
  set -e
  python3 - "$OUT" <<'PY'
import json, glob, os, sys
out = sys.argv[1]; rows = []
for f in sorted(glob.glob(os.path.join(out, "shard-*", "*.json"))):
    if os.path.basename(f) == "summary.json": continue
    try: d = json.load(open(f))
    except Exception: continue
    for s in d.get("scenarios", []):
        bad = [v["name"] for v in s.get("verdicts", []) if not v.get("ok")]
        rows.append({"scenario": os.path.basename(f)[:-5], "ok": not bad, "failing": bad})
json.dump({"scenarios": rows, "pass": sum(r["ok"] for r in rows), "total": len(rows)}, open(os.path.join(out, "summary.json"), "w"), indent=1)
print(f"[Verify] {sum(r['ok'] for r in rows)} of {len(rows)} pass (JOBS shards merged); failing: " + ", ".join(r["scenario"] for r in rows if not r["ok"]))
PY
  grep -h "\[Verify\] [0-9]* of" "$OUT"/shard-*.txt || true   # each shard's own count — a scenario that never loaded has no verdicts to merge
  echo "shards: $OUT/shard-*/junit.xml · summary: $OUT/summary.json · exit $code"
  exit $code
fi
if [[ "${TIER:-editor}" == "cluster" ]]; then
  # ⭐ RT-5 (R-4279) — one fresh --mode all cluster per scenario, judged over HTTP in sim time (it starts xvfb-run itself when there is no display)
  dotnet "$RUNNER" --mode all --verify "$WHAT" --verify-out "$OUT"
else
  dotnet "$RUNNER" --mode editor --headless --verify "$WHAT" --verify-out "$OUT"
fi
code=$?
set -e
echo "junit: $OUT/junit.xml · summary: $OUT/summary.json · exit $code"
exit $code
