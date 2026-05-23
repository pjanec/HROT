# BATCH-02: Shared Infrastructure Foundation (Part 1 — Core Types)

**Batch Number:** BATCH-02  
**Tasks:** TASK-S1-01, TASK-S1-02, TASK-S1-03, TASK-S1-04, TASK-S1-05, TASK-S1-06, TASK-S1-07  
**Phase:** Phase 1 — Shared infrastructure foundation (first half)  
**Estimated Effort:** 14-18 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (DONE)

---

## Mandatory Workflow

**CRITICAL: Complete tasks in sequence, tests passing before moving on:**

1. **Project creation:** Create `Hrot.Editor.AiShared` and its test project → builds clean
2. **TASK-S1-01:** `IEditableAsset` + `AssetKind` → tests pass
3. **TASK-S1-02:** `AssetIdHash.Fnv1a32` → tests pass
4. **TASK-S1-03:** `EditorSelectionStore` + sub-selection model → tests pass
5. **TASK-S1-04:** `IAssetCatalog` + contributor pattern → tests pass
6. **TASK-S1-05:** `IReferenceCatalog` + FQN reference model → tests pass
7. **TASK-S1-06:** `FluentCSharpEmitter` framework → tests pass
8. **TASK-S1-07:** `LayoutDiscovery` + layout attribute support → tests pass
9. **Final:** ALL tests pass; main solution builds

Do NOT stop and ask for permission. Complete the entire batch and submit the report.

---

## Onboarding & Workflow

### What you're building

A new class library `Hrot.Editor.AiShared` that will be the shared foundation for BTree, HSM, and Blueprint visual editors. In this batch you build everything except UI windows (those come in BATCH-03). The result is a testable pure-C# library with no ImGui, no DDS, no Raylib dependencies.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Code Standards:** `.dev/.guides/CODE-STANDARDS.md`
3. **Task Definitions:** `.dev/blueprints-2/TASK-DETAIL.md` — Phase 1, TASK-S1-01 through TASK-S1-07
4. **Design Spec:** `.dev/blueprints-2/AI_Editor_Shared_Infrastructure.md`
   - **Read fully** — sections 1–7 map directly to this batch
   - §1: Scope and goals
   - §3: Asset identity model (Guid + FNV-1a-32)
   - §4: Action/event FQN reference model
   - §5: `EditorSelectionStore` — FULL spec including code shape
   - §6: `FluentCSharpEmitter` framework
   - §7: `[…Layout]` method discovery
5. **Existing Blueprints Editor:** Study `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/` — existing Blueprint `EditorSelectionStore.cs`, `IAssetCatalog.cs`, `AssetBrowserWindow.cs`, `InspectorWindow.cs` are references for style.

### Source Code Locations

| What | Path |
|------|------|
| New project (create) | `Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj` |
| New test project (create) | `Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj` |
| Blueprints Editor (reference) | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/` |
| Blueprints Editor csproj (reference) | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Hrot.Blueprints.Editor.csproj` |
| FDP Core Entity | `FDP/Engine/Fdp.Core/Entity.cs` |
| Main solution | `IOS-IG-SimHost.sln` |
| Hrot.Common (contains CommandLane enum) | `Hrot/Engine/Hrot.Common/` |

### Build Commands

```powershell
# Build just the new project
dotnet build Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj

# Run the new tests
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj

# Build main solution (run at end to verify no regressions)
dotnet build IOS-IG-SimHost.sln
```

### Report Submission

**When done, submit report to:**  
`.dev/blueprints-2/reports/BATCH-02-REPORT.md`

---

## Project Setup (Do This First)

### Step 1: Create project directory structure

```
Hrot/Editor/
  Hrot.Editor.AiShared/
    Hrot.Editor.AiShared.csproj
    Identity/           (AssetIdHash, AssetKind, IEditableAsset)
    Selection/          (EditorSelectionStore, IAssetSubSelection)
    Catalog/            (IAssetCatalog, IAssetCatalogContributor, AssetCatalog)
    References/         (IReferenceCatalog, IAssetSubElement, AssetReference, SubElementKind, ReferenceCatalog)
    Emit/               (IFluentCSharpEmitter, FluentCSharpEmitterBase)
    Layout/             (LayoutDiscovery, BTreeEditorLayoutBuilder, HsmEditorLayoutBuilder)
  Hrot.Editor.AiShared.Tests/
    Hrot.Editor.AiShared.Tests.csproj
    Identity/
    Selection/
    Catalog/
    References/
    Emit/
    Layout/
```

