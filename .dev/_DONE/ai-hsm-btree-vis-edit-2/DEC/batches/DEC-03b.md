# DEC-03b — Right-click node → "Add Decorator →" + kill standalone-decorator bug

**Workstream:** DEC ([../DEC-PLAN.md](../DEC-PLAN.md)). **Layer:** NodeEditor.Core + NodeEditor.UI (new seam, additive) + Hrot.BTree.Editor host. **Depends:** DEC-02, DEC-03 (landed). **Size: medium-large.**

## Why

User-tested DEC-03. The picker path works but is unintuitive, AND there are bugs:
1. **The intuitive UX is missing:** right-clicking the node you want to decorate shows no "Add Decorator". The `HoverKind.Node` context menu (`CanvasRenderer.DrawContextMenu`, ~line 649-739) is **entirely hardcoded** with NO host hook.
2. **Standalone-decorator bug:** the canvas right-click "Add Node…" menu (`CanvasRenderer.cs:503-508`) and — per user — picking a decorator there creates a **free standalone node**, because that picker callback calls `cb.AddNode(entry.Kind…)` ignoring `PaletteAction`. (DEC-02 only fixed the Tab/Space picker in `CanvasInput.cs`; this second callback was missed.)

Goal: add a generic **node context-menu provider** seam; the BTree host contributes an "Add Decorator →" submenu (7 types) on a node; and route BOTH "add node" pickers through one shared `PaletteAction` helper so a decorator can never become a free node.

(Out of scope, log only: the picker popup showing a single flat "All" list with no category grouping / no icons, and Tab doing nothing while hovering a node. Note these in DEC-PLAN as a separate picker-UX follow-up — the right-click menu makes the picker unnecessary for decorators.)

## Key facts (verified)

- `ContextMenuItem` is `record ContextMenuItem(string Label, System.Action Execute, bool Enabled = true)` in `NodeEditor.Core/Interfaces/IAttachmentContextMenuProvider.cs:10`. **No submenu support today.**
- `IEditorHostServices` (`NodeEditor.Core/Interfaces/IEditorHostServices.cs:56`) has `ICustomElementContextMenuProvider? CustomElementContextMenu => null;` (default-null pattern). No node-context-menu member yet.
- `CanvasRenderer.DrawContextMenu` (`NodeEditor.UI/Canvas/CanvasRenderer.cs:487`): `HoverKind.None` branch has "Add Node…" (line 494-512, picker→`AddNode`); `HoverKind.Node` branch (649-739) is hardcoded (Go to Definition / Expand / Delete / Add Comment / Toggle Breakpoint); `HoverKind.CustomElement` branch (797-812) renders a provider's `ContextMenuItem`s flatly via `ImGui.MenuItem`.
- `CanvasInput.cs:109-130` (the Tab/Space picker) already routes `AttachToSelected` (DEC-02) — this logic must be **extracted to a shared helper** and reused.
- BTree host: `BTreeEditorHostServices` holds `_commandSink` (`IGraphCommandSink`) and applies commands directly, e.g. `_commandSink.Apply(new GraphCommand.SetNodeProperty(...))` (`BTreeEditorHostServices.cs:105`); it implements `IEditorHostServices.CustomElementContextMenu` explicitly (line 91). It can construct a provider with the sink + the graph model.
- `GraphCommand.AddAttachment(AttachmentId NewId, NodeId HostNodeId, AttachmentCategory Category, string? Glyph, string? Label, string? Tooltip, int StackIndex, IReadOnlyDictionary<string,object?>? HostProperties)`. `BTreeKinds` constants `Inverter`/`Repeater`/… map to `NodeType` via `KindIdToNodeType`. `DEC-03`'s `ApplyAddPill` already handles `HostProperties["decoratorType"] = NodeType` (default params for Repeater=1/Cooldown=1f).

## Implementation

### A. NodeEditor.Core (additive)
1. **Submenu support:** extend `ContextMenuItem` to `(string Label, Action Execute, bool Enabled = true, IReadOnlyList<ContextMenuItem>? Children = null)`. When `Children` is non-null/non-empty, the item renders as a submenu (its own `Execute` is ignored).
2. **New seam** `NodeEditor.Core/Interfaces/INodeContextMenuProvider.cs`:
   ```csharp
   public interface INodeContextMenuProvider
   {
       /// <summary>Extra context-menu items for a right-clicked node (and the current multi-selection). Empty = none.</summary>
       IReadOnlyList<ContextMenuItem> GetItemsFor(NodeId node, IReadOnlyList<NodeId> selection);
   }
   ```
3. `IEditorHostServices`: add `INodeContextMenuProvider? NodeContextMenu => null;` (default-null → no host breaks).

