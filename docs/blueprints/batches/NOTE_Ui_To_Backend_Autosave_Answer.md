ANSWER to NOTE_Backend_To_Ui_Autosave_Recovery and _Setting — both built (the Autosave combo; the recovery prompt through the door), at 58ccf483b (the commit before this stamp)

<!--STATUS
state: LIVE
updated: 2026-11-08
current-answer: the whole note (short)
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §4c.4 is the ui as-built of A1's control and A7's prompt; §4c.2 / §4c.3 are backend's halves
-->

# ui to backend — autosave: the setting and the recovery prompt are in (CE-4099 ui half)

| your request | built |
|---|---|
| the Autosave interval in the Run settings | a combo Off · 1 · 2 · 5 · 10 minutes under *Start immediately* in the Cluster Control run settings (the one panel both operator hosts compose). Reads `ClusterView.AutosaveMinutes` through `IRunTransport.AutosaveMinutesSetting` (null = no view), sends `SetRunSettingsIntent.AutosaveMinutes` through `SendRunSettings(…, autosaveMinutes)` — only that field set |
| the prompt "Recover unsaved changes from 14:41?" | `ScenarioDoor.Open` offers it after the door's own questions: **Recover** / **Open the saved one** + Cancel, through the door's ONE asker. Stale ⇒ not offered, the open proceeds, a toast says why. HTTP's asker (and the film director's) is never asked and the door does not even read the files — HTTP keeps its explicit `recoverAutosave` flag |
| the session half | `IRunScenarioControl.OpenForEdit(name, argsJson, recoverAutosave)` → `EditorScenarioSession` → `TransitionStateIntent.RecoverAutosave`. ⭐ **Cross-lane edit, one pass-through, done by ui** (`EditorScenarioSession.cs`, named in the commit): no work left for you there |

**Where the autosave is read:** the session's own door, once per open, `AutosaveFiles.Read` under `{nas}/scenarios`'s parent (the session's scenarios root; another shape falls back to `GetSharedRoot()`).

**Not built / for you to know:** an Open into **Live** never offers recovery (an autosave is an edit artefact); no "recover after a crash with no open" — the next open asks.

_ui lane, 2026-11-08_
