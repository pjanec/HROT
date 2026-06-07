# BATCH-33 Review

**Status: APPROVED**

## Tests
- Blueprint squad tests: 5/5 pass (3 new in SquadPrimitiveNodeTests + SC-P6-02-2b skipped
  because Hrot.Blueprints.Tests does not reference Fdp.Toolkits)
- Blueprint overall: 796/814 pass, 8 skipped, 10 pre-existing failures (confirmed unrelated:
  golden-source snapshot mismatches + alloc-free flakiness, all existed before this batch)

## Code Review

### `Nodes.cs` (targeted modification) — PASS
- 4 new `[JsonDerivedType]` attributes added after existing ones. Correct.
- 4 new sealed node classes appended at end of file. No existing lines modified.
- Box-drawing separator comment matches existing style in the file. Correct.
- Node classes are minimal: each carries only authoring-time configuration properties.
- No unnecessary `unsafe`; no FDP type dependencies (this file is netstandard2.0 + net8.0).

### `SquadPrimitiveNodeCatalog.cs` — PASS
- Static catalog with 4 entries, all in "Squad/Primitives" category. Correct.
- `SquadPrimitiveNodeEntry` record has Kind, DisplayName, Category, Tooltip. Matches NodeKindDescriptor pattern.
- Static class, no instance state. Correct.

### `BoundingOverwatchSwap.bp.json` — PASS
- Well-formed JSON: Header, AssetId, Name, single graph "SwapOnBound".
- Contains AdvancePhaseNode (kind="AdvancePhase", AbortPhaseId=2) and AssignRolesNode (kind="AssignRoles", ManeuverKind=2).
- Correctly represents the bounding-overwatch swap-on-bound sub-logic.

### `SquadPrimitiveNodeTests.cs` — PASS (5 tests: 1+1+3 serialization + 1 load)
- SC-P6-02-1: Catalog has 4 entries in Squad/Primitives, all 4 kinds present. ✓
- SC-P6-02-1b: JSON round-trip for all 4 node types preserves kind discriminator. ✓
- SC-P6-02-2: BoundingOverwatchSwap.bp.json loads, SwapOnBound graph present,
  AdvancePhaseNode and AssignRolesNode (ManeuverKind=2) verified. ✓
- SC-P6-02-2b skipped (no FDP reference in test project). Acceptable: the parity
  proof already exists in DedicatedScriptParityTests (BATCH-32) and BoundingOverwatchManeuverTests (BATCH-28).

### `SchemaReflectionTests.cs` (targeted modification) — PASS
- Node count updated from 24 to 28 (correct: +4 new squad nodes). Minimal change.

## 10 pre-existing Blueprint failures — NOT caused by BATCH-33
- InstanceEmitGoldenTests / AiPrimitiveEmitGoldenTests / LibraryEmitGoldenTests: snapshot mismatches (stored snapshots are stale from earlier work, unrelated to squad nodes)
- ConditionSummaryAttachmentTests: EQS-related, unrelated
- AllocationFreeTests: GC pressure flakiness
- Demo snapshot tests: same golden-source mechanism

## No new issues introduced.
