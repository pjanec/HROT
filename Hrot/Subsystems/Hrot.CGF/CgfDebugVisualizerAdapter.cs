using System;
using System.Numerics;
using System.Text;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Adapters;
using Fdp.Toolkit.Vis2D.Shapes;
using Hrot.Map.Definitions.Tkb;
using Raylib_cs;

namespace Hrot.CGF;

/// <summary>
/// Visualizer adapter for the CGF (Brain) perspective.
///
/// <para>Renders each entity as a colour-coded oriented silhouette (shape driven
/// by <see cref="DefaultEntityShapeLibrary"/> and the entity's DIS type) with a
/// compact behavior label, and provides a rich brain-state hover tooltip for
/// rapid diagnostics without opening the entity inspector.</para>
///
/// <para>Colour coding:
/// <list type="bullet">
///   <item><b>Gray</b>   — no behavior / idle.</item>
///   <item><b>Blue</b>   — BTree behavior active (Brain tier 2).</item>
///   <item><b>Teal</b>   — HSM behavior active (Brain tier 1).</item>
///   <item><b>Amber</b>  — Traffic behavior active (Brain tier 0).</item>
///   <item><b>Yellow</b> — selected by user.</item>
/// </list>
/// </para>
///
/// <para>Position is read from <see cref="NetworkTransform"/> (written by
/// <c>GeoSpatialIngressTranslator</c> when the Muscle publishes WorldPos back to the Brain)
/// with a fallback to <see cref="SimTransform"/> for locally-owned entities that have not
/// yet delegated authority.  Rotation follows the same priority.</para>
/// </summary>
public sealed class CgfDebugVisualizerAdapter : PerspectiveEntityVisualizerBase
{
    // ── Rendering constants ───────────────────────────────────────────────────
    private const float HitRadiusWorld = 10f;
    private const int   LabelFontSize  = 8;
    private const int   LabelOffsetX   = 7;

    // ── Colours ───────────────────────────────────────────────────────────────
    private static readonly Color ColIdle    = new(120, 120, 120, 255); // gray
    private static readonly Color ColBTree   = new(0,   120, 220, 255); // blue
    private static readonly Color ColHsm     = new(0,   180, 120, 255); // teal
    private static readonly Color ColTraffic = new(200, 140,  30, 255); // amber

    // ── CGF brain registry for behavior name resolution ───────────────────────
    private readonly BehaviorRegistry? _behaviorRegistry;

    // ── Construction ──────────────────────────────────────────────────────────

    /// <param name="shapeLibrary">Shared entity shape library (injected by the composition root).</param>
    /// <param name="behaviorRegistry">
    /// Optional registry; when provided, behavior hashes are resolved to human-readable
    /// names in the hover label and the compact map annotation.
    /// </param>
    public CgfDebugVisualizerAdapter(
        IEntityShapeLibrary shapeLibrary,
        BehaviorRegistry? behaviorRegistry = null)
        : base(shapeLibrary)
    {
        _behaviorRegistry = behaviorRegistry;
    }

    // ── Position: prefer NetworkTransform ────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Prefers <see cref="NetworkTransform.LastPosition"/> (updated when the Muscle
    /// publishes WorldPos via the shared <c>GeoSpatialIngressTranslator</c>).
    /// Falls back to <see cref="SimTransform"/> for any entity that holds its own
    /// position locally.  Returns <c>null</c> when neither component is present
    /// or when <see cref="NetworkTransform"/> has not yet received data.
    /// </remarks>
    public override Vector2? GetPosition(ISimulationView view, Entity entity)
    {
        if (view.HasComponent<NetworkTransform>(entity))
        {
            ref readonly var nt = ref view.GetComponentRO<NetworkTransform>(entity);
            // LastRotation remains default(Quaternion) until the first DDS packet arrives;
            // use it as an indicator that real data has arrived.
            if (nt.LastRotation != default(Quaternion))
                return new Vector2(nt.LastPosition.X, nt.LastPosition.Y);
        }

        if (view.HasComponent<SimTransform>(entity))
        {
            ref readonly var st = ref view.GetComponentRO<SimTransform>(entity);
            return new Vector2(st.Position.X, st.Position.Y);
        }

        return null;
    }

    // ── Rotation: prefer NetworkTransform ────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Uses <see cref="NetworkTransform.LastRotation"/> if available (updated by the
    /// <c>GeoSpatialIngressTranslator</c> when the Muscle publishes WorldPos), otherwise
    /// falls back to <see cref="SimTransform.Rotation"/>.
    /// </remarks>
    protected override bool TryGetRotation(ISimulationView view, Entity entity, out Quaternion rotation)
    {
        if (view.HasComponent<NetworkTransform>(entity))
        {
            ref readonly var nt = ref view.GetComponentRO<NetworkTransform>(entity);
            if (nt.LastRotation != default(Quaternion))
            {
                rotation = nt.LastRotation;
                return true;
            }
        }

        return base.TryGetRotation(view, entity, out rotation);
    }

    // ── Domain-specific implementations ──────────────────────────────────────

    /// <inheritdoc/>
    protected override Color ResolveColor(ISimulationView view, Entity entity)
    {
        if (!view.HasComponent<BehaviorState>(entity)) return ColIdle;
        ref readonly var ds = ref view.GetComponentRO<BehaviorState>(entity);
        if (ds.ActiveBehaviorHash == 0) return ColIdle;

        return ds.BrainTier switch
        {
            0 => ColTraffic,
            1 => ColHsm,
            _ => ColBTree,
        };
    }

