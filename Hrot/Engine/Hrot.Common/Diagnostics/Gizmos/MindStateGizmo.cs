using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>⭐ <c>CE-1309</c> (docs/DESIGN_People_Display.md §7e U20b) — the band a mind-state bar is drawn in.</summary>
    public enum MindBand : byte { None = 0, Calm, Nervous, Broken, Fear }

    /// <summary>
    /// ⭐ <c>CE-1309</c> (People Display §7e U20a–d) — what a man's mind-state bar shows: a SOLDIER's stress (<see cref="Stress"/>) with a tick at
    /// his breaking point (<see cref="Stresses.BreakLevel"/> — his nerve), banded calm / nervous / broken; else a CIVILIAN's fear
    /// (<see cref="CivilianState"/> on the brain node, else the replicated <see cref="Demeanour"/> quarter). Nothing for a calm man. Pure.
    /// </summary>
    public static class MindStateBar
    {
        /// <summary>U20d — a soldier's bar shows from this stress up (or broken) …</summary>
        public const float StressFrom = 0.02f;
        /// <summary>… a civilian's from this fear up.</summary>
        public const float FearFrom = 0.05f;

        /// <summary>The bar of <paramref name="man"/>: its fill 0 … 1, the breaking point's tick (negative: none) and its band.</summary>
        public static (float Fill, float Tick, MindBand Band) Of(ISimulationView view, Entity man)
        {
            if (view is EntityRepository repo && repo.IsComponentTypeRegistered<Stress>() && view.HasComponent<Stress>(man))
            {
                float level = Stresses.LevelOf(view, man), at = Stresses.BreakLevel(view, man);
                bool broken = Stresses.IsBroken(view, man);
                if (!broken && level < StressFrom) return (0f, -1f, MindBand.None);
                var band = broken ? MindBand.Broken : level >= Stresses.NervousFrom * at ? MindBand.Nervous : MindBand.Calm;
                return (System.Math.Clamp(level, 0f, 1f), at, band);
            }
            float fear = -1f;
            if (view is EntityRepository r2 && r2.IsComponentTypeRegistered<CivilianState>() && view.HasComponent<CivilianState>(man))
                fear = view.GetComponentRO<CivilianState>(man).Fear;
            else if (Demeanours.Of(view, man, out var d)) fear = d.Fear / 4f;
            return fear >= FearFrom ? (System.Math.Clamp(fear, 0f, 1f), -1f, MindBand.Fear) : (0f, -1f, MindBand.None);
        }

        /// <summary>U20b — calm slate blue · nervous amber · broken red · fear amber-orange.</summary>
        public static Rgba32 Colour(MindBand band) => band switch
        {
            MindBand.Calm => new Rgba32(110, 150, 200, 255),
            MindBand.Nervous => new Rgba32(240, 170, 40, 255),
            MindBand.Broken => new Rgba32(225, 50, 45, 255),
            MindBand.Fear => new Rgba32(250, 130, 30, 255),
            _ => default,
        };
    }

    /// <summary>
    /// ⭐ <c>CE-1309</c> (docs/DESIGN_People_Display.md §7e U20) — the MIND-STATE bar: one thin row of a person's card right under his health
    /// bar (<see cref="HealthBarGizmo.Row"/>), filled to his stress with a white tick at his breaking point, <c>BROKEN</c> under it when he
    /// breaks; a civilian's fear. On its own layer, <see cref="DebugTraceLayers.MindState"/> ("Mind state", off by default). ⚠ Only where
    /// the brains run — <see cref="Stress"/> is brain-local (TS-8). Auto-discovered on every map host (<c>GizmoReflectionRegistrar</c>).
    /// </summary>
    [GizmoProjector(typeof(SimTransform))]
    public sealed class MindStateGizmo : IStatelessGizmo
    {
        /// <summary>The bar's row: right under the health bar (10), above the name (20).</summary>
        public const byte Row = 11;
        /// <summary>The BROKEN line's row, under the bar.</summary>
        public const byte BrokenRow = 12;
        public const float BarHeight = 4f;
        private static readonly Rgba32 Back = new(40, 40, 40, 220);
        private static readonly Rgba32 TickColour = new(255, 255, 255, 255);

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!draw.IsLayerActive(DebugTraceLayers.MindState) || view is not EntityRepository repo) return;
            if (!PhysicsColliderReaders.IsPerson(repo, entity) || CombatLife.IsDestroyed(repo, entity)) return;
            if (!repo.IsComponentTypeRegistered<NetworkIdentity>() || !view.HasComponent<NetworkIdentity>(entity)) return;   // a card is keyed by it
            long net = view.GetComponentRO<NetworkIdentity>(entity).Value;
            if (net == 0) return;
            var (fill, tick, band) = MindStateBar.Of(view, entity);
            if (band == MindBand.None) return;
            var card = draw.Card(net, DebugTraceLayers.MindState);
            var row = card.Row(Row).Bar(fill, MindStateBar.Colour(band), Back, BarHeight);
            if (tick >= 0f) row.Tick(tick, TickColour, BarHeight);
            if (band == MindBand.Broken) card.Row(BrokenRow).Text("BROKEN", MindStateBar.Colour(band), 12f);
        }
    }
}
