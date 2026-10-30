<!--STATUS
state: LIVE — RE-STAMPED 2026-10-30 before backend started (rule 1a): R-4100 priority — coordinator → backend: CE-4118, a Live recording loses its combat events (found filming, 2026-10-30)
updated: 2026-10-30
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Blueprint_Issues_Tracker.md — CE-4118 (the full measurement)
  - ../../PLAN_Feature_Films.md — §1c films from recordings, §1d the defects filming found (this one blocks every combat clip)
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — the recording and its frame index
  - ../../RUNBOOK_Filming.md — §0b the exact sequence that reproduces it
-->
# NOTE — coordinator → backend: a Live recording loses its combat events (CE-4118)

🔒 **User, `2026-10-30`:** *"I would like to return back to filming the scenarios … now, when we can use the recording of the live scenario
first, and then analyzing it in the replay to find out the exact time of those events, we can take much better clips"* · on this finding:
*"blasts are consequences of firing so without fire no blast possible"* · *"Of course write bugs notes for ui and backend"*.

⭐⭐ **Priority — ahead of everything else queued for you** (user, `2026-10-30`, R-4100: *"If the block the filming we will solve them with priority"*). ⭐ **Why it blocks:** films are now cut from Live recordings (PLAN_Feature_Films §1c). Every combat clip cuts on a shot, a hit or a blast;
without them in the recording, no combat clip can be made (F-45 and F-40 of the pilot are blocked). It also hides combat from every AAR /
DAR timeline.

## What was measured — `coordinator@2fc2aa164`, `-m editor` under Xvfb, RUNBOOK §0b's sequence

| recording | sim | `combat.shot` · `hit` · `blast` in its index | yet |
|---|---|---|---|
| `ua-tank-ammo` (Live) | 45 s | 0 · 0 · 0 | the preview scouts had the M1 firing at 8.9 s and 17.7 s |
| `ua-weapon-choice` (Live) | 25 s | 0 · 0 · 0 | ⭐ the T-72's `Health` 2500 → 0, with `HitMarks`, `PartDamage`, `UnderFire` — the TOW killed it |
| `ua-air-strike` (Live) | 40 s | 0 · 0 · **1** | the bomb's burst at 33.12 s is there, its release is not |
| `ua-weapon-choice` (**Preview**, same process, minutes later) | 16 s | **4 · 4 · 4** | the editor's live index |

⇒ the run fights; the combat events do not reach the Live recording (or its index). ⚠ **Lead, not a cause:** at the start of each Live run the
editor logs **564** `EventAccumulator reader N fell behind the history's bound (256 slots / 65536 events) and EXPIRED — events up to frame 3548
were dropped`. If the recorder's or the index's reader is among them, exactly these events vanish.

## What to do

| # | |
|---|---|
| 1 | ⭐ measure first: which reader writes combat events into the recording, and whether it is one of the expired ones; Preview vs Live — what differs in who reads the event bus |
| 2 | fix so a Live recording carries the same combat events as the run (every blast with its shot) |
| 3 | a rail in the recorder's own suite: a Live load of a firing scenario ⇒ `combat.shot > 0`, and every `combat.blast` has its shot |
| 4 | fold the cause into the owning design (the recorder / Replay Time Axis) |

Also seen, for you to rule on: the Live load came up **paused** (`isPaused: true`, sim 0) and needed `POST /sim/play`, where RUNBOOK §0b says
it runs — is that intended (AQ108's armed-mode ▶), or a change? The runbook now says to send play.

Report with the gate contract (CLAUDE.md, Rule 8); the coordinator re-films F-45 and F-40 when this closes.

_coordinator lane, 2026-10-30_
