using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Civilians
{
    /// <summary>
    /// ⭐ <c>CE-3265</c> (G2, MD-1; docs/DESIGN_Civilians_And_Crowd_Control.md §7.18) — how a civilian SEEMS: his mode and his fear and anger in
    /// quarters (0 … 4). On the man, Brain group, replicated as <c>dtDemeanour</c> (153) — the brain-private <see cref="CivilianState"/> stays
    /// private. Written only through <see cref="Demeanours.Set"/>, and only when a quarter or the mode changes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Demeanour)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct Demeanour
    {
        public CivilianMode Mode;
        /// <summary>Fear in quarters: 0 calm … 4 terrified.</summary>
        public byte Fear;
        /// <summary>Anger in quarters: 0 … 4.</summary>
        public byte Anger;
    }

    /// <summary>⭐ <c>CE-3265</c> — the one writer and the one reader of <see cref="Demeanour"/>. The writer's CALL SITE is behaviors' (§5o,
    /// <c>CE-2250</c>: <c>CrowdSystem</c>'s pass).</summary>
    public static class Demeanours
    {
        /// <summary>0 … 1 in quarters: 0 · 1 (≥ 0.125) · 2 (≥ 0.375) · 3 (≥ 0.625) · 4 (≥ 0.875).</summary>
        public static byte Quarter(float value) => (byte)Math.Clamp((int)MathF.Round(Math.Clamp(value, 0f, 1f) * 4f, MidpointRounding.AwayFromZero), 0, 4);

        /// <summary>Writes <paramref name="man"/>'s demeanour when it CHANGED (the mode, or a quarter of fear or anger); true when it wrote.
        /// Call it on the node that owns his brain.</summary>
        public static bool Set(EntityRepository repo, Entity man, CivilianMode mode, float fear, float anger)
        {
            if (!repo.IsAlive(man) || !repo.IsComponentTypeRegistered<Demeanour>()) return false;
            var d = new Demeanour { Mode = mode, Fear = Quarter(fear), Anger = Quarter(anger) };
            if (repo.HasComponent<Demeanour>(man))
            {
                ref var cur = ref repo.GetComponentRW<Demeanour>(man);
                if (cur.Mode == d.Mode && cur.Fear == d.Fear && cur.Anger == d.Anger) return false;
                cur = d;
                return true;
            }
            repo.AddComponent(man, d);
            return true;
        }

        /// <summary>The man's demeanour on this node (the owner's or the replicated one); <c>false</c> when he has none.</summary>
        public static bool Of(ISimulationView view, Entity man, out Demeanour demeanour)
        {
            demeanour = default;
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<Demeanour>()) return false;
            if (!view.IsAlive(man) || !view.HasComponent<Demeanour>(man)) return false;
            demeanour = view.GetComponentRO<Demeanour>(man);
            return true;
        }
    }
}
