namespace Fdp.Toolkit.ImGui.Renderers;

/// <summary>
/// Custom ImGui renderer that can replace summary or detail rendering for a specific C# type.
///
/// <para>Implement this interface and annotate the class with
/// <see cref="ImGuiRendererAttribute"/> to auto-register it via reflection scan
/// performed by <see cref="ImGuiRendererRegistry"/>. Both methods are optional:
/// return <c>null</c> / <c>false</c> to fall back to the built-in behaviour.</para>
///
/// <para>Example usage:
/// <list type="bullet">
///   <item>Inline summary for <c>Vector3</c>: <c>[x, y, z]</c></item>
///   <item>Quaternion to Euler angles: <c>Y:45° P:0° R:0°</c></item>
///   <item>Context-specific: only decode EntityId inside <c>TargetMemory</c> component</item>
/// </list>
/// </para>
/// </summary>
public interface IImGuiRenderer
{
    /// <summary>
    /// Produces a short inline summary string for <paramref name="value"/>.
    /// Used in entity inspector component headers and event browser row summaries.
    /// Return <c>null</c> to use the default reflection-based summary.
    /// </summary>
    /// <param name="value">The non-null value to summarise.</param>
    /// <returns>A compact summary string, or <c>null</c> for default rendering.</returns>
    string? GetSummary(object value);

    /// <summary>
    /// Renders a custom value cell in the current ImGui table row (column 1 is active).
    /// Called by <see cref="Fdp.Toolkit.ImGui.Utils.ImGuiPropertyTree"/> when matching this type.
    /// Return <c>true</c> if rendering was performed; return <c>false</c> to fall through to
    /// the default hierarchical tree rendering.
    /// </summary>
    /// <param name="value">The non-null value to render.</param>
    /// <returns><c>true</c> if this renderer handled the output; <c>false</c> for default.</returns>
    bool RenderValue(object value);
}
