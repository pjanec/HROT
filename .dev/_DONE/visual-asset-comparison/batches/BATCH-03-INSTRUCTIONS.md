# BATCH-03: Blueprint Sanitizer (No-Op Adapters + BlueprintComparisonSanitizer) + D-08 Debt Fix

**Batch Number:** BATCH-03
**Tasks:** TASK-C-08, TASK-C-09 + D-08 debt fix
**Slice:** C-3 — Blueprint sanitizer (JSON DOM-based)
**Estimated Effort:** 14-18 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 (framework interfaces), BATCH-02 (no additional deps, but FakeCatalog consolidation touches BTree/HSM tests)

---

## Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Developer Skill:** `.github\skills\developer\SKILL.md`
2. **Design Document — §3.5:** `.dev\visual-asset-comparison\Visual_Asset_Comparison_Detailed_Design.md` — the Blueprint sanitization section in full, including the before/after example JSON.
3. **Task Details:** `.dev\visual-asset-comparison\TASK-DETAILS.md` — TASK-C-08 and TASK-C-09 sections in full.
4. **BATCH-01 and BATCH-02 implementation (study):**
   - `Hrot/Subsystems/AI/Hrot.BTree.Editor/Comparison/BTreeComparisonSanitizer.cs` — structural pattern to follow for the Blueprint sanitizer.
   - `Hrot/Editor/Hrot.Editor.AiShared/Comparison/IComparisonMigrationAdapter.cs` — interface already in place.
   - `Hrot/Editor/Hrot.Editor.AiShared/Comparison/IMetaEnvelopeSanitizer.cs` — interface already in place.
5. **Blueprint JSON format (study actual files):**
   - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/simple-action.bp.json` — basic asset structure.
   - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/with-callable-peer.bp.json` — `CallPeerBlueprint` node.
   - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/Nodes.cs` — node types and their JSON field names.
6. **Debt Tracker:** `.dev\visual-asset-comparison\DEBT-TRACKER.md` — see D-08 (FakeCatalog consolidation).

### Source Code Locations

| What | Path |
|------|------|
| No-op migration adapter (NEW) | `Hrot/Editor/Hrot.Editor.AiShared/Comparison/NoOpComparisonMigrationAdapter.cs` |
| No-op meta sanitizer (NEW) | `Hrot/Editor/Hrot.Editor.AiShared/Comparison/NoOpMetaEnvelopeSanitizer.cs` |
| No-op adapter tests (NEW) | `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/NoOpAdapterTests.cs` |
| Blueprint sanitizer (NEW) | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Comparison/BlueprintComparisonSanitizer.cs` |
| Blueprint DI extension (NEW) | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Comparison/BlueprintEditorComparisonServiceCollectionExtensions.cs` |
| Blueprint tests (NEW sub-folder) | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Comparison/` |
| Shared test helper (NEW) | `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/TestHelpers/FakeCatalogHelper.cs` |
| BTree test classes to update (D-08) | `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/BTreeComparisonSanitizerTests.cs`, `BTreeSanitizationDeterminismTests.cs`, `BTreeSelfComparisonTests.cs` |
| HSM test class to update (D-08) | `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/HsmComparisonSanitizerTests.cs` |
| Blueprint csproj (MAY NEED EDIT) | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj` |
| Blueprint Editor csproj (READ) | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Hrot.Blueprints.Editor.csproj` |

### Test Execution

```powershell
# Run shared AiShared tests (for NoOpAdapterTests)
dotnet test "Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj" -c Debug

# Run Blueprint tests (for BlueprintComparisonSanitizerTests)
dotnet test "Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj" -c Debug

# Run BTree tests (to verify D-08 refactor didn't break anything)
dotnet test "Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj" -c Debug

# Run HSM tests (to verify D-08 refactor didn't break anything)
dotnet test "Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj" -c Debug

# Full solution build
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4
```

### Report Submission

Submit completed report to: `.dev\visual-asset-comparison\reports\BATCH-03-REPORT.md`

---

## Context

BATCH-03 completes all four per-kind sanitizers (BTree and HSM done; Blackboard done; Blueprint here). After this batch, the sanitization layer is complete — every `AssetKind` has a working, deterministic sanitizer registered in `SanitizerRegistry`.

