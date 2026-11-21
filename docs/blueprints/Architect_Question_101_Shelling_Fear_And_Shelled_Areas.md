<!--STATUS
state: LIVE
updated: 2026-11-14
current-answer: §R — re-measured 2026-11-14 against the as-built stress model (CE-2270/2272); revised leans ✅ APPROVED 2026-11-14 (R-4227); ✅ BUILT 2026-11-14: CE-4584 (C), CE-4583 (D — node-level, see §R as built), CE-7015 (E)
stale-below: ⛔ HISTORY (§1–§3, 2026-10-22) — written two days BEFORE the stress model was built; its "soldier fear: none found" and its new `Composure` value are overtaken; do not quote
known-rot: §1 row "soldier fear, morale, composure, panic — none found" is false since 2026-10-24 (`Stress`, `StressSystem`, `BreakBehavior`)
related-designs:
  - ../DESIGN_Thermal_And_Acoustic_Sensing.md — §8.6a, R-2071 E-1…E-4 (this question is E-3 and E-4; E-1/E-2 are built there)
  - ../DESIGN_Civilians_And_Crowd_Control.md — the civilian fear owner (CivilianFearSystem) this would mirror for soldiers
  - ../designs/group-maneuvers/DESIGN_Squad_Tactics.md — the danger-area sensor and crossing drill E-4 would feed
  - ../DESIGN_Sensors_And_Doctrine.md — threat = danger × freshness; RecentSenses
  - ../DESIGN_Temperament_And_Stress.md — OWNS the per-man fear this question asked for (Stress, Nerve, Resolve, the break TS-6); §R makes it the only fear value
  - ../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — OWNS the platoon fall-back (E-3a, DA-66…DA-70) and the attack abort (AT-10) that §R's rout extends
  - ../designs/group-maneuvers/DESIGN_City_Battle_Showcase.md — §3a "morale and rout", the must-have this answers (CE-7015)
-->
# Architect Question 101 — soldiers under shelling: fear, panic, and the shelled area (R-2071 E-3, E-4)

🔒 **User, `2026-10-22`** (R-2071, CE-2348 ③): *"such an nearby explosion should somehow alert the soldiers if they were calm before, it
could also make them beight freightened (under heavy bombardment -> some of them with lower discipline can panic or collapse psychically
maybe) - but it should definitely not be an enemy position … it does not need to create a new dangerous area unless the explosions are
repeated there as that might be blocking them in movements"*

E-1 (a burst is never an enemy) and E-2 (it alerts the calm) are built in [Sensing §8.6a](../DESIGN_Thermal_And_Acoustic_Sensing.md).
This question is the two halves that need new machinery.

## §R — re-measured `2026-11-14` (coordinator), revised leans *(current — ✅ APPROVED `2026-11-14`, R-4227: "approved")*

🔒 User, `2026-11-14`: *"agreed with the leans but you need to measure the unknowns first"*.

**INVENTORY** — `scripts/cbm.sh search_graph name_pattern ".*(Detonat|Blast|Explosi|Splash|Fragment|Shell).*" label Class` (53, read) ·
grep `new Events.NearMissEvent` (1 producer) · grep `Stresses.IsBroken` (3 readers) · grep `CombatEffective.(FightingMembers|IsInTheFight)`
(9 files) · grep `Injury.IsInTheFight` (72 sites). ⚠ Graph coverage cannot be checked through the CLI.

