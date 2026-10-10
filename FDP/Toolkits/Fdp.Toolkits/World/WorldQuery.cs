using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.World
{
    /// <summary>
    /// ⭐ The one place a caller obtains an <see cref="IWorldQuery"/> (<c>docs/DESIGN_World_Query_Seam.md</c> WQ-A). Today it answers
    /// from the terrain stand-in; a second implementation (Stride, a production engine — slice Q5) plugs in HERE and nowhere else.
    /// </summary>
    public static class WorldQuery
    {
        /// <summary>
        /// The world query for <paramref name="view"/>, bound to that view's dynamic state (its doors — R-219: a background view's
        /// snapshot, never a live value), or null when the view has no world. Allocation-free while nothing changed (R-220).
        /// </summary>
        public static IWorldQuery? Of(ISimulationView view)
        {
            if (view is not EntityRepository repo || !repo.HasSingletonManaged<TerrainWorld>()) return null;
            var world = repo.GetSingletonManaged<TerrainWorld>();
            return world == null ? null : TerrainWorldQuery.For(world, world.Doors.Count > 0 ? DoorStates.Of(view, world) : null);
        }
    }
}
