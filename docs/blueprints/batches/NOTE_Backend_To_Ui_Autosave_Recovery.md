# NOTE — backend → ui: autosave recovery is in (CE-4099 A7) — the prompt and the door are yours

`REQUEST — the "Recover unsaved changes from 14:41?" prompt (AQ108 §4c A7) and a recovery open through your ScenarioDoor`

**What backend built** ([AQ108 §4c.2–§4c.3](../Architect_Question_108_Run_Mode_Picker.md)):

| piece | how you reach it |
|---|---|
| is there an autosave? | `AutosaveFiles.Read(nas, name, ScenarioDescription.HashOf)` → `{Exists, AtUtc, Stale, Refusal}` (Fdp.Toolkit.Orchestration) · HTTP `GET /scenario/autosave?name=` |
| open it | `TransitionStateIntent.RecoverAutosave = true` on the edit load (the wire carries it) · HTTP `POST /scenario/load/edit {name, recoverAutosave:true}` |
| what happens | the nodes stage the autosave as `scenario.json` for this load only; the name and folder stay; `ClusterMaster.RecoveredAutosaveSinceLoad` makes the check round say CHANGED until a real save; a save deletes the autosave |

**What is yours:**
1. The prompt on Open / the start surface when `Exists && !Stale`: *"Recover unsaved changes from 14:41?"* (Recover / Open the saved one). A stale one is reported, not offered.
2. `ScenarioDoor.Open` has no recovery flag: today HTTP's own deciding door sends the recovery intent (`DebugApiService.LoadScenarioEdit(…, recoverAutosave)`). For the UI path, add one to your door and pass it into the session's open (`IRunScenarioControl.OpenForEdit` would need a `recoverAutosave` — your interface; backend can add the session half on request).

---
_backend lane, 2026-11-08_
