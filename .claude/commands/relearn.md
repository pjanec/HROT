---
description: Re-learn the design canon and report a DESIGN BRIEF (same as after a compaction)
---

Re-learn the design decisions for this repo, then report what you learned.

## Do this, in order

1. Run `bash scripts/session-design-brief.sh` and read all of its output — the
   rulings ledger, the 7-day design digest, the probe verdict, and the three
   randomly-drawn ruling ids at the end.
2. Establish the current state yourself, do not assume it:
   - `git log --oneline -3` on this branch
   - `git fetch origin claude/hrot-implementation-j1jvin -q && git log --oneline -2 origin/claude/hrot-implementation-j1jvin`
   - whether a handoff is dispatched and whether a `chore: started batch N` marker exists
3. Reply with the `DESIGN BRIEF` block exactly as the hook prints it — and then
   answer whatever the user asked, in the same reply, below the block.

## The lines that matter

- **`spot-check`** — the three ids are drawn at random each run, so a canned
  answer cannot fit. Reciting the ledger row is not the test; **joining it to the
  work in hand** is.
- **`would have got wrong`** — the money line. State one concrete thing you would
  have got wrong without this pass, or write "nothing identified". Do not pad it;
  a vacuous line here means the pass was theatre, and the user should say so.
- If any line cannot be filled, **say so**. An empty line is a finding about the
  ledger, not something to paper over.

## Notes

- On the implementation branch this is a no-op by design: the brief is a
  coordinator obligation. Read the canon, then get on with the handoff.
- This is the same content the SessionStart hook injects after a compaction. Use
  it whenever the design context feels thin, not only after compacting.
