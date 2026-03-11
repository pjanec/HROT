using Fdp.Kernel;

namespace FDP.Toolkit.Vis2D.Abstractions;

/// <summary>
/// A precompiled, allocation-free predicate that tests whether a given ECS entity
/// is eligible for selection by the <see cref="FDP.Toolkit.Vis2D.Tools.EntityPickerTool"/>.
///
/// <para>
/// Implementations are created once by an <see cref="IEntityFilterFactory"/> when a
/// pick operation is initiated. All per-frame calls to <see cref="IsMatch"/> must
/// be allocation-free to maintain the 60 FPS hot-path budget.
/// </para>
/// </summary>
public interface IEntityFilter
{
    /// <summary>
    /// Returns <c>true</c> if <paramref name="entity"/> is a valid pick target.
    /// Must be allocation-free; called once per hovered entity per frame.
    /// </summary>
    bool IsMatch(Entity entity);
}

/// <summary>
/// Injectable factory that translates human-readable filter preset names
/// (e.g. <c>"road_graphs"</c>, <c>"units_ground"</c>) into a compiled
/// <see cref="IEntityFilter"/> instance.
///
/// <para>
/// Hosting applications (e.g. <c>Bagira.IG</c>) inject their own
/// domain-specific implementation so that the Vis2D toolkit remains decoupled
/// from concrete ECS component types or layer-registry internals.
/// The expensive string-to-mask translation is performed exactly once inside
/// <see cref="CreateFilter"/>; subsequent per-frame calls to
/// <see cref="IEntityFilter.IsMatch"/> are O(1).
/// </para>
/// </summary>
public interface IEntityFilterFactory
{
    /// <summary>
    /// Compiles a filter from one or more named preset strings.
    /// The returned filter must be allocation-free on its <c>IsMatch</c> hot path.
    /// </summary>
    /// <param name="filterPresets">
    /// Domain-specific preset names understood by the hosting application,
    /// e.g. <c>["road_graphs"]</c> or <c>["units_ground","vehicles"]</c>.
    /// An empty array should produce a filter that matches all entities.
    /// </param>
    IEntityFilter CreateFilter(string[] filterPresets);
}
