<!--STATUS
state: LIVE
updated: 2026-11-15
current-answer: §0 the morning report · §1 the items · §2 the gates · §3 decisions
stale-below: nothing
related-designs:
  - HANDOFF_Coordinator_To_Behaviors_Command_Chain_Overnight.md — the batch (dispatched at d6a58138a)
  - STATUS_Behaviors_Command_Chain_Prereqs.md — part A's items, row by row
  - ../../designs/group-maneuvers/DESIGN_Command_Chain_Battalion_Test.md — CE-4615, the scenario this batch proves
-->

# STATUS — `behaviors`, the command chain overnight (CE-4615)

Scope frozen at `d6a58138a`. Started at `bb9366695` (merged `coordinator`), marker `b64adc669`.


## 0. The morning report

### 0.1 The chain per variant (✓ seen in order and caused · not seen — not judged on that variant)

**Pass count** (each variant's judged arrows all ✓): collapse **#4 #5 #6** · stall **#3 #4 #5** · base **none** (closest: #9, every arrow but the
PhaseLine). ⚠ R-2105 (three-squad Blue platoons) landed before collapse #6 / stall #5, so on the CURRENT scenario each has ONE pass.

| arrow (§3.2) | base #11 (current) | base #9 (best) | stall #5 | collapse #6 |
|---|---|---|---|---|
| ① outpost SPOT → withdraws | ✓ 4.0 → 122.4 s | ✓ 3.9 → 173.5 s | — | — |
| ② base of fire Set → the assault | ✓ 88.3 → 105.6 | ✓ 144.2 → 157.7 | ✓ 245.5 → 264.2 | ✓ 115.7 → 126.0 |
| ③ REQUEST FireSupport → fire on the reported point | ✓ 91.5 s, 1 m | ✓ 147.4 s, 1 m | ✓ 184.0 s, 2 m | ✓ 118.9 s, 1 m |
| ④ a block failing / LOST → the reserve commits | ✓ 64.3 → 82.0 | ✓ 49.2 → 60.7 | ✓ 109.4 → 126.1 | ✓ 34.2 → 42.3 |
| ⑤ Red company RED → REQUEST Reinforce | ✓ 110.0 | ✓ 165.0 | ✓ 203.8 | ✓ 136.2 |
| ⑥ the battalion reserve sent | ✓ 122.4 | ✓ 173.5 | ✓ 205.3 | ✓ 141.9 |
| ⑦ the Blue reserve committed (tempo) | ✓ 339.8 | ✓ 336.7 | ✓ 343.3 | ✓ 344.5 |
| phase line reached BY a report | · Objective 1 never taken (CE-2436) | · ended before the report | — | — |
| ⑧ the end | · Red at 35 % at 900 s | ✓ Red < 30 % at 389.6 s | — | — |

### 0.2 What each run taught (root cause → fix)

