<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: the whole note
related-designs:
  - ../../DESIGN_Building_Interiors.md — §3j 5d-5 (turns at a doorway, the funnel) — rows ⑥⑦⑧ added by this work
  - ../../designs/group-maneuvers/DESIGN_Building_Clearing.md — the room drill whose stack exposed them
  - ../../designs/motion-models/DESIGN_Motion_Domains.md — §5f.1, the hull guard's rise from each point's own ground
-->
# NOTE — behaviors → backend: three deadlocks in the door turns (CE-4040) and a hull held on any slope (CE-3286), fixed in your files

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

## Added `2026-10-22` — a fourth, in `HullClearance` (CE-3286): a hull held at the foot of any slope

📐 `ua-hill-attack` (editor and `--mode all`): every M1 was HELD at the ridge's foot (y ≈ 166, slope ~0.1, a 9.8 m hull) by your hull guard, backed out ~4.5 m by the recovery and held again — for minutes, no round fired. `/terrain/query` crossed nothing; `/terrain/levels` a smooth ramp ≤ 0.31 m/m. `HullClearance.Enters` read each CORNER's rise against the hull CENTRE's height (and traced the barrier there): on a slope a corner half a hull ahead stands L/2 × slope above the centre, so past `VehicleMaxStepM` it "rose like a wall". ⭐ Now the rise and the trace use each point's OWN ground (`cfbbde02b`, `10d693c7b`). Rail `CarKinematicsSystemTests.CE4041_AHullDrivesUpARampItCanClimb_NeverHeld` (height-grid hillside), red-proved; CarKinem + Terrain 537/537; your CE-3286 rails green. Live `ua-hill-attack` PASS 8/8. [Motion Domains §5f.1](../../designs/motion-models/DESIGN_Motion_Domains.md) — ⚠ incl. the one case it does not cover (a built SLAB ramp steeper than StepHeight ÷ L/2).
