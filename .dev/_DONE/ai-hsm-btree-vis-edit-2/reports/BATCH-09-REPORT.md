# BATCH-09 REPORT — Emit AssetId in [BTreeDefinition] (REVIEW-BT F1)

**Task:** TASK-BT-09 — do for BTree exactly what HSM already does: carry AssetId through codegen so JSON/assembly dedupe works.

**Date:** 2026-06-12

---

## Changes Made

### 1. `BTreeDefinitionAttribute.cs` — add `AssetId` property (mirror HsmDefinitionAttribute)

**File:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Attributes/BTreeDefinitionAttribute.cs`

```diff
+ /// <summary>Stable editor asset GUID (8-4-4-4-12). Set by the editor codegen; null for hand-authored.</summary>
+ public string? AssetId { get; set; }
```

Additive, mirroring `HsmDefinitionAttribute.AssetId`. No breaking changes — existing code that doesn't set it continues to work with `null` default.

### 2. `BTreeEmitCore.cs` — emit AssetId in `[BTreeDefinition]` (mirror HsmEmitCore:468)

**File:** `Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeEmitCore.cs`

- Replaced `[BTreeDefinition("{dto.Name}")]` with `[BTreeDefinition("{dto.Name}", AssetId = "{dto.AssetId:D}")]`
- Added `QuoteStr(string s)` helper (mirror HsmEmitCore:554)
- Removed the stale comment `// Note: BTreeDefinitionAttribute does not have an AssetId property`

### 3. `BTreeAssetContributor.cs` — prefer attribute AssetId (mirror HsmAssetContributor)

