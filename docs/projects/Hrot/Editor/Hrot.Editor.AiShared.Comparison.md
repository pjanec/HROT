# Visual Asset Comparison

**Feature area:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/`
**Per-kind sanitizers:**
  - BTree: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Comparison/`
  - HSM: `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Comparison/`
  - Blueprint: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Comparison/`
  - Blackboard: `Hrot/Editor/Hrot.Editor.AiShared/Comparison/` (shared)
**Tests:**
  - Shared: `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/`
  - BTree: `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/`
  - HSM: `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/`
  - Blueprint: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Comparison/`
**Date documented:** 2026-05-30

---

## Executive Overview

The Visual Asset Comparison feature allows a designer to compare two historical versions
of a visually-authored AI asset — BTree, HSM, Blackboard, or Blueprint — and visualize
the semantic differences on the editor canvas.

The workflow is deliberately LLM-agnostic: the editor **produces** a sanitized text
export, the designer hands it to any LLM (Claude, ChatGPT, etc.) out-of-band, and
then **pastes the LLM response back** into the editor. The editor parses the
structured response and annotates affected nodes on the canvas with colored outlines
and badge glyphs, while an auto-opening side panel shows the prose summary.

The editor never calls an LLM directly. No network calls are made. The feature has
no dependency on any specific LLM vendor or API key.

### What ships in Phase 1

| Area | Delivered |
|------|-----------|
| Sanitization | BTree, HSM, Blackboard, Blueprint all sanitizable to deterministic LLM-ready text. |
| Export workflow | Asset-selection dialog, companion-file discovery, export delivery modal (save/copy), 8 MB threshold guard, 30-line preview, reverse A<->B. |
| Response parsing | Robust `LlmResponseParser` handles markdown fences, missing markers, truncated JSON. |
| Visualization | Canvas outlines (dashed, 2 px, per-severity color), node badges, `ComparisonSummaryPanel`, `ComparisonSidebar` with click-to-focus. |
| Lifecycle | Stale-comparison badge, severity filter toggles, Exit Comparison Mode. |
| Test coverage | 25 test files across four test projects. |

### What is deferred

- **Ghost rendering** of removed nodes (Slice C-8; deferred pending user feedback).
- **Sibling-asset diff** — comparing two structurally-similar but distinct assets.
- **Git integration** — reading old versions from git.
- **Automatic LLM invocation** — the editor never calls an LLM; text is shuttled manually.

---

## End-to-End Workflow

```
  [1] "Compare with..." toolbar button
          |
          v
  [2] AssetSelectionDialog:
      - User picks Version A (single file or folder)
      - Active asset is Version B
      - AssetSelectionValidator runs: kind match, file exists, AssetId warning
          |
          v
  [3] ComparisonExportBuilder:
      - Sanitizes both versions via the registered IAssetComparisonSanitizer
      - Emits fixed instruction block + per-version metadata blocks + sanitized text
          |
          v
  [4] ExportDeliveryModal:
      - Save to file  |  Copy to clipboard  (>8 MB: clipboard disabled)
      - 30-line preview + "Show full" expansion
          |
          |  (user shuttles text to their LLM; editor is idle)
          v
  [5] User pastes LLM response back via "Paste LLM Response..." button
          |
          v
  [6] LlmResponseParser: extracts HumanSummary + structured JSON
          |
          v
  [7] ComparisonSessionState stored in ComparisonSessionRegistry
          |
          v
  [8] ComparisonAnnotationRenderer (AfterNodes pass) draws outlines + badges
      ComparisonSummaryPanel opens with prose + severity filters
      ComparisonSidebar opens with change list + click-to-focus
```

---

## Architecture

### Component Map

