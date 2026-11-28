using System;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
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
    /// <summary>
    /// ⭐ CE-4388 (docs/DESIGN_Map_View_Filter.md D-3, R-1049) — the map scales the card's rows stop at, in ONE place (m per screen pixel). 📐 Measured on Windows
    /// (scale-1 + 1 000 men, the whole terrain at ≈ 2.5 m/px): 1 247 cards drawn at ≈ 5 µs each = 6.6 ms a frame, and ≈ 6 400 primitives built for them, where no
    /// card can be read. 🔒 User, 2026-11-10: <i>"hiding the cards at small zooms, of course, if they cannot be read, then no need to keep them"</i>.
    /// </summary>
    public static class CardZoom
    {
        /// <summary>The frame and the health bar: coarser than this a man is under a pixel and the cards pile into a band.</summary>
        public const float FrameMaxMetresPerPixel = 1f;
        /// <summary>The name (white text, still legible a little further out).</summary>
        public const float NameMaxMetresPerPixel = 2f;
    }

    [GizmoProjector(typeof(NetworkIdentity), typeof(EntityInfo), MaxMetresPerPixel = CardZoom.FrameMaxMetresPerPixel)]
    public sealed class EntityCardFrameGizmo : IStatelessGizmo
    {
        /// <summary>⭐ CE-1480 G5 — the picture is a function of the card's id and side, and of Seeable.OnMap: held while none moved.</summary>
        public GizmoInputs? Inputs { get; } = Seeable.Inputs(typeof(NetworkIdentity), typeof(EntityInfo));

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            int net = view.GetComponentRO<NetworkIdentity>(entity).Value;
            if (net == 0 || !Seeable.OnMap(view, entity)) return;   // ⭐ CE-4239 — no card for a rule / score / evaluation
            draw.Card(net, DebugTraceLayers.Labels, Fdp.Toolkit.Squad.CombatEffective.IsUnit(view, entity)).Row(0).Frame(SidePalette.Of(view.GetComponentRO<EntityInfo>(entity).ForceId));
        }
    }

    /// <summary>⭐ CE-1033 S5b (§3.6) — the entity's name and network id in its card (row 20): <c>"T-72 #1043"</c>, <c>"#1043"</c> unnamed.</summary>
    [GizmoProjector(typeof(NetworkIdentity), typeof(EntityInfo), MaxMetresPerPixel = CardZoom.NameMaxMetresPerPixel)]
    public sealed class EntityNameGizmo : IStatelessGizmo
    {
        public const byte Row = 20;
        private static readonly Rgba32 NameColour = new(240, 240, 240, 255);

        /// <summary>⭐ CE-1480 G5 — the picture is a function of the card's id and name, and of Seeable.OnMap: held while none moved.</summary>
        public GizmoInputs? Inputs { get; } = Seeable.Inputs(typeof(NetworkIdentity), typeof(EntityInfo));

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (IsAircraft(view, entity) && !InAirUnit(view, entity)) return;   // ⭐ CE-1971 — a lone aircraft's name is AircraftNameGizmo's (no zoom limit); ⭐ CE-11029 — a swarm member's follows the ground's rule
            DrawName(view, entity, draw);
        }

        internal static void DrawName(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            int net = view.GetComponentRO<NetworkIdentity>(entity).Value;
            if (net == 0 || !Seeable.OnMap(view, entity)) return;   // ⭐ CE-4239
            // ⭐ CE-11031 (R-2084) — formatted on the STACK (the span overload of CE-4582): the name, " #", the id — no string per card per frame (the string path was ~90 B a card a frame)
            Span<char> text = stackalloc char[96];   // a name is at most 63 characters; " #" and an int are 13 more
            int n = view.GetComponentRO<EntityInfo>(entity).Name.CopyTo(text);
            bool blank = true;
            for (int i = 0; i < n; i++) if (!char.IsWhiteSpace(text[i])) { blank = false; break; }
            if (blank) n = 0; else { text[n++] = ' '; }
            text[n++] = '#';
            net.TryFormat(text[n..], out int digits, default, System.Globalization.CultureInfo.InvariantCulture);
            draw.Card(net, DebugTraceLayers.Labels, Fdp.Toolkit.Squad.CombatEffective.IsUnit(view, entity)).Row(Row).Text(text[..(n + digits)], NameColour);
        }

        /// <summary>⭐ CE-1971 — a fixed-wing or rotary-wing aircraft (never a bird, a rope or a canopy).</summary>
        internal static bool IsAircraft(ISimulationView view, Entity entity)
            => view.HasComponent<Fdp.Toolkit.Motion.Locomotion>(entity)
               && view.GetComponentRO<Fdp.Toolkit.Motion.Locomotion>(entity).Class is Fdp.Toolkit.Tkb.Domain.LocomotionClass.FixedWing or Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft;

        /// <summary>
        /// ⭐ <c>CE-11029</c> (SimNetGateway R-143 / R-144) — the aircraft's DIRECT commander is an AIR UNIT: a body-less unit commander whose live members are ALL
        /// aircraft (a swarm of drones, a flight). Its card stands for the swarm, so the members' names keep the ground's zoom limit. 🔒 User, <c>2026-11-21</c>:
        /// <i>"for expensive aircraft (scarse) each shoudl have a label"</i> — not each of <i>"a swarm of cheap drones (numerous)"</i>. An aircraft under a mixed
        /// HQ (its members are not all aircraft) is NOT in an air unit: it keeps its always-on name. Allocation-free (at most 16 members, R-2084).
        /// </summary>
        internal static bool InAirUnit(ISimulationView view, Entity entity)
        {
            if (view is EntityRepository repo && (!repo.IsComponentTypeRegistered<UnitSubordinate>() || !repo.IsComponentTypeRegistered<UnitRoster>())) return false;
            if (!view.HasComponent<UnitSubordinate>(entity)) return false;
            var commander = view.GetComponentRO<UnitSubordinate>(entity).Commander;
            if (!view.IsAlive(commander) || !Fdp.Toolkit.Squad.CombatEffective.IsUnit(view, commander) || !view.HasComponent<UnitRoster>(commander)) return false;
            var roster = view.GetComponentRO<UnitRoster>(commander);
            int aircraft = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (!view.IsAlive(m)) continue;
                if (!IsAircraft(view, m)) return false;   // a mixed unit is no air unit
                aircraft++;
            }
            return aircraft > 0;
        }
    }

    /// <summary>
    /// ⭐ CE-1971 — an AIRCRAFT's name, at EVERY zoom: the card zoom limit (<see cref="CardZoom"/>) suits ground units — under a pixel they pile into a band —
    /// but an aircraft is small, far and high, few in number, and without its name nothing on the map says where it is. 🔒 User, 2026-11-17: <i>"for the
    /// aircraft we might force different turn of distance, or maybe eliminate it whatsoever, so the label is always shown"</i>. Same row, same card.
    /// </summary>
    [GizmoProjector(typeof(NetworkIdentity), typeof(EntityInfo), typeof(Fdp.Toolkit.Motion.Locomotion))]
    public sealed class AircraftNameGizmo : IStatelessGizmo
    {
        public GizmoInputs? Inputs { get; } = Seeable.Inputs(typeof(NetworkIdentity), typeof(EntityInfo));

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (EntityNameGizmo.IsAircraft(view, entity) && !EntityNameGizmo.InAirUnit(view, entity)) EntityNameGizmo.DrawName(view, entity, draw);   // ⭐ CE-11029
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
            if (s.State != Fdp.Toolkit.ScenarioRules.ObjectiveState.Active) return;
            Span<char> kind = stackalloc char[Fdp.Core.FixedString32.MaxLength];   // ⭐ CE-11031 — no string per status per frame
            if (!MemoryExtensions.Equals((ReadOnlySpan<char>)kind[..s.Kind.CopyTo(kind)], "Bounty", StringComparison.OrdinalIgnoreCase)) return;
            if (!view.HasComponent<Fdp.Toolkit.ScenarioRules.ObjectiveLinks>(entity)) return;
            int subject = view.GetComponentRO<Fdp.Toolkit.ScenarioRules.ObjectiveLinks>(entity).Subject.NetworkId;
            if (subject != 0) draw.Card(subject, DebugTraceLayers.Labels).Row(Row).Text("BOUNTY", Gold);
        }
    }
}
