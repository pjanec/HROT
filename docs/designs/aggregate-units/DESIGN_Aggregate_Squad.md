<!--STATUS
state: LIVE
build-state: DESIGN — decisions D-1…D-9 are leans for the user (2026-11-14); the shape is ruled (R-4244)
updated: 2026-11-14 (first version — coordinator)
current-answer: the whole file — §1 the request · §2 INVENTORY · §3 diagrams · §4 decisions · §5 every squad behaviour with one member · §6 real-squad defects found · §7 slices · §8 acceptance · §9 design docs checked
stale-below: nothing
known-rot: none
known-conflict: DESIGN_Squad_Tactics.md §7.6.3 ("fire and movement ends … a half gone") — D-5 changes it for a squad down to one man; folded there when built
related-designs:
  - MEASURED_Aggregate_Squad_Seams.md — OWNS the measurements behind every row here (two sweeps, calibration numbers, R-4244 §8)
  - ../group-maneuvers/DESIGN_City_Battle_Showcase.md — §12 the test ladder: the aggregate battle is a rung between ② (puppet bench) and ③ (dry twins)
  - ../group-maneuvers/DESIGN_Command_Chain_Example.md — OWNS orders down / reports up, the seam the aggregate squad plugs into; §8e the battalion's reserve
  - ../group-maneuvers/DESIGN_Squad_Tactics.md — OWNS the squad plays §5 classifies (bounding, fire and movement, suppress and manoeuvre, crossing, march)
  - ../group-maneuvers/DESIGN_Defend_And_Attack_Position.md — OWNS SquadDefend (D-2a…D-2m, the lone man's round DL-4, the MG's post D-2h)
  - ../group-maneuvers/DESIGN_Building_Clearing.md — OWNS the room drill and ClearBuilding (§6a reduced squads) the aggregate version replaces for this class
  - ../../DESIGN_Orders_Params_Entity_Classes.md — OWNS order routing (`Order.<name>` → `<Class><Order>`, R-273, D3-C) that D-1 extends to the squad orders
  - ../weapons/DESIGN_Weapon_Layers.md — OWNS one fire channel per entity and the turret ruling D-4 leans on
  - ../../DESIGN_Scenario_Rules.md — §10.2 the objective census D-2's seam feeds
-->

# DESIGN — the aggregate squad: a squad unit with ONE representative member *(R-4244)*

## 1. The request

🔒 User, `2026-11-14`: *"replace the squads with a puppet squad, meaning just the Squad commander will play for the whole squad … a great base for
simulating larger battles without individual units"* · *"We go with the aggregate squad … keep the vehicles in the play … approximate the squad
position and firepower with the aggregate squad, carrying weapons and sensors"* · *"record the decision to have just one member. In addition to the
virtual commander"* · *"either let's design these behaviors to work even with a single member squad … or to replace with specific aggregated squad
behaviors"* · *"how to deal with … placing squad members … the machine gunner likely needs different position … will it be able to use them at
once"* · *"leave the doors open to … fully aggregated squads that are having no real member … simulated with probabilistic calculations."*

**Why:** prove the command chain (battalion → company → platoon → squad) with ~80 entities instead of ~1 000, and start constructive simulation.

## 2. INVENTORY *(measured 2026-11-14; nine read-only sweeps, the decisive claims re-read by the coordinator — detail in [the measurements](MEASURED_Aggregate_Squad_Seams.md))*

| query | found |
|---|---|
| `search_graph` `.*(Aggregate\|Constructive\|Lanchester\|Attrition).*` Class (3) · `.*(Aggregate\|Representative).*` · `.*Puppet.*` (0) | ⛔ no aggregate model; `AggregateState` (Vis2D) has no writer or reader |
| `search_graph` `.*Play$` Class (19) + the runner | the squad plays: one runner, one element partition at Start (`SquadManeuverRunner.cs:406`), gates that pass on an empty element (`SquadGates.cs:61-90`) |
| `search_graph` `.*(SquadRole\|CoveragePost\|PostPlan\|SquadDefend\|FireTeam).*` (8) · `.*(Coverage\|Role\|Gunner\|Mount\|WeaponState\|FireIntent\|AimAndFire\|AmmoChoice\|ThreatDanger).*` (67) | one role reader (`SquadRoles.CarriesSupportWeapon`), four users; one fire channel per entity |
| grep `IsHidden`, `CombatEffective.*`, `ObjectiveCensus.*`, `CommandOrders.Assign/Order`, `OrderResolver` | ✔ a unit is hidden (`PhysicalPresence.cs:27`); strength = men counted (`CombatEffective.cs:54-56`); platoons order squads by concrete name (`CommandNodes.cs:80-86`) |
⚠ Graph coverage not checkable through the CLI; every absence above is also a grep.

