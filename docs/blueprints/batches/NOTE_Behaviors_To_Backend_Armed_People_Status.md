<!--STATUS
state: LIVE — behaviors → backend: a second AMENDMENT note to a dispatched frame (rule 1: neither the frame nor note 1 is edited).
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §5h AW-21 (R-2027) OWNS it, with the redrawn decision graph's first branch.
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Armed_People.md — the frame (B-7…B-14).
  - docs/blueprints/batches/NOTE_Behaviors_To_Backend_Armed_People_Amended.md — note 1 (B-7 per observer, R-2026).
-->

# NOTE 2 — behaviors → backend: the armed rule keeps STATUS for combatants *(R-2027, AW-21)*

**B-7 again, one addition.** Note 1 made `Armament` judge per observer (what he can identify). ⚠ Routed through the 27 readers, that would
have lowered an enemy soldier at night to "unidentified" and moved every military scenario's threat ranking and the Sentry threshold. The
user ruled (*"approved, add AW-21 to the design"*, [Civilians §5h](../../DESIGN_Civilians_And_Crowd_Control.md) AW-21):

| target | `Armament` answers |
|---|---|
| an enemy **combatant** — Hostile and NOT of the `Civilian` class (`EntityClassTable` 3.1.*.2) | **armed by status**, exactly as `ThreatDanger` reads it today — unless out of the fight (`IsOutOfTheFight`) or laid down (B-10) |
| the `Civilian` class of any side, and neutral or unknown targets | the per-observer judgement of note 1 |

**Acceptance (added to B-7's):** every existing `ThreatDanger` / threat-ranking rail stays green with no expectation changed; an enemy
rifleman at 300 m at night still reads danger 1; a Hostile-flipped civilian holding only stones still reads unarmed.
