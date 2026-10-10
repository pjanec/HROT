using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.World;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// ⭐ CE-1035 Q0b (<c>docs/DESIGN_World_Query_Seam.md</c> WQ-H) — where an intent's destination really is. The brain sends a real
    /// 3-D point when its source has one (an EQS sample, an entity, a waypoint), and a 2-D intent with
    /// <see cref="NavigationIntent.OnSurface"/> when it has only (x, y). This is the ONE place the motion side turns the second into a
    /// point: on the surface at (x, y) nearest the mover's own level (the ground, a deck, a floor) — so the brain never has to know
    /// what the terrain looks like (R-252), and nothing reads Z = 0 as "the ground".
    /// </summary>
    public static class NavigationDestination
    {
        /// <summary>The destination of <paramref name="intent"/> for <paramref name="mover"/>, as a real 3-D point.</summary>
        public static Vector3 Of(ISimulationView view, Entity mover, in NavigationIntent intent)
        {
            var d = intent.FinalDestination;
            if (!intent.OnSurface) return d;
            var world = WorldQuery.Of(view);
            if (world == null) return d;   // no world: the stand-in's flat ground at the authored Z, as before
            float level = view.HasComponent<SimTransform>(mover) ? view.GetComponentRO<SimTransform>(mover).Position.Z : d.Z;
            return new Vector3(d.X, d.Y, world.SurfaceZ(d.X, d.Y, level));
        }
    }
}
