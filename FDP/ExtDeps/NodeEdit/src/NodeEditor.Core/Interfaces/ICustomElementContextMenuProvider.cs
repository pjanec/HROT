using System.Collections.Generic;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Host-supplied provider that returns context menu items for a given custom-drawn element.
/// Registered via IEditorHostServices.CustomElementContextMenu.
/// Matched by RendererId to the renderer that owns the element.
/// If no matching provider is registered, right-clicking a custom element falls through
/// to the canvas empty-area context menu.
/// </summary>
public interface ICustomElementContextMenuProvider
{
    /// <summary>
    /// Identifies which renderer's elements this provider handles.
    /// Must match ICustomCanvasRenderer.Id.
    /// </summary>
    string RendererId { get; }

    /// <summary>Returns the context menu items for the given element.</summary>
    IReadOnlyList<ContextMenuItem> GetItemsFor(string elementKey, CustomElementHit hit);
}
