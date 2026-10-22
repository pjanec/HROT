<!--STATUS
state: LIVE
updated: 2026-10-18 (first version — every built doctrine rule, what implements it, every demo; extracted from the owning designs and checked against code)
current-answer: the whole file — an INDEX (rules → implementers → demos), not a design. Every row names its owning design §; the design wins on any disagreement
stale-below: none
known-rot: statuses are a 2026-10-18 snapshot — re-check the owning design's demo table before relying on a ✅
related-designs:
  - CAPABILITIES.md — the reader's summary ABOVE this index (features by chapter, one demo each); PLAN_Feature_Films.md films them
  - OVERVIEW_Behaviour_Building_Blocks.md — OWNS the machinery map (decision slots, sensors, EQS, library, groups, vehicles); this file is the RULES and DEMOS built on it
  - DESIGN_Civilians_And_Crowd_Control.md — OWNS §2–§3 here (CV-, AW-, CH-, KC-, CO-, FL-, EL-, BM- rules; §5c demo table)
  - DESIGN_Decision_Layer.md — OWNS ROE (Roe.Fire, ReturnFireWindowSeconds, ForceLevel) used by the firing gate (§1)
  - DESIGN_Sensors_And_Doctrine.md — OWNS threat = danger × freshness, sensor kinds (§4)
  - DESIGN_Utility_AI_Demo_Scenarios.md — OWNS U1–U7 posture/ranking/weapon-choice/fire-distribution/danger-area rules and demos (§4)
  - DESIGN_Eqs_Consuming_Behaviours.md — OWNS TakeCover / FallBack / Flank / FiringPosition (§4)
  - DESIGN_Peek_And_Fire.md — OWNS fight-from-cover, aim, position heat (§4, §6)
  - designs/group-maneuvers/DESIGN_Squad_Tactics.md — OWNS squad manoeuvres and casualty care (§5)
  - DESIGN_Entity_Interactions.md — OWNS embark, carry, pick-up (§5)
  - DESIGN_Injury_States.md — OWNS ok / wounded / incapacitated / dead (§4)
  - DESIGN_Building_Interiors.md — OWNS doors, blast and fragments in buildings, cover points (§6)
  - designs/hill-attack/DESIGN.md — OWNS the hull-down platoon attack (§7)
  - designs/vehicle-tactics/DESIGN_Urban_Armour_Tactics.md, DESIGN_Armour_Hit_Model.md, DESIGN_Missiles_And_Ammo.md, DESIGN_Vehicle_Exposure_Cycle.md, DESIGN_FPV_Drone.md — OWN §7–§8
  - designs/weapons/DESIGN_Weapon_Systems.md, DESIGN_Weapon_Layers.md — OWN weapon systems and the brain/muscle split (§10)
  - designs/air-attack/DESIGN_Air_Operations.md — OWNS air attack, air defence, air assault (§8)
  - designs/indirect-fire/DESIGN_Indirect_Fire.md — OWNS fire missions (§9)
  - designs/airborne/DESIGN_Airborne_Insertion.md — OWNS fast rope, parachute, eject (§10)
-->

# Rules and demos — what is built, what does it, how it is shown

- **What this is:** one row per built doctrine rule → the code that does it → the design § that owns it; one row per demo → what it shows → how it must end → which rules it proves.
- **What it is not:** a design. The owning design wins. The machinery (slots, sensors, EQS, library) is [OVERVIEW_Behaviour_Building_Blocks.md](OVERVIEW_Behaviour_Building_Blocks.md).
- **Legend:** ✅ live check passes · ⚠ passes with a caveat · ⏸ parked · ⏳ not built · ⛔ superseded (the row gives the current rule)
- **Demo tooling:** every demo is `scenarios/<name>/scenario.json`, a live check `python3 scripts/utility-demo-check.py --launch <name>`, and (most) an in-process twin in `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/PostureScenarioTests.cs` that checks every frame.
- **Code roots:** `FT/` = `FDP/Toolkits/Fdp.Toolkits/` (engine-side rules, systems, executors) · `BR/` = `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/` (behaviours and nodes).

## 1. The firing gate — every AI round passes it

`AimAndFireExecutor.HoldOf` (`FT/Combat/Executors/AimAndFireExecutor.cs`). An operator's own fire skips it. The order is the precedence.

| # | check | holds as | rule |
|---|---|---|---|
| 1 | target is Neutral | `hold: neutral` | CE-2233 (CV-8) |
| 2 | target shows hands up | `hold: hands up` | CV-13a |
| 3 | lethal round at a non-combatant he neither sees armed, knows, nor is aimed at by | `hold: unarmed civilian` | CH-1 |
| 4 | police ladder: warn if due; at the Warn rung, no round | `hold: warning` | EL-1…EL-6 |
| 5 | round above the unit's force level (self-defence lifts it for THAT target) | `hold: force level` | FL-2, FL-3 |
| 6 | round wrong for the target's domain (SAM at a truck) | `wrong domain` | A7 |
| 7 | missile inside its minimum range | `too close` | A11 |
| 8 | mount cannot fire on the move | `hold: moving` | A12 |
| 9 | bomb before its release cue | `awaiting release` | O3 |
| 10 | fixed gun outside its nose cone | `out of arc` | O7 |
| — | later, while aiming: reloading · not seen · aim clock · turning · friend on the line · **neutral on the line or in the spread** (AW-20) · masked muzzle · `Roe.Fire` / return-fire window | as named | PF, AW-20, ROE |

## 2. Civilians and crowds — [Civilians design](DESIGN_Civilians_And_Crowd_Control.md) §4–§5g

**Rules**

