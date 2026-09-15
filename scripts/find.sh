#!/usr/bin/env bash
# find.sh <pattern> [--glob '*.cs'] [--limit N]
#
# Runs the codebase-memory graph search AND grep, side by side, and prints the
# files each one found that the other missed.
#
# WHY THIS EXISTS -- it is a cost fix, not a style preference.
#   CLAUDE.md has said "codebase-memory FIRST" since 2026-08-18 and it keeps being
#   skipped. The reason is not the rule, it is the PRICE: Grep is one tool call,
#   the graph CLI is a Bash call with JSON quoting. The cheaper thing to type was
#   the wrong thing, and no amount of emphasis beats that asymmetry.
#   This makes the cheapest thing to type the correct thing.
#
#   grep answers "does X exist". It CANNOT answer "what is the complete set of X",
#   so a NEGATIVE or EXHAUSTIVE claim needs both. Measured 2026-09-03: an exhaustive
#   claim ("no production construction of StrideNodeBootstrapper") was made from a
#   grep for 'new StrideNodeBootstrapper' -- the right question, the wrong shape, so
#   Stride/HrotStrideApp.Game/StrideHrotGame.cs never appeared. A search_code on the
#   BARE NAME returned it and the conclusion flipped. That is the failure this script
#   makes cheap to avoid: it always searches the bare pattern through both engines.
#
#   Conversely the graph is not sufficient either: it under-reports C# interface
#   dispatch (3 callers vs 9+) and does not model field reads/writes at all. That is
#   exactly why this prints BOTH and shows you where they disagree.
set -uo pipefail
cd "$(dirname "$0")/.." || exit 1

PATTERN=""; GLOB=""; LIMIT=200
while [ $# -gt 0 ]; do
  case "$1" in
    --glob)  GLOB="$2";  shift 2 ;;
    --limit) LIMIT="$2"; shift 2 ;;
    -h|--help)
      sed -n '2,4p' "$0" | sed 's/^# \?//'
      exit 0 ;;
    *) if [ -z "$PATTERN" ]; then PATTERN="$1"; else GLOB="$1"; fi; shift ;;
  esac
done
[ -z "$PATTERN" ] && { echo "usage: scripts/find.sh <pattern> [--glob '*.cs'] [--limit N]" >&2; exit 2; }

# ── Locate the binary (CLAUDE.md lists three homes; honour all of them) ──────────
BIN="${CODEBASE_MEMORY_MCP_BIN:-}"
[ -z "$BIN" ] && BIN="$(command -v codebase-memory-mcp 2>/dev/null || true)"
[ -z "$BIN" ] && [ -x /opt/codebase-memory-mcp/codebase-memory-mcp ] && BIN=/opt/codebase-memory-mcp/codebase-memory-mcp
[ -z "$BIN" ] && [ -x "$HOME/.local/bin/codebase-memory-mcp" ] && BIN="$HOME/.local/bin/codebase-memory-mcp"
# A CODEBASE_MEMORY_MCP_BIN pointing at nothing must degrade to "UNAVAILABLE", not to
# a silent parse failure that reads like an empty result set -- the whole disease.
[ -n "$BIN" ] && [ ! -x "$BIN" ] && BIN=""

# ── grep half -- always runs, even if the graph is unavailable ───────────────────
GREP_ARGS=(-rn --binary-files=without-match
           --exclude-dir=.git --exclude-dir=obj --exclude-dir=bin
           --exclude-dir=node_modules --exclude-dir=.vs)
[ -n "$GLOB" ] && GREP_ARGS+=(--include="$GLOB")
GREP_OUT="$(grep "${GREP_ARGS[@]}" -- "$PATTERN" . 2>/dev/null | sed 's|^\./||')"
GREP_FILES="$(printf '%s\n' "$GREP_OUT" | grep -v '^$' | cut -d: -f1 | sort -u)"
GREP_LINES="$(printf '%s\n' "$GREP_OUT" | grep -c . || true)"
GREP_NFILES="$(printf '%s\n' "$GREP_FILES" | grep -c . || true)"

# ── graph half ──────────────────────────────────────────────────────────────────
GRAPH_FILES=""; GRAPH_NOTE=""
if [ -z "$BIN" ]; then
  GRAPH_NOTE="UNAVAILABLE -- binary not found. Say so explicitly in any exhaustive claim."
else
  PROJ="$("$BIN" cli list_projects 2>/dev/null | python3 -c '
import sys, json
try:
    d = json.loads(sys.stdin.read().strip().splitlines()[-1])
    print(d["projects"][0]["name"] if d.get("projects") else "")
except Exception:
    print("")' 2>/dev/null)"
  if [ -z "$PROJ" ]; then
    GRAPH_NOTE="NO INDEXED PROJECT -- run: $BIN cli index_repository --repo-path \"$PWD\"  (tens of seconds)"
  else
    # ⭐⭐⭐ MEASURED 2026-09-12: this build of the CLI prints a HUMAN-READABLE TABLE, not
    #   JSON, and the flag-form (--project/--pattern) is not what it parses. The JSON-args
    #   form + the GLOBAL --json flag is what produces machine-readable output.
    # ⛔⛔ The previous invocation produced "PARSE FAILED -- no JSON returned" on EVERY call,
    #   for weeks, and a session read that as "the graph is unavailable" and silently used
    #   grep alone. That is the exact failure this script exists to prevent, so the parser
    #   below now understands BOTH shapes and the note says which one it got.
    CM_JSON="{\"project\":\"$PROJ\",\"pattern\":\"$PATTERN\",\"limit\":$LIMIT"
    [ -n "$GLOB" ] && CM_JSON="$CM_JSON,\"file_pattern\":\"$GLOB\""
    CM_JSON="$CM_JSON}"
    ARGS=(cli --json search_code "$CM_JSON")
    # The binary logs level=... lines to stdout alongside the JSON, and an error is
    # itself JSON -- so take the last {...} line and let python classify it.
    PARSED="$("$BIN" "${ARGS[@]}" 2>/dev/null | grep '^{' | tail -1 | python3 -c '
