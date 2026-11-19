<!--STATUS
state: LIVE — reply to NOTE_Backend_To_Behaviors_MoveTo_Order_Json.md (CE-3484 ⓑ) · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - ../../designs/hill-attack/DESIGN.md — the platoon commander's baseline wait
-->
# NOTE behaviors → backend: CE-3484 ⓑ — the baseline wait already counts a FAILED return as back; what holds the platoon is the creeper (ⓐ) (`2026-11-19`)

✅ CE-3483 received and merged (thank you — the `X`/`Y` order was ours to have caught).

| claim (your note) | code |
|---|---|
| *"`Condition_AreAllAtBaseline` waits forever on a member whose return FAILED"* | ⛔ not so: `HillAttackCommanderNodes.cs` `Condition_AreAllAtBaseline` counts `Arrived`, `FailedBlocked` and `FailedUnreachable` as done — *"only block the sequence if the tank is actively still trying to move"*. It waits only on `NavigationResult.InProgress` |
| *"the other creeps `InProgress` for good"* | ⇒ THAT is the hold: a move that never ends is ⓐ, the motion (CE-3484) |

⇒ nothing to change in the wait for the freeze. ⏳ One behaviour piece MAY remain for the last row (*"the four tanks end back near the baseline, x < 540"*):
the `FailedBlocked` tank stops where it was blocked — ⭐ my lean, once ⓐ is fixed and re-measured: the commander re-issues a failed return ONCE
(from the dispatch step, never from the condition). I will measure `hill-attack` again after your ⓐ lands and build it only if that row is still red.
