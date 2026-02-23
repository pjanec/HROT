using Fdp.Kernel;

namespace FDP.Toolkit.Behavior.Executors
{
    public interface IActionExecutor<TChannel> where TChannel : struct
    {
        void OnEnter(Entity entity, ref TChannel channel, EntityRepository world);
        void Execute(Entity entity, ref TChannel channel, EntityRepository world, float dt);
        void OnExit(Entity entity, ref TChannel channel, EntityRepository world);
    }
}
