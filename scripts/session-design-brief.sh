#!/usr/bin/env bash
# Fires on SessionStart (startup | resume | compact).
#
# Compaction destroys design context while leaving conclusions behind, so a session
# resumes confident and wrong. This puts the canon back in front of the model without
# anyone having to ask -- and then REQUIRES a written brief, because injecting text
# proves it arrived, not that it was engaged with.
#
# ── 2026-09-03: THE HOOK WAS FIRING AND BEING THROWN AWAY ──────────────────────────
# Measured: this script emitted 66 KB (RULINGS.md 53 KB + a 110-line digest). The
# harness truncates a large hook payload to a ~2 KB preview and writes the rest to a
# file the model never opens. So RULE ZERO obligation 0 was mechanised and the
# mechanism failed SILENTLY at the delivery step -- the exact silent-default family
# this repo keeps finding: the control exists, the caller holds the value, it never
# arrives. The session then reasons from 2 KB while believing it read the canon.
#
# Two defences, because the cap is not ours to control:
#   1. The ACTION BLOCK is FIRST and small, so it survives even a 2 KB truncation.
#   2. Total output is held under BUDGET_NOTE below, so normally nothing is truncated.
# The ledger is therefore DIGESTED here, not cat'ed. A digest that ARRIVES beats a
# full file that is discarded -- and the action block tells you to Read the full file
# the moment a design question comes up, which is when the rows actually matter.
BUDGET_NOTE="target: <8 KB total; action block <1.5 KB so it survives truncation"
cd "$(dirname "$0")/.." || exit 0

LEDGER=docs/blueprints/RULINGS.md

# ── Which lane is this? ───────────────────────────────────────────────────────
# 2026-10-02 (user): lanes are PEERS -- "every lane can be a coordinator", and "keep
# design brief, but per lane". The brief used to be printed only on the branch named
# 'coordinator' and every other lane was told to skip it; now EVERY lane writes it,
# scoped to its own work (its own resume doc, its own in-flight batch).
# The harness may append a postfix to a session's branch, so the LANE is the branch
# name with any "<prefix>/" and trailing "-<postfix>" removed. HROT_LANE overrides it.
CURRENT_BRANCH="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo unknown)"
LANE="${HROT_LANE:-${CURRENT_BRANCH##*/}}"

# ══ ACTION BLOCK -- FIRST, SMALL, SURVIVES TRUNCATION ═════════════════════════
# These five are the ones measured to decay across compaction. Everything below
# this block is reference; this block is what to DO before the first tool call.
cat <<'ACT'
==============================================================
 BEFORE YOUR FIRST TOOL CALL -- the five that decay on compaction
==============================================================
1. GRAPH BEFORE GREP for any COMPLETE-SET or ABSENCE claim.
   grep answers "does X exist"; it CANNOT answer "what is the whole set".
   -> scripts/find.sh <pattern> [--glob '*.cs']   runs BOTH and diffs them.
   The graph's health is PROBED below -- read that line before you claim
   anything is unavailable. The binary is NOT on PATH: invoke it by its
   ABSOLUTE path (the probe prints it). A bare `codebase-memory-mcp ...`
   fails with "command not found" and that is NOT the graph being down.
2. INTENT IS IN THE DESIGN DOC, NOT THE CODE (R-129). Before touching OR
   REASONING ABOUT an existing feature, search docs/ then .dev/ BY TOPIC and
   read the owning DESIGN doc. Code says how it IS, never how it was MEANT.
3. NO LEAN WITHOUT A CLAIM TABLE (R-139). Each row cites file:line AND a
   design basis, or is marked assumed. NO ASSUMED ROW MAY BE LOAD-BEARING.
   "searched <where>, none found" is a complete answer; "not searched" is not.
4. READ YOUR LANE'S RESUME DOC after the ledger (R-135) -- its STATUS block's
   current-answer names the ONE section to start from.
