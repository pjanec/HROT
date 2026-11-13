<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - ../../designs/group-maneuvers/DESIGN_City_Battle_Showcase.md — ⭐ THE design: §3–§5 the battle, §6–§8 UML, §9 the parallel contract, §11 every item a row, §12 the test ladder
  - NOTE_Coordinator_To_Ui_Threaded_Editor_Limitations.md — ⭐ the switch settings every automated test here uses, and the threaded editor's known limits L-1…L-6
  - HANDOFF_Ui_To_Showcase_World.md — the sister lane (terrain, obstacles, bulldozer, scenario skeleton) on `showcase-world`
  - ../../DESIGN_Parallel_Simulation.md — §18.5–§18.8 the per-entity parallel phases every behaviour runs in
-->

# HANDOFF — ui → `showcase-brains`: the behaviours of the city battle (CE-7000)

**For:** a new lane on its own VM, branch **`showcase-brains`**. **From:** ui. **Dispatched at `aaff69f31`** (+ the commit that carries this file);
⛔ **your scope is FROZEN there** — later documents are FYI only; one that invalidates an item ⇒ STOP that item and report it (R-106: never the batch).
🔒 User, `2026-11-13`: *"lets break the rest of the showcase into tasks and lets write behavior handoff"* · *"all needs to be recorded in traceable
tasks"* · *"It needs to be dynamic"*. ⛔⛔ **Autonomous** (R-4156): a decision that needs the user is skipped with a note, never a pause.

## 0. Start