| id | rule | built by | § |
|---|---|---|---|
| CV-1 | one civilian family (types 1101–1104); class `Civilian`; default behaviour `CivilianHsm` | `CivilianTkbCatalog`, `EntityClassTable` | §4, §7.2a |
| CV-3 | one HSM per civilian; modes are its states | `CivilianBehavior` (`CivilianHsm`) | §5d |
| CE-2235 | heard shots/near misses → fear; fear fades 0.05/s | `CivilianFearSystem` | §5d |
| CE-2235 | Routine ⇄ Alarmed at fear 0.3 / 0.1; wounded → Casualty (no exit) | `CivilianBehavior` | §5d |
| CE-2236 | fear ≥ 0.6 → Panic: flee 20 m, look for shelter; none in 3 s → Cower | `CivilianBehavior.PanicState`, `FindEnterableShelter` | §5e |
| CV-7 | shelter = an openable door → inside → hide time → removed | `EnterableDoorGenerator`, `InsideTest`, `DoorPassageSystem` | §5e |
| CE-2236 | Cower ends in Alarmed (⛔ not Panic) | `CivilianBehavior`, `Effects.SetCowering` | §2.2, §5e |
| CV-5 | a bodiless crowd forms from civilians within 15 m: mood, focus, contagion | `CrowdSystem`, `Crowd`, `CrowdRules` | §5f |
| CE-2237 | calm civilians within 80 m of a casualty gather on a 4–9 m ring | `CrowdSystem`, `CivilianBehavior.GatherState` | §5f |
| CE-2237 | contagion: a member within 20 m pulls fear up to 0.9 × his | `CrowdSystem` | §5f |
| CE-2240 | protest = a parameter: ring 8–15 m, 40 % HoldSign, rest FistRaised; only Panic ends it | `CivilianBehavior.ProtestState` | §5g |
| CV-13 | one replicated gesture byte; brain writes it, maps draw it | `Gestures` / `Gesture`, gesture translators | §7.7 |
| CV-4 | a hostile ACT flips the side to Hostile (one write path, replicated) | `HostileActs.Commit` | §7.1a |
| CE-2233 | neutral danger 0 · never a pick · never a round · not a threat edge · not a posture input | `ThreatDanger.IsNeutral`, `EqsTacticsNodes.Engageable`, `HoldOf`, `ThreatEvaluationSystem`, `StandardInputs` | §5b |
| CE-2245 | a path answered after the move was stopped is refused | `EngineBackedPathResponseSystem` | §5e |
| PV-1 (CV-14) | police within 30 m FACING a crowd member raise his anger 0.01/s (half at 30 m); no fade while provoked | `CrowdSystem` (`Police`, `Provocation`), `CrowdRules.Provoke*` | §5m |
| PV-2 (CV-14) | force on a member (a new knock-down, stun or irritation) spikes his crowd neighbours within 10 m by up to 0.3, once per effect | `CrowdSystem.Provoke`, `CivilianState.ForceSeenUntil` | §5m |
| PV-3 (CV-14) | hidden `Attitude` −1…+1 sets HIS riot threshold `RiotAt − 0.5 × Attitude` (−1 never riots) ⇒ only a fraction riots | `CrowdRules.RiotAtFor`, `CivilianBehavior.Rioting/Calmed` | §5m |
| PV-6 (CV-14) | a rioter panics only when fear ≥ PanicAt AND ≥ his anger (§2.2 "fear beats anger") — the crowd's fright does not break an angry man, force on him does | `CivilianBehavior.FearBeatsAnger` | §5m |
| PV-4 (CV-14) | `BatonCharge {point, Reach, Seconds}`: police strike standing Civilian-class people near the point, never below NonLethal | `BatonChargeBehavior`, `Melee.Strike`, `ForceLevels.LevelFor` | §5m |
| HD-1 (CE-2248, G7) | a civilian takes his phone out on 30 % of his Routine pauses (his own seeded draw), puts it away to walk on or when he leaves the routine | `CivilianBehavior.Routine`, `HeldItems.Set` | §5n |
| HD-2 / HD-3 (CE-2248, G7) | a rioter with a stone in his hands shows `Throwable`; the HSM clears only what it set, never a drawn weapon | `CivilianBehavior.Riot`, `Hold` / `PutAway` | §5n |

**Demos**

| demo | shows | ends with | proves | status |
|---|---|---|---|---|
| `ua-civ-neutral-gate` | soldiers among neutrals and one Hostile | Hostile killed, no neutral hit; a neutral turned Hostile is engaged | CE-2233 | ✅ 3/3 |
| `ua-civ-routine-alarm` | shots near a street of civilians | near ones Alarmed, far ones Routine; calm returns; the wounded one stays Casualty | CV-3, CE-2235, CE-2245 | ✅ 11/11 |
| `ua-civ-shelter` | shots near two houses (one locked) | all panic, enter the open house through its door, gone on every node | CV-7, CE-2236 | ✅ 15/15 |
| `ua-civ-cower` | fire with no building near | Cower on every node, then Alarmed (not Panic) | CV-7 fallback | ✅ 11/11 |
| `ua-civ-gathering` | a hurt man at a crossroads | onlookers form one crowd on a 2.5–11 m ring | CV-5, CE-2237 | ⚠ twin ✅, live re-run pending |
| `ua-civ-contagion` | a fight at one end of a street | fear reaches those who heard nothing, through the crowd | CE-2237, CE-2246 | ✅ 3 in a row |
| `ua-civ-protest` | 12 protesters, no police | one crowd; fists and placards on every node; nobody Hostile or rioting | CE-2240, CV-13 | ⚠ twin ✅, live not recorded |
| `ua-civ-provocation` | 12 protesters (4 friendly at heart) and 3 police who walk up, baton-charge the front rank, then hold the line | nobody riots before the police are near; after the blows a FRACTION riots, never a friendly one; only rioters turn Hostile, the same on every node | CV-14, PV-1…PV-6 | ✅ 7/7 |

## 3. Armed people, riots and police force — [Civilians design](DESIGN_Civilians_And_Crowd_Control.md) §5h–§5l

**Rules — who is armed, who is a combatant**

