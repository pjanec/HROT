using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Physics.Components;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ <c>CE-1170</c> (docs/DESIGN_People_Display.md §5 U9, R-1005) — <b>AIMED AT A PERSON: a thin line from the muzzle to him</b>, in 2-D
    /// and 3-D, on the <c>UnitDetails</c> layer (off by default — an always-on line would clutter a crowd). Reads the replicated
    /// <see cref="WeaponPosture"/> (<c>Aimed</c> + <c>Target</c>), so every node draws it. A man aimed at a vehicle, or at a point, gets
    /// no line (the posture already shows it). Auto-discovered on every map host (<c>GizmoReflectionRegistrar</c>).
    /// </summary>
    [GizmoProjector(typeof(WeaponPosture))]
    public sealed class AimLineGizmo : IStatelessGizmo
    {
        /// <summary>Thin and red-orange: a threat, not a selection.</summary>
        public static readonly Rgba32 Colour = new(255, 90, 40, 220);

        /// <summary>A standing man's shoulder (the muzzle when his type has no muzzle data — a rifleman) and a man's chest, above his feet (m).</summary>
        public const float ShoulderZ = 1.4f, ChestZ = 1.2f;

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (view is not EntityRepository repo) return;
            if (!Of(repo, entity, out var from, out var to)) return;
            draw.DrawLine(from, to, Colour, thickness: 1f, layer: DebugTraceLayers.UnitDetails);
        }

        /// <summary>The line, when <paramref name="man"/> aims at a living PERSON: from his weapon's muzzle (or his shoulder) to the target's
        /// chest. Public for the rail.</summary>
        public static bool Of(EntityRepository repo, Entity man, out Vector3 from, out Vector3 to)
        {
            from = to = default;
            if (!repo.IsComponentTypeRegistered<WeaponPosture>() || !repo.IsComponentTypeRegistered<SimTransform>()) return false;
            if (!repo.IsAlive(man) || !repo.HasComponent<WeaponPosture>(man) || !repo.HasComponent<SimTransform>(man)) return false;
            ref readonly var posture = ref repo.GetComponentRO<WeaponPosture>(man);
            var target = posture.Target;
            if (posture.Kind != WeaponPostureKind.Aimed || target == Entity.Null || !repo.IsAlive(target)) return false;
            if (!repo.HasComponent<SimTransform>(target) || !PhysicsColliderReaders.IsPerson(repo, target)) return false;

            var self = repo.GetComponentRO<SimTransform>(man).Position;
            to = repo.GetComponentRO<SimTransform>(target).Position + new Vector3(0f, 0f, ChestZ);
            if (!Muzzle.TryOf(repo, man, posture.ActiveMount, out from))
            {
                var shoulder = self + new Vector3(0f, 0f, ShoulderZ);
                var flat = new Vector3(to.X - self.X, to.Y - self.Y, 0f);
                from = flat.LengthSquared() > 0.25f ? shoulder + Vector3.Normalize(flat) * 0.5f : shoulder;   // the barrel's half metre ahead
            }
            return true;
        }
    }
}
