# BATCH-HS-08 REPORT — Authoring-loop round-trip test

**Date:** 2026-06-13
**Task:** TASK-HS-08 (headless portion)
**Status:** ✅ COMPLETE — 2 new tests, 0 failures, 0 new failures

---

## Real save/open path used

| Direction | Step 1 | Step 2 |
|-----------|--------|--------|
| **Save** | `HsmAssetMapper.ToDto(HsmAsset)` → `HsmAssetDto` | `HsmJsonServices.Serialize(HsmAssetDto)` → JSON string |
| **Open** | `HsmJsonServices.Deserialize(string)` → `HsmAssetDto` | `HsmAssetMapper.ToModel(HsmAssetDto, sourceFilePath, isEditorOwned)` → `HsmAsset` |

Key files:
- `Hrot.Hsm.Editor/Persistence/HsmAssetMapper.cs` — `ToDto()` (line 23) and `ToModel()` (line 181)
- `Hrot.AiEditor.Persistence/Hsm/HsmJsonServices.cs` — `Serialize()` (line 43) and `Deserialize()` (line 54)

This is the **exact inverse** of the recipe/open path used by `HsmNewAssetService` and `HsmDocumentFactory`. No hand-rolled serializer was used.

---

## Tests added

### `HsmAuthoringRoundTripTests.cs`
Location: `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmAuthoringRoundTripTests.cs`

#### Test 1: `AuthoringRoundTrip_CreateEditSaveReopen_PreservesTopologyAndLayout`
Flow:
1. **Create** empty `HsmAsset` + `HsmCommandSink`
2. **Edit** via command sink:
   - `AddNode` ×4: composite-ish parent (`Simple`), 2 children (`Simple`), 1 final state
   - `ChangeParent` child1 under parent → parent becomes `Composite`
   - `AddLink` from child1 → child2 (transition)
   - `SetContainerCollapsed` on the composite
   - `MoveNodes` to set 4 distinct positions
3. **Save**: `ToDto` → `Serialize`
4. **Reopen**: `Deserialize` → `ToModel`
5. **Assert preserved:**
   - State count
   - Each state's `StableId`, `Name`, kind flags (`IsFinal`, `IsInitial`, etc.)
   - Parent/child topology (`Children`, `Parent`, `Kind = Composite`)
   - Transition count, `VisualId`, `Source.StableId`, `Target.StableId`
   - No dangling references (Source/Target resolve to non-null `StateNode`s)
   - Layout: `Position` (4 positions) and `IsCollapsed == true`
   - All fields survived — **no persistence gaps found**

#### Test 2: `StarterRecipeRoundTrip_SaveReopen_PreservesSingleInitialState`
Flow:
1. `HsmNewAssetService.MakeStarterDto()` → DTO
2. `ToModel` → live `HsmAsset`
3. Save → reopen (full ToDto→Serialize→Deserialize→ToModel path)
4. Assert:
   - State count (2: `__Root` + `InitState`)
   - Region count (1)
   - Single `IsInitial` state with preserved `StableId`, `Name`, `Position`
   - Parent/child topology: `InitState.Parent == __Root` and `__Root.Children` contains it

---

## Fields asserted preserved

| Field | Survives round trip? | Asserted? |
|-------|---------------------|-----------|
| `StableId` | ✅ Yes (`Guid` via DTO) | Yes |
| `Name` | ✅ Yes | Yes |
| `IsInitial` | ✅ Yes | Yes |
| `IsFinal` | ✅ Yes | Yes |
| `IsHistory` | ✅ Yes | Yes |
| `IsDeepHistory` | ✅ Yes | Yes |
| `IsParallel` | ✅ Yes | Yes |
| `Position` (X/Y) | ✅ Yes | Yes |
| `IsCollapsed` | ✅ Yes | Yes |
| `VisualId` (transition) | ✅ Yes | Yes |
| `Source.StableId` / `Target.StableId` | ✅ Yes | Yes |
| Parent/child topology | ✅ Yes | Yes |

**No field failed to survive the real save path.** All assertions hold — no persistence gaps triggered.

---

## Before/after counts

| Metric | Before | After | Delta |
|--------|--------|-------|-------|
| Total tests | 454 | **456** | +2 |
| Passed | 454 | **456** | +2 |
| Failed | 0 | **0** | 0 |
| Build errors | 0 | **0** | 0 |

---

## What was NOT touched

- ❌ `EditorSubsystem.cs` — not touched (VE-DEBT-005 deferred)
- ❌ Window classes (`HsmEventsWindow.cs`, `HsmGlobalsStrip.cs`) — not touched
- ❌ `HsmCommandSink.cs` — not touched
- ❌ `HsmAsset.cs` / model behavior — not touched
- ❌ `HsmAssetMapper.cs` / persistence code — not touched
- ❌ Any renderers — not touched
- ✅ Only one new file added: `HsmAuthoringRoundTripTests.cs`
- ✅ No read-only accessors needed on the model — existing public API was sufficient

---

## Verification

```
dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj  → 0 errors
dotnet test  Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests                   → Failed: 0, Passed: 456
```

No `BLUEPRINT_REGENERATE_SNAPSHOTS` env var was set.

## No commit

Per working agreement, no commit was made.
