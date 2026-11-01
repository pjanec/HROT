<!--STATUS
state: LIVE — coordinator → backend: the QUEUED batch after S0 (overnight, autonomous): CE-4153 S9′ · node features S1 · the semantic types CE-4201…CE-4204 · scenario macros S3a (the core)
updated: 2026-11-01
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Entity_Keys.md — S9′ (✅ 2026-11-01): references in files carry their GUID — no reverse remap
  - ../../DESIGN_Node_Features.md — §4 F-13…F-20 (✅) and §6 THE S1 TECHNICAL SPEC (catalogue, C# shapes, home, speed, wire, recording + go-live)
  - ../../DESIGN_Semantic_Value_Types.md — SV-1, SV-4, SV-5, SV-6 (✅) — CE-4201…CE-4204; SV-2 / SV-3 rode S0 with CE-4200
  - ../../DESIGN_Scenario_Macros.md — §1 S3a (the slice), §9.y SR-146…SR-151 (the expander = the creation path's child branch), §10 relocation SR-152…SR-157, SR-80 / SR-148 (staged on every node, any node expands)
  - ../../DESIGN_Scenario_Rules.md — §9.8 (the types, S0's as-built §9.8.11) and §00 (the programme's build spec)
  - ../Blueprint_Issues_Tracker.md — CE-4153, CE-4201…CE-4204 (the rows are part of the spec)
-->
# HANDOFF — coordinator → backend: **after S0 — S9′ · node features S1 · semantic types · macros core** *(overnight, autonomous)*

**Dispatched at** the commit that adds this line (`coordinator`); ⛔ **scope frozen there** — a document that changes after it is FYI; one that
invalidates an item ⇒ STOP that item, report it, carry on (`R-106`).

🔒 **User, `2026-11-01`:** *"I would like to give it a next queued batch to do once it finishes s0 … the stuff you listed for backend, ending with
the macros … It should be an overnight autonomous run."*

## Start

1. ⭐ Finish S0 first — its report and its as-built in `DESIGN_Scenario_Rules.md` §9.8.11 — and push it.
2. Merge `coordinator` (it carries the designs below; ⚠ it also carries the 2026-11-01 cleanup of `DESIGN_Scenario_Rules.md` — §9.8 there was
   left untouched for you, so expect conflicts only around it; keep your §9.8.11 and the cleaned text around it). Push an empty
   `chore: started after-S0 batch at <sha>`.
3. ⭐ `dotnet restore` each test project you will run once after the merge (the third stale-binary trap in CLAUDE.md).

## The items, in this order *(each a separate commit series; a stop leaves a green tree)*

| # | item | design basis — read it, it is the spec | done when |
|---|---|---|---|
| **1** | ⭐ **CE-4153 S9′** — references in a file carry their GUID | `DESIGN_Scenario_Entity_Keys.md` S9′: (a) `RoutePlan.Waypoints[].HoldUnit` (`Hrot.Core/Components/Map/RoutePlan.cs:47-49`, a raw `long` that IS a reference) → `EntityLink` · (b) a code-written saved reference goes through a GUID-keyed translator (they all do — keep it so) · (c) a RAIL fails on an `EntityRef` / `long` id in an auto-serialized saved component · (d) the content migration writes every numeric `EntityLink` in a file as the target's GUID (`Exact`) — extend S0's C# migration tool, ⛔ no Python · (e) the map / ORBAT pick into an `EntityLink` writes `Exact` (the picked entity's `EntityGuid`) — today it writes a bare link (S0 item 5, `ComponentEditDrawer`) · then **S8**: drop `NetworkIdentity` from the scenario file | no scenario / recipe file names a network id; a round trip writes GUIDs; the rails red-proved; the CE-4153 GUID rails + the scenario load suites green |
| **2** | ⭐ **node features S1** | `DESIGN_Node_Features.md` §4 F-13…F-20 and **§6** (the C# shapes, the home in `SharedApplicationBootstrapper` after the capability walk, `IFeatureGate` in `Fdp.ModuleHost`, the `[NodeFeature]` gate in `SystemScheduler` beside `[TickMode]`, the wire structs, `FeaturesAtLoad` in the load payload and in `RecordingMetadata`, the branch-to-live copy in `RecordingModule.StartBranch`) · migrate **`map3d.in-load`** (`MapInLoad` / `MapInLoadNodes` DELETED) · ⛔ there is NO `scenario.rules` feature (F-20) | §5 S1's done-when: the 3-D map in the load works exactly as today (the R-1046 / CE-3354 ③ rails re-pointed, green); an override on a named node applies at the next load; an override for a node without the feature is refused; a `[NodeFeature]` system is skipped while off; a branch to live re-applies the recorded set; an undeclared id throws at registration; the catalogue rail prints §6.1 |
| **3** | **the semantic types** — CE-4201 `SimInstant` / `SimDuration` (and WIDEN the 29 `float` instants), CE-4202 `Angle` (relative, radians), CE-4203 `StationId`, CE-4204 `ExerciseId` / `EpisodeId` | `DESIGN_Semantic_Value_Types.md` §2 + §3 (the shared pattern: identical memory + JSON except where a width is fixed on purpose, natural implicit conversions, ⛔ `instant + instant` does not compile, the TYPE drives the tools, one rail per type) | each type's rail red-proved; recordings / wire changes stated in the report (the `float` instants widen) |
| **4** | ⭐ **scenario macros S3a — the core** | `DESIGN_Scenario_Macros.md` **§1 S3a** (the scope) · **§9.y** SR-146…SR-151 — the `Expander` IS `CreateEntityRequestSystem`'s child branch (`Hrot.Common/Systems/CreateEntityRequestSystem.cs:296-575`) with a second SOURCE, ⛔ never a second spawning loop · **§10** SR-152…SR-157 relocation — S0's `Relocation` applier does the typed values; add the two relocators (`EditablePolyline` turns only, `SimVelocity.Linear` turns only) and clear `NavState`'s road / trajectory ids · SR-148 / SR-80: ANY node that processes the creation request expands; the orchestrator stages the macros package on EVERY node · ⛔ NOT in this batch: `InsertMacro`, run variants, `SetSkillAmplifier`, the director (S3b — they need the trigger runtime) | S3a's done-when (§1): the same platoon macro placed twice, rotated, expands and plays on a cluster; nesting composes; a missing macro / wrong binding / cycle fails the load naming the path, nothing created; an args edit keeps ids and outside references; a pinned macro placed elsewhere is refused; ⚠ the open rail first: do dismounted soldiers settle on the ground after a Z change (SR-157)? |

⭐ **Design detail is yours** (frame delegation): where a design leaves a choice, decide it, write it INTO the owning design as an as-built
sub-section (with a small diagram if a box changes), and mark anything you deviate from SUPERSEDED there — not only in the report.

⭐ **Sonnet** for the mechanical retypes (item 3, item 1's migration run, item 2's `MapInLoad` re-pointing) — one warm worker, reused, handed the
file:line classification you measured. ⭐ **Your own model** for the gate, the converters, the expander, the validator, the reconcile, and every
review.

⚠ **Cross-lane:** items 2 and 4 touch files every lane uses (the scheduler, the bootstrapper, the creation path) — name every other lane's file in
the commit messages and the report.

## Gates *(CLAUDE.md Rule 8 — every row, per item)*

| gate | must report |
|---|---|
| ⭐ **T-1, the features' own suites FIRST** | item 1: the CE-4153 GUID rails, the scenario load / save suites · item 2: the R-1046 / CE-3354 ③ map-in-load rails, the `SystemScheduler` TickMode rails, the recording / branch rails (`RecordingModule`, `LiveBranchAggregator`) · item 3: the clock / time rails, the turret / weapon rails touching angles · item 4: the CE-4153 derived-GUID rails, the TKB child-blueprint creation rails, `ScenarioLoadStep` rails, S0's `S0WalkerTests.Relocate_*` |
| each touched TEST project's suite, `--no-build` after ONE build of the test project | pass / fail / skip + delta vs baseline |
| ⭐ **the integration suites holding the cross-node invariant** | `ClusterRunner.Integration.Tests` `--filter`ed to load / spawn / replication / ownership / time-sync (the full batch hangs — CE-4158, pre-existing; state the base sha) · `Hrot.SimHost.Integration.Tests` · `Hrot.IG.Tests` |
| ⭐ **a live run per item 2 and 4** | over HTTP (`docs/RUNBOOK_Cluster_Debugging_Over_Http.md`): a feature override reaching a named node; a scenario with two placed macros expanding on a cluster — the entities seen on a Player node |
| every red | confirmed pre-existing against the base sha, named |
| golden movement | as a diff shape |
| `tracker-counts.py --check` · `design-digest.py --check` · `rulings-check.py` · `mermaid-check.mjs` on every touched design | ✅ / ❌ |
| the working tree after every suite run | clean |
| ids you allocated | from the `backend` block |

## Done when

All four items' done-when hold, the designs carry the as-built, and the report has the table above per item — or a stopped item is named with
its reason and the rest are done.

_coordinator lane, 2026-11-01_
