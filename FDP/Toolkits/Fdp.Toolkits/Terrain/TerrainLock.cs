using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ CE-5006 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-17 / TE-18; R-5036 … R-5038) — PLACED terrain is selectable only while the station's
    /// Terrain toolbar is open. 🔒 User, <c>2026-11-16</c>: <i>"Maybe if we simply disallow selecting these features without the toolbar would
    /// fulfill my desired behavior"</i>.
    /// <para>ONE predicate every path asks: the selection store's write, the hover, the entity menu, the authoring gizmos. The OPEN flag is per world
    /// (per station: a world is one station's view) and is never replicated or recorded — a UI fact, like the selection's hover. Closed by default:
    /// a host that never opens the Terrain toolbar (the Player, a display node) never makes placed terrain selectable.</para>
    /// <para>⭐ Placing NEW things is not affected (a placement is not a selection), nor the simulation, nor an order's own target pick.</para>
    /// </summary>
    public static class TerrainLock
    {
        private sealed class Flag { public bool Open; }
        private static readonly ConditionalWeakTable<EntityRepository, Flag> PerWorld = new();

        /// <summary>Opens (the Terrain toolbar is shown) or closes terrain authoring on <paramref name="world"/>.</summary>
        public static void SetOpen(EntityRepository world, bool open) => PerWorld.GetOrCreateValue(world).Open = open;

        /// <summary>Whether terrain authoring is open on <paramref name="view"/>'s world (false for a view that is not a world, and by default).</summary>
        public static bool IsOpen(ISimulationView view) => view is EntityRepository repo && PerWorld.TryGetValue(repo, out var f) && f.Open;

        /// <summary>A PLACED terrain thing: a terrain feature, a block, or a placed obstacle entity.</summary>
        public static bool IsTerrain(ISimulationView view, Entity entity)
            => !entity.IsNull
               && (Has<TerrainFeature>(view, entity) || Has<TerrainBlock>(view, entity) || Has<StaticObstacle>(view, entity));

        /// <summary>A placed terrain thing while terrain authoring is closed: it cannot be selected, hovered or given its menu.</summary>
        public static bool IsLocked(ISimulationView view, Entity entity) => IsTerrain(view, entity) && !IsOpen(view);

        /// <summary>The authoring-only kinds (a feature, a block) — their marks are drawn only while authoring is open (TE-18). A placed obstacle is a
        /// thing of the world (its symbol stays, so an order can still target it).</summary>
        public static bool IsAuthoringMark(ISimulationView view, Entity entity)
            => !entity.IsNull && (Has<TerrainFeature>(view, entity) || Has<TerrainBlock>(view, entity));

        private static bool Has<T>(ISimulationView view, Entity e) where T : unmanaged
            => (view is not EntityRepository repo || repo.IsComponentTypeRegistered<T>()) && view.HasComponent<T>(e);
    }
}
