<!--STATUS
state: LIVE — behaviors → ui (cc backend): the behaviour halves of the people-display gaps G1–G8.
updated: 2026-10-18
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_People_Display.md — (ui) the drawing, §8 the gaps
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §5n (G7, built next) and §5o (the other halves, frames with leans)
-->

# NOTE — behaviors → ui (cc backend): who WRITES what the people display draws

**Answering:** [HANDOFF_Ui_To_Backend_And_Behaviors_People_Display_Gaps.md](HANDOFF_Ui_To_Backend_And_Behaviors_People_Display_Gaps.md) ·
**Design:** [Civilians §5n, §5o](../../DESIGN_Civilians_And_Crowd_Control.md) · **Tracker:** [CE-2248…CE-2255](../Blueprint_Issues_Tracker.md)

| gap | behaviors row | what behaviors writes | waits on |
|---|---|---|---|
| G7 held item | `CE-2248` | phone out on 30 % of a Routine pause; `Throwable` while a rioter holds a stone (§5n) | nothing — next |
| G1 speech | `CE-2249` | warning (ladder), order (police line / charge), insult (angry protester), chant (the ring), talk (Routine) | backend `Utterance` |
| G2 mood | `CE-2250` | `CrowdSystem` writes `Demeanour`, quarters only, on change | backend `Demeanour` |
| G3 / G4 melee | `CE-2251` | `Melee.Strike` says Fist / Baton / Knife; the AW-19 knife attacker + `ua-armed-knife` | backend `StrikeNotification`, melee items |
| G5 taser / spray | `CE-2252` | chosen by range before the baton round (the ladder's weapon choice) | backend munitions |
| G6 toy gun | `CE-2253` | no behaviour; the `ua-armed-identify` demo | backend toy type |
| G8 shield | `CE-2254` | the shield man turns to the nearest thrower | backend shield + blocker |
| ⭐ found here | `CE-2255` | ⛔ nobody writes `GestureKind.Waving`; binoculars need a lookout `Observe` order | nothing |

- ⭐ Replicated enums ui draws that grow a member: behaviors will send a one-line note (you asked; noted).
- ⭐ New since your handoff, all on the wire already: `CivilianState.Attitude` is written (brain-local, like the rest of it), and the
  police order `BatonCharge` strikes protesters with `Melee.Strike` — the same swing G3 will carry.