### Step 2: Project file content

**`Hrot.Editor.AiShared.csproj`:**
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Hrot.Editor.AiShared.Tests" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\FDP\Engine\Fdp.Core\Fdp.Core.csproj" />
  </ItemGroup>

</Project>
```

**`Hrot.Editor.AiShared.Tests.csproj`:**
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Hrot.Editor.AiShared\Hrot.Editor.AiShared.csproj" />
  </ItemGroup>

</Project>
```

### Step 3: Add projects to solution

Add both new projects to `IOS-IG-SimHost.sln` — follow the same sln format as existing entries.

---

## Tasks

### Task 1: TASK-S1-01 — `IEditableAsset` and `AssetKind` enum

**Spec:** Shared infra §5.2; §3.6.

**Files to create:**
- `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetKind.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Identity/IEditableAsset.cs`

**Required public surface:**

```csharp
// Identity/AssetKind.cs
namespace Hrot.Editor.AiShared;

public enum AssetKind
{
    Blueprint,
    BTree,
    Hsm,
}
```

```csharp
// Identity/IEditableAsset.cs
namespace Hrot.Editor.AiShared;

public interface IEditableAsset
{
    Guid AssetId { get; }
    string Name { get; }
    AssetKind Kind { get; }
    string SourceFilePath { get; }
    bool IsDirty { get; }
    bool IsEditorOwned { get; }
    event Action? Changed;
}
```

**Tests:**
- Verify `AssetKind` has the three values (Blueprint, BTree, Hsm) — compiler-level test
- Verify `IEditableAsset` contract — compile a stub implementing it and verify all members are required

---

### Task 2: TASK-S1-02 — `AssetIdHash.Fnv1a32`

**Spec:** Shared infra §3.3.

**File to create:** `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetIdHash.cs`

**Required public surface:**
```csharp
namespace Hrot.Editor.AiShared.Identity;

public static class AssetIdHash
{
    public static int Fnv1a32(ReadOnlySpan<byte> bytes);
}
```

**Algorithm (from spec §3.3):**
```csharp
const uint OffsetBasis = 2166136261u;
const uint Prime = 16777619u;
uint hash = OffsetBasis;
for each byte: hash ^= byte; hash *= Prime;
return unchecked((int)hash);
```

**Tests (put in `Identity/AssetIdHashTests.cs`):**
- `Fnv1a32_EmptySpan_ReturnsOffsetBasis`: empty span → `unchecked((int)2166136261u)`
- `Fnv1a32_SingleByte_A_ReturnsKnownValue`: `[0x41]` → `unchecked((int)(2166136261u ^ 0x41u) * 16777619u)`
- `Fnv1a32_KnownGuid_ReturnsKnownHash`: use Guid `f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21` (from spec example), compute hash = `AssetIdHash.Fnv1a32(guid.ToByteArray())`, then re-run and assert same value (determinism test)
- `Fnv1a32_SameInputTwice_Deterministic`: same bytes → same output always
- `Fnv1a32_DifferentInputs_DifferentHashes`: two distinct Guids → different ints (basic collision test)

---

### Task 3: TASK-S1-03 — `EditorSelectionStore` (per-asset model)

**Spec:** Shared infra §5.1 (full section with complete code shape).

**Files to create:**
- `Hrot/Editor/Hrot.Editor.AiShared/Selection/IAssetSubSelection.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Selection/SubSelectionRecords.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Selection/EditorSelectionStore.cs`

**Required public surface — copy from spec §5.1:**

The spec provides the exact class implementation. Implement it verbatim. Key points:
- `ActiveAsset` set: if value unchanged, return without event; else update + fire `OnSelectionChanged`
- `ActiveSubSelection` set: if no active asset, do nothing; if value unchanged (Equals), return; else update + fire
- `SetSubSelection(assetId, selection)`: same equality check before firing
- `SelectedEntity` set: same equality check
- `RegisterOpenAsset` / `UnregisterOpenAsset` / `Forget`: as in spec
- Sub-selection records: `BlueprintNodeSelection`, `BTreeNodeSelection`, `HsmStateSelection`, `HsmTransitionSelection`, `HsmRegionSelection`

**Important:** `Entity` comes from `Fdp.Core` (namespace `Fdp`). Look at `FDP/Engine/Fdp.Core/Entity.cs` for the type.

