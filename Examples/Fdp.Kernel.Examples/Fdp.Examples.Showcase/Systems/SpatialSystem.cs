using Fdp.Kernel;
using Fdp.Examples.Showcase.Components;

namespace Fdp.Examples.Showcase.Systems
{
    public class SpatialSystem : ComponentSystem
    {
        public SpatialMap Map { get; } = new SpatialMap();
        private EntityQuery _query = null!;

        public SpatialSystem(EntityRepository repo)
        {
            Create(repo);
        }

        protected override void OnCreate()
        {
            // Only map things that can collide or be shot (Position + UnitStats)
            _query = World.Query().With<Position>().With<UnitStats>().Build();
        }

        protected override void OnUpdate()
        {
            Map.Clear();
            // This can't be parallelized easily without concurrent collections (slow)
            foreach (var e in _query)
            {
                ref readonly var pos = ref World.GetComponentRO<Position>(e);
                Map.Add(e, pos);
            }
        }
    }
}
