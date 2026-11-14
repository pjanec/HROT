# DEC-03 — BTree host: decorators attach as pills (picker path)

**Workstream:** DEC ([../DEC-PLAN.md](../DEC-PLAN.md)). **Layer:** Hrot.BTree.Editor host. **Depends:** DEC-02 (landed: `NodePaletteAction.AttachToSelected`, `AttachmentHostPropertyKeys.Kind`, picker routing). **Size: small.**

## Goal

Make picking a decorator from the node picker **attach a pill to the selected node** instead of creating a free, pinless decorator node (the user's bug). DEC-02 wired the generic picker→`AddAttachment` routing in NodeEditor core; this batch flips the BTree decorator catalog entries to use it and teaches the BTree command sink to build a pill from the picked kind. End result: select a node → open the picker (Space/Tab over empty canvas) → pick e.g. "Repeater" → a Repeater pill appears on the selected node, persists, round-trips.

(The discoverable right-click "Add Decorator →" node menu needs a NEW node-context-menu seam in NodeEditor core — that's the separate DEC-03b, NOT this batch.)

## Key facts (verified)

- `BTreeNodeCatalog.MakeDecorator` (`Host/BTreeNodeCatalog.cs:191`) builds the 7 decorator entries as plain `NodeCatalogEntry`s (positional, ending in `Array.Empty<PinSignature>()` for Outputs). DEC-02 added two trailing optional record params: `NodePaletteAction PaletteAction = CreateNode` and `AttachmentCategory? AttachmentCategory = null` (both in `NodeEditor.Core.Interfaces`, already imported by the catalog).
- `BTreeCommandSink.ApplyAddPill` (`Host/BTreeCommandSink.cs:329`) currently REQUIRES `HostProperties["decoratorType"]` to be a `NodeType`, else returns. The DEC-02 picker instead sends `HostProperties[AttachmentHostPropertyKeys.Kind]` (key string `"paletteKind"`) = the entry's `NodeKindKey.Id` (e.g. `"bt.decorator.repeater"`). It does NOT send `decoratorType`/`intParam`/`floatParam`.
- Mapping helpers exist on `BTreeKinds`: `public static bool IsDecorator(string id)` and `public static NodeType KindIdToNodeType(string kindId)` (decorator ids → `NodeType.Inverter/Repeater/...`). Decorator kind-id constants: `BTreeKinds.Inverter` = `"bt.decorator.inverter"`, etc.
- `BTreeEditorPill` fields: `VisualId`, `HostNodeVisualId`, `DecoratorType` (NodeType), `IntParam` (int?), `FloatParam` (float?), `Comment`, `StackIndex`. `_asset.AddPill(pill)` + `_asset.MarkDirty()`.

## Implementation

1. **`BTreeNodeCatalog.cs` — flip decorator entries.** In `MakeDecorator`, pass the two new entry params so every decorator entry is `PaletteAction = NodePaletteAction.AttachToSelected` and `AttachmentCategory = AttachmentCategory.Decorator`. (Use named args for clarity since they're trailing.) Leave all other entries (composites/leaves) as default `CreateNode`.

2. **`BTreeCommandSink.ApplyAddPill` — accept the picker path.** Resolve the decorator `NodeType` from EITHER source, in this order:
   - If `HostProperties["decoratorType"]` is a `NodeType` → use it (existing programmatic/test path — keep working).
   - Else if `HostProperties[AttachmentHostPropertyKeys.Kind]` is a `string kind` AND `BTreeKinds.IsDecorator(kind)` → `dt = BTreeKinds.KindIdToNodeType(kind)`.
   - Else → safe no-op (return; do NOT add a pill). This covers a non-decorator kind or missing props.
   Then build + add the pill exactly as today (VisualId = `att.NewId.Value`, HostNodeVisualId = `att.HostNodeId.Value`, StackIndex = `att.StackIndex`). Keep reading optional `intParam`/`floatParam`/`comment` if present.
   **Sensible defaults so a freshly-picked pill is valid + shows its param label:** if the resolved type is `NodeType.Repeater` and no `IntParam` was supplied, default `IntParam = 1`; if `NodeType.Cooldown` and no `FloatParam`, default `FloatParam = 1f`. (Other decorator types take no param.)
   `using NodeEditor.Core.Interfaces;` for `AttachmentHostPropertyKeys` if not already present.

## Constraints
- Scope: `BTreeNodeCatalog.cs`, `BTreeCommandSink.cs`, + tests. Do NOT touch NodeEditor core (DEC-02 is done) or any other host. Do NOT modify any `.btree.json` asset.
- The existing `"decoratorType"` programmatic path must keep working unchanged (other code/tests rely on it).

## Tests (Hrot.BTree.Editor.Tests)
- ApplyAddPill via an `AddAttachment` whose `HostProperties` = `{ ["paletteKind"] = "bt.decorator.repeater" }` (no `decoratorType`) → a `Repeater` pill is added to the host node with the given `StackIndex` and a non-null default `IntParam`. Build the command with the real key constant `AttachmentHostPropertyKeys.Kind`.
- Same for `"bt.decorator.cooldown"` → `Cooldown` pill with a non-null default `FloatParam`.
- A `paletteKind` that is NOT a decorator (e.g. `BTreeKinds.Sequence`) → no pill added (safe no-op).
- The existing `decoratorType`-based path still adds a pill (regression guard).
- Round-trip: after adding a pill via the picker path, the asset serializes + deserializes with the pill intact (use the project's existing pill round-trip helper if one exists; mirror DecoratorPillCollapseTests / RR-03 BTree round-trip patterns).
- Catalog: the 7 decorator entries report `PaletteAction == AttachToSelected` and `AttachmentCategory == Decorator`; non-decorator entries report `CreateNode`.

## Verification (run + paste RAW output)
1. `dotnet build` `Hrot.BTree.Editor` → 0 errors.
2. `dotnet test` `Hrot.BTree.Editor.Tests` and `Hrot.AiEditor.Persistence.Tests` → report counts; byte-identity gate must stay green; no new failures vs baseline (BTree.Editor 524/0, Persistence 129/0). Known pre-existing failures elsewhere (do not chase): 2 Generators pretty-print, 7 Blueprints.

## Report back
Diff summary; how the two HostProperties sources are resolved; default-param choices; raw build + test output. **Do NOT commit** — lead reviews & commits.
