# Note: `va-pop-smoke` passes at fixed step and fires nothing live — the scenario is a timing race (CE-4691)

| | |
|---|---|
| **From** | the HROT-integration session, 2026-11-26 |
| **For** | `backend` (CE-4691's owner) |
| **Evidence** | [NOTE_UnrealIg_To_UiIntegration_VaPopSmoke_Live.md](NOTE_UnrealIg_To_UiIntegration_VaPopSmoke_Live.md) (`unreal-ig` `efe5e78ae`) — HROT alone, headless editor, no client |
| **Build** | `unreal-ig` `7d4438d90`, which has both CE-4691 fixes (`47008cbed` the drive-out fires · `c85017a20` the hull-sized hide + burn) and the moved fixture |

## What was measured (by unreal-ig, same build)

| | fixed step (`scenario-regression.sh va-pop-smoke`) | live (`--mode editor --headless`, load live, play) |
|---|---|---|
| TOW | t 15.28 s | ⛔ none in 43 s |
| smoke | t 15.35 s | ⛔ none |
| verdict | 6/6 PASS | — |

Live: the tank drives off at once and is in its hide (~294.7, 235.3) by ~12 s, behaviour `VehicleFightFromCover`. The Bradley stops at (105.5, 232.8),
then wanders 107–118 × 210–230 to the end. At 17 s `GET /entities/1001/weapons?target=1000`: turret task **Track, mount 0 (25 mm, not the TOW mount 1)**,
muscle **OnTarget**, `roundsFired 0`, status `"aiming"`, progress 0.

## Our reading (⛔ not measured by us)

1. ⭐ Since the fixture move the outcome is a RACE: the TOW comes only after the tank reaches a true hide (~9 s, the row's own note). At fixed step it
   loses by luck (15.3 s), live it wins. The scenario's header warns of exactly this. ⇒ the regression's 6/6 does not say the demo works live.
2. ⚠ The Bradley sits `OnTarget` on mount 0 for the whole run and fires nothing — whether the 25 mm is meant to hold against a tank, and why the TOW
   mount is never chosen while it sees the tank, is not measured.

## Asked

- Lean: make `va-pop-smoke` not depend on the race (a start or timing where the TOW is ready while the tank is still seen, at any step), and check
  item 2. Your lane's call; please say what you settle so we can tell unreal-ig.
