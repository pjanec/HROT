using Fdp.Kernel;

namespace Fdp.Toolkit.Behavior.Executors
{
    public interface IActionExecutor<TChannel> where TChannel : struct
    {
        void OnEnter(Entity entity, ref TChannel channel, EntityRepository world);

        /// <summary>
        /// Drive the active action for one simulation frame.
        /// To signal completion or failure, write directly into <paramref name="channel"/>:
        ///   channel.Status = NodeStatus.Success;  // or NodeStatus.Failure
        /// This direct write is intentional — zero allocation, no boxing.
        /// </summary>
        void Execute(Entity entity, ref TChannel channel, EntityRepository world, float dt);

        void OnExit(Entity entity, ref TChannel channel, EntityRepository world);
    }
}
