<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: §2 — four sub-questions, each with a lean; awaits the user
stale-below: nothing
related-designs:
  - ../DESIGN_Thermal_And_Acoustic_Sensing.md — §8.6a, R-2071 E-1…E-4 (this question is E-3 and E-4; E-1/E-2 are built there)
  - ../DESIGN_Civilians_And_Crowd_Control.md — the civilian fear owner (CivilianFearSystem) this would mirror for soldiers
  - ../designs/group-maneuvers/DESIGN_Squad_Tactics.md — the danger-area sensor and crossing drill E-4 would feed
  - ../DESIGN_Sensors_And_Doctrine.md — threat = danger × freshness; RecentSenses
-->
# Architect Question 101 — soldiers under shelling: fear, panic, and the shelled area (R-2071 E-3, E-4)

🔒 **User, `2026-10-22`** (R-2071, CE-2348 ③): *"such an nearby explosion should somehow alert the soldiers if they were calm before, it
could also make them beight freightened (under heavy bombardment -> some of them with lower discipline can panic or collapse psychically
maybe) - but it should definitely not be an enemy position … it does not need to create a new dangerous area unless the explosions are
repeated there as that might be blocking them in movements"*

E-1 (a burst is never an enemy) and E-2 (it alerts the calm) are built in [Sensing §8.6a](../DESIGN_Thermal_And_Acoustic_Sensing.md).
This question is the two halves that need new machinery.

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