**Tests (put in `Selection/EditorSelectionStoreTests.cs`) — minimum 15:**

- `ActiveAsset_SetToSameValue_DoesNotFireEvent`
- `ActiveAsset_SetToDifferentValue_FiresEvent`
- `ActiveSubSelection_WithNoActiveAsset_SetIsNoOp`
- `ActiveSubSelection_FiresEvent_WhenChanged`
- `ActiveSubSelection_DoesNotFire_WhenValueUnchanged`: set same `BTreeNodeSelection(guid)` twice — event fires only once
- `SetSubSelection_NonActiveAsset_StoresWithoutChangingActiveAsset`
- `SetSubSelection_SameValue_DoesNotFire`
- `GetSubSelection_ReturnsNullForUnknownAsset`
- `GetSubSelection_ReturnsStoredValue`
- `PerAssetIsolation_DifferentAssets_HaveIndependentSubSelections`
- `ActiveAssetSwitch_SubSelectionFollowsActiveAsset`: set A as active, set sub-selection A; switch to asset B; `ActiveSubSelection` is null (no sub-selection for B yet)
- `Forget_RemovesSubSelection_AndFiresEvent`
- `RegisterOpenAsset_DoesNotFireEvent`
- `SelectedEntity_SetToSameValue_DoesNotFireEvent`
- `SelectedEntity_SetToDifferentValue_FiresEvent`
- `OnSelectionChanged_FiresExactlyOnce_PerMutation`: a single `ActiveAsset` change fires exactly one event

---

### Task 4: TASK-S1-04 — `IAssetCatalog` and contributor pattern

**Spec:** Shared infra §3.6.

**Files to create:**
- `Hrot/Editor/Hrot.Editor.AiShared/Catalog/IAssetCatalog.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Catalog/IAssetCatalogContributor.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Catalog/AssetCatalog.cs`

**Required public surface (from spec §3.6):**

```csharp
// Interfaces
public interface IAssetCatalog
{
    IReadOnlyList<IEditableAsset> All { get; }
    IEditableAsset? FindByAssetId(Guid assetId);
    IEditableAsset? FindByName(string name);
    IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId);
    event Action? Changed;
}

public interface IAssetCatalogContributor
{
    AssetKind Kind { get; }
    IReadOnlyList<IEditableAsset> Enumerate();
    event Action? ContributorChanged;  // fires when the contributor's asset list changes
}
```

**`AssetCatalog` implementation:**
- Accepts `IReadOnlyList<IAssetCatalogContributor>` in constructor (or add-contributor pattern)
- Merges all contributors via `Enumerate()` on demand (rebuild on every `Changed` trigger)
- Subscribes to each contributor's `ContributorChanged`; on fire, rebuilds and fires own `Changed`
- `FindByAssetId`: dictionary lookup
- `FindByName`: case-sensitive first match
- `WhereDependsOn`: return empty list for now (Phase 5/6 will fill it in)

**Tests (put in `Catalog/AssetCatalogTests.cs`) — minimum 10:**

- `All_MergesContributors_WhenContributorsRegistered`
- `All_IsEmpty_WithNoContributors`
- `FindByAssetId_ReturnsAsset_WhenExists`
- `FindByAssetId_ReturnsNull_WhenNotFound`
- `FindByName_ReturnsFirst_OnMatch`
- `FindByName_ReturnsNull_WhenNotFound`
- `Changed_FiresOnce_WhenContributorChanges`
- `Catalog_Rebuilds_AfterContributorChange`
- `WhereDependsOn_ReturnsEmpty_Initially`
- `MultipleContributors_AllAssetsVisible`

---

### Task 5: TASK-S1-05 — `IReferenceCatalog` (FQN reference model)

**Spec:** Shared infra §4.3.

**Files to create:**
- `Hrot/Editor/Hrot.Editor.AiShared/References/SubElementKind.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/References/IAssetSubElement.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/References/AssetReference.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/References/IReferenceCatalog.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/References/ReferenceCatalog.cs`

**Required public surface (from spec §4.3):**