5. BUILD THE AFFECTED PROJECT (~8 s), never the solution (~115 s), in a fix
   loop. Then --no-build for every run after. E2E is async, never a blocker.

FULL CANON: docs/blueprints/RULINGS.md -- Read it IN FULL when a design
question arises. Digested below because a 66 KB hook payload gets truncated
to ~2 KB and silently discarded (measured 2026-09-03).
ACT

# ══ GRAPH HEALTH -- MEASURED EVERY SESSION AND EVERY COMPACTION ═══════════════
# ⭐⭐⭐ WHY THIS EXISTS (user, 2026-09-12): "using codebase memory is critical because
#   your grep is frequently missing lots of important occurrences ... how to make sure
#   you are really following it and not forgetting it after every compaction?"
# ⛔⛔ The honest diagnosis: a rule that says "remember to use the graph" DECAYS. What
#   actually failed was STRUCTURAL and silent -- scripts/find.sh printed
#   "PARSE FAILED -- no JSON returned" on every call (it used the wrong CLI form), and a
#   session read that as "the graph is unavailable" and fell back to grep alone.
# ⇒ So this PROBES it instead of exhorting. A broken graph is now LOUD, at the one moment
#   that is guaranteed to be read: the session-start / post-compaction hook.
{
  CM_BIN="${CODEBASE_MEMORY_MCP_BIN:-}"
  [ -z "$CM_BIN" ] && [ -x /opt/codebase-memory-mcp/codebase-memory-mcp ] && CM_BIN=/opt/codebase-memory-mcp/codebase-memory-mcp
  [ -z "$CM_BIN" ] && [ -x "$HOME/.local/bin/codebase-memory-mcp" ] && CM_BIN="$HOME/.local/bin/codebase-memory-mcp"
  echo "=============================================================="
  echo " CODEBASE-MEMORY GRAPH -- probed just now, not assumed"
  echo "=============================================================="
  if [ -z "$CM_BIN" ]; then
    echo " STATUS: BINARY NOT FOUND -- run: bash scripts/cloud-bootstrap.sh"
    echo " Until then SAY SO in every inventory/absence claim you make."
  else
    echo " binary: $CM_BIN   (NOT on PATH -- always call it by this path)"
    CM_PROJ="$("$CM_BIN" cli --json list_projects 2>/dev/null | grep '^{' | tail -1 \
      | python3 -c 'import sys,json
try:
    d=json.loads(sys.stdin.read().strip())
    sc=d.get("structuredContent")
    if not sc and "content" in d:
        sc=json.loads("".join(c.get("text","") for c in d.get("content",[]) if isinstance(c,dict)))
    ps=(sc or {}).get("projects") or []
    print(ps[0].get("name","") if ps else "")
except Exception:
    print("")' 2>/dev/null)"
    if [ -z "$CM_PROJ" ]; then
      echo " STATUS: NO INDEXED PROJECT -- run:"
      echo "   $CM_BIN cli index_repository '{\"repo_path\":\"$PWD\"}'   (tens of seconds)"
    else
      echo " project: $CM_PROJ"
      echo " STATUS: OK -- use  scripts/find.sh <pattern> [--glob '*.cs']  for any"
      echo "   complete-set or absence claim. It runs the graph AND grep and prints"
      echo "   what each one MISSED. Neither half alone settles an exhaustive claim."
      echo " ⛔ check_index_coverage is NOT available via the CLI -- so say"
      echo "   'coverage not checked' in any negative claim rather than implying it was."
    fi
  fi
  echo
} 2>/dev/null

# ── The ledger, DIGESTED: section heads + row ids + a one-line headline ────────
echo
echo "=============================================================="
echo " LEDGER DIGEST -- ids and headlines only. Rows are NOT quotable."
echo "=============================================================="
python3 - "$LEDGER" <<'PY' 2>/dev/null
import re, sys
try:
    lines = open(sys.argv[1], encoding="utf-8").read().splitlines()