| id | rule | built by | § |
|---|---|---|---|
| AW-15 | armed is judged PER OBSERVER: identified lethal · unidentified · nothing (range × light × optics) | `Armament.Judge` | §5h |
| TC-7 (CE-2256) | a lethal thing in the HAND is lethal only within his throw (the hands mount's Range, 25 m); beyond it Unidentified — warn, not shoot | `Armament.OfHands` | §5o |
| AW-2 | stones, canisters, baton rounds, stun grenades arm nobody | `ItemAppearances.IsLethal` | §5h |
| AW-3 | a Hidden carry station is invisible | `Armament.ArmedByStatus`, `CarryStations` | §5h |
| AW-21 | combatants are engaged on STATUS, civilians on CONDUCT; out of the fight is exempt | `Armament.IsCombatant`, `ThreatDanger.IsOutOfTheFight` | §5h |
| AW-24 | a soldier vs a combatant costs one status lookup (the simple path stays simple) | status first in `ForceLevels.ConductPermits`, `EscalationLadder.Applies` | §5h |
| AW-16 / CO-1 | drawing is not hostile; AIMING at the forces is a lethal act | `WeaponPostureOf.Report` | §5h.7a |
| CO-2 | one behaviour draws / slings / conceals (Hands / Back / Hidden) | `StationWeaponBehavior`, `CarryStations.Move/Ask` | §5h.7a |
| AW-22 / KC-1′…KC-5 | a lethal act makes him a combatant to the units that saw it (8 ids, sticky); squad-mates share | `KnownCombatants.MarkWitnesses/Knows`, `KnownCombatantList` | §5h.6, §5h.7b |
| CH-1 | no lethal round at a civilian unless identified lethal, known, or aiming at me | `ForceLevels.ConductPermits` | §5h.7b |
| AW-20 | a neutral person on the line or in the spread holds the shot | `LineOfFire.BlockedByNeutral` | §5h.1 |
| CV-13a | hands up holds fire (any side; does not disarm) | `ThreatDanger.ShowsHandsUp` | §5h.2 |
| CV-13a | a covered man who acted raises his hands; 5 s uncovered → down | `Covering.By`, `CivilianBehavior` Surrender | §5h.3 |
| AW-5 | surrender = weapons laid down + hands up; `Seconds` > 0 = a feint | `SurrenderBehavior`, `WeaponLaying` | §5h.5 |

**Rules — riots**

| id | rule | built by | § |
|---|---|---|---|
| riot | anger ≥ RiotAt → rioter at the nearest forces person (30 m) | `CivilianBehavior.Riot`, `CrowdSystem.Rouse` | §5i.1 |
| blow | melee 1.5 m, 5 hp, 3 s knock-down; first blow = hostile act | `Melee.Strike` | §5i.1 |
| AW-8 | stones from a source area or off the ground; a throw every 3 s at 4–20 m | `ItemSources`, `Stones`, `RoundPickUp` | §5i.2 |
| AW-9 / PK-1…5 | empty hands fetch a lying non-lethal throwable (≤ 15 m); a gas canister can be thrown back | `Throwables.Nearest`, `CivilianBehavior.Scavenge` | §5i.3 |
| FL-6 | being stunned or gassed frightens like a hit | `CivilianFearSystem` | §5j.1 |

**Rules — police force level and the escalation ladder**

| id | rule | built by | § |
|---|---|---|---|
| FL-1 | `Roe.ForceLevel` {Warn, NonLethal, Lethal}; police default NonLethal | `Roe`, `CivilianTkbCatalog` | §5j.1 |
| FL-2 | the cap at the gate: Warn = no round, NonLethal = no lethal round | `ForceLevels.Permits` | §5j.1 |
| FL-3 / AW-18 | self-defence (aimed at me, or a lethal act) lifts the cap against THAT man only | `ForceLevels.SelfDefence` | §5j.1 |
| FL-4 | weapon choice drops mounts above the cap | `ForceLevels.AllowsMount` in `UtilityScorer` | §5j.1 |
| FL-5 | police line: hold the post, fire at a Hostile in reach, baton in arm's reach | `HoldTheLineBehavior` (police default) | §5j.1 |
| EL-1 | the ladder is for SecurityForce units vs non-combatants only (soldiers never) | `EscalationLadder.Applies`, `Armament.IsSecurityForce` | §5k |
| EL-2 | unarmed → NonLethal · armed at ease → Warn · ready → NonLethal, Lethal after a warning ignored 5 s | `EscalationLadder.Level`, `ForceLevels.LevelFor` | §5k |
| EL-3 / EL-4 | warning = a 2 s `Warning` gesture + a stamp in the warner's own log; per warner | `EscalationLadder.Warn/Watch`, `WarningLog` | §5k |
| EL-5′ | fire into the air = the ready rung, never self-defence (⛔ EL-5's posture conversion) | `ForceLevels.SelfDefence` asks aimed AT me | §5k.1 |
| EL-6 | a round held at the Warn rung reads `hold: warning` | `HoldOf` | §5k |
| EL-7 | the baton is non-lethal force: no blow below NonLethal | `HoldTheLineBehavior` | §5k.1 |
| EL-8 | the line engages the man it may use the most force on, nearest among those | `HoldTheLineBehavior.Nearest` | §5k.1 |
| EL-9 | a kept weapon choice is re-made when the force level on that target changes | `AimAndFireExecutor` (`AimState.HasChoice`) | §5k.1 |
| — | a weapon a person carries is never the target — its carrier is | `HoldTheLineBehavior.Nearest` | §5k.1 |
| — | `FireAtPoint` with empty hands fires the weapon he carries | `FireAtPointExecutor` | §5k.1 |
| BM-1 / BM-2 | the bomber stands still `Seconds`, then sets off his worn charge; it stays hidden till then | `DetonateChargeBehavior` → `Charges.Ask` | §5l |
| DA-93…DA-95 | the trigger man sets off a PLACED charge by phone when a hostile vehicle HE sees is in its kill zone (`AnyTarget`: people too) | `TriggerIedBehavior`, `KillZone.Entered` → `Charges.Ask` | [Defend & Attack §8e](designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md) |
| DA-96 | an ambush (`SquadDefend.InitiatedBy`) holds through the enemy in its sector and opens fire once its charge is gone | `SquadDefendBehavior.LiftOwnHold` | [Defend & Attack §8e](designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md) |
| CH-1…CH-4 | a carrier hit while it drives with troops aboard pushes through (150 m on, then its route); the squad gets out only of a STANDING carrier | `PushThroughBehavior`, `ConvoyConditions.CanPushThrough`, `BasicVehicleSop` row, `SquadSopConditions` | [Squad Tactics §5b](designs/group-maneuvers/DESIGN_Squad_Tactics.md) |
| SP-1…SP-4 | the spotter picks his own target (the biggest hostile group he SEES, a vehicle double, within 1500 m), runs the IF-3 adjust mission on it, rests 30 s, repeats | `SpotForFireBehavior`, `FireTargets.Pick` | [Indirect Fire §3.11](designs/indirect-fire/DESIGN_Indirect_Fire.md) |
| HQ-1, HQ-2 | a heavy launch heard ≥ 1 km off is SPOTted as IndirectFire; a SPOT carries Vehicle / AirDefence for what was identified | `StaffComposerSystem.ComposeLaunch`, `EquipmentOf`, `AirDefenceWeapons` | [Command Chain §8d](designs/group-maneuvers/DESIGN_Command_Chain_Example.md) |
| HQ-3…HQ-5 | a platoon relays the mortar and the air defence up, asks FireSupport on the mortar, asks Reinforce when Red with the enemy near | `StaffComposerSystem.ComposeSupport` | [Command Chain §8d](designs/group-maneuvers/DESIGN_Command_Chain_Example.md) |
| HQ-6…HQ-9 | the HQ answers: air defence near ⇒ the strike plane on it first; else the attack helicopter searches the point (attacks what it finds, ends when quiet), else the battery fires; Reinforce ⇒ a free unit defends at the requester | `SupportCommandBehavior`, `AirArmedSearchBehavior`, `CommandOrders.Busy` | [Command Chain §8d](designs/group-maneuvers/DESIGN_Command_Chain_Example.md) |
| AT-1…AT-12 | a platoon attacks an AREA, objective by objective (enemy elements by building, nearest first, else the area's buildings): base of fire 120 m off · bound to a flank point 40 m off · lift (support ReturnFire) · clear the building or assault through · consolidate; a Black assault ⇒ the reserve takes over, none left ⇒ break off (the run fails); a Casevac ⇒ the free reserve | `PlatoonAttackBehavior`, `AttackObjectives.Plan`, `CasevacAnswers` | [Defend & Attack §8f](designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md) |

**Demos**

| demo | shows | ends with | proves | status |
|---|---|---|---|---|
| `ua-armed-shield` | rifleman vs insurgent, civilians beside the line | holds while a civilian is in the spread; fires when clear; no civilian hit | AW-20 | ✅ 5/5 |
| `ua-civ-melee` | a protester strikes a policeman | policeman knocked down; rioter Hostile on every node | blow, CV-4 | ✅ 4/4 |
| `ua-civ-riot-stone` | rioters at a stone source | hands filled, stones thrown; Hostile only after his own throw, on every node | AW-8, CV-4 | ✅ 6/6 |
| `ua-civ-surrender` | a Hostile rioter covered by a rifleman | hands up on every node; 10 s no round, unhurt; still Hostile | CV-13a | ✅ 8/8 |
| `ua-armed-surrender` | an enemy soldier surrenders, then feints | rifle down + hands up; no round while down; re-armed → engaged | AW-5, AW-21 | ✅ 7/7 |
| `ua-civ-police-line` | 3 police on default HoldTheLine vs a riot | non-lethal force only; rioters stunned; riot breaks; nobody dies | FL-1…FL-6 | ✅ 6/6 |
| `ua-armed-gas-throwback` | a hardened rioter picks up a gas canister | holds it (irritated), throws it back; new cloud near the police | AW-9 | ✅ 6/6 |
| `ua-armed-draw-aim` | covert insurgent draws a hidden pistol, later aims | Neutral while drawn, no round; Hostile only when he aims; engaged | AW-16, CO-1, CO-2 | ✅ 7/7 |
| `ua-armed-blend-back` | he aims from behind civilians, conceals, walks off | patrol A (saw it) fires once clear, rifle still hidden; patrol B holds | AW-22, KC, CH-1 | ✅ 5/5 |
| `ua-civ-escalation` | police (Lethal ceiling) vs riot + a gunman | warned at ease, no round; lethal rounds only at him; rioters non-lethal only | EL-1…EL-3, EL-8, EL-9 | ✅ 6/6 |
| `ua-civ-escalation-warn` | the same under a Warn ceiling | no round, no blow, nobody hurt; the warning cue only | EL-2, EL-6, EL-7 | ✅ 6/6 |
| `ua-armed-self-defence` | NonLethal police; a Neutral aims a rifle at them | lethal fire at him only; others non-lethal | AW-18, FL-3, CO-1 | ✅ 4/4 |
| `ua-armed-celebration` | police patrol near a wedding; a guest fires in the air, later aims at them | warned, no round during the volley; shot once he aims; no guest hurt | AW-17, EL-5′ | ✅ 5/5 |
| `ua-armed-bomber` | a civilian with a hidden vest walks into a crowd | never seen armed, no round; vest bursts, he dies; casualties follow the warhead | AW-12, BM-1/2 | ✅ 5/5 · ⛔ `CE-2247` spent vest stays live on CGF/IG |
| `ua-car-bomb` | a civilian car with a charge in its boot drives to a checkpoint; its driver sets it off (TriggerIed) | nobody fires at the car; the burst at the car with the APC within 15 m; the soldiers nearest hurt | ST-9 + DA-93 (CE-4035 G-6) | ⏳ new |
| `ua-armed-identify` | phone / pistol / toy gun at 20 m and 150 m, binoculars | identified at 20 m; unidentified at 150 m unless binoculars | AW-15 | ⏳ needs a toy-gun item |
| `ua-armed-knife` · `ua-armed-stones` · `ua-armed-status` · `ua-player-fratricide` | knife / stone throwers / combatant guard rail / player kills friends | — | AW-19, AW-2, AW-21/24, AW-23 | ⏳ |

## 4. Infantry: sensing, posture, weapons, injury

**Threat and sensing** — [Sensors](DESIGN_Sensors_And_Doctrine.md) · [Utility demos](DESIGN_Utility_AI_Demo_Scenarios.md)

| id | rule | built by | § |
|---|---|---|---|
| T1 | danger judged on read: armed 1, unarmed/unidentified 0.3 | `ThreatDanger.Of`, `Armament.Judge` | SD §7.8a |
| T2 | freshness = memory score ÷ 500; +50/s tracked, −10 %/s unseen | `ThreatFreshness`, `ThreatEvaluationSystem` | SD §7.8a |
| T3 / T4 | threat = danger × freshness; live = tracked or freshness ≥ 0.25 | `StandardInputs`, `SquadInputs`, `ThreatFreshness.IsLive` | SD §7.8a |
| T5 | ranking: armed, visible, near first; civilian 0 | `ThreatRankingDecision` | UD §4 U2 |
| T7 / T8 | sensor kinds Visual · Radar · Thermal · Acoustic · DangerArea; one node per kind | `SensorModality`, `SensorKindRegistry`, `SensorNodes` | SD §7.10 |
| T10 / T11 | danger areas along the route (roads ≤ 40 m, open runs ≥ 20 m); threatened = an armed contact sees it | `DangerAlongRouteClassifier`, `DangerAreaSensorSystem` | UD §10.7 |
| T12 | single unit: hold short of a watched crossing, rush when clear | `DangerAreaNodes.HoldShort/Cross`, `DangerCrossing` tree / `DangerCrossingBp` | UD §10.4a |

**Posture and cover** — [Utility demos](DESIGN_Utility_AI_Demo_Scenarios.md) · [EQS tactics](DESIGN_Eqs_Consuming_Behaviours.md) · [Peek and fire](DESIGN_Peek_And_Fire.md)

| id | rule | built by | § |
|---|---|---|---|
| P1 | posture = AdvanceAndAttack · TakeCover · Suppress · Flee · HoldProne · Hold (hysteresis 0.08); one C# tree | `CombatPostureDecision`, `CombatPostureBehavior` | UD §4 U1 |
| P3 | hurt with no cover → HoldProne; flee only at ≥ half health | `CombatPostureDecision` | UD §11.1 |
| P4 | target out of sight → Flank or FiringPosition beats Direct | `AttackApproachDecision` | UD §4 U4 |
| P5 / P6 | TakeCover / FallBack / Flank / FiringPosition: one EQS action; re-route only on ≥ 5 m change | `EqsTacticsNodes` | EQ §6, §9.8 |
| P7 / P8 | hide → expose → aim if seen (0.8 s) → fire → hide; unseen → blind burst | `PeekAndFireNodes`, `AimAndFireExecutor`, `SightNow` | PF §2, D3 |
| P9 / P10 | position heat (half-life 45 s, burned at 2.5); used-up spot → bound to the next cover | `FiringPositionMemory`, `PeekAndFireNodes` | PF §8 |
| P11 | squad-mates do not take a friend's claimed cover | `CoverClaim` | PF §10.9 |
| P12 / P13 | reload hidden (5 × 30, 3 s); masked muzzle → move on | `Magazine`, `LineOfFire.MuzzleMasked` | PF D9, §10.10 |

**Weapon choice** — [Utility demos](DESIGN_Utility_AI_Demo_Scenarios.md) §11–§12

| id | rule | built by | § |
|---|---|---|---|
| W1 / W2 | the leader spreads fire over pooled contacts, ≤ 2 per target, on each pool merge | `ThreatMatrixAssignmentSystem`, `LeaderAssignmentDecision`, `SquadCoordinationSystem` | UD §11 |
| W3 | the member's posture can veto the leader | `CombatPostureDecision` | UD §11 D4 |
| W4 / W5 | mount chosen per shot (Mount=Auto): effectiveness × ammo × range band | `WeaponSelectionDecision`, `WeaponChoice` | UD §12 |
| W8 / W9 | aim point by cover and sight; a round hits only inside the stance's body band | `AimPoint.For`, `RaycastSolverSystem` | PF D1–D2 |

**Injury** — [Injury states](DESIGN_Injury_States.md)

| id | rule | built by | § |
|---|---|---|---|
| I1 / I2 | state derived from health: Ok ≥ 0.6 · Wounded < 0.6 · Incapacitated < 0.3 · Dead 0 | `Injury.StateOf`, `InjuryThresholds` | IS I1–I2 |
| I3 | incapacitated loses move and fire | `CombatLife.ApplyHitCapabilities` | IS I6 |
| I4 | bleed-out: linear over 120 s | `BleedOutSystem` | IS I3 |
| I5 | incapacitated = no threat, no target (blasts still hit) | `ThreatDanger.IsOutOfTheFight` | IS I4 |
| I6 | wounded speed × 0.6 | `Injury.SpeedFactor` | IS §7 |

**Demos**

| demo | shows | ends with | proves | status |
|---|---|---|---|---|
| `ua-posture` | one rifleman, health lowered step by step | Suppress when healthy → defends when hurt; no flicker | P1 | ✅ |
| `ua-threat-ranking` | armed / unarmed / hidden / far contacts | armed near first; civilian 0; hidden unranked | T5, CE-2233 | ✅ |
| `ua-danger-crossing` (+ `-bp`) | a rifleman's route crosses two roads, one watched | holds ≥ 10 s at the watched one; crosses once the watcher leaves (BTree and blueprint) | T10–T12 | ✅ |
| `ua-fire-distribution` | a squad vs three hostiles | all assigned, ≥ 2 targets, ≤ 2 per target; a 10 HP member goes prone | W1–W3 | ✅ |
| `ua-weapon-choice` | an IFV vs a tank and an insurgent | TOW at the tank, 25 mm at the man | W4, W5 | ✅ |
| `ua-three-hosts` | the posture decision on each host | the same winner at every health step | P1 (one tree) | ✅ |
| `ua-attack-approach` | target lost from sight | Flank or FiringPosition; regains sight and fires | P4 | ✅ |
| `ld-sentry-surprise` (+ `-360`) | a sentry with a 200° view cone holds a sector | A crossing in front is seen; B walking up behind is NOT, until he fires; the 360° control sees B | LD-5 (CE-2298) | ✅ |
| `ua-grenade-or-rifle` | rifle or grenade against a man who ducks out of sight | the grenadier throws, the rifleman does not | WL-4 | ⏳ in-process rail only, no live check |
| `ua-weather-sensing` · `ua-night-sensing` | two identical observer / target pairs: one in a 60 m fog bank · the same at night | first seen at ~150 m clear, ≤ 60 m in fog · ~40 m at night (light floor 0.25) | S2-1, S2-1n (CE-4035 G-3, G-10) | ⏳ new |

## 5. Squads — [Squad tactics](designs/group-maneuvers/DESIGN_Squad_Tactics.md) · [Entity interactions](DESIGN_Entity_Interactions.md)

**Rules — manoeuvres**

| id | rule | built by | § |
|---|---|---|---|
| S1 / S2 | one runner; the commander speaks only through orders; phases move on gates (*covering*: all fired ≤ 2 s; *set*: movers in cover) | `SquadManeuverRunner`, `SquadGates` | ST §7.6 |
| S3 / S4 | contact → Assault · BreakContact · Hold · Rescue; a far contact is closed by fire and movement | `SquadContactResponseDecision`, `ReactToContactBehavior` | ST §7.5.7, §7.6.3 |
| S5 / S6 | fire and movement: one half suppresses, the other rushes ≤ 15 m to cover; or in buddy pairs | `FireAndMovementPlay` | ST §7.6.1, §7.6.4 |
| S7 | bounding overwatch: watchers hold until contact, then suppress; bounds end in cover | `BoundingOverwatchPlay` | ST §7.6.2 |
| S8 / S9 | danger crossing: security first, a pair per lane, far side set, security last; contact aborts to ReactToContact | `DangerCrossingPlay` | ST §7.7.4 |
| S10 | how to cross: Cross · Bound · Hold; open ground → Bound | `SquadCrossingDecision` | ST §7.7.7 |
| S11 | march: the lead is the man nearest the goal; laggards > 4 m go at 1.6× | `SquadMarchPlay` | ST §6.1a |
| S12 | break contact: the retreat query always answers | `BreakContactPlay` | ST S2-F |

**Rules — casualty care and carrying**

| id | rule | built by | § |
|---|---|---|---|
| C1 | a man down raises one SquadMemberDown on the commander | `SquadSensing.MemberDown` | ST R-a |
| C2 / C3 | rescue score = urgency × (1−exposure) × quiet × (1−enemy share); heavy fire → nobody goes | `SquadContactResponseDecision`, `ReactToContactBehavior` | ST §7.5.4 |
| C4 / C5 | the Medic goes (else the nearest fighter, not the leader), after covering fire | `SquadCasualtyCarePlay`, `SuppressNodes` | ST K3′, K5′ |
| C7 | a downed leader hands over; succession skips the Medic | `SquadSensing.Succession` | ST K6 |
| C8–C11 | only the incapacitated is carried; carrier slows (× 0.5 carry, × 0.3 drag) and cannot fire; carrier down → casualty dropped alive | `Embarkation`, `CarryNodes`, `PickUpExecutor`, `EmbarkedStateSystem` | EI §7.4.9 |
| C12 | embark / disembark: one applier on the vehicle's owner | `EmbarkNodes`, `EmbarkationSystem` | EI §7.1b |

**Demos**

| demo | shows | ends with | proves | status |
|---|---|---|---|---|
| `ua-fire-and-movement` | a squad closes on a contact by halves | ≤ 17 m rushes under real covering fire; ≥ 30 m closed; never both halves up | S5 | ✅ 6/6 |
| `ua-fire-and-movement-pairs` | the same in buddy pairs | ≤ 2 up per half; waiting buddy fires; ≥ 20 m closed | S6 | ✅ 5/5 |
| `ua-bounding-overwatch` | a squad advances with no contact, then contact | one half moves at a time; no fire before contact; suppress ≤ 3 s after | S7 | ✅ 9/9 |
| `ua-squad-danger-crossing` | a marching squad meets a road | security set; ≤ 1 pair in the road; far side set; reform ≤ 120 s | S8 | ✅ |
| `ua-squad-danger-crossing-contact` | contact during the crossing | crossing replaced by ReactToContact; both sides fire; assault by fire and movement | S9, S4 | ✅ |
| `ua-squad-march-loose` | a strung-out squad marches | lead keeps ≥ 0.9 m/s; everyone within 35 m at the end | S11 | ✅ |
| `ua-squad-bound-open-ground` | a 100 m open field on the route | Bound chosen; bounding overwatch across; ends beyond the field | S10, S7 | ✅ |
| `ua-casualty-care` | a man is hit in a firefight | Rescue wins; only the Medic goes after covering fire; casualty alive at cover | C1–C5, C8–C10 | ✅ |
| `ua-casualty-care-heavy` | the same under heavy fire | nobody goes for 30 s; enemy down → they go at once | C3 | ✅ |
| `ua-casualty-care-leader` | the leader is the casualty | the next man (not the Medic) takes command; care runs | C7 | ✅ |
| `ua-squad-danger-crossing-hold` | crossing choice picks Hold | — | S10 | ⏳ no live check yet |
| `pc-attack-dry` | a platoon attacks the area round OBJ on faked reports | base of fire → bound → lift + ClearBuilding → consolidate → won | AT-1…AT-12 | ✅ 12/12 |
| `pc-defend-house` | a platoon attack against a platoon defence, AI on both sides | both plays run, the line opens fire, both sides fire, the assault or a break-off — no winner asserted | AT, DA-98, E-3a | ✅ |
| `pc-urban-move` | a platoon moves through town on foot (UrbanMovement, CE-2284 U-1) | the lead walks each leg with its crossing drill; the others trail it 30 m / 60 m along the route, round the corner; the task ends at the route's end | UM-1…UM-7 | ⏳ re-run after the front-man fix (`2026-10-22`) |
| `pc-urban-ambush` · `pc-ambush-bypass` | fired on from a building on the move (CE-2284 U-2) | beside the route (≤ 50 m) or a Militia platoon: the squad fired on supports, the next clears it, the movement resumes · 100 m off a Regular platoon's route: bypassed, no reaction | UM-10…UM-13, DA-18 | ⏳ twins running |
| `sl0-platoon-march` | a body-less platoon marches three squads (one of them dead) | each squad fulfils its own march order; the dead squad is left | SL-0 (CE-2262) | ⏳ no live pass recorded |
| `cc-platoon-bound` | the first command chain: Bravo supports, Alpha bounds on its SET; contact ⇒ SPOT ⇒ the platoon holds both | all five end conditions | CC-D (CE-2294) | ✅ |
| `uf-alone-under-fire` · `uf-squad-under-fire` | one man ordered across open ground into fire · a squad's fire and movement under fire | he drops prone and crawls / bounds to cover · rushing half crouched, a near-missed mover goes prone | AQ96 (CE-2319, CE-2320) | ✅ alone · ⏳ squad |
| `df-lone-soldier-reflex` · `df-civilian-ordered` | default SOPs with no orders: a soldier under fire, civilians ordered to stand | the soldier on his type's SOP reacts, the InertSop one does not · the ordered civilian still flees on CivilianSop | AQ97 (CE-2322) | ✅ both |
| `uc-convoy-hit` / `-ifv` | demo-city: an IED under a convoy | the charge only in the kill zone; an APC halts and its squad fights / a Bradley pushes through with its squad aboard | CE-2306, CH-1…CH-4, CE-2313 | ✅ 8/8 · 9/9 |
| `uc-request-chain` (+ `-dry`) | spotter → mortar → heard launch → SPOT → FireSupport → the HQ's AH-64 armed search | nothing sent without a report; the AH-64 attacks the cell | HQ-1…HQ-9 | 🟡 once (terrain size) · ✅ dry 9/9 |
| `uc-a10-zu23-dry` · `uc-reinforce-dry` | air defence near the point · a Red platoon with the enemy near | the A-10 at the ZU-23 first, the AH-64 waits · the reserve sent, never the requester | HQ-7, HQ-9 | ✅ 7/7 · 6/6 |
| ⭐ `uc-urban-battle` | the flagship: convoy, IED, ambush round a corner, spotter + mortar, ZU-23, an HQ with air and a reserve, civilians | the charge only in the kill zone · no round at a civilian · nothing sent without a REQUEST · both sides fight | all of the above | ✅ 6/6 |

## 6. Buildings — [Building interiors](DESIGN_Building_Interiors.md)

| id | rule | built by | § |
|---|---|---|---|
| B1 | the mover routes round a locked door and opens a closed one on the way | `DoorPassageSystem` | BI §3j |
| B2 / B3 | a behaviour can walk to a door and operate it; the locksmith unlocks the front | `DoorNodes`, `DoorActionExecutor`, `DoorLocksmithBehavior` | BI §3j |
| B4 / B5 | fragments per body point of the stance; blast through barriers by material (a roof burst spares the floor below) | `AreaEffectSystem` | BI §3k |
| B6 / B7 | throws and mortars fly a gravity arc to a point; a door in the lethal radius is breached | `FireAtPointExecutor`, `AreaEffectSystem` | BI §3k |
| B8 / B9 | cover and window points per storey; the window duel is one PeekAndFire per side | `CoverPointsGenerator`, `WindowDuelBehavior` | BI §3l, PF P-8 |

| demo | shows | ends with | proves | status |
|---|---|---|---|---|
| `bc-room-clear` | a squad clears room A of the CQB house through its side door | stack out of the window's view, breach, one man at a time to his point; both insurgents down | BC-1 (CE-2257) | ✅ twin · ⚠ live flaky (the flaky lane) |
| `bc-house-clear` | the squad clears the whole house room by room | foothold, then each reachable room; civilians unharmed | BC-3 (CE-2259) | ✅ 2/2 (`2026-10-22`) |
| `bc-house-defended` | a defender on the front door's axis | the stack holds outside and fights from the pie points; the hold ends after the pie is sliced (BC-5, 20 s cap) | BC-5 (CE-2261) | ⚠ losses 3 → 1; the pie men barely fire back |
| `bt-doors` | a visitor and a locksmith at a house | locked front avoided; back door opened; locksmith unlocks the front and walks in | B1–B3 | ✅ |
| `bt-grenade-posture` | an M67 between a standing and a prone man | standing man hurt; prone man (exposure ≤ 0.1) unhurt | B4 | ✅ |
| `bt-mortar-roof` | a mortar round on a roof | courtyard man badly hurt; man downstairs ≥ 90 HP | B5, B6 | ✅ |
| `bt-window-duel` | two units across a street | both fire; A uses two windows; B bounds to the next van | B9, P7–P10 | ✅ |

## 7. Vehicles and armour — [Urban armour](designs/vehicle-tactics/DESIGN_Urban_Armour_Tactics.md) · [Hit model](designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md) · [Missiles and ammo](designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md) · [Exposure cycle](designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md)

| id | rule | built by | § |
|---|---|---|---|
| A1 / A2 | the face hit comes from the 3-D arrival (≥ 45° down or top-attack → Top); armour per face × penetrator | `ArmorModel`, `HitResolutionSystem`, `DamageCalculationSystem` | AHM §6 |
| A3 | a hull is a cylinder: a round over the roof misses | `RaycastSolverSystem` | AHM §6 |
| A4 / A5 | blast hurts hulls × susceptibility; fragment mass by casing | `AreaEffectSystem`, `AreaEffect.Fragments` | AHM §7.6 |
| A7 / A8 | the weapon brain picks the round: the cheapest one that is enough; a switch costs a reload | `AmmoChoice` | MA §2 |
| A9 / A10 | guidance: unguided · command (lost when sight breaks) · fire-and-forget · IR; top attack only when the range fits | `GuidedRoundSystem` | MA §3 |
| A11 / A12 | a locking mount never fires blind; ATGM/mortar/SAM hold while moving | `AimAndFireExecutor.NeedsLock`, `CombatTkb.FiresOnTheMove` | MA §4a |
| V1 | danger TO ME = his best round's penetration chance on my exposed face | `ThreatDanger.ToMe` | VEC §2 |
| V2 / V3 | hide / firing / shelter points sized to the hull; overhead cover; beyond threat range | `FindVehicleHide`, `FindVehicleFiringPoint`, `FindShelter`, `OverheadCoverTest` | VEC §3 |
| V4 / V5 | cycle Hidden → Exposing → Engaging → Withdrawing; exposure budget = min(ExposeSeconds, 0.7 × the threat's aim time) | `VehicleFightFromCoverNodes` (`LandPlatformAttack`) | VEC §4 |
| V6 / V7 | strong front → reverse to the same hide, else scoot to a new one; a hit or launch cuts exposure short | `VehicleFightFromCoverNodes` | VEC §4 |
| V8–V10 | fire or track while driving; no hide → stand and fight; a hide with no line to the target is burned | `VehicleFightFromCoverNodes` | VEC §4 |
| V12 | the AT side is composition: RPG man from windows; TOW gunner holds sight through the flight | `PeekAndFireNodes.Guiding` | VEC §7 |
| H1–H7 | hull-down platoon: alternating pairs, one go time, creep until the target is seen, one round, reverse; the commander designates, the weapon brain picks the round | `HillAttackCommanderNodes`, `VehicleFightFromCoverNodes`, `PostureNodes.Designate` | [Hill attack](designs/hill-attack/DESIGN.md) |

| demo | shows | ends with | proves | status |
|---|---|---|---|---|
| `ua-tank-corner` | a tank round a corner vs AT teams | ranks the Javelin first; fires from two points; gunner killed | V1, V4–V6 | ✅ 4/4 |
| `ua-javelin-lock` | short exposures vs a Javelin | the Javelin never gets its lock; 0 hits | V5, A11 | ✅ 5/5 |
| `ua-technical-scoot` | technicals shoot and scoot | the tank ranks the ATGM first | V6 | ⏸ varies run to run (CE-2232) |
| `ua-rpg-window` | an RPG man between windows vs IFVs | he moves between windows; IFVs fire on the move | V12 | ✅ 4/4 |
| `ua-ifv-standoff` | a 25 mm IFV vs RPG men | kills at 326 m untouched; up close an RPG hits it | V1, A7 | ✅ 4/4 |
| `ua-tank-ammo` | one tank, different targets | the round is chosen per target | A7, A8 | ⚠ 2/2, soft targets survived; backend fixed the causes, not re-run |
| `ua-hill-attack` | four M1s vs two T-72s over a crest | every shot below the crest; one round per exposure; pairs together; both T-72s dead | H1–H7 | ✅ |

## 8. Drones, air operations, air defence — [Air operations](designs/air-attack/DESIGN_Air_Operations.md) · [FPV drone](designs/vehicle-tactics/DESIGN_FPV_Drone.md)

| id | rule | built by | § |
|---|---|---|---|
| D1–D3 | an FPV flies the rotorcraft model; hits the first thing on its path (a deck shields); heard as a rotor | `RotorcraftModel`, `DroneImpactSystem`, `VisionBroadphase` | FPV §6 |
| D4 / D5 | dive: search 150 m → pick → pursue → led dive; battery out → straight down | `AirAttackNodes.Dive` | AO §9i |
| D6–D9 | air threat raised by an air contact; vehicle SOP: engage aircraft > shelter > …; shelter, else evade; fire at a seen drone | `AirContacts`, `BasicVehicleSop`, `ShelterFromAirNodes` | VEC §8 |
| O1 | aircraft orders resolve to `AirPlatform<Order>`; move at 120 m above ground | `AirOrderNodes` | AO §9a |
| O2 | one attack node; profile picked (gun for an entity, bomb for a heavy point) | `AirAttackProfiles.Pick`, `AirAttackNodes` | AO §9c–e |
| O3 / O4 / O5 | level bomb released on the weapon brain's cue; rockets/guns in their range window, leading the target, ≤ 3 passes | `AimAndFireExecutor.ReleaseCue`, `BombBallistics`, `TargetLead` | AO §9c–e |
| O6 | collateral: no weapon whose injury radius holds a friend or neutral (unless DangerClose) | `HiddenTargetThrow.FriendlyInBurst` | AO §9e |
| O8 | door-gun orbit; a gunner shot → the orbit reverses to the other door | `AirAttackNodes.Orbit` | AO §9f |
| O9 / O10 | missile warning → decoys, break onto the beam, come round; IR seeker can be seduced | `MissileWarningSensingSystem`, `Countermeasures`, `GuidedRoundSystem` | AO §7e, MA §3c |
| O11 / O12 | ground air defence (SAM or a gun ≥ 80°) fires at the newest aircraft; aircraft are hittable and fall | `AirDefenceNodes`, `EngageAircraftBehavior`, `FixedWingModel` | AO §7c, §7f |
| O13 | air assault / rescue: ride while aboard, dismount on unloading, hold until all are out; rescue = recover + load | `AirHolds`, `SquadRideBehavior`, `SquadRecoverBehavior`, `CarryNodes.LoadCasualty` | AO §9a/b/j |

| demo | shows | ends with | proves | status |
|---|---|---|---|---|
| `ua-air-strike` | a plane level-bombs a point | burst 2.7 m off; target destroyed; a technical 123 m away untouched | O2, O3, O6 | ✅ 5/5 |
| `ua-air-attack` | bomb, gun run, Apache rockets + gun, door-gun orbit | each vignette hits; the orbit reverses when a gunner is shot | O2–O5, O8 | ✅ 15/15 |
| `ua-air-defence` | ground fire at aircraft | ground engages; aircraft evades | O9, O11 | ⚠ 4/4, flares + re-attack not shown |
| `ua-air-assault` | helicopter insertion | nobody out in the air; lifts off after the last man; squad moves on | O13 | ✅ 7/7 |
| `ua-air-rescue` | the medic carries a downed pilot to a helicopter | pilot seated alive; lift-off | O13, C8–C10 | ✅ 8/8 |
| `ua-drone-shelter` | two FPV drones on two M1s | tank by the deck backs under it (unhurt); tank in the open hit on the roof | D2, D6–D8, A1 | ✅ |

## 9. Indirect fire — [Indirect fire](designs/indirect-fire/DESIGN_Indirect_Fire.md)

| id | rule | built by | § |
|---|---|---|---|
| I1 / I2 | low arc at the lowest charge that clears, else high, else refused; dispersion deterministic | `FireSolution.For`, `FireProcessingSystem` | IF §3.3–§3.6 |
| I3 / I10 | the brain names point + arc; the tube lays, holds until its fire time, fires as itself | `FireAtPointExecutor`, `BallisticAim`, `WeaponTaskDispatcherSystem` | IF §3.6, §3.10 |
| I4 / I5 / I11 | Strike = the fire mission; a battery runs a battery play (sheaf converged/parallel/open); a lone SP gun runs its own | `BatteryFireMissionPlay`, `IndirectFireNodes` | IF §3.7 |
| I6 | time on target: every round arrives together | `BatteryFireMissionPlay.Plan`, `TurretSlew` | IF FM-4 |
| I7 | a friend within 600 m refuses the mission unless DangerClose | `IndirectFireNodes.DangerClose` | IF FM-5 |
| I8 / I9 | the observer adjusts 800 → 50 m from bursts he sees (one gun), then fire for effect | `PersonObserveFireBehavior`, `RecentDetonations` | IF §3.8 |

| demo | shows | ends with | proves | status |
|---|---|---|---|---|
| `ua-indirect-fire` | four vignettes: adjust, time on target, high arc, danger close — ① and ② now play from the file (CE-4035 G-5; the observer waits for the tubes to be manned) | adjust then fire for effect; rounds land together; high arc over a mask; danger close refused / allowed | I1–I9 | ✅ all four · ⏳ re-run self-running |

## 10. Weapon systems, airborne — [Weapon systems](designs/weapons/DESIGN_Weapon_Systems.md) · [Weapon layers](DESIGN_Weapon_Layers.md) · [Airborne](designs/airborne/DESIGN_Airborne_Insertion.md)

| id | rule | built by | § |
|---|---|---|---|
| WL1 | the behaviour names the target; the weapon brain picks mount, round, ROE; the muscle slews and fires | `WeaponTaskDispatcherSystem`, weapon brain profiles | WL W-1 |
| W1–W4 | separable weapons are entities with ONE controller; `CanFire` = controller + deployed + gunner + in arc + not broken; fired as itself | `WeaponSystems` (`ControllerOf`, `CanFire`) | WS §6, §9 |
| W5 / W6 | presets fill station weapons once; killing the gunner silences the gun; weapons break | `SeatPresets`, `AreaEffectSystem` | WS §9, §5b |
| W9 / W10 | WeaponSystemAttack / Hold; SetUp = put down · man it · deploy; Carry = pick up | `WeaponSystemOrders`, `WeaponSystemBehaviors` | WS §10 |
| WL3 | posture AtEase / Ready / Aimed; a person fires within 75° of heading, else turns first | `WeaponPostureOf`, `HumanGait` | WL W-13, W-14 |
| AB1–AB4 | a man on a rope or parachute is still a person; rope 5 m/s; parachute flies a pattern and flares | `Descent`, `RopeSlideModel`, `ParachuteModel` | AB §5, §7a |
| AB5–AB8 | the exit crosses the wire; release point upwind; SOP: roping → fast rope, dropping → airborne exit, then assemble | `Embarkation.ExitIntoDescent`, `ReleasePoint`, `SquadFastRopePlay`, `SquadAirborneExitPlay`, `AssembleAfterDropPlay` | AB §6.7, §7b |
| AB9–AB11 | fall > 6 m/s hurts; eject at 20 % health; a landed pilot evades ≥ 100 m and goes prone | `DescentRules`, `EjectExecutor`, `EjectionSystem`, `PilotSop` | AB §7c–§7d |

| demo | shows | ends with | proves | status |
|---|---|---|---|---|
| `ua-weapon-systems` | sentry gun, M2 set-up, Javelin carry, an intruder walking into the M2's arc *(CE-4035 G-11; the Kornet team this row named is not in the file — removed)* | sentry fires on its own; the M2 deploys and fires on Intruder 2; the Javelin is carried | W1–W10 | ⏳ re-run after G-11 |
| `ua-fast-rope-roof` | 8 men rope onto a roof | 8 down in 7.6 s, a ring on the roof; helicopter leaves after | AB2, AB3, AB7, AB8 | ✅ |
| `ua-airborne-drop` | a stick of 8 jumps | each lands 7.8–10.5 m from his point; squad assembles | AB4–AB7 | ✅ |
| `ua-eject` | an A-10 is hit; the pilot ejects | pilot out before the jet dies; lands; evades | AB9–AB11 | ⚠ 4/5 — canopy at 3.96 s vs the check's 3.5 s |
| `ua-shootdown-eject` | the A-10 with NO eject task is shot by a Stinger and a ZU-23 under its route | the pilot ejects by himself (≤ 20 % health or a wreck), canopy open, down | AB10 (CE-4035 G-1) | ⏳ new |

## 11. Not built, open, or known wrong

- ⏳ **AW-19** knife / closing fast · **AW-23** player fratricide · **AW-15** toy gun item (blocks `ua-armed-identify`).
- ⏳ crossing-Hold live check (`ua-squad-danger-crossing-hold`) · team stretcher carry · medic stabilising · helicopter battle position, pop-up, dive bomb, radar SAM · smoke, counter-battery · rappel.
- 🔴 **CE-2247** (backend): a spent charge stays a live charge on CGF and IG · **CE-2160**: authoring an entity `Roe` crashes the cluster · **CE-2232**: hide choice ignores line to target.
- ⚠ other shooters than the police line may still target a carried item that has a hostile side (§3 "never the target" covers `HoldTheLine` only).
