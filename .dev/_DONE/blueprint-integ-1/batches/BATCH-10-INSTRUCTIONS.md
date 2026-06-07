# BATCH-10: Game-side layout-contracts assembly + sample BTree & HSM assets
**Tasks:** AIE-ENABLE-1 (layout contracts), AIE-ENABLE-2 (sample assets)   **Phase:** Enablement (pre-Phase-4)   **Est:** ~10h
**Dependencies:** solution currently compiles (after parking the broken scaffold).

## Why
The editor's fluent emitters write a `[BTreeLayout]`/`[HsmLayout]` method (returning `BTreeEditorLayout`/`HsmEditorLayout`) into BTree/HSM C# files that live in the **runtime** project `Hrot.AI.Behaviors` and are hot-reloaded. But those layout-contract types currently live **only** in the heavy editor assembly `Hrot.Editor.AiShared` — which `Hrot.AI.Behaviors` cannot (and must not) reference. So authored/emitted assets don't compile (this is what broke the build via the parked `MyCustomPatrol.cs`). Also there are **no** `[BTreeDefinition]`/`[HsmDefinition]` methods in `Hrot.AI.Behaviors`, so the editor's Asset Browser shows nothing to open.

## Onboarding
1. `.dev/.guides/DEV-GUIDE_claude.md`.
2. This file (the facts below are verified — use them).
Use **codebase-memory MCP** first; not `search_code`. Verify against code; don't invent.

## Verified facts
- Layout-contract types currently in `Hrot/Editor/Hrot.Editor.AiShared/Layout/`:
  `BTreeEditorLayout`, `BTreeEditorLayoutBuilder`, `BTreeLayoutAttribute`, `HsmEditorLayout`, `HsmEditorLayoutBuilder`, `HsmLayoutAttribute`, `BlueprintLayoutAttribute`, `NodeLayoutEntry`, `RegionLayoutEntry`, `StateLayoutEntry`, `TransitionLayoutEntry`. All in namespace `Hrot.Editor.AiShared.Layout`. **`LayoutDiscovery.cs` is reflection (editor-only) — it STAYS in AiShared.**
- `BTreeEditorLayout` references `Hrot.Editor.AiShared.Blackboard` types (e.g. `SubtreeSyncBinding`) — check the exact set; those referenced-by-contract types must also be reachable from the new assembly (move the minimal game-side data types, or keep the contract independent of heavy Blackboard logic).
- Emitter namespaces (MISMATCH to fix): `BTreeFluentEmitter` `LayoutNamespace = "Hrot.AI.Behaviors.Trees.Layout"` (line ~25) — wrong; `HsmFluentEmitter` `LayoutNamespace = "Hrot.Editor.AiShared.Layout"` (line ~20) — matches actual.
- Duplicate: `Fhsm.Kernel.Attributes.HsmLayoutAttribute` AND `Hrot.Editor.AiShared.Layout.HsmLayoutAttribute`. The HSM emitter/projector/LayoutDiscovery use the `Hrot.Editor.AiShared.Layout` one. Resolve the ambiguity (one canonical; no CS0104).
- `Hrot.AI.Behaviors.csproj` references: Fdp.Toolkits, Fdp.Core, Hrot.Core, Fbt.Compiler, Fhsm.Kernel, Fhsm.Compiler, Blueprints.Generators, Blueprints.Compiler (NO editor assemblies). `[BTreeDefinition]` (Fbt.Kernel), `[HsmDefinition]`/`[HsmLayoutAttribute]` (Fhsm.Kernel) are already available there.
- Contributors `BTreeAssetContributor.LoadFrom(assembly)` / `HsmAssetContributor.LoadFrom(assembly)` reflect the assembly for `[BTreeDefinition]`/`[HsmDefinition]` methods. Layout is OPTIONAL (LayoutDiscovery → auto-layout if absent).

