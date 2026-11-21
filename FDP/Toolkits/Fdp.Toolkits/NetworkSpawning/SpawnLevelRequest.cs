using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.NetworkSpawning
{
    /// <summary>
    /// ⭐ <c>CE-3271</c> (Q94 D, R-3018 — 📄 docs/blueprints/Architect_Question_94_Brain_Geometry_Sites.md §5.2a) — a spawn's birth level,
    /// asked by the creator and resolved by the node that OWNS THE POSITION (it has the live terrain: a floor destroyed before the spawn is
    /// seen there). Muscle group, replicated as <c>dtSpawnLevel</c>. It stays on the entity once <see cref="Resolved"/>, so a late or
    /// repeated request can never re-snap a man who has since moved.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.SpawnLevelRequest)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct SpawnLevelRequest
    {
        public SpawnHeightMode Mode;
        public short Level;
        /// <summary>1 once the position's owner applied it (or found no terrain); 0 while it waits.</summary>
        public byte Resolved;

        public readonly SpawnHeight Height => new(Mode, Level);
    }

    /// <summary>⭐ <c>CE-3271</c> — the one place a birth level is asked for and applied.</summary>
    public static class SpawnLevels
    {
        /// <summary>Asks for <paramref name="height"/> on <paramref name="entity"/> (no terrain read here). A world without the component
        /// type (a host that does not register it) resolves in place, as before CE-3271.</summary>
        public static void Request(EntityRepository world, Entity entity, SpawnHeight height)
        {
            if (height.Mode == SpawnHeightMode.Absolute || !world.HasComponent<SimTransform>(entity)) return;
            if (!world.IsComponentTypeRegistered<SpawnLevelRequest>())
            {
                Apply(world, entity, height);
                return;
            }
            world.AddComponent(entity, new SpawnLevelRequest { Mode = height.Mode, Level = height.Level });
        }

        /// <summary>Resolves <paramref name="entity"/>'s waiting request if THIS node owns its position; true when it did.</summary>
        public static bool TryResolve(EntityRepository world, Entity entity)
        {
            ref var request = ref world.GetComponentRW<SpawnLevelRequest>(entity);
            if (request.Resolved != 0 || !world.HasComponent<SimTransform>(entity) || !world.HasAuthority<SimTransform>(entity)) return false;
            Apply(world, entity, request.Height);
            request.Resolved = 1;
            return true;
        }

        /// <summary>Replaces the sent Z with the level's surface at the entity's (x, y); no terrain resident ⇒ the sent Z stands.</summary>
        private static void Apply(EntityRepository world, Entity entity, SpawnHeight height)
        {
            if (Fdp.Toolkit.World.WorldQuery.Of(world) is not { } terrain) return;   // ⭐ CE-1035 Q2
            var xf = world.GetComponent<SimTransform>(entity);
            float levelZ = terrain.ResolveLevel(xf.Position.X, xf.Position.Y, height.Level);
            xf.Position.Z = height.Apply(levelZ, xf.Position.Z);
            world.SetComponent(entity, xf);
        }
    }
}