```csharp
public enum SubElementKind
{
    ActionFqn,
    ConditionFqn,
    GuardFqn,
    EventName,
    AssetReference,
    BlackboardField,
}

public interface IAssetSubElement
{
    string Key { get; }
    SubElementKind Kind { get; }
    string DisplayName { get; }
    Guid? SourceAssetId { get; }
}

public sealed record AssetReference(
    Guid HostAssetId,
    AssetKind HostKind,
    Guid HostElementId,
    string HostDisplayPath,
    string TargetKey,
    SubElementKind TargetKind);

public interface IReferenceCatalog
{
    IReadOnlyList<IAssetSubElement> AllElements { get; }
    IAssetSubElement? FindElement(string key);
    IReadOnlyList<AssetReference> FindReferences(string targetKey);
    IReadOnlyList<AssetReference> AllReferencesIn(Guid hostAssetId);
    event Action? Changed;
}
```

**`ReferenceCatalog` implementation:**
- Constructor takes `IAssetCatalog` (subscribes to its `Changed` event to trigger rebuild)
- `IAssetCatalogContributor` pattern: each contributor may also implement an `IReferenceCatalogContributor` interface (define this too: `IReadOnlyList<IAssetSubElement> EnumerateElements(IEditableAsset asset)` + `IReadOnlyList<AssetReference> EnumerateReferences(IEditableAsset asset)`)
- Actually, for Phase 1 without any host editors, the `ReferenceCatalog` just needs to store and query; contributors are injected in Phase 5/6. Implement the infrastructure but let it be populated programmatically via a `Contribute(IAssetSubElement element, IReadOnlyList<AssetReference> refs)` method (or through constructors) for testing.
- `FindReferences(targetKey)`: multi-index lookup, return all `AssetReference` records where `TargetKey == targetKey`
- `AllReferencesIn(hostAssetId)`: return all references where `HostAssetId == hostAssetId`

**Tests (put in `References/ReferenceCatalogTests.cs`) — minimum 10:**

- `FindElement_ReturnsElement_WhenRegistered`
- `FindElement_ReturnsNull_WhenNotFound`
- `FindReferences_ReturnsRefs_WhenTargetMatches`
- `FindReferences_ReturnsEmpty_WhenNoMatch`
- `AllReferencesIn_ReturnsOnlyRefsBelongingToHost`
- `Changed_Fires_WhenRebuilt`
- `AllElements_IsEmpty_Initially`
- `AllElements_ContainsAll_RegisteredElements`
- `MultipleReferences_ToSameElement_AllReturned`
- `AssetReference_Record_Equality`

---

### Task 6: TASK-S1-06 — `FluentCSharpEmitter` framework

**Spec:** Shared infra §6.

**Files to create:**
- `Hrot/Editor/Hrot.Editor.AiShared/Emit/IFluentCSharpEmitter.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Emit/FluentCSharpEmitterBase.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Emit/UsingDirectiveSet.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Emit/EmitterOptions.cs`

**Required public surface:**

```csharp
// IFluentCSharpEmitter.cs
namespace Hrot.Editor.AiShared.Emit;

public interface IFluentCSharpEmitter<TAsset>
{
    string Emit(TAsset asset);
}
```

**`FluentCSharpEmitterBase` provides the infrastructure per §6.2–6.6:**

```csharp
namespace Hrot.Editor.AiShared.Emit;

// Base class for per-asset emitters. Subclasses call helper methods to build
// the output string; the base class handles using-ordering, marker, and file policy.
public abstract class FluentCSharpEmitterBase
{
    public const string EditorGeneratedMarker =
        "// HROT_EDITOR_GENERATED — manual edits to this file will be overwritten by the AI editor on next save.";

    /// <summary>Produces the complete .cs file content for the given asset, deterministically.</summary>
    protected abstract string EmitCore(IEditableAsset asset);

    /// <summary>Sorts using directives: System.* first (alphabetical), then rest (alphabetical).</summary>
    protected static IReadOnlyList<string> SortUsings(IEnumerable<string> namespaces);

    /// <summary>Builds the marker header lines for a file.</summary>
    protected static string BuildHeader(Guid assetId);

    /// <summary>
    /// Writes the file atomically (*.tmp then File.Move). No-op if content unchanged.
    /// Returns true if the file was written, false if identical.
    /// </summary>
    public static bool WriteAtomic(string filePath, string content);
}
```

**`UsingDirectiveSet`:** maintains the set of namespaces; knows the sort order rule.

**`EmitterOptions`:** configures `NewLine` (default `"\r\n"` on Windows, `"\n"` on Unix), indentation string (default `"    "`).

