<!--STATUS
state: LIVE
updated: 2026-11-08
current-answer: the whole file
-->

# NOTE — coordinator → behaviors: what is yours, in order (nothing new is dispatched)

🔒 User, `2026-11-08`: *"what can we give to the behavior lane which is still idle?"* — everything below is ALREADY dispatched or reported; this only
orders it. Merge `coordinator`, `ui` and `backend` first.

| # | what | where | state at `origin/behaviors` `e50ce21d4` |
|---|---|---|---|
| 1 | **finish B-17** — map interaction P2, P4–P6 (P1 done) | [HANDOFF_Coordinator_To_Behaviors_Taken_From_Ui](HANDOFF_Coordinator_To_Behaviors_Taken_From_Ui.md) W-2 · your [STATUS](STATUS_Editing_UX_Build_Behaviors.md) | 🔨 in progress |
| 2 | **the Review perspective, V1–V7** (CE-3419) | [HANDOFF_Coordinator_To_Behaviors_Review_Perspective](HANDOFF_Coordinator_To_Behaviors_Review_Perspective.md) (dispatched at `d344ab9ec`) · [DESIGN_Review_Perspective](../../UX/DESIGN_Review_Perspective.md) | not started (`d344ab9ec` is not in `behaviors`) |
| 3 | **your pre-existing reds** — `CE1431` hosting snapshot (regenerate if the new behaviours are intended) · `OneSharedJsonOptionsTests` (`BehaviorParamsValidation.cs:16`, `RoutePlanTranslator.cs:135`) · Blueprints `CE1161_TheTypedStartTime…`, `SubTickRestoreRegistrationTests…`, `CatalogTests.ChannelCommandPins…` (9 vs 8 pins), `CE2083_SopOrderUrgency…` | [NOTE_Backend_To_Lanes_Preexisting_Reds](NOTE_Backend_To_Lanes_Preexisting_Reds.md) | each FIXED or FILED with its cause, ⛔ never skipped |

⭐ Row 3 can go between items when a build is running. ⛔ Not yours: recording storage (backend), the recording-storage UI (ui, later).

---
_coordinator lane, 2026-11-08_
