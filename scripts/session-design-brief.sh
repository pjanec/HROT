#!/usr/bin/env bash
# Fires on SessionStart (startup | resume | compact).
#
# Compaction destroys design context while leaving conclusions behind, so a session
# resumes confident and wrong. This puts the canon back in front of the model without
# anyone having to ask -- and then REQUIRES a written brief, because injecting text
# proves it arrived, not that it was engaged with.
cd "$(dirname "$0")/.." || exit 0

echo "=============================================================="
echo " DESIGN CANON -- re-read before answering any design question."
echo " Code answers 'how it IS'. It can never answer 'how it was MEANT to be'."
echo "=============================================================="
echo
cat docs/blueprints/RULINGS.md 2>/dev/null
echo
echo "=============================================================="
echo " WHAT MOVED RECENTLY (newer overrules older)"
echo "=============================================================="
python3 scripts/design-digest.py --days 7 2>/dev/null | head -110
echo
python3 scripts/rulings-check.py 2>/dev/null | tail -10

# --- The forcing function -------------------------------------------------
# Three rulings drawn at random. Reciting the ledger back is not the test; the
# test is JOINING these to the work in hand, which is the exact step that failed
# on 2026-08-17 (four times, each with the ruling sitting unread in the corpus).
echo
echo "=============================================================="
echo " REQUIRED: your FIRST reply this session must open with this block"
echo "=============================================================="
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
grep -oE '^\| (⭐|⚠|⛔|🔴| )*\*\*R-[0-9]+[a-z]?\*\*' docs/blueprints/RULINGS.md 2>/dev/null \
  | grep -oE 'R-[0-9]+[a-z]?' | sort -u | shuf -n 3 | sed 's/^/  - /'
echo
echo "If you cannot fill a line, SAY SO rather than guessing -- an empty line is a"
echo "finding about the ledger, not something to paper over."