    /// <inheritdoc/>
    protected override EntityShapeCondition ResolveCondition(ISimulationView view, Entity entity)
    {
        var condition = EntityShapeCondition.None;

        if (view.HasComponent<ActorCapabilityState>(entity))
        {
            ref readonly var caps = ref view.GetComponentRO<ActorCapabilityState>(entity);
            if (!caps.Capabilities.HasFlag(ActorCapabilities.CanMove))
                condition |= EntityShapeCondition.Immobile;
            if (!caps.Capabilities.HasFlag(ActorCapabilities.CanShoot))
                condition |= EntityShapeCondition.Damaged;
        }

        return condition;
    }

    /// <inheritdoc/>
    protected override string? ResolveShapeName(ISimulationView view, Entity entity)
    {
        if (!view.HasComponent<VisualData>(entity)) return null;
        string name = view.GetComponentRO<VisualData>(entity).MapShapeName.ToString();
        return name.Length > 0 ? name : null;
    }

    /// <inheritdoc/>
    /// <remarks>Called inside Raylib <c>BeginMode2D</c>.</remarks>
    public override void Render(
        ISimulationView view,
        Entity          entity,
        Vector2         position,
        RenderContext   ctx,
        bool            isSelected,
        bool            isHovered)
    {
        // Delegate shape geometry to base class.
        base.Render(view, entity, position, ctx, isSelected, isHovered);

        // Compact behavior label to the right of the symbol.
        string label = ResolveShortLabel(view, entity);
        if (label.Length > 0)
        {
            Raylib.DrawText(
                label,
                (int)(position.X + LabelOffsetX),
                (int)(position.Y - LabelFontSize / 2),
                LabelFontSize,
                Color.White);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Builds a multi-line diagnostic summary shown as a tooltip when the user
    /// hovers over an entity on the CGF map.  Covers: net-id, behavior + tier,
    /// locomotion channel, weapon channel, perceived targets, mission plan phase,
    /// and any lost capabilities.
    /// </remarks>
    public override string? GetHoverLabel(ISimulationView view, Entity entity)
    {
        var sb = new StringBuilder();

        if (view.HasComponent<NetworkIdentity>(entity))
        {
            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
            sb.AppendLine($"NetId: {netId.Value}");
        }

        if (view.HasComponent<BehaviorState>(entity))
        {
            ref readonly var ds = ref view.GetComponentRO<BehaviorState>(entity);
            string docName = ResolveBehaviorLabel(ds.ActiveBehaviorHash);
            string tierStr = ds.BrainTier switch
            {
                0 => "T0-Traffic",
                1 => "T1-HSM",
                2 => "T2-BTree",
                _ => $"T{ds.BrainTier}",
            };
            sb.AppendLine($"Behavior: {docName} ({tierStr})");
        }

        if (view.HasComponent<MissionPlanQueue>(entity))
        {
            ref readonly var mpq = ref view.GetComponentRO<MissionPlanQueue>(entity);
            if (mpq.PhaseCount > 0)
                sb.AppendLine($"Mission: phase {mpq.CurrentPhase + 1}/{mpq.PhaseCount}");
        }

        if (view.HasComponent<LocomotionChannel>(entity))
        {
            ref readonly var lc = ref view.GetComponentRO<LocomotionChannel>(entity);
            sb.AppendLine($"Loco: act={lc.ActiveAction} {lc.Status}");
        }

        if (view.HasComponent<WeaponChannel>(entity))
        {
            ref readonly var wc = ref view.GetComponentRO<WeaponChannel>(entity);
            if (wc.ActiveAction != 0)
                sb.AppendLine($"Weapon: act={wc.ActiveAction} {wc.Status}");
        }

        if (view.HasComponent<InteractionChannel>(entity))
        {
            ref readonly var ic = ref view.GetComponentRO<InteractionChannel>(entity);
            if (ic.ActiveAction != 0)
                sb.AppendLine($"Interaction: act={ic.ActiveAction} {ic.Status}");
        }

        if (view.HasComponent<TargetMemory>(entity))
        {
            ref readonly var tm = ref view.GetComponentRO<TargetMemory>(entity);
            sb.AppendLine($"Targets: {tm.Count}");
        }

        if (view.HasComponent<ActorCapabilityState>(entity))
        {
            ref readonly var caps = ref view.GetComponentRO<ActorCapabilityState>(entity);
            bool canMove  = caps.Capabilities.HasFlag(ActorCapabilities.CanMove);
            bool canShoot = caps.Capabilities.HasFlag(ActorCapabilities.CanShoot);
            if (!canMove || !canShoot)
            {
                var incap = new StringBuilder("Incap:");
                if (!canMove)  incap.Append(" !Move");
                if (!canShoot) incap.Append(" !Shoot");
                sb.AppendLine(incap.ToString());
            }
        }

        string result = sb.ToString().TrimEnd();
        return result.Length > 0 ? result : null;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private string ResolveShortLabel(ISimulationView view, Entity entity)
    {
        if (view.HasComponent<BehaviorState>(entity))
        {
            ref readonly var ds = ref view.GetComponentRO<BehaviorState>(entity);
            if (ds.ActiveBehaviorHash != 0)
                return ResolveBehaviorLabel(ds.ActiveBehaviorHash);
        }

        if (view.HasComponent<NetworkIdentity>(entity))
        {
            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
            return $"#{netId.Value}";
        }

        return string.Empty;
    }

    private string ResolveBehaviorLabel(int hash)
    {
        if (hash == 0)
            return "idle";
        if (_behaviorRegistry != null && _behaviorRegistry.TryGetName(hash, out string? name))
            return name;
        return $"#{hash}";
    }
}
