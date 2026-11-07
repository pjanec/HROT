<!--STATUS state: LIVE · updated: 2026-11-06 · current-answer: the table -->
# STATUS — backend, editing-UX build ([handoff](HANDOFF_Coordinator_To_Backend_Editing_UX_Build.md), dispatched `9c50cec00`, started at `6754d2ecc`)

| item | state | sha | what the other lane can now use / notes |
|---|---|---|---|
| B-1 | ✅ DONE | `b258e464b` | ⭐ **for U-14:** `LogicalOperator.None` (true when no child holds; one child = NOT) · `InUnitPredicateDto { Unit: EntityLink, IncludeSelf = true }` (`"InUnit"`) · `InTheFightPredicateDto {}` (`"InTheFight"`) — compiled in both modes, in `SearchPredicateDto.cs`. The walk is `Fdp.Toolkit.Squad.UnitTree`. Condition editor §5.1 |
| B-18 | PART — SV-6 ✅ `2bc68e4be` | — | `Fdp.Core.ExerciseId` / `EpisodeId` (over Guid, identical memory + JSON). SV-4 `Angle`, SV-1 next |
| B-2 | ✅ DONE | `97892dd32` | ⭐ **for U-23 / U-26:** `Fdp.Toolkit.Replication.Edits.Edits.Submit(world, EditOp)` — the ONE entry (`EditOp.Write(entity, change(s), label)` · `WritePatch` · `SetCommander`); `EditJournal.Of(world)` (entries: label, mode, Status, Reason, `Version` to redraw; call `Refresh(world)` once a frame); `EditStatus` + Conflict, Lost. Review refused on every host; `HrotEditGate` (Hrot.Common.Editing, + `EditingMode`, `EditingLevel`) installed by the seat later. Rename (CE-4274), ORBAT (CE-4275), the clock go through it. Edit framework §4.1 |
| B-4 | IN PROGRESS (worker) | — | the form attributes |
| B-3 | PART — F2 ✅ `57faadb52` + `ffbbdb377`; F3 next | — | ⭐ **for U-23:** `EditOp.Create(send, label)` / `EditOp.Delete(entity, label)` through `Edits.Submit`; create and delete answer with `EntityEditOutcome` (Pending → Applied with the new id, or Refused + why) into `EditJournal`. ⛔ `CreateUpdateDeleteEntityAck` / `RequireAck` are GONE (use `EntityEditOutcome`). The map placement and the delete menus are already journalled. Edit framework §4.2. F3 (add / remove a component) waits for B-4's attribute files |
| B-6 | IN PROGRESS | — | F4 Interact (RequestId on the interaction payloads), F6 Commit + Conflict |
