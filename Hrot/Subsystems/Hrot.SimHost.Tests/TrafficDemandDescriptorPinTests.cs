using Hrot.NED.Descriptors;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐ AQ93 §12 N4/S2 — <c>Hrot.Traffic</c> cannot reference the NED assembly, so the traffic mirror carries the
/// <c>dtTrafficDemand</c> ordinal as a constant to ask "do I serve these settings?". This pins the two equal: a renumbered
/// descriptor would otherwise make every node silently serve nothing.
/// </summary>
public class TrafficDemandDescriptorPinTests
{
    [Fact]
    public void TheMirrorsDescriptorConstant_IsNedsTrafficDemandDescriptor()
        => Assert.Equal((long)EDescriptorType.dtTrafficDemand, Hrot.Traffic.TrafficMirrorSystem.TrafficDemandDescriptor);
}
