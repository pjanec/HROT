namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Bundle of services the host provides to the editor at construction.
/// Wraps all the optional and required adapters in one container so the
/// editor's constructor doesn't take 12 parameters.
/// </summary>
public interface IEditorHostServices
{
    /// <summary>Node catalog for search popup and picker.</summary>
    INodeCatalog NodeCatalog { get; }

    /// <summary>Type system for colors, shapes, compatibility.</summary>
    ITypeSystem TypeSystem { get; }

    /// <summary>Link validator (rejects illegal connections).</summary>
    ILinkValidator LinkValidator { get; }

    /// <summary>Sink for all mutations.</summary>
    IGraphCommandSink CommandSink { get; }

    /// <summary>Picker registry (host registers sources here).</summary>
    IPickerRegistry Pickers { get; }

    /// <summary>Clipboard adapter.</summary>
    IClipboard Clipboard { get; }

    /// <summary>Icon provider for catalog icons, status icons, etc.</summary>
    IIconProvider Icons { get; }

    /// <summary>Optional diagnostics sink for logging/telemetry.</summary>
    IDiagnosticsSink? Diagnostics { get; }

    /// <summary>Optional debug session (breakpoints, watches, executing-node viz).</summary>
    IDebugSession? Debug { get; }

    /// <summary>Input source for the canvas (mouse + keyboard abstraction).</summary>
    IInputSource Input { get; }

    /// <summary>Theme (colors, fonts, sizes).</summary>
    IEditorTheme Theme { get; }

    /// <summary>
    /// Optional provider for attachment right-click context menus.
    /// Returns null when no provider is registered.
    /// </summary>
    IAttachmentContextMenuProvider? AttachmentContextMenu => null;
}
