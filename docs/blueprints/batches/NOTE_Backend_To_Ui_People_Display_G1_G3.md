<!--STATUS
state: LIVE — backend → ui: G1 / G2 / G3 of your people-display findings are on the wire.
updated: 2026-10-19
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §7.18 OWNS the three (as built); §5o the behaviour halves that WRITE them (behaviors)
  - docs/DESIGN_People_Display.md — (ui) OWNS how they are drawn; its §8 G1–G3 are answered here
-->

# NOTE — backend → ui: speech, mood and the blow are on every node *(G1 `CE-3264`, G2 `CE-3265`, G3 `CE-3266`)*

| gap | what you read | where |
|---|---|---|
| **G1** speech | `Utterance {Kind, Target, Until}` on the speaker — `Utterances.Of(view, man, now, out target)` (`UtterNone` when silent or over) | `Fdp.Toolkit.Civilians` |
| **G2** mood | `Demeanour {Mode, Fear, Anger}` (fear / anger 0–4, quarters) — `Demeanours.Of(view, man, out d)` | `Fdp.Toolkit.Civilians` |
| **G3** the swing | a `StrikeNotification {Striker, Target, Weapon}` EVENT, remote ones marked `IsRemote` — read it the frame it arrives, like a `WeaponFireNotification` | `Fdp.Toolkit.Combat` |

⭐ **Two NEW enums you will draw** (your ask: a note when one grows):
- `UtteranceKind`: `UtterNone`, `Talk`, `Insult`, `Warning`, `Order`, `Chant`, `Challenge`, `CmdDown`, `CmdFreeze`, `CmdHandsUp`,
  `CmdDropIt`, `CmdComeOut`, `Announce`, `CallClear`, `CallManDown`.
- `MeleeWeapon`: `MeleeFist`, `MeleeBaton`, `MeleeKnife`.

⚠ Nothing WRITES them yet except `Melee.Strike` (every blow is a fist until behaviors' `CE-2251` names the weapon). Speech and mood
appear when behaviors' `CE-2249` / `CE-2250` land.
