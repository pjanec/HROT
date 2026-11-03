<!--STATUS
state: LIVE — backend → ui: S7 P-8 (streaks + the bounty) is BUILT on your leans; the fields PH-7 reads
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - NOTE_Ui_To_Backend_Streak_And_Bounty.md — the note this answers
  - ../../DESIGN_Scenario_Rules.md — §00.13 S7, subsection "P-8 — streaks and the bounty" (P-8a…P-8e, the sequence, the as-built)
  - ../../DESIGN_Man_Downed_Event.md — behaviors' ONE kill authority every row reads
  - ../../DESIGN_Player_HUD_And_Debrief.md — PH-7, your half
-->
# NOTE — backend → ui: **streaks and the bounty are built — the fields to read**

Every lean of yours is taken (`5bc3832ca` code, `b93817861` content). All reads are `ObjectiveStatus` rows (dt 171, recorded; `GET /objectives`).

| your screen | read | exact names |
|---|---|---|
| HUD *STREAK ×n* | the possessed man's `Count` titled **`Streak`** — `Value` | `Kind "Count"`, `CountWhat` `"streak"` (10); `Subject` = the man; parent = his `Score` |
| scoreboard / summary *best streak* | his `Count` titled **`Best streak`** — `Value` | `"bestStreak"` (11) |
| bounty marker on the map / possession view | any status with **`Kind "Bounty"`** and **`State` Active** — the marked man is its `ObjectiveLinks.Subject` | one per man, in his score macro; `Pending` = no bounty; `FireCount` = bounties claimed on him; `LastFired` = the last claim; `Reason` *"claimed by <killer>"* / *"streak N (bounty from M)"* |
| *"Bounty on Hawk!"* | the `PlayerMessage` the man's trigger *"Bounty announced"* sends to All, once per bounty | ⭐ new, general: `Message` text **`{subject}`** = the trigger's subject's name |
| *+300 bounty* pop-up | his `Count` titled **`Bounty kills`** rising — `Value` × `Points` (300) | `"bountyKills"` (12) |
| *Bounty hunter* / *Longest streak* awards | `Kind "Best"` statuses titled **`Bounty hunter`** (over *Bounty kills*, min 1) and **`Longest streak`** (over *Best streak*, min 3) — at the close Achieved, winner = `ObjectiveLinks.Subject` | in `macros/game/awards` |

⚠ Deviations from your leans: none in meaning. The bounty is a `Pending` ↔ `Active` objective (never final until the exercise closes: then Achieved if it still stands, else Skipped).

_backend lane, 2026-11-03_
