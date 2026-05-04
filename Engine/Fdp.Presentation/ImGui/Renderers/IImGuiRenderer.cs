using Fdp.Core;
using Fdp.Presentation.Abstractions;

namespace Fdp.Presentation.Renderers;

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
    /// Called by <see cref="Fdp.Presentation.Utils.ImGuiPropertyTree"/> when matching this type.
    /// Return <c>true</c> if rendering was performed; return <c>false</c> to fall through to
    /// the default hierarchical tree rendering.
    /// </summary>
    /// <param name="value">The non-null value to render.</param>
    /// <returns><c>true</c> if this renderer handled the output; <c>false</c> for default.</returns>
    bool RenderValue(object value);
}

/// <summary>
/// Extended ImGui renderer that receives entity and session context.
/// Implement this in addition to <see cref="IImGuiRenderer"/> when the renderer
/// needs to read sibling ECS components (e.g., BehaviorState alongside BrainBlackboard).
/// </summary>
public interface IEntityAwareImGuiRenderer : IImGuiRenderer
{
    /// <summary>
    /// Produces a short inline summary string for <paramref name="value"/> with
    /// entity/session context available.
    /// Return <c>null</c> to use the default reflection-based summary.
    /// </summary>
    string? GetSummary(IInspectableSession session, Entity entity, object value) => GetSummary(value);

    /// <summary>
    /// Renders a custom detail view using entity and session context.
    /// Return <c>true</c> if rendering was handled; <c>false</c> to fall through
    /// to the default hierarchical tree rendering.
    /// <para>Set <paramref name="doubleClickedPath"/> to the JSON path of the field
    /// the user double-clicked (e.g. <c>$.Memory.Speed</c>) to open a scoped edit
    /// window. Set to <c>null</c> if no click occurred or the renderer does not
    /// support field-level editing.</para>
    /// </summary>
    bool RenderValue(IInspectableSession session, Entity entity, object value, out string? doubleClickedPath);
}
