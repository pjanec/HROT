using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Hrot.Animation.Replication;
using Hrot.Animation.Replication.Translators.Descriptors;
using Xunit;

namespace Hrot.Animation.Replication.Tests;

/// <summary>⭐ <c>CE-2121</c> slice ② — the body-stance wire: each role gets exactly its half of the stance pair, and nothing else.
/// 📄 docs/DESIGN_Decision_Layer.md §3.3g.</summary>
public class StanceWireTests
{
    [Fact]
    public void CE2121_TheBrainSendsTheRequest_AndReceivesTheReport_TheBodyTheReverse()
    {
        var map = new NetworkEntityMap();
        var brain = AnimationReplicationModule.StanceTranslators(null!, map, NodeRole.Brain);
        Assert.Collection(brain, t => Assert.IsType<StanceIntentEgressTranslator>(t), t => Assert.IsType<StanceStatusIngressTranslator>(t));
        var body = AnimationReplicationModule.StanceTranslators(null!, map, NodeRole.MuscleGround);
        Assert.Collection(body, t => Assert.IsType<StanceIntentIngressTranslator>(t), t => Assert.IsType<StanceStatusEgressTranslator>(t));
    }

    [Fact]
    public void CE2121_TheGateOrdinals_AreNedsStanceDescriptors()
    {
        Assert.Equal(104L, StanceIntentEgressTranslator.DescriptorOrdinal);   // dtStanceIntent (Hrot.NED AllDescriptors)
        Assert.Equal(105L, StanceStatusEgressTranslator.DescriptorOrdinal);   // dtStanceStatus
    }
}
