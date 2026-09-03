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
# The canon helps BOTH sessions. The written BRIEF is a coordinator obligation:
# it exists so the user can check that the coordinator re-learned the design
# after a compaction. An implementation session's first move is the rule-1b
# started-marker, not a brief -- and on 2026-08-18 one of them dutifully wrote a
# brief instead of starting Batch 84, because this hook did not distinguish.
#
# 2026-09-03: this was "...-gm0akp", which CLAUDE.md's lane table marks STALE and
# SUPERSEDED (user, 2026-08-26 -- the live coordinator lane is -6sr5ld). Harmless on
# an implementation branch (both names differ from it), but it would have told the
# real coordinator to skip its own brief.
COORDINATOR_BRANCH="claude/blueprint-authoring-status-6sr5ld"
CURRENT_BRANCH="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo unknown)"

# 2026-08-21 (user: "lets continue without the coordinator, it just slows us down"):
# the TIME lane now carries BOTH roles on its own branch, so a pure branch-name test
# would tell it to skip the brief it is now obliged to write. A branch that owns both
# roles declares itself here. Anything not listed stays implementation-only.
SELF_COORDINATED_BRANCHES="claude/time-system-refactor-batch-104-gp617x"
for b in $SELF_COORDINATED_BRANCHES; do
  [ "$CURRENT_BRANCH" = "$b" ] && COORDINATOR_BRANCH="$CURRENT_BRANCH"
done

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
   A report saying "MCP not connected so I used grep" is a MISS: the same
   binary has a CLI (codebase-memory-mcp cli <tool> --help).
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
if [ "$CURRENT_BRANCH" != "$COORDINATOR_BRANCH" ]; then
  echo "=============================================================="
  echo " IMPLEMENTATION LANE -- do NOT write a design brief"
  echo "=============================================================="
  echo "Branch: $CURRENT_BRANCH (the coordinator lane is $COORDINATOR_BRANCH)."
  echo
  echo "The canon above is context, not an assignment. The written DESIGN BRIEF"
  echo "is a COORDINATOR obligation. Your first move is your handoff's: rule 7"
  echo "(merge the coordinator branch), then rule 1b (push 'chore: started batch"
  echo "N at <sha>'), then build. If a document above contradicts your handoff:"
  echo "STOP AND REPORT -- do not adapt, do not revert; your scope is frozen at"
  echo "the dispatch sha."
  exit 0
fi
echo "=============================================================="
echo " REQUIRED: your FIRST reply this session OPENS with this block"
echo "=============================================================="
echo "Then answer whatever the user asked, IN THE SAME REPLY, below the block."
echo "/compact ends without an assistant turn, so this can only land on the next"
echo "thing the user types -- it is a HEADER on your reply, never a replacement."
echo
cat <<'FMT'
DESIGN BRIEF (post-compaction)
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
