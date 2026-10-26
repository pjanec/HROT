<!--STATUS
state: LIVE — ui → behaviors: two cross-lane edits to your stress code, said here
updated: 2026-10-24
current-answer: the whole note (short)
stale-below: nothing
related-designs:
  - docs/DESIGN_Temperament_And_Stress.md — TS-4, TS-6, TS-8: stress, the break, brain-local
  - docs/DESIGN_People_Display.md — §7e U20, the mind-state bar that draws them (CE-1309)
-->

# NOTE — ui → behaviors: `StressSystem` had no `[TickMode]` (every AI host crashed) · the stress reads moved beside `Stress`

**① A crash, fixed in your file.** `cb575abba` registers `StressSystem` in `CgfLogicPack` without `[TickMode]`; R-3036's scheduler throws on
the first frame (*"must declare [TickMode(...)]"*), so the editor died on every scenario. ⭐ Fixed with `[TickMode(TickPolicy.WhenTimeAdvances)]`
(its siblings' policy — `RestlessSystem`, `UnderFireMoveSystem`). ⚠ Your `b96a86529` still lacks it — **take the ui merge or add the line**.
A guard now covers the class: `Hrot.SimHost.Tests/BrainSystemsDeclareTickModeTests` — every concrete system in Hrot.AI.Behaviors and
Hrot.CGF declares its tick mode.

**② The stress READS live in FDP now.** `LevelOf`, `IsBroken`, `BreakLevel`, `Nervousness`, `BreakFloor`, `NervousFrom` moved to
`Fdp.Toolkit.Behavior.Components.Stresses` (in `Stress.cs`) so ui's mind-state bar (People Display §7e, `CE-1309`) reads the same breaking
point the brain breaks at — one copy. `StressModel` keeps every member as a one-line forward, so no caller changed. Your stress tests are green.

---
_ui lane, 2026-10-24_
