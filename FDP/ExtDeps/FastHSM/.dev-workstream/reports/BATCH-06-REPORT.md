# BATCH-06 Report: Compiler - Normalizer & Validator

## Status
**Complete**

## Implementation Details

### 1. HsmNormalizer
Implemented `src/Fhsm.Compiler/HsmNormalizer.cs`.
- **Flat Indexing**: Performs BFS on the state tree. Starts Root at 0. Ensures States are indexed in contiguous blocks for cache efficiency.
- **Depth Calculation**: Recursively computes depth. Root=0.
- **Initial State Resolution**: Iterates all states. If a composite state has no `IsInitial` child, sets the first child as Initial.
- **History Slot Assignment**: Implemented the **Architect Critical** requirement: Sorts history states by `StableId` (GUID) before assigning slots. This ensures `HistorySlotIndex` is stable across recompiles even if state names change, supporting Hot Reload.

### 2. HsmGraphValidator
Implemented `src/Fhsm.Compiler/HsmGraphValidator.cs`.
- **Logic**: Enforces ~20 rules across structure, transitions, and logic.
- **Key Checks**:
    - **Orphans**: States not reachable from Root.
    - **Cycles**: Circular parent references.
    - **Initial States**: Enforces "Exactly One" initial for composite states (after normalization).
    - **History**: Ensures history states have composite parents.
    - **Registration**: Ensures all string references (Guards, Actions, Events) exist in the lookup tables.

### 3. StateNode Updates
- Added `ushort HistorySlotIndex` to `StateNode.cs`.

### 4. Visibility Changes
- Set `HsmNormalizer` and `HsmGraphValidator` to `internal` as they are internal compiler pipeline stages. `InternalsVisibleTo` allows testing.

## Test Results
- **Total Tests**: 120 (107 existing + 13 new scenarios covering multiple cases).
- **New Tests**:
    - BFS Index verification.
    - Initial state resolution (implicit vs explicit).
    - History slot sorting by GUID.
    - Orphan detection.
    - Cycle detection.
    - Missing registrations.
- **Status**: All passing.

## Next Steps
- Implement **Flattening** (Graph -> Blob).
- Implement **Emission** (Blob -> byte[] or C# code).
