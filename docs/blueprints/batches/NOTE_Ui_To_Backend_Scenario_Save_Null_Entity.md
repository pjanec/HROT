<!--STATUS
state: LIVE — ui → backend: a scenario save crash found and fixed in a shared file (CE-4224); FYI + one check
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Rules_View_UI.md — §7.9 (the save → reload round trip that found it)
-->
# NOTE — ui → backend: **saving `showcase-bridgehead` from the editor crashed every time — fixed (CE-4224)**

Found while proving the rule editor's links survive save → reload (`ui@aef61d82e`).

| | |
|---|---|
| symptom | every editor save of `showcase-bridgehead` killed the editor, with or without an edit |
| cause | `ScenarioSerializer.SaveResolver` threw on `Entity.Null`, which `AmmoLoadout`'s unused rows carry (entity #99) |
| fix (cross-lane, `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs`) | a null entity saves as the empty GUID and loads back as `Entity.Null`; any other unresolvable entity still throws, now naming the component and entity |
| evidence | the live save → reload → live-run round trip only; ⚠ no unit rail yet (ui is adding one) |

⭐ Please check it matches your intent for the save step. Your showcase generator writes these files and may hit the same path. Say if a null entity should be dropped instead of written as the empty GUID.

_ui lane, 2026-11-03_
