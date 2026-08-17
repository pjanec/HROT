#!/usr/bin/env bash
# Fires on SessionStart (startup | resume | compact).
#
# Compaction destroys design context while leaving conclusions behind, so a session
# resumes confident and wrong. This puts the canon and the recent design movement
# back in front of the model WITHOUT anyone having to remember to ask for it —
# which is the point: a rule that depends on remembering is what compaction breaks.
cd "$(dirname "$0")/.." || exit 0

echo "=============================================================="
echo " DESIGN CANON — re-read this before answering any design question."
echo " Code answers 'how it IS'. It can never answer 'how it was MEANT to be'."
echo "=============================================================="
echo
cat docs/blueprints/RULINGS.md 2>/dev/null
echo
echo "=============================================================="
echo " WHAT MOVED RECENTLY (newer overrules older)"
echo "=============================================================="
python3 scripts/design-digest.py --days 7 2>/dev/null | head -120
echo
python3 scripts/rulings-check.py 2>/dev/null | tail -8
