<!--STATUS
state: LIVE — for exp/agg-backend, exp/agg-behaviors, exp/agg-ui
updated: 2026-11-17
current-answer: the whole note
-->

# NOTE — coordinator → the three agg lanes: MERGE EACH OTHER (and coordinator) before every piece of work (R-4286)

🔒 User, `2026-11-17`: *"Are the agg branches merging each other? If not tell them please"*.

📐 **Measured `2026-11-17` 08:00:** everyone merges agg-backend, nobody merges the other two.

| branch | lacks | last merged it |
|---|---|---|
| exp/agg-backend | 55 commits of agg-behaviors · 25 of agg-ui | never · never |
| exp/agg-behaviors | 0 of agg-backend · 25 of agg-ui | `11-17 00:26` · never |
| exp/agg-ui | 80 of agg-backend · 135 of agg-behaviors | `11-16 18:49` · never |

⇒ the three lanes are building against three different worlds (agg-ui's panels against an agg-backend 80 commits old; agg-backend's
manager without the behaviours that drive it). The merge at the end will be the first time they meet — the worst moment.

⭐ **The rule from now on** (CLAUDE.md "Syncing"): at the START of each piece of work, and again before its final commit,
```bash
git fetch origin coordinator exp/agg-backend exp/agg-behaviors exp/agg-ui
git merge origin/coordinator origin/exp/agg-backend origin/exp/agg-behaviors origin/exp/agg-ui   # your own is a no-op
```
merge (never rebase), restore the test projects you will run (the new-dependency trap), name the shas in your started note, and
re-run your agg scenarios once after the first catch-up merge. Conflicts are mostly neighbouring lines in shared registration lists —
keep both sides. ⭐ Your own notes to each other keep their shape.
