<!--STATUS
state: LIVE — DRAFT FOR THE BACKEND LANE (not dispatched; the user relays it)
updated: 2026-10-01
current-answer: the whole file — a FRAME handoff (goal, measured state, fences, decisions with leans, acceptance). The backend
  session designs the detail (inventory, UML, seams) in docs/ as step 1.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/eqs-2/EQS_Design_v1.3_final.md — owns EQS 1.3 and AreaQuery's footprint (§16.3, updated 2026-10-01: no
    behaviour caller left) and the parity matrix (§17.5) whose OLD half goes with the pipeline.
  - docs/blueprints/DESIGN_Hill_Attack_Eqs_Migration.md — the behaviours-lane migration that emptied AreaQuery of callers
    (CE-478); §6 lists what the live run could and could not observe (the CE-476 gap).
  - docs/blueprints/batches/REPORT_EQS_Unification.md — the backend lane's own report this continues (items ④/⑤ were held back
    by the user's ruling; ⑤ is now done by the behaviours lane).
  - tools/ai-debug-mcp/SKILL.md — the debug API surface item B restores on the cluster (groups K, O).
-->

# HANDOFF — retire AreaQuery · make the AI debug API work on a cluster (`CE-476`)

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). **Status:** draft — ⛔ not dispatched.
Stamp `Dispatched at <sha>` when it is; from then the scope is frozen at that sha.
🔒 **User, `2026-10-01`:** *"Hand it to the backend lane, together with ce 476."* · *"The system needs to be reliable."*

## A. Retire the AreaQuery pipeline

### A.1 Measured state *(`2026-10-01`, behaviours head `796f49ae9` — re-verify before building)*

| | |
|---|---|
| behaviour callers | ✅ **none.** `HillAttackCommanderNodes` and `PlatoonHillAttackBp` ask the `EntitiesOfForceInArea` EQS sensor (`CE-478`); `AreaQueryBatchOps`/`TargetPoolOps` are deleted. `grep AreaQueryBatchHelper` over `Hrot/Subsystems/Hrot.AI.Behaviors` ⇒ 0 |
| what is left | EQS §16.3: `AreaQueryEvents`, `AreaQueryBatchData` (+`EqsTargetPool`), `AreaQueryBatchHelper`, `AreaQuerySolverSystem`, `AreaQueryInitializationSystem`, `AreaQueryResultMaterializationSystem`, 4 translators + 2 DDS messages (**wire contract**, `AllDescriptors.cs`), 2 ImGui singleton renderers, registrations in SimHost / Stride / editor / `StrideNodeBootstrapper` |
| ⚠ one shared function | `EntitiesInAreaGenerator` calls `AreaQuerySolverSystem.PointInPolygon` (parity by construction, §17.1) ⇒ **move it, do not delete it** |
| its rails | `AreaQueryBatchDataTests`, `AreaQueryTranslatorTests`, the AreaQuery half of `EqsModuleTests` and of `EqsDistributedTests` (T-DIS4…10), `HillAttackIntegrationTests.SC_HA015_6` *(a behaviours-lane file: ✅ you may delete that ONE test — it tests the old solver, not the doctrine)* |

### A.2 Decisions — each with a lean

| | question | lean | what would change it |
|---|---|---|---|
| **A-D1** | the parity rails compare old vs new — what survives? | ⭐ **keep every scenario as an EQS-only rail with the expected network-id set written down.** The scenarios (concave L, edge, area moves, force flip, 3 sensors…) are the value; the old half was only the oracle | — |
| **A-D2** | the two DDS messages | **remove them in the same change** and say so in the report — a wire-contract removal is not silent | a deployed peer still on the old contract |
| **A-D3** | `EqsTargetPool` | goes with AreaQuery unless an EQS reader exists (measure) | an EQS reader |
| **A-D4** | the old pipeline's 0..1000 m perception-grid blind spot | gone with it; ⚠ the perception GRID itself stays (perception uses it) | — |

### A.3 Acceptance

