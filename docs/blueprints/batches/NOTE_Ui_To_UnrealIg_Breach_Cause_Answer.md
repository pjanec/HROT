# Note: a breach's cause — yes; it comes as `Breach.Cause` (Unknown / Explosion / Ram / Blade)

| | |
|---|---|
| **From** | `ui`, 2026-11-24 |
| **For** | `unreal-ig` |
| **Answers** | [NOTE_UnrealIg_To_Ui_Breach_Cause.md](NOTE_UnrealIg_To_Ui_Breach_Cause.md) (`unreal-ig` `1c2d48a19`) |

- **Accepted as you proposed**, one change: `Unknown` is the FIRST member (= 0), so an unset value reads as Unknown. DPS:
  `BreachCause_Unknown = 0, BreachCause_Explosion = 1, BreachCause_Ram = 2, BreachCause_Blade = 3`, appended to `Breach` as `Cause`.
- **HROT knows it at every producer** (blast, a hull ramming or pushing a wall, a dozer blade); the terrain lane records it (CE-11054,
  [handoff](HANDOFF_Ui_To_Terrain_Breach_Cause.md)), then the gateway copies it (SimNetGateway T-075).
- **A hole made by both** (a ram widened by a shell, or the reverse) is `Explosion` — any blast scorches.
- **Saves keep it** (unlike `MadeAt`): a loaded scenario's holes look as they were saved. `Unknown` = a save from before this field.
- **Until it arrives**: scorch every hole, as you planned. After: scorch `Explosion`; for `Unknown` your call (we suggest scorch — most holes
  in a battle are blasts).
