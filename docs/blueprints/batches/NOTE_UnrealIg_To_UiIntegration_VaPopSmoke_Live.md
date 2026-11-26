# Note: `va-pop-smoke` live — no TOW in 43 s; it passes at fixed step

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-26 |
| **For** | the HROT-integration session (backend: CE-4691) |
| **Answers** | [NOTE_UiIntegration_To_UnrealIg_Coordinator_Merge_2026_11_26.md](NOTE_UiIntegration_To_UnrealIg_Coordinator_Merge_2026_11_26.md) §1 |
| **Build** | HROT-live at `unreal-ig` `7d4438d90` (contains the coordinator merge `b41f8ad77`), Release, threaded defaults |

⭐ **Not a client issue:** HROT ALONE (headless editor, no Unreal, no gateway client) fires nothing. The Unreal client run showed the same: 2 entities
for 56 s, never a missile or smoke.

## The two runs, same build

| | fixed step — `scripts/scenario-regression.sh va-pop-smoke` | ⛔ live — `--mode editor --headless`, `POST /scenario/load/live`, `POST /sim/play` |
|---|---|---|
| TOW | ✅ t 15.28 s | ⛔ none in 43 s (all 7 TOWs on board) |
| smoke | ✅ t 15.35 s | ⛔ none |
| verdict | 6/6 expect rows PASS (golden differs: a warning). ⚠ the TOW is at **15.3 s**, not the 8.63 s the note quotes | — |

## What the live run did (`/entities/{id}/field?path=SimTransform.Position` every ~1 s)

| t (wall s) | Tank 1000 | Shooter 1001 (Bradley) |
|---|---|---|
| 0 | (262, 215) | (100, 225) |
| 1–8 | drives off at once, to (289.6, 227.7) | moves to (105.5, 232.8), stops ~5 s |
| 12 | reaches (294.7, 235.3) and stays (behaviour **`VehicleFightFromCover`**) | repositions (108→118, 228→210) |
| 17 | in its hide | `GET /entities/1001/weapons?target=1000`: turret task **Track, mount 0 (the 25 mm gun, not the TOW mount 1)**, muscle **OnTarget**, `roundsFired 0`; status `reason "aiming"`, progress 0 |
| 26–30 | moves again to (293.3, 250.4) and stays | wanders 107–118 × 210–230 to the end |

⇒ live, the tank goes for its hide from the first second and the Bradley never gets a TOW off. ⚠ My reading (not measured further): in the fixed run
the launch is at 15.3 s — after the tank has already reached its hide live at ~12 s — so the live timing loses the race. The scenario's own header
warns about exactly this race (a start where the tank reaches a true hide before the TOW is ready ⇒ nothing is launched).

Recipe (all scratch, nothing in git): HROT_HIDDEN_WINDOW=1, `--mode editor --headless --domain 7`, debug API 8111, load live, play, sample 40 s.
