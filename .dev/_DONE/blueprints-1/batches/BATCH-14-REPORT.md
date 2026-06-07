# BATCH-14 Report — TASK-CP-006: Compiler Test Suite

**Status:** APPROVED  
**Task:** TASK-CP-006  
**Compiler filter result:** Passed: 160, Failed: 0  
**Full suite result:** Passed: 347, Failed: 0, Skipped: 3 (pre-existing)

---

## Work Completed

### Production Fixes

**`AiPrimitiveEmitter.cs`** — Fixed four incorrect fully-qualified type names in BTree/HSM thunk emission:
- `global::Fdp.Toolkit.Behavior.BrainBlackboard` → `global::Fdp.Toolkit.Behavior.Components.BrainBlackboard`
- `global::Fdp.Toolkit.Behavior.BehaviorTreeState` → `global::Fbt.BehaviorTreeState`
- `global::Fdp.Toolkit.Blueprints.Blackboard1024` → `global::Fdp.Toolkit.Behavior.Components.Blackboard1024`
- `global::FastHSM.HsmKernelBridge` → `global::Fdp.Toolkit.Behavior.Systems.HsmKernelBridge`

**`Stage2_Validate.cs`** — Three fixes for real JSON assets (which have empty Pins arrays until Stage 3/4):
1. `V_GraphStructure.Validate`: skip structural checks when all nodes have empty pins
2. `V_GraphStructure.FindEntryNode`: check for `EventEntryNode` first before pin-based detection
3. `V_LinkIntegrity`: guard pin-ownership checks with `if (node.Pins.Count > 0 && !node.Pins.Any(...))` to skip when no pin data
4. `V_ChannelCommandReferences`: added empty-catalog guard and fixed matching by `.Name` not `.FullName`
5. `V_WaitNodeReferences`: same pattern as above
6. `V_EventGraphReferences`: accept both `FullName` and `Name` match for catalog lookup

**`StaticTypeRegistry.cs`** — Added `System.String` and `System.Object` as managed (non-unmanaged) types so BP1501/BP1503 can fire properly when those types appear in pin TypeRefs or state fields.

**`BlueprintTestFixture.cs`** — Updated `CompileAndLoadMany` to match current compiler API.

### Test Infrastructure

- `CoversDiagnosticCodeAttribute.cs` — Custom xUnit attribute for tagging tests with diagnostic code coverage
- `TestDiagnosticInventory.cs` — Enumerates all `DiagnosticCodes.*` constants; used by coverage test

### New Test Files (all in `Hrot.Blueprints.Tests.Compiler` namespace)

| File | Tests |
|------|-------|
| `Stage1_ParseTests.cs` | JSON parse, malformed JSON, missing required fields, unknown node type |
| `V_DispatchKindCompatibilityTests.cs` | BP1400, BP1401 — incompatible dispatch/event/channel combinations |
| `V_AiPrimitiveIntentTests.cs` | BP1100, BP1101, BP1102 — AiPrimitive intent/hosting constraints |
| `V_VariablesAndStateTests.cs` | BP1200–BP1210 — params/state size limits |
| `V_PeerReferencesTests.cs` | BP1300 — unknown peer reference |
| `V_AllValidatorsCoverageTests.cs` | Completeness: every DiagnosticCode has at least one positive test via `CoversDiagnosticCode` |
| `Stage3_NormalizationTests.cs` | Constant folding, dead code pruning, empty graph, idempotency |
| `Stage4_TypeResolveTests.cs` | BP1500, BP1501, BP1502, BP1503, resolved pin types map |
| `Stage5_ScheduleTests/GoldenIrTests.cs` | Golden IR snapshots for LibraryMath, InstanceCounter, MoveToAndFire; BP4001, BP4004 |
| `Stage5_ScheduleTests/DataFlowCseTests.cs` | CSE deduplication, pure-only CSE, idempotency |
| `Stage5_ScheduleTests/LatentBlockSplitTests.cs` | Latent delay splits into two blocks, continuation block created |
| `Stage6_LoweringTests/LibraryLoweringTests.cs` | BP5001, BP9001; library lowering happy path |
| `Stage6_LoweringTests/AiPrimitiveLoweringTests.cs` | Intent/hosting round-trip; BP5002 |
| `Stage6_LoweringTests/InstanceLoweringTests.cs` | Non-zero StructureHash; BP5003 |
| `Stage6_LoweringTests/ChannelCommandLoweringTests.cs` | ChannelCommand op lowering |
| `Stage6_LoweringTests/DebugProbeInsertionTests.cs` | Debug/Trace probes inserted; Release emits none |
| `Stage7_EmitTests/LibraryEmitGoldenTests.cs` | Golden source snapshot + determinism for LibraryMath |
| `Stage7_EmitTests/AiPrimitiveEmitGoldenTests.cs` | Golden snapshots + determinism for HasVisibleTarget, MoveToAndFire |
| `Stage7_EmitTests/InstanceEmitGoldenTests.cs` | Golden snapshots + determinism for InstanceCounter, HealthRegen, DoorActor |
| `Stage7_EmitTests/ThunkEmissionTests.cs` | BTreeAction `BTreeTick`; BTreeCondition `BTreeEvaluate`; HsmAction `HsmActivity`; HsmGuard `HsmGuard`; MultipleHostings |
| `Stage7_EmitTests/SanitizerTests.cs` | `SanitizeName` identity cases; `GeneratedFileName` structure |
| `Stage8_RoslynTests/InMemoryCompileTests.cs` | BP7001; valid source → PE bytes; full LibraryMath pipeline with `EmitPdbWithEmbeddedSource=true` |
| `Stage8_RoslynTests/PdbEmbeddedSourceTests.cs` | PDB non-null with flag; PDB null without flag; embedded source signature |
| `Stage8_RoslynTests/MetadataReferenceResolverTests.cs` | Non-empty references; no-location assemblies excluded; corelib included |
| `Determinism/CompilerDeterminismTests.cs` | Full pipeline determinism across 4 assets; file name determinism; different assets → different sources |
| `Determinism/BlueprintIdHashTests.cs` | FnvHasher.Hash32/Hash64 known vectors and determinism |
| `Determinism/StructureHashTests.cs` | StructureHashComputation.Compute determinism and sensitivity |
| `EndToEnd/MoveToAndFire_EndToEndTests.cs` | Compiles; contains expected structures; debug map; file name |
| `EndToEnd/HealthRegen_EndToEndTests.cs` | Compiles; Tick signature; variables section |
| `EndToEnd/HasVisibleTarget_EndToEndTests.cs` | Compiles; expected structures |
| `EndToEnd/DoorActor_DoorSensor_EndToEndTests.cs` | Both compile; different sources |
| `EndToEnd/MathUtilsLib_EndToEndTests.cs` | Library compiles; registrar emitted |