```
Shared (Hrot.Editor.AiShared.Comparison)
+-----------------------------------------------------------+
|                                                           |
|  Interfaces / Types                                       |
|    IAssetComparisonSanitizer     SanitizationResult       |
|    AssetExportRequest            AssetMetadataBlock       |
|    IComparisonMigrationAdapter   IMetaEnvelopeSanitizer   |
|    ComparisonResponse            ComparisonChange         |
|    DiscoveredAsset               DiscoveredCompanion      |
|    ValidationResult              ValidationIssue          |
|                                                           |
|  Core services                                            |
|    SanitizerRegistry             ComparisonExportBuilder  |
|    LlmResponseParser             ComparisonSessionState   |
|    ComparisonSessionRegistry     StaleBadgeWatcher        |
|    AssetSelectionValidator       CompanionFileDiscovery   |
|    ResponseAssetMatcher          BlackboardComparisonSanitizer
|    NoOpComparisonMigrationAdapter  NoOpMetaEnvelopeSanitizer
|    ComparisonErrorMessages                                |
|                                                           |
|  Rendering                                                |
|    ComparisonAnnotationRenderer  ComparisonStyleMap       |
|                                                           |
|  UI                                                       |
|    ComparisonToolbarAction       AssetSelectionDialog     |
|    ExportDeliveryModal           PasteResponseModal       |
|    ExitComparisonAction          ComparisonSummaryPanel   |
|    ComparisonSidebar                                      |
+-----------------------------------------------------------+

Per-kind sanitizers (registered into SanitizerRegistry at startup)
+-----------------------------+  +-----------------------------+
|  Hrot.BTree.Editor          |  |  Hrot.Hsm.Editor            |
|  BTreeComparisonSanitizer   |  |  HsmComparisonSanitizer     |
|  BTreeComparisonToolbar     |  |  HsmComparisonToolbar       |
|  AddBTreeEditorComparison() |  |  AddHsmEditorComparison()   |
+-----------------------------+  +-----------------------------+

+-----------------------------+  +-----------------------------+
|  Hrot.Blueprints.Editor     |  |  Hrot.Editor.AiShared       |
|  BlueprintComparisonSanitizer  |  BlackboardComparisonSanitizer
|  AddBlueprintComparison()   |  |  (no separate toolbar       |
+-----------------------------+  |   -- integrates via         |
                                 |   BlackboardAuthoringWindow)|
                                 +-----------------------------+
```

### Sanitization Pipeline

Each asset kind has its own sanitizer registered with `SanitizerRegistry`. All four
follow the same `IAssetComparisonSanitizer` interface. The sanitization is
**deterministic**: identical input always produces byte-identical output.

```csharp
public interface IAssetComparisonSanitizer
{
    AssetKind TargetKind { get; }
    SanitizationResult Sanitize(AssetExportRequest request);
}
```

The output is `SanitizationResult`, which carries:
- `SanitizedText` — the LLM-ready string, normalized to `\n` line endings.
- `Metadata` — asset name, kind, AssetId, source path, companion files, last-modified
  timestamp, and an optional migration notice.
- `Warnings` — non-fatal warnings (missing layout method, missing file, etc.).

### Export Text Format

The full export emitted by `ComparisonExportBuilder` has five sections:

```
{INSTRUCTION_BLOCK}

================================================================================
VERSION A (OLD)
================================================================================
{VERSION_A_METADATA_BLOCK}

--- COMPANION FILES ---
{VERSION_A_SANITIZED_CONTENT}

================================================================================
VERSION B (NEW)
================================================================================
{VERSION_B_METADATA_BLOCK}

--- COMPANION FILES ---
{VERSION_B_SANITIZED_CONTENT}

================================================================================
END OF COMPARISON INPUT
================================================================================
```

Each version's metadata block is fixed-key key-value pairs:

```
ASSET NAME:       OrcGuard_BT
ASSET KIND:       BTree
ASSET ID:         f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21
SOURCE PATH:      /project/AI/Combat/OrcGuard_BT.cs
LAST MODIFIED:    2026-01-14 11:23:08 UTC
COMPANION FILES:  OrcGuard_BT.Blackboard.cs (present)
                  OrcGuard_BT.HeavyBlackboard.cs (not present)
                  OrcGuard_BT.Orchestrators.g.cs (present)
```

The instruction block is fixed text (~70 lines) written for the LLM, not the human.
It defines the task, the JSON schema for the structured-changes block, and severity
calibration guidance.

---

## Sanitizers

### BTree — `BTreeComparisonSanitizer`

Operates on raw `.cs` file text without reflection. Steps:

