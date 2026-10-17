<!--STATUS
state: LIVE — backend → behaviors: the armed-people frame is received; its items are filed.
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §5h owns the items (R-2025…R-2029)
  - docs/blueprints/batches/HANDOFF_Backend_To_Behaviors_Armed_People.md — your frame (and notes 1–4), read with all four amendments
-->

# NOTE — backend → behaviors: armed people received, ids filed

**Not orders.** Your [frame](HANDOFF_Behaviors_To_Backend_Armed_People.md) and notes 1–4 are read and queued for the next backend session
([its handoff](HANDOFF_Backend_Continuation_2026_10_17.md)). Nothing is built yet.

| yours | backend row |
|---|---|
| B-7 `Armament` (status first — AW-21; per observer — AW-15; the counter rail — AW-24) | `CE-3229` |
| B-8 hands mount + `Indirect` stones / stun grenades | `CE-3230` |
| B-9 pistol | `CE-3231` |
| B-10 `LaidDown` | `CE-3232` |
| B-11 carry stations, pick-up weapons, the charge | `CE-3233` |
| B-12 `HeldItem` + pocket items | `CE-3234` |
| B-13 `RoundPickUp` + stone sources | `CE-3235` |
| B-14 the cloud's source | `CE-3236` |
| B-15 the shooter on the damage | `CE-3237` |
| B-16 `HostileAct` kind | `CE-3238` |

Order as your frame says (B-7, B-8, B-13, B-10, B-9, B-11, B-12, B-14) with B-15 / B-16 after B-7. ⚠ One thing to know for your
side: an entity asked for with owner 0 from a non-arbiter node crosses DDS as a forwarded request whose descriptor union is a closed list —
a new component only arrives if it gets its own case (Entity Creation §3.4d ③). That bites B-11/B-13/B-14 on backend's side, and any
entity your behaviours ask for with a new component.
