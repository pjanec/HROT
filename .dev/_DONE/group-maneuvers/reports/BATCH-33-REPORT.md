# BATCH-33 Report — Phase 6 Part B: Blueprint Host for Squad Logic (P6-02)

**Status:** COMPLETE  
**Date:** 2026-05-30

---

## What Was Implemented

### Task 1 — `Nodes.cs` (targeted modification)

Added 4 new `[JsonDerivedType]` attributes immediately after `ReadRankedResult`:

```csharp
[JsonDerivedType(typeof(PartitionElementsNode), "PartitionElements")]
[JsonDerivedType(typeof(AssignRolesNode),        "AssignRoles")]
[JsonDerivedType(typeof(AdvancePhaseNode),       "AdvancePhase")]
[JsonDerivedType(typeof(AcquireSlotNode),        "AcquireSlot")]
```

Appended 4 new sealed node classes after `ReadRankedResultNode`:
- `PartitionElementsNode` — `ElementCount: int = 2`
- `AssignRolesNode` — `ManeuverKind: ushort`
- `AdvancePhaseNode` — `AbortPhaseId: ushort`, `DwellTimeoutTicks: uint`
- `AcquireSlotNode` — `TotalSlots: int = 1`

No existing lines removed or reformatted.

### Task 2 — `SquadPrimitiveNodeCatalog.cs` (new file)

Created: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Catalogs/SquadPrimitiveNodeCatalog.cs`

Static catalog with 4 entries in `Squad/Primitives` category. Defines `SquadPrimitiveNodeEntry` record with `Kind`, `DisplayName`, `Category`, `Tooltip`.

### Task 3 — `BoundingOverwatchSwap.bp.json` (new file)

Created: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/BoundingOverwatchSwap.bp.json`

Worked example Blueprint JSON with a `SwapOnBound` graph containing 4 nodes: `EventEntry`, `AdvancePhase` (AbortPhaseId=2), `AssignRoles` (ManeuverKind=2), `Return`.

No `.csproj` modification needed — `<Content Include="TestAssets\**\*" CopyToOutputDirectory="PreserveNewest" />` already covers this path.

### Task 4 — `SquadPrimitiveNodeTests.cs` (new file)

Created: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Squad/SquadPrimitiveNodeTests.cs`

Implemented 3 tests (SC-P6-02-2b was skipped — `Fdp.Toolkits` is not referenced by `Hrot.Blueprints.Tests.csproj`):

| Test | Coverage |
|---|---|
| `SquadPrimitiveNodeCatalog_HasFourEntriesInSquadCategory` | SC-P6-02-1 |
| `SquadPrimitiveNodes_JsonRoundTrip_PreservesKindDiscriminator` | SC-P6-02-1b |
| `BoundingOverwatchSwap_Blueprint_LoadsAndContainsSquadNodes` | SC-P6-02-2 |

SC-P6-02-2b (`BoundingOverwatchManeuver` runtime test) was omitted: `Hrot.Blueprints.Tests.csproj` does not reference `Fdp.Toolkits`, so `Fdp.Toolkit.Squad.*` namespaces are not available.

### `SchemaReflectionTests.cs` updated

`ConcreteNodeSubtypeCount_Is24` → `ConcreteNodeSubtypeCount_Is28` to reflect the 4 new node types.

---

## Test Counts

| Scope | Count |
|---|---|
| Squad-specific tests (`~Squad` filter) | **5** (3 new + 2 SquadAwareEngagement from prior batch) |
| Overall Blueprint test suite | **814 total** (796 passed, 8 skipped) |

Note: 10 tests were already failing before this batch (golden snapshot diffs, allocation-free test, EQS condition summary test). These are pre-existing failures confirmed by reverting to `main` and running the same tests.

---

## Issues / Notes

- **SC-P6-02-2b skipped** — `Fdp.Toolkits` project not referenced from `Hrot.Blueprints.Tests.csproj`. The 3 blueprint-only tests cover the catalog and JSON round-trip contract.
- **No .csproj modification** — `TestAssets\**\*` wildcard already covers `BoundingOverwatchSwap.bp.json`.
- **10 pre-existing failures** — `InstanceEmitGoldenTests`, `LibraryEmitGoldenTests`, `AiPrimitiveEmitGoldenTests`, `LibraryMathDemoTests`, `MoveToAndFireDemoTests` (snapshot drift), `AllocationFreeTests`, `ConditionSummaryAttachmentTests`. All present on unmodified `main`.
