# BTree / HSM JSON Persistence — Detailed Design (Thread 1)

> **Status:** Detailed design. Grounded in read-only verification of the `blueprint-integ-1` sources (8 verification passes, all cited inline). Ready for batch breakdown.
> **Audience:** Implementation agents (sonnet) + human reviewer (lead).
> **Drives:** Switching BTree and HSM visual-editor assets from *C#-as-source-of-truth* to *JSON-as-source-of-truth*, mirroring the Blueprint subsystem. Plus unified Save/Save-All, path-at-creation, a unified tree asset browser, and one-time migration.
> **Does NOT cover:** The visual *blackboard authoring* feature set (panel, aggregation, aliasing, sync, bin-packing, validation) — that is the separate **Blackboard Authoring DD** (`.dev/ai-hsm-btree-vis-edit/Blackboard_Authoring_Detailed_Design.md`), implemented as Slice 1.5 and **activated AFTER** this thread, re-based on the JSON substrate this thread lands. This thread only lands the JSON *substrate* for blackboard schema (forward-compatible round-trip).
> **Ordering:** Runs **after** Thread 2 (`.dev/blueprint-finalize/`). Rebase on latest `blueprint-integ-1` first.
> **Scope guardrails:** branch `blueprint-integ-1`. GizmoMap.Contracts stays 0.2.2. No `Hrot.IG` / DDS / `Stride/`. No `editor_stride`.

---

## 1. Mission & the problem being fixed

Today the three visual-editor asset kinds persist inconsistently, and two of them fragilely:

- **Blueprint** → `.bp.json` is the source of truth; editor loads by JSON deserialization; C# is a *generated* artifact. ✅ safe.
- **BTree / HSM** → the generated `.cs` file **is** the source of truth; the editor loads them by **reflecting over the compiled assembly** (invoke the `[BTreeDefinition]`/`[HsmDefinition]` thunk → runtime blob → project to editor model). ❌ fragile.

**The failure mode:** if the emitted C# (or anything else in `Hrot.AI.Behaviors`) doesn't compile — routine when saving incomplete work, or when a referenced hand-written action/guard FQN was renamed/deleted — the assembly won't build, the reflection load can't run, and **the asset can't be reopened. Saved work is effectively lost** until a human fixes C# in an IDE.

**The fix:** make **JSON the universal source of truth and the editor's load path** for all three kinds; demote C# to a regenerated build artifact. Save then always works and is always reopenable, even for broken/incomplete graphs; compile failures become diagnostics, not lost work.

---

## 2. Verified current state (cited — read before changing anything)

