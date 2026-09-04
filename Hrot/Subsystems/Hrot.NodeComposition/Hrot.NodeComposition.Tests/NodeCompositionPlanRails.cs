using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Common.Infrastructure;
using Hrot.Common;
using Xunit;

namespace Hrot.NodeComposition.Tests;

/// <summary>
/// <b><c>B4a</c> — the role → capability seam.</b>
///
/// <para>These pin the two properties the whole composition programme rests on: a node's capability
/// set is the <b>union</b> over its role flags with <b>one</b> registration per capability, and a
/// resource is allocated because some selected capability <i>declared</i> it — never because a role
/// name mentions it.</para>
///
/// <para>⚠ Deliberately no rail here for "a capability cannot read a resource it did not declare":
/// that is <see cref="NodeBootValues"/>'s guarantee and <c>NodeBootPlanRails</c> already covers it.
/// Restating it here would be a second rail for one rule.</para>
/// </summary>
public sealed class NodeCompositionPlanRails
{
    // ── Doubles ───────────────────────────────────────────────────────────────

    private sealed class FakeCapability : INodeCapability
    {
        public FakeCapability(string key, params string[] needs)
        {
            Key   = key;
            Needs = needs;
        }

        public string Key { get; }
        public IReadOnlyList<string> Needs { get; }
        public int RegisterCalls { get; private set; }

        public void Register(HrotNodeContext context, NodeBootValues values) => RegisterCalls++;
    }

    private sealed class FakeProvider : INodeResourceProvider
    {
        public FakeProvider(string key) => Key = key;

        public string Key { get; }
        public int Disposals { get; private set; }

        public void Allocate(HrotNodeContext context, NodeBootValues values) { }
        public void Dispose() => Disposals++;
    }

    // ── The union, deduplicated ───────────────────────────────────────────────

    /// <summary>
    /// The case the whole split exists for: two roles that both select one capability must yield ONE
    /// of it. A second registration is the memory-owning double-registration hazard that
    /// <c>[SingleInstance]</c> catches at the system axis and <c>CE-177</c> hit at the registry axis.
    /// </summary>
    [Fact]
    public void ACapabilitySelectedByTwoRolesIsResolvedExactlyOnce()
    {
        var perception = new FakeCapability(CapabilityKeys.Perception);

        var plan = new NodeCompositionPlan()
            .Capability(NodeRole.Brain,        perception)
            .Capability(NodeRole.MuscleGround, perception);

        IReadOnlyList<INodeCapability> resolved = plan.Resolve(NodeRole.Brain | NodeRole.MuscleGround);

        Assert.Single(resolved);
        Assert.Same(perception, resolved[0]);
    }

    /// <summary>Two DIFFERENT capabilities sharing a key are still one — identity is the key.</summary>
    [Fact]
    public void TwoDistinctInstancesUnderOneKeyCollapseToTheFirst()
    {
        var first  = new FakeCapability(CapabilityKeys.MuscleGround);
        var second = new FakeCapability(CapabilityKeys.MuscleGround);

        var plan = new NodeCompositionPlan()
            .Capability(NodeRole.MuscleGround,     first)
            .Capability(NodeRole.NavigationSolver, second);

        IReadOnlyList<INodeCapability> resolved =
            plan.Resolve(NodeRole.MuscleGround | NodeRole.NavigationSolver);

        Assert.Single(resolved);
        Assert.Same(first, resolved[0]);   // first-wins, matching SystemComposition.DistinctByType
    }

    /// <summary>A role the node does not carry contributes nothing.</summary>
    [Fact]
    public void OnlyTheDeclaredRoleFlagsContribute()
    {
        var brain  = new FakeCapability(CapabilityKeys.Brain);
        var muscle = new FakeCapability(CapabilityKeys.MuscleGround);

        var plan = new NodeCompositionPlan()
            .Capability(NodeRole.Brain,        brain)
            .Capability(NodeRole.MuscleGround, muscle);

        Assert.Equal(new[] { CapabilityKeys.Brain }, plan.Resolve(NodeRole.Brain).Select(c => c.Key));
        Assert.Empty(plan.Resolve(NodeRole.None));
        Assert.Empty(plan.Resolve(NodeRole.ImageGenerator));
    }

