using Fdp.Toolkit.Traffic;
using Hrot.Map.Common.Replication.Egress;

namespace Hrot.Map.Common.Tests.Replication.Egress;

/// <summary>⭐ AQ93 §12 N4 / §15 K1 — every field of the traffic settings reaches the wire: a field added to <see cref="TrafficDemand"/>
/// and not to <c>EntityTrafficDemand</c> (or not copied) would be silently lost on every receiver, the traffic node included.</summary>
public sealed class EntityTrafficDemandWireTests
{
    [Fact]
    public void EveryTrafficDemandField_IsOnTheWire_AndCopied()
    {
        var demand = new TrafficDemand
        {
            TargetCars = 11, Seed = 22, StepSeconds = 0.25f, TargetPedestrians = 33, ShotPanicRadius = 44f, BlastPanicRadius = 55f, MaxConverted = 66,
        };
        var wire = EntityTrafficDemandEgressTranslator.ToSample(7, demand);

        foreach (var f in typeof(TrafficDemand).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            var w = typeof(Hrot.NED.Descriptors.EntityTrafficDemand).GetField(f.Name);
            Assert.True(w is not null, $"TrafficDemand.{f.Name} has no wire field");
            Assert.Equal(f.GetValue(demand), w!.GetValue(wire));
        }
    }
}
