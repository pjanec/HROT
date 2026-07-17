# HANDOFF — Hill-attack → Blueprints migration (session bootstrap)

> **Read this first, in full.** It is the single source of truth for resuming the blueprint-migration
> effort in a fresh session. It persists the mission, conventions, capability/gap map, the remaining
> task plan, and the exact build/verify commands. Task-tool state does NOT carry across sessions — the
> task plan below IS the persisted backlog.

**Repo:** `/home/user/IOS-IG-SimHost-FDP` · **Branch:** `claude/hill-attack-blueprints-p1b-uj8cbb`
· **HEAD == main ==** `50ff6e4` + this doc commit (keep them in lockstep; fast-forward `main` after each commit).
> Note: an earlier session used branch `claude/hill-attack-json-slice-3-stages-0nsrpp`; the active branch
> is now `claude/hill-attack-blueprints-p1b-uj8cbb` (same lockstep-with-`main` discipline).

---

## 1. Mission (the north star)

Enable **non-programmers to author complex AI behaviors — up to the full Platoon Hill-attack — entirely
in visual blueprints.** The vehicle is a **rebuild** (not a port) of the C# Hill-attack behavior as
`.bp.json` blueprint graphs, hosted back into the existing BTrees. The C# original stays untouched as the
**oracle** (ground truth we test against). Each missing capability is filled by building a **generic,
hardcoded C# blueprint node** (+ static helpers/structs that are "public API"), architect-endorsed — NOT
by hand-porting one-off logic. Working name: `HillAssault2` (tank) / `HillAssault2`-prefixed assets.

**Oracle files (do not modify):** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackTankNodes.cs`,
`HillAttackCommanderNodes.cs`, `HillAttackDtos.cs`; topology in `Assets/BTrees/PlatoonHillAttack.btree.json`.

---

## 2. Hard conventions (non-negotiable)

| Rule | Detail |
|---|---|
| **Branch** | Develop/commit/push ONLY to `claude/hill-attack-blueprints-p1b-uj8cbb`. **`main` is SHARED with parallel sessions** — push commits to the feature branch; do **NOT** ff-merge/push `main` unless the user explicitly asks (a parallel session may have advanced it; reconcile by `git rebase origin/main` on the feature branch when asked). |
| **No PR** | Never open a pull request unless explicitly asked. |
| **Commit trailers** | End every commit body with:<br>`Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`<br>`Claude-Session: https://claude.ai/code/session_015yR8zetnZ4ryyZPBN1oaVE` |
| **No model id in artifacts** | Never put the model identifier in commits, code, PRs, or pushed files. Chat only. |
| **Questions** | Plain chat prose only — NEVER the AskUserQuestion widget (per project CLAUDE.md). |
| **Diagrams** | Hand-authored SVG for anything non-trivial; Mermaid only for simple flowcharts, short box labels. |
| **Docs** | Keep prose short; lead with tables/visuals. |
| **Review discipline** | When a subagent implements, **review the ACTUAL diff** (never trust the summary), **re-run all gates yourself**, then commit. |
| **Opus vs Sonnet** | Opus = orchestrator + hard-reviewer + **novel core-compiler/scheduler work**. Sonnet = implement **mirror-an-existing-pattern** slices. **Lesson learned:** a Sonnet agent stalled ~7h on the novel FlowForEach scheduler surgery — do that class of work hands-on. |
| **Codebase Memory MCP** | Project CLAUDE.md mandates `mcp_codebase-memo_*` tools first, but they are NOT registered in this environment — use Read/Grep/Glob directly. |

---

## 3. The reflection-free constraint (the deepest architectural fact)

The blueprint **source generator runs as a netstandard2.0 Roslyn analyzer** that **cannot load game
assemblies** (`Hrot.AI.Behaviors.dll`, `Fdp.Toolkits.dll`, …). So it can NEVER reflect a game type at
generation time. Every capability is built to **bake FQN strings / decisions into the `.bp.json` at author
time** and emit `global::{FQN}` text; the downstream real C# compiler validates it. Precedents you MUST
follow when adding nodes:
- `StaticTypeRegistry.TryResolve` accepts any `global::`-prefixed TypeId as an unmanaged value type (AN2 "trust the FQN").
- P7.1 bakes `FunctionCallNode.TrailingContext`; P2/P4/P1 bake component/event/accessor FQNs.
- `IrTypeRef` is a pure string record (`FullName`/`IsUnmanaged`/`SizeBytes`) — build it from baked strings.

