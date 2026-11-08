<!--STATUS
state: LIVE
updated: 2026-11-08
current-answer: the whole note (short)
related-designs:
  - docs/blueprints/Architect_Question_108_Run_Mode_Picker.md — §4c.2 owns the autosave (A1: the master's timer); §0 D31 is the Start immediately setting this one mirrors
-->

# Backend to ui — the Autosave setting is on the wire (CE-4099 phase 2, part 1)

**Backend built the setting; ui puts it in the Run settings dialog.** No widget on our side (your lane).

## What the dialog reads

| where | what |
|---|---|
| `ClusterView.AutosaveMinutes` (`int`) | the master's last broadcast: the autosave interval in whole minutes **while editing a scenario**; **0 = off**; 0 also before the first broadcast and for an older master. Default on a fresh master: **2**. Also `ClusterUiCache.AutosaveMinutes` and `ClusterSlave.AutosaveMinutes` (same fact, same field) |
| `SetRunSettingsIntent.AutosaveMinutes` (`int?`) | what the dialog sends. `null` = no change (an op that sets another setting leaves it alone). 0 = off. A negative number is treated as off |
| how it travels | the same op as the other run settings (`ClusterOpType.SetRunSettings`, DTO field `AutosaveMinutes`, appended) — nothing new to wire: send the intent like `StartLiveImmediately` |

Seen by the setting's own mirror, `StartLiveImmediately` (CE-3415): same op, same broadcast, same read model. Suggested control: a number box / combo (Off · 1 · 2 · 5 · 10 minutes) beside *Start immediately*; the master applies it from the next interval.

## HTTP (agents / tests)

- `GET /run-settings` now also answers `"autosaveMinutes": <int>` (0 = off).
- `POST /run-settings {"autosaveMinutes": 5}` — a whole number >= 0; `0` turns autosave off. Anything else (negative, text, 1.5) is a 400 with the usual usage text. The reply echoes `autosaveMinutes`. MCP tools `get_run_settings` / `set_run_settings` carry it (catalog + SKILL.md regenerated).

## Behaviour worth showing in the UI

- Autosave runs only while the cluster is **editing a scenario**; it is not a save (no re-baseline, no "save failed" fact) and a real save deletes the autosave.
- A setting of 0 means no check round at all while editing.
