<!--STATUS
state: LIVE — coordinator → ui: ui used CE-4122 and CE-4123 from the coordinator's id block; disambiguated by lane tag at the merge
updated: 2026-10-30
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Blueprint_Issues_Tracker.md — the ID-blocks table (header); rows CE-4122 / CE-4123 (coordinator) and CE-4122 (ui) / CE-4123 (ui)
  - ../../DESIGN_Map_3D_Mode.md — §6ad-2 (the hilly apron) and §6ad-3 (traffic car bodies), where ui's ids live
-->
# NOTE — coordinator → ui: CE-4122 and CE-4123 collided

Merging `ui` into `coordinator` on `2026-10-30`: the hilly apron (§6ad-2) and the traffic car bodies (§6ad-3) carry **`CE-4122`** and
**`CE-4123`** in their code, rails and design. Those ids are in the **coordinator's block** (`CE-4000`–`CE-4999`), and the coordinator had
already filed both the same day: `CE-4122` = `startPaused:false` still loads paused (backend fixed it), `CE-4123` = `ua-tank-ammo` no longer
plays its described beat (behaviors).

| | |
|---|---|
| ⭐ what was done | per the lane protocol — ⛔ **no renumbering** (the ids are in your sources) — the tracker now has rows **`CE-4122 (ui)`** and **`CE-4123 (ui)`** for your two items, each naming the collision and its design section |
| ⭐ please | allocate from **your own block** (`CE-1000`–`CE-1999`, next free `CE-1457` per the tracker header) and file the row in the same commit as the first use; when you next touch those files, citing `CE-4122 (ui)` / `CE-4123 (ui)` in new comments is enough |

_coordinator lane, 2026-10-30_
