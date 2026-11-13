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
tasks"* · *"It needs to be dynamic"*. ⛔⛔ **Autonomous overnight, NO QUESTIONS** (R-4156, §1a): every decision is pre-made in §1a; anything still open is decided by its ladder and listed for the user's morning review.

## 0. Start

| # | step |
|---|---|
| 0.1 | `git fetch origin exp/threaded-build ui` · `git switch -c showcase-brains origin/exp/threaded-build` *(must contain `a9955908e`, the coordinator note's base)* · `git merge origin/ui` — ⚠ check first `git diff --name-only origin/exp/threaded-build...origin/ui` is docs only; a `.cs` ⇒ STOP and report · push · empty commit `chore: started showcase-brains at <sha>` (rule 1b) |
| 0.2 | ⭐ **merge `origin/showcase-world` at the start too** — measured `2026-11-13`: the world lane has BUILT the town (CE-6001), water (CE-6002), obstacle removal (CE-6003), the bulldozer type `Engineer_Bulldozer = 112` + model (CE-6006/6007), wrecks (CE-6005), macros (CE-6009), variants (CE-6011) and the skeleton is in progress (CE-6010). A conflict in a file the parallel programme owns ⇒ `exp/threaded-build`'s side wins, note it. Then merge both as they move; ⛔ never rebase |
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
- ⭐⭐ **TERRAIN: build on the EXISTING ones — never wait for the final town** (user, `2026-11-13`). Every feature scenario and rail uses
  `assets/terrain/demo-city` (the town fights: streets, houses, a square) or `test-town` / `cqb-village` (small, fast). Items 12–13 run first on
  **demo-city** (`showcases/city-battle-demo-city`); the final town (`showcase-world` CE-6001) is swapped in later by changing the scenario's terrain —
  ⛔ so nothing in a behaviour may depend on the final town's layout (no hard-coded positions; objectives, phase lines and sectors come from the scenario).
- **No UI work.** **Obligation ⑤:** a deviation is folded into the design (and the owning design of the behaviour) before the item closes.
- 📌 **Measured facts the plan already folded in** (do not rediscover): CIV-3 fear-spreads is BUILT (`CrowdSystem.cs:400-421`) · no smoke round exists ·
  no drone launch exists (drones are placed airborne) · triggers fire on CONDITIONS (`OnEntity` / `ScenarioTime` / `Compound` / `CountMatching`), not events ·
  `TimerAction` exists in the HSM builder (`HsmBuilder.cs:248`) and no platoon play uses it — the tempo deadlines' natural seam.

## 1a. ⭐⭐⭐ AUTONOMY — an overnight run with NO QUESTIONS *(🔒 user, `2026-11-13`: "i also need the handoff and tasks to be prepared so well that they can be run overnight autonomously with NO QUESTIONS")*

⛔⛔ **Never ask, never pause, never wait for a reply.** Every choice below is DECIDED; where something is still open, the ladder decides it.

**The decision ladder — take the first rung that answers:**

| # | rung |
|---|---|
| ① | this handoff's **defaults table** below |
| ② | the design (`DESIGN_City_Battle_Showcase.md`) and the behaviour's owning design |
| ③ | **mirror the nearest existing behaviour** (the platoon for the company, the FPV for a new drone, Alarmed for Watch) — same shapes, same names pattern |
| ④ | the option with the **smaller blast radius** (fewer files, no shared seam changed, data before code) |
| ⑤ | record what you chose as a **lane default** — a ruling `R-70xx` in RULINGS.md marked *"lane default, user may override"* + a line in the design's §10 — and carry on |

**When something goes wrong — what to do instead of asking:**

| situation | do |
|---|---|
| an item's premise is false (a seam is not where this file says) | measure where it is, fold it into the design (obligation ⑤), continue the item |
| an item needs a file the parallel programme is changing (`git diff --name-only HEAD...origin/exp/threaded-build` touches it after your last merge), a user ruling changed, or the final town | ⛔ STOP **that item only** — note it in its row (`⏸ blocked: <why>`), take the next item (R-106) |
| a test red that is not yours | prove it pre-existing at the base sha (one run on a clean worktree), note it, continue; known flaky ⇒ `[FlakyFact("<row>: why")]` with an OPEN row |
| stuck on a defect: 3 hypotheses tested, none confirmed, or ~90 min on one item | file a row with what was measured, mark the item `⏸ stuck: <row>`, take the next |
| a threaded-editor limit bites (L-1…L-4 in the coordinator note) | use the switch setting of §1 (threaded editor OFF); ⛔ never work around it in code |
| a merge conflict with `exp/threaded-build` | their side wins in their files; yours in `scenarios/showcases/`, your new files and Area S-B rows |
| a new TKB type / munition / component id | the next free number in the SAME range, checked on every lane (`git grep` over `origin/*`) — ⛔ a permanent wire value, never reused |
| a number this table does not give | take the nearest existing behaviour's value; record it (rung ⑤) |

⭐ **Push after every green item** (`showcase-brains`), with its row updated — the user reads progress in the morning from the rows and `STATUS_Showcase_Brains.md`.
⭐ **The morning report** (end of `STATUS_Showcase_Brains.md`): done / blocked / stuck per row, every lane default taken (rung ⑤), every decision the user should review — ⛔ a list, not questions.

### The defaults table — every decision the items would otherwise ask

| item | decided |
|---|---|
| **1** F-0 | CE-2341: author `Suspected: true` as the row proposes; done = 12/12 in its flaky lane, else keep it `FlakyFact` and move on · CE-2304: one root-cause pass (ladder's stuck rule) · the two LIVE looks: run the named scenario headless (threaded editor OFF), done = the state is reached in the recording (`FallBack` entered; the technical fires, moves, fires) — record the tick |
| **2** company | the company = 3 platoons + 1 support element (mortar section + AT team + drone team as one element) ⇒ 4 subordinates ≤ `MaxSubordinates = 6` — **no change to `CommandPicture`** · the commander's entity mirrors the platoon commander's exactly (same kind of entity, same brain hosting, `defaultSop: "BasicCompanySop"`) · new type ids by the id rule |
| **3** attack / defend | phase lines and sectors arrive IN THE ORDER: objective + an ordered list of phase-line positions, carried by the existing intent payload; if it cannot hold a list, by named scenario markers the order references — pick the one the payload supports, record it · attack: one platoon base of fire, one or two assault, the third or the reserve held until the assault reaches ≤ 50 % strength or the objective is half taken · defend: one block per platoon in line, the reserve 300 m behind; fall back to the next phase line when ≥ half the platoons report failing (the platoon's `HalfTheLineFailing` rule one level up) |
| **4** tempo | deadline per company phase = the order's deadline, default **8 min sim**; at 50 %: request smoke + fires on the blocking position; at 100 %: commit the reserve, else switch to the bypass axis (the next sector's street) · `TimerAction` for the clock |
| **5** smoke | the mortar smoke round = grenade 6041's cloud scaled up: radius 20 m, height 8 m, 90 s · `FireSupport` gains a munition kind (HE default, Smoke) · CE-2278 as its row says |
| **6** active defence | shoot and move: a man in contact changes to another post of his squad's set after **25–40 s** (seeded per entity) at one post or after his post takes a hit · OP withdrawal: the route = 2–3 points from the OP to the main line, chosen by EQS (covered, ≤ 80 m apart) · pinned: "forever" = stress over the break level for **> 120 s** with no shot heard within 50 m; the fix: calm resumes after **30 s** without a new close sound, and a leader within 20 m doubles it (the existing term) |
| **7** air gate | "AA in reach" = a reported air-defence element whose weapon range (from its TKB) + 20 % covers the request point or the planned route · refused ⇒ the HQ turns it into a ground FireSupport on the reported AA (mortars), else a Reinforce-style attack order to the nearest free platoon · the air request is re-tried when that AA is reported destroyed, or after **10 min** with no new report of it |
| **8** drones | a new infantry type "drone operator" with a stock: **4 FPV, 2 bomber, 1 recon** (params) · launch: an ELM creation at the operator, airborne after 2 s · bomber: hovers 30 m over the target, drops the existing frag grenade, 3 drops, returns · recon: loiters 100 m up over the ordered sector, its existing visual sensor spots, SPOT reports into the observer chain · new drone types reuse the `Drone` visual family — ⛔ no new models · infantry cover: the vehicle's `ShelterFromAir` pattern as an infantry SOP row (go prone / into the nearest overhead cover) · jammer: a component (radius **300 m**) a vehicle or post carries, authored in the scenario; a drone inside loses its link: FPV / bomber fly 3 s on their last heading and crash, recon returns to its operator |
| **9** civilians | Watch: the nearest shot / blast at **150–600 m** ⇒ stop, face it; **40 %** of watchers take the phone out; < 150 m ⇒ the existing Alarmed / Panic · boldness: **5 %** seeded per entity — Watch down to 50 m, flee only when hit near (≤ 30 m) or fear ≥ 1.5 × `PanicAt` · the driver: a moving car within **50 m** of a blast ⇒ its driver bails out as a panicking civilian (the hit-car path) |
| **10** the rest | dismount: carriers stop at the last covered point ≤ 300 m from the objective, squads dismount, carriers run `VehicleFightFromCover` there · casualty evacuation: carry to a collection point **150 m** behind the platoon (the platoon's rear) · LZ: a new EQS openness test (no building / obstacle within 25 m, slope < 10°) + `BeyondThreatRangeTest`, 300–800 m from the objective · counterattack: when a company objective is lost and the reserve is ≥ 70 % strength ⇒ `CompanyAttack` on it, once |
| **11** ClearObstacle | `rate` = the obstacle's `MaxHealth` / **10 s** (any obstacle cleared in 10 s of contact) through `ObstacleDamage.Damage`; the company orders it after smoke lands |
| **12** dynamism + interest | the gate's thresholds = **70 %** of the first good demo-city run's measured values (recorded in the design) · the drafter's new lanes: aircraft present 2, a squad moving > 50 m 1, a building collapse 4 — merged like the existing ones |
| **13** the battle | `showcases/city-battle-demo-city`: 2 attacking companies (mech) vs 1 defending company + its reserve, ≈ 400 men, civilians ≈ 60, recording ON, visibility 5 000 m (`world.WeatherConditions.VisibilityM`) |

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
| **11** | **CE-7012** `ClearObstacle` | ✅ unblocked — CE-6003 + CE-6006 are BUILT on `showcase-world` (merged in 0.2): the bulldozer is `Engineer_Bulldozer = 112`; the blade's seam is **`ObstacleDamage.Damage(repo, obstacle, rate × dt, dozer)` once per tick of contact** ([Peek and Fire §9.9](../../DESIGN_Peek_And_Fire.md), as built); the drive nodes `PlatformMove` / `MoveToLocation` (`ClassOrderBehaviors.cs:47`, `CgfNodes.cs:600`); the push is CE-6003's damage seam | `features/bulldozer-clears`: ordered, it drives up and the roadblock is gone |
| **12** | **CE-7034** company vs company · **CE-7033** the dynamism gate · **CE-7018** the interest score | the drafter scores only shots / hits / blasts (`Drafter.cs:80,112`) | the stalemate probe runs nightly; the gate reads a recording |
| **13** | **CE-7017** the full battle · **CE-7019** recording ON within budget | first on **demo-city** (`showcases/city-battle-demo-city`, the forces scaled to fit); then on `showcase-world`'s `showcases/city-battle` (CE-6010) and its town (CE-6001) — merged in 0.2 | the dynamism gate green on a filmed seed — demo-city first |

⏸ **Not in this batch:** **CE-7015** morale / rout (waits for the user's AQ101 E-3 / E-4) · **CE-7020** the radar SAM (parked) · **CE-2264** (on hold).

## 3. Report — the gate contract (CLAUDE.md rule 8)

Per item, in its row and in `STATUS_Showcase_Brains.md` (this folder, on `showcase-brains`): verbatim commands WITH the switches · counts · `--no-build`
(build the TEST project) · the feature's own suite first (T-1) · stepped-hash per thread count · every red proved pre-existing at the base sha · clean tree ·
`tracker-counts.py --check` · ids allocated · the design section updated (obligation ⑤). ⛔ No broad suite re-runs; ⛔ no full-solution build in the loop;
⛔ slow runs in the background, never polled.