| the proven solution for the problem class | adopted / adapted — why |
|---|---|
| constructive simulators move UNITS (position, strength, posture) and resolve combat by attrition (Lanchester-type, range / cover / suppression factors); mixing units with individual entities is the hard multi-resolution problem | ⭐ **adapted**: v1 resolves combat through ONE representative entity that uses the EXISTING sensors, weapons and damage — so a real tank, mortar or aircraft meets it with no new interaction code (multi-resolution solved by construction); the probabilistic resolver stays a second implementation of the same two seams (D-2) |

## 3. Diagrams

### 3.1 Classes

```mermaid
classDiagram
  direction LR
  class SquadUnit {
    <<existing, hidden UnitEntity>>
    UnitRoster · CommandPicture · BehaviorState
    the virtual commander: orders in, reports out
  }
  class AggregateSquad {
    <<NEW unmanaged component on the unit>>
    Represents : men authored
    Men : float, men left
    Suppression
  }
  class Representative {
    <<NEW TKB type, person class>>
    sensors · mounts: small arms scaled, RPG
    Health = Represents x 25
    no injury model
  }
  class SquadMen {
    <<NEW static seam>>
    Of(unit) float
    Of(member) float
  }
  class AggregateResolution {
    <<seam>>
    Detect · ResolveLosses
  }
  class ByRepresentative {
    <<v1>>
    real sensors, weapons, damage
  }
  class ByProbability {
    <<later>>
    no member, attrition over unit points
  }
  class CombatEffective { <<existing, EXTENDED>> FightingMembers from SquadMen }
  class StaffSystems { <<existing, EXTENDED>> StrengthOf from SquadMen }
  class ObjectiveCensus { <<existing, EXTENDED>> men weighted by SquadMen }
  class SquadInputs { <<existing, EXTENDED>> OwnStrength from SquadMen }
  class ThreatDanger { <<existing, EXTENDED>> danger x men }
  SquadUnit "1" o-- "1" Representative : roster
  SquadUnit *-- AggregateSquad
  AggregateResolution <|.. ByRepresentative
  AggregateResolution <|.. ByProbability
  SquadMen ..> AggregateSquad
  CombatEffective ..> SquadMen
  StaffSystems ..> SquadMen
  ObjectiveCensus ..> SquadMen
  SquadInputs ..> SquadMen
  ThreatDanger ..> SquadMen
```
*What it shows that prose hid: the squad's state lives on the UNIT (`AggregateSquad`), not on the member — so every reader (strength, colour,
census, ratios, threat) goes through ONE seam (`SquadMen`) and does not care whether a representative or a probability model fills it. That is
the open door to the fully aggregated squad.*

### 3.2 One engagement — a real tank meets an aggregate squad

```mermaid
sequenceDiagram
  participant P as Platoon (unchanged)
  participant U as Squad unit (hidden)
  participant R as Representative
  participant T as Real tank
  participant S as Staff (reports)
  P->>U: order (SquadDefend, by name today, by Order.Defend after D-1)
  U->>R: post = the MG's post (longest view, D-3)
  R->>R: sensors see the tank, memory feeds the squad pool
  U->>S: SPOT / CONTACT, unchanged pipeline
  R->>T: WeaponChoice picks the RPG mount (armour)
  T->>R: coax rounds hit, Health falls, capped at 2 men a hit (D-6)
  R->>U: AggregateSquad.Men = Represents x Health / Max
  U->>S: SITREP colour from SquadMen
  S->>P: Amber, the platoon reacts as for a real squad
```
*What it shows: nothing between the tank and the representative is new — sight, weapon choice, rounds and damage are today's paths; the only new
step is the strength read-back to the unit, and everything the platoon sees comes from it.*

