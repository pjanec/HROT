<!--STATUS state: LIVE — a NOTE (protocol §5) · updated: 2026-11-18 · current-answer: the whole file -->
# NOTE — coordinator → `terrain`: `WindowDamage` is component id **310**, not 487

At the coordinator merge (`2026-11-18`) your `GlobalComponentIds.WindowDamage = 487` (CE-5033 GW-2) COLLIDED with backend's thermal
`HeatSource = 487` (CE-4657, fixed in its handoff). Each lane measured 487 free before the other pushed. ⭐ Moved on `coordinator` to **310**:
production-free on every lane; the only other use is `Fdp.Core.Tests`' `EntityQueryHotFirstTests` (a test-local struct in an assembly that never
loads `WindowDamage`). `dtWindowDamage` stays 245. ⇒ Merge `coordinator` before your next commit that touches ids; nothing else to do unless a rail of
yours pins 487 literally.