1. Locate the `[BTreeLayout(...)]` attribute line.
2. Parse the layout method body: extract per-node `comment:`, `expressionTarget:`, and
   `.SubtreeSyncField(...)` binding metadata, keyed by `visualId`.
3. Walk the `CreateBuilder()` chain: inject `//` comment lines and sync-binding
   annotations (`// sync (in): Field <-- Path`) above each matching builder call.
   Humanize cross-asset GUID references inline (`// -> AssetName (BTree)`).
4. Truncate everything from `[BTreeLayout(...)]` onward; append the closing brace.
5. Strip the "; manual edits..." clause from the `HROT_EDITOR_GENERATED` header.
6. Normalize line endings to `\n`.

Sync-binding arrow conventions (ASCII-only):

| Direction | Annotation |
|-----------|-----------|
| `SyncDirection.In` | `// sync (in):  SubField <-- MasterPath` |
| `SyncDirection.Out` | `// sync (out): SubField --> MasterPath` |
| `SyncDirection.Both` | `// sync (both): SubField <--> MasterPath` |

### HSM — `HsmComparisonSanitizer`

Same structural approach as BTree, adapted for HSM's identifier model:
- `stableId` for states and regions.
- `visualId` for transitions and global transitions.
- `.State(...)`, `.Transition(...)`, `.Region(...)` layout entries are parsed
  for `comment:` only (no expressionTarget or sync bindings for HSM in Phase 1).

### Blackboard — `BlackboardComparisonSanitizer`

Simplest sanitizer. No layout method, no comment hoisting. Steps:

1. Read the inline `{Name}.Blackboard.cs` file.
2. Discover the optional `{Name}.HeavyBlackboard.cs` companion in the same directory.
3. Emit a labeled concatenation:

```csharp
// === Inline blackboard ===
{inline file content}

// === Heavy blackboard (overflow) ===   // (only when present)
{heavy file content}
```

XML `///` doc comments on struct fields are already canonical; no hoisting is needed.

### Blueprint — `BlueprintComparisonSanitizer`

Operates on the JSON DOM of `.bp.json` files. Steps:

0. **Migrate schema** via the injected `IComparisonMigrationAdapter` (no-op default
   until the full migration system is live). Sets `MigrationNotice` in metadata
   when migration actually occurred.
1. Parse the adapted JSON into a `JsonNode` DOM.
2. Walk all `EditorMetadata` objects at root, graph, and node levels:
   - Node-level: hoist `Comment` to `node.Comment`; strip `X`, `Y`, and all other keys.
   - Graph-level: hoist `CanvasComments[].Text` to a `_canvasComments` array on the
     graph; strip `Viewport`, `DockState`, `NodeViewStates`, and all other keys.
   - Root-level: strip entirely.
3. Strip diagnostic `$meta` fields (`engineVersion`, `createdBy`, `createdUtc`) via
   the injected `IMetaEnvelopeSanitizer` (no-op default); preserve `docType` and
   `schemaVersion`.
4. Humanize `CallPeerBlueprint` nodes: add `"_targetName": "Name (Blueprint)"` from
   the catalog; `"(asset not found in catalog)"` on miss.
5. Re-serialize the cleaned DOM with **alphabetically sorted keys** at every level
   for determinism.

---

## Companion-File Discovery

`CompanionFileDiscovery` auto-resolves companion files from the main asset file path
or a folder.

### Per-kind companion conventions

| Asset Kind | Main File | Companion Files |
|------------|-----------|-----------------|
| BTree | `{Name}_BT.cs` | `{Name}_BT.Blackboard.cs`, `{Name}_BT.HeavyBlackboard.cs`, `{Name}_BT.Orchestrators.g.cs` |
| HSM | `{Name}_HSM.cs` | `{Name}_HSM.Blackboard.cs`, `{Name}_HSM.HeavyBlackboard.cs`, `{Name}_HSM.Orchestrators.g.cs` |
| Blackboard | `{Name}.Blackboard.cs` | `{Name}.HeavyBlackboard.cs` |
| Blueprint | `{Name}.bp.json` | (none) |

### Folder mode