import sys, json
raw = sys.stdin.read().strip()
if not raw:
    print("NOTE\tPARSE FAILED -- no JSON returned"); raise SystemExit
try:
    d = json.loads(raw)
except Exception as e:
    print("NOTE\tPARSE FAILED -- %s" % e); raise SystemExit
if d.get("error"):
    print("NOTE\t%s -- %s" % (d["error"], d.get("hint", ""))); raise SystemExit
# ⭐ SHAPE B (measured 2026-09-12): the MCP envelope {"content":[{"type":"text","text":...}]}
#   carrying a TEXT TABLE. Unwrap it into the same fields shape A uses, so one parser below
#   handles both and neither shape can silently look like an absence.
if "content" in d and "results" not in d:
    txt = "".join(c.get("text", "") for c in d.get("content", []) if isinstance(c, dict))
    rows, tg, tr = [], None, None
    for line in txt.splitlines():
        st = line.strip()
        if st.startswith("total_grep_matches:"):
            try: tg = int(st.split(":", 1)[1].strip())
            except Exception: pass
            continue
        if st.startswith("total_results:"):
            try: tr = int(st.split(":", 1)[1].strip())
            except Exception: pass
            continue
        if not line.startswith("  ") or st.startswith("(") or ":" in st.split(" ")[0]:
            continue
        parts = st.split()
        # cols: qn label file lines matches in out  -> the path is field 3
        if len(parts) >= 3:
            rows.append({"file": parts[2]})
    d = {"results": rows, "total_grep_matches": tg, "total_results": tr}
# raw_matches[].file is NOT always a path -- on markdown hits it can carry a prose
# fragment, which then prints as a bogus "file the graph found that grep missed".
# Keep only values that actually look like repo-relative paths.
def looks_like_path(p):
    return isinstance(p, str) and p and " " not in p and ("/" in p or "." in p)
files = sorted({r.get("file", "") for r in d.get("results", []) if looks_like_path(r.get("file"))}
               | {r.get("file", "") for r in d.get("raw_matches", []) if looks_like_path(r.get("file"))})
tg, tr = d.get("total_grep_matches"), d.get("total_results")
trunc = "  TRUNCATED-raise-limit" if isinstance(tr, int) and isinstance(tg, int) and tr < tg else ""
print("NOTE\tmatches=%s results=%s%s" % (tg, tr, trunc))
for f in files:
    print("FILE\t%s" % f)
' 2>/dev/null)"
    GRAPH_NOTE="$(printf '%s\n' "$PARSED" | awk -F'\t' '$1=="NOTE"{print $2; exit}')"
    GRAPH_FILES="$(printf '%s\n' "$PARSED" | awk -F'\t' '$1=="FILE"{print $2}')"
    [ -z "$GRAPH_NOTE" ] && GRAPH_NOTE="no output from search_code"
  fi
fi
GRAPH_FILES="$(printf '%s\n' "$GRAPH_FILES" | grep -v '^$' | sort -u)"
GRAPH_NFILES="$(printf '%s\n' "$GRAPH_FILES" | grep -c . || true)"

# ── report ──────────────────────────────────────────────────────────────────────
echo "pattern: $PATTERN${GLOB:+   glob: $GLOB}"
echo "-----------------------------------------------------------------------"
printf '  graph (search_code) : %-5s files   %s\n' "$GRAPH_NFILES" "$GRAPH_NOTE"
printf '  grep                : %-5s files   %s lines\n' "$GREP_NFILES" "$GREP_LINES"
echo "-----------------------------------------------------------------------"

ONLY_GRAPH="$(comm -23 <(printf '%s\n' "$GRAPH_FILES") <(printf '%s\n' "$GREP_FILES") | grep -v '^$' || true)"
ONLY_GREP="$(comm -13 <(printf '%s\n' "$GRAPH_FILES") <(printf '%s\n' "$GREP_FILES") | grep -v '^$' || true)"

if [ -n "$ONLY_GRAPH" ]; then
  echo "ONLY THE GRAPH FOUND THESE -- grep would have missed them:"
  printf '%s\n' "$ONLY_GRAPH" | sed 's/^/  + /'
  echo
fi
if [ -n "$ONLY_GREP" ]; then
  echo "ONLY GREP FOUND THESE -- outside the index, or not modelled by the graph:"
  printf '%s\n' "$ONLY_GREP" | sed 's/^/  - /'
  echo
fi
[ -z "$ONLY_GRAPH$ONLY_GREP" ] && echo "The two agree on the file set." && echo

echo "GREP HITS (file:line):"
printf '%s\n' "$GREP_OUT" | grep -v '^$' | head -80 | sed 's/^/  /'
[ "$GREP_LINES" -gt 80 ] && echo "  ... $((GREP_LINES - 80)) more lines (narrow with --glob)"

echo
echo "REMINDER: neither half alone settles an exhaustive or negative claim."
echo "  complete SET of a symbol   -> search_graph --label Interface/Class (not this script)"
echo "  who implements / overrides -> roslyn_find_implementations (graph under-reports dispatch)"
echo "  is a text hit REALLY this symbol -> roslyn_find_references"
