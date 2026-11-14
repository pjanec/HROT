using System;
using Fdp.Core;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ R-219 test helper — door states the way production gets them: door ENTITIES in a world, read back as that world's
    /// <see cref="DoorStates"/>. ⛔ No test sets a door on the terrain itself any more: the terrain holds no live state.
    /// </summary>
    internal static class DoorFixtures
    {
        public static EntityRepository WorldWithDoors(TerrainWorld terrain, params (string Key, TerrainDoorState State)[] doors)
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<DoorState>();
            repo.RegisterManagedComponent<TerrainObjectKey>();
            repo.SetSingletonManaged(terrain);
            foreach (var (key, state) in doors)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new DoorState { State = state });
                repo.SetManagedComponent(e, new TerrainObjectKey { Key = key });
            }
            return repo;
        }

        /// <summary>The doors of <paramref name="terrain"/> with the given ones set — as a view holding those door entities sees them.</summary>
        public static DoorStates States(TerrainWorld terrain, params (string Key, TerrainDoorState State)[] doors)
        {
            using var repo = WorldWithDoors(terrain, doors);
            return DoorStates.Of(repo, terrain);
        }
    }
}
