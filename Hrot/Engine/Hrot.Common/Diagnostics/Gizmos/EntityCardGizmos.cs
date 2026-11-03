using System;
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
        // ⭐ CE-1163 (Symbology §3.2) — the FOURTH side palette folded into the one table (ResolvedStyleConstants, via AffiliationColors),
        //   so the card frame, the 2-D outline and the IG tint agree
        public static readonly Rgba32 Friend = AffiliationColors.Of(ForceId.Friend);
        public static readonly Rgba32 Hostile = AffiliationColors.Of(ForceId.Hostile);
        public static readonly Rgba32 Neutral = AffiliationColors.Of(ForceId.Neutral);

        public static Rgba32 Of(ForceId force) => AffiliationColors.Of(force);
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
            int net = view.GetComponentRO<NetworkIdentity>(entity).Value;
            if (net == 0 || !Seeable.OnMap(view, entity)) return;   // ⭐ CE-4239 — no card for a rule / score / evaluation
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
            int net = view.GetComponentRO<NetworkIdentity>(entity).Value;
            if (net == 0 || !Seeable.OnMap(view, entity)) return;   // ⭐ CE-4239
            string name = view.GetComponentRO<EntityInfo>(entity).Name.ToString();
            draw.Card(net, DebugTraceLayers.Labels).Row(Row).Text(string.IsNullOrWhiteSpace(name) ? $"#{net}" : $"{name} #{net}", NameColour);
        }
    }

    /// <summary>
    /// ⭐ CE-4229 (rules runnable ⑨a; DESIGN_Player_HUD_And_Debrief PH-8 <c>game.bounty-marker</c>) — the BOUNTY marker on the 2-D (and 3-D) map: a gold <i>BOUNTY</i> line on the card of the man an Active <c>Bounty</c> status is ON (its <c>Subject</c>).
    /// ⭐ Projected over the STATUS, drawn into the SUBJECT's card — the one per-entity overlay seam (the Labels layer), no new layer. The possession view's own marker (<c>PlayerHudLayer</c>) reads the same statuses (<c>GameHud.Build.BountyOn</c>).
    /// </summary>
    [GizmoProjector(typeof(NetworkIdentity), typeof(Fdp.Toolkit.ScenarioRules.ObjectiveStatus))]
    public sealed class BountyMarkerGizmo : IStatelessGizmo
    {
        public const byte Row = 10;
        private static readonly Rgba32 Gold = new(255, 210, 122, 255);

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            ref readonly var s = ref view.GetComponentRO<Fdp.Toolkit.ScenarioRules.ObjectiveStatus>(entity);
            if (s.State != Fdp.Toolkit.ScenarioRules.ObjectiveState.Active || !s.Kind.ToString().Equals("Bounty", StringComparison.OrdinalIgnoreCase)) return;
            if (!view.HasComponent<Fdp.Toolkit.ScenarioRules.ObjectiveLinks>(entity)) return;
            int subject = view.GetComponentRO<Fdp.Toolkit.ScenarioRules.ObjectiveLinks>(entity).Subject.NetworkId;
            if (subject != 0) draw.Card(subject, DebugTraceLayers.Labels).Row(Row).Text("BOUNTY", Gold);
        }
    }
}