**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Catalog/BTreeAssetContributor.cs`

```diff
- var assetId = AssetIdHasher.FromName(defAttr.TreeName);
+ var assetId = !string.IsNullOrWhiteSpace(defAttr.AssetId) && Guid.TryParse(defAttr.AssetId, out var parsed)
+     ? parsed
+     : AssetIdHasher.FromName(defAttr.TreeName);
```

Exact same idiom as `HsmAssetContributor:46-49`.

### 4. CombatShowcase.g.cs — regenerated

**File:** `Hrot/Subsystems/Hrot.AI.Behaviors/obj/GeneratedFiles/.../CombatShowcase.g.cs`

Now emits:
```csharp
[BTreeDefinition("CombatShowcase", AssetId = "aaaaaaaa-0000-0000-0000-000000000001")]
```

### 5. Tests added/updated

**ByteIdenticalGateTests.cs** (`Hrot.AiEditor.Persistence.Tests`):
- Updated `BTree_CoreEmit_MatchesCommittedCs_SampleScout` to check for `[BTreeDefinition("SampleScout", AssetId = "` (matching HSM pattern)
- Added `BTree_EmitTopologyCore_EmitsAssetId_InBTreeDefinitionAttribute` — explicit emit test verifying AssetId in output

**BTreeAssetContributorTests.cs** (`Hrot.BTree.Editor.Tests/Catalog/` — NEW):
- `LoadFrom_UsesAttributeAssetId_WhenPresent` — fixture `[BTreeDefinition("Bt09Fixture", AssetId = "12345678-0000-0000-0000-0000000000aa")]` → asset's AssetId == that GUID, NOT FromName
- `LoadFrom_FallsBackToFromName_WhenAssetIdAbsent` — fixture `[BTreeDefinition("Bt09NoAssetIdFixture")]` → asset's AssetId == FromName("Bt09NoAssetIdFixture")
- Includes two static fixture classes (`Bt09FixtureHost`, `Bt09NoAssetIdFixtureHost`) in the test assembly

---

## Test Results

### Fbt.Tests (FastBTree)
| Total | Passed | Failed | Skipped |
|-------|--------|--------|---------|
| 233   | 224    | 9*     | 0       |

\* **9 failures — ALL PRE-EXISTING** (verified by stashing changes and re-running):
- `GeneratorOutputTests.GeneratedRegistrar_RegisterAll_PopulatesRegistry`
- `BuilderValidationTests.DtoTooLarge_ThrowsBehaviorTreeBuildException`
- `SharedAiGeneratorTests` × 4
- `AutoDiscoveryTests` × 3

These failures exist on the base commit (`411dda39`) without any BATCH-09 changes. The `BTreeDefinitionAttribute.AssetId` addition is purely additive and does not affect the FastBTree source generator (`BTreeDefinitionGenerator` only reads the constructor argument `TreeName`).

### Hrot.AiEditor.Persistence.Tests
| Total | Passed | Failed | Skipped |
|-------|--------|--------|---------|
| 113   | 113    | 0      | 0       |

✅ All pass, including:
- `BTree_CoreEmit_IsByteIdentical_ToFluentEmitter` — adapter→core still byte-identical with new AssetId format
- `BTree_EmitTopologyCore_EmitsAssetId_InBTreeDefinitionAttribute` — new test passes
- `BTree_CoreEmit_MatchesCommittedCs_SampleScout` — updated assertion passes
- All WriteAtomic determinism tests pass

### Hrot.AiEditor.Generators.Tests
| Total | Passed | Failed | Skipped |
|-------|--------|--------|---------|
| 41    | 39     | 2**    | 0       |

\*\* **2 failures — ALL PRE-EXISTING** (verified by stashing changes and re-running):
- `BTree_SampleScout_MigrationJson_RoundTrips_And_CarriesLayout` — JSON byte-stability test fails because committed JSON has `$meta` field not preserved in Deserialize→Serialize round-trip
- `Hsm_SampleGuard_MigrationJson_RoundTrips_And_CarriesLayout` — same `$meta` issue for HSM

Both failures exist on the base commit. Not caused by BATCH-09 changes.

### Hrot.BTree.Editor.Tests
| Total | Passed | Failed | Skipped |
|-------|--------|--------|---------|
| 493   | 493    | 0      | 0       |

✅ All pass, including:
- `LoadFrom_UsesAttributeAssetId_WhenPresent` — new test passes
- `LoadFrom_FallsBackToFromName_WhenAssetIdAbsent` — new test passes
- `BTreeAssetContributor_LoadFrom_DiscoversSampleScout` — still discovers
- `BTreeAssetContributor_LoadFrom_SampleScout_HasCorrectAssetId` — FromName fallback still works (SampleScout's committed attribute has no AssetId)
- All 493 tests green

---

## Success Criteria

- [x] `dotnet build` — 0 errors, 0 new warnings across all touched projects
- [x] **Failed: 0** in Hrot.AiEditor.Persistence.Tests (113/113)
- [x] **Failed: 0** in Hrot.BTree.Editor.Tests (493/493)
- [x] Fbt.Tests: 9 pre-existing failures, no new failures introduced
- [x] Hrot.AiEditor.Generators.Tests: 2 pre-existing failures, no new failures introduced
- [x] `CombatShowcase.g.cs` emits `[BTreeDefinition("CombatShowcase", AssetId = "aaaaaaaa-0000-0000-0000-000000000001")]`
- [x] Contributor uses attribute AssetId when present, else FromName fallback
- [x] No tests deleted or weakened; new tests added for both emit and contributor

---

## Root Cause Resolution

**Before:** `BTreeDefinitionAttribute` had no `AssetId` property → `BTreeEmitCore` didn't emit one → `BTreeAssetContributor` always used `FromName(TreeName)` → CombatShowcase's real AssetId (`aaaaaaaa-…`) ≠ `FromName("CombatShowcase")` → JSON-loaded and assembly-reflected instances had different AssetIds → dedupe failed → duplicate CombatShowcase in browser.

**After:** Mirror HSM exactly — attribute carries AssetId, emit core writes it, contributor reads it. JSON and assembly paths now share the same AssetId → dedupe works.
