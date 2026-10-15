<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-15); scope frozen there.
updated: 2026-10-15
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/air-attack/DESIGN_Air_Operations.md — §7d OWNS this frame (AD1–AD6, the leans); §7c the measurement it builds on; fold the aircraft side's as-built there
  - docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md — OWNS guidance kinds and GuidedRoundSystem: the IR seeker, the proximity fuze, the decoy rule go there
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — OWNS the MANPADS launcher and the ZU-23-2 as weapon-system entities
  - docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md — §8 V8a's AirThreat sensor change, the pattern the missile warning follows
-->

# HANDOFF — backend: air defence, the engine half — MANPADS, the IR seeker, flares and chaff, the missile warning, the ZU-23-2 (CE-2187…CE-2190)

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at the commit that adds this file**: your
scope is FROZEN there; documents that change after it are FYI. If a later document invalidates an item, **STOP and report it** (R-106) —
⭐ stop THAT item, never the batch.

⭐ **A FRAME** — the goal, the fences, the decisions with my leans, the acceptance. **Step 1 is yours**: read
[Air Operations §7d](../../designs/air-attack/DESIGN_Air_Operations.md) and §7c (what already works against aircraft), design the engine
classes and sequences into [Missiles & Ammo](../../designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md) and
[Weapon Systems](../../designs/weapons/DESIGN_Weapon_Systems.md) (UML, INVENTORY first), build, fold the as-built.

🔒 **The user's words this rests on** (`2026-10-15`): *"we might need anti aircraft missile system as well so aircraft can be threatened and
destroyed"* · *"support for firing flares/chaffs would be good"* · *"soldiers should be able to attack air targets, like the rpg to
helicopters (can be from buildings for example), bullets agains low flying aircraft and drones"* · *"wouldnt it be wise to include the
threat avoiding/evasive action into the attack programmes"*. ⚠ **The leans below are mine, sent to the user with this handoff; build on them
unless the user changes one** — a changed lean arrives as a new handoff, never an edit of this one.

## 0. What already works (your AT1–AT4, MC1 — Air Ops §7c-i)

A bullet and an unguided rocket hit a flying hull (AT1) · a straight round at an aircraft is led (AT2, `TargetLead`) · a killed aircraft falls
(AT3) · the UH-60s have health (AT4) · soldiers already see and target aircraft. ⭐ **New today (behaviors, cross-lane in your
`FireProcessingSystem`, one line, railed in your suite):** a DOOR GUN's round keeps its helicopter's velocity — A4 read the bare weapon
system (no locomotion ⇒ "not in the air"); it now reads `HitModel.BodyOf`, as the aim already did. Rail
`FireProcessingSystemTests.S6d_ADoorGunOnAFlyingHelicopter_ItsRoundKeepsTheHelicoptersVelocity`.

## 1. The items

| # | row | item — ⭐ my lean | acceptance — rails in the features' own suites |
|---|---|---|---|
| **1** | **CE-2187** | **MANPADS + the IR seeker (AD1, AD2).** ⭐ A new `GuidanceKind.InfraRed` (not `FireAndForget`: only an IR seeker can be decoyed by a flare) · a Stinger-class and an Igla-class munition (`MissileDto`: speed, flight time, min / max range, the seeker's cone and lock time, ⭐ `TargetDomain = Air` so the weapon choice never fires one at a truck and an ATGM never at a helicopter) · a carried launcher weapon system (the Javelin 603 pattern, `FireWhileCarried`) on a soldier type · ⭐ a proximity fuze on the warhead (`ProximityFuzeM`: it bursts within N m of its target — a SAM rarely strikes the hull) | a launched IR round homes on a flying helicopter and its burst damages it; a missile of `TargetDomain = Air` is never chosen against a ground target, nor an ATGM against an aircraft; a proximity burst at N m without a hull hit |
| **2** | **CE-2188** | **Flares and chaff (AD3).** ⭐ The aircraft type declares its dispensers (`CountermeasuresDto`: flares, chaff, salvo, interval) ⇒ a `CountermeasureState` · the BRAIN raises a `DispenseCountermeasureIntent` (behaviors decides WHEN — §2) · the SimHost spawns short-lived decoys and every homing round of the matching seeker kind that has the decoy in its cone re-targets it with the seeker's rejection chance (TKB per munition), drawn once per salvo from `SimRng` (deterministic) · chaff: the same rule for a `GuidanceKind.Radar` seeker — ⚠ **no radar seeker exists yet (AD6)**, so chaff is the dispenser and the decoy only, railed with a test-only radar round | a salvo seduces an IR round that has the flare in its cone (it misses the aircraft) and does not seduce one that has not; the count drops by the salvo; an empty dispenser spawns nothing; the same seed ⇒ the same outcome |
| **3** | **CE-2189** | **The missile warning (AD4).** ⭐ When a guided round is launched at (or locks on) an aircraft, the aircraft's BRAIN node gets a warning — bearing, seeker kind, time to impact — as a sensor change beside V8a's `AirThreat` (`ThreatEvaluationSystem`), carried across nodes like the detonation report | the warning reaches a brain on ANOTHER node within a frame of the launch, with the round's bearing; none for a round at a ground target |
| **4** | **CE-2190** | **The ZU-23-2 (AD5).** ⭐ A weapon system (Ground and Pintle — on a technical's station like the M2, or on its own wheels with a two-man crew), twin 23 mm, a high elevation (−10…+90°) and a fast traverse, HE-I and API rounds; ⭐ no proximity fuze (the real one has none) — AT2's lead does the work | from a pickup's station the ZU-23 tracks and hits a helicopter crossing at 60 m/s at 1 km; its elevation reaches a helicopter overhead |

⛔ **Not in this batch:** the radar SAM (AD6 — a design question for the user after this lands) · per-face aircraft armour (your §7c-i
proposal, still with the user).

## 2. What behaviors builds against it (CE-2191 — FYI, not yours)

The aircraft's reaction inside the one attack (W4): a warning ⇒ flares on the IR seeker's bearing + a break turn, the run aborted and come
round from another bearing; the battle position for a helicopter that knows of a threat · the MANPADS team and the ZU-23 crew engaging
aircraft (the existing aimed path) · RPGs at helicopters from buildings · the demo `ua-air-defence`.

## 3. Gates — the report contract (CLAUDE.md rule 8)

One row per gate with the verbatim command, counts and the delta; `--no-build` column; reds confirmed pre-existing against your base sha;
clean tree; quarantine counts; `tracker-counts.py --check`; ⭐ row 8: this crosses nodes (the warning, the decoys) ⇒ name the cluster-level
suite that exercises it, or say why none can.
