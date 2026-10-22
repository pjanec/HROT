<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: the whole note
related-designs:
  - ../../DESIGN_Thermal_And_Acoustic_Sensing.md — §8.4 CE-4039: bit 15 of an answer's flags is the solver's pending-ray flag
  - ../../DESIGN_Building_Interiors.md — §3j 5d-5 CE-4040: turns at a doorway, the door funnel, a held person stops
-->
# NOTE — backend → coordinator: civilians hear gunfire and shelter again (`CE-4039`, `CE-4040`)

**From:** backend, `2026-10-22`. **Re:** [NOTE_Coordinator_To_Backend_Civilian_Hearing_Regression](NOTE_Coordinator_To_Backend_Civilian_Hearing_Regression.md).

| item | cause (measured) | fix | the feature's rails |
|---|---|---|---|
| **CE-4039** hearing | a sound's family rides `Flags` bits 13–15; **bit 15 is the solver's `FlagPendingRay`**, read on every template ⇒ a hearing solve holding a rifle (6), launcher (5) or AA gun (4) waited for raycasts nobody asked for — never observed, never published. Every Regular+ listener (a civilian with no proficiency counts as Regular; soldiers too) was deaf to small arms. *(behaviors found the same line by reading.)* | the solver reads the bit only on a template that runs `AccurateLineOfSightTest` | `EqsModuleTests.CE4039_ARegularEar_HearsEveryFamily_ThroughTheSolver` (red-proved: AaGun / Launcher / Rifle); twins CE2235 alarm, CE2236 cower, CE2237 contagion ✅ |
| **CE-4040** shelter | four panicked civilians at House A's one-man door: ① a held runner coasted 1.6 m into the wall beside the door; ② at the open door they jostled with no progress until the watchdog failed every move (a MoveTo allows no replan); ③ released beside the doorway, aiming at the next path corner cut into the jamb; ④ the one who should go kept giving way to the others waiting | turns at a doorway (nearest goes, the rest are held) · the door funnel (onto the axis, then through) · the walker whose turn it is does not give way · "through" judged by the wall's side, not by progress | `DoorPassageSystemTests.CE4040_TwoWalkersAtAnOpenDoor_*`; twin `CE2236_CivShelter` ✅ 3/3 |

⚠ **The bisect commit** (`f1f4c89ba`, CE-2304) is where shelter first went red: its stricter "no progress in 4 s" watchdog stopped
tolerating a crowd at a door. Turning its parts off one at a time at today's head did not bring the twin back, so the fix is in the door
passage, and the watchdog is unchanged.

⭐ **Also found, not yours:** `PostureScenarioTests.CE2239_Escalation_*` and `CE2239_PoliceLine_*` are red at `backend@9e14ead20` too
(pre-existing, filed as [`CE-3295`](../Blueprint_Issues_Tracker.md)) — the police clips would show it. `Fdp.Core`'s
`OneSharedJsonOptionsTests` is red on `LayerControlGizmo.cs:421` (ui's CE-1203).

_backend lane, 2026-10-22_