If you ever find yourself wanting `Type.GetType`/AppDomain scan in a compiler stage → STOP; that was GAP-9, and it silently drops args producing CS7036 on the real build.

---

## 4. Compiler pipeline (where things live)

`.bp.json` → **Stage0_Rehydrate** (pins; `NodeRequiresExecFallback`) → **Stage2_Validate** (BP#### rules;
`Validators` list) → **Stage4_TypeResolve** (`StaticTypeRegistry`) → **Stage5_Schedule** (BFS basic-block
scheduler; IR lowering; `EmitNodeStatements` exec switch + `ResolveNodeOutput` pure-node switch) →
**StatementEmitter** (Roslyn C# text emit) → **IrOperation.cs** (IR op records).

All under `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/`. Node kinds:
`Assets/Nodes.cs` (`[JsonDerivedType]` discriminators + classes). Node pin skeletons:
`Compiler/Catalogs/BuiltInNodeRegistry.cs`. Catalogs: `BuiltInEngineEventCatalog.cs`,
`BuiltInChannelCommandCatalog`, `CatalogInterfaces.cs`.

**Dispatch:** the migration blueprints are **AiPrimitive** (BTree-hosted Conditions/Actions). Generated
`TickCore(ref Params p, ref WorkingState ws, Entity self, EntityRepository world, float time)`. Note:
**no `ecb`** in the AiPrimitive ABI (deliberate — structural mutations aren't allowed at this tier).
`WorldVar` = `world` (AiPrimitive) / `((EntityRepository)view)` (Instance).

### The "add a generic node" recipe (mirror pattern — how P2/P4/GAP-11 succeeded fast)
1. `Nodes.cs`: `[JsonDerivedType(typeof(XNode),"X")]` + class with baked-string props.
2. `BuiltInNodeRegistry.GetStaticPins`: pure-data node → `Array.Empty<PinSchema>()`; exec node → `new[]{ExecIn(),ExecOut()}` (or named exec-outs).
3. `Stage0_Rehydrate.NodeRequiresExecFallback`: `false` for pure-data, `true` for exec.
4. `Stage5_Schedule`: pure node → `case XNode` in the `ResolveNodeOutput` switch (mirror `GetSharedNode`); exec node → `case XNode` in `EmitNodeStatements` (mirror `ChannelCommandNode`). Reuse existing IR ops where possible.
5. `IrOperation.cs` + `StatementEmitter.cs`: only if a NEW emit shape is needed.
6. Proof: a `HillAssault2_*` `.bp.json` under `Hrot/Subsystems/Hrot.AI.Behaviors/Assets/Blueprints/` + a `*_ProofTests.cs` reflecting the REAL generated `Hrot.AI.Behaviors.Generated.*_Bp` type.
7. `NodeCoverageTests.cs`: add a `Build*MinimalAsset()` + register in `CoverageAssets()` (FullRoslynPipeline if game-assembly-free; else `ValidateOnlyStage1To7`, like PublishEvent/FlowForEach).

---

## 5. Capability / gap map (status)

| Capability | Node(s) | Gap | Status | Commit |
|---|---|---|---|---|
| Context-aware FunctionCall (baked self/view) | `FunctionCall` + `TrailingContext` | GAP-9 | ✅ | `f8e7089`/`4702817` |
| ECS component read (self + cross-entity) | `GetComponent` | GAP-7 reads, GAP-2 | ✅ | `178d0ae` |
| Read declared Parameters | `GetParameter` → `IrOp_ReadParam` | GAP-11 | ✅ | `96e1a90` |
| Publish engine events | `PublishEvent` → `world.Bus.Publish` | GAP-3 | ✅ | `a4bd09f` |
| **Bounded loop (inline `for`)** | `FlowForEach` + `UnitRosterOps` | **GAP-1** | ✅ **P1a** | `9e785eb` |
| **Loop body with in-body `if`** | scheduler inline-if (`IrOp_If`) | **P1b** | ✅ **P1b** | `50ff6e4` |
| **Loop index + count outs** | `FlowForEach` `CurrentIndex`/`Count` data-outs (`IrOp_ForEach.IndexVar`/`CountVar`) | round-out | ✅ | `de2986cc` |
| **Roster AND-reduce condition** | `Condition_AreAllAtBaseline` (slice 4) | GAP-1+2 | ✅ **slice 4** | `50ff6e4` |
| **Native comparison node** | `Compare` (`IrOp_Compare`, full 6-op set) | **GAP-12** | ✅ **GAP-12** | `d763dab` |
| **Native arithmetic node** | `BinaryOp` (`IrOp_BinaryOp`, `+ - * / %`) | round-out | ✅ | `5481e7d` |
| **Native boolean-logic nodes** | `BooleanOp` (And/Or) + `Not` | round-out | ✅ | `7c84b01` |
| Singleton read | `GetSingleton` | GAP-7 singleton | ⏳ low-value | — |
| Editor node drawers | (When/WaitForChannel/etc.) | GAP-8 | ⏳ Windows track | — |

**Gaps still open:**
- ~~**GAP-1 (P1b):** in-body `Branch` → inline `if/else`.~~ ✅ **Done** (`50ff6e4`): `IrOp_If` +
  `Stage5.ScheduleInlineBodyChain`/`FindInlineBranchJoin`; BP2050 relaxed to allow Branch (latent still
  rejected). Slice 4 `Condition_AreAllAtBaseline` shipped on it. Join detection = nearest common
  successor of the two arms (null when an arm ends → self-contained arms, the slice-4 shape).
- **GAP-12 (next):** no comparison/bool/arithmetic pure node exists (`ComparisonOperator` lives only inside `WhenNode`). Until built, conditions use a tiny pure C# comparator helper (`HillAssault2NavOps.IsArrived`). Build a native `Compare`/`BinaryOp` node, then retire the helpers.
- **GAP-10:** `ISimulationView` has no singleton read; AiPrimitive helpers downcast `world` to `EntityRepository`.
- **Unlowered/broken nodes** (safety net confirmed): squad primitives (`PartitionElements`/`AssignRoles`/`AdvancePhase`/`AcquireSlot`), `CallEventDispatcher`/`BindEventDispatcher`, `ArrayMake`/`ArrayGet` (silent-default bug), `WaitForEvent` (CS0400 vs BP1402 — no valid EventTypeId). Decide per-slice: implement lowering or route around.

---

## 6. Remaining task plan (the persisted backlog — do in order)

**Immediate migration path:**
1. ~~**P1b — inline-`if` loop body.**~~ ✅ **Done** (`50ff6e4`). `IrOp_If` + `ScheduleInlineBodyChain`
   /`FindInlineBranchJoin` in Stage5; BP2050 relaxed. Reusable for any in-body branch, incl. joins
   (`if(x){A}else{B}C`) via nearest-common-successor detection.
2. ~~**Slice 4 — `Condition_AreAllAtBaseline`.**~~ ✅ **Done** (`50ff6e4`).
   `HillAssault2_AreAllAtBaseline.bp.json` + proof (source-inspection + behavioral TickCore) vs oracle.
   Emits `for {…; if(IsArrived){}else{ws.AllAtBaseline=false;}}` then post-loop Branch→Return. Confirmed
   WorkingState fields ARE emitted (`[MarshalAs(I1)] bool`); first asset to use a named WorkingState var.
3. ~~**GAP-12 — native `Compare` node.**~~ ✅ **Done** (`d763dab`). `CompareNode` + `IrOp_Compare`
   (mirror-pattern; design in `GAP12_Compare_Node_Design.md`). `IsSelfArrived` + slice 4 retrofitted
   helper-free; `HillAssault2NavOps.cs` deleted. Both conditions are now the true non-programmer endpoint.
4. **Remaining action slices.** **Architect Q#6 answered (2026-07-17) — all four leans APPROVED**
   (`Architect_Question_6_Access_Shapes_And_Vocabulary.md`). Decided shapes, now build-ready in order:
   - ~~**`Action_ReverseToBaseline`**~~ ✅ **Done** (`3d333fa`, slice 2). `ChannelCommand`(Locomotion/MoveTo) + `WaitForChannel` + `PublishEvent(ClearBehaviorEvent)` + new curated `VectorOps.Vec3` helper (Destination from `GetParameter(BaselineX/Y)`). First WIRED-`ChannelCommand`-param slice (explicit `MoveToParams`-named pins; scalars via `PinDefaults`→Stage3 literals). Deviation: Clear published on Success only. Behavioral test drives a real `LocomotionChannel` to Success.
   - **Target-resolve = curated context-aware `FunctionCall`** to `NetworkEntityMapOps.TryGetEntity` (Q6-B) — **NO generic `GetSingleton` node** (wrong abstraction; the need is a method call w/ `out`), keep the `EntityRepository` downcast, do NOT touch `ISimulationView`. Unblocks **`AimAndFireSpecific`** (Weapon ChannelCommand + P2 ammo + GetParameter round-count + this helper).
   - ~~**`AssignTacticalIntentEvent.JsonParams` = curated `FunctionCall` JSON-builder helper feeding `PublishEvent`'s string field**~~ ✅ **Done** (Q6-C; `MoveIntentJson.Build`, no new IR). Consumed by the two slices below. Reused by the wave core later.
   - ~~**EQS request/poll (`RequestAreaQuery` + `IsAreaQueryResolved`)**~~ ✅ **Done** (`b6f2ed97`; architect Q7 all leans approved, `Architect_Question_7_EQS_Slice.md`). Curated `AreaQueryBatchOps` (Request/IsReady/TargetCount/TargetGroupHandle/Free) + `WorldOps.SimTime` over `AreaQueryBatchHelper`. **First `Return(Running)` stateless poll** (no `__phase`; host re-ticks from top — proven across ticks). Both `Intent=Action` (a Condition's bool wrapper would collapse `Running`). **Surfaced + fixed a compiler bug:** impure/exec `FunctionCall` to a CLR helper mis-lowered to `IrOp_LibraryCall(0)` (→ `__LibBp_00000000`), and the per-block CSE cache dropped statement values across blocks — fixed with `IrOp_PureCall` lowering + a never-cleared `_statementPinCache` + void bare-statement emit.
   - **Wave core last** (`DispatchWaveWithTargets` + `IsWaveCompleted`, L): new `MemberSlotList` SoA + verb nodes; design blessed in Q#3 / `Squad_State_Fit_And_Lean_Slot_Design.md`; watch the `[InlineArray]` `ldobj` defensive-copy hazard (`GetSpanRW()`).
   - **Q6-A:** arithmetic `BinaryOp` is **demand-driven** — build it only when a slice needs math (e.g. `AimAndFire` round-count / `CalculateSegments`); boolean composition stays `Branch`/helper. Detail below:
   - `Action_ReverseToBaseline` — MoveTo `ChannelCommand` + terminal `ClearBehaviorEvent` (P4).
   - ~~`Action_AimAndFireSpecific`~~ ✅ **Done** (slice 3, branch `c7925177`). Weapon `ChannelCommand` + ammo read (P2) + WorkingState round-count (`Compare`/`BinaryOp`/`BooleanOp`) + curated `NetworkEntityMapOps.ResolveTarget`/`WorldOps` target-resolve. **Surfaced + fixed two compiler bugs** (WS field-shift `7c06315`; convergent-Branch merge blocks `c4541305`).
   - ~~`Action_CalculateSegments`~~ ✅ **Done** (`171edebf`). Pure setup: `GetParameter`×5 → curated `SegmentMath.TotalSlots` `FunctionCall` → `SetVariable(TotalSlots)` + 8 `Literal`/`SetVariable` zeroing a 9-field commander WorkingState. Surfaced the `Literal.ValueJson` verbatim-splice gotcha (narrow/wide numerics need `"(ushort)0"`/`"(byte)0"`/`"-1L"`).
   - ~~`Action_DispatchAllToBaseline`~~ ✅ **Done** (`f892926f`). First real consumer of the FlowForEach `CurrentIndex`/`Count` outs: in-body `Branch(WorldOps.IsAlive)` (one branch subsumes both oracle `continue`s), per-tank `SegmentMath.LerpParam`/`Lerp` → `MoveIntentJson.Build` → **managed** `PublishEvent(AssignTacticalIntentEvent)` (`world.Bus.PublishManaged`, `Target`→`Entity` field, string `IntentId`/`JsonParams` pins) → `MaskOps.WithBitSet` mask accum. New curated helpers: `SegmentMath.LerpParam`/`Lerp`, `MoveIntentJson.Build`, `MaskOps.WithBitSet`.
   - `Action_DispatchWaveWithTargets` + `Condition_IsWaveCompleted` — the hard core (slot alloc, wave parity, SoA/bitmask working-state — see `Squad_State_Fit_And_Lean_Slot_Design.md`, GAP-5).

**Parallel / deferred tracks:**
- **P3 `GetSingleton`** — narrow value (Hill-attack singleton `NetworkEntityMap` needs a method call anyway); needs a new `IrOp` + emit off `world`. Only if a slice needs a field-read singleton.
- **Editor authoring UI (GAP-8, Windows-verifiable)** — wire existing components per `Blueprint_Editor_Components_Reuse.md` (StructEdit-universal; `NodePinSchema`/`ReflectDataMembers` for pin reification). Drawer priority: `When` → `CallCustomEvent`/`CallPeerBlueprint` → `WaitForChannel`/`ReadRankedResult`; then drawers for the new P2/P4/P1 nodes.
- **Visual conceptual docs** (task #17/DOC-2 follow-ups): D2 memory-layout SVG, D3 lifetime timeline for the variable/scope/GetShared/params-vs-workingstate model ("beyond an ordinary user without visuals"). Terminology chosen: Parameter / private scratch / behavior shared / **Squad shared**.
- **UX-1 / UX-2:** intent-first "what is this memory?" picker; unify the two shared-state doors (Get/SetShared vs entity-scope). Architect nod needed.
- **P5 lean slot path** — `SlotRotation`/`SlotRotationState` (exists) + new `MemberSlotList` `[BlackboardDtoStruct]` SoA; hazard: C#12 `[InlineArray]` defensive-copy `ldobj` bug → expose `GetSpanRW()`. For the wave core.
- **Unlowered/broken node cleanup** (GAP-5 area, `ArrayMake/Get` silent-default, `WaitForEvent` FQN resolution).

---

## 7. Key design docs (read as needed)

| Doc | What |
|---|---|
| `HillAssault_Blueprint_Migration.md` | **The migration log** — slice ladder, GAP table, capability-status, findings per slice. Update it as you go. |
| `P1_FlowForEach_Design.md` | Loop design + P1a/P1b split + the reduce pattern + open questions. |
| `Architect_Question_5_Next_Capability_Tier.md` | The 4 architect answers (events=`world.Bus`, ChannelCommand-only writes, inline-`for` loop, close GAP-11). |
| `Blueprint_Generic_Primitives_Design.md` | The P1–P7 primitive family + approvals. |
| `Blueprint_Feature_Maturity_Matrix.md` | 30-node × Compiler/Authoring/Tests audit (what's real vs designed-ahead). |
| `Blueprint_Editor_Components_Reuse.md` | Architect map for building node drawers by REUSE (Windows track). |
| `Squad_State_Fit_And_Lean_Slot_Design.md` | Lean slot path + `[InlineArray]` hazard (wave core). |
| `Blueprint_New_Node_Authoring_Guide.md` | Per-node authoring/exposure mechanism. |

---

## 8. Verify (run these gates for every change)

```bash
# Real generator build (0 errors required — this is the netstandard2.0-analyzer path):
dotnet build Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj -t:Rebuild -c Debug

# Migration proof + regression (adjust filter to the slice under test + all HillAssault2_*):
dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/ --filter "FullyQualifiedName~HillAssault2_"

# Safety net (per-node coverage + schema round-trip + validator rules):
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/ --filter "FullyQualifiedName~NodeCoverage|FullyQualifiedName~SchemaReflection|FullyQualifiedName~Validator"
```
Inspect generated C# under `Hrot/Subsystems/Hrot.AI.Behaviors/obj/GeneratedFiles/Hrot.Blueprints.Generators/.../HillAssault2*_Bp.g.cs` to confirm the emit. Current green baseline on branch `claude/hill-attack-blueprints-p1b-uj8cbb` @ `b6f2ed97` (P1b + slices 4/2/3 + FlowForEach `CurrentIndex`/`Count` outs + `CalculateSegments` + `DispatchAllToBaseline` + **EQS `RequestAreaQuery`/`IsAreaQueryResolved`** + `Compare`/`BinaryOp`/`BooleanOp`/`Not` + 3 scheduler/lowering fixes): real build 0 err / 0 warn; `HillAssault2_*` **43/43**; full Blueprints.Tests **2042 pass / 10 skip**; safety-net subset (NodeCoverage|SchemaReflection|Validator) **167 pass / 1 pre-existing skip** (`WaitForEventNode_...BUG`). Native operator vocabulary complete: `Compare` (6 comparisons) + `BinaryOp` (`+ - * / %`) + `BooleanOp` (And/Or) + `Not`. Curated helpers: `UnitRosterOps`, `VectorOps`, `NetworkEntityMapOps`, `WorldOps` (`IsAlive`/`IsNull`/`SimTime`), `HillAssault2TankOps`, `SegmentMath` (`TotalSlots`/`LerpParam`/`Lerp`), `MoveIntentJson`, `MaskOps`, `AreaQueryBatchOps` (`Request`/`IsReady`/`TargetCount`/`TargetGroupHandle`/`Free`). **Compiler fixes:** WorkingState-field-shift (`7c06315`), convergent-control-flow merge blocks (`c4541305`, general — regression test `Inline/DiamondMerge`). **FlowForEach index/count** (`de2986cc`): `CurrentIndex` (body-scoped loop var copy) + `Count` (hoisted outer-scope bound) opt-in data-outs; unwired ⇒ goldens byte-identical; proof `FlowForEach_IndexAndCount_EmitsHoistedCountAndBodyIndexCopy` + `Inline/FlowForEachIndexCount` coverage. **Slices done this session: `CalculateSegments` (`171edebf`), `DispatchAllToBaseline` (`f892926f`), EQS `RequestAreaQuery`+`IsAreaQueryResolved` (`b6f2ed97`, + impure-FunctionCall compiler fix + `Return(Running)` poll proven).** **Next:** the **wave core** — `Action_DispatchWaveWithTargets` (`HillAttackCommanderNodes.cs:287+`) + `Condition_IsWaveCompleted` (GAP-5, the hard slot-alloc/wave-parity/SoA-bitmask slice; see `Squad_State_Fit_And_Lean_Slot_Design.md`). Likely needs an architect pass (new slot-list vocabulary). **Follow-up:** a game-free compiler-unit fixture for the impure-FunctionCall lowering (currently guarded only by the EQS integration proofs). **Known papercut (future):** `Literal.ValueJson` splices verbatim — narrow/wide numeric + string literals need hand-authored casts (`"(ushort)0"`, `"-1L"`, `"\"str\""`); a type-aware `Literal` emit would remove the footgun. ⚠ **Main is shared with parallel sessions — commit/push to the feature branch only; do NOT ff-push `main` without an explicit request.** (Env note: `dotnet` isn't preinstalled — `sudo apt-get install -y dotnet-sdk-8.0` after `apt-get update`.)

---

## 9. Workflow reminders

- Use a **Sonnet subagent** for mirror-pattern slices (agent implements+builds+tests+reports the diff; does NOT commit). **Opus reviews the real diff, re-runs gates, commits.** Do **novel scheduler/IR work hands-on**.
- Keep the migration log (`HillAssault_Blueprint_Migration.md`) and this handoff current.
- Commit doc/status changes separately from code; don't `git add -A` while a subagent is mid-write (stages its partial work).
- If a subagent goes silent for a long time, check `git status` + file mtimes; if the core files are untouched it has stalled — kill and take over.
