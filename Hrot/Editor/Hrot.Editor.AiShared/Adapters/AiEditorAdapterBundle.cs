using Fdp.Presentation.Icons;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;

namespace Hrot.Editor.AiShared.Adapters;

/// <summary>
/// Single construction point for all five NodeEdit engine adapters plus the
/// <see cref="PickerRegistry"/>.  Builds and wires everything once; exposes
/// the adapters for injection into host-services factories.
/// <para>
/// Construction requires an <see cref="IconAtlas"/> (no GPU calls needed for
/// <see cref="SilkIconProvider"/> or <see cref="EngineEditorTheme"/>).
/// </para>
/// </summary>
public sealed class AiEditorAdapterBundle
{
    /// <summary>Icon provider backed by the engine silk atlas.</summary>
    public SilkIconProvider Icons { get; }

    /// <summary>Editor theme wrapping <c>DefaultTheme</c> + engine fonts.</summary>
    public EngineEditorTheme Theme { get; }

    /// <summary>Per-frame ImGui input snapshot.</summary>
    public ImGuiInputSource Input { get; }

    /// <summary>OS clipboard via ImGui.</summary>
    public ImGuiClipboard Clipboard { get; }

    /// <summary>NLog-backed diagnostics sink.</summary>
    public NLogDiagnosticsSink Diagnostics { get; }

    /// <summary>
    /// Picker registry with <see cref="IIconProvider"/> and <see cref="IEditorTheme"/>
    /// already registered via <see cref="PickerRegistry.SetServices"/>.
    /// </summary>
    public PickerRegistry Pickers { get; }

    /// <summary>
    /// Build the bundle.  Pass the pre-loaded engine atlas; no GPU calls are made here.
    /// </summary>
    /// <param name="atlas">Engine icon atlas (loaded by the presentation shell).</param>
    public AiEditorAdapterBundle(IconAtlas atlas)
    {
        Icons       = new SilkIconProvider(atlas);
        Theme       = new EngineEditorTheme();
        Input       = new ImGuiInputSource();
        Clipboard   = new ImGuiClipboard();
        Diagnostics = new NLogDiagnosticsSink();

        Pickers = new PickerRegistry();
        Pickers.SetServices(Icons, Theme);
    }

    // ── Interface-typed accessors (for passing to IEditorHostServices ctors) ──

    /// <summary><see cref="Icons"/> as <see cref="IIconProvider"/>.</summary>
    public IIconProvider IconProvider => Icons;

    /// <summary><see cref="Theme"/> as <see cref="IEditorTheme"/>.</summary>
    public IEditorTheme EditorTheme => Theme;

    /// <summary><see cref="Input"/> as <see cref="IInputSource"/>.</summary>
    public IInputSource InputSource => Input;

    /// <summary><see cref="Clipboard"/> as <see cref="IClipboard"/>.</summary>
    public IClipboard ClipboardInterface => Clipboard;

    /// <summary><see cref="Diagnostics"/> as <see cref="IDiagnosticsSink"/>.</summary>
    public IDiagnosticsSink DiagnosticsSink => Diagnostics;

    /// <summary><see cref="Pickers"/> as <see cref="IPickerRegistry"/>.</summary>
    public IPickerRegistry PickerRegistry => Pickers;
}
