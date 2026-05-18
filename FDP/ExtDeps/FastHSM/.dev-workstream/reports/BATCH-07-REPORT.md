# BATCH-07 REPORT: Compiler - Flattener & Emitter

## Implementation Status
- [x] **Component: HsmFlattener**
  - Implemented transformation from `StateMachineGraph` to `FlattenedData`.
  - Implemented Least Common Ancestor (LCA) algorithm for cost calculation.
  - Implemented topological state flattening (Root -> Children recursively).
  - Implemented dispatch table generation (`ActionIds`, `GuardIds`).
  - **Optimization**: Transition cost pre-calculated to avoid runtime LCA.

- [x] **Component: HsmEmitter**
  - Implemented `HsmDefinitionBlob` generation.
  - Implemented **SHA256 Hashing** for integrity and hot-reload detection:
    - `StructureHash`: Topology (ParentIndex, Depths, etc.).
    - `ParameterHash`: Logic (ActionIds, GuardIds, etc.).
  - Implemented sorting and dispatch indexing.

- [x] **Kernel Updates**
  - Refactored `HsmDefinitionBlob` to be `sealed` and immutable.
  - Optimized `StateDef`, `TransitionDef`, `GlobalTransitionDef` layout to match final design (added `NextSiblingIndex`, `Cost`, aligned offsets).
  - Updated `StateFlags` to use `IsHistory` and `IsParallel` naming.

## Architecture Decisions Implemented
- **AD-Q12 (Hot Reload Hashing)**: Emitter computes two separate hashes. This allows the runtime to distinguish between structural changes (requiring full reset) and logic/parameter changes (patchable).
- **AD-Q7 (Global Transitions)**: Flattened into a separate `GlobalTransitionDef` table for O(G) lookup preference.

## Verification
- **Unit Tests**:
  - Created `tests/Fhsm.Tests/Compiler/FlattenerEmitterTests.cs`.
  - Verified topological ordering of flattened states.
  - Verified dispatch table generation.
  - Verified definition validation logic.
  - Verified Hash sensitivity (parameter changes alter ParameterHash).
- **Integration Tests**:
  - Updated `DataLayerIntegrationTests.cs` to match immutable `HsmDefinitionBlob` API.
  - Updated `RomStructuresTests.cs` to match renamed fields (`TriggerEventId` -> `EventId`, `HasHistory` -> `IsHistory`).
- **Results**:
  - Total Tests: 133
  - Passed: 133
  - Failed: 0

## Code Statistics
- `HsmFlattener.cs`: ~360 lines
- `HsmEmitter.cs`: ~110 lines
- `FlattenerEmitterTests.cs`: ~340 lines
- `HsmDefinitionBlob`: Refactored to pure ROM container (Span-based access).

## Next Steps
- Batch 08: Bytecode Generation (if using bytecode) or Runtime Linker.
- Current compiler pipeline is: `Builder -> Normalizer -> Validator -> Flattener -> Emitter -> Blob`.
- The Blob is now ready for the Runtime (`HsmInstanceManager`) to consume.
