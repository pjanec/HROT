# DEC-02 — NodeEditor core: `AttachToSelected` palette action

**Workstream:** DEC ([../DEC-PLAN.md](../DEC-PLAN.md)). **Layer:** NodeEditor.Core + NodeEditor.UI (SHARED LIBRARY — additive only). **Depends:** none. **Size: medium.**

## Goal

Today, picking any catalog entry from the node picker emits `AddNode` → a free node. For decorators that's wrong: a decorator must attach as a **pill** to the selected node (`AddAttachment`), never become a free node. This batch adds a **generic, host-agnostic** mechanism in NodeEditor core: a catalog entry can declare `PaletteAction = AttachToSelected`, and the picker then emits `AddAttachment` on the currently-selected node instead of `AddNode`. (The BTree host flips its decorator entries to use it + adds the "Add Decorator →" menu in DEC-03 — do NOT touch any host here.)

## Key facts (verified)

- Picker→command seam: `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasInput.cs:104-117` — `Pickers.Open("nodes.all", …, pick => { if (pick is NodeCatalogEntry entry) { cb.AddNode(entry.Kind, graphPos, null); … } })`. **This is the only call site to branch.**
- A second creation site (`CanvasInput.cs:~1141`, drag-wire-onto-empty-canvas → `AddNode`) uses `QueryForPinContext`, which **excludes decorators** (`BTreeNodeCatalog.QueryForPinContext` filters out `CatDecorator`) and decorators have no pins — so it can never receive an AttachToSelected entry. **Leave it untouched.**
- `NodeCatalogEntry` is a positional `record` in `NodeEditor.Core/Interfaces/INodeCatalog.cs:28` (11 params). `AttachmentCategory` enum is in the same namespace (`NodeEditor.Core.Interfaces`, `IAttachmentModel.cs:51`).
- `CommandBuilder` (`NodeEditor.Core/Commands/CommandBuilder.cs`) has `AddNode`/`AddLink` returning `(Forward, Inverse)` using `IdGenerator.NewNodeId()`/`NewLinkId()`. `IdGenerator` (`NodeEditor.Primitives/IdGenerator.cs`) has NO `NewAttachmentId` yet — add one (mirror the others).
- `GraphCommand.AddAttachment(AttachmentId NewId, NodeId HostNodeId, AttachmentCategory Category, string? Glyph, string? Label, string? Tooltip, int StackIndex, IReadOnlyDictionary<string,object?>? HostProperties)` and `RemoveAttachments(IReadOnlyList<AttachmentId>)` already exist (`GraphCommand.cs:114,125`).
- Selection: `view.Selection` is a `SelectionState` exposing `IEnumerable<NodeId> Nodes`. Attachment count for a host: `view.Model.GetAttachmentsForNode(hostId).Count` (on `IGraphModel`).

## Implementation

1. **`INodeCatalog.cs`** — add an enum and two **trailing, optional** params to the `NodeCatalogEntry` record (trailing + defaults keeps every existing positional `new NodeCatalogEntry(...)` call compiling):
   ```csharp
   public enum NodePaletteAction { CreateNode, AttachToSelected }
   ```
   Append to the record: `NodePaletteAction PaletteAction = NodePaletteAction.CreateNode,` and `AttachmentCategory? AttachmentCategory = null`.
   Also add a shared constant for the HostProperties key the picker will use to convey the entry kind to the host sink — put it where both core and hosts can see it (e.g. a `public static class AttachmentHostPropertyKeys { public const string Kind = "paletteKind"; }` in `NodeEditor.Core.Interfaces`). Document it.

2. **`IdGenerator.cs`** — add `public static AttachmentId NewAttachmentId() => AttachmentId.NewId();` (if `AttachmentId` has no `NewId()`, add a `NewId()` mirroring `NodeId.NewId()`, or use `new AttachmentId(Guid.NewGuid())` — match how `NodeId.NewId()` is implemented).

