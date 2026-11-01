<!--STATUS
state: LIVE — coordinator → behaviors: the scenario defects the feature-film pass found (2026-10-30/31), each a tracker row; nothing was fixed
updated: 2026-10-31
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../PLAN_Feature_Films.md — §1 (each clip's described beat), §1d (what filming found, refilm conditions)
  - ../Blueprint_Issues_Tracker.md — rows CE-4123, CE-4124, CE-4132..CE-4137, CE-4139 (the measurements in full)
-->
# NOTE — coordinator → behaviors: what the feature-film pass found in the scenarios

The coordinator filmed every feature scenario from an editor Live recording (`2026-10-30/31`, PLAN_Feature_Films §1c): 50 clips made, 6
skipped. ⛔ No scenario or behaviour was changed — each finding is a tracker row with its measurement (times, counts, units, read from the
recording's timeline index in the Replay Browser), and a §1d row saying when the clip is refilmed.

⭐ **Each row says "measure first":** a difference from the description may be intended tuning — then the fix is the description (and the
clip list), not the behaviour. Please judge which, per row.

| row | scenario · clip | what the run does, against the description | lean |
|---|---|---|---|
| [CE-4123](../Blueprint_Issues_Tracker.md) | `ua-tank-ammo` · F-45 | the M1 needs **three** sabot rounds for the T-72; its HE comes ~50 s in, at the technical's **gunner**, never the truck | likely tuning — confirm the damage model, fix the description |
| [CE-4124](../Blueprint_Issues_Tracker.md) | `ua-civ-escalation` · F-13 | the police rifleman did not fire in a 28 s run — ⭐ **but the pass's longer run shows the rifle firing at ~30.5 s** | likely NOT a defect (the pilot run was too short) — one longer run to confirm, then close |
| [CE-4132](../Blueprint_Issues_Tracker.md) | `ua-civ-provocation` · F-07 | **one** protester riots (77 s); the "baton charge" is three rounds at him within 0.13 s at ~81 s; the other eleven stay Neutral | described: *some — not all — riot*, a ~12 s charge |
| [CE-4133](../Blueprint_Issues_Tracker.md) | `ua-civ-contagion` · F-05 | fear DOES spread (Cower at 30–88 m, Alarmed at 143 m) — but as a **crouch in place**; nobody runs; from ~30 s they drift **toward** the fight | described: *fear runs east down the street* |
| [CE-4134](../Blueprint_Issues_Tracker.md) | `ua-casualty-care` F-28 · `uc-request-chain-dry` F-34 · `bt-mortar-roof` F-37 | **nothing happens without the live check's action** (no casualty, no SPOT, no mortar mission) — clips skipped | put the orders in the files (PLAN §1 already asks) |
| [CE-4135](../Blueprint_Issues_Tracker.md) | `ua-indirect-fire` · F-58 | only `Mortar Gunner 2` boards; tubes 1 and 3 are **never manned** — one tube, not a section | described: three 81 mm tubes |
| [CE-4136](../Blueprint_Issues_Tracker.md) | `ua-javelin-lock` F-42 · `ua-rpg-window` F-43 · `uc-convoy-hit-ifv` F-47 · `bt-window-duel` F-20 | **two runs, two stories**: the Javelin lock denied / not denied (and a hit) · 1 vs 3 RPG rockets · the squad dismounts / stays aboard · 6 vs 4 shots | runs that flip the described beat — the most valuable to look at |
| [CE-4137](../Blueprint_Issues_Tracker.md) | `cc-platoon-bound` · F-31 | the squads' roles are **swapped** (Alpha takes Bravo's support point, Bravo moves to Alpha's); the bound takes ~140 s | swap back, or fix the description |
| [CE-4139](../Blueprint_Issues_Tracker.md) | `ld-sentry-surprise-360` · F-22 | the sentry's "turn" is a 22° adjustment at 2–8 s, then he stands still while the man walks up behind | described: he turns to face him as he arrives |

⭐ **Also good news:** the civilian regressions **CE-4039 / CE-4040 no longer hold** — F-03 (shelter), F-04 (cower) and F-06 (gathering) play
their beats and were filmed.

⭐ **To look yourself:** the clip ids above are in PLAN_Feature_Films §1; each takes's anchors are in `scenarios/<s>/takes/main.take.json`
(a second take file for `ua-air-attack` / `ua-indirect-fire`). The MP4s are with the user (not in git).

_coordinator lane, 2026-10-31_
