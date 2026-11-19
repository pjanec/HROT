<!--STATUS
state: LIVE
updated: 2026-11-19
current-answer: the whole note
related-designs:
  - ../../DESIGN_Scenario_Rules.md — S0 item 5 as-built (the CE-3483 line)
  - ../../designs/hill-attack/DESIGN.md — the platoon commander whose baseline dispatch this fixes
-->

# NOTE — backend → behaviors: a cross-lane edit in `HillAttackCommanderNodes` (CE-3483)

FYI, nothing to do unless you disagree.

**What was wrong.** `Action_DispatchAllToBaseline` serialised the blackboard struct `CgfNodes.MoveToLocationParams` (`{"X":…,"Y":…}`) as the
`MoveToLocation` order. S0 item 5 (`489774f21`, 2026-11-01) retired those keys in favour of `target`; the params parse ignores them, so every
tank resolved to (0, 0) and drove away from the hill. 📐 Measured live in the editor: tank 1001 at t 8 s on `PathToPoint`, `FinalDestination [0,0,0]`,
velocity (−7.4, −6.7). `hill-attack` failed its own checks in BOTH tiers (the advance only at ~145 s, no shot, no kill). The rail
`SC_HA010_4` stayed green because it only compared the four JSON strings for distinctness.

**What changed (backend, cross-lane).**

| file | change |
|---|---|
| `Hrot.Core/…/MoveToLocationParamsJsonDto.cs` | `MoveToLocationParamsJsonDto.Json(x, y, speed, radius)` — the ONE way code writes a move order |
| `Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs` *(yours)* | the baseline dispatch uses it |
| `Hrot.Network.NED/SimHost/NedSimHostMissionSender.cs` | SimHost's click-to-move had the same `X`/`Y` string — same fix |
| `HillAttackNodeTests.SC_HA010_4` *(yours)* | now reads every slot back through the resolver; red-proved with the old serialisation |

⚠ **For new behaviour code:** never serialise a blackboard struct as an order's JSON — write the authored DTO (`…ParamsJsonDto`). Only the
LOAD path refuses retired keys (S0 item 9); a runtime order with them is silently zero.

## Also from the same re-check — CE-3484, half ⓑ is yours

After the fix, `hill-attack` passes 3 / 4 in both tiers. The last check ("the four tanks end back near the baseline") fails because two tanks
returning on crossing paths jam each other: one ends `FailedBlocked`, the other creeps `InProgress` for good. ⓐ the jam is backend's motion
(CE-3484, open). ⓑ `Condition_AreAllAtBaseline` then waits forever on a member whose return FAILED — nothing re-orders it. ⭐ Lean: a failed
return is re-issued once (or the member counts as "back" where it stands), so one blocked tank cannot freeze the platoon.
