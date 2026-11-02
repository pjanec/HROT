<!--STATUS
state: LIVE — coordinator → backend: scenario macros, slice S3a (the core)
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Macros.md — THE spec: §1 (S3a row), §5.1 (bindings), §6–§10, ⭐ §14 (critical review, all accepted) — §14 amends earlier rows; where they disagree §14 wins
  - ../../DESIGN_Scenario_Entity_Keys.md — S9′ / S8 BUILT: GUID references, no network ids in files; SR-167 derived GUIDs build on it
  - ../../DESIGN_Scenario_Rules.md — §9.8.11 the walker + RefSpec (S0 as-built); the rules are built in PARALLEL by the behaviors lane (its handoff below)
  - ../../DESIGN_Add_Entity_Picker.md — D6b SpawnHeight, the intent pattern placement extends (SR-180)
  - HANDOFF_Coordinator_To_Behaviors_Objectives_Triggers.md — the parallel lane; ① below is the shared piece it waits for
-->
# HANDOFF — coordinator → backend: **scenario macros — S3a, the core**

**Dispatched at** `d358a8728` (`coordinator`) · ⛔ scope FROZEN at this commit — a later document that invalidates an item ⇒ STOP that item, report, carry on (R-106).
🔒 **User, `2026-11-02`:** *"ok lets delegate the scenario macros feature to the backend"* · the hold on S3a is lifted.

## Start
Merge `coordinator`; `dotnet restore` each test project once; push `chore: started macros S3a at <sha>`.

## Items, in order
| # | item | spec | done when |
|---|---|---|---|
| **①** ⭐ FIRST, own pushed commit | the SHARED binding piece: `BindableNode` (a typed `Bind` map on a shared base — predicate nodes + `do` entries, SR-160) · `IParamSource` + ONE `ParamBinder.Apply` (SR-164: the `Param` RefSpec form via a `ParamBind` leaf applier on the S0 walker; `Bind` maps; `ParamBindings` paths), NON-DESTRUCTIVE (SR-168) | §5.1, §5.1.2, §14 SR-168 | rails per kind; ⭐ **tell the behaviors lane the sha** (a `NOTE_Backend_To_Behaviors_*` or the report) — their S2 builds on it |
| ② | the macro FILE + id: scenario name `macros/<path>`, file `scenario.json` (one `ContentLayout` resolver arm, SR-173 / §14.1 ①), `header.kind = macro`, `header.params` (SR-44), `header.anchor` (SR-152), `header.placement.maxShift`, `relocatable` | §5, §10, §14.1 | open / load / save / prefetch work by name |
| ③ | `MacroInstance` (immutable record, SR-175; wire twin R-134) · `MacroExpansionStatus` (unmanaged) · `MacroOrigin {Instance, Local}` (SR-170) · the `ScenarioMacro` TKB class + marker row (SR-178) | §14 | types + rails |
| ④ | the EXPANDER = `CreateEntityRequestSystem`'s child branch, second source (SR-146): flatten the whole instance tree from the files (SR-185), bind (①) · resolve · relocate (§10) · derived GUID ALWAYS (`macro:<local>`, SR-167) · children ALWAYS transient (SR-169) · runtime instances transient (SR-171) · only on request processing, never in a translator (SR-174) | §9.y, §10, §14 | the S3a done-when in §1 |
| ⑤ | PLACEMENT as a solver query (SR-180…SR-184): `PlacementRequestBatch` → `PlacementResponseBatch` beside the path pair (`PathfindingTranslators.cs`), answered by `NavigationSolverModule`; one batch per flattened tree (SR-185); avoid existing entities (SR-190); whole expansion fails if any slot fails (SR-182); outside buildings (SR-183); `InitialPlacementIntent` so `ScenarioLoadStep` waits (SR-185) | §14.2, §14.3, §14.4 | a platoon dropped half on water lands dry or fails naming the child |
| ⑥ | the VALIDATOR in the prepare (SR-149), `@Key` resolution with duplicates allowed, first match, warning (SR-188), reconcile by GUID on an args / anchor edit without resurrecting destroyed children (SR-150, SR-172), delete instance ⇒ its entities (SR-170) | §9.y, §14 | rails |
| ⑦ | the macros package staged on EVERY node (SR-80, SR-148) · scenario-level params + `--param` / HTTP, recorded in `RecordingMetadata.ScenarioArgs` (SR-41, SR-176) | §9, §14 | a cluster load with a param |

⛔ NOT in S3a: `InsertMacro` (S3b — after the behaviors lane's S2) · the editor UI (ui lane: CE-4210, CE-4213, SR-189 Preview, SR-191 / SR-192 looks) · the drag ghost (SR-186).

## Gates *(Rule 8)*
Touched test classes per item (red-proved rails) · `DistributedScenarioLoadTests` alone · one live `--mode all` run of a scenario using a macro twice, rotated (§1 done-when) · `tracker-counts.py --check`, `rulings-check.py`, `design-digest.py --check`, `mermaid-check.mjs` on touched designs · fold the as-built into `DESIGN_Scenario_Macros.md`.

_coordinator lane, 2026-11-02_
