using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fbt;

namespace Fdp.Toolkit.Behavior.Executors
{
    /// <summary>
    /// Parameters packed into InteractionChannel.Params for the OpenDoor action.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct OpenDoorParams
    {
        /// <summary>The door entity to interact with.</summary>
        public Entity TargetDoor;
    }

    /// <summary>
    /// Stub executor for the OpenDoor interaction action (kind = 4).
    /// Instantly returns Success for the Slice 1 demo.
    /// </summary>
    public class OpenDoorExecutor : IActionExecutor<InteractionChannel>
    {
        public void OnEnter(Entity entity, ref InteractionChannel channel, EntityRepository world)
        {
            // Acknowledge the command has started
            channel.Status = NodeStatus.Running;
        }

        public void Execute(Entity entity, ref InteractionChannel channel, EntityRepository world, float dt)
        {
            // Instantly succeed without performing real door-opening logic
            channel.Status = NodeStatus.Success;
        }

        public void OnExit(Entity entity, ref InteractionChannel channel, EntityRepository world)
        {
            // No cleanup required
        }
    }
}
