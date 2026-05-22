using ImGuiNET;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.Panels.Views;

/// <summary>
/// Fallback details view: renders a read-only property tree via reflection
/// on whatever object the target resolves to. Shown when no registered
/// provider handles the current target.
/// </summary>
internal sealed class FallbackDetailsView : IDetailsView
{
    private readonly object? _subject;

    /// <summary>Construct a fallback view for the given (possibly null) subject.</summary>
    public FallbackDetailsView(object? subject) => _subject = subject;

    /// <inheritdoc/>
    public bool IsDirty => false;

    /// <inheritdoc/>
    public void Commit() { }

    /// <inheritdoc/>
    public void Revert() { }

    /// <inheritdoc/>
    public void Draw(IDetailsRenderContext ctx)
    {
        if (_subject is null)
        {
            ImGui.TextColored(ctx.Theme.TextMuted, "(no target)");
            return;
        }

        foreach (var prop in _subject.GetType().GetProperties())
        {
            if (!prop.CanRead) continue;

            object? value;
            try { value = prop.GetValue(_subject); }
            catch { value = "<error>"; }

            ImGui.Text(prop.Name + ":");
            ImGui.SameLine();
            ImGui.TextDisabled(value?.ToString() ?? "<null>");
        }
    }
}
