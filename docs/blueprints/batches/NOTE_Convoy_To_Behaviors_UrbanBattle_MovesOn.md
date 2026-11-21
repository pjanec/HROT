<!--STATUS
state: LIVE
updated: 2026-11-17
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../designs/vehicle-tactics/DESIGN_Road_Convoy.md — §6 CE-9012 (the platoon's vehicles drive the route as one road column)
  - ../../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — §11a PlatoonConvoy (ride, halt on contact, secure, remount, carry on)
-->

# NOTE — convoy → behaviors: `uc-urban-battle`'s "the convoy moves on" row was a FALSE GREEN (CE-9023)

From the convoy lane, `2026-11-17`, after CE-9012 (CE-9013 fixed — thank you): `PlatoonConvoy` now gives each vehicle a `ConvoyOrder`
(one road column, the first vehicle that can move leads) instead of its own `FollowRoute`. The cross-lane edit is in `01fe81649` (the two
`FollowRoute` task rows of `uc-urban-battle/checks.json` now say `PlatformConvoy`; the PlatoonConvoy rails count `PlatformConvoy` orders).

## What was measured (editor tier, two traces, `/entities/{id}/brain` every 10–40 s)

| fact | |
|---|---|
| the IED leaves the lead APC unable to move: its convoy order fails (CE-9014) and it is `Idle` from ~t 20 s | ✅ correct |
| ⛔ BEFORE the switch, the immobile APC kept "running" `FollowRoute` for good — and that alone satisfied *"and the convoy moves on (a vehicle on FollowRoute again)"* the instant a squad remounted (both rows at t 139.5 s in the old passing run) | the row never saw the convoy move |
| after the switch: run 1 — remount at 104 s, no vehicle on its column order by 200 s; run 2 — `PlatoonHold` (the contact reaction) until ~220 s, then `CContact` to ≥ 300 s; the IFV `Idle` | the platoon does not reach CMove again inside `runFor` 200 |

## The ask (yours to decide — the play and the scenario are behaviors')

- Is a platoon that has not moved on 200 s after the IED the play working as meant (secure, remount — 120 s timeout — carry on), or a defect?
- Lean: the row was right to ask for the move-on; it was the stale `FollowRoute` that let it pass. Either a longer `runFor`, or a defect in
  getting back to CMove (run 2 never left the contact reaction for 200 s). ⛔ The convoy lane did not loosen the row.

## What the convoy lane changed on the way

- `PlatoonConvoyBehavior.KeepVehicle`: a column order that RAN and ENDED (its lead left the column — hit, fighting, killed) is given again,
  with the column's new lead and rank; one still landing or paused by a reaction is left alone (`ConvoyPlan.VehicleRan`).
- `Arrived`: each vehicle may halt its rank × the gap behind the route's end (a column halts closed up — CE-9022).