| claim | code (how it is) | design (how it was meant) |
|---|---|---|
| a soldier HAS a fear value with a break | ✅ `Stress` (FDP), `StressSystem`, `BreakBehavior` — surrender / flee / prone, by Nerve and Resolve | ✅ [Temperament §7.2](../DESIGN_Temperament_And_Stress.md) TS-6, built 2026-10-24 |
| only a bullet raises a near miss | ✅ `BallisticsSystem.ReportNearMisses` is the only `NearMissEvent` producer; it also stamps `UnderFire` | ✅ §1 row "suppression — bullets only" still true |
| a burst within 10 m adds a little stress | ✅ a heard burst is an anonymous acoustic slot ⇒ `StressModel.HeardCloseUnseen` (`StressSystem.cs:300`) ⇒ +0.03/s × (1 − Nerve) for ~3 s ≈ one near miss | ⛔ searched, no design names bursts here — a side effect of CE-2376's "heard close" term. ⚠ read from code, not run |
| a burst beyond 10 m does nothing to a soldier | ✅ no other stress input; civilians DO fear it (`CivilianFearSystem`, Explosion 0.8 with falloff) | ✅ E-3 asks for it |
| a "shaken" soldier already acts differently | ✅ `StressDamp` modifiers on AdvanceAndAttack / Suppress (TS-C, CE-2271) | ✅ Temperament §7.3 |
| a platoon falls back when half its line fails | ✅ `PlatoonDefendBehavior.HalfTheLineFailing` → `FallBackOrders` (BreakContact to `FallBackTo`, authored or 100 m behind) | ✅ Defend & Attack E-3a, DA-66…DA-70 |
| …but a BROKEN man still counts as fighting | ✅ `SlotFailing` → `CombatEffective.FightingMembers` → `Injury.IsInTheFight` (Ok or Wounded); nothing reads `Stress` there | ⛔ AQ101 §2 B (2026-10-22) already wanted *"a squad's runner sees a broken man as not in the fight"*; never built |
| an attack breaks off only when the assault is Black | ✅ `PlatoonAttackBehavior.AssaultLost` (AT-10) — casualties only | — |
| a squad on its own never falls back | ✅ `SquadDefendBehavior` moves a man to his alternate post on `PositionUntenable`; no squad fall-back | ⛔ searched, none |
| the shared fighting count has wide readers | ✅ `CombatEffective` feeds the SITREP colour (`StaffSystems`), the unit centroid, the replay search, callouts | ✅ CB-37 "the shared SITREP colour is not changed" (`SlotFailing`'s own note) |
| a danger area can be dynamic | ✅ `DangerAreaKind.GasCloud = 6` (CE-3227) | ✅ §2 D's precedent stands |

**Revised leans**

| # | lean | why | blast radius |
|---|---|---|---|
| **A** | ⛔ **no `Composure`** — `Stress` is the soldier's fear | one implementation per concept (ruling 9); it already has the falloff, calm, cover, leader and nerve terms §2 A asked for | none |
| **B** | ⭐ **already answered by the as-built** — shaken = `StressDamp`, broken = the break (panic = flee, collapse = prone, Resolve decides surrender); no new bands | the design §2 B wanted exists under other names | none |
| **C** · `CE-4584` | ⭐ **a burst within its injury radius × 2 publishes `SensorChange.NearMiss` and stamps `UnderFire`**, as a bullet does — one more caller of the same two calls | then stress, suppression, the squad's under-fire sense and civilian fear all follow with no new code | one new publisher beside the area effect; existing readers |
| **D** · `CE-4583` | ⭐ unchanged — 3 bursts in 60 s ⇒ a `Shelled` danger area (GasCloud precedent) | — | danger-area sensor + crossing decision |
| **E (new) — the rout** · `CE-7015` | ⭐ **the fall-back and abort guards count a broken man as lost**: one helper beside `CombatEffective` (*fighting and not broken*) read by `SlotFailing` and `AssaultLost`'s count; the shared SITREP count stays as it is | the rout machinery exists (DA-66…70, AT-10); it is blind only because the count is casualties. A broken platoon then BreakContacts to its fall-back point — the §3a picture | two guards + one helper; SITREP, centroid, replay search untouched |
| **E2** | ⏭ a squad on its own falls back on the same rule — later | the showcase fights in platoons; no squad fall-back exists to extend | — |

**As built `2026-11-14` (showcase-brains):** C = `CE-4584` — `HitModel.NearMiss`, the one near miss both callers use; the ring is 2 × the
blast INJURY radius (the field of that name, ~10–15 m for a mortar bomb), every person in it, not the struck man
([Temperament §7.6](../DESIGN_Temperament_And_Stress.md)). E = `CE-7015` — `CombatEffective.IsStanding` / `StandingMembers`; `SlotFailing`
(both arms + the baseline) and `AssaultBlack` (⚠ shared by AT-9 and AT-10 — one predicate for both, else a broken assault with a free reserve
is neither relieved nor aborted); `Stress` declared in `BrainReads` ([Defend & Attack DA-67, AT-10](../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md)).
D = `CE-4583` — ⚠ **deviates**: the heat is node-level on the solving node (the Muscle), not in the brain's squad pool — the solve runs where the
navmesh is; the gas precedent is node-level ([Squad Tactics §7.7](../designs/group-maneuvers/DESIGN_Squad_Tactics.md)).

Would change the leans: if the SITREP should report broken men as lost too (then E changes `CombatEffective` itself, and the colour, centroid
and replay search move with it); if a near burst should frighten but not spoil aim (then C publishes only the sense).

## ⛔ HISTORY — the 2026-10-22 question (superseded by §R)

## 1. INVENTORY — what exists (measured `2026-10-22`)

Queries: `grep -rn "IsAnonymous"` and `grep -rni "fear|morale|suppress|alertness"` over `FDP/Toolkits/Fdp.Toolkits` and
`Hrot/Subsystems/Hrot.AI.Behaviors` (excluding tests); `grep DangerArea|PositionHeat|AreaCost`. ⚠ The graph's coverage check is not
available through the CLI, so "none found" below is from these searches.

| concept | exists? | where |
|---|---|---|
| civilian fear 0..1, an explosion impulse 0.8 halved at 100 m, decay 0.05/s | ✅ civilians only | `Civilians/CivilianFearSystem.cs:20-121` (`IsAfraidKind` lets only civilians in) |
| soldier fear, morale, composure, panic | ⛔ **none found** | `PanicFlee` is a civilian behaviour (`BehaviorCatalog.cs:25`) |
| soldier "alerted" | ✅ derived, not stored | `SopConditions.InContact` (memory not empty); the `FirstThreat` sense |
| suppression (aim spoiled 5 s) | ✅ bullets only | `HitModel.StampUnderFire`, called from a near miss or a hit — never from a blast or a sound |
| proficiency / discipline per man | ✅ proficiency (DA-A, CE-2273) | read by the posture and peek decisions |
| a danger area | ✅ route geometry only | `Squad/DangerArea/*` (OpenGround, StreetCrossing, Intersection, **GasCloud** — a dynamic hazard capsule, CE-3227) |
| decaying heat on a point | ✅ per unit, own firing points | `Combat/FiringPositionMemory.cs` `PositionHeat` (half-life 45 s) |
| movement that avoids an area | ⛔ none | `INavmeshProvider.PathCost` takes a layer mask and doors only |

## 2. The questions

| # | question | ⭐ lean | why | blast radius | would change the lean |
|---|---|---|---|---|---|
| **A** | where does a soldier's fear live? | ⭐ a new per-man **`Composure`** value 0..1 (1 = steady), on soldiers only, beside `RecentSenses`. A near burst takes `impulse × (1 − d / audible range)`; a hit or near miss takes more; it recovers over time, faster in cover and faster with a leader near | the civilian fear system is the model (impulse, falloff, decay) but civilians flee at low fear and soldiers must not. A separate value keeps the two from bending each other | one component, one system on the brain; readers opt in | if you want ONE fear value for civilians and soldiers, with the response differing by person type |
| **B** | what does fear DO to a soldier? | ⭐ three bands. **Shaken** (< 0.6): hug cover, slower to expose (PeekAndFire's hide time × 1.5), no advance by choice. **Pinned** (< 0.3): stays down, fires only when fired on at close range. **Broken** (< 0.1): for a man whose discipline is below Regular, a seeded roll each shelling minute: **panic** (runs away from the bursts to the nearest cover behind) or **collapse** (stays down, does nothing, until composure rises past 0.3). Veteran and above never break | your words: lower discipline panics or collapses; everyone else is frightened, not broken. Bands map onto existing levers (hide time, the posture choice, the SOP) | posture decision, PeekAndFire, the SOP gate; a squad's runner sees a broken man as not in the fight | if a broken man should recover only when his leader reaches him |
| **C** | does a burst alone count as "under fire" (suppression)? | ⭐ **yes within the burst's injury radius × 2**: the burst stamps `UnderFire` (aim spoiled) and a `NearMiss` sense, as a bullet passing does | a shell landing 10 m off is at least a near miss; today only bullets suppress | the hit model's stamp gains a second caller | if you want blasts to frighten but not spoil aim |
| **D** | the shelled area (E-4) | ⭐ the squad pool keeps **burst heat**: a burst adds heat at its point (merged within 15 m); **3 bursts within 60 s** make a **`Shelled` danger area** (a new `DangerAreaKind`, a circle round the bursts, the GasCloud precedent). The march's danger sensor then treats it like any threatened area: the crossing decision picks **Hold** or a way round. It cools after 2 min without a burst | your "only if repeated, as it might block movement". The danger-area machinery already makes a squad stop, wait or cross; navigation has no area cost, so routing round it is a later item | danger-area sensor, the crossing decision's inputs; nothing in navigation | if you want the area to change the PATH (that needs an area cost in navigation: backend) |

## 3. Proposed order

E-1/E-2 are built now (no question). On approval: **A + C** together (the value and its inputs), then **B** (what it does), then **D**.
A demo with each: a squad under a mortar barrage (a Veteran squad steadies, a Green one breaks).
