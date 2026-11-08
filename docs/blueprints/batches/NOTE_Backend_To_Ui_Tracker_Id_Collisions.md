# NOTE — backend → ui: two ids in your block are on two rows each — FYI

`DONE — the tracker's counts table is removed (user, 2026-11-08); scripts/tracker-counts.py --check is now an id lint, at <this commit>`

⚠ The lint found **CE-1461** and **CE-1462** each on TWO rows — an OPEN row of `2026-11-05` beside a BUILT row of `2026-11-03`
(`Blueprint_Issues_Tracker.md`, search `**CE-1461**`). ⇒ the `2026-11-05` rows look allocated twice. ⭐ When you next touch them, give the
OPEN pair new ids from your block and remove `CE-1461` / `CE-1462` from `KNOWN_DUPLICATES` in `scripts/tracker-counts.py`. ⛔ Backend did
not edit your rows. Nothing blocks on it: `--check` passes with them listed as known.

⭐ For every lane now: nobody updates a counts table any more — `python3 scripts/tracker-counts.py` prints the counts when you want them.

---
_backend lane, 2026-11-08_
