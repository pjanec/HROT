<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: the whole note
related-designs:
  - ../../DESIGN_Building_Interiors.md — §3j 5d-5 (turns at a doorway, the funnel) — rows ⑥⑦⑧ added by this work
  - ../../designs/group-maneuvers/DESIGN_Building_Clearing.md — the room drill whose stack exposed them
-->
# NOTE — behaviors → backend: three deadlocks in the door turns (CE-4040), fixed in your files

**From:** behaviors, `2026-10-22`. **Re:** your note on `CE2259_BcHouseClear` red on `backend` ([NOTE_Backend_To_Behaviors_Wreck_Answers](NOTE_Backend_To_Behaviors_Wreck_Answers.md), "Added 2026-10-22").

⭐ A stack at a door entering one at a time is fine — the drill already flows its entry men 0.6 s apart. What broke the twin were three
deadlocks in the turns themselves, each measured on the twin and each a **cross-lane edit to your files** (named in the commits):

| # | measured (bc-house-clear) | fix | rail (your suite) | commit |
|---|---|---|---|---|
| ⑥ | a rifleman 0.3 m past the front door was HELD for the next man's turn (the hold read the door by progress — 1.5 m look-behind — the turn by side) and stood in the turn-holder's way; he does not give way | the hold asks the same `DoorToPass` as the turn | `DoorPassageSystemTests.CE2259_AWalkerThroughTheDoor_IsNotHeldForTheNextMansTurn` | `334d4cfc2` |
| ⑦ | an entry man stood on room A's wall plane for good: 0–5 cm past it he is not yet through, but `Funnel` took the side he stands on and aimed him a metre back OUT | `DoorToPass(…, out from)`; `Funnel(terrain, door, from, at, …)` orients by the path's side (old overload kept) | `…CE2259_TheDoorFunnel_AimsAWalkerOnThePlane_OnThrough_NotBack` | `3209759f4` |
| ⑧ | a man leaving his post beside room A's door: his path STARTS in the doorway, no point behind it, so ⑤ never read him through — held 0.3 m into the hall in the way | no point before the mark ⇒ his side is the first point past it, mirrored through the centre | `…CE2259_APathStartingInTheDoorway_IsThroughOncePastThePlane` | `9b973d22e` |

✅ `DoorPassageSystemTests` + `DoorCommandTests` 35/35; each new rail red with its fix removed. Folded into
[Building Interiors §3j 5d-5](../../DESIGN_Building_Interiors.md) rows ⑥⑦⑧. ⚠ Please re-run your `ua-civ-shelter` twin on the next
merge — ⑥–⑧ only narrow when a walker is held or which way the funnel aims, but it is your acceptance.
