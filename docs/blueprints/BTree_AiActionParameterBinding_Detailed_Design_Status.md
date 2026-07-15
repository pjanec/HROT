# BTree AI Action/Condition Parameter Binding — Implementation Status (blueprint-authored actions/conditions)

> **Status:** status snapshot (2026-07-13). Measures the *current codebase* against the target design in `BTree_AiActionParameterBinding_Detailed_Design.md`, specifically §3.2's claim that "**Blueprints (AiPrimitives) are the primary authoring source of actions/conditions**." Verdicts are code-grounded (file:line) on branch `claude/hill-attack-json-slice-3-7fbaf4`.
> **Scope:** what is missing — in **infrastructure** (compiler/codegen/runtime) and in the **visual editor** — for a user to define a BTree/HSM action or condition as a blueprint (`AiPrimitive` dispatch), place it as a node, and bind its parameters. Does not restate the target design (that is the parent doc) or the resolver design (`Behavior_Parameter_Resolver_Detailed_Design.md`).
> **Audience:** engineers scoping the blueprint-authored-action slice; reviewers.
> **Verdict legend:** IMPLEMENTED (built + reachable) · PARTIAL (built but degraded/unreachable) · DESIGNED-ONLY (spec exists, no code) · MISSING.
> **Related canonical docs:** `BTree_AiActionParameterBinding_Detailed_Design.md` (the target, esp. §3.2 composition model + §4.4 scoped working state), `Behavior_Parameter_Resolver_Detailed_Design.md` (§8.3 shares the adapter rail this needs), `Blackboard_Authoring_Addendum_v3_ActionParamAuthoring.md` (whole-DTO binding / Promote), `BTree_HSM_JSON_Persistence_Detailed_Design.md` (the `[BlueprintRegistrar]` masquerade registrar, D14).
> **Companion code:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/` (`AiPrimitiveEmitter.cs`, `CSharpEmitter.cs`), `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/` (`BTreeBridgeEmitCore.cs`, `HsmBridgeEmitCore.cs`), `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs`, `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs`, `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/ActionSchemaExporter.cs`, `Hrot/Subsystems/AI/Hrot.BTree.Editor/`, `Hrot/Subsystems/AI/Hrot.Hsm.Editor/`. Tracked debt: `.dev/btree-ai-action-binding/DEBT-TRACKER.md` (`DEBT-AIB-025`, `-005`, `-009`).

> **⚡ Update (2026-07-15, branch `claude/hill-attack-json-slice-3-7fbaf4`):** The three §1 TL;DR blockers below are now **RESOLVED end-to-end and Windows-verified** — individual verdicts in §3/§4 predate this note.
> - **I1** — AiPrimitive thunks now register into the FastBTree string-keyed `ActionRegistry` the interpreter reads (not the orphaned `BehaviorRegistry` side-table, which was removed).
> - **I2/I3** — emit the §3.2 **bin-packed-offset Params + partition-slot WorkingState** form (via `BTreeBridgeEmitCore.EmitBlueprintActionThunks` over the S2-1/S3-G slot rail), replacing the Slice-1 `paramIndex*sizeof` / fixed-`Blackboard1024` legacy.
> - **I4** — generated `TickCore` carries `[Fbt.Kernel.GeneratedAiPrimitiveAction]`; `ActionSchemaExporter` surfaces it (`ActionSchemaEntry.IsAiPrimitive`), `BehaviorActionCatalog` tags `Source=AiPrimitive`, and `BTreeNodeCatalog` lists it under a **"Blueprint"** palette category labelled by blueprint name. (Supersedes the `DEBT-AIB-025` deferral.)
> - **E2** — placing a palette blueprint action produces a valid composed node (`DelegateShape=AiPrimitiveTickCore`, `WorkingStateTypeId`, auto-created `bpParams`), structurally equal to the hand-authored `T31`.
> - **Cross-generator sizing (architect Option A)** — the BTree generator cannot resolve the Blueprint generator's `{Blueprint}_{id:X8}_Bp+Params` type in the same compilation, so it sizes the composed Params from the blueprint's own `.bp.json` schema via the existing Sequential bin-pack math (`GeneratedBlueprintSchemaCatalog`). Params stay **inline** (role=Input), not slot-keyed. Bool params carry `[MarshalAs(UnmanagedType.I1)]` so the predicted (bin-pack) size matches the reflected `Marshal.SizeOf` (AAR integrity); a predicted-vs-reflected test guards it.
> - **Verified:** `Hrot.AI.Behaviors` builds with composed real-blueprint trees (`T32`=EnumDemo, `T33`=ParamDemo with `int`+2×`bool` params); a user authored → hot-reloaded (no CS1503) → full-rebuilt a composed EnumDemo tree in the Windows editor (no more `BTreeBuilder<, >` / "no root node" / no-op).
> - **Remaining:** the fuller predicted-vs-reflected safeguard as a **generator-emitted runtime/build assertion** (beyond the current test guard); **E1/E3** param-*value* authoring UX (no blueprint declares editor-authored params yet — `ParamDemo` is a hand-authored fixture); **E4–E6 / G7 / HSM (E5 + the ID contract)**.

> **⚡ Update (2026-07-15 later, branch `claude/hill-attack-json-slice-3-stages-0nsrpp` → merged to `main`):** the "Remaining" items above are now largely closed; the composed-blueprint-action path (place → author param values → build) is **Windows-verified end-to-end**.
> - **Layout-drift guard (fuller safeguard) — DONE.** `BTreeBridgeEmitCore.EmitBlueprintActionThunks` now emits a registration-time hard check `Marshal.SizeOf<{Blueprint}_{id:X8}_Bp.Params>() != <predicted>` → `throw InvalidOperationException`, so a predicted-vs-reflected mismatch fails startup loud instead of silently corrupting the AAR schema (architect-mandated). Zero-param blueprints are exempt (a fieldless struct reflects as 1 byte vs predicted 0, and has no fields whose offsets could drift). Confirmed present in the real editor-generated `NewBTree1.Registrar.g.cs`.
> - **Managed-blackboard papercut — FIXED.** Placing a composed AiPrimitive node (`BTreeCommandSink.ComposeAiPrimitiveAction`) now auto-enables `IsBlackboardEditorManaged` — the binding hard-requires `Managed=true` (else `BTREE0002`), so the first Full Rebuild of a fresh tree now succeeds with 0 warnings instead of failing and forcing a manual "use editor managed blackboard" step.
> - **E1/E3 param-*value* authoring — ALREADY COMPLETE (not new work); now test-locked + Windows-verified.** The reflectable Params struct schema (`ActionSchemaEntry.DtoType`), the StructEdit "Static Parameters" panel (`DefaultValueAuthoring`, rendered inline in the *node* inspector for the bound variable), `BlackboardVariableEntry.DefaultValueJson` persistence, and the generic `EmitParseParamsLocal` bake all pre-existed and compose over the composed Params with **no new code**. New regression `ParseParamsEmissionTests.ComposedAiPrimitiveNode_WithAuthoredDefault_BakesParamsIntoParseParams` proves a composed node's authored default bakes into `ParseParams` at offset 0 alongside the guard + thunk. Windows-verified: authoring `FlagB=true` → `.btree.json` `DefaultValueJson` → generated `ParseParams` deserializing `{"Threshold":0,"FlagA":false,"FlagB":true}` + `Unsafe.Write(memory + 0, __v)`.
> - **Still remaining:** **E4** (UI to choose `Intent=Condition` + a Condition recipe); **E6** cross-asset picker; **E1** authoring `Dispatch`/`Intent`/`Hostings` without recipe-clone; **G7**; **HSM (E5 + the ID contract)** as its own track.

> **⚡ Update (2026-07-15 — "finish the BTree loop", branch `claude/hill-attack-json-slice-3-stages-0nsrpp` → `main`):** the BTree authoring loop for blueprint actions **and conditions** is now closed. Headlessly built + tested; commits on `main` (`0f756f8`→`2461bd1`).
> - **Composed CONDITIONs — DONE (was the E4 codegen gap).** A blueprint AiPrimitive condition composes as a host-BTree condition node identically to an action: `BTreeConditionPayload.WorkingStateTypeId`, `BTreeCommandSink.ComposeAiPrimitiveCondition`, `BTreeBridgeEmitCore.EmitBlueprintConditionThunks` (+`AppendReusableStatefulConditionThunk`, registers via `RegisterCondition`, dispatches `TickCore(...) == NodeStatus.Success`), and `BTreeEmitCore.EmitCondition`'s `AiPrimitiveTickCore` blob branch + fail-loud guard. Architect-confirmed: a composed condition gets the **same partition-slot WorkingState** as an action (edge-detection/hysteresis need cross-tick memory), not a transient/zeroed state. Proof: `T34_ComposedAiPrimitiveCondition` (real interpreter, Failure→Failure→Success with slot persistence) + emission/round-trip tests.
> - **E1 Condition recipe — DONE.** `GateConditionDemo` (`Recipes/Blueprints/` cloneable + `Assets/Blueprints/` compiled instance): `Dispatch=AiPrimitive`, `Intent=Condition`, `Hostings=[BTreeCondition]`, synchronous `EventEntry→Return(Success)` (BP1100/BP1101-clean). Generates `[GeneratedAiPrimitiveAction(bTreeCondition:true)]` + `BTreeEvaluate` + self-`RegisterCondition`, so it surfaces in the palette as a Blueprint condition. Per architect, a recipe IS the E1 creation flow (Dispatch/Intent are immutable post-creation by design); the from-scratch wizard is optional polish, not built.
> - **Reference integrity (Phase C) — DONE.** BTree→Blueprint refs are tracked by **FQN string** (architect rule; no persisted AssetId, no schema bump): `IComposedBlueprintIdentity` (shared layer, dependency-inverted so it stays Roslyn-free) exposes each blueprint's precomputed `{San}_{id:X8}_Bp`; `ComposedBlueprintResolver` matches node `MethodFqn`→asset by pure string; `BlueprintReferenceContributor` + `BTreeComposedBlueprintReferenceContributor` publish the element + references; `RefactorService` delete-refusal (ActionFqn/ConditionFqn = Critical) now blocks deleting a referenced blueprint; `BTreeValidator` (opt-in `IAssetCatalog`) flags a dangling composed reference (`DanglingReferenceAfterReload`). Graceful load on a missing blueprint already existed.
> - **Navigation (Phase D) — DONE.** `BTreeNodeContextMenuProvider` "Open Blueprint" resolves a composed node → blueprint asset → `AiDocumentManager.Open` (switches perspective). ImGui menu rendering unchanged/unverified by unit tests.
> - **Still remaining:** an **Asset Browser delete affordance** for blueprints (delete-refusal *logic* is done+tested; no delete *button* exists for any asset kind — a separate UI surface); **E6** cross-asset picker; **E1** from-scratch AiPrimitive wizard (recipe covers the need); **G7**; **shared-state beyond one WorkingState struct** (a `GetShared<T>` runtime accessor + a blueprint graph node — neither exists; design queued); **HSM (E5 + the ID contract)** as its own track.

---

## Table of Contents
1. TL;DR
2. What already exists (do not rebuild)
3. Infrastructure gaps
4. Visual-editor gaps
5. Critical path, dependencies, and reuse
6. Tracked debt & the skipped flagship demo
7. Cross-references

---

## 1. TL;DR

A surprising amount is already emitted, but the feature is unreachable end-to-end for three compounding reasons:

1. **The registration wire is cut.** AiPrimitive action/condition thunks are registered into a `BehaviorRegistry` side-table that the FastBTree interpreter never reads (it reads a different, string-keyed `ActionRegistry`). This is the single highest-leverage fix.
2. **The emitted thunks are Slice-1 legacy** (`paramIndex*sizeof`, fixed `Blackboard1024` offset-8 working state) — not the §3.2 bin-packed-offset + partition-slot form, and with no stateful-slot support for blueprints at all.
3. **The editor can't discover them** (generated thunks carry no discovery attributes), and authoring an AiPrimitive's dispatch/intent/hostings is recipe-clone-only. The palette surfacing was **deliberately deferred** (`DEBT-AIB-025`).

Net: no blueprint-authored action has ever executed in a real BTree tick — the flagship `MoveToAndFire` runtime tests are `[Skip]`'d with a documented "7 interacting bugs" list. §3.2 is a sound design that has **not** been wired up.

## 2. What already exists (do not rebuild)

- **`TickCore` matches the target.** `AiPrimitiveEmitter.EmitClass` emits, per asset, `Params`/`WorkingState` structs and `TickCore(ref Params p, ref WorkingState ws, Entity self, EntityRepository world, float time)` — exactly §3.2's signature (`Hrot.Blueprints.Compiler/.../Emit/AiPrimitiveEmitter.cs:105-124`).
- **All four host thunks are emitted:** `BTreeTick(ref BrainBlackboard, ref BehaviorTreeState, ref BTreeContext, int paramIndex)`, `BTreeEvaluate(...)→bool`, `HsmActivity(void*, void*, HsmCommandWriter*)`, `HsmGuard(void*, void*, ushort)→bool`, plus `Call(...)` for intra-blueprint use (`AiPrimitiveEmitter.cs:126-301`).
- **Action-vs-condition is a real, validated concept:** `AiPrimitiveIntent.Action`/`Condition` with hosting-compatibility validation (`Stage2_Validate.cs` `V_DispatchKindCompatibility`, errors `BP1022`/`BP1023`).
- **Params can be authored in the editor:** `BlueprintVariablesWindow` in `_isParams` mode reads/writes `_asset.Parameters` → the generated `Params` struct (`Hrot.Blueprints.Editor/Variables/BlueprintVariablesWindow.cs:44-55`).
- **The BTree node catalog is already dynamic** and would list blueprint actions if they were discoverable — `BTreeNodeCatalog.BuildDynamicEntries` adds an entry per `ActionHosting.BTree` schema item (`Hrot.BTree.Editor/Host/BTreeNodeCatalog.cs:81-115`).
- **The HSM runtime dispatch table is live:** `HsmActionDispatcher` is called directly by `HsmKernelCore` (`EvaluateGuard`/`ExecuteAction`), and AiPrimitive HSM thunks register into it by `BlueprintId` (`CSharpEmitter.cs:227-230`). (BTree has no equivalent live path — see I1.)
- **The bin-packed-offset + partition-slot machinery is fully built and tested** — but only for hardcoded `MethodFqn` methods: `BTreeBridgeEmitCore.EmitStatefulActionThunks` / `EmitStatefulWorkingSlotsArray` (`:593-775`), used by S3-G (`PlatoonHillAttack`/`HillAttackMutableState`). This is the rail the blueprint path must join, not reinvent.

## 3. Infrastructure gaps

### I1 — The registration bridge (the blocker) · MISSING
The FastBTree runtime resolves every node's action via `ActionRegistry<BrainBlackboard,BTreeContext>.TryGetAction(name)` — a **string-keyed** registry (`"{MethodFqn}@{offset}[@{slotKey}]"`), bound in `Interpreter.BindActions` (`FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs:691-715`). The AiPrimitive registrar instead emits `behReg.RegisterAction(BlueprintId, name, BTreeTick)` / `RegisterCondition(...)` into `BehaviorRegistry._bTreeActions`/`_bTreeConditions` — an **int-keyed** dictionary (`CSharpEmitter.cs:207-231`; `BehaviorRegistry.cs:170-171,279-301`) that has exactly one reader in the repo: a unit test asserting it got populated (`QuickReloadServiceTests.cs:130`). Nothing drives a tick through it.
**Fix:** emit registration into the `ActionRegistry` under the interpreter's string-key scheme.
**Reuse:** `BTreeBridgeEmitCore.EmitManagedActionThunks`/`EmitStatefulActionThunks` registration pattern (`actionRegistry.Register("key", (ref bb, ref st, ref ctx, pi) => …)`) — same `NodeLogicDelegate<BrainBlackboard,BTreeContext>` shape.

### I2 — The per-node "provides TickCore" adapter · DESIGNED-ONLY
No emission anywhere wraps an AiPrimitive as a BTree node calling its `TickCore` (repo-wide `TickCore` grep: zero hits in `Hrot.AiEditor.Persistence`). The generator has the projection logic — `EmitStatefulActionThunks` already projects Params at a bin-packed offset and WorkingState from a `BlueprintBlackboardPartitions` slot — but only for hardcoded `MethodFqn` methods.
**Fix:** add a "MethodFqn = the generated `{BlueprintClass}.TickCore`" case to the adapter emitter.

### I3 — Bin-packed / partition-slot form + blueprint stateful slots · MISSING
`AiPrimitiveEmitter`'s `BTreeTick` uses the Slice-1 legacy model: `paramIndex*sizeof` addressing into `bb.BehaviorParameters` and a **fixed** `Blackboard1024` offset-8 working-state slot with a `StructureHash` guard (`AiPrimitiveEmitter.cs:158-192`) — the exact mechanism the target calls the "one-stateful-primitive-per-entity" cap. No reference to `BlueprintBlackboardPartitions`/`StatefulWorkingSlots`/`ThreeParamReusableStateful` exists in the blueprint compiler.
**Fix:** move blueprint actions onto the S2-1/S3-G partition-slot rail. **Reuse:** `BTreeBridgeEmitCore.cs:593-775` slot codegen + `ResolveStatefulSlotKey`/`ComputeStatefulSlotKey` (`:180-295`) — the slot-key algorithm and `BlueprintBlackboardPartitions` API are dispatch-kind-agnostic.

### I4 — Editor discovery hook · MISSING
`ActionSchemaExporter.Rebuild` discovers actions/conditions purely by reflecting `[BTreeAction]`/`[BTreeCondition]`/`[HsmAction]`/`[HsmGuard]`/`[SharedAi*]` attributes (`Hrot.Editor.AiShared/Blackboard/ActionSchemaExporter.cs:32-183`). The generated `BTreeTick`/`BTreeEvaluate`/`HsmActivity`/`HsmGuard` methods carry **none** of these, so no blueprint action can appear in any editor catalog or typed-binding lookup.
**Fix:** emit the discovery attributes on the generated thunks, or feed a parallel AiPrimitive catalog. Note `BehaviorActionSource.AiPrimitive` already exists as an enum value but is never assigned (`BehaviorActionCatalog.cs:168` labels everything `Hardcoded`).

### Conditions · same gaps, no extra hole
Conditions are emitted symmetrically (`BTreeEvaluate`/`HsmGuard` returning `bool`) and inherit I1/I4 identically. The action/condition distinction is already modeled; nothing is *more* missing for conditions.

### HSM · closer, but needs an ID contract · DESIGNED-ONLY/MISSING
Unlike BTree, the HSM runtime table (`HsmActionDispatcher`) is real and consulted by `HsmKernelCore` (`:604`, `:763`), and the AiPrimitive registers into it by `BlueprintId`. But `HsmBridgeEmitCore` assigns its **own** sequential small-int IDs (`actionId++`/`guardId++`) to hardcoded `[HsmAction]`/`[HsmGuard]` methods (`:107-142`); nothing routes an `.hsm.json` node to a blueprint's `BlueprintId`, and there is no guard against the two ID spaces colliding.
**Fix:** an explicit ID-allocation contract (reserve a `BlueprintId` sub-range, or resolve the `.hsm.json` action reference through a `BlueprintId` lookup instead of the counter). Working state on HSM is the same fixed-slot legacy as BTree (I3 applies).

## 4. Visual-editor gaps

### E1 — Author the AiPrimitive (dispatch/intent/hostings) · PARTIAL
`Dispatch=AiPrimitive` + `Primitive.Intent` + `Hostings` can only be obtained by **cloning a recipe** — New-Asset hardcodes `Dispatch = Instance` (`BlueprintNewAssetService.cs:96`); `NewFromRecipeService.CreateFromRecipe` JSON-clones verbatim (`:23-31`). Two Action recipes exist (`Recipes/Blueprints/LocomotionMoveToDemo.bp.json`, `SampleWiredDemo.bp.json`); **no Condition recipe.** Dispatch is read-only post-creation (`InspectorWindow.cs:66`); no UI edits `Intent`/`Hostings`. Params authoring works (see §2).
**Fix:** a "New AiPrimitive" flow (or an editable dispatch/intent/hostings inspector) + a Condition recipe.

### E2 — Appear in the BTree/HSM node palette · MISSING (blocked by I4; tracked `DEBT-AIB-025`)
`BTreeNodeCatalog` is dynamic and ready, but is fed only by `ActionSchemaExporter`, which can't see attribute-less blueprint thunks (I4). Explicitly deferred: `DEBT-AIB-025` — "the BTree-node→blueprint-`TickCore` composition … does not exist and was deliberately NOT built."

### E3 — Place + bind params (typed filter / Promote) · IMPLEMENTED (2026-07-15; was PARTIAL, blocked by I4)
Now that I4 lands the exporter entry (`ActionSchemaEntry.IsAiPrimitive` + `DtoType`), the existing generic binding UI works for composed blueprint actions: placement auto-creates the typed `bpParams` variable and binds `ExpressionTargetField`, and the node inspector's **"Static Parameters"** panel (`DefaultValueAuthoring` StructEdit over `DtoType`) authors the Params field values inline. Authored values persist as `BlackboardVariableEntry.DefaultValueJson` and are baked into the generated `ParseParams` at the Params' baked offset (`EmitParseParamsLocal`). Windows-verified end-to-end (place → set `FlagB=true` → Full Rebuild → `ParseParams` deserializes the authored JSON + `Unsafe.Write(memory + 0, …)`); test-locked by `ParseParamsEmissionTests.ComposedAiPrimitiveNode_WithAuthoredDefault_BakesParamsIntoParseParams`. (The historical "unfiltered list / Promote no-op" degradation below was the pre-I4 state.)

### E4 — Author a condition · PARTIAL (compiler only)
Compiler-supported (`AiPrimitiveIntent.Condition`, `EmitBTreeConditionThunk`/`EmitHsmGuardThunk`), but no UI affordance to choose `Intent=Condition` and no example recipe (E1).

### E5 — HSM action/guard binding is weaker still (independent of blueprints) · MISSING
`HsmActionPickerAttribute`'s doc-comment claims it's "populated from `HsmActionDispatcher.AllActions`," but that enumeration API **does not exist** on `HsmActionDispatcher` (a `ushort→IntPtr` table). The real picker scavenges names already typed elsewhere in the same asset and offers no free-text entry (`HsmPickerDrawers.cs:59-116`), and `HsmDocumentFactory.Build` is called without an `actionSchema` at all (`EditorSubsystem.cs:2836-2839`). So there is no discoverable way to bind even a *new hardcoded* action name in HSM, blueprint or not.
**Fix:** a real action-name source for the HSM picker (prerequisite before blueprint HSM actions are usable).

### E6 — Cross-asset "pick a blueprint action" UX · MISSING
No picker to reference a blueprint asset from a BTree/HSM node. The nearest analog, the Subtree node, is a bare free-text name field (`BTreeFacets.cs:158-159`). The only asset-reference picker of this family, `BlueprintPeerSource`, is Blueprint→Blueprint peer-call only.
**Fix:** a cross-asset action/condition picker (or rely on the name-based selection of E3 once I4 lands, treating the blueprint action like any other registered action name).

## 5. Critical path, dependencies, and reuse

- **Critical path:** **I1** (registry wire) → **I4** (discovery attributes) → **E2/E3** (palette + typed binding). Nothing in the editor works until I1 and I4 land. I1 is the single highest-leverage fix — without it, even a perfectly-authored blueprint action cannot execute.
- **I2 + I3 share the resolver's adapter rail.** They reuse `EmitStatefulActionThunks` and the "behavior owns layout, blueprint provides core" pattern that `Behavior_Parameter_Resolver_Detailed_Design.md` §8.3 also depends on. Landing S3-G first built that rail; the blueprint-action work and the resolver work amortize one adapter-emission investment.
- **Suggested slice order:** (1) I1 + I4 with a hardcoded-thunk smoke test → un-skip a minimal `MoveToAndFire` tick; (2) I3 (partition slots) to lift the one-stateful-primitive cap and support stateful blueprint actions; (3) I2 to route through `TickCore` on the bin-packed path; (4) E1/E2/E3/E4 editor ergonomics; (5) HSM (E5 + the ID contract) as its own track.

## 6. Tracked debt & the skipped flagship demo

This is a self-acknowledged, half-built slice, not greenfield:
- `.dev/btree-ai-action-binding/DEBT-TRACKER.md`: `DEBT-AIB-025` (BTree-node→blueprint composition deliberately not built), `DEBT-AIB-005` (blueprint-authored AiPrimitive demo is a follow-up), `DEBT-AIB-009`.
- `Hrot.Blueprints.Tests/Compiler/EndToEnd/MoveToAndFire_EndToEndTests.cs:99-135`: runtime tick tests `[Fact(Skip=…)]` with a documented "7 interacting bugs" list (catalog FQN resolution, JSON pin traversal, enum mismatches, invalid emitted C#). Compile-time structural tests (that `TickCore`/`BTreeTick` text appears) pass, but there is no passing runtime proof.

## 7. Cross-references

- **Measures against** `BTree_AiActionParameterBinding_Detailed_Design.md` §3.2 (composition target) and §4.4 (scoped working state).
- **Shares infrastructure with** `Behavior_Parameter_Resolver_Detailed_Design.md` §8 (same adapter rail, same `ActionRegistry` registration seam, same world-singleton considerations).
- **Depends on prior art in** `BTree_HSM_JSON_Persistence_Detailed_Design.md` (D14 masquerade registrar) and `Blackboard_Authoring_Addendum_v3_ActionParamAuthoring.md` (whole-DTO binding / Promote — the UI that E3 reuses once I4 lands).