`DiscoverFromFolder(folderPath, targetAssetId, expectedKind)` scans for a file whose
AssetId matches `targetAssetId`, then resolves companions from the same folder.

- Main-asset files (`// AssetId:` header) score 2.
- Companion files (`// OwningAssetId:` header) score 1.
- Directories whose name starts with `.` are skipped entirely (protects against
  accidentally picking up `.migration-snapshots/` sidecar files).

---

## Asset Selection & Validation

`AssetSelectionValidator.Validate()` runs before the export is built:

| Rule | Result on failure |
|------|-------------------|
| Both main files must exist and be readable. | Error; export blocked. |
| Both files must be parseable enough to extract AssetId. | Error; export blocked. |
| Both files must have the same `AssetKind`. | Error; export blocked. |
| If AssetIds differ, warn but allow. | Warning; export allowed with notice. |

The dialog supports a **Reverse A<->B** button that swaps paths and toggles a
`Reversed` flag so downstream code knows whether the active asset was treated as
Version A or Version B.

---

## LLM Response Parsing

`LlmResponseParser.Parse(string responseText)` returns a `ComparisonResponse`. It
never throws; all parse failures produce warnings.

### Section detection

The parser expects two marker lines:

```
----- HUMAN SUMMARY -----
...prose paragraphs...

----- STRUCTURED CHANGES (JSON) -----
{...json object...}
```

If the markers are absent, the fallback locates the first `{` brace as the JSON
boundary and treats everything before it as the human summary.

### Robustness rules

| Condition | Handling |
|-----------|----------|
| JSON wrapped in ` ```json ... ``` ` fences | Fences stripped before parsing. |
| Unknown `kind` value | Normalized to `"node_modified"` with a `Warnings` entry. |
| Unknown `severity` value | Normalized to `"tuning"` with a `Warnings` entry. |
| Missing optional fields (`field`, `oldValue`, `newValue`) | Default to `null`. |
| Truncated JSON (LLM ran out of tokens) | Parser trims to last complete `}` and retries. On failure, returns a truncation-warning response instead of throwing. |

### Change kinds

| `kind` | Meaning |
|--------|---------|
| `node_added` | Node exists in B but not A. |
| `node_removed` | Node exists in A but not B. |
| `node_modified` | Node exists in both; a property changed. `field`, `oldValue`, `newValue` populated. |
| `variable_added` | Blackboard variable added. |
| `variable_removed` | Blackboard variable removed. |
| `variable_renamed` | Variable name changed. |
| `variable_retyped` | Variable type changed. |
| `connection_changed` | An edge was rewired. |
| `comment_changed` | A node or variable comment changed. |
| `intent_shift` | LLM's higher-level interpretation: a subgraph's purpose shifted. |

### Severity levels

| `severity` | Color (canvas) | Meaning |
|-----------|---------------|---------|
| `cosmetic` | Gray (60% opacity) | Rename, comment edit, reorder with no semantic effect. |
| `tuning` | Blue | Parameter value change (timing, counts, thresholds). |
| `feature` | Green | New functionality added. |
| `removal` | Red | Functionality removed. |
| `behavior` | Orange | A change that shifts the asset's overall behavior. |
| `intent_shift` | Orange | Same as `behavior` for color; higher-level LLM interpretation. |

---

## Visualization (Comparison Mode)

### ComparisonAnnotationRenderer

Registered as a custom canvas renderer with ID `"comparison.annotations"` at the
`CanvasRenderPass.AfterNodes` pass. Reads `ComparisonSessionRegistry` each frame.

For each visible change (severity in `EnabledSeverities`), draws:
- **A dashed outline**, 2 px wide, offset 3 px outward from the node's bounding box.
  Dash pattern: 6 px on / 4 px off, scaled inversely with canvas zoom so dashes
  always appear the same size regardless of zoom level.
- **A badge glyph** on the upper-right of the node.

Badge glyphs (ASCII-safe, rendered by ImGui):

| Kind | Glyph |
|------|-------|
| `node_added` | `+` |
| `node_removed` | `-` |
| `node_modified` | `~` |
| `variable_added` | `+v` |
| `variable_removed` | `-v` |
| `variable_renamed` | `>>>` |
| `variable_retyped` | `[]` |
| `connection_changed` | `~>` |
| `comment_changed` | `"` |
| `intent_shift` | `!!` |

