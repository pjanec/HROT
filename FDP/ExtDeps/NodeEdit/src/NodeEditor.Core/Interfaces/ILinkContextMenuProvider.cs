using System.Collections.Generic;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// ⭐ CE-1323 (HROT DESIGN_Behaviour_Debugging DBG-14) — host-supplied items for a right-clicked LINK, the twin of
/// <see cref="INodeContextMenuProvider"/>: appended after the built-in link items (Break Link, Select Connected Nodes, …). Registered via
/// <see cref="IEditorHostServices.LinkContextMenu"/>; none ⇒ only the built-in items. (An HSM draws a transition as a link: its
/// breakpoint is set here.)
/// </summary>
public interface ILinkContextMenuProvider
{
    /// <summary>Extra context-menu items for a right-clicked link. Empty = none.</summary>
    IReadOnlyList<ContextMenuItem> GetItemsFor(LinkId link);
}
