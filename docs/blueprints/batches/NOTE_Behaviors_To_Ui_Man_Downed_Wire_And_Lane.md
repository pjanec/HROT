<!--STATUS
state: LIVE — behaviors → ui: both asks of NOTE_Ui_To_Behaviors_Man_Downed_Readers.md built (CE-3364 D5)
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Man_Downed_Event.md — owns the event, the topic and the lane (§5 As built)
  - NOTE_Ui_To_Behaviors_Man_Downed_Readers.md — the asks this answers
-->
# NOTE — behaviors → ui: **`dtManDowned` and `combat.downed` are built**

| ask | as built |
|---|---|
| ① wire topic | **`dtManDowned` = 175**, topic `Hrot.NED.Messages.EntityManDowned`: `VictimEntityId` · `SourceEntityId` · `OriginalSourceEntityId` (network ids, 0 = unknown / same as source) · `State` (`InjuryState`: 2 down, 3 dead) · `BodyPart` (**id + 1, 0 = none**) · `VictimSide` (`ForceId`) · `SourceNodeId`. Egress + ingress on EVERY node. On a node that did not apply the Health you read an ordinary `ManDownedEvent` with `IsRemote = true` (victim / source already resolved to that node's entities); on the Health owner it is the local one. **One event per downing per node** — the own-sample echo is dropped. |
| ② lane | **`combat.downed`** (`CombatTimelineLanes.Downed`, group Combat): Subject = victim, Other = who downed him, Value = `(float)State`, X = `(float)VictimSide`. Live and in recordings. **Recorded = yes** — rail `TimelineIndexTests.CE3364_AManDownedEvent_IsRecorded_AndIndexedOnTheDownedLane`. The shipped catalogue is 18 lanes. |

An event on a node that does not know the victim is dropped (not held). ⚠ Counting stays as you said: the forces card reads `Injury.StateOf`; kills / assists in the measures read this event.

_behaviors lane, 2026-11-03_
