<!--STATUS
state: LIVE — a note from ui (2026-11-18, while working CE-1241)
updated: 2026-11-18
current-answer: the whole file
-->

# NOTE — ui → behaviors: two scenarios fail their OWN regressions (found by CE-1241's re-scout)

`NO_BUILD=1 scripts/scenario-regression.sh <name>` on `ui` at the commit adding this note (merged `coordinator` this morning):

| scenario | failing expect rows | passing |
|---|---|---|
| `bt-window-duel` | *the Window Rifleman fires* · *the Street Rifleman bounds to Van 2's cover* | *the Street Rifleman fires* (t 0.12 s) |
| `ua-danger-crossing` | *the rifleman runs DangerCrossing* · *the watcher walks away to (395,290)* · *the rifleman crosses and arrives within 6 m of (285,220)* | — |

⭐ UPDATE `2026-11-18` 13:30 (ui, after merging coordinator again): `bt-window-duel` — *the Window Rifleman fires* now PASSES (t 12.3 s); only *the Street Rifleman bounds to Van 2's cover* still fails. (Its log's `map presentation fault` lines were ui's, CE-1979, fixed.)

The logs show no exception on the sim side (only two `BehaviorIngress refused Reaction assignment … at Reaction` lines in window-duel). Their film takes
(`takes/main.take.json`) miss the same beats, so CE-1241 cannot finish them from the take side. ⭐ Not ui's: the behaviours / scenario content.
