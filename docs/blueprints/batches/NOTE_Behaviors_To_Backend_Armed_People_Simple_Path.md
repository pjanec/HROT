<!--STATUS
state: LIVE — behaviors → backend: a fourth note to the armed-people frame (rule 1: the frame and notes 1–3 are not edited).
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §5h AW-24 (R-2029) OWNS it; §5c `ua-armed-status` is its guard rail.
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Armed_People.md — the frame (B-7…B-14); notes 1–3 amend B-7 and add B-15, B-16.
-->

# NOTE 4 — behaviors → backend: the simple path stays simple *(R-2029, AW-24)*

The user: *"I would like to prevent overcomplicated decisions where they are not necessary."* ⇒ a soldier against an enemy combatant must
cost one status lookup and nothing more ([Civilians §5h](../../DESIGN_Civilians_And_Crowd_Control.md) AW-24). Four bounds on items you hold:

| item | bound | acceptance added |
|---|---|---|
| **B-7** (the armed rule) | the status branch (note 2) comes FIRST and returns at once for a combatant; an item's `Appearance` (note 1) DEFAULTS from its weapon category — only odd items set it by hand | ⭐ a **counter rail**: in a scenario with no `Civilian`-class entity the conduct path (identification, intent) is entered **zero** times |
| **B-8** (the hands mount) | the weapon brain never CHOOSES the hands mount while it is empty or holds only non-lethal rounds (`WeaponChoice`) | a rifleman with an empty hands mount fires his rifle exactly as before (a rail) |
| **B-9** (the pistol) | only on the types that should carry one — not every person type | re-run the soldier demos with their expectations unchanged: `ua-threat-ranking`, `ua-posture`, `ua-universal-soldier`, `bt-window-duel` (live + twins); a deviation is a finding, not a re-baseline |
| **all of B-7…B-16** | ⭐ the guard rail `ua-armed-status` (§5c) stays green from your first commit on | name it in every gate table |