    // ── Resources follow NEEDS, not role names ────────────────────────────────

    /// <summary>
    /// A resource is allocated because a selected capability declared it. A node whose capabilities
    /// need nothing allocates nothing — which is what makes a NavigationSolver-only node cheap
    /// instead of dragging the whole Muscle resource set behind it.
    /// </summary>
    [Fact]
    public void OnlyResourcesSomeSelectedCapabilityNeedsAreRequired()
    {
        var pool = new FakeProvider(ResourceKeys.TrajectoryPool);
        var grid = new FakeProvider(ResourceKeys.PerceptionGrid);

        var plan = new NodeCompositionPlan()
            .Provider(pool)
            .Provider(grid)
            .Capability(NodeRole.MuscleGround, new FakeCapability(CapabilityKeys.MuscleGround, ResourceKeys.TrajectoryPool))
            .Capability(NodeRole.Perception,   new FakeCapability(CapabilityKeys.Perception,   ResourceKeys.PerceptionGrid));

        Assert.Equal(
            new[] { ResourceKeys.TrajectoryPool },
            plan.RequiredResources(NodeRole.MuscleGround).Select(p => p.Key));

        Assert.Empty(plan.RequiredResources(NodeRole.ImageGenerator));
    }

    /// <summary>
    /// And the shared case: two capabilities needing one resource require it ONCE. This is
    /// <c>CE-180</c>'s hazard expressed at the composition layer — the solver and the kinematics side
    /// must end up holding the same trajectory pool, or routes resolve into memory nothing reads.
    /// </summary>
    [Fact]
    public void OneResourceNeededByTwoCapabilitiesIsRequiredOnce()
    {
        var pool = new FakeProvider(ResourceKeys.TrajectoryPool);

        var plan = new NodeCompositionPlan()
            .Provider(pool)
            .Capability(NodeRole.MuscleGround,     new FakeCapability(CapabilityKeys.MuscleGround,     ResourceKeys.TrajectoryPool))
            .Capability(NodeRole.NavigationSolver, new FakeCapability(CapabilityKeys.NavigationSolver, ResourceKeys.TrajectoryPool));

        IReadOnlyList<INodeResourceProvider> required =
            plan.RequiredResources(NodeRole.MuscleGround | NodeRole.NavigationSolver);

        Assert.Single(required);
        Assert.Same(pool, required[0]);
    }

    /// <summary>
    /// An undeclared need must FAIL the node, not default one. This is the composition-layer form of
    /// the silent-default pattern: a capability whose need nobody supplies is exactly the situation
    /// in which a module quietly allocates its own copy.
    /// </summary>
    [Fact]
    public void ANeedNoProviderSuppliesIsRefused()
    {
        var plan = new NodeCompositionPlan()
            .Capability(NodeRole.MuscleGround, new FakeCapability(CapabilityKeys.MuscleGround, ResourceKeys.TrajectoryPool));

        var ex = Assert.Throws<InvalidOperationException>(() => plan.RequiredResources(NodeRole.MuscleGround));

        Assert.Contains(ResourceKeys.TrajectoryPool, ex.Message);
        Assert.Contains(CapabilityKeys.MuscleGround, ex.Message);
    }

    /// <summary>A resource has exactly one owner; two providers for one key is a composition defect.</summary>
    [Fact]
    public void TwoProvidersForOneResourceAreRefused()
    {
        var plan = new NodeCompositionPlan().Provider(new FakeProvider(ResourceKeys.PerceptionGrid));

        var ex = Assert.Throws<InvalidOperationException>(
            () => plan.Provider(new FakeProvider(ResourceKeys.PerceptionGrid)));

        Assert.Contains(ResourceKeys.PerceptionGrid, ex.Message);
    }

    /// <summary>Declaring the same provider instance twice is idempotent, not an error.</summary>
    [Fact]
    public void TheSameProviderInstanceMayBeDeclaredTwice()
    {
        var grid = new FakeProvider(ResourceKeys.PerceptionGrid);
        var plan = new NodeCompositionPlan().Provider(grid).Provider(grid);

        plan.Capability(NodeRole.Perception, new FakeCapability(CapabilityKeys.Perception, ResourceKeys.PerceptionGrid));

        Assert.Single(plan.RequiredResources(NodeRole.Perception));
    }
}