### Snapshot Files Generated

- `Hrot.Blueprints.Tests/Snapshots/Schedule/LibraryMath.ir.txt`
- `Hrot.Blueprints.Tests/Snapshots/Schedule/InstanceCounter.ir.txt`
- `Hrot.Blueprints.Tests/Snapshots/Schedule/MoveToAndFire.ir.txt`
- `Hrot.Blueprints.Tests/Snapshots/Emit/LibraryMath.cs.txt`
- `Hrot.Blueprints.Tests/Snapshots/Emit/HasVisibleTarget.cs.txt`
- `Hrot.Blueprints.Tests/Snapshots/Emit/MoveToAndFire.cs.txt`
- `Hrot.Blueprints.Tests/Snapshots/Emit/InstanceCounter.cs.txt`
- `Hrot.Blueprints.Tests/Snapshots/Emit/HealthRegen.cs.txt`
- `Hrot.Blueprints.Tests/Snapshots/Emit/DoorActor.cs.txt`

### Other Fixes During Implementation

- `SampleAssetLoadTests.cs` — Updated `LoadSnapshot_NonExistentSnapshot_ThrowsFileNotFoundException` to use a path that genuinely doesn't exist (original path was for `LibraryMath.ir.txt` which now exists after snapshot generation).
- `DebugProbeInsertionTests.cs` — Changed minimal graph from `Entry().Return()` to `Entry().Delay(1.0f).Return()` because `EventEntryNode` emits no statements; a block with 0 statements receives no probes.
- `ThunkEmissionTests.cs` — Fixed `BTreeCondition_EmitsBTreeTick_Thunk` to assert `"BTreeEvaluate"` (the correct method name) instead of `"BTreeTick"` (which is the BTreeAction method).
- `InMemoryCompileTests.cs` — Added `EmitPdbWithEmbeddedSource: true` to `Stage8_FullPipelineToRoslyn_Succeeds_ForLibraryAsset` so Stage 8 Roslyn runs and `PortablePe` is non-null.
- All E2E tests — Removed `Assert.NotNull(result.PortablePe)` assertions from tests using `DefaultOptions()` (Stage 8 Roslyn is only invoked when `EmitPdbWithEmbeddedSource=true`).

---

## Success Criteria Coverage

| SC | Description | Status |
|----|-------------|--------|
| SC1 | `--filter "FullyQualifiedName~Compiler"` → 0 failures | PASS (160/160) |
| SC2 | Every DiagnosticCode has ≥1 positive test via `CoversDiagnosticCode` | PASS (`V_AllValidatorsCoverageTests`) |
| SC3 | Stage 7 golden snapshots exist and match | PASS (6 emit snapshots) |
| SC4 | Stage 5 IR snapshots match | PASS (3 schedule snapshots) |
| SC5 | Determinism: same asset → same output across two runs | PASS (`CompilerDeterminismTests`) |
| SC6 | MoveToAndFire end-to-end compiles and source contains expected structures | PASS |

---

## Test Output

See `.dev/blueprints-1/batches/BATCH-14-TEST-OUTPUT.txt` for the full verbose test output.