| # | step |
|---|---|
| 0.1 | `git fetch origin exp/threaded-build ui` · `git switch -c showcase-brains origin/exp/threaded-build` *(must contain `a9955908e`, the coordinator note's base)* · `git merge origin/ui` — ⚠ check first `git diff --name-only origin/exp/threaded-build...origin/ui` is docs only; a `.cs` ⇒ STOP and report · push · empty commit `chore: started showcase-brains at <sha>` (rule 1b) |
| 0.2 | ⭐ merge `exp/threaded-build` as it moves (it brings the threaded editor's fixes, L-1…L-4); merge `showcase-world` when CE-6003 + CE-6006 land (CE-7012 needs them); ⛔ never rebase |
| 0.3 | ⭐ read RULINGS.md, the design end to end, [the coordinator note](NOTE_Coordinator_To_Ui_Threaded_Editor_Limitations.md), and [`OVERVIEW_Behaviour_Building_Blocks.md`](../../OVERVIEW_Behaviour_Building_Blocks.md) (the map of what exists) |
| 0.4 | ⭐ ids: tracker `CE-7000…`, rulings `R-7000…` (registered). Your rows are **Area S-B**; new rows go at its END |

## 1. The rules — pointed at, not restated

- **`.claude/CLAUDE.md` "BEHAVIOUR CODE RUNS IN PARALLEL"** — the nine never / instead rows. Another unit is changed only by a command that lands NEXT tick
  (R-4212) — design outcomes, never same-tick ordering; a cross-entity read is DECLARED (`BrainPhase.Contribute(…, declareReads)` `BrainPhase.cs:46`,
  `BrainReads.cs:16-32`, staff `IStableReads` `StaffSystems.cs:133-140`).
- ⭐⭐ **The switches, named in EVERY command** (the coordinator note §3 — an env var overrides the user's saved settings):

| test | `HROT_THREADED_KERNEL` | `HROT_PARALLEL_SIM` | checks |
|---|---|---|---|
| a behaviour's unit rails | n/a | **1** AND **0** — both pass | `HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` with `=1` |
| anything over HTTP, stepped, hashed, a scenario rail | **0** | **1** | `HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` |
| determinism | — | — | `python3 scripts/stepped-hash.py --kernel 0 --threads 1,4,8 <scenario>` |

  ⛔ Do not work around the threaded editor's limits (L-1 stepped divergence, L-2 the extra step, L-3 `/view`, L-4 undo after HTTP delete) — they are
  fixed at their source; you pick them up by merging `exp/threaded-build`.
- **Hot path:** no LINQ, zero steady-state allocation (R-2084) — prove it in the rail. **New components:** unmanaged when recorded.
- **T-1 first:** every item names the feature's OWN suite; run it before anything, add rails INTO it. The existing suites: `SquadManeuverTests` (the drills,
  228 / 228 both ways), `StressSystemTests`, `CivilianHsmTests`, `CrowdSystemTests`, `AirDefenceNodesTests`, `ShelterFromAirNodesTests`, `DroneImpactSystemTests`,
  `ScenarioRulesTests`, `DrafterTests`.
- **Each item ships a small feature scenario** (folder with `scenario.json` + `checks.json`, verified by `VerifyCommand` / `ScenarioChecks` — the
  `uc-reinforce-dry` pattern) under `scenarios/showcases/city-battle/features/`. ⛔ Never test correctness on the full battle (D-12).
- **No UI work.** **Obligation ⑤:** a deviation is folded into the design (and the owning design of the behaviour) before the item closes.
- 📌 **Measured facts the plan already folded in** (do not rediscover): CIV-3 fear-spreads is BUILT (`CrowdSystem.cs:400-421`) · no smoke round exists ·
  no drone launch exists (drones are placed airborne) · triggers fire on CONDITIONS (`OnEntity` / `ScenarioTime` / `Compound` / `CountMatching`), not events ·
  `TimerAction` exists in the HSM builder (`HsmBuilder.cs:248`) and no platoon play uses it — the tempo deadlines' natural seam.

## 2. The items — in this order *(design §11; seams measured on `exp/threaded-build` `2026-11-13`)*

| # | rows | frame | done when |
|---|---|---|---|
| **1** | **CE-7001** F-0 | CE-2341 (`RoomGrenade.cs:86`; the row proposes authoring `Suspected: true`), CE-2304 (Progress `Set` fails 1 in 5 — `NavigationExecutionSystem.IsResentMove` :355, `CommandOrders.StillRunning`, `DefencePosts.Reached`), CE-3361's live look (`showcase-bridgehead`), CE-2232's live look (`ua-technical-scoot`). ⛔ CE-2264 is on hold at the user's word — skip | each green N runs of N in its own flaky lane, rows closed |
| **2** | **CE-7021** company echelon · **CE-7022** puppet platoons | mirror the platoon: template `defaultSop` (`NedTkbCatalog.cs:949`), `AssignSopEvent` (`BehaviorTkbTranslator.cs:124-135`), orders by `CommandOrders.Assign` (`CommandNodes.cs:80`) → `AssignTacticalIntentEvent` → `TacticalIntentResolutionSystem` (:94); `CommandPicture` (`CommandComponents.cs:112`, `MaxSubordinates = 6` :116) — say whether 6 holds a company + support. The puppet bench is design §12 ②: light platoons a test script moves | a company passes an order to each platoon; the bench runs a commander 5 min of sim in seconds |
| **3** | **CE-7023** `CompanyAttack` · **CE-7024** `CompanyDefend` | design §6 / §7 (the UML is THERE — draw any change there first); mirror `PlatoonAttackBehavior.cs:213-263`, `PlatoonDefendBehavior.cs:205-231` | decision rails (§12 ①) + bench rails: phase changes, reserve commit, fall back |
| **4** | **CE-7005** tempo | every company order carries a deadline; 50 % / 100 % escalation (smoke + fires + reserve → switch axis / bypass) — on `TimerAction`; ⛔ never "wait" | a bench rail: a stuck platoon ⇒ escalation at the deadline |
| **5** | **CE-7003** support calls · **CE-7025** smoke rounds · **CE-2278** smoke grenade | `SupportCommandBehavior` answers FireSupport / Reinforce (:75-82, :116-140), no munition choice (:82) ⇒ a request that names smoke; a smoke mortar round (Indirect Fire IF-5); grenade 6041 into loadouts + `ThrowSmoke` (CE-2278's row). The cloud is built (`AreaEffectCloud`) | `features/smoke-the-street`: a company asks for smoke, the street is screened, sight through it is cut |
| **6** | **CE-7007** shoot and move · **CE-7032** outposts withdraw · **CE-7008** suppression wears off | men move between posts today only on `Displace` / `Flanked` / `Hunt` (`SquadDefendBehavior.cs:880,904,939`); OP back = ONE point (`PlatoonDefendBehavior.cs:497`); calm is zeroed while an enemy is heard close and unseen (`StressSystem.cs:107`) ⇒ **first the rail: can a squad be pinned forever?** then the rally | a defender squad changes posts under fire; an OP withdraws along a route; the pinned rail answers yes/no and the fix makes it no |
| **7** | **CE-7010** the air gate | `SupportCommand` today: a known air defence near the point ⇒ a strike plane hits it first (`AirDefenceNear` :148; flag `ElementEquipment.AirDefence = 32`, `CommandReports.cs:99`) — an air request with a known AA in reach is REFUSED and becomes an AA-kill task; routes away from reported AA (design §3b sequence) | `features/air-gate`: the helicopters wait until the ZU-23 / MANPADS are reported dead |
| **8** | **CE-7026** launch · **CE-7027** bomber · **CE-7028** recon → observer · **CE-7029** infantry take cover · **CE-7014** jammer | FPV type 403 (`NedTkbCatalog.cs:1208`), dive brain (`AirAttackBehaviors.cs:281,528`), `DroneImpactSystem`; SPOT → FireSupport (`StaffSystems.cs:303-313`) → `PersonObserveFire` bracket (`IndirectFireBehaviors.cs:471,683`); vehicles shelter (`BasicVehicleSop.cs:58-64`), infantry do not (`BasicInfantrySop.cs:65`); no jammer exists anywhere | `features/drone-team`: an operator launches a recon drone, the mortar fires on what it sees; a bomber drops; men under a drone take cover; a drone in a jammer bubble loses its link |
| **9** | **CE-7030** Watch · **CE-7031** boldness · **CE-7011** the noise-panicked driver | `CivilianMode` (`CivilianState.cs:14`) has no Watch; phone `PhoneShare` (`CivilianBehavior.cs:83,349`); params `AlarmAt` / `PanicAt`; `TrafficPanic.cs:187-200` turns only a HIT car's driver into a civilian. ⭐ fold CIV-3's BUILT state into the Civilians design §5f and check CE-2237's row | `features/civilians-watch`: far fighting ⇒ they watch and film; close ⇒ flee / hide; a bold few keep filming |
| **10** | **CE-7006** mounted / dismounted · **CE-7009** casualty evacuation · **CE-7016** landing zone · **CE-7004** counterattack | `SquadDismountBehavior.cs:32-56`, `VehicleFightFromCoverBehavior.cs:17`; `SquadRecoverBehavior` (`CasualtyCareBehavior.cs:346`) carries to cover only; the LZ is authored today (`ua-air-assault`, `RouteHold` Unload) — EQS has `BeyondThreatRangeTest`, no openness test | each its feature scenario |
| **11** | **CE-7012** `ClearObstacle` | ⏳ after `showcase-world` CE-6003 + CE-6006 (merge it then); the drive nodes `PlatformMove` / `MoveToLocation` (`ClassOrderBehaviors.cs:47`, `CgfNodes.cs:600`); the push is CE-6003's damage seam | `features/bulldozer-clears`: ordered, it drives up and the roadblock is gone |
| **12** | **CE-7034** company vs company · **CE-7033** the dynamism gate · **CE-7018** the interest score | the drafter scores only shots / hits / blasts (`Drafter.cs:80,112`) | the stalemate probe runs nightly; the gate reads a recording |
| **13** | **CE-7017** the full battle · **CE-7019** recording ON within budget | on `showcase-world`'s CE-6010 skeleton | the dynamism gate green on a filmed seed |

⏸ **Not in this batch:** **CE-7015** morale / rout (waits for the user's AQ101 E-3 / E-4) · **CE-7020** the radar SAM (parked) · **CE-2264** (on hold).

## 3. Report — the gate contract (CLAUDE.md rule 8)

Per item, in its row and in `STATUS_Showcase_Brains.md` (this folder, on `showcase-brains`): verbatim commands WITH the switches · counts · `--no-build`
(build the TEST project) · the feature's own suite first (T-1) · stepped-hash per thread count · every red proved pre-existing at the base sha · clean tree ·
`tracker-counts.py --check` · ids allocated · the design section updated (obligation ⑤). ⛔ No broad suite re-runs; ⛔ no full-solution build in the loop;
⛔ slow runs in the background, never polled.
