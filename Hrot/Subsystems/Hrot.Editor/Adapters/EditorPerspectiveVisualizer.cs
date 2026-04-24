using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Vis2D.Adapters;
using Fdp.Toolkit.Vis2D.Shapes;
using Hrot.Map.Definitions.Tkb;
using Raylib_cs;

namespace Hrot.Editor.Adapters;

/// <summary>
/// Visualizer adapter for the offline Scenario Editor.
///
/// <para>Renders each entity as a colour-coded oriented silhouette driven by
/// <see cref="DefaultEntityShapeLibrary"/> and the entity's DIS type.  Colour
/// represents force affiliation read from the <see cref="EntityInfo"/> component:</para>
/// <list type="bullet">
///   <item><b>Blue</b>  — friendly (<see cref="ForceId.Friend"/>).</item>
///   <item><b>Red</b>   — hostile (<see cref="ForceId.Hostile"/>).</item>
///   <item><b>Green</b> — neutral / default (<see cref="ForceId.Neutral"/>).</item>
/// </list>
///
/// <para>Hover labels show the entity name when an <see cref="EntityInfo"/> component
/// is present.</para>
/// </summary>
public sealed class EditorPerspectiveVisualizer : PerspectiveEntityVisualizerBase
{
    // ── Force-affiliation colours ─────────────────────────────────────────────
    private static readonly Color ColFriend  = new(40,  120, 220, 255); // blue
    private static readonly Color ColHostile = new(200,  40,  40, 255); // red
    private static readonly Color ColNeutral = new(40,  170,  60, 255); // green

    // ── Construction ──────────────────────────────────────────────────────────

    /// <param name="shapeLibrary">Shared entity shape library (injected by the composition root).</param>
    public EditorPerspectiveVisualizer(IEntityShapeLibrary shapeLibrary)
        : base(shapeLibrary)
    {
    }

    // ── Domain-specific implementations ──────────────────────────────────────

    /// <inheritdoc/>
    protected override Color ResolveColor(ISimulationView view, Entity entity)
    {
        if (!view.HasComponent<EntityInfo>(entity)) return ColNeutral;
        return view.GetComponentRO<EntityInfo>(entity).ForceId switch
        {
            ForceId.Friend  => ColFriend,
            ForceId.Hostile => ColHostile,
            _               => ColNeutral,
        };
    }

    /// <inheritdoc/>
    protected override EntityShapeCondition ResolveCondition(ISimulationView view, Entity entity)
        => EntityShapeCondition.None;

    /// <inheritdoc/>
    protected override string? ResolveShapeName(ISimulationView view, Entity entity)
    {
        if (!view.HasComponent<VisualData>(entity)) return null;
        string name = view.GetComponentRO<VisualData>(entity).MapShapeName.ToString();
        return name.Length > 0 ? name : null;
    }

    /// <inheritdoc/>
    public override string? GetHoverLabel(ISimulationView view, Entity entity)
    {
        if (!view.HasComponent<EntityInfo>(entity)) return null;
        ref readonly var info = ref view.GetComponentRO<EntityInfo>(entity);
        string name = info.Name.ToString();
        return name.Length > 0 ? name : null;
    }
}
