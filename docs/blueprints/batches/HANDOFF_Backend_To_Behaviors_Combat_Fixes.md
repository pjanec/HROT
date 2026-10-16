<!--STATUS
state: LIVE
updated: 2026-10-16
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Peek_And_Fire.md — §10.10: the muzzle-line hold (CE-2218) beside the friendly-line hold
  - docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md — §9b: a vehicle part's face is the armour arc (CE-2217)
  - docs/designs/airborne/DESIGN_Airborne_Insertion.md — §7d: the ejected pilot crosses to the Muscle descending (CE-2210)
  - docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md — §4: the exposure budget that reads ThreatDanger (CE-2217)
-->

# HANDOFF — backend → behaviors: three of your rows are fixed — re-run the demos, and one behaviour reaction (CE-2210, CE-2217, CE-2218)

**Dispatched at `3339e32d1`** (backend). Your scope is frozen at that sha; nothing here stops an item.

| row | what backend changed | ⭐ what is yours |
|---|---|---|
| **CE-2210** (the ejected pilot never flies) | the descent's sender now covers a body still being CONSTRUCTED (the creator owns his Muscle group only then), and `EjectionSystem` reads the jet's motion through `ReplicatedMotion.Of` — the pilot reaches SimHost **descending, with the jet's speed** (two-node rail IT-SA-4) | ⭐ re-run **`ua-eject`** (CE-1118 H-3): the canopy should open ~3 s after the ejection and `PilotSop`'s ground row fire on landing |
| **CE-2217** (a direct Javelin rated harmless on the front, scored on the side) | measured offline, **nobody moving**: on the M1 kit the box face ENTERED was Side from 16° to 58° off the nose while the danger model predicted Front. A vehicle part's face is now the armour ARC it arrives from (in the part's frame — a turned turret still shows its front). ⇒ **the score now equals `ThreatDanger.ToMe`'s prediction** for a hull hit | ⭐ re-run **`ua-javelin-lock`** ①. ⚠ Expect the shot at 19° off the nose to be what the tank predicted: FRONT armour, 760 vs the front — so `ToMe` 0.11 was RIGHT and the tank staying out is correct. If the vignette needs the tank to fear Javelin A, place A so it sees the side (> 60° off the nose) or within top-attack range |
| **CE-2218** (an RPG into his own window frame) | before release the round is carried along its OWN line (the barrel, else the eye) by the penetration rule; stopped within 2 m — or a warhead's blast-injury radius — ⇒ the weapon **HOLDS** with `ActionReason.Masked` (Running, nothing spent, posture Ready). Aimed and blind releases of `AimAndFireExecutor` | ⭐ **the behaviour's answer to `Masked`**: a shooter held `Masked` should move (a step back from the window, the next window, another cover point) — today it waits. `PeekAndFire` relocates on a failed EXPOSURE; `Masked` is a new reason for it to read. Then re-run **`ua-rpg-window`** at its steep geometry (the IFV 10 m off) — he must live |

**Also fixed:** CE-2209 — a route needs no `SimTransform` any more (its `RoutePlan` is published regardless); your demos may drop the placeholder transform.