The Blueprint sanitizer operates on JSON (`.bp.json` files) rather than C# text, so the implementation approach differs significantly from BTree/HSM/Blackboard. Use `System.Text.Json.Nodes.JsonNode` for DOM manipulation (already available in .NET 8).

---

## Important: Blueprint JSON Format Discrepancies

**Read the actual `.bp.json` test assets BEFORE implementing**, because the design document uses a simplified/forward-looking JSON format that differs from the real files in two important ways:

1. **`$meta` envelope:** The design doc says "every versioned JSON document carries a `$meta` envelope". The **actual files use `"Header"` instead of `"$meta"`**. The `Header` object has `SubsystemType` and `SchemaVersion`. The `IComparisonMigrationAdapter` and `IMetaEnvelopeSanitizer` are no-ops for now — the Blueprint sanitizer should pass the raw JSON through the migration adapter first (no-op returns unchanged), then process the DOM. No `$meta` stripping is needed in Phase 1 because the `Header` object itself is **structural (semantic)**, not diagnostic — preserve `Header` in the sanitized output. The no-op `IMetaEnvelopeSanitizer` can be a true no-op that never touches the DOM at all.

2. **`CallPeerBlueprint` field name:** The design doc shows `"TargetBlueprint": "guid"` on the node. The **actual field is `"PeerBlueprintId": "guid-string"`** (see `Nodes.cs`: `public string PeerBlueprintId { get; set; }`). Humanization adds `"_targetName"` based on `PeerBlueprintId`.

---

## Tasks

### D-08 Debt Fix — Consolidate FakeCatalog/FakeAsset to Shared Test Helper

**Context:** `FakeCatalog` and `FakeAsset` are currently duplicated across:
- `BTreeComparisonSanitizerTests.cs` (in `Hrot.BTree.Editor.Tests`)
- `BTreeSanitizationDeterminismTests.cs` (in `Hrot.BTree.Editor.Tests`)
- `BTreeSelfComparisonTests.cs` (in `Hrot.BTree.Editor.Tests`)
- `HsmComparisonSanitizerTests.cs` (in `Hrot.Hsm.Editor.Tests`)

**What to do:**

**For BTree tests:** Create a single `FakeCatalogHelper.cs` in `Hrot.BTree.Editor.Tests/Comparison/` (internal to that project) with `internal sealed class FakeCatalog : IAssetCatalog` and `internal sealed class FakeAsset : IEditableAsset`. Remove the duplicate nested private classes from all three BTree test classes and use the shared helper instead.

**For HSM tests:** Create a `FakeCatalogHelper.cs` in `Hrot.Hsm.Editor.Tests/Comparison/` with the same classes. Remove the private nested duplicates from `HsmComparisonSanitizerTests.cs`.

**Note:** The BTree and HSM test projects are separate projects, so the helpers cannot be shared between them. Each test project gets its own copy of the helper classes. The goal is consolidation *within* each project, not cross-project sharing.

All tests must still pass after the refactor. Verify with the test commands above.

---

### TASK-C-08 — No-Op `IComparisonMigrationAdapter` and `IMetaEnvelopeSanitizer`

