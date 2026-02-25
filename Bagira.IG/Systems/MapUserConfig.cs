namespace Bagira.IG.Systems;

/// <summary>
/// Operator-facing runtime configuration supplying the highest-priority (Layer-3)
/// overrides inside <see cref="StyleResolutionSystem"/>.
///
/// Mutated from the ImGui settings panel and read every simulation frame.
/// Not an ECS component — owned by the application shell and injected at construction.
/// </summary>
public class MapUserConfig
{
    /// <summary>
    /// When <c>true</c>, <see cref="StyleResolutionSystem"/> forces every entity's
    /// affiliation to <see cref="Bagira.IG.Components.ForceId.Hostile"/> and applies a
    /// red tint, overriding both TKB defaults and network overrides.
    /// </summary>
    public bool ForceHostile { get; set; }

    /// <summary>
    /// When <c>true</c>, label text is suppressed (set to empty string) so the renderer
    /// omits text draw calls entirely.
    /// </summary>
    public bool HideLabels { get; set; }
}
