using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.Panels;

/// <summary>
/// Concrete implementation of <see cref="IDetailsContext"/>.
/// </summary>
internal sealed class DetailsContext : IDetailsContext
{
    public required IGraphCommandSink CommandSink { get; init; }
    public required IPinDefaultValueEditorRegistry Editors { get; init; }
    public required IIconProvider Icons { get; init; }
    public required IEditorTheme Theme { get; init; }
}

/// <summary>
/// Concrete implementation of <see cref="IDetailsRenderContext"/>.
/// </summary>
internal sealed class DetailsRenderContext : IDetailsRenderContext
{
    public required IIconProvider Icons { get; init; }
    public required IEditorTheme Theme { get; init; }
    public bool ShowAdvanced { get; set; }
    public bool ShowHelpTooltips { get; set; } = true;
}