3. **`CommandBuilder.cs`** — add:
   ```csharp
   public (GraphCommand Forward, GraphCommand Inverse) AddAttachment(
       NodeId host, AttachmentCategory category, string? glyph, string? label,
       string? tooltip, int stackIndex, IReadOnlyDictionary<string, object?>? hostProps)
   {
       var newId = IdGenerator.NewAttachmentId();
       return (new GraphCommand.AddAttachment(newId, host, category, glyph, label, tooltip, stackIndex, hostProps),
               new GraphCommand.RemoveAttachments(new[] { newId }));
   }
   ```
   (add `using NodeEditor.Core.Interfaces;` if needed for `AttachmentCategory`.)

4. **`CanvasInput.cs` picker callback (line ~109)** — branch on the entry's palette action:
   ```csharp
   if (pick is NodeCatalogEntry entry)
   {
       var cb = new CommandBuilder(view.Model);
       if (entry.PaletteAction == NodePaletteAction.AttachToSelected)
       {
           var hosts = view.Selection.Nodes.ToList();
           if (hosts.Count == 1)
           {
               var host = hosts[0];
               int stackIndex = view.Model.GetAttachmentsForNode(host).Count;
               var props = new Dictionary<string, object?> { [AttachmentHostPropertyKeys.Kind] = entry.Kind.Value };
               var (fwd, inv) = cb.AddAttachment(host, entry.AttachmentCategory ?? AttachmentCategory.Custom,
                   glyph: null, label: entry.DisplayName, tooltip: entry.Description, stackIndex, props);
               view.Execute(fwd, inv, "Add Decorator");
           }
           // else: no single host selected → safe no-op (optionally a hint log). Do NOT create a free node.
       }
       else
       {
           var (fwd, inv) = cb.AddNode(entry.Kind, graphPos, null);
           view.Execute(fwd, inv, "Add Node");
       }
   }
   view.Interaction.ResetToIdle();
   ```
   Keep `entry.Kind.Value` exactly (it's the NodeKindKey's string). Match existing using-directives/types in the file.

## Constraints
- **Additive only.** A `CreateNode` (default) entry behaves EXACTLY as today. No host code changes (Blueprint/HSM/BTree untouched). The Blueprint editor must require zero changes.
- Scope: `INodeCatalog.cs`, `IdGenerator.cs` (+ maybe `AttachmentId.cs` for `NewId`), `CommandBuilder.cs`, `CanvasInput.cs`, plus tests. Nothing else.

## Tests (NodeEditor.Core.Tests and/or NodeEditor.UI.Tests)
- `CommandBuilder.AddAttachment` returns a forward `AddAttachment` with the given host/category/label/stackIndex/props and an inverse `RemoveAttachments([sameNewId])`.
- `NodeCatalogEntry` default `PaletteAction == CreateNode` and `AttachmentCategory == null` (back-compat).
- Picker routing: if the test harness can simulate a picker pick (look for existing CanvasInput/picker tests to mirror) — AttachToSelected entry + exactly one selected node → an `AddAttachment` is executed on that node with `HostProperties[AttachmentHostPropertyKeys.Kind] == entry.Kind.Value` and `StackIndex == prior attachment count`; with zero or >1 selected nodes → NO `AddNode` and NO `AddAttachment` (safe no-op). If the picker can't be unit-driven, cover the routing logic by extracting/testing the decision at the CommandBuilder level and note the gap.

## Verification (run + paste RAW output)
1. `dotnet build` NodeEditor.Core, NodeEditor.UI, and all three editor hosts (`Hrot.BTree.Editor`, `Hrot.Hsm.Editor`, and the Blueprint editor host project) → 0 errors (proves the additive record change broke no positional call sites).
2. `dotnet test` NodeEditor.Core.Tests and NodeEditor.UI.Tests → report counts; no new failures vs baseline (NodeEditor.UI.Tests baseline ~70/0).

## Report back
Diff summary, the chosen HostProperties key, how (or whether) you could unit-test the picker routing, raw build + test output. **Do NOT commit** — lead reviews & commits.
