using Fdp.Kernel;
using Fdp.Examples.Showcase.Components;

namespace Fdp.Examples.Showcase.Systems
{
    public class PatrolSystem : ComponentSystem
    {
        private const float MinX = 0f;
        private const float MaxX = 80f; // Console width roughly
        private const float MinY = 0f;
        private const float MaxY = 24f;
        
        private EntityQuery _query = null!;

        public PatrolSystem(EntityRepository repo)
        {
            Create(repo);
        }

        protected override void OnCreate()
        {
            _query = World.Query()
                .With<Position>()
                .With<Velocity>()
                .With<UnitStats>()
                .Build();
        }

        protected override void OnUpdate()
        {
             _query.ForEachParallel(entity =>
             {
                 ref var pos = ref World.GetComponentRW<Position>(entity);
                 ref var vel = ref World.GetComponentRW<Velocity>(entity);
                 
                 // Bounce logic
                 if (pos.X < MinX || pos.X > MaxX) vel.X = -vel.X;
                 if (pos.Y < MinY || pos.Y > MaxY) vel.Y = -vel.Y;
             });
        }
    }
}
