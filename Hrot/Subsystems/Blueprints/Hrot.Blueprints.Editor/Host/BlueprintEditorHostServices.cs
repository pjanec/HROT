using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// <see cref="IEditorHostServices"/> that bundles all Blueprint-specific services and the
/// <see cref="Hrot.Editor.AiShared.Adapters.AiEditorAdapterBundle"/> engine adapters into a single
/// implementation.
///
/// <para>
/// Mirrors <c>BTreeEditorHostServices</c> / <c>HsmEditorHostServices</c> in shape.
/// The custom Blueprint canvas renderers (e.g. <see cref="Hrot.Blueprints.Editor.Visuals.WhenFiringPulseRenderer"/>)
/// and any registered attachment providers are surfaced via the interface's extension points.
/// </para>
/// </summary>
public sealed class BlueprintEditorHostServices : IEditorHostServices
{
    private readonly BlueprintNodeCatalog                        _nodeCatalog;
    private readonly BlueprintTypeSystem                         _typeSystem;
    private readonly BlueprintLinkValidator                      _linkValidator;
    private readonly BlueprintCommandSink                        _commandSink;
    private readonly IPickerRegistry                             _pickers;
    private readonly IClipboard                                  _clipboard;
    private readonly IIconProvider                               _icons;
    private readonly IDiagnosticsSink?                           _diagnostics;
    private IDebugSession?                                       _debug;
    private readonly IInputSource                                _input;
    private readonly IEditorTheme                                _theme;
    private readonly IReadOnlyList<ICustomCanvasRenderer>        _customRenderers;
    private readonly IAttachmentContextMenuProvider?             _attachmentContextMenu;

    public BlueprintEditorHostServices(
        BlueprintNodeCatalog                    nodeCatalog,
        BlueprintTypeSystem                     typeSystem,
        BlueprintLinkValidator                  linkValidator,
        BlueprintCommandSink                    commandSink,
        IPickerRegistry                         pickers,
        IClipboard                              clipboard,
        IIconProvider                           icons,
        IDiagnosticsSink?                       diagnostics,
        IInputSource                            input,
        IEditorTheme                            theme,
        IDebugSession?                          debug              = null,
        IReadOnlyList<ICustomCanvasRenderer>?   customRenderers    = null,
        IAttachmentContextMenuProvider?         attachmentContextMenu = null)
    {
        _nodeCatalog           = nodeCatalog           ?? throw new ArgumentNullException(nameof(nodeCatalog));
        _typeSystem            = typeSystem             ?? throw new ArgumentNullException(nameof(typeSystem));
        _linkValidator         = linkValidator          ?? throw new ArgumentNullException(nameof(linkValidator));
        _commandSink           = commandSink            ?? throw new ArgumentNullException(nameof(commandSink));
        _pickers               = pickers                ?? throw new ArgumentNullException(nameof(pickers));
        _clipboard             = clipboard              ?? throw new ArgumentNullException(nameof(clipboard));
        _icons                 = icons                  ?? throw new ArgumentNullException(nameof(icons));
        _diagnostics           = diagnostics;
        _debug                 = debug;
        _input                 = input                  ?? throw new ArgumentNullException(nameof(input));
        _theme                 = theme                  ?? throw new ArgumentNullException(nameof(theme));
        _customRenderers       = customRenderers        ?? System.Array.Empty<ICustomCanvasRenderer>();
        _attachmentContextMenu = attachmentContextMenu;
    }

    // ── IEditorHostServices ──────────────────────────────────────────────────

    public INodeCatalog                         NodeCatalog           => _nodeCatalog;
    public ITypeSystem                          TypeSystem            => _typeSystem;
    public ILinkValidator                       LinkValidator         => _linkValidator;
    public IGraphCommandSink                    CommandSink           => _commandSink;
    public IPickerRegistry                      Pickers               => _pickers;
    public IClipboard                           Clipboard             => _clipboard;
    public IIconProvider                        Icons                 => _icons;
    public IDiagnosticsSink?                    Diagnostics           => _diagnostics;
    public IDebugSession?                       Debug                 => _debug;
    public IInputSource                         Input                 => _input;
    public IEditorTheme                         Theme                 => _theme;
    public IReadOnlyList<ICustomCanvasRenderer> CustomCanvasRenderers => _customRenderers;

    IAttachmentContextMenuProvider? IEditorHostServices.AttachmentContextMenu
        => _attachmentContextMenu;

    // ── runtime mutability ──────────────────────────────────────────────────

    /// <summary>Allows attaching/detaching the debug session at runtime.</summary>
    public void SetDebugSession(IDebugSession? session) => _debug = session;

    // ── typed accessors (for tests / factory use) ────────────────────────────

    /// <summary>The Blueprint command sink (typed accessor).</summary>
    public BlueprintCommandSink BlueprintCommandSink => _commandSink;

    /// <summary>The Blueprint node catalog (typed accessor).</summary>
    public BlueprintNodeCatalog BlueprintNodeCatalog => _nodeCatalog;

    /// <summary>The Blueprint type system (typed accessor).</summary>
    public BlueprintTypeSystem BlueprintTypeSystem => _typeSystem;

    /// <summary>The Blueprint link validator (typed accessor).</summary>
    public BlueprintLinkValidator BlueprintLinkValidator => _linkValidator;
}