except OSError:
    print("  !! RULINGS.md unreadable -- say so in any claim made this session."); raise SystemExit
def strip(s):
    s = re.sub(r'\*\*|`|\*|📄|📐|📌|🔒|⇒', '', s)
    s = re.sub(r'\[([^\]]*)\]\([^)]*\)', r'\1', s)
    s = re.sub(r'[^\x00-\x7f]', '', s)
    return re.sub(r'\s+', ' ', s).strip(' -|')
n = 0
for ln in lines:
    if ln.startswith("## "):
        print("\n" + strip(ln[3:])[:78])
        continue
    m = re.match(r'^\|[^|]*\*\*(R-\d+[a-z]?|M-\d+)\*\*\s*\|(.*)$', ln)
    if m:
        n += 1
        # Take only the RULING cell -- the trailing "| source" column is noise here,
        # and the whole point is that you open the file rather than quote this line.
        print("  %-6s %s" % (m.group(1), strip(m.group(2).split("|")[0])[:88]))
print("\n  %d rows. NEVER quote a row from here -- open the file." % n)
print("  Section M rows are PERISHABLE: run the command, do not reuse the answer.")
PY

# ── What moved recently. Trimmed: the digest's own headlines carry the signal. ──
echo
echo "=============================================================="
echo " WHAT MOVED IN 7 DAYS (newer overrules older)"
echo "=============================================================="
# Names only. The 4-line excerpts cost ~600 B per document and cannot be complete at
# 60 changed docs anyway -- a LIST of what moved is both denser and more honest, and
# every entry is one Read away. (The excerpts are still there: run the script itself.)
python3 scripts/design-digest.py --days 7 2>/dev/null \
  | grep -E '^(DESIGN DIGEST|[0-9]{4}-[0-9]{2}-[0-9]{2}  )' | head -26
echo "  (excerpts: python3 scripts/design-digest.py --days 7)"
echo
python3 scripts/rulings-check.py 2>/dev/null | tail -3

# --- The forcing function -------------------------------------------------
# Three rulings drawn at random. Reciting the ledger back is not the test; the
# test is JOINING these to the work in hand, which is the exact step that failed
# on 2026-08-17 (four times, each with the ruling sitting unread in the corpus).
echo
echo "Lane: $LANE (branch $CURRENT_BRANCH) -- the brief is PER LANE: 'in flight' is THIS lane's"
echo "work, read from this lane's resume doc (docs/blueprints/RESUME_*.md), never another lane's."
echo "=============================================================="
echo " REQUIRED: your FIRST reply this session OPENS with this block"
echo "=============================================================="
echo "Then answer whatever the user asked, IN THE SAME REPLY, below the block."
echo "/compact ends without an assistant turn, so this can only land on the next"
echo "thing the user types -- it is a HEADER on your reply, never a replacement."
echo
cat <<'FMT'
DESIGN BRIEF (post-compaction) -- lane: <lane>
  ledger      : <N rulings, N/N probes verifying, staleness warnings on <files or none>>
  in flight   : <batch + the sha its scope is frozen at, or "nothing">
  constrains  : <ruling ids that BIND what I am about to do, one line each>
  moved lately: <any doc from the digest that changes it, with its date>
  spot-check  : <the three ruling ids below, in my own words, each joined to the work>
  would have got wrong: <one concrete thing, or "nothing identified" -- do not pad>
FMT
echo
echo "SPOT-CHECK these three (drawn at random, so a canned answer will not fit):"
grep -oE '^\| (⭐|⚠|⛔|🔴| )*\*\*R-[0-9]+[a-z]?\*\*' "$LEDGER" 2>/dev/null \
  | grep -oE 'R-[0-9]+[a-z]?' | sort -u | shuf -n 3 | sed 's/^/  - /'
echo
echo "If you cannot fill a line, SAY SO rather than guessing -- an empty line is a"
echo "finding about the ledger, not something to paper over."
