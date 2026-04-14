using System.Numerics;
using System.Text;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Kernel;
using Fdp.ModuleHost.Abstractions;
using Raylib_cs;

namespace Hrot.CGF;

/// <summary>
/// Visualizer adapter for the CGF (Brain) perspective.
///
/// <para>Renders each entity as a colour-coded circle with a compact doctrine label,
/// and provides a rich brain-state hover tooltip for rapid diagnostics without
/// opening the entity inspector.</para>
///
/// <para>Colour coding:
/// <list type="bullet">
///   <item><b>Gray</b>   — no doctrine / idle.</item>
///   <item><b>Blue</b>   — BTree doctrine active (Brain tier 2).</item>
///   <item><b>Teal</b>   — HSM doctrine active (Brain tier 1).</item>
///   <item><b>Amber</b>  — Traffic doctrine active (Brain tier 0).</item>
///   <item><b>Yellow</b> — selected by user.</item>
///   <item><b>Orange</b> — hovered by mouse.</item>
/// </list>
/// </para>
///
/// <para>Position is read from <see cref="NetworkTransform"/> (written by
/// <c>GeoSpatialIngressTranslator</c> when the Muscle publishes WorldPos back to the Brain)
/// with a fallback to <see cref="SimTransform"/> for locally-owned entities that have not
/// yet delegated authority.</para>
/// </summary>
internal sealed class CgfDebugVisualizerAdapter : IVisualizerAdapter
{
    // ── Rendering constants ───────────────────────────────────────────────────
    private const int   CircleRadius  = 8;
    private const float HitRadiusWorld = 10f;
    private const int   LabelFontSize = 8;
    private const int   LabelOffsetX  = 12;

    // ── Colours ───────────────────────────────────────────────────────────────
    private static readonly Color ColIdle     = new(120, 120, 120, 255); // gray
    private static readonly Color ColBTree    = new(0,   120, 220, 255); // blue
    private static readonly Color ColHsm      = new(0,   180, 120, 255); // teal
    private static readonly Color ColTraffic  = new(200, 140,  30, 255); // amber
    private static readonly Color ColSelected = Color.Yellow;
    private static readonly Color ColHovered  = Color.Orange;
    private static readonly Color ColOutline  = new(200, 200, 200, 180);

    // ── CGF brain registry for doctrine name resolution ───────────────────────
    private readonly DoctrineRegistry? _doctrineRegistry;

    /// <param name="doctrineRegistry">
    /// Optional registry; when provided, doctrine hashes are resolved to human-readable
    /// names in the hover label and the compact map annotation.
    /// </param>
    public CgfDebugVisualizerAdapter(DoctrineRegistry? doctrineRegistry = null)
    {
        _doctrineRegistry = doctrineRegistry;
    }

    // ── IVisualizerAdapter ────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Prefers <see cref="NetworkTransform.LastPosition"/> (updated when the Muscle
    /// publishes WorldPos via the shared <c>GeoSpatialIngressTranslator</c>).
    /// Falls back to <see cref="SimTransform"/> for any entity that holds its own
    /// position locally.  Returns <c>null</c> when neither component is present
    /// or when <see cref="NetworkTransform"/> has not yet received data (its
    /// <c>LastRotation</c> remains the zero quaternion before any packet arrives).
    /// </remarks>
    public Vector2? GetPosition(ISimulationView view, Entity entity)
    {
        if (view.HasComponent<NetworkTransform>(entity))
        {
            ref readonly var nt = ref view.GetComponentRO<NetworkTransform>(entity);
            // LastRotation remains default(Quaternion) until the first DDS packet
            // is received; use it as an indicator that real data has arrived.
            if (nt.LastRotation != default(System.Numerics.Quaternion))
                return new Vector2(nt.LastPosition.X, nt.LastPosition.Y);
        }

        if (view.HasComponent<SimTransform>(entity))
        {
            ref readonly var st = ref view.GetComponentRO<SimTransform>(entity);
            return new Vector2(st.Position.X, st.Position.Y);
        }

        return null;
    }

    /// <inheritdoc/>
    public float GetHitRadius(ISimulationView view, Entity entity) => HitRadiusWorld;

    /// <inheritdoc/>
    /// <remarks>Called inside Raylib <c>BeginMode2D</c>.</remarks>
    public void Render(
        ISimulationView view,
        Entity          entity,
        Vector2         position,
        RenderContext   ctx,
        bool            isSelected,
        bool            isHovered)
    {
        Color fill = ResolveColor(view, entity, isSelected, isHovered);

        Raylib.DrawCircleV(position, CircleRadius, fill);
        Raylib.DrawCircleLinesV(position, CircleRadius, ColOutline);

        if (isSelected)
            Raylib.DrawCircleLinesV(position, CircleRadius + 4f, ColSelected);

        // Compact label: doctrine name (or net-id if no doctrine) to the right of the symbol.
        string label = ResolveShortLabel(view, entity);
        if (label.Length > 0)
        {
            Raylib.DrawText(
                label,
                (int)(position.X + CircleRadius + LabelOffsetX),
                (int)(position.Y - LabelFontSize / 2),
                LabelFontSize,
                Color.White);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Builds a multi-line diagnostic summary shown as a tooltip when the user
    /// hovers over an entity on the CGF map.  Covers: net-id, doctrine + tier,
    /// locomotion channel, weapon channel, perceived targets, mission plan phase,
    /// and any lost capabilities.
    /// </remarks>
    public string? GetHoverLabel(ISimulationView view, Entity entity)
    {
        var sb = new StringBuilder();

        if (view.HasComponent<NetworkIdentity>(entity))
        {
            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
            sb.AppendLine($"NetId: {netId.Value}");
        }

        if (view.HasComponent<DoctrineState>(entity))
        {
            ref readonly var ds = ref view.GetComponentRO<DoctrineState>(entity);
            string docName = ResolveDoctrineLabel(ds.ActiveDoctrineHash);
            string tierStr = ds.BrainTier switch
            {
                0 => "T0-Traffic",
                1 => "T1-HSM",
                2 => "T2-BTree",
                _ => $"T{ds.BrainTier}",
            };
            sb.AppendLine($"Doctrine: {docName} ({tierStr})");
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

    private Color ResolveColor(ISimulationView view, Entity entity, bool isSelected, bool isHovered)
    {
        if (isSelected) return ColSelected;
        if (isHovered)  return ColHovered;

        if (!view.HasComponent<DoctrineState>(entity))
            return ColIdle;

        ref readonly var ds = ref view.GetComponentRO<DoctrineState>(entity);
        if (ds.ActiveDoctrineHash == 0)
            return ColIdle;

        return ds.BrainTier switch
        {
            0 => ColTraffic,
            1 => ColHsm,
            _ => ColBTree,
        };
    }

    private string ResolveShortLabel(ISimulationView view, Entity entity)
    {
        if (view.HasComponent<DoctrineState>(entity))
        {
            ref readonly var ds = ref view.GetComponentRO<DoctrineState>(entity);
            if (ds.ActiveDoctrineHash != 0)
                return ResolveDoctrineLabel(ds.ActiveDoctrineHash);
        }

        if (view.HasComponent<NetworkIdentity>(entity))
        {
            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
            return $"#{netId.Value}";
        }

        return string.Empty;
    }

    private string ResolveDoctrineLabel(int hash)
    {
        if (hash == 0)
            return "idle";
        if (_doctrineRegistry != null && _doctrineRegistry.TryGetName(hash, out string? name))
            return name;
        return $"#{hash}";
    }
}
