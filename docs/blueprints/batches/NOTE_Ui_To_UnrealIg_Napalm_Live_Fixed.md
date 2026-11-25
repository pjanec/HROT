# Note: the napalm strike works live now — what was wrong, what to re-run, what is still open

| | |
|---|---|
| **From** | `ui`, 2026-11-25 |
| **For** | `unreal-ig` |
| **Follows** | [NOTE_Ui_To_UnrealIg_Breach_Cause_And_Live_Runs.md](NOTE_Ui_To_UnrealIg_Breach_Cause_And_Live_Runs.md) §2 · your [NOTE_UnrealIg_To_Ui_How_We_Run_Scenarios.md](NOTE_UnrealIg_To_Ui_How_We_Run_Scenarios.md) |
| **Fixed in** | HROT `ui` `c6ed1c7e9` (backend's CE-11057, merged) — merge it; no gateway change is needed (the gateway reads HROT's records and effects) |

## What was wrong (measured by backend, CE-11057)

| your symptom | the cause | fixed |
|---|---|---|
| no burning by 60 s | a napalm bomb that hit the GROUND did not burst at all (it only burst on a body); live it landed 1.2 m off a man | ✅ it bursts where it lands, like a Mk 82 |
| the strike ~6 s late | live the A-10 started at ~5 m/s, not its 150 m/s cruise: the first frame after a resume was ~3 µs long and the plane's motion turned it into a near-zero speed | ✅ a motion model keeps its velocity on such a frame — live it flies at 150 m/s from the first sample, the release at 19.6 s as in the regression |

⇒ ⭐ **Re-run `fl-napalm-strike` with your recipe** after merging: the fireball, the burning line men (~32 s) and the ground fire.

## Still open (not your side)

- Live, the **Late Man** stops against the Far Car and never walks into the ground fire — not yet measured (backend, CE-11057).
- **`va-pop-smoke`** live (the late TOW) — not yet measured (backend, CE-11057). Do not judge the smoke salvo by it yet.
- The clock's tiny resume frame itself is parked (CE-4689 / CE-4690); the motion fix makes it harmless for flight.
