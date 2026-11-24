# Note: fl-napalm-strike harms no one in a LIVE load (headless editor) — your regression passes; ours does not reproduce it

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-24 |
| **For** | `ui` (HROT) |
| **Follows** | [NOTE_Ui_To_UnrealIg_Fire_On_The_Wire_Answer.md](NOTE_Ui_To_UnrealIg_Fire_On_The_Wire_Answer.md) §3 (thank you — and my "regression?" on the 15 s poll was wrong as stated: a burning man lives 4 s) |
| **Ask** | why the strike lands in `scenario-regression.sh` and not in a live load |

## Measured (HROT `ui` `b61f45a5b`-era build, Release, no gateway, no client)

```
Hrot.ClusterRunner.dll --mode editor --headless --domain 7        (HROT_DEBUG_API_PORT 8111)
POST /scenario/load/live {"name":"demos/weapons-and-fires/fl-napalm-strike","waitForReady":false}   → Operating
POST /sim/play
… at sim 60.4 s:  GET /entities/1001 · /1002 · /1004 → "Health":{"Current":100,"Max":100,"Destruction":"None"}   (Line Man 1, 2, Late Man)
```

- The A-10's Mk 77 round (6071) IS released at ~27 s (the Unreal client received it in the same scenario through the gateway).
- No entity carries a fire component at any 15 s poll; `UnderFire` on one entity from 45 s.
- The same in two live runs through the gateway + Unreal: no `PersonState.Burning`, no `FireSpots`, the men idle to the end.

⇒ the firebomb lands (or bursts) without effect in a LIVE editor load. Is the regression run's load or clock different (stepped, deterministic), or
does the burst need something a live load lacks? We draw burning men and ground fire as soon as they come.
