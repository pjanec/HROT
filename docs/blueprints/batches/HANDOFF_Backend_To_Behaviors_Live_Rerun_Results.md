<!--STATUS
state: LIVE
updated: 2026-10-16
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/batches/HANDOFF_Backend_To_Behaviors_Combat_Fixes.md — the handoff these results follow (CE-2210 / CE-2217 / CE-2218)
  - docs/DESIGN_Peek_And_Fire.md — §10.10: the node's answer to Masked, now built by backend
  - docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md — §4: the exposure budget whose vignette ① is yours
-->

# HANDOFF — backend → behaviors: the three demos re-run live after CE-2210 / CE-2217 / CE-2218 — two items for you

**Dispatched at `606630388`** (backend). Your scope is frozen at that sha; nothing here stops an item. Follows
[the combat-fixes handoff](HANDOFF_Backend_To_Behaviors_Combat_Fixes.md).

| demo (`--launch`, `2026-10-16`) | result | ⭐ for you |
|---|---|---|
| **`ua-eject`** | **4 / 5** — one Pilot (206), under a canopy, landed **250 m** from the patrol, stays down on `EvadeAndSignal` (CE-2210 works live). ⛔ ② *"the canopy open by ~3 s"* measured **3.96 s** against the check's 3.5 s | ⭐ the check reads the phase over HTTP from the replicated copy at sub-second polling; the model opens it at 3 s from its stamp (`DescentModelsTests.B7_*`). Read the phase on the Muscle's perspective, or allow ~4 s. If it stays ~1 s late there, send it back to backend |
| **`ua-rpg-window`** | **PASS 3 / 3** (the IFV now starts 50 m off, so the steep shot does not arise) | ⭐ ⚠ **one item of the earlier handoff is DONE by backend:** `PeekAndFire` now burns a spot whose weapon holds `Masked` and moves on (`a30a2fb14`, the node backend built in CE-3158). What stays yours: any OTHER behaviour that fires (posture, vehicle fight-from-cover) and its answer to `Masked` |
| **`ua-javelin-lock`** | **4 / 5** — ② the tank in the open locked and hit **2 / 2 on its HULL FRONT** (the face the danger model predicts — CE-2217 agrees live). ⛔ ① *"its short exposures deny Javelin A a lock"*: 1 launch | ⭐ as the earlier handoff said: A sees the cycling tank ~19° off its nose, so its rating of A (front armour vs 760) is now RIGHT and it does not hide from A. The vignette needs A where the tank shows its side (> 60° off the nose) or within top-attack range — a placement change, yours |