The comparison outline coexists without z-fighting with the existing NodeEditor
outlines (Selected, Error, Warning, Executing) because it uses a dashed stroke
drawn 3 px outward, while all other outlines are solid and flush against the node
body.

### connection_changed badge placement

For `connection_changed` entries the renderer tries in order:
1. Badge at the edge midpoint when both endpoints exist in Version B.
2. Badge on the surviving endpoint when one endpoint was deleted.
3. Sidebar entry only when neither endpoint exists on the canvas.

### Removed nodes

Phase 1 ships sidebar-only for `node_removed`: the change entry appears in the
sidebar with a `(removed)` label and description; no canvas annotation (ghost
rendering is deferred to Phase 1.5).

### ComparisonSummaryPanel

Window ID: `ai_comparison_summary`. Docked panel, `WindowScope.PerspectiveBound`.

Displays:
1. Asset name + one-sentence `TopLevelSummary`.
2. Migration notice (when Version A's schema was migrated before comparison).
3. Full `HumanSummary` prose, scrollable.
4. Per-severity filter toggles. Default: `behavior`, `feature`, `removal`, `tuning`
   enabled; `cosmetic` disabled. Toggling a severity immediately hides/shows
   matching canvas outlines and sidebar entries.

### ComparisonSidebar

Window ID: `ai_comparison_sidebar`. Docked panel, `WindowScope.PerspectiveBound`.

Each row shows:
- Glyph + severity label.
- `ElementDescription`.
- For `node_modified` / `variable_retyped`: `field: oldValue -> newValue`.
- 1-2 lines of truncated description; click expands.

Clicking a row triggers the `FocusChange` callback, which calls
`_onFocusNode(change.ElementId)` so the host window can pan and zoom to center the
affected node.

Severity filters applied by `ComparisonSidebarState.VisibleChanges` via LINQ.

### Stale badge

`StaleBadgeWatcher.OnAssetSaved(assetId)` is called whenever an asset is saved.
If the asset has an active `ComparisonSessionState`, it calls `MarkStale()`. The
toolbar and title bar then show a "Comparison may be stale" chip.

### Exiting Comparison Mode

`ExitComparisonAction.Execute(assetId)` removes the session from
`ComparisonSessionRegistry`, which causes `ComparisonAnnotationRenderer.IsActive`
to return `false` and immediately suppresses all annotations. The summary panel and
sidebar clear on their next frame draw.

---

## Key Types Reference

### `SanitizerRegistry`

Singleton DI registration. Maps `AssetKind` to `IAssetComparisonSanitizer`.

```csharp
public sealed class SanitizerRegistry
{
    public void Register(IAssetComparisonSanitizer sanitizer);
    public IAssetComparisonSanitizer Get(AssetKind kind);         // throws if not registered
    public bool TryGet(AssetKind kind, out IAssetComparisonSanitizer? sanitizer);
}
```

### `ComparisonExportBuilder`

```csharp
public sealed class ComparisonExportBuilder
{
    public string Build(
        IAssetComparisonSanitizer sanitizer,
        AssetExportRequest versionA,
        AssetExportRequest versionB);
}
```

Calls `sanitizer.Sanitize()` for both versions, emits the full export string with
the fixed instruction block and normalized `\n` line endings.

### `LlmResponseParser`

```csharp
public static class LlmResponseParser
{
    public static ComparisonResponse Parse(string responseText);
}
```

### `ComparisonSessionState`

```csharp
public sealed class ComparisonSessionState
{
    public Guid AssetId { get; }
    public ComparisonResponse Response { get; }
    public string? MigrationNotice { get; }
    public bool IsStale { get; }
    public IReadOnlySet<string> EnabledSeverities { get; }

    public void ToggleSeverity(string severity);
    public void MarkStale();
}
```

Default enabled severities: `behavior`, `feature`, `removal`, `tuning`.

### `ComparisonSessionRegistry`

Singleton DI registration. Maps `Guid assetId -> ComparisonSessionState`.

```csharp
public sealed class ComparisonSessionRegistry
{
    public ComparisonSessionState? GetSession(Guid assetId);
    public void SetSession(ComparisonSessionState state);
    public void ClearSession(Guid assetId);
}
```

### `ComparisonResponse` / `ComparisonChange`

```csharp
public sealed record ComparisonResponse(
    string? HumanSummary,
    string TopLevelSummary,
    IReadOnlyList<ComparisonChange> Changes,
    IReadOnlyList<string> Warnings);

public sealed record ComparisonChange(
    string Kind,
    string? ElementId,
    string ElementDescription,
    string? Field,
    string? OldValue,
    string? NewValue,
    string Severity,
    string Description);
```

### `IComparisonMigrationAdapter` / `IMetaEnvelopeSanitizer`

Both have no-op default implementations shipped alongside the feature:

```csharp
public interface IComparisonMigrationAdapter
{
    string Adapt(string jsonText, out bool didMigrate);
}

public interface IMetaEnvelopeSanitizer
{
    JsonObject StripDiagnosticFields(JsonObject metaEnvelope);
}
```

`NoOpComparisonMigrationAdapter` returns the input unchanged (`didMigrate = false`).
`NoOpMetaEnvelopeSanitizer` returns the envelope unchanged. Both are registered as
defaults in the DI container; production implementations swap in once the JSON
migration system is fully live.

### `ComparisonStyleMap`

```csharp
public static class ComparisonStyleMap
{
    public static Vector4 ColorForSeverity(string severity);  // returns RGBA
    public static string GlyphForKind(string kind);           // returns ASCII glyph
}
```

Unknown severities return neutral gray `(0.5, 0.5, 0.5, 0.6)`. Unknown kinds
return `"?"`.

### `AssetSelectionDialogState`

Testable state model (no ImGui dependencies):

```csharp
public sealed class AssetSelectionDialogState
{
    public string PathA { get; set; }
    public string PathB { get; set; }
    public bool Reversed { get; }
    public string? ValidationError { get; }
    public string? ValidationWarning { get; }

    public void Reverse();
    public string? Validate(AssetKind expectedKind);
    public AssetSelectionResult? BuildResult(AssetKind expectedKind);
}
```

### `CompanionFileDiscovery`

```csharp
public static class CompanionFileDiscovery
{
    public static DiscoveredAsset DiscoverFromMainFile(string mainFilePath, AssetKind expectedKind);
    public static DiscoveredAsset? DiscoverFromFolder(string folderPath, Guid targetAssetId, AssetKind expectedKind);
}

public sealed record DiscoveredAsset(string MainFilePath, IReadOnlyList<DiscoveredCompanion> Companions);
public sealed record DiscoveredCompanion(string FilePath, bool Exists);
```

---

## DI Registration

### Shared services (added by `AddSharedAiEditor()` in `Hrot.Editor.AiShared.Di`)

The following comparison-specific singletons are registered as part of
`AddSharedAiEditor()`:
- `SanitizerRegistry`
- `ComparisonExportBuilder`
- `ComparisonSessionRegistry`
- `StaleBadgeWatcher`
- `ComparisonAnnotationRenderer`
- `ComparisonSummaryPanel`
- `ComparisonSidebar`
- `NoOpComparisonMigrationAdapter` (as `IComparisonMigrationAdapter`)
- `NoOpMetaEnvelopeSanitizer` (as `IMetaEnvelopeSanitizer`)
- `BlackboardComparisonSanitizer` (auto-registered into `SanitizerRegistry`)

### Per-kind extensions

Each editor project provides a DI extension that registers its sanitizer and wires
it into `SanitizerRegistry` at first resolution:

```csharp
// BTree
services.AddBTreeEditorComparison();    // registers BTreeComparisonSanitizer

// HSM
services.AddHsmEditorComparison();      // registers HsmComparisonSanitizer

// Blueprint
services.AddBlueprintEditorComparison(); // registers BlueprintComparisonSanitizer
```

Blackboard has no separate extension; its sanitizer is registered in
`AddSharedAiEditor()` because `BlackboardComparisonSanitizer` lives in the shared
project.

### Per-kind toolbar integration

Each editor host window instantiates a thin toolbar wrapper that delegates to the
shared `ComparisonToolbarAction`:

```csharp
// BTree example
var toolbar = new BTreeComparisonToolbar(
    sanitizerRegistry, exportBuilder, sessionRegistry);

// Each frame:
toolbar.DrawToolbar(activeAsset);  // renders buttons + all modals
```

`ComparisonToolbarAction.Render()` manages three modals internally:
`AssetSelectionDialog`, `ExportDeliveryModal`, `PasteResponseModal`.

---

## Source File Index

### Shared — `Hrot/Editor/Hrot.Editor.AiShared/Comparison/`

| File | Type | Purpose |
|------|------|---------|
| `IAssetComparisonSanitizer.cs` | Interface + records | Sanitizer contract; `AssetExportRequest`, `SanitizationResult`, `AssetMetadataBlock`, `SanitizationWarning`. |
| `IComparisonMigrationAdapter.cs` | Interface | JSON schema migration hook for Blueprint sanitizer. |
| `IMetaEnvelopeSanitizer.cs` | Interface | `$meta` diagnostic-field strip hook for Blueprint sanitizer. |
| `NoOpComparisonMigrationAdapter.cs` | `sealed class` | Pass-through default; `didMigrate = false`. |
| `NoOpMetaEnvelopeSanitizer.cs` | `sealed class` | Pass-through default. |
| `SanitizerRegistry.cs` | `sealed class` | `AssetKind -> IAssetComparisonSanitizer` registry. |
| `ComparisonExportBuilder.cs` | `sealed class` | Assembles full export text with fixed instruction block. |
| `CompanionFileDiscovery.cs` | `static class` | Discovers companion files; handles dot-prefixed directory exclusion. |
| `AssetSelectionValidator.cs` | `static class` | Pre-comparison validation rules; returns `ValidationResult`. |
| `LlmResponseParser.cs` | `static class` | Parses LLM response text into `ComparisonResponse`. |
| `ComparisonResponse.cs` | Records | `ComparisonResponse`, `ComparisonChange`. |
| `ComparisonSessionState.cs` | `sealed class` + `sealed class` | Per-asset session state; `ComparisonSessionRegistry`. |
| `BlackboardComparisonSanitizer.cs` | `sealed class` | Blackboard-specific sanitizer (inline + heavy concatenation). |
| `BlackboardComparisonDecorator.cs` | `sealed class` | Adds severity outlines to variable rows in `BlackboardAuthoringWindow`. |
| `ResponseAssetMatcher.cs` | `sealed class` | Detects response/asset mismatch before applying a paste. |
| `StaleBadgeWatcher.cs` | `sealed class` | Marks sessions stale on asset save. |
| `ComparisonErrorMessages.cs` | `static class` | User-facing error message string constants. |
| `Rendering/ComparisonAnnotationRenderer.cs` | `sealed class` | `ICustomCanvasRenderer`; draws outlines + badges at `AfterNodes`. |
| `Rendering/ComparisonStyleMap.cs` | `static class` | Severity -> RGBA color, kind -> ASCII glyph. |
| `UI/ComparisonToolbarAction.cs` | `sealed class` | Toolbar button coordinator; owns three modals. |
| `UI/AssetSelectionDialog.cs` | `sealed class` + state model | Asset-selection dialog with file/folder modes and reversal. |
| `UI/ExportDeliveryModal.cs` | `sealed class` | Save/copy/preview delivery modal; 8 MB threshold guard. |
| `UI/PasteResponseModal.cs` | `sealed class` | Multi-line paste or load-from-file modal. |
| `UI/ExitComparisonAction.cs` | `sealed class` | Clears session from registry on demand. |
| `UI/ComparisonSummaryPanel.cs` | `sealed class` + state model | Docked summary panel with severity filter toggles. |
| `UI/ComparisonSidebar.cs` | `sealed class` + state model | Docked change list; click-to-focus. |

### BTree — `Hrot/Subsystems/AI/Hrot.BTree.Editor/Comparison/`

| File | Type | Purpose |
|------|------|---------|
| `BTreeComparisonSanitizer.cs` | `sealed class` | BTree-specific sanitizer: layout parse, comment hoist, sync-binding hoist, GUID humanization, layout truncation. |
| `BTreeComparisonToolbar.cs` | `sealed class` | Thin host wrapper delegating to `ComparisonToolbarAction`. |
| `BTreeEditorComparisonServiceCollectionExtensions.cs` | `static class` | `AddBTreeEditorComparison()` DI extension. |

### HSM — `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Comparison/`

| File | Type | Purpose |
|------|------|---------|
| `HsmComparisonSanitizer.cs` | `sealed class` | HSM-specific sanitizer: layout parse (stableId/visualId), comment hoist, layout truncation. |
| `HsmComparisonToolbar.cs` | `sealed class` | Thin host wrapper. |
| `HsmEditorComparisonServiceCollectionExtensions.cs` | `static class` | `AddHsmEditorComparison()` DI extension. |

### Blueprint — `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Comparison/`

| File | Type | Purpose |
|------|------|---------|
| `BlueprintComparisonSanitizer.cs` | `sealed class` | Blueprint-specific sanitizer: JSON DOM walk, EditorMetadata hoist/strip, `$meta` strip, GUID humanization, alphabetical re-serialization. |
| `BlueprintEditorComparisonServiceCollectionExtensions.cs` | `static class` | `AddBlueprintEditorComparison()` DI extension. |

---

## Test Coverage

| Test project | Test files | What is tested |
|---|---|---|
| `Hrot.Editor.AiShared.Tests/Comparison/` | 25 files | All shared types: sanitizer registry, blackboard sanitizer, companion-file discovery, asset selection dialog, validator, export builder, LLM response parser, all robustness fixture cases, annotation renderer, style map, summary panel, sidebar, toolbar action, paste modal, exit action, stale-badge watcher, response-asset matcher, error messages, session state, no-op adapters. |
| `Hrot.BTree.Editor.Tests/Comparison/` | 5 files | BTree sanitizer determinism (property tests), self-comparison round-trip, full sanitization with comment hoist, sync-binding hoist, and GUID humanization. Includes a `FakeCatalogHelper` and fixture `.cs` files. |
| `Hrot.Hsm.Editor.Tests/Comparison/` | 5 files | HSM sanitizer determinism, self-comparison, full sanitization with stableId/visualId handling. |
| `Hrot.Blueprints.Tests/Comparison/` | 1 file | Blueprint sanitizer with `DeepNestedBlueprint.bp.json` fixture (EditorMetadata strip, property ordering, GUID humanization). |

Fixture corpus for `LlmResponseParser` tests (`AiShared.Tests/Comparison/Fixtures/Responses/`):

| Fixture | Tests |
|---------|-------|
| `well_formed.txt` | Normal parse path. |
| `markdown_fenced.txt` | ` ```json ``` ` fence stripping. |
| `extra_leading_prose.txt` | Fallback section detection. |
| `truncated_recoverable.txt` | Last-`}` recovery. |
| `truncated_unrecoverable.txt` | Truncation-warning response. |
| `unknown_kind.txt` | Kind normalization + warning. |
| `unknown_severity.txt` | Severity normalization + warning. |
| `missing_required_field.txt` | Missing optional fields default to null. |
| `unresolvable_element_ids.txt` | Parser succeeds; renderer degrades gracefully. |

---

## Design Invariants

1. **No LLM calls from the editor.** The feature produces and consumes text artifacts
   only. No network access, no API keys, no vendor dependency.
2. **Sanitization is deterministic.** Same input file -> same sanitized output,
   byte-identical across runs. Required for the self-comparison no-changes test.
3. **Sanitization is in-memory.** No intermediate artifacts are written to disk.
4. **No kernel-side changes.** The feature is entirely editor-side; `Fbt.Kernel`,
   `Fhsm.Kernel`, and the Blueprint runtime are unaffected.
5. **No new project-level artifacts.** The feature never writes `.editor-state` files
   or similar sidecars into the user's repository; export artifacts are written only
   when the user explicitly clicks Save and picks a path.
6. **Comparison Mode does not block authoring.** The user can continue editing the
   asset while a comparison is loaded. Annotations remain pinned to their visualIds.
