using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fbt;

namespace Hrot.Blueprints.Tests.MockSystems;

/// <summary>
/// Test-only dispatcher for weapon channel commands.
/// Processes entities with WeaponChannel.ActiveAction != 0 and
/// writes Status back from the configurable NextStatus lambda.
/// </summary>
public sealed class MockWeaponDispatcher : MockDispatcherSystem<WeaponChannel>
{
    public Func<WeaponChannel, NodeStatus> NextStatus { get; set; } = _ => NodeStatus.Success;
    public int InvokeCount { get; private set; }
    public int LastObservedActionInstanceId { get; private set; }

    protected override void HandleChannel(ref WeaponChannel channel, Entity entity, ISimulationView view)
    {
        if (channel.ActiveAction != 0)
        {
            InvokeCount++;
            LastObservedActionInstanceId = (int)channel.ActionInstanceId;
            channel.Status = NextStatus(channel);
        }
    }
}
