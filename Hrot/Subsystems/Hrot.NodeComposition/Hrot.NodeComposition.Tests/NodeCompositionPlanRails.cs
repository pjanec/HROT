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

    /// <summary>A resource key no production provider uses, so the rails cannot collide with one.</summary>
    private const string FakeResourceKey = "res:fake-for-rails";

    private sealed class FakeProvider : INodeResourceProvider
    {
        /// <summary>What <see cref="Allocate"/> publishes, so a rail can prove the value round-trips.</summary>
        public const string Payload = "allocated-by-the-provider";

        public FakeProvider(string key) => Key = key;

        public string Key { get; }
        public int Disposals { get; private set; }
        public int Allocations { get; private set; }

        /// <summary>
        /// ⭐ CE-197 — this PUBLISHES now. It used to be an empty body, which mirrored production
        /// faithfully only because production never called it: the seam's resource half was declared and
        /// verified but never executed. Mirroring <c>TrajectoryPoolProvider.Allocate</c>'s one line
        /// (<c>values.Set(Key, …)</c>) is what lets a rail assert the round trip.
        /// </summary>
        public void Allocate(HrotNodeContext context, NodeBootValues values)
        {
            Allocations++;
            values.Set(Key, Payload);
        }

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

    /// <summary>
    /// <b>Resolution order is DECLARATION order — and that is behaviour, not cosmetics.</b>
    ///
    /// <para><c>ModuleHostKernel.RegisterModule</c> appends to a plain <c>List</c> which the frame loop
    /// iterates in sequence, so the order capabilities register in <b>is</b> the order their modules
    /// tick in. A host switching from a hand-written registration block to a resolved capability set is
    /// therefore only behaviour-preserving if the resolved sequence reproduces the old one exactly —
    /// which is why <c>SimHostCapabilities</c> splits perception into two capabilities rather than
    /// collapsing them and quietly moving the navigation module later.</para>
    ///
    /// <para>⚠ Without this rail the resolver could switch to any set-like ordering and every existing
    /// suite would stay green, because none of them observe module tick order.</para>
    /// </summary>
    [Fact]
    public void ResolutionPreservesDeclarationOrderAcrossRoles()
    {
        var a = new FakeCapability("cap:a");
        var b = new FakeCapability("cap:b");
        var c = new FakeCapability("cap:c");
        var d = new FakeCapability("cap:d");

        // Interleaved exactly the way SimHost's registration sequence interleaves perception
        // around navigation.
        var plan = new NodeCompositionPlan()
            .Capability(NodeRole.MuscleGround,     a)
            .Capability(NodeRole.Perception,       b)
            .Capability(NodeRole.NavigationSolver, c)
            .Capability(NodeRole.Perception,       d);

        Assert.Equal(
            new[] { "cap:a", "cap:b", "cap:c", "cap:d" },
            plan.Resolve(NodeRole.MuscleGround | NodeRole.Perception | NodeRole.NavigationSolver)
                .Select(x => x.Key));
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
    // ── CE-197: the resource half must actually RUN ──────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>CE-197 — <c>Allocate</c> CANNOT be driven from a free-standing bag, and this rail exists
    /// because that mistake was actually made.</b>
    ///
    /// <para>The resource half of the seam looks like it only needs wiring: no production code path
    /// calls <see cref="INodeResourceProvider.Allocate"/> at all (measured: zero call sites repo-wide),
    /// so the obvious "fix" is to resolve the providers and allocate them into a fresh
    /// <see cref="NodeBootValues"/>. 📐 That THROWS: the bag refuses any write that is not inside a boot
    /// step which declared the key in its <c>provides</c> — <i>"Boot step '(outside any step)' set
    /// 'res:…', which it does not declare in its provides []."</i></para>
    ///
    /// <para>⇒ ⭐ Allocation is blocked on a host moving its capability registration INSIDE a
    /// <c>NodeBootPlan</c> step that declares the resource keys — which is exactly what
    /// <c>SimHostNodeBootstrapper</c>'s own "the values bag is EMPTY" note already said. This rail turns
    /// that note into something that fails a build instead of being re-litigated. ⛔ Do not make it pass
    /// by relaxing the guard: the guard is what keeps the declared dependency graph the real one.</para>
    /// </summary>
    [Fact]
    public void AllocatingIntoAFreeStandingBagIsRefused()
    {
        var provider   = new FakeProvider(FakeResourceKey);
        var capability = new FakeCapability("cap:reader", FakeResourceKey);

        var plan = new NodeCompositionPlan()
            .Provider(provider)
            .Capability(NodeRole.MuscleGround, capability);

        IReadOnlyList<INodeResourceProvider> required = plan.RequiredResources(NodeRole.MuscleGround);
        Assert.Single(required);
        Assert.Equal(0, provider.Allocations);   // resolution alone must never allocate

        // The whole point: resolving is legal, allocating outside a declaring boot step is not.
        var ex = Assert.Throws<InvalidOperationException>(
            () => required[0].Allocate(context: null!, new NodeBootValues()));

        Assert.Contains("does not declare in its provides", ex.Message);
    }

    /// <summary>
    /// ⭐⭐ <b>CE-197 — a role whose capabilities declare no Needs selects NO providers.</b>
    /// That minimality is what makes IG's loud refusal correct rather than merely convenient: IG resolves
    /// lazily with no context, and that is safe only for as long as this stays empty.
    /// </summary>
    [Fact]
    public void ARoleThatNeedsNothingSelectsNoProviders()
    {
        var provider = new FakeProvider(FakeResourceKey);

        var plan = new NodeCompositionPlan()
            .Provider(provider)
            .Capability(NodeRole.ImageGenerator, new FakeCapability("cap:presentation"));

        Assert.Empty(plan.RequiredResources(NodeRole.ImageGenerator));
        Assert.Equal(0, provider.Allocations);
    }

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