**Full spec:** See [TASK-DETAILS.md](../TASK-DETAILS.md#task-c-08--no-op-icomparisonmigrationadapter-and-imetaenvelopesanitizer-implementations)
**Design refs:** §3.5 step 0, §3.5 step 1, §8.1

**New files:**

| File | Description |
|------|-------------|
| `Hrot/Editor/Hrot.Editor.AiShared/Comparison/NoOpComparisonMigrationAdapter.cs` | Returns input unchanged, `didMigrate=false` |
| `Hrot/Editor/Hrot.Editor.AiShared/Comparison/NoOpMetaEnvelopeSanitizer.cs` | Returns input unchanged |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/NoOpAdapterTests.cs` | 3 tests |

**Implementation (trivial):**

```csharp
// NoOpComparisonMigrationAdapter.cs
public sealed class NoOpComparisonMigrationAdapter : IComparisonMigrationAdapter
{
    public string Adapt(string rawJson, out bool didMigrate)
    {
        didMigrate = false;
        return rawJson;
    }
}

// NoOpMetaEnvelopeSanitizer.cs
public sealed class NoOpMetaEnvelopeSanitizer : IMetaEnvelopeSanitizer
{
    public string Sanitize(string metaEnvelopeJson) => metaEnvelopeJson;
}
```

**DI registration:** In `SharedAiEditorServiceCollectionExtensions.AddSharedAiEditor()`, register both as default singletons:
```csharp
services.TryAddSingleton<IComparisonMigrationAdapter, NoOpComparisonMigrationAdapter>();
services.TryAddSingleton<IMetaEnvelopeSanitizer, NoOpMetaEnvelopeSanitizer>();
```
Use `TryAddSingleton` so that production implementations registered before this call are not overwritten.

**Tests required (`NoOpAdapterTests.cs`):**
- `NoOpAdapter_Adapt_ReturnsSameJson_DidMigrateFalse`: pass JSON string → same string returned, `didMigrate=false`
- `NoOpMetaSanitizer_Sanitize_ReturnsSameEnvelope`: pass envelope JSON string → same string returned
- `DI_DefaultContainer_ResolvesNoOpAdapter`: build a default DI container with `AddSharedAiEditor()`, resolve `IComparisonMigrationAdapter`, verify it's a `NoOpComparisonMigrationAdapter`

---

### TASK-C-09 — `BlueprintComparisonSanitizer` (JSON DOM Walk, Strip EditorMetadata, Sort, Re-Serialize)

**Full spec:** See [TASK-DETAILS.md](../TASK-DETAILS.md#task-c-09--blueprintcomparisonsanitizer-json-dom-walk-strip-editormetadata-sort-re-serialize)
**Design refs:** §3.5 (steps 0–8), classification tables

**New files:**

| File | Description |
|------|-------------|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Comparison/BlueprintComparisonSanitizer.cs` | JSON DOM sanitizer |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Comparison/BlueprintEditorComparisonServiceCollectionExtensions.cs` | DI extension |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Comparison/BlueprintComparisonSanitizerTests.cs` | Unit tests |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Comparison/Fixtures/simple_node.bp.json` | Minimal fixture (1 graph, 1 node) |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Comparison/Fixtures/with_editor_metadata.bp.json` | Fixture with Comment, CanvasComments, X/Y, Viewport |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Comparison/Fixtures/with_peer_call.bp.json` | Fixture with CallPeerBlueprint node |

**The Blueprint JSON format (actual, from test assets):**

```json
{
  "Header": { "SubsystemType": "Hrot.Blueprints", "SchemaVersion": "1.0" },
  "AssetId": "11111111-0000-0000-0000-000000000001",
  "Name": "ExampleAsset",
  "Dispatch": "AiPrimitive",
  "Graphs": [
    {
      "Id": "bbbbbbbb-0000-0000-0000-000000000001",
      "Name": "Execute",
      "Kind": "Function",
      "Inputs": [],
      "Outputs": [],
      "Nodes": [
        {
          "kind": "FunctionCall",
          "Id": "cccccccc-0000-0000-0000-000000000001",
          "TargetTypeId": "System.Console",
          "MethodName": "WriteLine",
          "IsPure": false,
          "Pins": [],
          "EditorMetadata": {
            "X": 320,
            "Y": 180,
            "Comment": "writes the debug message"
          }
        }
      ],
      "Links": [],
      "EditorMetadata": {
        "Viewport": { "Pan": [10, 20], "Zoom": 1.0 },
        "CanvasComments": [
          { "Text": "Main execution flow", "X": 100, "Y": -50 }
        ]
      }
    }
  ],
  "EditorMetadata": {}
}
```

**Implementation steps (in code):**

**Step 0:** Call `IComparisonMigrationAdapter.Adapt(rawJson, out bool didMigrate)`. If `didMigrate=true`, record migration for `Metadata.MigrationNotice`.

**Step 1:** Parse the adapted JSON string into a `JsonNode` DOM using `JsonNode.Parse(adaptedJson)`.

**No `$meta` stripping in Phase 1:** The actual Blueprint files use `"Header"` (not `"$meta"`). The `IMetaEnvelopeSanitizer` is a no-op for now. Preserve the `"Header"` object verbatim in the sanitized output.

**Step 2 + 3:** Walk the DOM and process every `EditorMetadata` object at every level (root-level, per-graph, per-node):

For **node-level** `EditorMetadata`:
- Hoist `Comment` (string): move to `node["Comment"] = value`, then remove from EditorMetadata.
- Strip `X`, `Y` (numbers).
- Strip any other unrecognized keys (defensive: strip-by-default on node-level EditorMetadata).
- If EditorMetadata is now empty (or contains only stripped keys), remove the `EditorMetadata` key from the node.

For **graph-level** `EditorMetadata`:
- Hoist `CanvasComments` (array): create `graph["_canvasComments"] = hoistedArray` where each element keeps only `"Text"` (strip `X`, `Y` from each canvas comment entry). Remove `CanvasComments` from EditorMetadata.
- Strip `Viewport` (object).
- Strip `DockState`.
- Strip `NodeViewStates`.
- Strip any other unrecognized keys.
- If EditorMetadata is now empty, remove it from the graph.

For **root-level** `EditorMetadata`:
- Strip everything (no semantic content at root level per design §3.5).
- Remove it from the DOM.

**Step 4:** Humanize `CallPeerBlueprint` nodes. Walk all nodes in all graphs. For each node where `node["kind"]?.GetValue<string>() == "CallPeerBlueprint"`:
- Read `node["PeerBlueprintId"]?.GetValue<string>()` (the actual field name — NOT `TargetBlueprint`).
- If present and parseable as Guid: look up in `IAssetCatalog.FindByAssetId(guid)`.
  - Found: `node["_targetName"] = $"{asset.Name} ({asset.Kind})"` (same format as BTree humanization).
  - Not found: `node["_targetName"] = "(asset not found in catalog)"`.
- If field not present or not a valid GUID: skip (no `_targetName` added).

**Step 5–7:** These are enforced by not stripping `kind`, `Id`, `FromNodeId`, `ToPinId`, `LinkedToIds`, or any variable/parameter/working-state declarations during the EditorMetadata walk. Since you only touch `EditorMetadata` keys specifically, all other keys are naturally preserved.

**Step 8:** Re-serialize with stable property ordering. Use a recursive DOM transform to build a new `JsonObject` with alphabetically sorted keys at each level. Arrays retain source order. Then serialize with:
```csharp
var options = new JsonSerializerOptions { WriteIndented = true };
string result = sortedNode.ToJsonString(options);
```

**`AssetId` and `AssetName` extraction:** The `AssetId` is at `root["AssetId"]`; the `AssetName` is at `root["Name"]`.

**Never-throws contract:** Wrap `SanitizeCore` in a try/catch. On any exception, return a `SanitizationResult` with the raw file text, fallback metadata, and a warning.

**Constructor:** `BlueprintComparisonSanitizer(IComparisonMigrationAdapter migrationAdapter, IMetaEnvelopeSanitizer metaSanitizer, IAssetCatalog catalog)`.

**`TargetKind`:** `AssetKind.Blueprint`.

**DI wiring:** Create `BlueprintEditorComparisonServiceCollectionExtensions` in `Hrot.Blueprints.Editor.Comparison` namespace with `AddBlueprintEditorComparison(services, registry)` that registers `BlueprintComparisonSanitizer` as a singleton and wires it into `SanitizerRegistry`. Mirror the BTree pattern exactly.

**Namespace:** `Hrot.Blueprints.Editor.Comparison`.

**JSON sort helper (write this utility method):**

```csharp
private static JsonNode SortPropertiesRecursive(JsonNode node)
{
    switch (node)
    {
        case JsonObject obj:
        {
            var sorted = new JsonObject();
            foreach (var kv in obj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                sorted[kv.Key] = kv.Value != null ? SortPropertiesRecursive(kv.Value.DeepClone()) : null;
            return sorted;
        }
        case JsonArray arr:
        {
            var result = new JsonArray();
            foreach (var item in arr)
                result.Add(item != null ? SortPropertiesRecursive(item.DeepClone()) : null);
            return result;
        }
        default:
            return node.DeepClone();
    }
}
```

**Tests required (`BlueprintComparisonSanitizerTests.cs`):**

**IMPORTANT:** The Blueprint Tests project (`Hrot.Blueprints.Tests.csproj`) does NOT currently reference `Hrot.Editor.AiShared`. You need to add a project reference to `Hrot.Editor.AiShared.Tests`? No — add a reference to `Hrot.Editor.AiShared` in the tests csproj. Check if it's already there. If not, add:
```xml
<ProjectReference Include="..\..\..\Editor\Hrot.Editor.AiShared\Hrot.Editor.AiShared.csproj" />
```
(Verify the relative path by looking at existing references in the csproj.)

**Test list:**

1. **`Sanitize_NodeComment_IsHoistedToTopLevelNodeProperty`**: write a temp `.bp.json` file with a node that has `EditorMetadata.Comment = "writes the debug message"`. After sanitization: `node["Comment"]` = `"writes the debug message"`, `node["EditorMetadata"]` is absent (empty/stripped). Assert the comment appears as a top-level node property, not inside `EditorMetadata`.

2. **`Sanitize_CanvasComments_AreHoistedToGraphLevelWithTextOnly`**: fixture with a graph-level `EditorMetadata.CanvasComments = [{Text: "Main flow", X: 100, Y: -50}]`. After sanitization: `graph["_canvasComments"]` is a JSON array with one element containing only `"Text"` (no `X`, `Y`). `graph["EditorMetadata"]` is absent.

3. **`Sanitize_NodePositionXY_IsStripped`**: fixture with node `EditorMetadata.X = 320, EditorMetadata.Y = 180`. After sanitization: no `X` or `Y` in the node at any level.

4. **`Sanitize_GraphViewport_IsStripped`**: fixture with graph `EditorMetadata.Viewport = {Pan: [0,0], Zoom: 1.0}`. After sanitization: no `Viewport` in output.

5. **`Sanitize_NodeId_IsPreserved`**: fixture with node `"Id": "cccccccc-0000-0000-0000-000000000001"`. After sanitization: `node["Id"]` is still `"cccccccc-0000-0000-0000-000000000001"`.

6. **`Sanitize_CallPeerBlueprint_AddsTargetName_WhenCatalogHit`**: fixture with node `"kind": "CallPeerBlueprint", "PeerBlueprintId": "11111111-0000-0000-0000-000000000099"`. Inject a `FakeCatalog` returning asset `Name="PeerAsset", Kind=AssetKind.Blueprint`. After sanitization: node has `"_targetName": "PeerAsset (Blueprint)"`.

7. **`Sanitize_CallPeerBlueprint_AddsMissMessage_WhenCatalogMiss`**: same but catalog returns null. After sanitization: node has `"_targetName": "(asset not found in catalog)"`.

8. **`Sanitize_OutputIsAlphabeticallySorted`**: fixture with root-level keys in non-alphabetical order (e.g., `Graphs`, `AssetId`, `Name`). After sanitization: in the output JSON, alphabetically-earlier keys appear before alphabetically-later keys at the same level.

9. **`Sanitize_RunTenTimes_ProducesByteIdenticalOutput`**: 10-run determinism loop on `with_editor_metadata.bp.json` fixture.

10. **`Sanitize_ShuffledInput_SameOutputAsCanonicalInput`**: take the `simple_node.bp.json` fixture, shuffle the JSON property order manually (write a helper that writes the same data but with keys in reverse-alphabetical order), run both through the sanitizer, assert byte-identical output.

11. **`Sanitize_MissingFile_ReturnsWarning_NeverThrows`**: pass a non-existent file path. Assert warning present, no exception.

12. **`Sanitize_WithNoOpMigrationAdapter_NoMigrationNotice`**: run with `NoOpComparisonMigrationAdapter`. Assert `Metadata.MigrationNotice` is null.

13. **`Sanitize_WithFakeMigrationAdapter_MigrationNoticePopulated`**: inject a fake adapter that returns the same JSON but sets `didMigrate=true`. Assert `Metadata.MigrationNotice` is non-null and contains "migrated".

**Fake implementations for tests:**

For tests in `Hrot.Blueprints.Tests`, you need fake implementations of `IComparisonMigrationAdapter`, `IMetaEnvelopeSanitizer`, and the shared `IAssetCatalog` (from `Hrot.Editor.AiShared.Catalog`). Create them as private nested classes in the test file (the Blueprint tests project already has enough isolation). Do NOT attempt to reuse the `FakeCatalogHelper.cs` from the AiShared tests project (different project boundary).

**Note on `IAssetCatalog` namespacing:** The Blueprint sanitizer uses `Hrot.Editor.AiShared.Catalog.IAssetCatalog`. The `Hrot.Blueprints.Editor` has its own `IAssetCatalog` in `Hrot.Blueprints.Editor` namespace. To avoid ambiguity, use the fully-qualified name `Hrot.Editor.AiShared.Catalog.IAssetCatalog` (aliased at the top of the file if needed).

**Fixtures to create:**

`simple_node.bp.json` — minimal valid Blueprint with 1 node, no EditorMetadata content:
```json
{
  "Header": { "SubsystemType": "Hrot.Blueprints", "SchemaVersion": "1.0" },
  "AssetId": "aaaaaaaa-0001-0000-0000-000000000001",
  "Name": "SimpleNode",
  "Dispatch": "AiPrimitive",
  "Primitive": null,
  "Parameters": [],
  "WorkingState": [],
  "Variables": [],
  "EventDispatchers": [],
  "CustomEvents": [],
  "CallablePeers": [],
  "Graphs": [
    {
      "Id": "bbbbbbbb-0001-0000-0000-000000000001",
      "Name": "Execute",
      "Kind": "Function",
      "Inputs": [],
      "Outputs": [],
      "Nodes": [
        {
          "kind": "Return",
          "Id": "cccccccc-0001-0000-0000-000000000001",
          "Pins": [],
          "EditorMetadata": {}
        }
      ],
      "Links": [],
      "EditorMetadata": {}
    }
  ],
  "EditorMetadata": {}
}
```

`with_editor_metadata.bp.json` — uses EditorMetadata for Comment, CanvasComments, X, Y, Viewport:
```json
{
  "Header": { "SubsystemType": "Hrot.Blueprints", "SchemaVersion": "1.0" },
  "AssetId": "aaaaaaaa-0002-0000-0000-000000000001",
  "Name": "WithEditorMetadata",
  "Dispatch": "AiPrimitive",
  "Primitive": null,
  "Parameters": [],
  "WorkingState": [],
  "Variables": [],
  "EventDispatchers": [],
  "CustomEvents": [],
  "CallablePeers": [],
  "Graphs": [
    {
      "Id": "bbbbbbbb-0002-0000-0000-000000000001",
      "Name": "Execute",
      "Kind": "Function",
      "Inputs": [],
      "Outputs": [],
      "Nodes": [
        {
          "kind": "FunctionCall",
          "Id": "cccccccc-0002-0000-0000-000000000001",
          "TargetTypeId": "System.Console",
          "MethodName": "WriteLine",
          "IsPure": false,
          "Pins": [],
          "EditorMetadata": {
            "X": 320,
            "Y": 180,
            "Comment": "writes the debug message"
          }
        }
      ],
      "Links": [],
      "EditorMetadata": {
        "Viewport": { "Pan": [10, 20], "Zoom": 1.0 },
        "CanvasComments": [
          { "Text": "Main execution flow", "X": 100, "Y": -50 }
        ]
      }
    }
  ],
  "EditorMetadata": {}
}
```

`with_peer_call.bp.json` — has a `CallPeerBlueprint` node with `PeerBlueprintId`:
```json
{
  "Header": { "SubsystemType": "Hrot.Blueprints", "SchemaVersion": "1.0" },
  "AssetId": "aaaaaaaa-0003-0000-0000-000000000001",
  "Name": "WithPeerCall",
  "Dispatch": "AiPrimitive",
  "Primitive": null,
  "Parameters": [],
  "WorkingState": [],
  "Variables": [],
  "EventDispatchers": [],
  "CustomEvents": [],
  "CallablePeers": ["11111111-0000-0000-0000-000000000099"],
  "Graphs": [
    {
      "Id": "bbbbbbbb-0003-0000-0000-000000000001",
      "Name": "Execute",
      "Kind": "Function",
      "Inputs": [],
      "Outputs": [],
      "Nodes": [
        {
          "kind": "CallPeerBlueprint",
          "Id": "cccccccc-0003-0000-0000-000000000001",
          "PeerBlueprintId": "11111111-0000-0000-0000-000000000099",
          "FunctionRef": "Execute",
          "Pins": [],
          "EditorMetadata": {}
        }
      ],
      "Links": [],
      "EditorMetadata": {}
    }
  ],
  "EditorMetadata": {}
}
```

**IMPORTANT — fixture file handling:** The Blueprint tests project already has:
```xml
<Content Include="TestAssets\**\*" CopyToOutputDirectory="PreserveNewest" />
```
Add a similar `<Content>` entry for the `Comparison/Fixtures/` folder in the `.csproj`. Then reference fixture files via `Path.Combine(AppContext.BaseDirectory, "Comparison", "Fixtures", "simple_node.bp.json")` in tests.

---

## Mandatory Workflow

1. **D-08 fix:** Consolidate FakeCatalog/FakeAsset in BTree and HSM test projects → all BTree and HSM tests still pass ✅
2. **TASK-C-08:** Create no-op adapters + DI registration + tests → all AiShared tests pass ✅
3. **TASK-C-09:** Implement BlueprintComparisonSanitizer + fixtures + tests → all Blueprint tests pass ✅
4. Final full solution build: 0 errors ✅

Complete tasks in order. Do NOT start the next task until all tests for the current one pass. Do NOT stop until all tasks are done. Fix all errors immediately.

---

## Quality Standards

- No compiler warnings (Blueprint Editor uses `TreatWarningsAsErrors`).
- `BlueprintComparisonSanitizer` must never throw — catch all exceptions and return a warning.
- Alphabetical property ordering must be deterministic — always the same result regardless of input ordering.
- `PeerBlueprintId` is the correct JSON field name for `CallPeerBlueprint` nodes (NOT `TargetBlueprint`).
- Fixture files must be included in the test project output directory (via `<Content>` entry in csproj).

---

## Developer Insights (Answer in Report)

**Q1:** The Blueprint sanitizer strips `EditorMetadata` at root, graph, and node levels with different rules. Did you find any EditorMetadata subkeys in the real test assets that weren't covered by the classification table in §3.5? How did you handle them?

**Q2:** The alphabetical sort is applied recursively to the entire DOM. Are there any cases where alphabetical sorting of object keys could change the semantics of the Blueprint DOM (e.g., arrays where order matters)?

**Q3:** The design's example JSON uses `"TargetBlueprint"` but the real field is `"PeerBlueprintId"`. Did you verify this discrepancy against the actual `Nodes.cs` source? Were there other discrepancies between the design doc and the real format?

**Q4:** Did you find any edge cases in the JSON sort helper (e.g., null values, values that are not `JsonObject` or `JsonArray`) that required special handling?

**Q5:** What test scenarios did you wish were covered but weren't specified? Document them as P3 debt items in your report.

---

## Success Criteria

This batch is DONE when:
- [ ] D-08 debt fix: FakeCatalog/FakeAsset consolidated within each test project; all BTree tests pass; all HSM tests pass
- [ ] TASK-C-08: `NoOpComparisonMigrationAdapter` and `NoOpMetaEnvelopeSanitizer` created + DI wiring; all 3 NoOpAdapterTests pass
- [ ] TASK-C-09: `BlueprintComparisonSanitizer` + fixtures + 13 tests created; all pass
- [ ] `dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4` — 0 errors
- [ ] All AiShared tests pass
- [ ] All Blueprint tests pass
- [ ] All BTree tests pass
- [ ] All HSM tests pass
- [ ] Report submitted to `.dev\visual-asset-comparison\reports\BATCH-03-REPORT.md`

---

## Reference Materials

- **BTree sanitizer (structural pattern):** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Comparison/BTreeComparisonSanitizer.cs`
- **Blueprint JSON format:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/simple-action.bp.json`, `with-callable-peer.bp.json`
- **Node type definitions:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/Nodes.cs`
- **Blueprint Editor csproj (DI extension reference):** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Hrot.Blueprints.Editor.csproj`
- **Design §3.5:** `.dev\visual-asset-comparison\Visual_Asset_Comparison_Detailed_Design.md`
- **Task specs:** `.dev\visual-asset-comparison\TASK-DETAILS.md` — TASK-C-08 and TASK-C-09