## Task 1: Game-side layout-contracts assembly (AIE-ENABLE-1)
Create a new **lightweight** assembly `Hrot/Editor/Hrot.Editor.AiContracts/Hrot.Editor.AiContracts.csproj` (net8.0; minimal deps — `System.Numerics`; add Fbt.Kernel/Fhsm.Kernel only if a moved type needs them; **no** ImGui/NodeEdit/AiShared-heavy deps). **Move** the layout-contract types listed above into it, **keeping their existing namespaces unchanged** (`Hrot.Editor.AiShared.Layout`, etc.) so existing editor code needs no edits. Move the minimal dependent data types they reference (e.g. the `SubtreeSyncBinding`/entry records) into this assembly too (or refactor the contract to not depend on heavy Blackboard logic) — keep it game-side-safe.
- Resolve the **duplicate `HsmLayoutAttribute`**: keep one canonical (the `Hrot.Editor.AiShared.Layout` one in the new assembly is used by the emitter/projector); remove or alias the other so there is no ambiguity and the HSM projector/LayoutDiscovery still resolve.
- Add `<ProjectReference>` to `Hrot.Editor.AiContracts` from **`Hrot.AI.Behaviors.csproj`** AND from **`Hrot.Editor.AiShared.csproj`** (so both runtime and editor see the same types). Add to the editor subsystem chain as needed so everything still resolves.
- Fix `BTreeFluentEmitter.LayoutNamespace` to the actual namespace the types now live in (align with HSM: `"Hrot.Editor.AiShared.Layout"`), so emitted BTree files reference a real, runtime-referenceable namespace.
**Tests required:** existing `LayoutDiscoveryTests` + `BehaviorTreeAssetProjectionTests` + HSM projection tests still pass. New `BTreeEmitter_LayoutUsing_ResolvesInRuntimeAssembly` — emit a sample model and assert the emitted layout `using` namespace is one that `Hrot.AI.Behaviors` references (and round-trips: see Task 2 round-trip test).

## Task 2: Sample BTree + sample HSM assets (AIE-ENABLE-2)
Add to `Hrot.AI.Behaviors` (e.g. `Hrot/Subsystems/Hrot.AI.Behaviors/Trees/SampleScout.cs` and `.../Machines/SampleGuard.cs`):
- **Sample BTree** `SampleScout`: a static class with `[BTreeDefinition("SampleScout", AssetId="<fixed guid>")] public static BehaviorTreeBlob Build()` returning a compiled tree built from **structural nodes that need no external action/condition delegates** (e.g. Root→Sequence→{Wait, Wait}, or use existing registered CGF actions if simple) so it compiles standalone. Include a matching `[BTreeLayout("<same guid>")] public static BTreeEditorLayout Layout()` with node positions (proves the layout contract compiles in the runtime project).
- **Sample HSM** `SampleGuard`: `[HsmDefinition("SampleGuard", AssetId="<fixed guid>")] public static HsmDefinitionBlob Compile()` with a few simple states + a transition (no external actions needed, or trivial ones), plus `[HsmLayout("<same guid>")] public static HsmEditorLayout Layout()`.
- Use **fixed, valid** GUIDs (not placeholders) and node `visualId`/`stableId`s that the layout method references, so projection maps layout↔nodes.
**Tests required:** `Hrot.AI.Behaviors` compiles (incl. the `[Layout]` methods). `BTreeAssetContributor_LoadFrom_DiscoversSampleScout` and `HsmAssetContributor_LoadFrom_DiscoversSampleGuard` (load the built `Hrot.AI.Behaviors` assembly → contributor enumerates the sample by name + AssetId). A **round-trip** test: project the sample → run the fluent emitter → the emitted C# compiles (or at least the layout `using`/attribute references resolve against the contracts assembly).

## Success Criteria
- [ ] **Full solution builds with 0 errors** (`dotnet build IOS-IG-SimHost.sln`).
- [ ] `Hrot.AI.Behaviors` contains a discoverable sample BTree + sample HSM (with `[Layout]`), and the contributors find them (tested).
- [ ] No editor assembly is referenced by `Hrot.AI.Behaviors`; layout contracts are in the new lightweight assembly referenced by both.
- [ ] Green: `Hrot.Editor.AiShared.Tests`, `Hrot.BTree.Editor.Tests`, `Hrot.Hsm.Editor.Tests`, `EditorSubsystemBoot` filter. Blueprints no new failures beyond DEBT-006's 10.
- [ ] No warnings; docs; no leftover TODO/debug.
- [ ] Report at `.dev/blueprint-integ-1/reports/BATCH-10-REPORT.md`.

## Execution rules
- Verify the exact dependency set of the moved types before moving (don't break AiShared). Keep namespaces identical to avoid editing dozens of usings.
- Run `dotnet build IOS-IG-SimHost.sln` and the named suites yourself; fix root causes; never fake a pass. The sample definitions must use only types/methods that compile standalone in `Hrot.AI.Behaviors`.

## Report Requirements
In `reports/BATCH-10-REPORT.md`: the new assembly's deps + exactly which types moved; how the duplicate HsmLayoutAttribute was resolved; the BTree emitter namespace fix; what the samples contain (GUIDs, node structure) and how discovery was verified; the round-trip result; full-solution build confirmation; actual test counts; suggested commit message. No comprehension questions.
