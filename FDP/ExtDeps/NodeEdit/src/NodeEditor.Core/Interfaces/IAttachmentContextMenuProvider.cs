using System.Collections.Generic;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// One item in an attachment context menu.
/// Label is the display text; Execute is the action to invoke on click.
/// </summary>
public sealed record ContextMenuItem(string Label, System.Action Execute, bool Enabled = true, IReadOnlyList<ContextMenuItem>? Children = null);

/// <summary>
/// Host-supplied provider that returns context menu items for a given attachment.
/// Registered via <see cref="IEditorHostServices.AttachmentContextMenu"/>.
/// If no provider is registered, right-clicking an attachment falls through to the
/// canvas empty-area context menu.
/// </summary>
public interface IAttachmentContextMenuProvider
{
    IReadOnlyList<ContextMenuItem> GetItemsFor(AttachmentId id);
}