| run | measured | fixed |
|---|---|---|
| 1 | the report log is a 256-entry ring — the check missed the early reports; zero rounds fired: every call refused *danger close* | the check keeps every report seen; **R-2099** |
| 2 | the company's only call was SMOKE; the outpost never withdraws (OP play unbuilt) | **R-2100** preparatory HE; **R-2101** a scenario rule + **CE-2435** |
| 3 | the rule's BreakContact read *not accepted* (the Order verb counts only a RUNNING order) | the rule orders a Move |
| 4 | Blue won the line's fight but never occupied the objective: the assault bounds ≈ 0.8 m/s to a point 210 m away and the 1.5 × deadline bypassed first | **R-2097 rev. 2** geometry and tempo |
| 5 | PL 1 reached; the assault platoon is destroyed on the objective, so the company never reports it taken | ⏳ open — the next lever is the assault's strength at the point of attack (2 squads against a dug-in line; doctrine wants 3 : 1) |
| 6 | base: the assault cleared every building in the default 80 m and the bypass came first · collapse 2: the block reads LOST and the reserve REINFORCES (CE-7004 by design) | **R-2097 rev. 3** `Radius` 35 · **R-2102** arrow ④ = the reserve commits, by reinforcement or counter-attack |
| 8–9 | base 8: the withdrawal circle held a Blue man at t = 0 ⇒ no edge (CE-4166), never fired · base 9: ①–⑧ ✓, ended before the PhaseLine | the area off Blue's start · the check reads the chain's PhaseLine · R-2104 |
| 10 | base: the two-squad assault platoon's one assault squad went Black inside the office ⇒ `AssaultLost` broke the platoon off at 456 s (probe) | **R-2105** three-squad Blue platoons |
| 11 | base: the three-squad assault is WORN DOWN on the approach — SITREP Red from 153 s, every man alive but hit; Blue never takes Objective 1 | ⏳ **CE-2436** filed — a tactics question (suppression / smoke on the bound, the base of fire's stand-off), not a scenario knob |
| 7 | 🔴 **stall 2 ABORTED the cluster** (`IndexOutOfRange` in `ParallelEntities.Runner.RunBatch`, surfacing in `LinearKinematicsSystem`) · collapse 3: both blocks thin ⇒ half the blocks Black ⇒ the company FELL BACK at 33 s, before contact · base 7: the outpost reads Health 0 at 4 s — a body-part hit from Blue's assembly, the OP standing in the open 140 m away | ⭐ **`FdpWorkerPool` race fixed** (`63a7ac2a7`: a late worker took a batch of a FINISHED run, or one batch twice; the new generation's cursor now goes in closed first; rail `FdpWorkerPoolTests`, red before) · collapse thins BLOCK 1 only, as Battalion Test §4 says (`040c912e8`) · **R-2103** the OP inside the house on the axis |

### 0.3 Dynamism (CE-7033) — thresholds NOT locked

Base run 5: moved > 50 m per 5 min **66 % · 35 % · 15 %**, contacts **84.6 / min**; runs 1–4 similar (45→6 %, 44→11 %, 52→13 %, 45→13 %). ⚠ the
battle goes static in the last window in EVERY run — that is the finding the measure exists for. ⇒ X / Y stay unlocked until three CLEAN base runs
exist (§5 of the design: "set from the first three runs" — a run that fails the chain is not a baseline).

### 0.4 Not reached — stated plainly

