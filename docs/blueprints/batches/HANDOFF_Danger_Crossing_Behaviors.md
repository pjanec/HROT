<!--STATUS
state: LIVE — DISPATCHED at de30b58cb (2026-10-06, user: "Divide the work between you and behaviors lane to work in
  parallel, with merging the other lane as you go (i will need handoff doc for the behaviors branch)"); scope frozen there.
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
