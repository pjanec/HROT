using System.Linq;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Abstractions;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Common;
using Hrot.Network.Replication;
using Hrot.Network.Routing;
using Xunit;
using EDescriptorType = Hrot.NED.Descriptors.EDescriptorType;

namespace Hrot.Network.NED.Tests;

/// <summary>
/// ⭐⭐⭐ <b>S3 — the creator grants each applicable ROLE GROUP to a node of that role</b> (push-only, R-164).
/// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §2 G-1/G-4/G-5/G-6, §5.2. The production table and the NED
/// descriptor binding are used as-is, so a change to either that alters who gets what reddens a row here.
/// </summary>
public sealed class RoleGroupOwnershipStrategyTests
{
    private const int SimHost = 1, Cgf = 400, Ig = 300;

    private static long D(EDescriptorType d) => (long)d;
    private static readonly long[] Brain = { D(EDescriptorType.dtEntityDamage), D(EDescriptorType.dtEntityMission),
        D(EDescriptorType.dtNavigationIntent), D(EDescriptorType.dtSensorConfig), D(EDescriptorType.dtEqsSensorConfig) };
    private static readonly long[] Muscle     = { D(EDescriptorType.dtWorldPos), D(EDescriptorType.dtNavigationStatus) };
    private static readonly long[] Perception = { D(EDescriptorType.dtEqsResult) };

    private static RoleGroupOwnershipStrategy Strategy(bool withBrain = true, bool withMuscle = true)
    {
        var cache = new SimpleClusterStateCache();
        if (withMuscle)
            cache.UpdateNode(new NodeCapability { NodeId = SimHost, Role = NodeRole.MuscleGround | NodeRole.Perception | NodeRole.NavigationSolver });
        if (withBrain)
            cache.UpdateNode(new NodeCapability { NodeId = Cgf, Role = NodeRole.Brain });
        cache.UpdateNode(new NodeCapability { NodeId = Ig, Role = NodeRole.Map2D });
        return new RoleGroupOwnershipStrategy(cache, HrotOwnershipGroups.Table, NedOwnershipGroupBinding.GroupDescriptors);
    }

    private static TkbTemplate Tank()
    {
        var t = new TkbTemplate("tank", 3);
        t.AddDescriptor(new BehaviorProfileDto { BrainTier = 1 });
        t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = 1000f });
        t.AddDescriptor(new VehicleParametersDto());
        return t;
    }

    private static long[] To(System.Collections.Generic.IReadOnlyList<Fdp.Toolkit.NetworkSpawning.Events.DescriptorGrant> g, int node)
        => g.Where(x => x.NodeId == node).Select(x => x.DescriptorTypeId).OrderBy(x => x).ToArray();

    /// <summary>⭐ CE-500: SimHost creates a tank ⇒ the brain group goes to CGF; SimHost keeps what it serves.</summary>
    [Fact]
    public void SimHostCreatesATank_TheBrainGroupGoesToTheBrainNode()
    {
        var grants = Strategy().GetInitialGrants(new GrantRequest(default, Tank(), SimHost));

        Assert.Equal(Brain.OrderBy(x => x), To(grants, Cgf));
        Assert.Empty(To(grants, SimHost));   // G-5: Muscle and Perception are served by the creator itself
        Assert.Equal(Brain.Length, grants.Count);
    }

    [Fact]
    public void IgCreatesATank_EachGroupGoesToItsRole()
    {
        var grants = Strategy().GetInitialGrants(new GrantRequest(default, Tank(), Ig));

        Assert.Equal(Brain.OrderBy(x => x), To(grants, Cgf));
        Assert.Equal(Muscle.Concat(Perception).OrderBy(x => x), To(grants, SimHost));
    }

    [Fact]
    public void CgfCreatesATank_KeepsTheBrain_GrantsKinematicsAndPerception()
    {
        var grants = Strategy().GetInitialGrants(new GrantRequest(default, Tank(), Cgf));

        Assert.Empty(To(grants, Cgf));
        Assert.Equal(Muscle.Concat(Perception).OrderBy(x => x), To(grants, SimHost));
    }

    /// <summary>⭐ R-175: no kinematics, no brain ⇒ no group applies; the creator owns it, position included.</summary>
    [Fact]
    public void ATacticalSymbol_GrantsNothing()
        => Assert.Empty(Strategy().GetInitialGrants(new GrantRequest(default, new TkbTemplate("area", 9), Cgf)));

    /// <summary>⭐ R-175: a composite unit has a brain but no kinematics ⇒ only the brain moves; the position stays.</summary>
    [Fact]
    public void ACompositeUnit_GrantsTheBrainOnly()
    {
        var platoon = new TkbTemplate("platoon", 10);
        platoon.AddDescriptor(new BehaviorProfileDto { BrainTier = 1 });

        var grants = Strategy().GetInitialGrants(new GrantRequest(default, platoon, Ig));

        Assert.Equal(Brain.OrderBy(x => x), To(grants, Cgf));
        Assert.DoesNotContain(grants, g => g.DescriptorTypeId == D(EDescriptorType.dtWorldPos));
    }

    [Fact]
    public void ABrainlessVehicle_GrantsNoBrainGroup()
    {
        var truck = new TkbTemplate("truck", 11);
        truck.AddDescriptor(new BehaviorProfileDto { BrainTier = 0 });
        truck.AddDescriptor(new VehicleParametersDto());

        var grants = Strategy().GetInitialGrants(new GrantRequest(default, truck, Ig));

        Assert.Empty(To(grants, Cgf));
        Assert.Equal(Muscle.OrderBy(x => x), To(grants, SimHost));
    }

    /// <summary>G-5: with no node serving a role, the creator keeps that group.</summary>
    [Fact]
    public void NoNodeServesTheRole_TheCreatorKeepsTheGroup()
    {
        var grants = Strategy(withBrain: false).GetInitialGrants(new GrantRequest(default, Tank(), SimHost));
        Assert.Empty(grants);
    }

    [Fact]
    public void NoTemplate_GrantsNothing()
        => Assert.Empty(Strategy().GetInitialGrants(new GrantRequest(default, null, Ig)));
}
