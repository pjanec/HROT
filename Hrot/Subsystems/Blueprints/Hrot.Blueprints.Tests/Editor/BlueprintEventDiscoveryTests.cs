using System.Linq;
using Fdp.Core;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Editor.AiShared;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>Q#14 slice 1b: reflection discovery of [BlueprintEvent] structs (the 2a C# path).</summary>
public sealed class BlueprintEventDiscoveryTests
{
    // A test event carrier in a Hrot* assembly so the discovery scan sees it.
    [BlueprintEvent("TestEvents", DisplayName = "Test Ping")]
    public struct TestPingEvent
    {
        [EventTarget] public Entity Target;
        public int   Count;
        public float Strength;
    }

    [Fact]
    public void Discover_FindsBlueprintEvent_WithReflectedFieldsAndTarget()
    {
        var ping = BlueprintEventDiscovery.Discover()
            .FirstOrDefault(e => e.EventTypeFqn.EndsWith("TestPingEvent", System.StringComparison.Ordinal));

        Assert.NotNull(ping);
        Assert.Equal("Test Ping", ping!.DisplayName);
        Assert.Equal("TestEvents", ping.Category);
        Assert.Equal("Target", ping.TargetFieldName);

        Assert.Contains(ping.Fields, f => f.Name == "Count"    && f.TypeId == "System.Int32");
        Assert.Contains(ping.Fields, f => f.Name == "Strength" && f.TypeId == "System.Single");
        Assert.Contains(ping.Fields, f => f.Name == "Target");
    }
}
