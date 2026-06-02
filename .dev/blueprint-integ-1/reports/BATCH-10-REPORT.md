# BATCH-10 Report

## Implementation Summary

### Task 1 — Game-side layout-contracts assembly (AIE-ENABLE-1)

**New assembly created:** `Hrot/Editor/Hrot.Editor.AiContracts/Hrot.Editor.AiContracts.csproj`

- Target framework: net8.0; no ImGui / NodeEdit / AiShared-heavy deps — only `System.Numerics` (implicit).
- Project GUID: `{A0B1C2D3-E4F5-6789-0ABC-DEF012345678}`, added to `IOS-IG-SimHost.sln` under the `Editor` solution folder.

**Types moved from `Hrot.Editor.AiShared` → `Hrot.Editor.AiContracts`** (namespaces kept identical):

| File | From namespace | Stays at |
|---|---|---|
| `Layout/BTreeEditorLayout.cs` | `Hrot.Editor.AiShared.Layout` | `Hrot.Editor.AiContracts` |
| `Layout/BTreeEditorLayoutBuilder.cs` | `Hrot.Editor.AiShared.Layout` | `Hrot.Editor.AiContracts` |
| `Layout/BTreeLayoutAttribute.cs` | `Hrot.Editor.AiShared.Layout` | `Hrot.Editor.AiContracts` |
| `Layout/HsmEditorLayout.cs` | `Hrot.Editor.AiShared.Layout` | `Hrot.Editor.AiContracts` |
| `Layout/HsmEditorLayoutBuilder.cs` | `Hrot.Editor.AiShared.Layout` | `Hrot.Editor.AiContracts` |
| `Layout/HsmLayoutAttribute.cs` | `Hrot.Editor.AiShared.Layout` | `Hrot.Editor.AiContracts` |
| `Layout/BlueprintLayoutAttribute.cs` | `Hrot.Editor.AiShared.Layout` | `Hrot.Editor.AiContracts` |
| `Layout/NodeLayoutEntry.cs` | `Hrot.Editor.AiShared.Layout` | `Hrot.Editor.AiContracts` |
| `Layout/StateLayoutEntry.cs` | `Hrot.Editor.AiShared.Layout` | `Hrot.Editor.AiContracts` |
| `Layout/TransitionLayoutEntry.cs` | `Hrot.Editor.AiShared.Layout` | `Hrot.Editor.AiContracts` |
| `Layout/RegionLayoutEntry.cs` | `Hrot.Editor.AiShared.Layout` | `Hrot.Editor.AiContracts` |
| `Blackboard/SubtreeSyncBinding.cs` | `Hrot.Editor.AiShared.Blackboard` | `Hrot.Editor.AiContracts` |

**12 source files deleted from `Hrot.Editor.AiShared`** (originals replaced by the project reference to AiContracts).

**Reference chain:**
- `Hrot.Editor.AiShared.csproj` → added `<ProjectReference>` to `Hrot.Editor.AiContracts`
- `Hrot.AI.Behaviors.csproj` → added `<ProjectReference>` to `Hrot.Editor.AiContracts` (in a new ItemGroup before the existing toolkits group)

`LayoutDiscovery.cs` remains in `Hrot.Editor.AiShared` (uses `System.Reflection`; editor-only).

**Duplicate `HsmLayoutAttribute` resolved:**  
`FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Attributes/HsmLayoutAttribute.cs` was **deleted**. Nothing in `Fhsm.Kernel` consumed it; the canonical type is now exclusively in `Hrot.Editor.AiContracts` (`namespace Hrot.Editor.AiShared.Layout`). All existing code using `[HsmLayout]` continues to work unchanged because they import `using Hrot.Editor.AiShared.Layout;` which resolves to the contracts assembly.

**`BTreeFluentEmitter.LayoutNamespace` fixed:**  
Changed from `"Hrot.AI.Behaviors.Trees.Layout"` (wrong, never existed) to `"Hrot.Editor.AiShared.Layout"` (matches HSM emitter and the actual namespace of the moved types).

### Task 2 — Sample BTree + sample HSM assets (AIE-ENABLE-2)

