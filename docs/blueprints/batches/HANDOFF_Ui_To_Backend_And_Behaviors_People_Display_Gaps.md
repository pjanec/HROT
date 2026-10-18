<!--STATUS
state: LIVE — ui → backend + behaviors: FINDINGS (what the people display needs and does not have). Each lane designs its own half.
updated: 2026-10-18
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_People_Display.md — ui's design (U7…U18); §8 is this list with its reasons, §1.1 the inventory the gaps come from.
  - docs/DESIGN_Civilians_And_Crowd_Control.md — OWNS the data each gap adds (§5h–§5l, §7.7–§7.16).
-->

# FINDINGS — ui → backend + behaviors: what the people display needs *(G1…G8)*

**Dispatched at** the ui commit that adds this file. Nothing here is asked by a date — the ui items that need a gap draw nothing until it
lands, and the rest build now. 🔒 The user asked for it, `2026-10-18`: *"if some of those are missing some backend implementation pls
record those as well."* Allocate your rows from your own blocks; ui's are `CE-1169`…`CE-1179`.

| # | the gap | what ui would read | lean | owner |
|---|---|---|---|---|
| **G1** | ⛔ **no SPEECH act exists** (searched shout, insult, chant, speech, utterance, verbal, megaphone: none); a warning is only `Gesture.Warning` + the brain-local `WarningLog` | who speaks, what kind (insult · warning · order · talk · chant), to whom, until when | a replicated `Utterance {Kind, Target, Until}` in the Brain group — the `Gesture` shape; the HSM and the escalation ladder write it | backend (component + `dt*`) · behaviors (who says what, when) |
| **G2** | ⛔ the MOOD lives on the brain node only (`CivilianState`, `Crowd` — `CognitiveComponentRegistry.cs:35-36`) | the mode (alarmed, panic, protest, riot, surrender…), fear and anger | a replicated `Demeanour {Mode, Fear, Anger}` (three bytes, fear / anger quantised to quarters), written only when a value crosses a quarter or the mode changes | backend · behaviors |
| **G3** | ⛔ the MELEE swing is not sent: `Melee.Strike` raises only the victim's `DamageAssessedEvent` (`Melee.cs:16-52`) | who struck whom, with what (fist, baton, knife) | a `StrikeNotification {Striker, Target, Weapon}` raised by `Melee.Strike`, on the wire like `WeaponFireNotification` | backend |
| **G4** | ⛔ no melee ITEM: the police baton is a strike in `HoldTheLineBehavior.cs:97`, no item; no knife or machete (AW-19 designed, not built) | the baton / knife in his hand, its look | mounts with `Appearance` (LongObject / SmallObject, `ReachM`) whose fire is `Melee.Strike`; the baton man (1202) carries one | backend (data) · behaviors (AW-19) |
| **G5** | ⛔ no TASER, no PEPPER SPRAY | the item in hand and the moment it fires | munitions + effects (taser: Stun ≤ 7 m, two darts; spray: Irritant cone ≤ 3 m) on police types; which rung of the ladder allows them | backend · behaviors |
| **G6** | ⛔ the TOY GUN exists only as a test type (`ArmamentTests.cs:28`) | a type to place in a scenario | a civilian type whose mount looks like a rifle (or pistol) and fires nothing | backend |
| **G7** | ⚠ nobody SETS the held item: `HeldItems.Set` has no production caller | a man on his phone, a lookout with binoculars, a stone ready to throw | the behaviours set it (the CivilianHsm's Routine: phone sometimes; a lookout: binoculars; before a throw: Throwable) | behaviors |
| **G8** | ⛔ no riot SHIELD item on the shield man (1201) | the shield in his hand | an item (look + size), later a blocker for thrown items | backend |

⭐ Also found while merging (fixed by ui, no action): behaviors' `GestureKind.Warning` (= 6) arrived with no ui pose, so the 3-D figure drew
the throwing wind-up and 2-D said PROTEST — ui's names rail (`CE1166_TheDrawingsGestures_AreBackendsGestures_ByteForByte`) caught it on
the merge; ui now draws the warning (arm out, palm forward) and writes WARNING (`CE-1180`). ⭐ Please send ui a one-line note when a
replicated enum ui draws grows a member.
