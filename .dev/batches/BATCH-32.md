# BATCH-32 Instructions

## Tasks
- **HS-S1-23**: `HsmAssetContributor` — `IAssetCatalogContributor` reflecting `[HsmDefinition]` methods
- **HS-S1-24**: `HsmEditorHostServices` — `IEditorHostServices` implementation wiring all HSM host components
- **HS-S1-25**: `HsmQuickReloadHasher` — quick reload tier classifier using `HsmDefinitionBlob.Header` hashes

## Mandatory reading BEFORE writing any code

Read these files in full to understand the exact patterns and APIs:

1. `Hrot/Subsystems/AI/Hrot.BTree.Editor/Catalog/BTreeAssetContributor.cs` — exact pattern for contributor
2. `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeEditorHostServices.cs` — exact pattern for host services
3. `Hrot/Subsystems/AI/Hrot.BTree.Editor/HotReload/BTreeQuickReloadHasher.cs` — exact pattern for reload hasher
4. `Hrot/Editor/Hrot.Editor.AiShared/Catalog/IAssetCatalogContributor.cs` — the interface to implement
5. `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetIdHasher.cs` — `AssetIdHasher.FromName`
6. `Hrot/Editor/Hrot.Editor.AiShared/Layout/LayoutDiscovery.cs` — `LayoutDiscovery.TryGetLayout`
7. `Hrot/Editor/Hrot.Editor.AiShared/Layout/HsmEditorLayout.cs` — layout type
8. `Hrot/Editor/Hrot.Editor.AiShared/HotReload/HotReloadClassifier.cs` — classifier helper
9. `Hrot/Editor/Hrot.Editor.AiShared/HotReload/HotReloadTier.cs` — `HotReloadTier` enum
10. `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Attributes/HsmDefinitionAttribute.cs` — `[HsmDefinition]` attribute
11. `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Attributes/HsmLayoutAttribute.cs` — `[HsmLayout]` attribute
12. `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/HsmDefinitionBlob.cs` — `HsmDefinitionBlob` struct
13. `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/HsmDefinitionHeader.cs` — `.Header.StructureHash`, `.Header.ParameterHash`
14. `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAssetProjector.cs` — `HsmAssetProjector.Project` signature
15. `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmAssetProjectionTests.cs` — see how Project is called in tests
16. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IEditorHostServices.cs` — full interface
17. `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmNodeCatalog.cs` — for HsmEditorHostServices constructor
18. `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmTypeSystem.cs` — for constructor
19. `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmLinkValidator.cs` — for constructor
20. `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmCommandSink.cs` — for constructor
21. `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmRendererRegistrationTests.cs` — existing test style
22. `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj` — project file
23. `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj` — test project file

## Repository root
`d:\Work\IOS-IG-SimHost-FDP-2\`

## Mandatory non-negotiable constraints (from AGENTS.md)

- **No Unicode characters** in comments or string literals where ASCII suffices.
- **Minimize diffs**: only add or change what is directly needed.
- **Preserve all existing comments** exactly; do not add, remove, or reword comments in files you did not author in this batch.
- **Build must compile with zero errors and zero warnings** before you report done.
- **All 87 existing tests must keep passing** plus the new tests you add.
- **TreatWarningsAsErrors** is active for `Hrot.Hsm.Editor`; every warning is a build failure.
- **No emoji, no Unicode arrows, no typographic dashes** in comments or strings.

## Project namespace conventions

- `Hrot.Hsm.Editor` assembly: namespace `Hrot.Hsm.Editor.*`
  - New files go in the appropriate subfolder: `Catalog/`, `HotReload/`, `Host/`
- `Hrot.Hsm.Editor.Tests` assembly: namespace `Hrot.Hsm.Editor.Tests`

---

## Task HS-S1-23: `HsmAssetContributor`

### File to create
`Hrot/Subsystems/AI/Hrot.Hsm.Editor/Catalog/HsmAssetContributor.cs`

### What it must do
Implement `IAssetCatalogContributor` (from `Hrot.Editor.AiShared.Catalog`).
On `LoadFrom(Assembly assembly)`:
1. Clear the internal list of assets.
2. Iterate all types in the assembly.
3. For each method decorated with `[HsmDefinitionAttribute]` that is public, static, takes zero parameters, and returns `HsmDefinitionBlob`:
   a. Invoke the method to get the blob (catch and skip on exception).
   b. Determine the asset GUID:
      - If `defAttr.AssetId` is non-null, try `Guid.TryParse(defAttr.AssetId, out var g)` — use that GUID.
      - Otherwise, derive via `AssetIdHasher.FromName(defAttr.MachineName)`.
   c. Try to find the layout via `LayoutDiscovery.TryGetLayout<HsmLayoutAttribute, HsmEditorLayout>(assembly, assetId)`.
   d. Project: `HsmAssetProjector.Project(blob, null, layout, assetId, defAttr.MachineName, string.Empty, false, type.Namespace ?? string.Empty)`.
      - Pass `null` for `MachineMetadata` — the projector falls back gracefully to `State_{id}` naming for Slice 1.
   e. Add the resulting `HsmAsset` to the internal list.
4. Fire `ContributorChanged`.

### Pattern to mirror exactly
`BTreeAssetContributor.cs` but replace:
- `BTreeDefinitionAttribute` → `HsmDefinitionAttribute`
- `BTreeLayoutAttribute` → `HsmLayoutAttribute`
- `BTreeEditorLayout` → `HsmEditorLayout`
- `BehaviorTreeBlob` → `HsmDefinitionBlob`
- `BehaviorTreeAssetProjector.Project` → `HsmAssetProjector.Project`
- `defAttr.TreeName` → `defAttr.MachineName`
- The blob metadata argument: BTree uses `blob.DebugMetadata`; HSM has no embedded metadata, so pass `null`.
- The extra `string.Empty` argument: BTree's projector has one more `string` param than HSM's; check the exact signature of `HsmAssetProjector.Project`.
- `AssetKind.BTree` → `AssetKind.Hsm`

### Exact call to HsmAssetProjector.Project
Read `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAssetProjector.cs` to confirm the exact signature:
```
public static HsmAsset Project(
    HsmDefinitionBlob blob,
    MachineMetadata? metadata,
    HsmEditorLayout? layout,
    Guid assetId,
    string machineName,
    string sourceFilePath,
    bool isEditorOwned,
    string assemblyNamespace)