**SampleScout** (`Hrot/Subsystems/Hrot.AI.Behaviors/Trees/SampleScout.cs`):
- Tree name: `"SampleScout"`
- Asset ID: `54ef3847-0000-0000-0000-000000000000` (FNV-1a-32 of "SampleScout")
- Structure: `Sequence → { Wait(1s), Wait(2s) }` — pure structural nodes, no external delegates
- Fixed node visual IDs: `10000000-…-0001` (root), `20000000-…-0001` (sequence), `30000000-…-0001` (wait1), `40000000-…-0001` (wait2)
- Has `[BTreeDefinition("SampleScout")]` returning `BehaviorTreeBlob`
- Has `[BTreeLayout("54ef3847-…")]` returning `BTreeEditorLayout` with 3 positioned nodes

**SampleGuard** (`Hrot/Subsystems/Hrot.AI.Behaviors/Machines/SampleGuard.cs`):
- Machine name: `"SampleGuard"`
- Asset ID: `979df4a4-0000-0000-0000-000000000000` (explicit on `[HsmDefinition]`; also equals FNV-1a-32 of "SampleGuard")
- Structure: `Idle --[Alert]--> Scanning --[Clear]--> Idle` — two states, two transitions, no action/guard delegates
- Fixed state stable IDs: `aa010000-…-0001` (Idle), `bb010000-…-0001` (Scanning)
- Fixed transition visual IDs: `cc010000-…-0001` (Alert), `dd010000-…-0001` (Clear)
- Has `[HsmDefinition("SampleGuard", AssetId = "979df4a4-…")]` returning `HsmDefinitionBlob`
- Has `[HsmLayout("979df4a4-…")]` returning `HsmEditorLayout` with 2 states + 2 transitions positioned

**Bug fixed during implementation:** `HsmAssetContributor.LoadFrom` was passing `null` for the `MachineMetadata` parameter to `HsmAssetProjector.Project`. The projector then used empty metadata, so all `StateStableIds` were absent and fallback `Guid.NewGuid()` values were assigned — preventing layout lookup by stable ID. Fixed to pass `blob.Metadata` (populated by `StateMachineGraph.Compile()`).

### New tests

**`Hrot.BTree.Editor.Tests/SampleScoutDiscoveryTests.cs`** (6 tests):
- `BTreeAssetContributor_LoadFrom_DiscoversSampleScout` — contributor finds the asset by name
- `BTreeAssetContributor_LoadFrom_SampleScout_HasCorrectAssetId` — FNV hash matches
- `BTreeAssetContributor_LoadFrom_SampleScout_LayoutIsApplied` — at least one node has non-zero position
- `BTreeEmitter_LayoutUsing_ResolvesInRuntimeAssembly` — emitted code contains `using Hrot.Editor.AiShared.Layout;`, not the old wrong namespace; the behaviors assembly references `Hrot.Editor.AiContracts`
- `SampleScout_Layout_ReturnsNonNullWithExpectedNodes` — layout method returns 3 entries
- `SampleScout_Build_ReturnsValidBlob` — compiled blob is non-null with nodes

**`Hrot.Hsm.Editor.Tests/SampleGuardDiscoveryTests.cs`** (7 tests):
- `HsmAssetContributor_LoadFrom_DiscoversSampleGuard` — contributor finds by name
- `HsmAssetContributor_LoadFrom_SampleGuard_HasCorrectAssetId` — explicit GUID matches
- `HsmAssetContributor_LoadFrom_SampleGuard_KindIsHsm` — kind is Hsm
- `HsmAssetContributor_LoadFrom_SampleGuard_LayoutIsApplied` — states have non-zero positions
- `SampleGuard_Layout_ReturnsNonNullWithExpectedStates` — 2 states and 2 transitions
- `SampleGuard_Compile_ReturnsValidBlob` — blob is non-null
- `SampleGuard_LayoutNamespace_IsResolvableFromBehaviorsAssembly` — behaviors assembly references AiContracts

---

## Design Decisions

1. **`SubtreeSyncBinding` moved to AiContracts** even though it is in the `Hrot.Editor.AiShared.Blackboard` namespace. `BTreeEditorLayout` depends on it for the `SyncBindings` property. Keeping both in AiContracts avoids a cross-namespace dependency from the lightweight contracts assembly back to AiShared. The namespace `Hrot.Editor.AiShared.Blackboard` is preserved on the record, so existing code is unaffected.

2. **`LayoutDiscovery` stays in AiShared** per spec (it uses `System.Reflection` and is editor-only). Its type parameters (`TAttr`, `TLayout`) work via generic duck-typing, so it handles types from AiContracts without a direct reference.