① `grep -rn "AreaQuery" --include=*.cs` outside history comments ⇒ only `PointInPolygon`'s new home and docs · ② every former
parity scenario green as an EQS rail · ③ `HillAttack*` (SimHost) green · ④ live `ClusterRunner --mode all`, both
`hill-attack-close` and `hill-attack-close-bp`: hostiles `Health 0`, platoon back on the baseline.

## B. `CE-476` — the step-by-step AI debug surface does not work on a cluster

### B.1 Measured *(`2026-09-30`/`2026-10-01`, live `--mode all`, the `Scenario` perspective)*

| route | answer | cause *(code)* |
|---|---|---|
| `POST /trace/observe` | `{"armed":false,"note":"Trace coordinator not available."}` | `Hrot.ClusterRunner/Program.cs:463` builds the cluster API with the **lifted** `DebugApiService` ctor (`DebugApiService.cs:607`), which takes no tracer and no BTree / HSM / Blueprint debug session; ⇒ `DebugApiService.cs:2724` refuses |
| `GET /entities/{id}/trace` | `{"tier":"unknown"}` for BOTH the C# BTree and the blueprint commander | same cause, falls through at `:2820`; ⚠ and the blueprint arm is a stub everywhere — *"assetId resolution not available via Debug API"* (`:2810-2817`); a Behavior-dispatch blueprint (`BrainTier 3`, `CE-446`) has no arm at all |
| `GET /entities/{id}/variables` | `400 "No blueprint debug session is available in this editor."` | same cause (`DebugApiService.Variables.cs:118`) |
| ⭐ what already exists | **CGF composes the sessions**: `CgfSubsystem.cs:1981` `AiDebugSessionComposer.Compose(_debugTimeController)` → `_btreeDebugSession`, `_hsmDebugSession` (+ the Blueprint session it already drives); the editor does the same at `EditorSubsystem.cs:1225` | ⇒ **a held dependency not passed** — the silent-default pattern (`CLAUDE.md`), not a missing capability |

⇒ an A/B of two commanders can only compare observable state (orders, health, positions), never which node ran or what a
blueprint variable holds. 🔒 *"The system needs to be reliable"* ⇒ this is the instrument for proving it.

### B.2 Decisions — each with a lean

| | question | lean | what would change it |
|---|---|---|---|
| **B-D1** | how does the cluster API get the sessions? | ⭐ **per perspective, as a `Func` from the subsystem that owns them** — exactly the `behaviorRegistry` getter pattern beside it (`Program.cs:458-468`, CE-169's boot-order reason); CGF exposes its composed sessions + tracer | ⛔ not a second composition in the runner (two sessions for one world) |
| **B-D2** | the blueprint trace | **one arm for Instance AND Behavior dispatch** reporting what the Blueprint debug session already captures (active graph/node, live variables) — no stub | — |
| **B-D3** | the tracer | the per-perspective tracer CGF uses for its runtime-inspector pane (`CE-351`); arm/disarm must address the perspective's world | — |

### B.3 Acceptance

On `ClusterRunner --mode all`, `Scenario` perspective: ① `observe_trace` ⇒ `armed:true` · ② `get_entity_trace` on the C#
`PlatoonHillAttack` commander ⇒ `tier:"BTree"` with an active node that changes as the waves run · ③ on
`PlatoonHillAttackBp` ⇒ a blueprint trace naming the active graph · ④ `GET /entities/1000/variables` on the blueprint commander
⇒ its variables, including `Phase` and `Sensor` · ⑤ the same calls on the editor host still answer (no regression).

## C. Fences

| | |
|---|---|
| ✅ yours | the AreaQuery pipeline, its translators/DDS messages/registrations/rails, `PointInPolygon`'s new home, `Hrot.ClusterRunner`, `Hrot.Editor/DebugApi`, the CGF wiring of sessions |
| ⚠ cross-lane — ask first | anything in `Hrot.AI.Behaviors`, the blueprint compiler, `HillAttack*` tests (except deleting `SC_HA015_6`) |
| ⛔ STOP paths | `Fdp.Core`, `Hrot.SystemTests`, `Hrot.IG.Tests` |

**Ids:** none allocated here — `CE-476` exists; number anything new yourself (plain incrementing `CE-` ids).
