<!--STATUS
state: LIVE — behaviors' status for HANDOFF_Coordinator_To_Behaviors_Overnight (dispatched at 55229b8ef)
updated: 2026-11-08
current-answer: the table
related-designs:
  - ../../UX/DESIGN_Map_Interaction.md — "⭐ P7 — the build" (O-1)
-->
# STATUS — behaviors overnight

| # | id | state | evidence |
|---|---|---|---|
| O-1 | B-17 P7 | 🔨 building — the design section "⭐ P7 — the build" written (classes, sequence, modules); P7-A (text backdrop + font, Done button, pick banner + crosshair, rotate) with the worker; P7-B (one drawing style, handles + 3-D stem, ghost, the direction line restyled) next | — |
| O-2 | CE-4350 | 📐 designed — Orders design §14 (`0b3aa8788`): the cause is `ScenarioMissionService.AppendEditorBTreeBehaviors` offering every behaviour to every type; the fix builds §5's ruled class-table filter (`BehaviorFit`); build queued for the worker after P7 | — |
| O-3 | CE-4344 | ✅ fixed — the latch is behaviors' (backend's measurement): `OperatorControlBehavior.OperateWeapon` drops a press while the weapon reloads / is empty / is too hot (`CanFireNow`); ⚠ not `Magazine.Ready` (false for one round in flight) | rail `EmbarkNodesTests.CE4344_APressDuringAReload_IsDropped_AndTheNextPressFires`; EmbarkNodesTests 27/0 |
| O-4 | CE-4123 … CE-4139 | 🔎 reading — CE-4137 hypothesis (to measure live): a commander's roster slot order is the order subordinates MATERIALISE (`GenesisMaterializationSystem.cs:221`), not the authored order, and `PlatoonBound` picks support = slot 2 ⇒ Alpha / Bravo can swap | — |
| O-5 | CE-4041 · CE-4055 | ⏳ queued | — |
| O-6 | CE-4109 | ⏳ queued | — |
| O-7 | CE-2423 | ⏳ queued | — |
| O-8 | CE-4035 | ⏳ queued | — |