3. **No type-forwarding stubs** were used. Since namespaces are identical and types were moved wholesale, the deleted AiShared files are simply gone — the AiContracts reference makes them visible again.

4. **`HsmAssetContributor.LoadFrom` metadata fix** was applied because the bug blocked the layout test. It's an observable correctness fix (state names and layout positions were wrong for assemblies compiled via `StateMachineGraph.Compile()`). The pre-existing `TestMachine` test is unaffected because it builds its blob manually via `HsmNormalizer.Normalize + HsmFlattener + HsmEmitter.Emit` without setting `blob.Metadata`.

5. **SampleScout uses `BrainBlackboard`/`BTreeContext`** — the types already used in `Hrot.AI.Behaviors`. No new dummy struct needed.

---

## Deviations

| Deviation | Why | Benefit | Risk |
|---|---|---|---|
| Deleted `Fhsm.Kernel.Attributes.HsmLayoutAttribute` entirely | Nothing in Fhsm.Kernel consumed it; keeping it caused CS0104 ambiguity | Single canonical type; no ambiguity | If an external package depended on `Fhsm.Kernel.Attributes.HsmLayoutAttribute`, it would break. Internally no consumers found. |
| Fixed `HsmAssetContributor.LoadFrom` metadata bug | Required for layout round-trip test to pass | Correct state names and positions when loading from assemblies built via `.Compile()` | None — pre-existing tests still pass |

---

## Test Results

| Test suite | Pass | Fail | Skipped | Notes |
|---|---|---|---|---|
| `Hrot.Editor.AiShared.Tests` | 702 | 0 | 0 | All layout/discovery/projection tests green |
| `Hrot.BTree.Editor.Tests` | 377 | 0 | 0 | Includes 6 new SampleScout tests |
| `Hrot.Hsm.Editor.Tests` | 330 | 0 | 0 | Includes 7 new SampleGuard tests |
| `Hrot.ClusterRunner.Integration.Tests --filter EditorSubsystemBoot` | 10 | 0 | 0 | Boot integration tests green |
| `Hrot.Blueprints.Tests` | 889 | 10 | 8 | Same 10 pre-existing failures (golden + timing + allocation); no new failures |
| **Full solution build** | — | **0 errors** | — | `dotnet build IOS-IG-SimHost.sln` → 0 errors, 0 warnings |

Pre-existing Blueprints failures (not caused by this batch):
- 7 golden/snapshot failures (`EmitMatchesGoldenSource`, `GeneratedSource_Snapshot`)
- 1 allocation test (`TickFrame_1000Frames_AllocatesZeroBytes`)
- 1 condition summary test (`Synthesize_EqsResult_ScoreCrossed_IncludesThreshold`)
- 1 timing/perf test (`ReadEqsResultNode_Under80ns_perInvocation` or similar — varies run-to-run)

---

## Developer Insights

1. **`BTreeDefinitionAttribute` has no `AssetId` property** — the fixture `simple_guard.cs` shows `AssetId = "..."` but that file is excluded from compilation (`<Compile Remove>`). For `SampleScout`, the asset ID is purely FNV-1a-32 derived from the tree name by `BTreeAssetContributor`.

2. **`HsmAssetContributor.LoadFrom` null metadata** was a silent bug: assets compiled via `CreateBuilder().Build().Compile()` had `blob.Metadata` populated (including state names and stable IDs), but the contributor discarded it. State names appeared as `"State_1"` instead of `"Idle"`, and layout positions defaulted to (0,0). Fixed.

3. **`HsmBuilder.GoTo` requires all target states to be registered before the call.** States must be declared first, then transitions defined. The initial `SampleGuard.CreateBuilder()` had the transition defined before "Scanning" was added, causing `InvalidOperationException` at runtime.

4. **Type identity across assemblies** worked correctly: both `Hrot.AI.Behaviors` (directly references AiContracts) and `Hrot.Editor.AiShared` (transitively references AiContracts) load the same `Hrot.Editor.AiContracts.dll` — so `HsmLayoutAttribute` is the same CLR type in both, and `LayoutDiscovery.TryGetLayout<HsmLayoutAttribute, ...>` correctly finds attributes decorated in the game assembly.

---

## Known Issues

None. All required functionality implemented and tested.

---

## Suggested Commit Message

feat: extract layout-contract types into Hrot.Editor.AiContracts; add SampleScout/SampleGuard samples; fix BTree emitter LayoutNamespace and HSM contributor metadata