### 3.3 Who runs what

```mermaid
graph TD
  BR[Brain - per-entity phase] --> PL[Platoon / Company / Battalion - unchanged]
  BR --> SQ[Squad plays - one member]
  SQ --> REP[Representative: AimAndFire, sensors]
  MU[Muscle] --> KIN[walker kinematics moves the representative]
  MU --> DMG[HealthApplication - the per-hit cap]
  DMG --> MEN[AggregateMenSystem - serial: Men from Health]
  MEN --> SEAM[SquadMen read by CombatEffective, StaffSystems, census, ratios, threat]
  LATER[ByProbability resolver]:::later -.writes Men, no member.-> MEN
  classDef later stroke:#888,color:#888,stroke-dasharray: 5 5
```
*What it shows: the one new per-frame system writes the unit's `Men` from the representative's health; a later probabilistic resolver writes the
same field with no member (dashed) — nothing downstream changes.*

## 4. Decisions — leans for the user

| # | decision | ⭐ lean | why | rejected |
|---|---|---|---|---|
| **D-1** | how an aggregate squad gets its OWN version of a play | ⭐ **the order routing, staged** (`Order.<name>` → `<Class><Order>`; class `AggregateInfantrySquad` = `11.1.*.1.2.2`, TKB 307, more specific than `InfantrySquad`, falls back to it) — FIRST make the order bookkeeping order-aware (record the order + its resolved name, the CE-4307 sender-side precedent) and fix the three readers that compare a behaviour hash (`StillRunning` / `Gave`, `NotDefending`, `HoldsFirm`), THEN move the platoons' squad orders one by one (Defend first) | it is what the routing was built for (R-273), the user's model ("orders will be translated to whatever the unit can do"), and the staging also fixes a latent bug (a squad given `Order.Hold` breaks contact today — `HoldsFirm` misses `InfantrySquadHold`) | a dispatch inside each behaviour (a second mechanism for one concept) · moving every call site at once (CE-2346 / CE-2359 return) |
| **D-2** | where the squad's state lives | ⭐ **on the UNIT**: `AggregateSquad { Represents, Men, Suppression }`; ONE seam `SquadMen.Of` feeds `CombatEffective`, `StrengthOf`, the census, `SquadEnemyStrengthRatio`, `ThreatDanger`, target weights (FPV, spotter) and seats; v1 writes `Men` from the representative's health | the open door to the fully aggregated squad (user): a probability resolver writes the same field | state on the member only (the no-member squad would have nowhere to keep it) |
| **D-3** | where the one member stands | ⭐ **where the squad's firepower decides**: in a defence with a threat area, the MG's post — the longest view down it (D-2h's view fan over the candidate posts, run for the lone man); all round, coverage rank 0 + the lone man's guard round (DL-4, already built); in a march, the lead; elsewhere what the play gives its first element | the MG is the squad's base of fire (DA-48, DA-19); the member carries it | the centre of the posts (an unaimed compromise) · the leader's post (the unit is the leader) |
| **D-4** | firing the squad's weapons | ⭐ **v1: one fire stream, scaled** — the small-arms mount's rate, ammo and spread scaled to the men left (`SquadMen`), `WeaponChoice` puts the RPG on vehicles; simultaneous fire at two targets is **not** in v1 | one channel per entity (`ChannelComponents.cs:29`) and per-mount tasks were rejected for turrets (Weapon Layers §205-215); the VOLUME of fire is what suppresses and attrits | a second fire loop for the MG now (a separate decision: hull-fixed mounts were never ruled) |
| **D-5** | the plays that fail with one man | ⭐ **make them work with one man** (they fail for a REAL squad's last man too): fire and movement (Start `live < 1`; an empty half is skipped, not a failure), break contact's smoke (the mover throws it when nobody covers); **aggregate versions only where one man cannot mean the squad**: suppress-and-manoeuvre's assault and `ClearBuilding` (§5) | R-4244: no hack into the half logic — a play either works with one man or is replaced | — |
| **D-6** | damage to a representative | ⭐ health = `Represents × 25` (a rifle hit ≈ one man); a hit removes at most **2 men**; a burst's fragments scaled by the squad's frontage (`PresentedAreaM2`); no injury model | measured: a 125 mm hit (1100) would empty the pool; a burst tests one man-sized body | per-round dispersion (no hook; under-represents, never over) |
| **D-7** | the squad's nerve | ⭐ squad-level stress on the representative: stimuli scaled `1/Represents`, "alone" off, leader calming on, outnumbering by `SquadMen` | measured: ~4 hits break one body standing for 8; "alone" grows for ever | per-man stress unchanged (it routs the squad on four hits) |
| **D-8** | seats | ⭐ a representative takes `ceil(Men)` seats | else one carrier "carries" 12 squads | one seat |
| **D-9** | role tag | ⭐ the representative is the squad's `SquadLeader` (succession is then a no-op) and carries the MG mount; ⚠ check DA-13 (snipers rank MG gunners) before tagging it `MachineGunner` | the leader-only case is designed (`SquadSensing.cs:113-117`) | — |

## 5. Every squad behaviour with ONE member *(analysis of design + code, no runs)*

| behaviour | one member — what happens | verdict |
|---|---|---|
| SquadDefend | one post, Set when he reaches it, the lone man's round (DL-4 designed for him), Displace / Flanked / ShootAndMove work (`SquadDefendBehavior.cs:395, :586, :807, :868-881`) | ✅ works · D-3 adds the MG post for a threat area |
| BoundingOverwatch | bounds; the empty element passes on the next tick; Success → Set (`BoundingOverwatchBehavior.cs:113, :156, :182-201`) | ✅ works (one tick of hold per switch) |
| FireAndMovement | Start fails `live < 2` (`FireAndMovementBehavior.cs:66`), Outcome fails on an empty half (`:100-104`) | 🔧 D-5 fix; ⚠ changes Squad Tactics §7.6.3 |
| SuppressAndManeuver / `InfantrySquadAttack` | Start fails `live < 2` (`SuppressAndManeuverBehavior.cs:120`) | ♦ aggregate version: the member holds as the base of fire; the assault is a timed strength exchange (attacker and defender `SquadMen`, intensity, cover), ending Success / TaskFailed |
| SquadDangerCrossing | crosses alone, empty gates pass, Success | ✅ works |
| BreakContact | moves to the answer, Success within 8 m; no smoke (only a coverer throws, `SquadContactBehaviors.cs:241`) | ✅ works · 🔧 smoke by the mover |
| SquadMarch / Move | he is the lead; Success on arrival | ✅ works |
| SquadCover | an empty element is Ready (`SquadCover.cs:66`) | ✅ works |
| ReactToContact | ratio counts one body (`SquadInputs.cs:251-282`) ⇒ breaks contact against anyone; assault falls through two failing plays to Hold | 🔧 the D-2 seam for the ratio; an assault that cannot start becomes Hold at choice time |
| BasicSquadSop rows | UnderFire, Contact, AirThreat work; LeaderDown / MemberDown inert (correct) | ✅ works |
| CasualtyCare / SquadRecover | inert for the representative (no injury, D-6) | ✅ (aggregate) · see §6 for real squads |
| ShelterFromAir · Coordination · Callouts | per member, works; a lone man never calls (optional exemption) | ✅ works |
| ClearBuilding / ClearRoom | the room drill runs with one man (entry size 1, `RoomEntryPlan.cs:177`) — mechanically fine, wrong for a member standing for 9 | ♦ aggregate version — the contract in [the measurements](MEASURED_Aggregate_Squad_Seams.md) §5 / the building sweep: same DTO, survey, a time per room (38–50 s measured, scaled by area and intensity), a strength exchange with the occupants per room, Foothold / RoomClear / BuildingClear reports, Success / Failure |
| Embark / Dismount / FastRope / AirborneExit | per body, work | ✅ works · D-8 seats |

## 6. Real-squad defects the analysis found *(independent of the aggregate — filed CE-4605…CE-4610)*

| row | defect | evidence |
|---|---|---|
| CE-4605 | ⭐ suppress-and-manoeuvre reports **Success with nobody having assaulted** when a reduced squad's survivors all fall in the base half (the split counts dead slots) | ✔ `SuppressAndManeuverManeuver.cs:69-76`, `SuppressAndManeuverBehavior.cs:121, :152-166` |
| CE-4606 | a 2-man squad with one down **livelocks** in casualty care (the survivor is the leader, `MedicFor` skips the leader) | `CasualtyCareBehavior.cs:282-287`, `SquadContactBehaviors.cs:437, :465-470` |
| CE-4607 | a convoy **never finishes** when a squad's only man is down (`LiveMen` counts the down man) | `PlatoonConvoyBehavior.cs:242-245, :406-408, :525-534` |
| CE-4608 | a spent assault squad relieved to reserve **keeps clearing** instead of holding the foothold (Building Clearing §6a) | `PlatoonAttackBehavior.cs:532, :664-672` |
| CE-4609 | a squad given `Order.Hold` **breaks contact** (`HoldsFirm` misses `InfantrySquadHold`) | `SquadContactBehaviors.cs:390` |
| CE-4610 | no producer for PROGRESS FallingBack / PhaseLine / ObjectiveSecure / FireLifted, LACE, ammo colour — the company reads FallingBack (`CompanyBehaviors.cs:804`); Building Clearing CB-3 vs CB-51 disagree on the two-man entry | the designs name senders (`DESIGN_Platoon_Attack_Defend_Clear.md:163, :199, :203`) |

## 7. Slices

| slice | builds | needs |
|---|---|---|
| **A-0** | the real-squad defects CE-4605…CE-4609 (they make every squad better, and the aggregate rests on the same plays) | — |
| **A-1** | D-1 staged: order-aware bookkeeping + the three hash readers; then `Order.Defend`, … one order at a time | A-0 (CE-4609) |
| **A-2** | the type (TKB 307 + the representative type), `AggregateSquad` on the unit, `SquadMen` and its readers (D-2), seats (D-8), role (D-9) | — |
| **A-3** | the combat model: scaled small arms (D-4), damage cap and burst frontage (D-6), squad nerve (D-7), the MG post (D-3), fire and movement / break contact with one man (D-5) | A-2 |
| **A-4** | the aggregate plays: assault, ClearBuilding | A-1, A-2 |
| **A-5** | the command-hierarchy scenario on aggregates (battalion → squads, ~80 entities; vehicles, mortars, drones real) + `Strength` / `QuietArea` objective kinds + the battalion reserve (CE-4589) + live checks and the dynamism measure (CE-7033) | A-3, A-4 |

## 8. Acceptance

1. A real tank, a mortar burst and an FPV drone each damage a representative through today's paths; it fires back with its RPG at the tank (rails).
2. The platoon sees Green → Amber → Red as the representative loses health; the census counts `Men`; a 125 mm hit removes ≤ 2 men (rails).
3. Every §5 ✅ row has a one-member rail; every 🔧 row is fixed and its rail red-proved; the two ♦ rows run as the aggregate class only.
4. A-1: every existing squad suite byte-identical in choices (the same play runs); `StillRunning` / `NotDefending` / `HoldsFirm` rails on routed orders.
5. A-5: the battalion → squad scenario runs 20+ minutes in seconds of run time with phase lines, tempo, a support call and the reserve, judged by its live check; `HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` + `stepped-hash.py --threads 1,4,8` equal.

## 9. Design docs checked

Squad Tactics §3, §6.1, §7.6–§7.7 — applies (the plays; §7.6.3 changes, known-conflict) · Defend and Attack Position §8c D-2h / D-2m / DL-4, DA-19,
DA-48 — applies (the lone man and the MG post) · Building Clearing §6a, CB-3 / CB-51 — applies (reduced squads; the contradiction filed) · Orders,
Params and Entity Classes §5a, §7.4, §8, R-273, R-277, R-278 — applies (D-1) · Weapon Layers :205-215 and Weapon Systems :354, :427 — applies (D-4,
one channel; self-controlled weapons) · Temperament and Stress TS-4 / TS-7 — applies (D-7) · City Battle §12 — applies (the test ladder) ·
Command Chain §8e — applies (A-5's reserve) · ⛔ no aggregate / constructive design existed (searched `docs/` and `.dev/`).
