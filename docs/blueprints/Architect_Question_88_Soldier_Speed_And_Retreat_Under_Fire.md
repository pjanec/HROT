<!--STATUS
state: LIVE
updated: 2026-10-10
current-answer: §3 the three sub-questions with leans — awaiting the user
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - DESIGN_Peek_And_Fire.md — OWNS the fight-from-cover node; its relocation speed (4.5 m/s) is one of the clamped asks (§2 row 2)
  - docs/designs/navig-2/Navigation_Design_v2_0.md — OWNS the move pipeline; §11 stance speed ratios exist for the crowd agent only
  - FDP/Docs/projects/toolkits/FDP.Toolkit.CarKinem.md — OWNS the pedestrian mover ("Human gait"): the MaxSpeedFwd clamp is here
  - DESIGN_Eqs_Consuming_Behaviours.md — OWNS FallBack and its retreat query (FindSafeRetreatPoint)
  - REVIEW_Behaviour_Library_Genericity.md — §6 the retreat (R-259: FallBack is THE retreat)
-->

# Architect Question 88 — a soldier cannot run, and his retreat hides from only one threat

📌 **Measured** on `ua-universal-soldier` (`CE-3159`, motion + node-phase probes in `PostureScenarioTests.CE3094`, `d13b0945b` /
`53aba97f4`): wounded by Hostile 2 at ~55 m, the rifleman flees **upright at 2.00 m/s** with stop-and-go, stays in Hostile 2's
sight, is hit again, takes cover and is killed **walking to his hide point** (0.3 s into TakeCover, 0 rounds fired).
`CE-3097` (*"units move at ⅓ of Speed"*) is **refuted**: asked 2.00 ⇒ 2.00 m/s, asked 1.20 ⇒ 1.20 m/s.

## 1. INVENTORY *(2026-10-10)*

| query | total | what it settles |
|---|---|---|
| `search_graph name_pattern=.*(NavigationIntentBridge\|Locomotion.*System\|Steering.*System\|PathFollow.*System\|Crowd.*System\|MovementSystem\|KinematicsSystem).* label=Class` | 13 (5 production movers) | a SimHost soldier is moved by `CarKinematicsSystem` (`VehicleClass.Pedestrian` ⇒ `HumanGait`) — measured `VehicleState` present, `NavState CustomTrajectory`, no crowd agent |
| `grep MaxSpeedFwd` over TKB catalogs | `InfantrySoldier` 2.0, other pedestrian types 2.0 / 1.5 | the cap is the TYPE's, copied from the generic pedestrian preset (`VehicleClass.cs:119` *"Walking speed"*) |
| `grep (Speed\|RelocateSpeed) = ` over library behaviours | advance 3 · flank 3 · firing position 3 · fallback 2.5 · PeekAndFire relocate 4.5 · duel B 5 | every library ask above 2 m/s is clamped |
| `grep StanceRequest` in tactics/posture nodes | HoldProne only | nothing crouches while moving |

## 2. Claim table

| the leans rest on | code — how it IS | design — how it was MEANT |
|---|---|---|
| asked speed is honoured up to the type's cap | ✅ `CarKinematicsSystem.cs:326` `Min(finalTargetSpeed, MaxSpeedFwd)`; probe: asked 2.5/4.5 ⇒ 2.00 | ✅ `FDP.Toolkit.CarKinem.md` "Human gait"; ⛔ no design states a soldier's top speed (searched `docs/`+`.dev/`: none) |
| the authors expected a soldier to run | ✅ CombatPosture asks 3 / 2.5, PeekAndFire 4.5 (`PeekAndFireNodes.cs` defaults) | ✅ `DESIGN_Peek_And_Fire.md` §8.4 relocation 4.5 m/s ("a dash") |
| the retreat is hidden from ONE threat | ✅ `StarterTemplates.cs:79` `CheapLineOfSightTest { Viewer = Slot, Require = Hidden }` + soft `ThreatExposureTest` | ⛔ `DESIGN_Eqs_Consuming_Behaviours.md` FallBack: "hidden from the threat" (singular) — not searched further for a multi-threat rule |
| threat ranking cannot prefer the shooter | ✅ `ThreatRankingDecision.cs`: LOS, distance, threat level, health, assigned — no "hit me" input | ⛔ not searched |
| a moving soldier stays upright | ✅ only `PostureNodes.HoldProne` requests a stance | ✅ Nav design §11: stance speed ratios 1.0 / 0.5 / 0.2 — crowd agent only, not the SimHost mover |

## 3. Sub-questions

| # | question | ⭐ lean | rejected (one line each) | blast radius |
|---|---|---|---|---|
| **A** | a soldier's top speed | ⭐ **`InfantrySoldier` (and the other soldier types) `MaxSpeedFwd` 2 → 5 m/s, `MaxAccel` 1 → 2.5 m/s²** — the cap becomes a SPRINT; every behaviour still asks its own speed (advance 3, fall back 2.5, dash 4.5). The generic `Pedestrian` preset (civilians) stays 2 | keep 2 — every library ask above a walk is silently a walk · per-behaviour clamps — the type, not the behaviour, knows what the body can do | every infantry scenario whose behaviour asks > 2 m/s moves faster; timing-budget rails may tighten (they get MORE margin, not less) |
| **B** | moving under fire | ⭐ **later, not now** — a crouched run (`StanceRequest` Crouched + the Nav §11 0.5 ratio on the SimHost mover) is one change in two places; do A first and re-measure | do it now — two unknowns in one measurement | `HumanGait` speed target, `FallBack`/`PeekAndFire` moves |
| **C** | which threats a retreat hides from | ⭐ **the threat that is HITTING him wins**: `FallBack` points its sensor at the freshest damage source (the shot's shooter, `RecentSenses` Hit) when there is one, else the top threat — the PeekAndFire `FreshestEvidence` rule, reused | hidden from ALL threats — on open ground it answers nothing and the unit freezes · a "hit me" utility input — the ranking is the decision layer's (behaviors lane), a larger change | `EqsTacticsNodes.FallBack` sensor aim; the CombatPosture Flee option |

⇒ **Recommended order:** A (one TKB edit, re-run `CE3094` and the posture suite) → C → re-measure → B only if still needed.

## 4. Answers

*(awaiting the user)*
