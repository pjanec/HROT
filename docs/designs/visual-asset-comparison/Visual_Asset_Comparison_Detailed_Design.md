# Visual Asset Comparison — Detailed Design

> **Status:** Detailed design for the asset-comparison feature in the visual AI editor. Derived from `AI_Editor_Shared_Infrastructure.md` + `BTree_Editor_NodeEditor_Host_Design.md` + `HSM_Editor_NodeEditor_Host_Design.md` + `Blackboard_Authoring_Detailed_Design.md` + the design conversations with the architect about export sanitization.
> **Audience:** Implementation agent and human reviewer.
> **Drives:** A feature that lets a designer compare two versions of a visually-authored asset by exporting both versions as sanitized text, handing the export to an out-of-band LLM, and pasting the LLM's structured response back into the editor for visual annotation on the canvas.
> **Doesn't cover:** Sibling-asset diff (deferred to Phase 2). Automatic LLM invocation (the editor never calls an LLM; it produces an export and consumes a response, both as text artifacts the user shuttles manually). Git integration (the editor does not read git). In-editor snapshot history (deferred).
> **Companion code lives in:** `Hrot/Subsystems/AI/Hrot.Editor.AiShared/Comparison/`. Subsystem-specific sanitizers live alongside their respective editors: `Hrot.BTree.Editor/Comparison/`, `Hrot.Hsm.Editor/Comparison/`, `Hrot.Blueprint.Editor/Comparison/`.

---

## Table of Contents

1. Scope and design goals
2. The end-to-end workflow
3. The export pipeline
4. The export text file format
5. The LLM contract
6. The re-import and visualization
7. The asset-selection UX
8. Required additions to existing infrastructure
9. Slice plan
10. Test strategy
11. Open questions

---

## 1. Scope and design goals

### 1.1 Why this feature exists

Designers periodically need to know "what changed" between two versions of a visually-authored asset — a BTree, HSM, Blueprint, or editor-managed Blackboard. The use cases:

- **PR review.** Reviewer wants to know what the author changed before approving.
- **AI-agent edit audit.** An AI coding agent modified the asset (a Claude Code session, a Cursor edit, etc.); the human wants to review what was done.
- **Refactor verification.** The designer made a large restructure and wants reassurance the behavior is "the same" before merging.
- **Bug-hunt regression.** "It worked yesterday; what changed?"

The naive solution — visual graph diff rendered on the canvas — is famously bad. Node positions shift incidentally; edges re-route; the eye drowns in coordinate noise. Real semantic changes (a new branch, a tuned parameter, an intent shift) get lost in the visual churn.

This design solves the problem by outsourcing the *semantic interpretation* to an LLM and using the editor only for *visual annotation* of changes the LLM identifies. The editor produces a sanitized text export; the user hands it to whatever LLM they prefer; the LLM produces a structured response identifying changes; the user pastes the response back; the editor annotates affected nodes on the canvas with colored outlines and badges.

The editor never invokes an LLM directly. It produces an export artifact and consumes a response artifact. Both are plain text shuttled by the user via copy-paste or file. This decouples the comparison feature from any specific LLM vendor, billing model, or network availability — and lets the user pick a model appropriate to the asset's complexity (fast cheap model for small diffs; capable model for large refactors).

### 1.2 What's in Phase 1

- **Export pipeline:** sanitize a BTree, HSM, Blackboard, or Blueprint asset into a deterministic, LLM-friendly text representation. Strip presentation noise (canvas positions, pan/zoom, EditorMetadata). Preserve semantic content (topology, parameter values, comments, blackboard schema). Keep visualIds intact for historical-diff correlation.
- **Comparison text file format:** an instruction block at the top, followed by per-version metadata blocks, followed by sanitized old and new content with clear separators. Delivered to the user as a text artifact via save-to-file or copy-to-clipboard.
- **LLM contract:** an instruction block that asks the LLM to produce both a human-readable prose summary AND a structured JSON block identifying each change with a kind, severity, affected visualId, and description.
- **Re-import and visualization:** the user pastes the LLM's response back; the editor parses the JSON, displays the prose summary in an auto-opening side panel, and annotates affected nodes on the canvas with colored outlines and badges.
- **Asset-selection UX:** user picks two assets (each as either a single file or a folder); editor opportunistically resolves companion files; the comparison runs.

### 1.3 What's deferred to Phase 2 or later

- **Sibling-asset diff** — comparing two structurally-similar but distinct assets (e.g., `OrcGuard_BT` vs. `OrcGuardElite_BT`). Requires deterministic ID renumbering with reference rewriting; deferred per architect's cost-vs-value analysis.
- **Git integration** — "Compare with previous commit" or revision picker. Deferred; user obtains the old version externally (git checkout, file copy) and feeds the file path to the editor.
- **In-editor snapshot history** — automatic rolling snapshots of every authored asset. Deferred; users use git or external backups.
- **Automatic LLM invocation** — the editor calls an LLM API directly using a configured key. Deferred; the user shuttles text artifacts manually.
- **Cross-asset-kind comparison** — diffing a BTree against an HSM. Genuinely hard semantically and rarely needed. Not on the roadmap.
- **Multi-asset comparison** — comparing two whole project subtrees. Out of scope; the user can run comparisons individually.

### 1.4 Required reading

This DD assumes familiarity with:

- BTH §3, §4 (BTree asset model, fluent emitter — knowing what the C# files look like).
- HSH §3, §4 (HSM asset model, fluent emitter).
- Blackboard DD §3, §3.5 (DTO file structure, editor-managed vs read-only-passthrough fields).
- Shared infra §4.3 (FQN reference catalog — touched lightly for asset identity).
- The architect's earlier confirmation that:
  - VisualIds are stable across same-asset historical diffs.
  - Layout-method content is presentation noise except for embedded `comment:` and `expressionTarget:` arguments.
  - Blueprint `EditorMetadata` blocks are presentation noise.
  - Blueprint `Id` references on nodes/pins/links are stable across same-asset historical diffs.

### 1.5 Design goals

- **No editor-side LLM calls.** The editor produces and consumes text artifacts only.
- **The user picks the LLM.** No vendor lock-in. The export must be vendor-neutral; the LLM contract is just a clearly-described task in the instruction block.
- **Phase 1 is historical-diff only.** Both versions are presumed to be the same asset at different points in time. VisualIds correlate. Sibling-diff is Phase 2.
- **Sanitization is deterministic.** Same input → same sanitized output, byte-identical. This is necessary for the round-trip test (no-edit comparison should produce no changes).
- **Sanitization is in-memory; no parallel persistent schema.** The export is derived from the canonical save format (C# for BTree/HSM/Blackboards, JSON for Blueprints) via a sanitization pipeline that runs on-demand. No separate "export format" file is maintained alongside the canonical files.
- **Visualization is annotation, not redraw.** The editor doesn't try to render a diff visualization of the graph. It annotates affected nodes with outlines and badges; the user reads details in a side panel.

---

## 2. The end-to-end workflow

```
┌────────────────────────────────────────────────────────────────────────────┐
│                                                                            │
│  ┌────────────────┐                                                        │
│  │ 1. User clicks │                                                        │
│  │  "Compare With"│                                                        │
│  └───────┬────────┘                                                        │
│          │                                                                 │
│          ▼                                                                 │
│  ┌────────────────────────────┐                                            │
│  │ 2. User selects two        │                                            │
│  │   versions (files/folders) │                                            │
│  └───────┬────────────────────┘                                            │
│          │                                                                 │
│          ▼                                                                 │
│  ┌────────────────────────────┐                                            │
│  │ 3. Editor sanitizes both,  │                                            │
│  │   builds comparison text,  │                                            │
│  │   shows in modal window    │                                            │
│  └───────┬────────────────────┘                                            │
│          │                                                                 │
│          │  [Save to file]   [Copy to clipboard]                           │
│          ▼                                                                 │
│  ┌────────────────────────────┐                                            │
│  │ 4. User pastes into LLM    │     ←─── happens out of editor             │
│  │   of their choice          │                                            │
│  │   (Claude, ChatGPT, etc.)  │                                            │
│  └───────┬────────────────────┘                                            │
│          │                                                                 │
│          ▼                                                                 │
│  ┌────────────────────────────┐                                            │
│  │ 5. LLM returns prose +     │                                            │
│  │   structured JSON block    │                                            │
│  └───────┬────────────────────┘                                            │
│          │                                                                 │
│          ▼                                                                 │
│  ┌────────────────────────────┐                                            │
│  │ 6. User clicks             │                                            │
│  │   "Paste LLM Response"     │                                            │
│  │   in editor                │                                            │
│  └───────┬────────────────────┘                                            │
│          │                                                                 │
│          ▼                                                                 │
│  ┌────────────────────────────┐                                            │
│  │ 7. Editor parses response, │                                            │
│  │   shows prose in side      │                                            │
│  │   panel, annotates affected│                                            │
│  │   nodes on canvas          │                                            │
│  └────────────────────────────┘                                            │
│                                                                            │
└────────────────────────────────────────────────────────────────────────────┘
```

Steps 1–3 are export. Step 4 is the user's LLM interaction (entirely outside the editor). Steps 6–7 are re-import and visualization.

The editor has no awareness of which LLM the user is using and does not depend on the LLM's response time, reliability, or capabilities beyond producing the requested format. If the LLM produces malformed output, the editor surfaces a parse error and lets the user fix the response and re-paste.

---

## 3. The export pipeline

### 3.1 The sanitization principle

Each asset kind has a canonical save format that contains both semantic content (what the asset does) and presentation noise (how it's laid out on the canvas). The sanitization pipeline preserves the semantic content and strips the presentation noise. Comments — being semantic — are preserved everywhere; in BTree/HSM where comments live in the layout method, they're hoisted inline before layout truncation.

The pipeline is **deterministic**: same input file → same sanitized output, byte-identical across runs. This is required so that comparing an asset against itself produces an empty diff.

The pipeline is **in-memory**: it operates on either the asset's loaded editor model or the file's text content; no intermediate artifacts are persisted to disk.

### 3.2 Per-asset-kind sanitization

Each asset kind has its own sanitizer registered via `IAssetComparisonSanitizer`:

```csharp
public interface IAssetComparisonSanitizer
{
    AssetKind TargetKind { get; }

    /// <summary>
    /// Read the canonical file (or set of companion files), produce sanitized text
    /// ready for LLM consumption. Returns the sanitized text plus metadata.
    /// </summary>
    SanitizationResult Sanitize(AssetExportRequest request);
}

public sealed record AssetExportRequest(
    string AssetMainFilePath,            // path to the canonical asset file
    string? CompanionDirectoryPath,      // typically the asset file's directory
    AssetKind ExpectedKind);

public sealed record SanitizationResult(
    string SanitizedText,                // the LLM-ready text
    AssetMetadataBlock Metadata,         // for the per-version header
    IReadOnlyList<SanitizationWarning> Warnings);

public sealed record AssetMetadataBlock(
    string AssetName,
    AssetKind Kind,
    Guid AssetId,
    string SourceFilePath,
    IReadOnlyList<string> CompanionFiles,    // e.g., .Blackboard.cs, .HeavyBlackboard.cs
    DateTime? LastModifiedTimestamp);
```

### 3.3 BTree and HSM sanitization

Both editors emit C# fluent-builder files with three methods: `CreateBuilder()`, the `[XxxDefinition]` thunk, and the `[XxxLayout]` method. The sanitizer's job:

1. **Locate the `[BTreeLayout]` or `[HsmLayout]` method's start** by string match against the canonical opening (the emitter guarantees a stable form: `[BTreeLayout(...)]` / `[HsmLayout(...)]` on its own line followed by the method declaration).
2. **Parse the layout method's body** to extract per-element metadata:
   - For each `.State(stableIdString, position?, size?, comment?, …)` or `.Node(visualIdString, position?, comment?, expressionTarget?, …)` entry, capture the visualId/stableId, the `comment:` string if present, and (BTree only) the `expressionTarget:` string if present.
   - For each `.SubtreeSyncField(visualIdString, subDtoField, masterPath, direction)` entry (per Blackboard DD §8), capture the visualId, sub-DTO field name, master path expression, and direction (`In`, `Out`, or `Both`). These are semantic content (Approach B parameter bindings), not presentation, and must survive the truncation.
   - Ignore `position:`, `size:`, `panOffset:`, `zoomLevel:`, `waypoints:`, and any other purely-positional arguments.
3. **Walk the `CreateBuilder()` chain** to find each builder call by its visualId/stableId argument. For each call:
   - If a comment was captured, inject the comment as a `//` line above the call.
   - For BTree builder calls that have a captured expression target, ensure it remains an argument of the call (it's already typically there; verify).
   - If sync bindings were captured (only meaningful for `.Subtree(...)` calls), inject one `// sync (in|out): subDtoField <-- masterPath` or `// sync (out): subDtoField --> masterPath` line per binding above the call. Bidirectional bindings emit as `// sync (both): subDtoField <--> masterPath`. The arrows are ASCII-only (`<--`, `-->`, `<-->`) so the export is plain-text-safe.
4. **Humanize cross-asset GUID references.** For any builder argument whose value is a GUID identifying another asset (today: `.Subtree(assetIdGuidString, …)`'s first argument; future: any similar reference), look up the asset name and kind in `IAssetCatalog` and append an inline `// -> AssetName (AssetKind)` comment after the argument. If the lookup fails, append `// -> (asset not found in catalog)`. This decoration runs on the builder-chain walk in step 3.
5. **Truncate the file at the layout method's opening line.** Everything from `[BTreeLayout(…)]` onward is discarded. The semantic content from the layout method (comments, expression targets, sync bindings) has already been hoisted into the builder chain in steps 3–4.
6. **Strip the `[XxxDefinition]` thunk's body** if it's just a delegation to `CreateBuilder()` — emit the attribute line, then the method signature, then the body call to `CreateBuilder()`, then close. The thunk carries no extra semantic content beyond what's in `CreateBuilder()`.
7. **Preserve the using directives** — they tell the LLM what namespaces are in scope, which helps it interpret action FQNs.
8. **Preserve the `namespace X` declaration** — adds context.
9. **Preserve the file header marker comment** (`HROT_EDITOR_GENERATED`, AssetId, asset name) — useful as the LLM's first signal of what kind of asset it's looking at.

The sanitized output is a self-contained C# file that compiles (in principle — we don't actually compile it) and that reads like a clean, comment-rich fluent builder definition with semantic annotations co-located next to the constructs they describe.

#### Example: BTree before and after sanitization

Before (canonical `.cs` file):

```csharp
// HROT_EDITOR_GENERATED — managed by AI editor; manual edits to this file will be overwritten on next save.
// AssetId: f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21

using Hrot.Game.Combat;
using Fbt.Compiler;

namespace Hrot.AI.Behaviors.Trees;

public static class OrcGuard
{
    public static BTreeBuilder<OrcGuard_BT_Blackboard, BTreeContext> CreateBuilder() =>
        new BTreeBuilder<OrcGuard_BT_Blackboard, BTreeContext>()
            .Sequence(s => s
                .Condition(dto => dto.ThreatVisible, CombatActions.HasThreat,
                           visualId: new Guid("a3f2c5d8-9c01-4b2e-8d7a-1f6e5c4b3a29"))
                .Action(dto => dto.AmmoCount, CombatActions.AimAndFire,
                        visualId: new Guid("c5e8b471-7a44-4d6e-9b1c-8f7a6e5d4c3b")),
                visualId: new Guid("f7c01188-1188-4c5d-9e3a-7b6c5d4e3f21"));

    [BTreeDefinition("OrcGuard_BT", AssetId = "f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21")]
    public static BehaviorTreeBlob Build() => CreateBuilder().Compile("OrcGuard_BT");

    [BTreeLayout("f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21")]
    public static BTreeEditorLayout Layout() => new BTreeEditorLayoutBuilder()
        .Canvas(panOffset: new Vector2(12f, -34f), zoomLevel: 1.0f)
        .Node("a3f2c5d8-9c01-4b2e-8d7a-1f6e5c4b3a29",
              position: new Vector2(120f, 340f),
              comment: "must see enemy before engaging")
        .Node("c5e8b471-7a44-4d6e-9b1c-8f7a6e5d4c3b",
              position: new Vector2(280f, 480f),
              expressionTarget: "AmmoCount",
              comment: "burst fire pattern")
        .Node("f7c01188-1188-4c5d-9e3a-7b6c5d4e3f21",
              position: new Vector2(400f, 60f))
        .Build();
}
```

After sanitization:

```csharp
// HROT_EDITOR_GENERATED — managed by AI editor.
// AssetId: f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21

using Hrot.Game.Combat;
using Fbt.Compiler;

namespace Hrot.AI.Behaviors.Trees;

public static class OrcGuard
{
    public static BTreeBuilder<OrcGuard_BT_Blackboard, BTreeContext> CreateBuilder() =>
        new BTreeBuilder<OrcGuard_BT_Blackboard, BTreeContext>()
            .Sequence(s => s
                // must see enemy before engaging
                .Condition(dto => dto.ThreatVisible, CombatActions.HasThreat,
                           visualId: new Guid("a3f2c5d8-9c01-4b2e-8d7a-1f6e5c4b3a29"))
                // burst fire pattern
                .Action(dto => dto.AmmoCount, CombatActions.AimAndFire,
                        visualId: new Guid("c5e8b471-7a44-4d6e-9b1c-8f7a6e5d4c3b")),
                visualId: new Guid("f7c01188-1188-4c5d-9e3a-7b6c5d4e3f21"));

    [BTreeDefinition("OrcGuard_BT", AssetId = "f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21")]
    public static BehaviorTreeBlob Build() => CreateBuilder().Compile("OrcGuard_BT");
}
```

The Layout method is gone; pan/zoom and positions are gone; comments survive as `//` lines above their builder calls; visualIds are preserved for LLM correlation.

#### Example: Subtree with sync bindings and asset-GUID humanization

A more complex BTree that hosts a sub-tree via Approach B sync (per Blackboard DD §8) and uses a Subtree reference (per BTH §8). The sanitizer must preserve the sync semantics and humanize the cross-asset reference.

Before (excerpt):

```csharp
public static BTreeBuilder<MasterBlackboard, BTreeContext> CreateBuilder() =>
    new BTreeBuilder<MasterBlackboard, BTreeContext>()
        .Subtree("00000000-aaaa-0001-0000-000000000005",
                 visualId: new Guid("d4e5f607-aaaa-4321-8765-1a2b3c4d5e6f"));

[BTreeLayout("...")]
public static BTreeEditorLayout Layout() => new BTreeEditorLayoutBuilder()
    .Node("d4e5f607-aaaa-4321-8765-1a2b3c4d5e6f",
          position: new Vector2(280f, 200f),
          comment: "delegate to shoot subtree")
    .SubtreeSyncField("d4e5f607-aaaa-4321-8765-1a2b3c4d5e6f",
                      subDtoField: "TargetNetworkId",
                      masterPath: "SharedTarget",
                      direction: SyncDirection.In)
    .SubtreeSyncField("d4e5f607-aaaa-4321-8765-1a2b3c4d5e6f",
                      subDtoField: "StatusOut",
                      masterPath: "LastFireStatus",
                      direction: SyncDirection.Out)
    .Build();
```

After sanitization:

```csharp
public static BTreeBuilder<MasterBlackboard, BTreeContext> CreateBuilder() =>
    new BTreeBuilder<MasterBlackboard, BTreeContext>()
        // delegate to shoot subtree
        // sync (in):  TargetNetworkId <-- SharedTarget
        // sync (out): StatusOut       --> LastFireStatus
        .Subtree("00000000-aaaa-0001-0000-000000000005",  // -> Shoot_BT (BTree)
                 visualId: new Guid("d4e5f607-aaaa-4321-8765-1a2b3c4d5e6f"));
```

Three semantic elements hoisted out of the layout method into co-located `//` comments:
- The node comment (`// delegate to shoot subtree`).
- The Sync In binding (`// sync (in): TargetNetworkId <-- SharedTarget`).
- The Sync Out binding (`// sync (out): StatusOut --> LastFireStatus`).

Plus the cross-asset reference is humanized inline (`// -> Shoot_BT (BTree)`). The LLM sees both the visualId for correlation and the asset name for semantic understanding. Sync arrows use ASCII forms (`<--`, `-->`, `<-->`) for compatibility.

### 3.4 Blackboard DTO sanitization

A blackboard asset may be one or two files: the inline `.Blackboard.cs` and optionally `.HeavyBlackboard.cs` when the bin-packer overflowed the 100-byte inline budget. Both files together describe the asset's data layout.

The sanitizer:

1. **Locates both files** by naming convention. If only the inline file exists, that's fine (the asset fits inline).
2. **Reads each file's content.** Blackboard files are structurally simple — a partial struct with field declarations and `///` XML doc comments. The sanitizer can pass the content through nearly verbatim.
3. **Preserves comments natively** — `///` blocks above fields are already canonical C# documentation; no hoisting needed (unlike BTree/HSM where comments are in the layout method).
4. **Optionally strips `[StructLayout]` and other struct-level attributes** — these are emit details, not semantic content. For Phase 1, we keep them — they're concise and they make the file self-explanatory.
5. **Emits both files as a labeled concatenation**: "// === Inline blackboard ===" followed by the inline file's content; if heavy exists, "// === Heavy blackboard (overflow) ===" followed by the heavy file's content. This makes it obvious to the LLM that the two files are facets of one asset.

If only the inline file exists in version A but both files exist in version B, the LLM sees one section in A and two in B — making it obvious that a variable crossed the heavy threshold.

### 3.5 Blueprint sanitization

Blueprint assets are stored as `.bp.json` files. The schema (per architect Q1 confirmation) has Root Metadata, State Declarations, Interfaces, an array of Graph objects (each with Inputs, Outputs, Nodes, Links), and an `EditorMetadata` block. Per the Migration System, every versioned JSON document also carries a `$meta` envelope at root level with `docType`, `schemaVersion`, and diagnostic fields.

Critically, the `EditorMetadata` block holds **both** presentation data (positions, viewport, dock state) **and semantic content** (per-node comments, canvas comments, node view states that flag execution intent). The sanitizer cannot strip the whole block without destroying designer comments — it has to hoist the semantic parts first, then drop the rest.

The `$meta` envelope also contains both load-bearing fields (`docType`, `schemaVersion` — the LLM needs these to interpret the format) and diagnostic-only fields (`engineVersion`, `createdBy`, `createdUtc` — these change on every save and would produce noise diffs).

The sanitizer:

0. **Up-migrate the document via `IComparisonMigrationAdapter`** before any other processing. The sanitizer receives an injected adapter (typically wrapping the engine's `ReadOnlyMigrationAdapter`); see §8.1. The adapter takes the file's raw text and returns a DOM at the current schema version. If Version A is at schema v3 and Version B is at v4, both DOMs are now at v4 after this step — the LLM never sees the schema-shift noise. If the adapter can't migrate (chain gap), it returns the DOM as-is; the comparison proceeds with potentially noisy output but does not refuse. The migration-adapter dependency may be a no-op implementation during initial rollout until the migration system is fully landed.
1. **Walk the `$meta` envelope** at the root of the DOM:
   - **Preserve** `docType` and `schemaVersion` — load-bearing context for the LLM.
   - **Strip** `engineVersion`, `createdBy`, `createdUtc` — diagnostic-only fields that change on every save and would generate noise diffs. The injected `IMetaEnvelopeSanitizer` performs this strip; like the migration adapter, it is an injected dependency with a no-op default implementation that becomes meaningful once `$meta` envelopes are widely deployed.
2. **Walk every `EditorMetadata` block** at every level (root-level and per-node) and classifies its contents:
   - **Hoist** — per-node comments, canvas comments, any text content with semantic meaning. These get re-attached to their owning node or graph as a top-level property the sanitized output preserves. For per-node comments, the comment moves from `node.EditorMetadata.Comment` to `node.Comment` (a new top-level key on the node object that exists only in the sanitized output, not in the canonical file).
   - **Strip** — positions (`X`, `Y`), viewport state, dock state, sub-window layouts, and any other purely-presentation values.
3. **After the hoist/strip pass, remove every now-empty (or presentation-only) `EditorMetadata` block** from the DOM.
4. **Humanize cross-asset GUID references.** Walk the DOM looking for nodes whose `kind` discriminator marks them as cross-asset references — `CallPeerBlueprint` and any future kinds that hold an `AssetId` GUID. For each such reference, look up the target asset in `IAssetCatalog` and add an inline `"_targetName"` property to the node object containing the resolved name (e.g., `"_targetName": "DoorActor (Blueprint)"`). The leading underscore makes it visually obvious this is a sanitizer addition not part of the canonical schema. If the lookup fails, set `"_targetName": "(asset not found in catalog)"`.
5. **Preserve the node `kind` discriminator** — critical for the LLM to interpret what each node does.
6. **Preserve all `Id` properties on nodes, pins, and links, and all `FromNodeId` / `ToPinId` / `LinkedToIds` references** — Phase 1 historical-diff relies on these as correlation keys. (Phase 2 sibling-diff will renumber them; Phase 1 keeps them.)
7. **Preserve all variable, parameter, and working-state declarations** including their comments (which per the Blackboard DD addendum, are also `///` content emitted from JSON-side comment fields).
8. **Re-serialize the cleaned DOM with stable property ordering** — alphabetical within objects, source order within arrays. Ensures determinism: two identical inputs produce byte-identical outputs.

The exact list of which `EditorMetadata` subkeys are semantic vs. presentation is a contract between the Blueprint editor and the sanitizer. The Blueprint editor team owns the schema; the sanitizer's classification table mirrors it. When the Blueprint editor adds a new `EditorMetadata` subkey, the sanitizer's classification table must be updated. Initial classification per the architect's Q1 answer:

| EditorMetadata subkey | Classification |
|---|---|
| `Comment` (per-node) | Hoist as `Comment` on the node. |
| `CanvasComments` (graph-level free-floating annotations) | Hoist as `_canvasComments` array on the graph object. |
| `X`, `Y` (node position) | Strip. |
| `Viewport` (pan/zoom of the graph canvas) | Strip. |
| `DockState` | Strip. |
| `NodeViewStates` | Strip (these are folded/expanded visual states only). |

The `$meta` envelope classification:

| `$meta` field | Classification |
|---|---|
| `docType` | Preserve. Identifies the document type to the LLM. |
| `schemaVersion` | Preserve. After step 0, both versions will show the same `schemaVersion` (the current registered version). |
| `engineVersion` | Strip. Diagnostic-only; changes on every save. |
| `createdBy` | Strip. Diagnostic-only. |
| `createdUtc` | Strip. Diagnostic-only; preserved across saves but irrelevant to comparison. |

#### Migration surfacing in the UI

If the migration adapter actually performed a migration (i.e., Version A and Version B originally had different `schemaVersion` values), the comparison UI surfaces a one-line notice at the top of the Comparison Summary panel:

```
ℹ Version A was migrated from schema v3 to v4 to match Version B before comparison.
```

This tells the designer that the LLM's analysis is over a schema-matched diff, so they're not confused if the migration system later changes behavior or if they're auditing across an engine-version boundary. The notice is shown only when migration occurred; standard comparisons within one schema version don't show it.



#### Example: Blueprint before and after sanitization

Before (excerpt of a `.bp.json`):

```json
{
  "Name": "DoorActor",
  "AssetId": "00000000-aaaa-0002-0000-000000000010",
  "Graphs": [
    {
      "Name": "Main",
      "EditorMetadata": {
        "Viewport": { "Pan": [10, 20], "Zoom": 1.0 },
        "CanvasComments": [
          { "Text": "Door state machine: closed -> opening -> open", "X": 100, "Y": -50 }
        ]
      },
      "Nodes": [
        {
          "Id": "node_001",
          "kind": "CallPeerBlueprint",
          "TargetBlueprint": "00000000-aaaa-0002-0000-000000000099",
          "EditorMetadata": {
            "X": 320,
            "Y": 180,
            "Comment": "delegate hinge animation to PeerAnim"
          }
        }
      ]
    }
  ]
}
```

After sanitization:

```json
{
  "AssetId": "00000000-aaaa-0002-0000-000000000010",
  "Graphs": [
    {
      "Name": "Main",
      "Nodes": [
        {
          "Comment": "delegate hinge animation to PeerAnim",
          "Id": "node_001",
          "TargetBlueprint": "00000000-aaaa-0002-0000-000000000099",
          "_targetName": "PeerAnim (Blueprint)",
          "kind": "CallPeerBlueprint"
        }
      ],
      "_canvasComments": [
        { "Text": "Door state machine: closed -> opening -> open" }
      ]
    },
    "Name": "DoorActor"
  ]
}
```

`Viewport`, `DockState`, `X`/`Y` on the canvas-comment, `X`/`Y` on the node all stripped. The node's `Comment` and the graph's `CanvasComments` are preserved (Text only, position stripped). The `_targetName` annotation makes the cross-asset reference legible to the LLM. Properties are alphabetically sorted within each object.

### 3.6 Companion-file discovery

Asset kinds vary in how many files form one asset:

| Asset Kind | Main File | Companion Files (auto-discovered) |
|---|---|---|
| BTree | `{Name}_BT.cs` | `{Name}_BT.Blackboard.cs`, `{Name}_BT.HeavyBlackboard.cs`, `{Name}_BT.Orchestrators.g.cs` |
| HSM | `{Name}_HSM.cs` | `{Name}_HSM.Blackboard.cs`, `{Name}_HSM.HeavyBlackboard.cs`, `{Name}_HSM.Orchestrators.g.cs` |
| Blackboard | `{Name}.Blackboard.cs` | `{Name}.HeavyBlackboard.cs` |
| Blueprint | `{Name}.bp.json` | (none) |

When the user picks the main file of one version, the editor automatically searches the same directory for companion files by naming convention. Each found companion is sanitized and included in the export. Missing companions are noted in the version's metadata block ("`{Name}_BT.HeavyBlackboard.cs`: not present in this version").

When the user picks a folder for a version, the editor looks for the main file within that folder by AssetId match: it parses each `.cs` and `.bp.json` looking for one whose AssetId equals the other version's AssetId. The first match is treated as the main file; companions are pulled by naming convention from the same folder.

**Excluded directories.** Folder-mode discovery skips the `.migration-snapshots/` directory entirely. Per the Migration System spec, this hidden sidecar directory holds `.snapshot.json` and `.unknowns.json` files that are verbatim copies of earlier asset states — same AssetId, same docType, but representing a pre-migration shape. If the discovery scan recursed into it, the editor could mistakenly pick a snapshot file as the main asset (or as a companion). The scan does not descend into any directory whose name starts with `.` as a defensive default; `.migration-snapshots/` is the specific case this rule protects against, and other dot-prefixed directories (e.g., `.git/`) get the same treatment incidentally.

Orchestrators files (`.Orchestrators.g.cs`) are included in the export — they carry real semantic content (auto-emitted sync code per Blackboard DD §8) and may change between versions even when the main file does not.

### 3.7 What about cross-version asset kind mismatches?

If the user selects an old `.cs` file (a BTree) and a new `.bp.json` file (a Blueprint), the editor refuses the comparison: "Cannot compare across asset kinds — the two versions appear to be different kinds of asset (BTree vs. Blueprint). Comparison requires both versions to be the same asset kind." This is checked at the file-selection stage before sanitization begins.

If the user selects two assets of the same kind but with different AssetIds, the editor surfaces a warning but allows the comparison to proceed: "The two assets have different AssetIds (`abc...` vs. `def...`). Phase 1 comparison treats both versions as the same asset for visualId correlation; if these are actually sibling assets, results may be noisy. Sibling-asset comparison is planned for Phase 2."

This lets users explicitly opt into noisy sibling-diff if they really want, while documenting the limitation.

---

## 4. The export text file format

### 4.1 Overall structure

The export is a single UTF-8 text file with five sections in order:

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

The `================` separator lines are 80 characters wide. They serve as both visual landmarks for the LLM and as unique strings the response-parser uses to locate boundaries when verifying user paste-back (the user might paste a partial response by mistake; the editor doesn't need this boundary in the response, but matching the input/output structure helps the LLM keep its bearings).

### 4.2 The instruction block

This is the top of the export file. The user is not expected to read it; it is written for an LLM. Tone is direct and prescriptive.

```
You are comparing two versions of a visually-authored AI behavior asset for the
Hrot game engine. Below this instruction block, you will find Version A (the
older revision) and Version B (the newer revision) separated by clearly-labeled
section headers.

Both versions have been sanitized: presentation noise (canvas positions,
pan/zoom state, sub-window layouts, file headers' file-system timestamps) has
been stripped. The remaining content is purely semantic — node topology,
parameter values, action references, blackboard variables, comments. Designers'
comments have been hoisted inline next to the code they describe.

The asset's kind is one of: BTree (fluent C# builder code), HSM (fluent C#
builder code), Blackboard DTO (C# struct definitions), or Blueprint (JSON).
The METADATA block before each version states the kind.

Each node (or state, transition, region, variable) carries a stable identifier:
visualId for BTree/HSM nodes, stableId for HSM states/regions, structuralPath
or field name for blackboard fields, Id for Blueprint nodes/pins/links. These
identifiers are preserved across versions for correlation: if visualId
"a3f2-...-9c01" appears in both versions, it is the same node modified, not
two different nodes.

Your task: identify the semantic differences between Version A and Version B,
focusing on what a human reviewer would care about: behavior changes, new
features, removed features, parameter tuning, blackboard schema changes, and
shifts in intent. Ignore identifier-only differences (an identifier that
appears only in one version means a node was added or removed, not that the
identifier itself is a change).

Produce TWO outputs, in the order below, separated exactly as shown:

----- HUMAN SUMMARY -----
A 2-6 paragraph prose summary intended for a human reviewer. Lead with the
most important change. Mention behavior shifts before tuning, and tuning
before cosmetics. Be specific (name the affected node by its action or role,
not just its identifier).

----- STRUCTURED CHANGES (JSON) -----
A single JSON object matching exactly this schema:

{
  "summary": "<one-sentence top-level description of the change set>",
  "changes": [
    {
      "kind": "<one of: node_added, node_removed, node_modified,
                       variable_added, variable_removed, variable_renamed,
                       variable_retyped, connection_changed, comment_changed,
                       intent_shift>",
      "elementId": "<the visualId/stableId/Id/fieldName of the affected
                     element; null for changes not tied to a specific
                     element such as overall intent_shift on a subgraph>",
      "elementDescription": "<human-readable description of which element,
                              e.g., 'Wait node in main combat sequence' or
                              'AmmoCount variable'>",
      "field": "<for node_modified or variable_retyped, the specific field
                 that changed, e.g., 'duration' or 'type'; null otherwise>",
      "oldValue": "<for changes with a before/after, the prior value as a
                    short string; null otherwise>",
      "newValue": "<the new value as a short string; null otherwise>",
      "severity": "<one of: cosmetic, tuning, feature, removal, behavior>",
      "description": "<1-3 sentence explanation of the change and its
                       likely impact>"
    }
  ]
}

Output JSON only in the STRUCTURED CHANGES section. No prose, no markdown
fences, no surrounding text. The JSON must parse with a standard JSON parser.

Limit total changes to 20 entries. If there are more, prioritize: behavior
shifts first, then features added or removed, then significant tuning, then
cosmetic edits.

For severity levels:
  - cosmetic: rename, comment edit, reorder without semantic effect
  - tuning: parameter value change (timing, thresholds, counts)
  - feature: net-new functionality added
  - removal: functionality removed
  - behavior: a change that shifts the asset's overall behavior, even if
              the mechanical edits are small

For "intent_shift" kind: use when a subgraph's overall purpose has shifted
even if individual node edits look small. Set elementId to the subgraph's
root node (the composite or state that bounds the affected region).

Begin your response now with the HUMAN SUMMARY section.
```

This instruction block is roughly 70 lines. It is verbose by design: the LLM benefits from explicit constraints, schema definitions inline, and severity calibration examples. The user does not read it; the LLM does.

The instruction block is **fixed text emitted by the editor**. It is the same on every export regardless of asset kind. (A more elaborate future version could specialize the instructions per asset kind; Phase 1 keeps one block for simplicity.)

### 4.3 The per-version metadata block

Each version's section starts with a metadata block describing what's being shown:

```
ASSET NAME:       OrcGuard_BT
ASSET KIND:       BTree
ASSET ID:         f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21
SOURCE PATH:      /Users/sam/project/AI/Combat/OrcGuard_BT.cs
LAST MODIFIED:    2026-01-14 11:23:08 UTC
COMPANION FILES:  OrcGuard_BT.Blackboard.cs (present)
                  OrcGuard_BT.HeavyBlackboard.cs (not present)
                  OrcGuard_BT.Orchestrators.g.cs (present)
```

The metadata block is fixed-format key-value pairs. The LLM uses it to ground its analysis ("the asset is a BTree", "modified within the last hour", etc.).

If the user provided a folder rather than a file for the version, the SOURCE PATH shows the folder followed by the resolved main filename inside it.

If a file's timestamp can't be read (e.g., user supplied raw text content), LAST MODIFIED is `(unknown)`.

### 4.4 The sanitized content section

After the metadata block and the `--- COMPANION FILES ---` marker, the sanitized content of each file appears in order: main file first, then each companion file in a stable order (Blackboard, HeavyBlackboard, Orchestrators).

Each file's content is preceded by a one-line file header:

```
// === FILE: OrcGuard_BT.cs ===
...sanitized content here...

// === FILE: OrcGuard_BT.Blackboard.cs ===
...sanitized content here...

// === FILE: OrcGuard_BT.Orchestrators.g.cs ===
...sanitized content here...
```

This makes it unambiguous to the LLM which file it's reading at any point.

### 4.5 The delivery mechanism

After the editor builds the text file in memory, it displays a modal window with two actions:

- **Save to file…** — opens a file picker; user chooses where to write the `.txt` file. Default filename: `{AssetName}_comparison_{timestamp}.txt`.
- **Copy to clipboard** — places the entire text on the system clipboard.

For very large comparisons (e.g., assets with many large companion files), the clipboard route may exceed platform limits silently. The editor checks the text size against a conservative 8 MB threshold; over that, the Copy to clipboard button is disabled with a tooltip recommending Save to file instead.

The modal also previews the first ~30 lines of the export so the user can sanity-check the contents before delivery. The preview is read-only.

A small "Show full" button expands the preview to show all content; useful for debugging when something looks off.

### 4.6 Determinism guarantees

The export is byte-identical across runs given the same input files. This is required for the self-test: comparing an asset to itself produces empty change lists from the LLM, which the editor surfaces as "No semantic changes detected" rather than churn.

Determinism rules:
- The instruction block is fixed text.
- The separator lines are fixed text.
- Metadata blocks have stable key order.
- File ordering within a version is stable (main file first, companions alphabetical by filename).
- Sanitized content per file is deterministic (per §3's sanitization rules — same input → same output).
- Line endings are normalized to `\n` (Unix) in the export, regardless of the source files' line endings. This avoids spurious `\r\n` vs `\n` "differences" the LLM might report.

---

## 5. The LLM contract

This section makes the LLM's expected output format explicit so the response parser knows exactly what to look for. The contract is also embedded in the instruction block (§4.2) so the LLM can see it inline.

### 5.1 The two-section response

The LLM's response has two clearly-separated sections:

```
----- HUMAN SUMMARY -----
{prose paragraphs}

----- STRUCTURED CHANGES (JSON) -----
{single JSON object}
```

The marker lines `----- HUMAN SUMMARY -----` and `----- STRUCTURED CHANGES (JSON) -----` (five dashes on each side, exact spacing) are how the parser locates the section boundaries.

If the LLM doesn't produce the markers, the parser falls back to: "first JSON object found in the response is the structured section; everything before it is the human summary." This tolerates LLMs that add small formatting variations.

### 5.2 The JSON schema (canonical)

```json
{
  "summary": "<string>",
  "changes": [
    {
      "kind": "<enum>",
      "elementId": "<string|null>",
      "elementDescription": "<string>",
      "field": "<string|null>",
      "oldValue": "<string|null>",
      "newValue": "<string|null>",
      "severity": "<enum>",
      "description": "<string>"
    }
  ]
}
```

**`kind` enum values:**

| Value | Use when |
|---|---|
| `node_added` | A node exists in B but not A. `elementId` = the new node's identifier. |
| `node_removed` | A node exists in A but not B. `elementId` = the removed node's identifier. |
| `node_modified` | A node exists in both versions with a changed property (parameter value, action FQN, etc.). `field`, `oldValue`, `newValue` populated. |
| `variable_added` | A blackboard variable in B not in A. `elementId` = the variable name. |
| `variable_removed` | A variable in A not in B. `elementId` = the variable name. |
| `variable_renamed` | A variable's name changed (same role, new name). `oldValue` = old name, `newValue` = new name. `elementId` = the new name. |
| `variable_retyped` | A variable's type changed. `oldValue` = old type, `newValue` = new type. |
| `connection_changed` | An edge between two nodes was rewired. `description` should specify what was rewired. |
| `comment_changed` | A node or variable's comment text changed. `oldValue` and `newValue` are the comment strings. |
| `intent_shift` | A higher-level interpretation: the LLM noticed that a subgraph's purpose has shifted, even if individual edits look mechanical. `elementId` = the root of the affected subgraph (may be null for asset-wide shifts). |

**`severity` enum values:**

| Value | Meaning |
|---|---|
| `cosmetic` | Comment edits, renames, reorders without semantic effect. |
| `tuning` | Parameter value changes (durations, counts, thresholds). |
| `feature` | New functionality added. |
| `removal` | Functionality removed. |
| `behavior` | A change that shifts the asset's overall behavior. The LLM should use `behavior` sparingly — only when confident the change is meaningful. |

### 5.3 The parser's robustness rules

The editor's parser is forgiving about LLM output variations:

- **JSON wrapped in markdown fences** (` ```json ... ``` `) is unwrapped before parsing.
- **Leading/trailing whitespace** in the JSON section is stripped.
- **Missing optional fields** (`field`, `oldValue`, `newValue` on changes that don't need them) default to null.
- **Unknown `kind` or `severity` values** are accepted but flagged with a warning in the response display ("LLM produced an unknown kind 'foo' — treating as 'node_modified'").
- **Truncated JSON** (LLM ran out of tokens mid-response) is detected by JSON parse failure. The parser tries to recover by finding the last complete `}` before the truncation and treating that as the array's end. If recovery fails, the user is shown a clear error: "LLM response appears truncated. Re-run with a more capable model or smaller asset."

The parser does **not** validate that `elementId` actually exists in the current asset. Missing IDs result in an unannotated change entry in the side panel (the user sees the change description but no canvas highlight). This handles cases where the LLM identifies a node by an ID it inferred but got slightly wrong; the prose description still conveys the change.

### 5.4 Example LLM response

For the OrcGuard example used in §3.3, suppose Version B adds a Repeater decorator and changes the AmmoCount expression target. A plausible LLM response:

```
----- HUMAN SUMMARY -----
The combat behavior in OrcGuard_BT has been refined to be more aggressive
about firing. The previously-single-shot Action node now repeats up to 3
times via a new Repeater decorator, which means the guard will commit to a
burst before re-evaluating threats. The expression target on the action has
been changed from AmmoCount to BurstShotsRemaining, suggesting the
blackboard schema has been extended (or a variable was renamed for clarity).

No other behavior changes were detected. Threat detection logic is unchanged.

----- STRUCTURED CHANGES (JSON) -----
{
  "summary": "Burst-fire behavior added to OrcGuard combat; ammo tracking variable renamed.",
  "changes": [
    {
      "kind": "node_added",
      "elementId": "b4711d22-1d22-4c5e-9a3b-2c4d5e6f7a8b",
      "elementDescription": "Repeater decorator wrapping AimAndFire",
      "field": null,
      "oldValue": null,
      "newValue": "Repeater(count: 3)",
      "severity": "behavior",
      "description": "AimAndFire now repeats 3 times per Sequence iteration before yielding back to threat re-evaluation. Guard will commit to burst fire."
    },
    {
      "kind": "variable_renamed",
      "elementId": "BurstShotsRemaining",
      "elementDescription": "AmmoCount blackboard variable",
      "field": "name",
      "oldValue": "AmmoCount",
      "newValue": "BurstShotsRemaining",
      "severity": "cosmetic",
      "description": "Variable renamed for clarity. The expression-target binding on the action node was updated to match. No functional change."
    }
  ]
}
```

The summary is prose for the human; the structured block is what drives the canvas annotation.

---

## 6. The re-import and visualization

### 6.1 Pasting the LLM response

After the user has obtained an LLM response (outside the editor), they bring it back. The editor offers two equivalent paths:

- **Paste LLM Response…** button in the comparison toolbar — opens a multi-line text input pane. The user pastes; clicks "Apply"; the editor parses.
- **Load from file…** — file picker for a `.txt` file containing the response. Useful when the user saved the response to disk first (some LLM clients make this easier).

The editor does **not** watch the clipboard automatically. Explicit paste-by-button is the only ingestion path. This avoids interfering with the user's other clipboard activity and gives them control over when the comparison is loaded.

After paste:
1. The text is run through the parser (§5.3).
2. On parse success, the editor enters **Comparison Mode** for the active asset.
3. On parse failure, the editor surfaces the error message and keeps the text input open so the user can edit and retry.

### 6.2 Comparison Mode — what the editor does

When Comparison Mode is active for an asset:

- The canvas window's title bar shows a chip: `🔍 Comparison: A → B`. Click the chip to exit Comparison Mode.
- The **Comparison Summary panel** opens automatically as a docked panel on the right side. Closeable; reopenable via View menu.
- Affected nodes on the canvas get **visual annotations** (outlines, badges) per §6.4.
- A **Comparison Sidebar** appears, listing all changes in the LLM's order. Closeable; reopenable.

Comparison Mode is per-asset. Switching to another asset clears the mode for that asset (or activates that asset's own comparison mode if one is loaded). Switching back returns to the original comparison.

Comparison Mode does not block authoring. The user can still edit the asset while in Comparison Mode; the annotations remain pinned to their visualIds and follow if a node is moved. If the user saves a change to the asset, the comparison annotations remain accurate against the now-modified asset — but the prose summary may be stale (the LLM analyzed an older state). A small "Comparison may be stale" badge appears in the title bar if the asset is saved while Comparison Mode is active.

### 6.3 The Comparison Summary panel

```
┌─────────────────────────────────────────────────────┐
│ COMPARISON SUMMARY                              [×] │
├─────────────────────────────────────────────────────┤
│ Asset: OrcGuard_BT                                  │
│ ℹ Version A migrated from schema v3 to v4 for       │
│   comparison.  (shown only when migration occurred) │
│                                                     │
│ Burst-fire behavior added to OrcGuard combat;       │
│ ammo tracking variable renamed.                     │
│                                                     │
│ The combat behavior in OrcGuard_BT has been         │
│ refined to be more aggressive about firing. The     │
│ previously-single-shot Action node now repeats up   │
│ to 3 times via a new Repeater decorator…           │
│                                                     │
│ [more prose]                                        │
│                                                     │
├─────────────────────────────────────────────────────┤
│ Filter by severity:                                 │
│ [● behavior] [● feature] [● removal]                │
│ [● tuning]   [○ cosmetic]                           │
└─────────────────────────────────────────────────────┘
```

- Asset name and one-sentence top-level summary at the top.
- Full prose summary below, scrollable.
- Severity filter toggles at the bottom — clicking a severity hides matching annotations from the canvas and entries from the sidebar. The example shows `cosmetic` filtered off by default, matching the common case where users want to skip rename noise; this is configurable in preferences.

### 6.4 Canvas annotations

For each change in the structured JSON with an `elementId` that resolves to a node on the canvas, the editor renders:

- **A colored outline** around the affected node, color-coded by severity:
  - `cosmetic` — gray (60% opacity)
  - `tuning` — blue
  - `feature` — green
  - `removal` — red (rendered as a strikethrough overlay on the node body since the node still exists in B; for `node_removed` cases where the node only existed in A, see §6.5)
  - `behavior` — orange
  - `intent_shift` — orange (shares color with `behavior`; the LLM can use either)
- **Outline style.** The comparison outline is drawn as a **dashed stroke, 2 px wide, offset 3 px outward** from the node's bounding box. This separates it visually from the existing NodeEditor outlines (which are 2 px solid strokes drawn flush against the node body for Selected, Primary-Selected, Error, Warning, and Executing states). The dashed style + outward offset guarantees the comparison annotation:
  - Coexists with a node's selection outline without z-fighting (both render simultaneously, visually distinct).
  - Coexists with validation outlines (Error / Warning) — designer can see at once that a node has both a validation warning AND a comparison annotation.
  - Is unambiguous as "comparison-feature output" rather than "asset state."
  - Dash pattern: 6 px dash, 4 px gap. Stable across zoom levels (the renderer scales it inversely so it always looks like dashes regardless of canvas zoom).
- **A small badge** on the upper-right of the node:
  - `node_added` → ➕
  - `node_removed` → ➖
  - `node_modified` → ✏️
  - `variable_renamed` → ↻ (rendered on every node that binds the renamed variable)
  - `variable_added` / `variable_removed` / `variable_retyped` → applied to the Blackboard Variables panel rows rather than canvas nodes
  - `connection_changed` → ↔ (rendered near the affected edge if specific; otherwise on the source/target nodes — see fallback rule below)
  - `comment_changed` → 💬
  - `intent_shift` → ⚡ rendered on the elementId node (typically the root composite of the affected subgraph; subordinates do not get badges unless they have their own changes)

**`connection_changed` fallback rule.** A `connection_changed` change describes a rewired edge. Its description typically names two endpoints (e.g., "edge between Idle and Combat was rerouted to End"). The renderer attempts to attach the ↔ badge in this priority order:

1. **On the affected edge itself** if both endpoints exist on the current canvas (Version B). The badge floats at the edge's midpoint.
2. **On the surviving endpoint** if one endpoint is a node that exists in A but not B (the rewire involved deleting a node and connecting elsewhere). The badge sits on the surviving node alongside any other badges that node has.
3. **In the sidebar only**, no canvas badge, if neither endpoint can be located on the current canvas (rare; the rewire involves two nodes that no longer exist). The sidebar entry still surfaces the change so the user can read about it.

The renderer never crashes on missing nodes; missing IDs degrade gracefully to sidebar-only entries.

Annotations render via the existing CustomCanvasRenderer extension (per `NodeEditor_Extension_CustomCanvasRenderer.md`). The comparison feature registers a renderer `comparison.annotations` at the `AfterNodes` pass.

Severity filters in the summary panel toggle the active set of annotations in real time — hiding `cosmetic` makes all gray outlines disappear immediately.

### 6.5 Showing removed elements

A `node_removed` change refers to a node that exists in Version A but not Version B. The editor cannot draw an outline around a node that doesn't exist on the current canvas.

Two options for surfacing removals:

- **(a) Sidebar only.** The removed node appears in the change sidebar with its description but no canvas annotation. User clicks to read the description; no visual presence on the canvas.
- **(b) Ghost rendering.** The editor reads the sanitized Version A content to reconstruct the removed node's identity and renders a faded "ghost" of the node on the canvas at an approximate position (centered near the LLM-described parent if discoverable).

Option (b) is appealing but adds significant complexity (reconstructing the canvas layout for a node that no longer exists, hosting it through a fake-node mechanism, handling clicks). Phase 1 ships **option (a)**. The change sidebar entry has a clear `(removed)` label and the description tells the user what was removed.

If user feedback indicates ghost rendering would meaningfully improve the workflow, it can be added in a Phase 1.5 polish slice.

### 6.6 The Comparison Sidebar

```
┌────────────────────────────────────────────────┐
│ CHANGES (4)                              [×]   │
├────────────────────────────────────────────────┤
│ ⚡ behavior                                    │
│   Repeater decorator wrapping AimAndFire      │
│   AimAndFire now repeats 3 times…             │
│   ──────────────────────────────              │
│                                                │
│ ➖ removal                                     │
│   ReportPosition action (removed)             │
│   Position reporting removed — possibly an…  │
│   ──────────────────────────────              │
│                                                │
│ ✏️ tuning                                      │
│   Wait node in main combat sequence            │
│   duration: 2.0 → 3.5                          │
│   Combat pacing slower; reduces twitch…       │
│   ──────────────────────────────              │
│                                                │
│ ↻ cosmetic                                    │
│   AmmoCount variable                          │
│   AmmoCount → BurstShotsRemaining             │
│   Renamed for clarity. No functional change.  │
└────────────────────────────────────────────────┘
```

Each entry:
- Icon + severity label at the top.
- Element description in bold.
- For modifications: `field: oldValue → newValue` line.
- 1-2 line truncated description; click expands the full description in a sub-panel.
- Click the whole entry to **focus the canvas** on the affected node (pan + zoom to center it; flash its outline briefly).

The sidebar is sorted by the LLM's order (which the contract instructs to be by importance). Severity filters apply.

### 6.7 Blackboard variable changes

`variable_added`, `variable_removed`, `variable_renamed`, `variable_retyped` changes don't have a canvas node to highlight. They are surfaced in two places:

- **The Comparison Sidebar** — same row layout as node changes.
- **The Blackboard Variables panel** — variable rows in the panel get the same severity-colored outline. Removed variables appear as ghost rows with strikethrough text in the position they used to occupy (per the LLM's prose if it specified, else at the end of the list).

A `variable_renamed` event also fires `↻` badges on every node whose binding referenced the renamed variable, so the user can see at a glance which actions were affected by the rename.

### 6.8 Exiting Comparison Mode

Closing the chip in the title bar, or selecting "Exit Comparison" from the View menu, clears:
- The summary panel content.
- The sidebar content.
- All canvas annotations.
- The "Comparison may be stale" badge if present.

The asset remains in its current state — no asset content is modified by comparison entry or exit. Comparison Mode is purely read-only display state.

### 6.9 Persisting Comparison Mode

Comparison Mode state (the loaded LLM response, summary, sidebar entries) is held in memory for the editor session. It is **not persisted** to disk — closing the editor and reopening loses the comparison state. The user can re-paste the response to restore it.

This is intentional: the comparison is ephemeral analysis, not authored content. Persisting it would clutter the project; users who want to keep a comparison record can save the LLM response (or the original export) as separate text files in their docs.

---

## 7. The asset-selection UX

### 7.1 Entry point

A toolbar action **Compare with…** appears on every visual editor (BTree canvas, HSM canvas, Blueprint canvas, Blackboard Variables panel). The active asset is automatically used as Version B (newer); the user picks Version A (older).

A dropdown alongside the action lets the user reverse: **Compare with… (as A)** treats the active asset as Version A instead. Useful for "I want to see what changed between this file and the version someone else just sent me."

### 7.2 The selection dialog

```
┌──────────────────────────────────────────────────────────────┐
│ COMPARE WITH                                                  │
├──────────────────────────────────────────────────────────────┤
│                                                               │
│  Active asset (Version B):                                    │
│    OrcGuard_BT (BTree)                                        │
│    /Users/sam/project/AI/Combat/OrcGuard_BT.cs                │
│                                                               │
│  Version A source:                                            │
│    ● Single file        [Browse...]                           │
│    ○ Folder             [Browse...]                           │
│                                                               │
│  Selected: /Users/sam/backups/2026-01-13/OrcGuard_BT.cs       │
│                                                               │
│  [Reverse A↔B]      [Cancel]      [Build Comparison Export]   │
└──────────────────────────────────────────────────────────────┘
```

Two source modes for Version A:

- **Single file** — user picks one main asset file (`.cs` or `.bp.json`). Editor opportunistically discovers companion files in the same directory by naming convention.
- **Folder** — user picks a folder; editor searches it for the main file matching the active asset by AssetId, then pulls companions from the same folder.

Both modes feed the same sanitization pipeline. The folder mode is useful when the user has a snapshot directory or git working tree extracted to a temp folder.

### 7.3 Validation at selection time

Before enabling **Build Comparison Export**, the editor validates:

- **Files exist and are readable.** Inaccessible files surface immediately: "Cannot read file: permission denied" or "File not found."
- **Asset kinds match.** If the user picks a `.bp.json` for Version A while Version B is a BTree, the editor refuses: "Asset kinds differ (Blueprint vs BTree). Comparison requires both versions to be the same kind."
- **AssetIds either match or user is warned.** Different AssetIds surface a warning (per §3.7) but allow proceeding.
- **Main file is parseable enough to find AssetId.** If the file is malformed at the structural level (can't locate the marker line, can't extract AssetId), the editor refuses: "Cannot parse Version A's metadata. Is this a valid asset file?"

Companion-file discovery does NOT block selection. Missing companions are noted but the export proceeds with the main file plus whatever companions were found.

### 7.4 The "Build Comparison Export" action

Clicking the button:

1. Runs the sanitization pipeline on both versions.
2. Assembles the export text file in memory.
3. Shows the **export delivery modal** (§4.5) with Save to file / Copy to clipboard actions plus the preview.

On success, this modal is the user's exit point — they save/copy the text and head to their LLM. The dialog closes when the user clicks "Done" or after a successful Save/Copy action.

### 7.5 Subsequent paste-back

After the user has obtained an LLM response, they return to the active asset and click **Paste LLM Response…** (per §6.1). The editor processes the response and enters Comparison Mode.

There is no explicit "comparison session" the user needs to maintain. The export and the response are independent text artifacts. The user can paste a response into any asset that matches the AssetId in the response (or that originally produced the export — see §7.6).

### 7.6 Detecting response/asset mismatch

The exported text file includes the AssetId in each version's metadata block (§4.3). When the LLM produces its response, the response references visualIds from those exports. The editor's parser:

1. Resolves each `elementId` in the structured JSON against the active asset's node table.
2. If many IDs resolve cleanly, the response matches the active asset.
3. If most IDs don't resolve, the response may be for a different asset. The editor surfaces a confirmation: "This LLM response references VisualIds not present in OrcGuard_BT. Apply anyway? (May result in unannotated changes.)"
4. The user confirms or cancels.

This handles the case where a user pastes the wrong response by mistake without silently producing useless annotations.

---

## 8. Required additions to existing infrastructure

### 8.1 Shared infrastructure additions

In `Hrot.Editor.AiShared.Comparison`:

- **`IAssetComparisonSanitizer`** — the per-asset-kind sanitizer interface (§3.2).
- **`IComparisonMigrationAdapter`** — injected dependency that performs in-memory schema migration on JSON documents before sanitization (§3.5 step 0). Wraps the engine's `ReadOnlyMigrationAdapter` in production; ships with a **no-op default implementation** that passes the DOM through unchanged. The no-op is what the comparison feature uses until the JSON migration system is fully landed. Once migration is live, the production adapter swaps in via DI; comparison code does not change. Only Blueprint (and any future versioned-JSON sanitizers) consume this dependency; C#-based sanitizers (BTree, HSM, Blackboard) do not.
- **`IMetaEnvelopeSanitizer`** — injected dependency that strips the diagnostic-only fields (`engineVersion`, `createdBy`, `createdUtc`) from a `$meta` envelope while preserving `docType` and `schemaVersion` (§3.5 step 1). Ships with a **no-op default implementation** for projects where `$meta` envelopes are not yet present. Production implementation matches the Migration System spec §2.4. Like the migration adapter, only JSON-based sanitizers consume this dependency.
- **`ComparisonExportBuilder`** — assembles the export text file from two sanitized versions plus the fixed instruction block. Owns the separator and metadata-block emission. Also responsible for surfacing the "Version A was migrated from schema v3 to v4 for comparison" notice when the migration adapter reports that migration occurred (§3.5 migration surfacing).
- **`LlmResponseParser`** — parses LLM response text into the structured changes model. Handles the robustness rules (§5.3).
- **`ComparisonSessionState`** — per-asset in-memory holder for the parsed response, drives the summary panel and sidebar. Also carries the migration notice (if any) for display.
- **`ComparisonAnnotationRenderer`** — `ICustomCanvasRenderer` registered at `AfterNodes` pass; reads `ComparisonSessionState` and renders outlines + badges.
- **`ComparisonSummaryPanel`** — docked window registered as `ai_comparison_summary`. Renders the migration notice (if present) above the prose summary.
- **`ComparisonSidebar`** — docked window registered as `ai_comparison_sidebar`.
- **`AssetSelectionDialog`** — the dialog from §7.2. Folder-mode discovery excludes dot-prefixed directories per §3.6 (specifically targeting `.migration-snapshots/` but applied as a general rule).

Per `AI_Editor_Shared_Infrastructure.md` updates:

- **§5 EditorSelectionStore** — gains an optional `ActiveComparisonId` per asset, used by the renderer and panels to know whether a comparison is active. Cleared on Exit Comparison.
- **§3.6 IAssetCatalog** — no schema changes, but every sanitizer takes `IAssetCatalog` as a dependency for the asset-GUID humanization pass (§3.3 step 4, §3.5 step 4). The catalog provides the GUID-to-AssetName + Kind lookup that produces the `// -> AssetName (AssetKind)` inline annotations. Sanitizers tolerate catalog misses gracefully (emit `(asset not found in catalog)` rather than failing).

### 8.2 BTree host additions

Per `BTree_Editor_NodeEditor_Host_Design.md`:

- **`BTreeComparisonSanitizer : IAssetComparisonSanitizer`** — implements the rules in §3.3 (find layout method, extract comment/expressionTarget args by visualId, hoist as `//` comments, truncate, strip definition thunk body, preserve usings/namespace/header).
- The host's `IEditorHostServices` registers the sanitizer with the shared comparison registry on editor startup.
- **`btree.comparison_annotations`** custom canvas renderer integration — actually shares the same `ComparisonAnnotationRenderer` from shared infra, but the BTree host registers it as one of its `CustomCanvasRenderers` (per BTH §15).

### 8.3 HSM host additions

Per `HSM_Editor_NodeEditor_Host_Design.md`:

- **`HsmComparisonSanitizer : IAssetComparisonSanitizer`** — implements §3.3 rules adapted for HSM (the structural identifiers are stableId for states/regions and visualId for transitions).
- Registers the renderer like BTree does (per HSH §15).

### 8.4 Blackboard / Blueprint additions

Per `Blackboard_Authoring_Detailed_Design.md`:

- **`BlackboardComparisonSanitizer : IAssetComparisonSanitizer`** — implements §3.4 (read inline + heavy files, concatenate with labels). Since blackboard files don't have a layout-method or EditorMetadata equivalent, the sanitizer is the simplest of the four.
- The Variables panel's renderer integration — variable rows get the severity outline and `↻ ➕ ➖` badges when the panel's asset has an active comparison.

For Blueprint (assumed to be its own design doc which this DD doesn't author):

- **`BlueprintComparisonSanitizer : IAssetComparisonSanitizer`** — implements §3.5 (load JSON DOM, walk and remove EditorMetadata, preserve all Id references, re-serialize with stable property ordering).
- The Blueprint canvas renderer integration — same `comparison.annotations` renderer applied to Blueprint nodes.

### 8.5 No kernel-side changes

The comparison feature is entirely editor-side. The kernel (`Fbt.Kernel`, `Fhsm.Kernel`, Blueprint runtime) is not aware of comparison mode and is not affected by it. No new attributes, no new runtime types, no source generator changes.

### 8.6 No new file artifacts in the project

The feature does not persist anything to the user's project repository:

- The export `.txt` file is written only if the user explicitly clicks Save to file, and only to a location the user picked. It is not committed by default and is not referenced by any other tool.
- The LLM response is held in memory only during Comparison Mode. No automatic save.
- No `.dbgmap.json`-style sidecars or `.editor-state` files appear in the project.

A user who wants to keep a comparison record saves the text files manually wherever they want.

---

## 9. Slice plan

This feature is a single phase but composed of independent slices that can ship in order. Each slice has its own deliverable acceptance.

### Slice C-1 — Sanitization framework + BTree sanitizer

- **TASK-C-01** — `IAssetComparisonSanitizer` interface + `ComparisonExportBuilder` skeleton. (§3.2, §4)
- **TASK-C-02** — `BTreeComparisonSanitizer` with comment-hoist and layout-truncate logic. (§3.3)
- **TASK-C-03** — Unit tests proving determinism (RT property): same input file → same sanitized output across runs.
- **TASK-C-04** — Round-trip test: an asset compared against itself produces the LLM-input file with versions A and B byte-identical.

Acceptance: given two BTree `.cs` files (real fixtures from the test project), the sanitizer produces a deterministic, layout-stripped, comment-hoisted, visualId-preserving sanitized output. Two identical inputs produce byte-identical outputs.

### Slice C-2 — HSM and Blackboard sanitizers

- **TASK-C-05** — `HsmComparisonSanitizer` with comment-hoist for layout method, region/state stableId preservation.
- **TASK-C-06** — `BlackboardComparisonSanitizer` (inline + heavy concatenation).
- **TASK-C-07** — Unit tests per sanitizer.

Acceptance: HSM `.cs` files and Blackboard `.cs` files (inline + heavy) both sanitize deterministically. Comments are preserved (XML `///` for blackboards, hoisted `//` for HSM layout-method comments).

### Slice C-3 — Blueprint sanitizer

- **TASK-C-08** — `BlueprintComparisonSanitizer` (JSON DOM load, EditorMetadata strip, stable re-serialization).
- **TASK-C-09** — Unit tests with Blueprint `.bp.json` fixtures.

Acceptance: Blueprint JSON files sanitize deterministically with EditorMetadata stripped and all Id references preserved.

### Slice C-4 — Export workflow

- **TASK-C-10** — `AssetSelectionDialog` UI. (§7.2)
- **TASK-C-11** — Companion-file discovery logic (file mode + folder mode). (§3.6)
- **TASK-C-12** — Asset-kind and AssetId validation at selection. (§7.3)
- **TASK-C-13** — Export delivery modal with Save to file / Copy to clipboard. (§4.5)
- **TASK-C-14** — `ComparisonExportBuilder` integration: instruction block + per-version metadata + sanitized content.
- **TASK-C-15** — Toolbar action "Compare with…" wired in BTree, HSM, Blueprint, and Blackboard editors.

Acceptance: a designer can pick two asset versions, validate they match, and produce a comparison `.txt` file (saved or clipboard-copied) ready for an LLM. End-to-end: click toolbar → select Version A → click Build → save file → paste into ChatGPT/Claude and get a response.

### Slice C-5 — Response parsing

- **TASK-C-16** — `LlmResponseParser` with the robustness rules. (§5.3)
- **TASK-C-17** — `ComparisonSessionState` model holding parsed response.
- **TASK-C-18** — "Paste LLM Response…" UI (text input + load-from-file alternative). (§6.1)
- **TASK-C-19** — Response/asset mismatch detection. (§7.6)
- **TASK-C-20** — Unit tests with sample LLM responses (well-formed, markdown-wrapped, truncated, malformed).

Acceptance: pasting various forms of LLM response into the editor results in a populated `ComparisonSessionState` or a clear error message that the user can act on.

### Slice C-6 — Visualization

- **TASK-C-21** — `ComparisonAnnotationRenderer` (custom canvas renderer). (§6.4)
- **TASK-C-22** — Severity → color mapping; kind → badge mapping.
- **TASK-C-23** — `ComparisonSummaryPanel` docked window with prose + severity filters. (§6.3)
- **TASK-C-24** — `ComparisonSidebar` docked window with change list + click-to-focus. (§6.6)
- **TASK-C-25** — Variable-binding badges (`↻`) on nodes affected by variable_renamed. (§6.7)
- **TASK-C-26** — Blackboard Variables panel integration (severity outline on variable rows).
- **TASK-C-27** — Exit Comparison Mode toolbar action. (§6.8)
- **TASK-C-28** — "Stale comparison" badge when asset is saved while comparison is active. (§6.2)

Acceptance: a parsed LLM response produces visible annotations on the canvas, an open summary panel with prose + filters, and a populated sidebar with click-to-focus. Severity filters update annotations and sidebar in real time. Exiting Comparison Mode cleanly clears all annotations.

### Slice C-7 — Polish and robustness

- **TASK-C-29** — 8MB clipboard threshold check + fallback recommendation. (§4.5)
- **TASK-C-30** — Modal export preview (first 30 lines + "Show full" expansion). (§4.5)
- **TASK-C-31** — Reverse A↔B button. (§7.1)
- **TASK-C-32** — Comprehensive sanitization test fixtures covering edge cases (assets with no comments, assets with only opaque blackboard fields, Blueprint with deeply nested graphs).
- **TASK-C-33** — Error handling polish: clear messages for every failure mode (file read errors, mismatched AssetIds, parse failures).
- **TASK-C-34** — Documentation: user-facing guide explaining the workflow + recommended LLM prompts for common scenarios (PR review, AI-agent edit audit, regression hunt).

Acceptance: the feature is robust against the common error modes, the user-facing UX is polished, and there's a documentation page explaining how to use it.

### Slice C-8 — (Optional) Ghost rendering for removed nodes

Deferred per §6.5. If user feedback indicates removed-node ghost rendering is needed, this slice adds it:

- **TASK-C-35** — Read sanitized Version A to enumerate removed nodes.
- **TASK-C-36** — Render ghost nodes at approximate positions near their LLM-described parents.
- **TASK-C-37** — Ghost click handling routing to sidebar entry.

Acceptance: removed nodes appear as faded ghosts on the canvas with clickable behavior matching their sidebar entries.

---

## 10. Test strategy

### 10.1 Unit tests (`Hrot.Editor.AiShared.Comparison.Tests`)

- **`BTreeComparisonSanitizerTests`** — fixtures cover: empty asset, asset with only one node, asset with deep nesting, asset with all comment placements, asset with expression targets, asset with no `[BTreeLayout]` method, malformed file (graceful failure), **asset with Subtree node and Approach B sync bindings (verifies sync-binding hoist as `// sync (in|out|both):` comments above the Subtree call with ASCII arrows), asset with Subtree node referencing another asset by GUID (verifies asset-GUID humanization via mock `IAssetCatalog`), asset with Subtree referencing a GUID not in the catalog (verifies graceful `(asset not found in catalog)` fallback)**.
- **`HsmComparisonSanitizerTests`** — fixtures cover: simple state machine, machine with parallel regions, machine with global transitions, machine with comments on transitions and regions, **machine hosting a sub-BTree via orchestrator with sync bindings (same hoist verification as BTree)**.
- **`BlackboardComparisonSanitizerTests`** — fixtures cover: inline-only, inline + heavy, blackboard with only read-only-passthrough fields, blackboard with XML doc comments, blackboard with no comments.
- **`BlueprintComparisonSanitizerTests`** — fixtures cover: simple blueprint, blueprint with multiple graphs, blueprint with all node kinds, blueprint with deep EditorMetadata pollution, **blueprint with per-node Comments in EditorMetadata (verifies comments are hoisted to top-level node property and the position keys are stripped), blueprint with CanvasComments in graph-level EditorMetadata (verifies hoist as `_canvasComments` array with Text preserved and position stripped), blueprint with `CallPeerBlueprint` node (verifies `_targetName` annotation added via mock `IAssetCatalog`), blueprint with full `$meta` envelope (verifies `docType` and `schemaVersion` preserved, `engineVersion` / `createdBy` / `createdUtc` stripped via injected `IMetaEnvelopeSanitizer`), blueprint at schema v3 with injected migration adapter that up-migrates to v4 (verifies migration runs before sanitization; both versions exit with `schemaVersion=4`)**.
- **`SanitizationDeterminismTests`** — for each sanitizer: run sanitization twice on the same input, verify byte-identical output. Run on shuffled DOM input (Blueprint), verify the sort produces stable output regardless of input ordering. **For Blueprint: verify the no-op migration adapter and no-op meta sanitizer pass DOMs through unchanged (regression test for the default DI bindings).**
- **`ComparisonExportBuilderTests`** — assembles expected output given mock sanitized content; verifies separator placement, metadata block format, instruction block contents. **Includes a fixture where the migration adapter reports migration occurred; verifies the summary panel notice ("Version A migrated from schema v3 to v4...") appears in the prose section's leading lines.**
- **`LlmResponseParserTests`** — fixtures cover: well-formed response, response wrapped in markdown fences, response with extra leading prose, truncated response (last `}` recoverable), truncated response (unrecoverable), response with unknown kind, response with unknown severity, response missing required fields, response with unresolvable elementIds.
- **`ComparisonSessionStateTests`** — verifies model integrity given various LLM response shapes; verifies severity filter logic.
- **`ComparisonAnnotationRendererTests`** — **mock-canvas tests verifying: outline drawn 3px outside the node's bounding box with dashed stroke; outline coexists with selection/validation outlines without z-fighting (both render to mock draw list); `connection_changed` badge placed at edge midpoint when both endpoints exist; `connection_changed` badge falls back to surviving endpoint when one endpoint is missing; `connection_changed` degrades to sidebar-only when neither endpoint exists.**

### 10.2 Integration tests

- **End-to-end: export → simulated LLM → re-import → annotation.** A test fixture provides two versions of a BTree, sanitizes both, assembles export, feeds the export to a mock LLM that returns a hand-crafted response, parses the response, verifies the resulting `ComparisonSessionState` matches expected, verifies the renderer would produce expected annotations.
- **Asset kind mismatch refused.** Selecting a `.bp.json` and a `.cs` triggers the validation refusal.
- **AssetId mismatch warns.** Selecting two BTrees with different AssetIds surfaces the warning but allows proceeding.
- **Companion file discovery.** Folder selection finds the main file by AssetId match; pulls companions by naming convention. **Folder-mode discovery skips `.migration-snapshots/` (and any other dot-prefixed directory) so the editor never picks a snapshot file as the comparison target.**
- **Stale comparison detection.** Loading a comparison, modifying the asset, saving — verifies the "stale" badge appears.
- **Cross-schema-version Blueprint comparison.** With a non-no-op migration adapter wired in, two Blueprints at different `schemaVersion` values are normalized to the current schema before sanitization, and the LLM's input shows both versions at the same schema. The summary panel surfaces the "Version A migrated from..." notice.

### 10.3 Sanitization round-trip property

The most important determinism test: for each asset kind, the property must hold that:

> `sanitize(canonical_file) == sanitize(canonical_file)` — byte-identical across runs.

Verified by running sanitization in a loop 10 times on the same input and verifying every output matches the first.

This property is required for the "no-change comparison" case to produce empty diffs from the LLM. If two byte-identical inputs produce byte-different sanitized outputs, the LLM would see spurious "changes" everywhere.

### 10.4 Manual/visual tests

A test scenario in the editor's demo harness:

- A fixture with two versions of OrcGuard_BT: one with a simple combat sequence, one with the burst-fire change from §5.4.
- Selecting "Compare with…" and the older version produces the expected export.
- Pasting the §5.4 example response activates Comparison Mode.
- The Repeater node is outlined orange with `➕` badge.
- The Wait node is outlined blue with `✏️` badge.
- The renamed variable's binding nodes show `↻` badges.
- The summary panel shows the prose.
- The sidebar shows the four entries.
- Severity filter toggles change canvas + sidebar instantly.
- Exit Comparison clears all annotations.

Manual checklist also covers the failure paths: paste malformed JSON, paste response for a different asset, paste truncated response.

---

## 11. Open questions

1. **Does the comparison need to render any indication of asset-level changes that aren't tied to a specific node?** For example, "the asset's TBlackboard generic argument was renamed" — there's no node to highlight, but it's a meaningful change. Today's plan: surface in the sidebar with `elementId: null`. Worth confirming this is sufficient or whether the title bar / summary panel needs a "global" indication.

2. **Should the export include the LLM's prior response (if any) so the LLM can refine its analysis?** Use case: user gets a response, finds it shallow, wants to re-prompt with "here's what you said before, plus go deeper on X." Today's plan: no, the export is single-shot; the user can craft their own follow-up prompts outside the editor. Worth revisiting if refinement is a common need.

3. **What's the right behavior when both versions are deeply similar but the LLM produces too many cosmetic-severity changes?** The instruction block asks the LLM to limit to 20 changes prioritized by severity, but a chatty LLM might still produce 20 cosmetic edits and miss the one behavior change. The severity filter helps but doesn't fix the prioritization upstream. Mitigation: include "(estimated edit count: high, please prioritize ruthlessly)" in the export when the editor detects many file changes in advance. Not in Phase 1.

4. **How to handle the user wanting to compare three or more versions?** Phase 1 supports two-at-a-time only. Realistic use case: "compare this to last month, last week, and yesterday." Workaround: run three separate comparisons. Worth flagging if this becomes a frequent request — supporting it requires a multi-way export format and the LLM would need to compare more than two snapshots, which complicates the contract.

5. **Should the LLM-response load path accept JSON-only files** (without the prose summary header)? Use case: a programmatic LLM client that emits just the structured JSON. Today's parser is forgiving (§5.3) but the explicit JSON-only path could be a documented convenience. Not in Phase 1.

6. **Per-asset-kind specialized instruction blocks.** Today the instruction block is the same across all asset kinds. A specialized version per kind (BTree-specific terminology, HSM-specific concerns about region conflicts, etc.) could improve LLM output quality. Cost is maintenance burden. Deferred to user feedback.

7. **Localization of the instruction block and UI labels.** Today everything is English. Future internationalization would need the instruction block translated, the UI labels translated, and confidence the LLM produces the JSON schema regardless of language. Out of scope.

Items resolved by architect feedback (no longer open):

- **Blueprint comments preserved via EditorMetadata hoist.** The Blueprint sanitizer classifies `EditorMetadata` subkeys per a table (§3.5): `Comment` and `CanvasComments` are hoisted to top-level properties; positions, viewport, dock state, and node view states are stripped. The Blueprint editor team owns the classification table; sanitizer mirrors it.
- **Approach B sync bindings preserved.** The BTree/HSM sanitizer extracts `.SubtreeSyncField(...)` entries from the layout method before truncation and hoists them as multi-line `// sync (in|out|both):` comments above the corresponding Subtree builder call, with ASCII-only arrows (`<--`, `-->`, `<-->`) for plain-text safety. (§3.3 step 3, second example.)
- **Comparison outlines coexist with existing node outlines.** Comparison annotations render as dashed 2px strokes offset 3px outward from the node bounding box, distinct from the solid 2px outlines used for Selected / Error / Warning / Executing states. Z-fighting avoided by construction. (§6.4 "Outline style.")
- **`connection_changed` fallback rule defined.** Renderer priority: badge at edge midpoint if both endpoints exist; badge on surviving endpoint if one endpoint missing; sidebar-only if neither endpoint exists. Renderer never crashes on missing nodes. (§6.4 fallback rule.)
- **Cross-asset GUIDs humanized via IAssetCatalog lookup.** Every sanitizer takes `IAssetCatalog` as a dependency and decorates outgoing asset-GUID references with inline `// -> AssetName (AssetKind)` comments (C# sanitizers) or `_targetName` properties (Blueprint sanitizer). Catalog misses fall back gracefully to `(asset not found in catalog)`. (§3.3 step 4, §3.5 step 4, §8.1.)
- **Migration system integration.** Three intersections with the JSON Migration System resolved: (a) `IComparisonMigrationAdapter` injected into the Blueprint sanitizer as step 0, wrapping `ReadOnlyMigrationAdapter` in production and a no-op default until JSON migration ships, so both versions are normalized to the current schema before sanitization (§3.5 step 0); (b) `IMetaEnvelopeSanitizer` injected to strip `engineVersion`, `createdBy`, `createdUtc` from the `$meta` envelope while preserving `docType` and `schemaVersion` (§3.5 step 1, $meta classification table); (c) folder-mode discovery skips `.migration-snapshots/` and any other dot-prefixed directory to avoid picking up snapshot files as comparison targets (§3.6). When migration actually runs, the Comparison Summary panel surfaces a one-line notice ("Version A migrated from schema v3 to v4..."). Missing migration chain is not specially handled — the worst-case noisy comparison is acceptable per the project owner's call.

---