```
Pass: `(blob, null, layout, assetId, defAttr.MachineName, string.Empty, false, type.Namespace ?? string.Empty)`

### Using directives needed
```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Identity;
using Hrot.Editor.AiShared.Layout;
using Hrot.Hsm.Editor.Model;
```

---

## Task HS-S1-24: `HsmEditorHostServices`

### File to create
`Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmEditorHostServices.cs`

### What it must do
Implement `IEditorHostServices` (from `NodeEditor.Core.Interfaces`).
Mirror `BTreeEditorHostServices` exactly, substituting HSM-specific types:

```csharp
internal sealed class HsmEditorHostServices : IEditorHostServices
{
    private readonly HsmNodeCatalog      _nodeCatalog;
    private readonly HsmTypeSystem       _typeSystem;
    private readonly HsmLinkValidator    _linkValidator;
    private readonly HsmCommandSink      _commandSink;
    private readonly IPickerRegistry     _pickers;
    private readonly IClipboard          _clipboard;
    private readonly IIconProvider       _icons;
    private readonly IDiagnosticsSink?   _diagnostics;
    private IDebugSession?               _debug;
    private readonly IInputSource        _input;
    private readonly IEditorTheme        _theme;
    private readonly IReadOnlyList<ICustomCanvasRenderer> _customRenderers;

    public HsmEditorHostServices(
        HsmNodeCatalog nodeCatalog,
        HsmTypeSystem typeSystem,
        HsmLinkValidator linkValidator,
        HsmCommandSink commandSink,
        IPickerRegistry pickers,
        IClipboard clipboard,
        IIconProvider icons,
        IDiagnosticsSink? diagnostics,
        IInputSource input,
        IEditorTheme theme,
        IDebugSession? debug = null,
        IReadOnlyList<ICustomCanvasRenderer>? customRenderers = null)
    { ... }