⛔ **Acceptance 1 (each variant passes three times) is NOT met.** Collapse and stall pass every run since their fixes (three each, one on the current
scenario); **base has no clean pass**: its assault is worn down crossing 113 m under the line's fire, so the PhaseLine report and the end come only when
Red breaks first (#9). Filed as **CE-2436** with the measurements — the fix is a tactics decision (Platoon / Company Attack), not more tuning here.
⛔ **The dynamism thresholds X / Y stay UNLOCKED** — §5 sets them from three CLEAN base runs, and there are none. Measured on every base run: moved
> 50 m per 5 min ≈ 55–70 % · 35 % · 15–30 %, contacts 54–85 / min ⇒ the battle goes static in the last window, every time.
⛔ **CE-7037 (the stepped gate on a big scenario) not run** — the CPU went to the live runs; diagnosis from code stands (the master advances on the WALL
clock outside `Stepping`); the probe `scratchpad/cc/clockprobe.py` is written. `stepped-hash.py` on cc-battalion therefore not run either.

## 1. The items

| part | row | state | where |
|---|---|---|---|
| A | CE-4614 `Strength` / `QuietArea` objectives | ✅ | Scenario Rules §10.2 · R-2093 |
| A | CE-4589 the battalion's reserve (`SupportCommand.Reserve`, order by echelon) | ✅ engine · HQ-15 showcase wiring → CE-2434 | Command Chain §8e as built · R-2094 |
| A | CE-4610 the reports nobody sends | ✅ PhaseLine + ObjectiveSecure from the attacks, the SITREP ammo colour; FallingBack's reader dropped; CB-3 reconciled | Platoon Attack §7a · R-2095 |
| A | CE-4605 live-men split by rank (one helper, six callers) | ✅ `f627a975e` (Sonnet worker) | rail `CE4605_*` ×2, red-proved |
| A | CE-4606 medic falls back to the leader | ✅ `96f0060f9` | rail `CE4606_*` |
| A | CE-4607 convoy skips a squad with nobody in the fight | ✅ `b3082d184` | rail `CE4607_*` |
| A | CE-4608 a relieved assault squad holds (SquadDefend at the objective's building point) | ✅ `c10c13f33` | rail `CE4608_*` |
| A | CE-7038 a battery mission that fires no round is refused with a reason | ✅ `8aac795db` | Indirect Fire as built · R-2098 |
| A | CE-7035 civilian half (fixture + five real undeclared reads) | ✅ `8aac795db` | Parallel Simulation §18.2a |
| A | CE-7037 the stepped gate on a big scenario | ⏳ code read: the master runs on WALL time outside `Stepping`, so city-battle's clock ran free for ~8 s; probe written, to run after the live checks | — |
| B | CE-7033 dynamism measure (`Dynamism` in utility-demo-check.py) | ✅ built; thresholds not yet locked | `2c372f627` |
| C | CE-4615 generator + 3 scenarios + `run_cc_battalion` | ✅ `2c372f627` | Battalion Test §4a · R-2096, R-2097 |
| D | live runs ×3 per variant, thresholds X / Y | ⏳ 5 base runs + 1 per variant so far, each run's failure root-caused and fixed (§0); acceptance (3 clean runs each) NOT reached — §0.4 | §0 |

## 2. The gates

| command | `--no-build` | result |
|---|---|---|
| `dotnet build Hrot.SimHost.Tests / Fdp.Toolkits.Tests --no-restore` | — | 0 errors |
| `dotnet test Hrot.SimHost.Tests --filter ScenarioRulesTests` plain · `PAR=1 READ_CHECK=1 WORLD_GUARD=1` | ✓ | 141 / 141 · 141 / 141 |
| `… --filter SquadManeuverTests\|StressSystemTests\|CompanyBehavior\|SquadReport\|ScenarioRulesTests` `PAR=0` | ✓ | 423 / 423 |
| the same `PAR=1 + READ_CHECK + WORLD_GUARD` | ✓ | 422 / 423 — `StressSystemTests.CE2272_…` PRE-EXISTING (red at `0a4a4d364`, prereqs status §2) |
| `dotnet test Fdp.Toolkits.Tests --filter S0IdTypesRail` | ✓ | 16 / 18 — the duration rail FIXED here (8 bare floats, see §4); the two left (`NodeId`, `ExerciseId` in Orchestration / ReplayBrowser types) are not this lane's and predate this run |
| `… --filter ClearObstacle\|DroneOperator\|ShippedTree\|Golden` (after the SimDuration change) | ✓ | 36 / 36 |
| `… --filter SquadManeuverTests\|StressSystemTests\|ScenarioRulesTests\|Sop\|ShippedTrees\|SquadDrill` `PAR=0` · `PAR=1 + checks` (after CE-4610) | ✓ | 483 / 483 · 477 / 483 after declaring `WeaponAmmo` — the six reds PRE-EXISTING: CE2272, the four `BasicInfantrySopTests` (prereqs §2), `CivilianHsmTests.CE2322` (CE-7035's civilian half, open) |
| red-proofs | — | CE-4610: `NextObjective`'s report dropped · SITREP without `Ammo` ⇒ each red. CE-4614: burst branch off · `Met` wrong · no deadline ⇒ each red. CE-4589: slot mask ignored · `DepthOf` one level ⇒ both red. All green after |
| `tracker-counts.py --check` | — | OK |

## 3. Decisions (lane decisions — for the user's review)

| id | decision | would change it |
|---|---|---|
| R-2093 | `Strength` takes a UNIT (a side = its top HQ); `QuietArea` counts a shot by the shooter's position, a hit by the victim's, a burst by where it lands | a side with no single HQ; fire passing over a sector meant to count |
| R-2095 | CE-4610 per report: PhaseLine k / ObjectiveSecure SENT by the commander attacks (objective k = line k); SITREP ammo colour SENT (≥ 50 % green, ≥ 25 % amber); FallingBack's reader dropped; FireLifted / LACE left unsent | a squad play with real phase lines; a Lift handshake; other ammo thresholds |
| R-2096 | Red is a CompanyDefend LINE at Objective 1 (blocks across the street), its depth / fall-back at Objective 2 | a CompanyDefend that holds blocks in depth |
| R-2097 | the battalion test's tunables: frontage 160, depth 220 (rev. 2 below for the rest) | the first runs |
| R-2099 | the HQ's answer to a subordinate's FireSupport REQUEST is declared danger close | a request that carries its own danger-close flag |
| R-2100 | `CompanyAttack` asks PREPARATORY HE on the objective as its assault goes in (the half-deadline smoke stays) | the user preferring the tempo's single request |
| R-2101 | the outpost's withdrawal is a scenario rule (Blue within 100 m ⇒ `Order.Move` behind the line) until the OP play (CE-2435) | CE-2435 built |
| R-2097 rev. 2 | Blue assembles 185 m out, IFVs at the rear, outpost 45 m before the line, `DeadlineSeconds` 300, PL 1 / 2 by 420 / 720 s — measured | the next runs |
| R-2098 | a battery mission with no round fired 30 s after its impact time is refused, with a reason | a plan that learns the muscle's refusal reason |
| R-2102 | arrow ④ = the company reserve COMMITS to a failing / lost block — by reinforcement (CE-7004 when the reserve is free) or counter-attack | a design that wants only the counter-attack |
| R-2103 | the outpost sits INSIDE the one house on the axis (296, 388) and withdraws when Blue passes within 35 m — in the open it died to Blue's assembly in 4 s | the OP play (CE-2435) choosing its own hide |
| R-2104 | the check watches up to 180 s PAST the end for the chain's PhaseLine, and a phase line FAILED by the exercise ending is not "missed" | an end that should freeze the chain |
| R-2105 | Blue's platoons are THREE squads (base of fire, assault, reserve): with two, a Black assault squad could only abort the platoon | the user preferring the two-squad slice |
| R-2094 | CE-4589's showcase wiring (HQ-15) deferred to CE-2434 — the defender-company macro has no company commander; the reserve is proved on `cc-battalion` | the user wanting the showcase wired in this lane |

## 4. Defects fixed on the way

| what | fix |
|---|---|
| live run 1: every company fire call refused danger close | R-2099 — `SupportCommandBehavior`, rail `CE4615_TheHqsAnswer…`, red-proved |
| live run 2: the assault met an untouched line (smoke only) | R-2100 — `CompanyAttack.EnterAssault` HE; rail CE7005 extended, red-proved |
| CE-7035: five civilian reads undeclared (would throw under the read check in production) | declared in `BrainReads` |
| `S0_EverySecondsTimeoutCooldownMember_IsNotABareFloat` red (8 members from recent behaviour work) | runtime `ClearObstacleParams.PushSeconds` / `DroneOperatorParams.IntervalSeconds` → `SimDuration` (same size); the six JSON-authored forms (`CompanyAttack.DeadlineSeconds`, `*Defend.ShootAndMoveSeconds` ×3, the two orders) added to the authored-form list |

## 5. Ids allocated

CE-2434 (HQ-15 showcase wiring), CE-2435 (the observation post's play), CE-2436 (the base assault worn down on the approach). Rulings R-2093 … R-2105 (recorded in their designs: Scenario Rules §10.2, Command Chain §8e, Platoon Attack §7a, Battalion Test §4a, Indirect Fire as built).

## 6. Lessons on the way

| | |
|---|---|
| ⚠ memory | the live cluster was OOM-killed while builds ran: ~10 reusable MSBuild nodes × 400 MB + the compiler server. Builds now use `-nodeReuse:false` and `dotnet build-server shutdown` before a live run; one live cluster at a time, no build during it |
