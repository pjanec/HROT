using Fdp.Kernel;
using Fdp.Examples.Showcase.Components;

namespace Fdp.Examples.Showcase.Systems
{
    public class MovementSystem : ComponentSystem
    {
        private EntityQuery _query = null!;

        public MovementSystem(EntityRepository repo)
        {
            Create(repo);
        }

        protected override void OnCreate()
        {
            _query = World.Query()
                .With<Position>()
                .With<Velocity>()
                .Build();
        }

        protected override void OnUpdate()
        {
            ref var time = ref World.GetSingletonUnmanaged<GlobalTime>();
            float dt = time.DeltaTime * time.TimeScale;

            _query.ForEachParallel(entity =>
            {
                ref var pos = ref World.GetComponentRW<Position>(entity);
                ref var vel = ref World.GetComponentRW<Velocity>(entity);

                pos.X += vel.X * dt;
                pos.Y += vel.Y * dt;
            });
        }
    }
}
