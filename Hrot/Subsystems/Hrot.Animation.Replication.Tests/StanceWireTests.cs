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

    /// <summary>⭐ CE-1139 — a VIEWER (the IG) takes BOTH stance topics in and sends neither, and a received request lands as the
    /// unit's <c>StanceIntent</c> even on a ghost that never carried one — so <c>LogicalStance.Of</c> (what the figure and the
    /// first-person eye read) answers the stance the brain asked for, not Standing.</summary>
    [Fact]
    public void CE1139_AViewer_TakesBothStanceTopicsIn_AndARequestReachesItsGhost()
    {
        var map = new NetworkEntityMap();
        var viewer = AnimationReplicationModule.StanceViewerTranslators(null!, map);
        Assert.Collection(viewer, t => Assert.IsType<StanceIntentIngressTranslator>(t), t => Assert.IsType<StanceStatusIngressTranslator>(t));
        Assert.All(viewer, t => Assert.Equal(Fdp.Interfaces.TranslatorDirection.Ingress, t.Direction));

        using var world = new EntityRepository();
        world.RegisterComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>();
        var ghost = world.CreateEntity();
        map.Register(42, ghost);
        using var cmd = new EntityCommandBuffer();
        ((StanceIntentIngressTranslator)viewer[0]).ProcessSample(
            new DdsStanceIntent { EntityId = 42, TargetStance = (byte)Fdp.Toolkit.Tkb.Domain.StanceId.Crouched, Version = 1 }, cmd, world);
        cmd.Playback(world);
        Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Crouched, Hrot.MuscleCharacter.Animation.Components.LogicalStance.Of(world, ghost));
    }

    [Fact]
    public void CE2121_TheGateOrdinals_AreNedsStanceDescriptors()
    {
        Assert.Equal(104L, StanceIntentEgressTranslator.DescriptorOrdinal);   // dtStanceIntent (Hrot.NED AllDescriptors)
        Assert.Equal(105L, StanceStatusEgressTranslator.DescriptorOrdinal);   // dtStanceStatus
    }
}