### 2.1 BTree/HSM load path = assembly reflection → blob → projector
- `BTreeAssetContributor.LoadFrom(Assembly)` reflects for `[BTreeDefinition]` methods, **invokes** them to obtain a `BehaviorTreeBlob`, then projects ([BTreeAssetContributor.cs:34](Hrot/Subsystems/AI/Hrot.BTree.Editor/Catalog/BTreeAssetContributor.cs#L34)). HSM symmetric ([HsmAssetContributor.cs:27](Hrot/Subsystems/AI/Hrot.Hsm.Editor/Catalog/HsmAssetContributor.cs#L27)).
- Projectors walk the **runtime blob** to build the editor model ([BehaviorTreeAssetProjector.cs:106](Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAssetProjector.cs#L106), [HsmAssetProjector.cs](Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAssetProjector.cs)).
- Assembly-loaded assets get `SourceFilePath = string.Empty` ([BTreeAssetContributor.cs:86](Hrot/Subsystems/AI/Hrot.BTree.Editor/Catalog/BTreeAssetContributor.cs#L86)).
- Visual metadata (positions, pan/zoom, comments, collapse, color) lives in the `[BTreeLayout]`/`[HsmLayout]` method and is applied during projection.

### 2.2 Emitters consume the in-memory editor model
- `BTreeFluentEmitter : IFluentCSharpEmitter<BehaviorTreeAsset>` / `HsmFluentEmitter : IFluentCSharpEmitter<HsmAsset>` deterministically write the whole `.cs`: `CreateBuilder()` (topology) + the `[BTreeDefinition]`/`[HsmDefinition]` thunk + the `[BTreeLayout]`/`[HsmLayout]` method ([BTreeFluentEmitter.cs:441 `EmitBuild`](Hrot/Subsystems/AI/Hrot.BTree.Editor/Emit/BTreeFluentEmitter.cs#L441), [HsmFluentEmitter.cs:319 `EmitCompile`](Hrot/Subsystems/AI/Hrot.Hsm.Editor/Emit/HsmFluentEmitter.cs#L319)).
- Shared base `FluentCSharpEmitterBase` holds the `// HROT_EDITOR_GENERATED …` marker constant and `WriteAtomic` (skips write if byte-identical).
- **Key:** the emitter input is the same model a JSON serializer would serialize. The thunk + topology are emitter-written into the committed `.cs`; the MSBuild generators do **not** produce them (§2.4).
- Determinism asserted by `SaveBTreeEmitTests` / `SaveHsmEmitTests`.

### 2.3 The `// HROT_EDITOR_GENERATED` marker drives ownership
- Marker present → editor-owned, regenerated on save. Marker absent → hand-authored, opened **read-only** (`IsEditorOwned = false`); editor never writes it.

### 2.4 The existing MSBuild generators do glue, NOT topology
In `Fdp.Toolkits.Analyzers` (`netstandard2.0`, `IsRoslynComponent`):
- `BTreeActionGenerator` → `FbtActionRegistrar.g.cs`: dispatch closures binding `[BTreeAction]`/`[BTreeCondition]`/`[SharedAi*]` methods by name; computes **field offsets** by reflecting the DTO `INamedTypeSymbol`.
- `BTreeDefinitionGenerator` → `FbtTreeCatalog.g.cs`: a reflection-lookup *catalog wrapper* over `[BTreeDefinition]` methods. **Reads** the thunk; does not write it.
- `HsmActionGenerator` → `HsmActionDispatcher.g.cs` (kernel) / `HsmActionRegistrar.g.cs` (user): action/guard dispatch + registration.
- **None of them consume or produce the tree/state topology.** Topology + thunk are emitter-written today. → switching to JSON requires a **new topology generator** (see §6); the existing action/guard generators are unaffected and keep serving hand-written methods.

### 2.5 In-process Quick Reload is Blueprint-ONLY *in current code* (the ≤100 ms target for BTree/HSM is documented but unmet)
`QuickReloadService` (in-process Roslyn → PE/PDB → collectible ALC) compiles **blueprints only** ([RegenerationScheduler routing](Hrot/Editor/Hrot.Editor.AiShared/Emit/RegenerationScheduler.cs); `flushAction` routes Blueprint → `_blueprintQuickReloadTrigger`, BTree/HSM → `emitService.Emit` file write). **BTree/HSM today**: write `.cs` → file-watcher → **MSBuild subprocess** → ALC swap (out-of-process; confirmed by `BTree_Editor_NodeEditor_Host_Design.md` §14: "the regeneration scheduler emits the `.cs` file… triggers MSBuild").
- **Important nuance:** the BTree/HSM host docs §1.3 **do** state a "Quick Reload ≤ 100 ms" *target* (matching Blueprint), but it is **aspirational — not implemented today**. So this thread's switch to JSON + MSBuild generator is **latency-neutral vs today** (both out-of-process), *not* a regression — but to actually meet the documented target we must add an in-process quick-reload path for BTree/HSM (§6.5).
- The shared `netstandard2.0` compiler lib pattern that makes this possible is proven by `Hrot.Blueprints.Compiler` (shared by editor `QuickReloadService` + `BlueprintIncrementalGenerator`).
- **Registration discovery (HR-001):** `AiHotReloadCoordinator.ScanForRegistrars` scans **only** `[BlueprintRegistrar]` — *explicitly not* `[FbtRegistrar]`/`[HsmActionRegistrar]` (comment cites the HR-001 constraint). BTree/HSM actions register via the `FbtActionRegistrar`/`FbtTreeCatalog` background `BuildRegistrationAction`, not the coordinator scan. This constrains §6.3 and §6.5.

### 2.6 Blueprint JSON = the reference pattern to mirror
- `BlueprintJsonServices.Serialize/Deserialize` ([BlueprintJsonServices.cs](Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/BlueprintJsonServices.cs)): System.Text.Json, `JsonStringEnumConverter`, `PropertyNameCaseInsensitive`, `AllowTrailingCommas`, `IncludeFields`, `WriteIndented=false`, `$meta` envelope (`docType`, `schemaVersion`) stamped first; unknown-property tolerant.
- DTO uses `[JsonPolymorphic(TypeDiscriminatorPropertyName="kind")]` for node types; `VariableDecl` carries `Id, Name, BlueprintTypeRef Type, DefaultValueJson, …`; `BlueprintTypeRef { TypeId, IsArray, GenericArgs }`.
- **Projection-only stance:** `SaveActiveBlueprintCommand` temporarily swaps each node's `Pins` to empty for serialization and restores in `finally` (pins are render-time projection, rehydrated on load from `Link` records).
- `BlueprintIncrementalGenerator` consumes `.bp.json` as `AdditionalFiles`, deserializes, compiles, emits `Blueprint.g.cs`.
- Discovery: `BlueprintAssetContributor` header-lazy enumerates `*.bp.json` reading only `AssetId`+`Name`.

### 2.7 Storage layout today
Under `Hrot/Subsystems/Hrot.AI.Behaviors/`: `Blueprints/**/*.bp.json` (fed to the generator via `<AdditionalFiles>`), `Trees/*.cs` (BTree, committed generated), `Machines/*.cs` (HSM, committed generated), `Brains/*.cs` (hand-written actions/DTOs). Assembly = `Hrot.AI.Behaviors`. Generated `.g.cs` → `obj/GeneratedFiles`.

### 2.8 Refactor service spans kinds
`IRefactorService` + `AtomicMultiFileWriter` rename FQNs via line-based string replacement on each asset's `SourceFilePath`; already supports Blueprint/BTree/HSM kinds.

### 2.9 Blackboard: feature is BUILT but DORMANT (not dead, not wired)
- The **Blackboard Authoring DD** (`.dev/ai-hsm-btree-vis-edit/`) is design-reviewed; its TASK-TRACKER shows **all 44 tasks `[x]`** — but our verification found **no production callers**, **no `*.Blackboard.cs` on disk**, vars populated **UI-only**, and **no asset adopts it**. Conclusion: components built + unit-tested, **never engine-wired**.
- Both editor models already carry `IsBlackboardEditorManaged` ([BehaviorTreeAsset.cs:205](Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs#L205), [HsmAsset.cs:60](Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs#L60)) and `BlackboardLoadState`; vars are `List<BlackboardVariableEntry>` (record `Name`, `System.Type FieldType`, `Comment` — **not JSON-friendly**, not persisted today).
- Runtime tiers: inline `BrainBlackboard.BehaviorParameters` (ceiling = `MaxBehaviorParamByteSize`; see §8 note) + on-demand `Blackboard1024` heavy component, provisioned via `[BTreeDefinition(HeavyDtoType=…)]`. Real heavy DTOs use `fixed`-buffer SoA arrays ([HillAttackDtos.cs:94 `HillAttackMutableState`](Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackDtos.cs#L94)).
- **Do NOT delete any blackboard scaffolding.** It is reused (logic is persistence-agnostic) when Slice 1.5 is activated on JSON.

---

## 3. Decisions (locked, with rationale)

| # | Decision | Rationale |
|---|---|---|
| D1 | **JSON is the source of truth** for BTree/HSM; C# is a regenerated, **non-committed** artifact (`obj/`). | Eliminates the data-loss failure; matches Blueprint. |
| D2 | **Codegen = Option B**: relocate the emit logic into a `netstandard2.0` library and drive it from a **Roslyn `IncrementalGenerator`** reading `*.btree.json`/`*.hsm.json` as `AdditionalFiles`. | Blueprint parity; no stale-`.cs` sync risk (C# never committed); incremental + IDE-integrated. Emitters are **relocated, not discarded**. |
| D3 | **Model = B2**: a dedicated `netstandard2.0` persisted DTO + editor⇄DTO mapping (not reusing the rich editor model directly). | Editor models carry visual/runtime baggage and live in net8 editor assemblies; a clean DTO shapes JSON deliberately and keeps the generator dependency-light. |
| D4 | **Dual-load retained:** editor-owned → JSON deserialize into editor model; hand-authored (no marker) → existing reflection/blob projector, read-only. | The blob projector is needed anyway for hand-authored assets; JSON load is purely additive and bypasses the blob walk. |
| D5 | **Three fixed roots** `Trees/`, `Machines/`, `Blueprints/`; user-driven subfolders + names under each. Hand-written `.cs` and editor-owned `.json` may coexist in a root; **hard validation forbids a `.cs` and a `.json` sharing a base name** in the same location. | User directive. Extension disambiguates kind; collision guard prevents confusion. |
| D6 | **Path-at-creation** for all three kinds: `<root>/<user subfolder>/<name>.<ext>`. No more `SourceFilePath = ""`. | Onboarding Task 3. |
| D7 | **Rename/refactor works across `.json` and `.cs`.** Editor-owned → JSON-aware mutation; hand-authored → existing `.cs` string-replace. | User directive; extends existing `IRefactorService`. |
| D8 | **Editor-owned files are 100% editor-controlled** — no hand edits, no read-only-passthrough *inside* editor-owned files. Exotic needs are met by **extending the editor** or by **Category-1 composition** (hand-written struct embedded by reference). | User directive. Drops the DD's fragile in-file passthrough mechanism; its own §2.4/§4a.4 composition path is the replacement. |
| D9 | **Blackboard = blittable + fixed-size invariant.** Editor-owned blackboards (and any Category-1 struct embedded in one) contain **only** blittable value types and **fixed-length inline arrays** of them. Type picker/generator reject `System.String`/managed arrays/object refs. **Strings are supported via `Fdp.Core.FixedString32`/`FixedString64`** (unmanaged UTF-8 inline value structs, ≤31/≤63 chars). | The whole `BrainBlackboard`+`Blackboard1024` region must be byte-copyable into **AAR recordings**, replayed, and network-replicated — a managed `string` would break zero-alloc, replay, and replication. |
| D10 | **Defaults are editor-authored** on blackboard vars (mirrors blueprint `DefaultValueJson`); generator emits the initializer applied at ingress. | Avoids setting defaults later in hand code (e.g. `-1` inits). |
| D11 | **Full feature set is preserved; only ordering changes.** JSON substrate first (this thread); then the *complete* Blackboard Authoring DD (Slice 1.5+) is revised for JSON and activated, reusing the built components. | User directive — nothing simplified or dropped. |
| D12 | **Dual-path compilation (mirror Blueprint).** The shared `netstandard2.0` emit core is driven by BOTH an **in-process Roslyn quick-reload** path (editor, ≤100 ms target) AND the **MSBuild IncrementalGenerator** (full rebuild). | The ≤100 ms target is documented for BTree/HSM (host §1.3) but unmet today; MSBuild-only would never meet it. Latency-neutral vs today regardless, so PU-09 may be sequenced as a follow-on without regressing. |
| D14 | **`[BlueprintRegistrar]` masquerade for registration (architect-prescribed; supersedes the HR-001-lift idea).** The JSON generator emits, per editor-owned asset, an isolated class decorated with **`[BlueprintRegistrar]`** (NOT `[FbtRegistrar]`/`[HsmActionRegistrar]`) exposing a coordinator-injectable `Register(BehaviorRegistry, …)` signature. Inside, it bridges to the native registries (compiles/registers the tree/HSM definition; statically calls `HsmActionDispatcher.RegisterAction/RegisterGuard`; registers BTree thunks). | `AiHotReloadCoordinator.ScanForRegistrars` is `[BlueprintRegistrar]`-only and injects only type-erased registries; native `[FbtRegistrar]` classes need generic `ActionRegistry<T,C>` it can't supply (the HR-001 hazard). Masquerading is discovered natively on **both** full rebuild and quick reload — **no HR-001 lift needed**. Also required because `FbtTreeCatalog` (built by the parallel `BTreeDefinitionGenerator`) **cannot see JSON-owned definitions emitted in-memory**, so the JSON generator must self-register them. ✅ **Verified (our tree):** `BehaviorRegistry` exposes a dedicated thunk-bypass — `RegisterAction(int,string,BlueprintBTreeActionDelegate)` / `RegisterCondition(…)` with the delegates at [BehaviorRegistry.cs:24,34,226,233](FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs#L24) (a *different* file from the Thread-2-modified `Blueprints/BlueprintRegistry.cs`). The masquerade registers BTree thunks via these. ⚠ **Still verify at PU-02:** how editor-owned trees are runtime-registered today. |
| D13 | **Post-reload stitching by VisualId/StableId.** After reload, the JSON-loaded editor model is stitched to the recompiled runtime blob by matching `VisualId` (`NodeDebugMetadata.VisualId`) / `StableId` to recompute `KernelBlobIndex`/`FlatIndex`, so the live debug overlay keeps working. | Under JSON SoT the editor model no longer comes from the assembly; the runtime blob still does. (architect catch) |

---

## 4. Target architecture

```
                 ┌──────────────── EDITOR (net8) ────────────────┐
  open asset ──▶ │  marker?                                       │
                 │   editor-owned ─▶ JSON deserialize ─▶ DTO ──┐  │
                 │   hand-authored ─▶ assembly reflect ─▶ blob ─┼─▶ editor model (BehaviorTreeAsset/HsmAsset)
                 │                                              │  │   (read-only if hand-authored)
  save       ◀── │  editor model ─▶ map to DTO ─▶ JSON  ────────┘  │  (only editor-owned saved)
                 └────────────────────────────────────────────────┘
                                   │  *.btree.json / *.hsm.json  (SOURCE OF TRUTH, committed)
                                   ▼
        ┌──────────── BUILD (MSBuild) ────────────┐
        │  AdditionalFiles: *.btree.json/*.hsm.json │
        │  NEW IncrementalGenerator (netstandard2.0)│
        │   deserialize DTO ─▶ emit CreateBuilder() │
        │   + [BTreeDefinition]/[HsmDefinition]      │
        │   + (editor-owned) param/heavy structs     │ ─▶ obj/GeneratedFiles/*.g.cs (NOT committed)
        │  existing FbtActionRegistrar/HsmAction... │
        │   continue serving hand-written actions    │
        └────────────────────────────────────────────┘
```

Emit logic moves into a shared `netstandard2.0` library (the "emit core") consumed by the new generator. The rich editor emitter is refactored to call that core (so editor and build share one emission implementation), or is retired in favor of the generator path for editor-owned assets — decided per §6.3.

---

## 5. JSON schema — `.btree.json` / `.hsm.json`

Mirror Blueprint exactly for serializer settings, `$meta` envelope, polymorphic node discriminator, header-lazy discovery, and the projection-only stance.

### 5.1 Serializer & envelope
Reuse the Blueprint conventions (§2.6): System.Text.Json, `JsonStringEnumConverter`, case-insensitive, trailing commas, `WriteIndented=false`, `$meta` first with new `HrotDocumentTypes` entries `Hrot.BTree` / `Hrot.Hsm`, `schemaVersion: 1`. Positions: decide between Blueprint-style separate `X`/`Y` floats (cross-kind metadata consistency) vs reusing FDP's existing [`Vector2ArrayConverter`](FDP/Engine/Fdp.Core/Serialization/Converters/VectorArrayConverters.cs) `[x,y]`. **Recommendation: Blueprint-style X/Y** for uniform `EditorMetadata` across kinds.

### 5.2 What persists vs what drops (from the verified model inventory)
- **Persist:** identity (`AssetId`, `Name`, `TargetNamespace`, `BlackboardTypeName`, `ContextTypeName`); topology (nodes/pills/states/transitions/regions/global-transitions/events, child links via `VisualId`/`StableId`, action/condition/guard keys, payloads, priorities, transition kinds, sync-group ids); layout (positions, pan/zoom, comments, collapse, color, transition waypoints); **subtree sync bindings**, **alias relationships**, **conflict/unused suppressions** (today smuggled in the `[*Layout]` method — promoted to first-class JSON); blackboard schema block (§5.4).
- **Drop (runtime/projection-only, rehydrated on load):** `Blob`/`Metadata`, `KernelBlobIndex`/`FlatIndex`, derived `*PinId`s, `_syncNodeMeta`, `_aliases` runtime hydration, `LoadDiagnosticMessage`; editor state (`IsDirty`, `Changed`, `IsBreakpoint`).
- **Reconciliation identity:** `VisualId` (BTree nodes/pills, HSM transitions) / `StableId` (HSM states/regions) — same keys the current `OnReloadCompleted` reconciliation uses.

### 5.3 Shape (BTree shown; HSM analogous)
```jsonc
{
  "$meta": { "docType": "Hrot.BTree", "schemaVersion": 1 },
  "AssetId": "…", "Name": "SampleScout",
  "TargetNamespace": "Hrot.AI.Behaviors.Trees",
  "ContextTypeName": "BTreeContext",
  "Blackboard": { /* §5.4 */ },
  "Nodes": [
    { "kind": "Sequence", "VisualId": "…", "ChildVisualIds": ["…","…"],
      "EditorMetadata": { "X": 120, "Y": 40, "Comment": null, "Collapsed": false, "Color": null } },
    { "kind": "Action", "VisualId": "…", "Action": { "MethodFqn": "…", "DelegateShape": "FourParamFull",
      "ExpressionTargetField": "FireTactics" }, "EditorMetadata": { /* … */ } }
  ],
  "Pills": [ /* decorator pills: VisualId, HostNodeVisualId, DecoratorType, Int/FloatParam, StackIndex */ ],
  "SubtreeSyncBindings": { "<subtreeVisualId>": [ { "FieldName":"…","MasterVariableName":"…","SyncIn":true,"SyncOut":false } ] },
  "Canvas": { "PanX": 0, "PanY": 0, "Zoom": 1.0 },
  "Suppressions": { "Conflict": [ { "VariableName":"…","WriterPairKey":"…" } ], "Unused": [ "…" ] }
}
```
Node types use `[JsonPolymorphic(TypeDiscriminatorPropertyName="kind")]`. Action/guard references are FQN/key strings (build-time resolved by the existing registrar generators).

### 5.4 Blackboard block — forward-compatible substrate (round-trip only in this thread)
```jsonc
"Blackboard": {
  "Managed": false,                 // false = Category-1 (reflect hand-written struct, read-only)
  "TypeName": "BrainBlackboard",    // Category-1: the referenced struct; Category-2: the generated struct name
  "HeavyDtoType": null,             // set when Blackboard1024 heavy tier is used
  "Variables": [
    { "Name": "AmmoCount", "Type": { "TypeId": "System.Int32", "IsArray": false, "FixedLength": null },
      "DefaultValueJson": "0", "Comment": "Bullets remaining" },
    { "Name": "ActiveEntityPacked", "Type": { "TypeId": "System.Int64", "IsArray": true, "FixedLength": 8 },
      "Tier": "Heavy", "DefaultValueJson": null, "Comment": "SoA attacker handles" }
  ]
}
```
- **Type-ref is array- and default-capable now** (`IsArray` + `FixedLength` + `DefaultValueJson`), so Slice 1.5's SoA-array + default authoring needs **no format break**. (`System.Type` in the in-memory `BlackboardVariableEntry` maps to/from the string `TypeId` via the existing `BlackboardTypeHelper`.)
- **Blittable + fixed-size invariant (D9):** schema validation (enforced fully in Slice 1.5; advisory here) rejects non-blittable `TypeId`s and requires `FixedLength` for arrays. **Strings → `Fdp.Core.FixedString32`/`FixedString64`** (not `System.String`). The reusable ImGui editors already exist (`FixedString32FieldEditor`/`FixedString64FieldEditor` in `Fdp.Presentation`); **wiring them into the blackboard Variables panel is Slice-1.5 work** (the blackboard folder does not reference them yet). Note the `Fdp.Core.FixedString32` vs `GizmoMap.Contracts.FixedString32` name collision — use `Fdp.Core` and respect the GizmoMap-0.2.2 guardrail.
- **This thread:** round-trip the block faithfully. Existing assets migrate as `Managed:false` (Category-1) — empty `Variables`, `TypeName` = referenced struct. No struct generation, no authoring activation here.

---

## 6. Generator & build wiring

### 6.1 Emit core (`netstandard2.0`)
Extract the deterministic emission logic (currently in the net8 editor emitters) into a `netstandard2.0` library that takes the **persisted DTO** (§5) and produces the C# string for `CreateBuilder()` + the `[BTreeDefinition]`/`[HsmDefinition]` thunk. This is the relocation of the existing, tested emit logic — not a rewrite.

### 6.2 New IncrementalGenerator
Mirror `BlueprintIncrementalGenerator`: `AdditionalTextsProvider.Where(*.btree.json/*.hsm.json)` → deserialize DTO → emit via the emit core → `obj/GeneratedFiles/*.g.cs`. Deserialize failures become reported diagnostics (never a hard build crash for one bad asset).
- **For JSON-owned assets the generated `.cs` needs only `CreateBuilder()` + the thunk — NO `[*Layout]` method** (layout lives in JSON, read by the JSON loader; the blob projector's layout-from-attribute path remains only for hand-authored assets).

### 6.3 The parallel-generator constraint (architect-confirmed)
When editor-owned blackboards generate a param/heavy **struct** (Slice 1.5), the existing `BTreeActionGenerator` computes offsets by reflecting the DTO `INamedTypeSymbol` — but Roslyn incremental generators run in parallel and **cannot see syntax trees emitted by another generator**. **Resolution (option 2a):** the new JSON generator emits the struct **and its own offset-projection thunks** for editor-owned assets, bypassing the global action registrar for those; the existing `BTreeActionGenerator` keeps serving hand-written `[BTreeAction]` methods. This thread does not emit blackboard structs (Slice 1.5 does), but the generator is designed with this seam.
- **Discovery — `[BlueprintRegistrar]` masquerade (architect-prescribed; D14).** Do NOT tag emitted thunks with `[FbtRegistrar]`/`[HsmActionRegistrar]` — the engine has **no dynamic aggregation** of those (the global `FbtActionRegistrar.RegisterAll` is hardcoded-called from `AiBehaviorFactory.BuildRegistrationAction`; HSM registrars are hardcoded too), so a separate `[FbtRegistrar]` class would be **silently ignored**, and `AiHotReloadCoordinator` is `[BlueprintRegistrar]`-only (HR-001). Instead, the JSON generator emits an **isolated class tagged `[BlueprintRegistrar]`** with a `Register(BehaviorRegistry beh, …)` signature; internally it registers the compiled BTree thunks into the injected `BehaviorRegistry` and statically calls `HsmActionDispatcher.RegisterAction/RegisterGuard` for HSM. The coordinator discovers it natively on both full rebuild and quick reload, with no HR-001 change.
- **This applies to definitions, not just blackboard thunks.** Because the parallel `BTreeDefinitionGenerator` can't see JSON-owned definitions emitted in-memory, `FbtTreeCatalog` won't include them — so the same `[BlueprintRegistrar]` bridge must also compile/register the JSON-owned **tree/HSM definition** (blob), per Q3. This pulls the bridge into **PU-02** (the generator), not only the blackboard slice.
- ✅ **Verified (our tree):** BTree thunks register via `BehaviorRegistry.RegisterAction/RegisterCondition` taking `BlueprintBTreeActionDelegate`/`BlueprintBTreeConditionDelegate` ([BehaviorRegistry.cs:24,34,226,233](FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs#L24)) — a dedicated thunk-bypass distinct from the generic `ActionRegistry<T,C>` (which the coordinator can't inject). Not in the Thread-2-modified `BlueprintRegistry.cs`.
- ⚠ **Still verify at PU-02:** how editor-owned trees are runtime-registered today (the sample `Trees/*.cs` may not be wired into `AiBehaviorFactory` at all — consistent with the "no engine wiring" finding); and the HSM-side `HsmActionDispatcher.RegisterAction/RegisterGuard` static-call bridge.

### 6.4 Determinism & test reuse
`SaveBTreeEmitTests`/`SaveHsmEmitTests` move to asserting the **emit core** output (or are re-based). Add: JSON round-trip byte-stability (serialize→deserialize→serialize identical), and a **migration equivalence** test (`json → regenerated .cs` byte-identical to today's committed `.cs`) proving behavior is unchanged.

### 6.5 In-process quick reload (dual-path; D12)
To meet the documented ≤100 ms target (host §1.3) rather than regress to MSBuild-only edit latency, add an in-process quick-reload path for BTree/HSM mirroring Blueprint's `QuickReloadService`: deserialize the in-memory editor model → emit C# via the **shared emit core** → in-memory Roslyn compile → collectible ALC → register → commit. This reuses the exact emit core the MSBuild generator uses (one emission implementation, two hosts), exactly as `Hrot.Blueprints.Compiler` is shared today.
- **Registration:** uses the **`[BlueprintRegistrar]` masquerade (D14)** — no HR-001 change. The same isolated bridge class the generator emits is discovered by `AiHotReloadCoordinator` on quick reload exactly as on full rebuild, and injected with `BehaviorRegistry` to wire the BTree/HSM definitions + thunks.
- **Scope/sequencing:** because JSON+MSBuild-generator is already latency-neutral vs today, PU-09 (this path) may be a **follow-on** if we want to keep the keystone lean — but it is required to *meet* the target, so it's tracked, not dropped.

### 6.6 Post-reload stitching (D13)
After `IAssetCatalog.Changed` / `OnReloadCompleted`, editor-owned assets are now loaded from JSON (topology + layout), while the runtime `BehaviorTreeBlob` / `HsmDefinitionBlob` (carrying `KernelBlobIndex`/`FlatIndex` for the live debug overlay) still comes from the recompiled assembly. Replace today's "re-project everything from the assembly" reconciliation ([AiDocumentManager.ReconcileFromCatalog](Hrot/Editor/Hrot.Editor.AiShared/Documents/AiDocumentManager.cs), wired at EditorSubsystem.cs ~2132) with a **stitch**:
1. Keep the JSON-loaded editor model as the authoritative topology + layout.
2. From the recompiled blob, read each node's `NodeDebugMetadata.VisualId` (BTree) / state `StableId`, transition `VisualId` (HSM).
3. Match blob node ↔ editor node by `VisualId`/`StableId`; assign the blob's `KernelBlobIndex`/`FlatIndex` (and any runtime hashes) onto the editor node.
4. Surface a diagnostic for any editor node with no matching blob node (e.g. compile failed / asset not yet built) — the node stays visible (JSON is the source of truth) but its debug overlay is inert until the blob catches up.
This preserves the `VisualId → KernelBlobIndex` mapping the debug overlay (recent MVE debug-observe work) depends on, without relying on the assembly for topology/layout.

---

## 7. Blackboard handling (this thread's slice)

- **Category 1 (hand-written, read-only):** unchanged — reflect the user's blittable struct via `ActionSchemaExporter` / `[BlackboardDtoStruct]`; surface read-only; editor never writes it.
- **Category 2 (editor-owned):** schema serialized into the asset JSON (§5.4). **In this thread:** round-trip only. **In Slice 1.5 (later):** the JSON generator emits the `TBlackboard` struct (+ heavy struct + `HeavyDtoType` registration) and offset thunks; panel/aggregation/aliasing/sync/bin-packing/validation are activated.
- **Invariants enforced (fully in Slice 1.5):** blittable value types only (strings → `FixedString32/64`); arrays are fixed-length inline buffers — `fixed {prim}[N]` for primitives, `[InlineArray(N)]` for blittable structs (both AAR/replication-safe). Generator must replicate `Sequential` alignment math (natural alignment capped at 8; pad to 8 before a `fixed long[]`) or editor offset calc diverges from the C# compiler → flight-recorder schema corruption.
- **Defaults (architect Q4):** applied inside the generated `ParseParamsDelegate` — instantiate DTO → apply editor-authored defaults → overlay JSON params (overrides win) → `Unsafe.Write`. **Inline tier only** is covered by `BehaviorIngressSystem`. **Heavy-tier defaults** (`Blackboard1024`) need an inline init-check in the execution thunks (verify the 8-byte `StructureHash` header; apply if uninitialized), mirroring Blueprint's `InitDefaultWorkingState`.
- **`[InlineArray]` mutation trap (architect Q5):** generated consuming/accessor code must mutate via `Span<T>` / `MemoryMarshal.CreateSpan` / `Unsafe.As` (or Get-Mutate-Set), never direct index — direct indexing emits `ldobj` (defensive copy) and silently loses the write to the ECS chunk.
- **No blackboard scaffolding is deleted.** The DD revision (separate, post-this-thread) flips Category-2 persistence from `.Blackboard.cs` to JSON, deleting only the fragile source-text-parser/verbatim-span/State-B-C machinery; all logic components survive.

---

## 8. Unified Save / Save-All
- `RegenerationScheduler.FlushNow()` drains pending work without the 500 ms debounce.
- A Save-All command iterates `AiDocumentManager.OpenDocuments`, filters `IsDirty`, dispatches by `Kind` → write JSON, mark clean. Generalize `SaveActiveBlueprintCommand` to any blueprint doc; add BTree/HSM JSON writers (projection-only stance: exclude render-time/runtime fields per §5.2).
- Wire **Save All** (toolbar + `Ctrl+Shift+S`, hooking `CommandCatalog.SaveAll = "editor.save-all"`) and **flush-on-close**. `Ctrl+S` = active-only (recommended).
- C# regeneration stays a separate build/on-demand step.

> **Note — inline param ceiling (resolved):** authoritative value is **`BehaviorConstants.MaxBehaviorParamByteSize = 100`** bytes; `BrainBlackboard` total = `BrainBlackboardByteSize = 128`; the param region is `[FieldOffset(0)] fixed byte BehaviorParameters[100]`, with reserved tail registers `ExpectedThreatLevel` (offset 120), `Interrupt_MobilityLost` (126), `Interrupt_Reserved` (127) and a soft-advice gap at 100–119 ([BehaviorConstants.cs](FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorConstants.cs), [BehaviorComponents.cs:58](FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs#L58)). The `HillAttackDtos.cs` "60-byte" comment is **stale**. The bin-packer must use **100** (Slice 1.5).

## 9. Path-at-creation & roots
Three fixed roots (`Trees/`, `Machines/`, `Blueprints/`) under `Hrot.AI.Behaviors`; user subfolders+names. New-asset flow assigns `SourceFilePath` at creation for all kinds. Add the **base-name collision guard** (D5). Add `<AdditionalFiles>` globs for `Trees/**/*.btree.json` and `Machines/**/*.hsm.json`; ensure generated `.cs` lands in `obj/GeneratedFiles` and committed `Trees/*.cs`/`Machines/*.cs` are migrated out (§11).

## 10. Unified tree asset browser
Replace the flat blueprint-only `AssetBrowserWindow` with a folder tree across all three kinds honoring subfolders, built on NodeEdit's tree widgets (`NodeEditor.UI/Picker/Layouts/TreeLayout.cs`, `MyBlueprintPanel` collapsible sections). Double-click opens via JSON load. Show dirty markers (`*`). Keep `MyBlueprintPanel` for intra-asset outline.

## 11. Migration (`.cs` → `.json`)
One-time pass: for each existing editor-owned `[BTreeDefinition]`/`[HsmDefinition]` asset, load via the current reflection path → project to editor model → map to DTO → serialize to `.btree.json`/`.hsm.json` at the appropriate root/path. **Verify regenerated `.cs` is byte-identical** to the original (proves unchanged behavior), then remove the committed `.cs` (now generated to `obj/`). Existing blackboards migrate as Category-1.

## 12. Refactor/rename across json + cs
Extend `IRefactorService`: editor-owned assets → JSON-aware mutation of FQN references; hand-authored `.cs` → existing string-replace. Point `SourceFilePath` at the `.json` for editor-owned assets. (Blackboard `BlackboardVariable`/`BlackboardField` rename kinds are Slice 1.5, but the JSON-aware writer seam lands here.)

---

## 13. Slice-1.5 handoff / Blackboard DD revision (separate, after this thread)
1. Revise `Blackboard_Authoring_Detailed_Design.md` §2/§3/§13/§14.6: Category-2 persistence moves from `.Blackboard.cs` to JSON; delete source-text-parser/verbatim-span/State-B-C/RT-over-C#. Category-1 reflection unchanged.
2. Activate (engine-wire) the built components on the JSON substrate; add JSON-generator struct + offset-thunk emission (§6.3, option 2a); enforce blittable+fixed-size + defaults; add SoA fixed-array authoring.
3. Persistence-coupled tasks change (`1b-01` emitter→JSON; `1f-05` suppression persistence→JSON; layout-method order/sync→JSON; `1g-04/05` already JSON). Logic tasks (bin-pack/aggregate/alias/sync/validate/panel) unchanged.

## 14. Architect confirmations — RESOLVED, + verify-at-implementation items
**Resolved by architect review** (folded into D12–D14, §6.3, §6.5, §6.6, §7, §8): dual-path quick reload; `[BlueprintRegistrar]` masquerade for registration (no HR-001 lift); VisualId/StableId stitching; 100-byte ceiling; defaults in `ParseParams` (+ heavy-tier `StructureHash` init); `fixed[N]`/`[InlineArray(N)]` emission with Sequential alignment; the `[InlineArray]` defensive-copy trap.

**All verify-first items RESOLVED against our actual (Thread-2) tree:**
1. ✅ **BTree + HSM registration bridge.** BTree thunks register via `BehaviorRegistry.RegisterAction/RegisterCondition(int,string,BlueprintBTree{Action,Condition}Delegate)` ([BehaviorRegistry.cs:24,34,226,233](FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs#L24)) — a dedicated thunk-bypass, coordinator-injectable, separate from generic `ActionRegistry<T,C>`. HSM thunks register via the **static** `HsmActionDispatcher.RegisterAction/RegisterGuard` (static class confirmed: [AiHotReloadCoordinator.cs:98,287](FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs#L98)). Both in `Behavior/*`, not the Thread-2-edited `Blueprints/BlueprintRegistry.cs`.
2. ✅ **Editor-owned trees are unwired at runtime today.** `SampleScout`/`SampleGuard` appear only in their own `.cs`, editor-side discovery tests, and BATCH docs — never in `AiBehaviorFactory`/runtime registration. So the masquerade bridge is **net-new wiring** that closes an existing gap, not a replacement.
3. ✅ **PU-04 byte-identical scope** (design stance, architect-agreed): the gate compares only the **topology core** (`CreateBuilder` + thunk + `[*Layout]`); the additive `[BlueprintRegistrar]` bridge is excluded. After decommit, functional-equivalence tests cover the rest.
4. ✅ **Coordinator injection contract** (verified in the Thread-2-modified file). `ResolveRegistrarArgument` injects `BlueprintRegistryStaging` ([:338](FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs#L338)) and `BehaviorRegistry` ([:340](FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs#L340)); throws on forbidden types incl. `BlueprintRegistry`/`HsmActionDispatcher` ([:342](FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs#L342)). So `Register(BehaviorRegistry, BlueprintRegistryStaging)` is the supported masquerade signature. **Thread-2 refinement (BPF-042):** the injected `BehaviorRegistry` is a *staging* instance (fresh per reload, [:293](FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs#L293)), merged via `MergeFrom`/`CommitStagingMerge` so a throwing registrar can't corrupt the live registry — the bridge simply registers into whatever registry it's handed; same path serves `ApplyQuickReload` ([:176](FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs#L176)).

## 15. Verification (reach green before reporting)
- `dotnet build IOS-IG-SimHost.sln` 0 errors; touched projects 0 new warnings (~26 pre-existing, DEBT-BCP-004 — leave).
- New: JSON round-trip byte-stability tests; migration equivalence tests (`json→.cs` byte-identical); emit-core determinism (re-based `SaveBTree/HsmEmitTests`).
- `EditorSubsystemBoot` filter 10/10; `Hrot.Editor.AiShared.Tests`; `Hrot.Blueprints.Tests` (only the 10 pre-existing DEBT-006).
- Pre-existing failures NOT regressions: DEBT-006 (10), DEBT-008, SpatialHashSystem AV in EditorPreview, ClusterOpE2e DDS crash, flaky sub-80ns perf (DEBT-014).

## 16. Risks / gotchas
- **Generated-C# byte-stability** post-relocation (covered by emit-core + migration tests).
- **Layout fidelity** round-trips via `VisualId`/`StableId` reconciliation (preserve `OnReloadCompleted` behavior).
- **Don't regress the blueprint path** — reuse its patterns; don't touch its working flow.
- **Parallel-generator visibility** (§6.3) — design the JSON generator self-contained for editor-owned struct emission from day one.
- **Thread 2 is mutating shared files concurrently** (`AiHotReloadCoordinator.cs`, `BlueprintRegistry.cs`, blueprint tests) — rebase first; keep edits in the BTree/HSM editor + new netstandard2.0 lib + `Fdp.Toolkits.Analyzers`.

## 17. Suggested batch breakdown (keystone first)
- **PU-01** netstandard2.0 emit core + persisted DTOs (B2) + editor⇄DTO mapping; JSON serializer/services for BTree & HSM (mirror `BlueprintJsonServices`); round-trip byte-stability tests. *(keystone — no behavior change yet)*
- **PU-02** New IncrementalGenerator(s) over `*.btree.json`/`*.hsm.json`; `<AdditionalFiles>` wiring; emit `CreateBuilder()`+thunk to `obj/` **plus the per-asset `[BlueprintRegistrar]` self-registration bridge (D14)** that compiles+registers the definition (since `FbtTreeCatalog` can't see JSON-owned defs); migration-equivalence tests. *(Verify §14 items 1–2 first.)*
- **PU-03** Editor load path: JSON deserialize for editor-owned; keep reflection/blob for hand-authored (dual-load); `SourceFilePath` from JSON.
- **PU-04** Migration pass (`.cs`→`.json`), byte-identical verification, decommit generated `.cs`.
- **PU-05** Path-at-creation + 3 fixed roots + base-name collision guard.
- **PU-06** Unified Save/Save-All (`FlushNow`, all dirty docs, `Ctrl+Shift+S`, flush-on-close).
- **PU-07** Unified tree asset browser (NodeEdit widgets).
- **PU-08** Refactor/rename across json+cs (JSON-aware writer seam).
- **PU-09** *(may be follow-on — required to meet ≤100 ms target, not to avoid regression)* In-process Roslyn quick-reload for BTree/HSM via the shared emit core (mirror `QuickReloadService`); registration via the `[BlueprintRegistrar]` masquerade (D14) — no HR-001 change. Implements D12.
- **Cross-cutting (PU-03):** post-reload stitching by `VisualId`/`StableId` (D13/§6.6) — fold into the load-path batch since it shares the reconciliation code.
