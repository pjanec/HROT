<!--STATUS
state: LIVE — backend → ui: the five items of NOTE_Ui_To_Backend_Walk_And_Run_Speeds + CE-1206, as built.
updated: 2026-10-19
current-answer: the whole note
stale-below: nothing
related-designs:
  - docs/DESIGN_Entity_Control.md — §8b (the walker's gait speeds), §8h U-5 (a possessed vehicle's gun), §8j.5 Z-1 (ActiveMount), §8k B-1 (binoculars)
-->
# Note — backend → ui: walk / run, the operator's mount, `UnderFire`, binoculars, the possessed vehicle's gun

Answers [NOTE_Ui_To_Backend_Walk_And_Run_Speeds](NOTE_Ui_To_Backend_Walk_And_Run_Speeds.md). Nothing in ui has to change.

| row | built | what you see |
|---|---|---|
| `CE-1208` | run = the type's top (every BDC person 2.5 → **6 m/s**, R-265 A); walk = the TKB's **`WalkSpeed`** (0 ⇒ 1.5 m/s), scaled by stance like the run | Shift ≈ 6 m/s, walk 1.5 m/s. ⚠ BDC AI soldiers now run where their behaviour asks (they were clamped to 2.5) |
| `CE-1222` | `OperatorFire` writes `WeaponPosture.ActiveMount` (the 1–4 pick) | the scope rule and the drawn weapon follow the operator's pick |
| `CE-1223` | `UnderFire` replicated (Muscle group → every node, `dtUnderFire` 156; a newer stamp at most every 0.5 s) | a remote station's spread ring doubles under fire |
| `CE-1224` | built by ROLE: any man whose `UnitSubordinate.Designation` is Commander / SquadLeader / TeamLeader holds binoculars, whatever his type | hold B works for every leader. ⚠ No forward-observer type exists |
| `CE-1206` | `CanFire` skips the gunner gate while the host is possessed | ⚠ the ZU-23 live case starts WITH a gunner, so it is not explained yet — please reproduce: which slot the HUD held, what refusal it showed |

---
_backend lane, 2026-10-19_
