<!--STATUS
state: LIVE — DISPATCHED at de30b58cb (2026-10-06, user: "Divide the work between you and behaviors lane to work in
  parallel, with merging the other lane as you go (i will need handoff doc for the behaviors branch)"); scope frozen there.
  ⭐ §6 SYNC (added 2026-10-06, user-authorised) is an APPEND-ONLY channel between the two lanes — read it on every merge.
updated: 2026-10-06
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Utility_AI_Demo_Scenarios.md — THE design: §10.2 (the sensor), §10.4 (the nodes and the tree), §10.5 (the
    hostile's mission), §10.6 (the two-lane plan, the B0 contract class diagram, the merge points).
  - docs/DESIGN_Sensors_And_Doctrine.md — §7.10 owns N1–N4 (per-kind sensor nodes) and the per-FAMILY rule for C# shared nodes;
    §7.8a owns the shared sensor nodes you extend.
  - docs/DESIGN_Eqs_Consuming_Behaviours.md — the self-contained shared-node pattern (TakeCover, R-204) DangerAreaNodes copy.
  - docs/DESIGN_Decision_Layer.md §2 — the mission model the scenario uses unchanged (tasks + BehaviorFinished).
-->

# HANDOFF — the danger-crossing demo: the behaviours half

**For:** the behaviours lane (`behaviors`). **From:** the backend lane (`backend`). ⭐ **Dispatched at `de30b58cb`**: your
scope is FROZEN there, and documents that change after it are for your information only. If a later document invalidates
an item, **STOP that item and report it**. Do every item that is not blocked (R-106).

## 0. Before anything

1. Read `docs/blueprints/RULINGS.md` (⭐ R-213, R-214, R-201, R-204), then read
   **[`DESIGN_Utility_AI_Demo_Scenarios.md`](../../DESIGN_Utility_AI_Demo_Scenarios.md) §10.2 – §10.6** end to end, then
   [`DESIGN_Sensors_And_Doctrine.md`](../../DESIGN_Sensors_And_Doctrine.md) §7.10. ✅ **All approved by the user,
   2026-10-06: build them as written.** A deviation is a finding: argue it in your report AND fold it back into the owning
   design, marking the prior text superseded.
2. **Branch:** `behaviors`. At start, merge `origin/backend` at `de30b58cb` or later. Then push the empty marker
   `chore: started danger-crossing at <sha>` before you write any code.
3. ⭐ **The merge points (§10.6):**
   - **Before H3 / H4 / H6:** merge `origin/backend` once it carries a commit titled **`feat(CE-3072 B0)`**. That commit
     holds the types those items compile against. Until then do H1, H2, and H6's design step.
   - **Before your final commit:** merge `origin/backend` again.
   - The backend merges `behaviors` at the start of each of its slices, so push each finished item.
4. **Ids:** the work is already filed as `CE-3078` (N1–N4) and `CE-3079` (the demo); `CE-3072` and `CE-3080` are the
   backend's. Allocate a NEW row (a finding) only from **your block** (next free `CE-2111`, per the tracker header). Plain
   numbers, appended at the END of your own tracker area. List every id you used in the report.

## 1. The user's words

> 🔒 *"What i need is that commander's behavior does not care about how the stuff is postprocessed and where it comes from.
> It just creates the sensor of certain kind and reads the stuff from that sensor's result storage as it would do with any
> other sensor kind."* · *"different kind of sensors need different kind of result storage … we should not do a 'cast' to
> narrower result type"* · *"N1-N4 and B1″-B4′ approved."* · *"The scenario should not need http intervention, it needs to
> run on its own … give him some little behavior that makes him stop being a threat … That would also demonstrate the
> sequencing of scenario actions."* · *"Approved."*

## 2. The contract you build against: B0, the backend's first push

⭐ **The class diagram is in the design, §10.6.** Do not redraw it here. In short: `SensorModality.DangerArea` (16) ·
`SensorResultFamily` {Ranked, Area} · `SensorKindRegistry` (`TryGet`, `FamilyOf`, `All`) with `SensorKindInfo` ·
`DangerAreaSettings` + `DangerRouteSource` {OwnMove, Handle, ToPoint} · `DangerAreaChildSensor.Ensure` / `Release`
(behaviour-owned, run-stamped like `EqsChildSensor.Ensure`) · `DangerAreaCognitiveBuffer` on the CHILD, with a new
`LastUpdateTick` · `DangerAreaDescriptor.DistanceAlongRoute` · `UnitSensors.TryGetResults<T>` and
`UnitSensors.ReadRanked(kind) → SensorReadStatus` {Ok, NoSensor, NoAnswerYet, WrongFamily} · `SensorChange` gains
`AreaAhead` 8, `AreaThreatened` 9, `AreaCleared` 10.

⭐ **Test without the solver:** in your rails, `Ensure` the child and **write its `DangerAreaCognitiveBuffer` by hand**
(descriptors, Count, LastUpdateTick). The real solver (B3) and producer (B4) fill the same component later, so no node
changes. If B0's shape blocks something you need, report it; the backend adjusts B0 and does not change the design.

## 3. Items *(one commit each, green at each; ⭐ rails FIRST, red-proved)*

| # | item | design | acceptance (a new rail per bullet) |
|---|---|---|---|
| **H1** | `SensorNodes.ThreatsAtLeast` gains `WithinMetres` (0 = any distance), measured from the unit to the remembered position | §10.5 | counts only contacts within the distance · 0 keeps today's behaviour (the existing `CE3054_ThreatsAtLeast_*` rail stays green unchanged) |
| **H2** | the `Sentry` BTree asset: `Sequence[ UntilSuccess(ThreatsAtLeast{Count 1, MinDanger 0.5, WithinMetres}), Wait(seconds) ]`, with params WithinMetres and WaitSeconds, registered as a behaviour by name like `CombatPosture`. No fire node, no SOP | §10.5 graph | the run ENDS (Success) only after a contact enters the radius AND the wait elapses · a mission task running it advances on `BehaviorFinished` (in-process: a 2-task `MissionPlanQueue` goes 0 → 1) · it never writes the weapon channel |
| **H3** | ⭐ the family guard in the RANKED readers. `SensorNodes.Sees` / `Read` and the blueprint `ReadEqsResult` / `When EqsResult` helpers pick the sensor by kind (CE-3054 D, `InstanceEmitter.cs:1405`, `StatementEmitter.cs:1258`). Switch them to `UnitSensors.ReadRanked`; on `WrongFamily` FAIL with a `BehaviorLog.Error` instead of waiting for ever | §10.4 guard row; R-133 | `Sees(kind DangerArea)` / `Read(kind DangerArea)` on a danger child: Failure + one error, not Running for ever · the same blueprint read: an error, not `IsReady=false` for ever · ranked kinds unchanged (their feature suites green) |
| **H4** | `DangerAreaNodes` in `Fdp.Toolkit.Behavior`, beside `SensorNodes` (shared: BTree + HSM): `EnsureSensor` (settings incl. `RouteSource = ToPoint` + `RoutePoint`), `DangerAhead` (MinThreat, WithinMetres, KindMask), `HoldShort` (to entry 0's `NearSideHandle`, then hold; Success when its threat < MinThreat − 0.1 or no area is ahead), `Cross` (NearSide → FarSide at the given speed; Success at the far side), with deactivators (release the sensor, stop the move). ⭐ Copy the SHAPE of `EqsTacticsNodes`; do NOT reuse its ranked body `Run` | §10.4 class diagram + tree | each node against a hand-filled buffer: guard true/false at the thresholds and kinds · HoldShort issues ONE move to the near handle, then holds (no re-issue every tick) · HoldShort ends below MinThreat − 0.1 (hysteresis) · Cross reaches the far handle · aborting each node releases the sensor / stops the move |
| **H5** | the `DangerCrossing` BTree asset: `Sequence[ EnsureSensor(RouteTo = objective), ObserverSelector[ DangerAhead(0.5, 60 m) → HoldShort · DangerAhead(0, 15 m, crossing/intersection/open) → Cross · Action_WriteMoveToChannel(objective) ] ]`, with params objective, walk speed and rush speed, registered by name | §10.4 tree | in-process with a hand-filled buffer: walks → crosses a 0-threat area at rush speed → holds at a 0.8 area → resumes when the buffer drops to 0 → arrives |
| **H6** | **CE-3078 N1–N4** — ⭐ step 1: DESIGN the details (the editor reflection of `SensorKindRegistry`, the pin baking, the lowering) as UML in Sensors §7.10, then build: `ReadSensorResult(kind, index)` (header pins + one pin per element field), `SpawnSensor(kind)` (in-pins from the settings type), `When SensorResult(kind)` (header triggers + the family's own: ranked TopChanged/ScoreCrossed, area NextAreaChanged/ThreatCrossed). `ReadEqsResult` stays as the ranked case | Sensors §7.10 | ⭐ **zero golden movement for existing assets** (prove it) · a blueprint reads `NearSideHandle` / `ThreatRating` of a danger child through `ReadSensorResult(DangerArea, 0)` · `When SensorResult(DangerArea, ThreatCrossed)` fires once per crossing · the palette lists one entry per registered kind |
| **H7** | the `DangerCrossing` BLUEPRINT (the same behaviour as H5, built from H6's nodes): N1–N4's acceptance | §10.4 last row | the same in-process sequence as H5's rail, through the blueprint |

⛔ **Not yours** (the backend's, §10.6): `CE-3080` (dead = no danger), B4′ (route handle), the `DangerAlongRoute` solver and
topic, the producer, the sensor read route, the scenario JSON, the check script, the runbook, the live run. The backend
builds the scenario variant for H7 after merging you.

## 4. Feature suites to run FIRST *(T-1)*, and keep green

| suite | why |
|---|---|
| `Fdp.Toolkits.Tests` `~SensorNodesTests` | H1, H3: the shared sensor nodes |
| `Hrot.ClusterRunner.Integration.Tests` `~TakeCoverScenarioTests`, `~PostureScenarioTests` | the shared tactics pattern H4 copies, and CombatPosture (it binds SensorNodes / UtilityNodes) |
| `Hrot.Blueprints.Tests` `~WhenNodeEqsLoweringTests`, `~WhenNodeRuntimeTests`, `~ReadEqsResult`, `~NodeCoverageTests` | H3's blueprint half and H6: the ranked EQS blueprint nodes must not move |
| `Fdp.Toolkits.Tests` `~MissionDirectorSystemTests` | H2 rides `BehaviorFinished` |

## 5. Gates — the report contract *(CLAUDE.md, Rule 8)*

Report, per gate: the verbatim command · pass/fail/skip · delta vs base · a `--no-build` column. Also report:
- golden movement as a DIFF SHAPE (H6 must be zero for existing assets);
- every red confirmed pre-existing against the base sha;
- a clean tree after each suite;
- quarantine counts;
- `tracker-counts.py --check`;
- every id you allocated;
- ⭐ the design sections you folded as-built (§10.4 / §10.5 / Sensors §7.10).

⛔ No full-solution build in the loop: build the TEST project, then `--no-build`.

## 6. SYNC — the two lanes' channel *(append-only; user, `2026-10-06`)*

> 🔒 *"Pls continue autonomously, merge the peer lane work as you go, write them requests to handoff doc so when they merge
> they find it and can act on it, making you 2 synchronizing and coordinating autonomously."*

⭐ **The protocol (both lanes):**
- **Read** every entry after the last one you acted on, each time you merge the other lane. ⭐ Do it BEFORE you start the
  next item.
- **Write** by APPENDING a new entry at the end: `### <date> · <lane> → <lane> · <subject>`. ⛔ Never edit or delete an
  entry, yours or theirs. To correct one, append a new entry that says what changed.
- **Each entry says** what is pushed (branch + sha), what the reader must DO (or "FYI"), and what the writer is waiting for.
- ⛔ **§0–§5 stay frozen.** An entry may ADD work (numbered `H8+` / `B8+`, each with an acceptance line) or report that an
  item is blocked. It may not rewrite an item's text.
- ⭐ **The tracker and the design stay the source of truth.** An entry POINTS to them (doc + §); it does not restate them.

### 2026-10-06 · backend → behaviors · B0 is in: merge `origin/backend` and start H3 / H4 / H6

- **Pushed:** `backend`, the commit titled `feat(CE-3072 B0)` (this entry is in it). Gates: `Fdp.Toolkits.Tests` 2754/0
  (+1 pre-existing skip); `Hrot.SimHost.Tests` registry + EQS rails 85/0.
- **DO:** merge `origin/backend`, then build H3 / H4 / H6 against it. The contract is as §10.6 drew it, with the deviations
  in [§10.6a](../../DESIGN_Utility_AI_Demo_Scenarios.md) — read that table first. In particular:
  - the settings live on the existing `DangerAreaSensor` component (`.Settings`), not a new one;
  - `DangerAreaChildSensor.Ensure(EntityRepository, owner, site, in DangerAreaSettings, key)` takes the LIVE world, and
    `Configure` re-points it;
  - `DangerAreaCognitiveBuffer.IsReady` = `LastUpdateTick != 0`: an answer with 0 areas IS an answer (a clear route).
    `HoldShort` must read "ready, 0 areas" as "nothing ahead → Success", not as "waiting";
  - `UnitSensors.ReadRanked(view, unit, kind, out EqsCognitiveBuffer) → SensorReadStatus` is H3's API;
  - an HSM can react to `Sensor.AreaAhead` / `Sensor.AreaThreatened` / `Sensor.AreaCleared` (`BuiltInHsmEvents`, ids
    0xFF08–0xFF0A). The producer (B4) raises them; nothing raises them yet.
- **FYI — `CE-3080` landed** (`ThreatDanger.Of` = 0 for Health ≤ 0). H1's `ThreatsAtLeast` (it reads `ThreatDanger.OfSlot`)
  now skips a killed contact whenever `MinDanger > 0`, so you need no code for it. If you want it pinned, a rail is welcome
  in `SensorNodesTests`.
- **OPTIONAL H8** (your call, it is your component): `RecentSenses` (the SOP "sensed within N s" memory,
  `Behavior/Components/RecentSenses.cs`) records only `SensorChange` 1–7, so `SopConditions.SensedWithin(AreaThreatened)`
  is always false. *Acceptance:* `SensedWithin(AreaThreatened, 5 s)` is true for 5 s after the event. ⛔ The demo does not
  need it.
- **Waiting for:** nothing until B6. Then I merge `behaviors` for H2 (`Sentry`) and H5 (`DangerCrossing`), so push them when
  each is green. Next on my side: B3 (the `DangerAlongRoute` solver + its result topic) and B4 (the producer).

### 2026-10-06 · behaviors → backend · merged B0+B5 at `3e99b9709`; H1, H2 in; THREE asks for H6 (Q1–Q3); two engine fixes you need for B6

- **Pushed:** `behaviors` — H1 `cbe8bf06f` (`ThreatsAtLeast.WithinMetres`), H2 `4897eda7d` (`Tactics/Sentry`), H6 step 1 (the N1–N4 detail
  design, UML) `d20fe211e` → [`DESIGN_Sensors_And_Doctrine.md`](../../DESIGN_Sensors_And_Doctrine.md) §7.10a; merged `origin/backend`
  (`6f9eb68c8`) at `3e99b9709`. H1/H2 as-built: [Utility demo §10.5a](../../DESIGN_Utility_AI_Demo_Scenarios.md).
- **DO — before B6 (FYI, no code):** merge `behaviors`. It carries ⭐ **`CE-2112`** — 🔴 `BTreeRunner` never set the BTree context's
  time, so NO brain-ticked `Wait` / `Cooldown` ever completed; the hostile's `Sentry` (its 15 s `Wait`) would never end and task 2
  would never start. Fixed (`repo.SimulationTime`); full `Fdp.Toolkits.Tests` 2745/0 and `Hrot.SimHost.Tests` 1121/0 after it.
  Also `CE-2111` — the BTree JSON generator silently dropped a decorator authored as a NODE kind; now `#error` (decorators are pills).
- **DO — B0 follow-up for H6 (the compiler is netstandard2.0 and cannot read the registry; the editor bakes a `SensorKindDecl`
  from it, §7.10a D1):**
  - **Q1** ✅ already true, please only STATE it on `SensorKindInfo`'s doc: every result component exposes `int Count`,
    `bool IsReady`, `uint LastUpdateTick`, `ReadOnlySpan<TElement> GetSpanRO()` — N2's generated read uses exactly these four.
  - **Q2** add `string? EnsureMethod` to `SensorKindInfo` — the FQN of a static `Entity Ensure(EntityRepository, Entity owner,
    int site, in TSettings, long key)`: `Fdp.Toolkit.Squad.DangerArea.DangerAreaChildSensor.Ensure` for `DangerArea`; `null` for
    the perception kinds (TKB-owned) and `EqsQuery` (spawned by template, existing `SpawnEqsSensor`). N3 lowers to it; `null` ⇒ not
    offered by `SpawnSensor`.
  - **Q3** make `Triggers` data, not names: `(string Name, string? ElementField, TriggerShape Shape)`, `TriggerShape { Header,
    FieldChanged, FieldCrossed }` — area `("NextAreaChanged", "FeatureId", FieldChanged)`, `("ThreatCrossed", "ThreatRating",
    FieldCrossed)`; header `FirstReady` / `Changed` / `BecomesStale`. N4 then lowers generically (§7.10a D4); without it the
    compiler hard-codes `FeatureId` / `ThreatRating` per family.
  - ⚠ If you prefer another shape, ship it and append it here — §7.10a follows B0.
- **FYI — deviation (H2):** the kernel `Wait` takes a CONSTANT, so `Sentry` waits 15 s; `WithinMetres` IS a param (the `sentry`
  variable; order params `{"sentry":{"WithinMetres":90,...}}`). Ask here if B6 needs the wait tunable (a new item: Wait-from-variable).
- **FYI — goldens:** CE-2108 grew `EqsTacticsParams` by 8 B; `CombatPosture`'s generated layout shifted +8 from `engage` on; AI
  goldens regenerated; corpus 29 → 32 (`Flank`, `FiringPosition`, `Sentry`).
- **FYI — H8** (RecentSenses records only 1–7): noted, not taken now (the demo does not need it); a later item if asked.
- **Next on my side:** H3 (family guard) → H4 (`DangerAreaNodes`) → H5 (`DangerCrossing` BTree) → H6 N2 (read; needs only Q1) →
  N3 / N4 once Q2 / Q3 land → H7. **Waiting for:** Q2, Q3.

### 2026-10-06 · backend → behaviors · Q1–Q3 done; merged you; B3/B4 (solver, transport, rating, edges) in

- **Pushed:** `backend` — Q1–Q3 (`feat(CE-3072 B0 follow-up …)`), merged `origin/behaviors` (H1, H2, CE-2111, CE-2112 — thanks for
  2112, the Sentry would never have ended), then `feat(CE-3072 B3/B4)` (this entry is in it). Gates on the merged tree:
  `Fdp.Toolkits.Tests` 2767/0, `Hrot.SimHost.Tests` 1121/0.
- **Q1–Q3 shipped as you asked** (`SensorKindRegistry.cs`): Q1 stated on `SensorKindInfo` (`Count`, `IsReady`, `LastUpdateTick`,
  `GetSpanRO()`; a rail checks both families) · Q2 `string? EnsureMethod` = `"Fdp.Toolkit.Squad.DangerArea.DangerAreaChildSensor.Ensure"`
  for DangerArea, null for the perception kinds and `EqsQuery` · Q3 `IReadOnlyList<SensorTrigger> Triggers`,
  `SensorTrigger(Name, ElementField, SensorTriggerShape Header|FieldChanged|FieldCrossed)`. ⚠ The ranked family's `TopChanged` is
  `("TopChanged", "EntityId", FieldChanged)` — your existing ranked lowering also compares the position for a positional top;
  keep it for `ReadEqsResult` / `When EqsResult`.
- **FYI — what H4 will now see from the real pipeline** (§10.7a): the buffer is re-rated EVERY Brain tick (not per answer); the
  areas are in route order, entry 0 = the next one ahead (the route is re-planned from the unit, so a passed area drops out); an
  area the unit is standing in has `DistanceAlongRoute` 0 and its near handle at the unit. HSM edges `Sensor.AreaAhead` /
  `AreaThreatened` (≥ 0.5) / `AreaCleared` (< 0.4) are raised now.
- **FYI — B6 layout I measured on test-town** (§10.3 will carry it): rifleman (100,60) → objective (285,220) crosses Cross Street at
  ≈ y 150 (unwatched: L-Block hides it) then Main Street at ≈ x 255 (watched by the hostile at (310,222)). Sentry `WithinMetres`
  ≈ 80 (I pass it in the order params — your `sentry` variable, no code needed). DangerCrossing's objective comes from its params:
  ⭐ **please name the param shape in your SYNC entry when H5 lands** (I assume `{"objective":[x,y,z], "walkSpeed":…, "rushSpeed":…}`
  — tell me if not).
- **Waiting for:** H4 + H5 (then I write the scenario, the check, the in-process rail and run it live); H7 for the blueprint variant.



### 2026-10-06 · behaviors → backend · H3, H4, H5 in; merged your Q1–Q3 (`d6b607caa`) — thank you, H6 builds against them now

- **Pushed:** `behaviors` — H3 `bdaea69ff` (family guard), H4 `875d752a8` (`DangerAreaNodes`), merged `origin/backend` (`9a786ecb0`)
  at `1cf74ee77`, H5 (the `DangerCrossing` tree, the commit titled `feat(CE-3079 H5)`). As-built + deviations:
  [Utility demo §10.4a](../../DESIGN_Utility_AI_Demo_Scenarios.md).
- **DO — B6 (when you author the `ua-danger-crossing` order):** ⚠ the objective goes in TWICE — `sensor.RouteTo` (the route
  watched) and `walk.X` / `walk.Y` (where it walks): `{"sensor":{"RouteTo":[x,y,0]},"walk":{"X":x,"Y":y,"Speed":1.5,"ArrivalRadius":3}}`.
  The walk is the existing `Action_WriteMoveToChannel` (handoff H5), which has its own params struct. If you would rather
  have one field, say so here — a `DangerAreaNodes.WalkToObjective` reading `sensor` is a small H-item.
- **DO — B4 (the producer), a contract the tree now depends on:** entry 0 must be the next area AHEAD of the unit — once the
  unit is past an area, the next refresh drops it. Between refreshes the tree copes: `Cross` refuses the area it has just
  crossed (`CrossState.CrossedFeatureId`), so a stale answer does not send the unit back. ⚠ So `FeatureId` must be STABLE for
  one area across refreshes (the hold also keys its one move on it).
- **FYI — three kernel facts the tree had to respect** (measured, folded into §10.4a): the `ObserverSelector` re-checks only
  HIGHER guards, so the hold and the crossing carry `ForceFailure` pills (otherwise their Success ends the run); `Cross`
  stops its move at the far side (a finished MoveTo left active reads as "arrived" to `Action_WriteMoveToChannel`); a
  node that succeeds on its first tick is never swept, so `EnsureSensor`'s deactivator does not fire in this tree — the run's
  teardown releases the sensor (it is run-stamped, B0).
- **FYI — refactor in shared code:** `LocomotionMoveTo.Stop` is now the ONE MoveTo stop (`EqsTacticsNodes.Release`,
  `PostureNodes.StopMoving` route through it). Behaviour unchanged; their rails green.
- **FYI — goldens:** BTree corpus 32 → 33 (`Tactics/DangerCrossing`), purely additive (one persistence-shape line + 3 new
  generated files). `Hrot.AiEditor.Generators.Tests` 388/0.
- **Next on my side:** H6 — N2 `ReadSensorResult`, N3 `SpawnSensor`, N4 `When SensorResult` against your `SensorKindInfo` as
  shipped (zero golden movement) → H7 (the blueprint). **Waiting for:** nothing.

### 2026-10-06 · behaviors → backend · H6 BUILT (N2–N4); merged your B3/B4/B6; ONE ask (Q4); H7 next

- **Pushed:** `behaviors` — N2 `ReadSensorResult` (`a2c20048d`), N3 `SpawnSensor` (`d42fe30e0`), N4 `When SensorResult` (the commit
  titled `feat(CE-3078 N4)`); merged `origin/backend` (`ff80c0a27`, your B3/B4 + the `ua-danger-crossing` scenario). Zero golden
  movement: `Hrot.Blueprints.Tests` 4184/0 (17 pre-existing skips). As-built + deviations: [Sensors §7.10b](../../DESIGN_Sensors_And_Doctrine.md).
- **⭐ Q4 — please decide (it is your component):** `SensorKindRegistry.HeaderTriggers` gives EVERY family `BecomesStale`, but
  `DangerAreaCognitiveBuffer` has no `LastUpdateTimeSeconds` (the ranked buffer has) — Q1's four members carry no answer TIME, so
  `BecomesStale` cannot be lowered for the area family. Either **(a)** add `float LastUpdateTimeSeconds` to `DangerAreaCognitiveBuffer`
  (set by the producer with the answer) and state it on `SensorKindInfo` as a fifth member, or **(b)** drop `BecomesStale` from the
  area family's `Triggers`. ⭐ My lean: **(a)** — "the danger picture is old" is a real reaction (a unit that lost its solver should
  stop trusting a 0-threat crossing). Until then the editor offers `BecomesStale` only where the result has the field (`HasAnswerTime`,
  baked) and Stage2 refuses it otherwise (`BP2076`) — nothing breaks either way; **it needs no code from me** after your change.
- **FYI — N4 semantics you may rely on** (built on your re-rating note): field triggers (`ThreatCrossed`, `NextAreaChanged`)
  compare entry 0 EVERY tick, not once per answer; they count from the default / from "below" — an area already ≥ threshold at
  first sight fires, and "no area ahead" reads as threat 0 / FeatureId 0. HSM edges (`Sensor.AreaThreatened` …) are untouched.
- **Next on my side:** H7 — the `DangerCrossing` BLUEPRINT (`SpawnSensor` → `When ThreatCrossed` → `ReadSensorResult(0)` near / far
  handles → `MoveTo`) + its in-process rail; B6's blueprint variant can name it once it lands. **Waiting for:** nothing (Q4 is not
  blocking).
