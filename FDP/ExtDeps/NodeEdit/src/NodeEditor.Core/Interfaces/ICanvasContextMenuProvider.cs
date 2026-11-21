using System.Collections.Generic;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// ⭐ CE-1368 — host-supplied items for the right-click menu of the EMPTY canvas (e.g. the BTree editor's "Reset layout").
/// Registered via <see cref="IEditorHostServices.CanvasContextMenu"/>. Shown even on a read-only graph, so a provider must offer only
/// view-state actions there. Empty = none.
/// </summary>
public interface ICanvasContextMenuProvider
{
    IReadOnlyList<ContextMenuItem> GetItems();
}
