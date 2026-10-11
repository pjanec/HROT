using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ CE-1033 S5b (docs/DESIGN_Map_3D_Mode.md M16) — ONE side palette: the colour of friend, hostile and neutral, looked up by
    /// <see cref="ForceId"/>. The card frame reads it. ⚠ M16 names two more mappings that still disagree (IG's
    /// <c>ResolvedStyleConstants</c>, the 2525 renderer by symbol letter) — routing them here is left, named in the design.
    /// </summary>
    public static class SidePalette
    {
        public static readonly Rgba32 Friend = new(90, 150, 255, 255);
        public static readonly Rgba32 Hostile = new(235, 72, 60, 255);
        public static readonly Rgba32 Neutral = new(96, 200, 100, 255);

        public static Rgba32 Of(ForceId force) => force switch
        {
            ForceId.Friend => Friend,
            ForceId.Hostile => Hostile,
            _ => Neutral,
        };
    }

    /// <summary>
    /// ⭐ CE-1033 S5b (§3.6) — an entity card's FRAME (row 0): a translucent background and an outline in its side colour. Together
    /// with <see cref="EntityNameGizmo"/> and <see cref="HealthBarGizmo"/> it makes the card the user asked for (U19) — each an
    /// ordinary gizmo, on the Labels layer, in 2-D and 3-D alike.
    /// </summary>
    [GizmoProjector(typeof(NetworkIdentity), typeof(EntityInfo))]
    public sealed class EntityCardFrameGizmo : IStatelessGizmo
    {
        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            long net = view.GetComponentRO<NetworkIdentity>(entity).Value;
            if (net == 0) return;
            draw.Card(net, DebugTraceLayers.Labels).Row(0).Frame(SidePalette.Of(view.GetComponentRO<EntityInfo>(entity).ForceId));
        }
    }

    /// <summary>⭐ CE-1033 S5b (§3.6) — the entity's name and network id in its card (row 20): <c>"T-72 #1043"</c>, <c>"#1043"</c> unnamed.</summary>
    [GizmoProjector(typeof(NetworkIdentity), typeof(EntityInfo))]
    public sealed class EntityNameGizmo : IStatelessGizmo
    {
        public const byte Row = 20;
        private static readonly Rgba32 NameColour = new(240, 240, 240, 255);

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            long net = view.GetComponentRO<NetworkIdentity>(entity).Value;
            if (net == 0) return;
            string name = view.GetComponentRO<EntityInfo>(entity).Name.ToString();
            draw.Card(net, DebugTraceLayers.Labels).Row(Row).Text(string.IsNullOrWhiteSpace(name) ? $"#{net}" : $"{name} #{net}", NameColour);
        }
    }
}