**Key rules to enforce:**
- `using` directives: `System.*` first (sorted alphabetically within each group), blank line, then all non-System (sorted alphabetically)
- `SortUsings(["Hrot.Foo", "System.IO", "Fbt", "System"])` → `["System", "System.IO", "", "Fbt", "Hrot.Foo"]` (empty string = blank line separator)
- Guid format: always `guid.ToString("D")` (8-4-4-4-12 lowercase hex)
- `WriteAtomic`: write to `.tmp` then `File.Move` over target; skip write if content matches existing

**Tests (put in `Emit/UsingDirectiveSetTests.cs` and `Emit/FluentCSharpEmitterBaseTests.cs`) — minimum 12:**

- `SortUsings_SystemFirst_ThenOthers`
- `SortUsings_SystemAlphabetical_WithinGroup`
- `SortUsings_NonSystemAlphabetical_WithinGroup`
- `SortUsings_EmptyInput_ReturnsEmpty`
- `SortUsings_SystemOnly_NoBlankLine`
- `SortUsings_NonSystemOnly_NoLeadingBlankLine`
- `SortUsings_Mixed_HasBlankLineBetweenGroups`
- `BuildHeader_ContainsMarker`
- `BuildHeader_ContainsAssetId`
- `BuildHeader_AssetIdFormatIs_D_Format`: header contains `guid.ToString("D")`
- `WriteAtomic_WritesFile_WhenContentDiffers` (use a temp file)
- `WriteAtomic_NoOp_WhenContentIdentical` (no file modification timestamp change)

---

### Task 7: TASK-S1-07 — `LayoutDiscovery` + layout attribute framework

**Spec:** Shared infra §7 (note: section numbers shift — read section starting "7. [...Layout] method").

**Files to create:**
- `Hrot/Editor/Hrot.Editor.AiShared/Layout/LayoutDiscovery.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Layout/BTreeLayoutAttribute.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Layout/HsmLayoutAttribute.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Layout/BlueprintLayoutAttribute.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Layout/BTreeEditorLayout.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Layout/BTreeEditorLayoutBuilder.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Layout/HsmEditorLayout.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Layout/HsmEditorLayoutBuilder.cs`

**Required public surface (from spec §7.3, §7.6):**

Layout attribute: each has a `string AssetId { get; }` property:
```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class BTreeLayoutAttribute : Attribute
{
    public BTreeLayoutAttribute(string assetId) => AssetId = assetId;
    public string AssetId { get; }
}
// Same pattern for HsmLayoutAttribute, BlueprintLayoutAttribute
```

`LayoutDiscovery`:
```csharp
public static class LayoutDiscovery
{
    // Finds the [TAttr]-decorated static method matching assetId in the assembly.
    // Returns null if not found.
    public static TLayout? TryGetLayout<TAttr, TLayout>(Assembly assembly, Guid assetId)
        where TAttr : Attribute
        where TLayout : class;
}
```

`BTreeEditorLayout` and `BTreeEditorLayoutBuilder`:
- `BTreeEditorLayout`: immutable result type containing canvas data + per-node layout entries
  - `Vector2 PanOffset { get; }`
  - `float ZoomLevel { get; }`
  - `IReadOnlyDictionary<Guid, NodeLayoutEntry> Nodes { get; }`
- `NodeLayoutEntry`: `Vector2 Position`, `Vector2? SizeOverride`, `string? Comment`, `bool Collapsed`, `string? Color`, `string? ExpressionTarget`
- `BTreeEditorLayoutBuilder`: fluent builder with `Canvas(...)` and `Node(string visualId, ...)` and `Build()`

`HsmEditorLayout` and `HsmEditorLayoutBuilder`:
- Similar, but with `States`, `Transitions` (with `Vector2[] Waypoints`), and `Regions`
- `StateLayoutEntry`, `TransitionLayoutEntry`, `RegionLayoutEntry`

**Requirements:**
- `TryGetLayout<BTreeLayoutAttribute, BTreeEditorLayout>(assembly, guid)` scans all types, all public static methods, matches if `BTreeLayoutAttribute.AssetId` parses to `assetId`, invokes the method, returns result
- If no match, returns null
- If multiple matches (duplicate entries), returns first (spec §6.4)

**Tests (put in `Layout/LayoutDiscoveryTests.cs`) — minimum 10:**

In test, define inline static methods decorated with `[BTreeLayout("...")]` in a test helper class, then use `Assembly.GetExecutingAssembly()`:

- `TryGetLayout_ReturnsLayout_WhenMethodExists`
- `TryGetLayout_ReturnsNull_WhenMethodDoesNotExist`
- `TryGetLayout_ReturnsNull_WhenAssetIdDoesNotMatch`
- `TryGetLayout_ReturnsNull_WhenWrongAttributeType`
- `BTreeEditorLayoutBuilder_Canvas_SetsCorrectPanOffset`
- `BTreeEditorLayoutBuilder_Canvas_SetsCorrectZoom`
- `BTreeEditorLayoutBuilder_Node_StoredByGuid`
- `BTreeEditorLayoutBuilder_NodeWithAllFields_StoredCorrectly`
- `HsmEditorLayoutBuilder_State_StoredByStableId`
- `HsmEditorLayoutBuilder_Transition_StoredByVisualId`

---

## Testing Requirements

- All tests in `Hrot/Editor/Hrot.Editor.AiShared.Tests/`
- Minimum: **67 new tests** (sum of the per-task minimums)
- ALL tests must pass
- Main solution must still build after adding the new projects

## Quality Standards

**NOT ACCEPTABLE:**
- Tests that only check "object was created"
- Tests for `EditorSelectionStore` that don't verify the "no duplicate event" contract
- `FNV-1a-32` tests that don't use known-value vectors

**REQUIRED:**
- `EditorSelectionStore` tests MUST verify that setting the same value twice fires the event only once
- `AssetIdHash` tests MUST include at least one known-output vector (compute expected value manually)
- `SortUsings` tests MUST cover the blank-line-separator rule between System and non-System groups

---

## Success Criteria

This batch is DONE when:

- [ ] New project `Hrot.Editor.AiShared` created and builds clean
- [ ] New test project `Hrot.Editor.AiShared.Tests` created and builds clean
- [ ] Both projects added to `IOS-IG-SimHost.sln`
- [ ] TASK-S1-01: `IEditableAsset` + `AssetKind` implemented
- [ ] TASK-S1-02: `AssetIdHash.Fnv1a32` with 5 tests
- [ ] TASK-S1-03: `EditorSelectionStore` with 16+ tests (per-asset isolation, event dedup)
- [ ] TASK-S1-04: `IAssetCatalog` + `AssetCatalog` with 10+ tests
- [ ] TASK-S1-05: `IReferenceCatalog` + `ReferenceCatalog` with 10+ tests
- [ ] TASK-S1-06: `FluentCSharpEmitter` framework with 12+ tests
- [ ] TASK-S1-07: `LayoutDiscovery` + builders with 10+ tests
- [ ] `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj` — ALL PASS
- [ ] `dotnet build IOS-IG-SimHost.sln` — builds clean (no regressions)
- [ ] Report submitted at `.dev/blueprints-2/reports/BATCH-02-REPORT.md`

---

## Common Pitfalls

- The `Entity` type is in `Fdp.Core` (namespace `Fdp`). The project reference to `Fdp.Core` is already in the template csproj.
- `EditorSelectionStore.ActiveSubSelection` uses `Equals()` for equality; `IAssetSubSelection` records have structural equality via C# `record` — verify this is correct for `BTreeNodeSelection(Guid)` etc.
- `LayoutDiscovery` uses reflection; in tests, decorate static methods in a test-local helper class so `Assembly.GetExecutingAssembly()` finds them.
- `FluentCSharpEmitterBase.WriteAtomic` uses file I/O — test it with `System.IO.Path.GetTempFileName()` to avoid polluting the source tree.
- Do NOT add Raylib, ImGui, or DDS dependencies to `Hrot.Editor.AiShared`. It must stay a pure netstandard/net8 library.

---

## Developer Insights Report Template

Use `.dev/.guides/BATCH-REPORT-TEMPLATE.md`.

**Questions to answer in your report:**

1. What was the trickiest part of `EditorSelectionStore` to implement correctly? Did the "no duplicate event on same value" contract require any non-obvious care?
2. For `ReferenceCatalog` — how did you structure the contribution/query surface for Phase 1 (before any host editors exist)? What will Phase 5 need to plug in?
3. For `LayoutDiscovery` — what edge cases did you handle for the reflection scan?
4. For `FluentCSharpEmitterBase.WriteAtomic` — did you handle the case where the `.tmp` write fails partway?
5. Any design decisions beyond the spec?

---

## Reference Materials

- **Task Defs:** `.dev/blueprints-2/TASK-DETAIL.md` — Phase 1 tasks
- **Design Spec:** `.dev/blueprints-2/AI_Editor_Shared_Infrastructure.md` — §1–7
- **Existing Blueprints Editor:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/` (style reference)
