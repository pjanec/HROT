<!--STATUS
state: LIVE — behaviors → backend: a measured sight gap found while building the look demo (CE-2298); a question, not a fix.
updated: 2026-10-19
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md — §3.3a / §3.4: the demo that found it (ld-sentry-surprise)
  - docs/DESIGN_Sensors_And_Doctrine.md — §5.7 BR-7 acquisition by exposure (backend's), the first suspect
-->

# NOTE — behaviors → backend: an exposed man at ~113 m is never acquired on bt-range

**Found live, 2026-10-19, building `ld-sentry-surprise`** ([Look Direction §3.4](../../designs/look-direction/DESIGN_Look_Direction_And_FOV.md)).
Row: [`CE-2298`](../Blueprint_Issues_Tracker.md).

| | measured |
|---|---|
| observer | the sentry, type 2002 (TKB `VisionRange` 150 m), Regular, standing at (185, 80) on bt-range, body and eyes north; scenario `PerceptionReceptor` `VisionRange` 300, `FieldOfViewCos` −0.1736 (200°) — the FOV is applied (`/state` `look.fovDegrees` 200) |
| target | hostile A, type 2002, walking `MoveToLocation` at 1 m/s from (150, 190) east along y = 190 — **≈ 113 m** in front of him, inside his cone, nothing between (bt-range's features are west of x = 170 or south of y = 100) |
| result | ⛔ A is **never** in his memory (`GET /entities/{id}/memory` → no slots) within 20 s — in the 200° run **and** in the 360° control (`ld-sentry-surprise-360`) |
| control | ✅ the same A crossing at y = 150 (**≈ 70 m**) is seen at once; B at 40 m is seen |
| not the cause, read | `Acquisition.DelaySeconds` gives ≈ 2 × (13/400 + hidden share) s — well under 20 s; the implicit visual sensor's `SearchRadius` is the TKB's 150 m (`VisualPerception.ImplicitEntry`), above 113 m |

**Ask:** what stops a sighting between 100 m and 150 m here? ⚠ Also worth a look while there: the implicit sensor's search radius is
fixed at spawn from the TKB, so a scenario's larger `PerceptionReceptor.VisionRange` can never widen it (the solver reads the receptor's
range, the candidate search does not) — say whether that is intended.

Repro: `scenarios/ld-sentry-surprise/scenario.json` with Hostile A's start and `MoveToLocation` y changed 150 → 190;
`python3 scripts/utility-demo-check.py --launch ld-sentry-surprise` prints the ② diagnostic (positions, memory).

---
_behaviors lane, 2026-10-19_
