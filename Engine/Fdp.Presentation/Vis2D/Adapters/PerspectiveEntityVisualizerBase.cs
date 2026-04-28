using System;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Rendering;
using Fdp.Toolkit.Vis2D.Shapes;
using Raylib_cs;

namespace Fdp.Toolkit.Vis2D.Adapters;

/// <summary>
/// Abstract base class for entity visualizer adapters that render entities as
/// oriented, perspective-deformed silhouettes on the 2-D map.
///
/// <para>
/// Concrete subclasses supply the domain-specific colour, tooltip text, and
/// runtime condition flags.  The base class owns all geometry logic:
/// <list type="bullet">
///   <item>Shape lookup via <see cref="IEntityShapeLibrary"/>.</item>
///   <item>3-D → 2-D projection with exaggerated perspective (see
///   <see cref="PerspectiveShapeRenderer"/>).</item>
///   <item>Selection / hover highlighting.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Scale semantics:</b>
/// <see cref="VisualScaleMultiplier"/> == 1.0 renders the entity at its true
/// physical size in metres (a 7.9 m tank occupies 7.9 world units).
/// The map camera's zoom factor converts world units to screen pixels
/// automatically.  Raise the multiplier when entities are too small to see
/// at strategic zoom levels.
/// </para>
/// </summary>
public abstract class PerspectiveEntityVisualizerBase : IVisualizerAdapter
{
    // ── Default dimensions when VehicleParams is absent ───────────────────────
    private const float DefaultLengthMeters = 5.0f;
    private const float DefaultWidthMeters  = 2.5f;
    private const float DefaultHitRadius    = 5.0f;

    // ── Injected dependencies ─────────────────────────────────────────────────

    private readonly IEntityShapeLibrary _shapeLibrary;

    // ── Public tuning properties ──────────────────────────────────────────────

    /// <summary>
    /// Controls how strongly the entity's roll and pitch deform the rendered shape.
    /// 0 = flat orthographic top-down view.
    /// ~0.05 gives a subtle but readable perspective effect.
    /// </summary>
    public float ExaggerationCoefficient { get; set; } = 0.05f;

    /// <summary>
    /// Uniform scale applied on top of the entity's physical dimensions.
    /// 1.0 = render at true real-world size (metres).
    /// Values above 1.0 enlarge the symbol for readability at tactical zoom.
    /// </summary>
    public float VisualScaleMultiplier { get; set; } = 1.0f;

    // ── Construction ──────────────────────────────────────────────────────────

    protected PerspectiveEntityVisualizerBase(IEntityShapeLibrary shapeLibrary)
    {
        _shapeLibrary = shapeLibrary
            ?? throw new ArgumentNullException(nameof(shapeLibrary));
    }

    // ── Abstract / virtual extension points ──────────────────────────────────

    /// <summary>
    /// Returns the base colour for the entity symbol.
    /// The base class applies selection and hover brightening on top of this.
    /// </summary>
    protected abstract Color ResolveColor(ISimulationView view, Entity entity);

    /// <summary>
    /// Returns the entity's current runtime condition bitmask so the renderer
    /// can show or hide conditional polyline elements (damage overlays, etc.).
    /// Return <see cref="EntityShapeCondition.None"/> when no special state applies.
    /// </summary>
    protected abstract EntityShapeCondition ResolveCondition(ISimulationView view, Entity entity);

    /// <summary>
    /// (Optional) Returns an explicit shape-library key stored on the entity.
    /// The default implementation returns <c>null</c>; override and read from
    /// the entity's visual-data component when accessible in the hosting project.
    /// </summary>
    protected virtual string? ResolveShapeName(ISimulationView view, Entity entity) => null;

    /// <summary>
    /// Attempts to obtain the entity's 3-D orientation.  Override to prefer an
    /// alternative transform source (e.g. <c>NetworkTransform.LastRotation</c> on
    /// nodes that receive rotation via DDS rather than maintaining local
    /// <c>SimTransform</c> authority).
    /// </summary>
    /// <returns>
    /// <c>true</c> and a valid <paramref name="rotation"/> when orientation data
    /// is available; <c>false</c> when the entity should not be rendered.
    /// </returns>
    protected virtual bool TryGetRotation(ISimulationView view, Entity entity, out Quaternion rotation)
    {
        if (view.HasComponent<SimTransform>(entity))
        {
            rotation = view.GetComponentRO<SimTransform>(entity).Rotation;
            return true;
        }
        rotation = Quaternion.Identity;
        return false;
    }

    // ── IVisualizerAdapter ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public abstract string? GetHoverLabel(ISimulationView view, Entity entity);

    /// <inheritdoc/>
    public virtual Vector2? GetPosition(ISimulationView view, Entity entity)
    {
        if (!view.HasComponent<SimTransform>(entity)) return null;
        ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
        return new Vector2(tf.Position.X, tf.Position.Y);
    }

    /// <inheritdoc/>
    public float GetHitRadius(ISimulationView view, Entity entity)
    {
        if (view.HasComponent<VehicleParams>(entity))
            return view.GetComponentRO<VehicleParams>(entity).Length / 2f;
        return DefaultHitRadius;
    }

    /// <inheritdoc/>
    public virtual void Render(
        ISimulationView view,
        Entity          entity,
        Vector2         position,
        RenderContext   ctx,
        bool            isSelected,
        bool            isHovered)
    {
        if (!TryGetRotation(view, entity, out Quaternion rotation)) return;

        // ── Colour with selection / hover highlight ────────────────────────────
        Color color = ResolveColor(view, entity);

        if (isSelected)
        {
            color = Color.Yellow;
        }
        else if (isHovered)
        {
            color = new Color(
                (byte)Math.Min(color.R + 50, 255),
                (byte)Math.Min(color.G + 50, 255),
                (byte)Math.Min(color.B + 50, 255),
                (byte)255);
        }

        // ── Physical dimensions ───────────────────────────────────────────────
        float length = DefaultLengthMeters;
        float width  = DefaultWidthMeters;
        if (view.HasComponent<VehicleParams>(entity))
        {
            ref readonly var prm = ref view.GetComponentRO<VehicleParams>(entity);
            length = prm.Length;
            width  = prm.Width;
        }

        // ── DIS entity type for shape fallback ────────────────────────────────
        // EntityRepository.GetDisType reads from the entity header — zero-cost
        // when no DIS type has been assigned (returns default(DISEntityType)).
        DISEntityType disType = default;
        if (view is EntityRepository repo)
            disType = repo.GetDisType(entity);

        // ── Shape lookup ──────────────────────────────────────────────────────
        string? shapeName = ResolveShapeName(view, entity);
        EntityShapeProfile shape = _shapeLibrary.GetShape(shapeName, disType);

        // ── Runtime condition flags ───────────────────────────────────────────
        EntityShapeCondition condition = ResolveCondition(view, entity);

        // ── Delegate all geometry to the stateless renderer ───────────────────
        PerspectiveShapeRenderer.RenderShape(
            shape,
            position,
            rotation,
            length,
            width,
            color,
            ExaggerationCoefficient,
            VisualScaleMultiplier,
            condition,
            ctx.Zoom);

        // ── Selection ring ────────────────────────────────────────────────────
        if (isSelected)
        {
            float radius = MathF.Max(length, width) * 0.6f * VisualScaleMultiplier;
            Raylib.DrawCircleLines(
                (int)position.X, (int)position.Y, radius, Color.White);
        }
    }
}