### B. NodeEditor.UI
4. **Shared palette router** — extract DEC-02's branch into a static helper (e.g. `NodeEditor.UI/Action/PaletteEntryExecutor.cs`):
   ```csharp
   public static void Execute(GraphView view, NodeCatalogEntry entry, Vector2 graphPos)
   ```
   Body = the DEC-02 logic: if `entry.PaletteAction == AttachToSelected` → attach to the single selected node (no-op on 0/>1), building props `[AttachmentHostPropertyKeys.Kind] = entry.Kind.Id`; else `cb.AddNode(entry.Kind, graphPos, null)`. Then have **both** picker callbacks call it: `CanvasInput.cs:~111-130` (replace the inline branch) AND `CanvasRenderer.cs:503-508` (the "Add Node…" callback — pass `_contextMenuGraphPos`). This fixes the standalone-decorator bug at the canvas menu.
5. **Render the node provider + submenus** in `DrawContextMenu`:
   - Add a recursive helper `RenderItems(IReadOnlyList<ContextMenuItem> items)` that for each item: if `Children` non-empty → `if (ImGui.BeginMenu(label, enabled)) { RenderItems(children); ImGui.EndMenu(); }`; else `if (ImGui.MenuItem(label, "", false, enabled)) item.Execute();`. Use it in the `CustomElement` branch (replace the existing flat loop at 805-809) AND the new node-provider block.
   - In the `HoverKind.Node` branch, before `break;` (after Toggle Breakpoint, ~line 737): if `view.Host.NodeContextMenu` is non-null, get `items = view.Host.NodeContextMenu.GetItemsFor(target.Node, targetNodes)`; if any, `ImGui.Separator();` then `RenderItems(items);`.

### C. Hrot.BTree.Editor host
6. **`BTreeNodeContextMenuProvider : INodeContextMenuProvider`** (new file in `Host/`). Constructed with the `IGraphCommandSink` and the `IGraphModel` (the live `BTreeGraphModel`). `GetItemsFor(node, selection)` returns a single parent item:
   - `new ContextMenuItem("Add Decorator", () => {}, true, Children: [ 7 items ])`.
   - One child per decorator type (Inverter, Repeater, Cooldown, ForceSuccess, ForceFailure, UntilSuccess, UntilFailure) with a friendly label. Each child's `Execute`:
     ```csharp
     int stackIndex = _model.GetAttachmentsForNode(node).Count;
     var props = new Dictionary<string, object?> { ["decoratorType"] = nodeType };
     _sink.Apply(new GraphCommand.AddAttachment(
         IdGenerator.NewAttachmentId(), node, AttachmentCategory.Decorator,
         glyph: null, label: friendlyName, tooltip: null, stackIndex, props));
     ```
   - Optional: skip the "Add Decorator" item when the node is the BTree Root (decorators on Root are nonsensical) — check the node's kind via the model if cheaply available; otherwise show for all (harmless). Keep it simple.
7. **Register** in `BTreeEditorHostServices`: add a `_nodeContextMenuProvider` field, construct it with the sink + model, and `INodeContextMenuProvider? IEditorHostServices.NodeContextMenu => _nodeContextMenuProvider;` (mirror the CustomElementContextMenu line at :91).

## Constraints
- Additive only in core (default-null seam, optional `Children`); existing hosts/behaviour unchanged. Do NOT alter unrelated context-menu items.
- The standalone-decorator bug fix must NOT change behaviour for normal (CreateNode) entries.
- No `.btree.json`/codegen changes here, so an incremental build is fine — but still run the full builds below.

## Tests
- **NodeEditor.Core.Tests:** `ContextMenuItem` with `Children` constructs/exposes them; default `Children == null`.
- **NodeEditor.UI.Tests / Core.Tests:** if the shared `PaletteEntryExecutor` is testable without ImGui (it operates on `GraphView`/`CommandBuilder`), test: AttachToSelected + 1 selected node → `AddAttachment` executed; CreateNode → `AddNode`; AttachToSelected + 0/>1 selected → neither. If `GraphView` can't be constructed in tests, note the gap and rely on the existing DEC-02 CommandBuilder coverage.
- **Hrot.BTree.Editor.Tests:** construct `BTreeNodeContextMenuProvider` with the real `BTreeCommandSink` + `BTreeGraphModel` over a small asset; `GetItemsFor(node, [node])` returns one "Add Decorator" parent with 7 children; invoking the "Repeater" child's `Execute` adds a Repeater pill to that node (assert via the model/asset, mirroring DEC-03 tests); StackIndex increments on a second add.

## Verification (run + paste RAW output)
1. `dotnet build` NodeEditor.Core, NodeEditor.UI, `Hrot.BTree.Editor`, `Hrot.Hsm.Editor`, and the Blueprint editor host → 0 errors (additive seam must not break other hosts; they keep the default-null `NodeContextMenu`).
2. `dotnet test` NodeEditor.Core.Tests, NodeEditor.UI.Tests, `Hrot.BTree.Editor.Tests` → counts; no new failures vs baseline (Core 190/0, UI 78/0, BTree.Editor 531/0).

## Report back
Diff summary; how the node-menu provider dispatches commands; whether the shared router / provider were unit-testable (and any gap); raw build + test output. **Do NOT commit** — lead reviews & commits.