    public INodeCatalog NodeCatalog => _nodeCatalog;
    public ITypeSystem TypeSystem => _typeSystem;
    public ILinkValidator LinkValidator => _linkValidator;
    public IGraphCommandSink CommandSink => _commandSink;
    public IPickerRegistry Pickers => _pickers;
    public IClipboard Clipboard => _clipboard;
    public IIconProvider Icons => _icons;
    public IDiagnosticsSink? Diagnostics => _diagnostics;
    public IDebugSession? Debug => _debug;
    public IInputSource Input => _input;
    public IEditorTheme Theme => _theme;
    public IReadOnlyList<ICustomCanvasRenderer> CustomCanvasRenderers => _customRenderers;

    public void SetDebugSession(IDebugSession? session) => _debug = session;
}
```

IMPORTANT: Read `BTreeEditorHostServices.cs` FIRST. Use `System.Array.Empty<ICustomCanvasRenderer>()` as default for null customRenderers (same as BTree does).

### Using directives needed
```csharp
using System.Collections.Generic;
using NodeEditor.Core.Interfaces;
```

---

## Task HS-S1-25: `HsmQuickReloadHasher`

### File to create
`Hrot/Subsystems/AI/Hrot.Hsm.Editor/HotReload/HsmQuickReloadHasher.cs`

### What it must do
Mirror `BTreeQuickReloadHasher` but use `HsmDefinitionBlob` and its header hashes.

```csharp
// Classifies an HSM hot-reload tier by comparing StructureHash and ParameterHash
// of the previous and next HsmDefinitionBlob headers.
// Delegates to the shared HotReloadClassifier.
public static class HsmQuickReloadHasher
{
    public static HotReloadTier Classify(HsmDefinitionBlob previous, HsmDefinitionBlob next) =>
        HotReloadClassifier.Classify(
            (int)previous.Header.StructureHash, (int)next.Header.StructureHash,
            (int)previous.Header.ParameterHash, (int)next.Header.ParameterHash);
}
```

Note: `HsmDefinitionHeader.StructureHash` and `.ParameterHash` are `uint`; `HotReloadClassifier.Classify` takes `int`. Cast with `(int)`.

### Using directives needed
```csharp
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared.HotReload;
```

---

## Tests to add

### File: `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmAssetContributorTests.cs`

Write 5 tests:

1. `LoadFrom_assembly_with_no_definitions_enumerates_empty()`
   - Create an in-process assembly stub using `Moq` or reflection-based fake type OR
   - Create a test-local assembly with no `[HsmDefinition]` methods by passing the current test assembly and verifying count is whatever it is (or use a known empty assembly)
   - SIMPLER APPROACH: Create a nested class inside the test with no `[HsmDefinition]` methods, pass `typeof(HsmAssetContributorTests).Assembly`, and verify all returned assets have `Kind == AssetKind.Hsm` (they're loaded from the test assembly which has no definitions — so result should be empty, since tests don't define `[HsmDefinition]` methods).
   - Actually SIMPLEST: Create a fresh `HsmAssetContributor`, do NOT call `LoadFrom`, call `Enumerate()` — should return empty.

2. `Enumerate_before_load_from_returns_empty()`
   - `new HsmAssetContributor()`, `contributor.Enumerate()` → `.Should().BeEmpty()`

3. `Kind_property_returns_Hsm()`
   - `new HsmAssetContributor().Kind.Should().Be(AssetKind.Hsm)`

4. `ContributorChanged_fires_after_LoadFrom()`
   - Subscribe to `ContributorChanged`, call `LoadFrom(Assembly.GetExecutingAssembly())`, verify event was fired.

5. `LoadFrom_with_definition_method_produces_asset()`
   - This is the integration test. Since we can't easily create a real HsmDefinitionBlob from `[HsmDefinition]` in test code without the full compiler, use `LoadFrom(Assembly.GetExecutingAssembly())` and just verify no exceptions are thrown and `Enumerate()` returns 0 or more `HsmAsset` entries with correct `Kind`.
   - Or define a static `[HsmDefinition]` method in the test file using `HsmBuilder` + `HsmEmitter` (look at `HsmAssetProjectionTests.cs` for how to create a blob) — this would be a proper integration test.

**RECOMMENDED approach for test 5**: Define a private static `[HsmDefinition]`-decorated method inside the test class that returns a compiled blob from `HsmBuilder`. This is an in-process reflection test. Check `HsmAssetProjectionTests.cs` to see how to build a blob.

```csharp
// Inside HsmAssetContributorTests class:
[HsmDefinition("TestMachine")]
public static HsmDefinitionBlob CompileTestMachine()
{
    var builder = new HsmBuilder("TestMachine");
    builder.State("Idle");
    var graph = builder.Build();
    HsmNormalizer.Normalize(graph);
    var flatData = HsmFlattener.Flatten(graph);
    return HsmEmitter.Emit(flatData);
}
```

Then call `LoadFrom(typeof(HsmAssetContributorTests).Assembly)` and verify `Enumerate().Count == 1` and `Enumerate()[0].Name == "TestMachine"`.

Read the existing test files (`HsmAssetProjectionTests.cs`) to get the exact using statements and builder pattern.

### File: `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmEditorHostServicesTests.cs`

Write 5 tests using Moq for the NodeEditor interfaces:

1. `Properties_return_injected_values()`
   - Mock all required interfaces, construct `HsmEditorHostServices`, verify each property returns the injected mock.

2. `CustomRenderers_defaults_to_empty_when_null_passed()`
   - Pass `customRenderers: null` to constructor, verify `CustomCanvasRenderers.Count == 0`.

3. `SetDebugSession_updates_debug_property()`
   - Construct with `debug: null`, call `SetDebugSession(mockSession)`, verify `Debug == mockSession`.

4. `Implements_IEditorHostServices()`
   - `new HsmEditorHostServices(...) should be assignable to IEditorHostServices`.

5. `CustomRenderers_returns_provided_list()`
   - Provide a `List<ICustomCanvasRenderer>` with one mock renderer, verify `CustomCanvasRenderers.Count == 1`.

For mocking, use `Moq` (already a dependency of the test project — check `Hrot.Hsm.Editor.Tests.csproj`). If Moq is NOT present, check whether the BTree editor tests use Moq or stubs; use the same approach.

### File: `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmQuickReloadHasherTests.cs`

Write 5 tests:

1. `Classify_identical_blobs_returns_cosmetic()`
   - Create two blobs with same StructureHash and ParameterHash → `HotReloadTier.Cosmetic`

2. `Classify_different_param_hash_returns_soft()`
   - Same StructureHash, different ParameterHash → `HotReloadTier.Soft`

3. `Classify_different_structure_hash_returns_hard()`
   - Different StructureHash, same ParameterHash → `HotReloadTier.Hard`

4. `Classify_both_hashes_different_returns_hard()`
   - Different StructureHash AND different ParameterHash → `HotReloadTier.Hard` (structure wins)

5. `Classify_zero_hashes_returns_cosmetic()`
   - Both blobs with `default` headers → `HotReloadTier.Cosmetic`

To create a blob with specific hashes:
```csharp
private static HsmDefinitionBlob BlobWithHashes(uint structureHash, uint paramHash)
{
    var blob = new HsmDefinitionBlob();
    blob.Header.StructureHash = structureHash;
    blob.Header.ParameterHash = paramHash;
    return blob;
}
```

Using directives for this test file:
```csharp
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared.HotReload;
using Hrot.Hsm.Editor.HotReload;
using Xunit;
```

---

## Build verification

After implementing all tasks, run from `d:\Work\IOS-IG-SimHost-FDP-2\`:
```
dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj
dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj
dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj --no-build
```

Expected: 0 errors, 0 warnings, all 87 existing tests pass plus the 15 new tests (total 102).

## Report format

Report back with:
1. List of files created/modified
2. Final test count (should be 102)
3. Confirmation: 0 errors, 0 warnings
4. Any decisions made (e.g., if Moq not available, what stub approach was used)
