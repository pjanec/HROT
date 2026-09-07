#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;   // ⚠ NOT Fdp.Network.Cyclone.Services — two types share this name
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Hrot.CGF;
using Hrot.Core.Network;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Editor;
using Hrot.SimHost;
using Hrot.SimHost.Modules;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// Rails for <see cref="EditorCapabilities"/> — host (d) on the capability axis
/// (<c>DESIGN_Subsystem_Composition_Unification.md</c> §4.1ac).
///
/// <para>
/// <b>The load-bearing test here is the DIFFERENTIAL one.</b> Everything else is a sanity check. The
/// risk in putting a live composition root on the seam is not that the declaration is wrong — it is
/// that the resolved set differs from the hand-written block in some way nobody notices, because
/// registration order is execution order and a reordered system list fails silently. So the rail
/// builds <b>both</b> paths from the same pack instances and asserts the resulting system sequences
/// are identical, type for type, position for position. That is the precondition for switching the
/// composition over; without it the switch would be a hope.
/// </para>
///
/// <para>
/// ⚠ This mirrors what <c>B4b</c> step 3 did for SimHost, and it exists because that step found the
/// opposite of what was assumed: SimHost declared <c>MuscleGround|Perception</c> while composing
/// <c>NavigationSolver</c> too, so narrowing to the declaration would have silently dropped two
/// modules (§4.1v).
/// </para>
/// </summary>
public class EditorCapabilitiesTests : IDisposable
{
    /// <summary>
    /// Everything these rails construct, disposed when the test ends.
    ///
    /// <para>
    /// ⚠ <b>Not hygiene theatre — measured.</b> <c>SimHostCoreLogicPack</c> is <c>IDisposable</c> and
    /// holds native-backed pools. Leaving five of them alive for the rest of the run made
    /// <c>AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected</c> — which asserts an
    /// <c>AssemblyLoadContext</c> actually gets collected — fail deterministically in the full suite
    /// while passing in isolation. That is a real interaction caused by these tests, not a
    /// pre-existing flake, and disposing is the honest fix rather than quarantining someone else's rail.
    /// </para>
    /// </summary>
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        for (int i = _disposables.Count - 1; i >= 0; i--)
        {
            try { _disposables[i].Dispose(); } catch { /* a rail must not fail in teardown */ }
        }
        _disposables.Clear();
    }

    private (CgfLogicPack cgf, SimHostCoreLogicPack muscle, CognitiveSpatialModule perception)
        BuildPacks(EntityRepository world)
    {
        var entityMap  = new NetworkEntityMap();
        var mapper     = new TacticalIntentMapperRegistry();
        var cgf        = new CgfLogicPack(new Fdp.Toolkit.Behavior.BehaviorRegistry(), entityMap, new ScenarioEntityCreationRequestSource(), mapper);
        var muscle     = new SimHostCoreLogicPack(entityMap);
        var perception = new CognitiveSpatialModule(world, colliderRadiusReader: static (_, _) => 0f);
        Track(cgf); Track(muscle); Track(perception);
        return (cgf, muscle, perception);
    }

    private void Track(object o)
    {
        if (o is IDisposable d) _disposables.Add(d);
    }

    /// <summary>
    /// ⭐ THE ONE THAT MATTERS. The resolved capability set must produce the same three system
    /// sequences the hand-written block produces — same types, same order, after the same
    /// de-duplication. If this passes, switching <c>EditorSubsystem</c> onto the plan is
    /// behaviour-preserving by construction rather than by inspection.
    /// </summary>
    [Fact]
    public void ResolvedSet_ProducesTheSameSystemSequencesAsTheHandWrittenBlock()
    {
        using var world = new EntityRepository();
        var (cgf, muscle, perception) = BuildPacks(world);

        // ── The hand-written block, as EditorSubsystem builds it today ──────────────
        //    Note the argument order: CGF first, muscle second. DistinctByType keeps the FIRST
        //    occurrence, so a type carried by both packs resolves to CGF's instance.
        Type[] handInput = Fdp.ModuleHost.Scheduling.SystemComposition
            .DistinctByType(cgf.InputSystems, muscle.InputSystems).Select(s => s.GetType()).ToArray();
        Type[] handSim = Fdp.ModuleHost.Scheduling.SystemComposition
            .DistinctByType(cgf.SimulationSystems, muscle.SimulationSystems).Select(s => s.GetType()).ToArray();
        Type[] handPostSim = muscle.PostSimulationSystems.Select(s => s.GetType()).ToArray();

        // ── The same thing, resolved from the plan ─────────────────────────────────
        IReadOnlyList<INodeCapability> resolved =
            EditorCapabilities.BuildDefault(cgf, muscle, perception)
                              .Resolve(EditorCapabilities.DefaultRole);

        var input = new List<IEcsModuleSystem>();
        var sim = new List<IEcsModuleSystem>();
        var postSim = new List<IEcsModuleSystem>();
        foreach (INodeCapability c in resolved)
            c.PopulateSystems(context: null!, input, sim, postSim);

        Type[] planInput = Fdp.ModuleHost.Scheduling.SystemComposition
            .DistinctByType(input, Array.Empty<IEcsModuleSystem>()).Select(s => s.GetType()).ToArray();
        Type[] planSim = Fdp.ModuleHost.Scheduling.SystemComposition
            .DistinctByType(sim, Array.Empty<IEcsModuleSystem>()).Select(s => s.GetType()).ToArray();
        Type[] planPostSim = postSim.Select(s => s.GetType()).ToArray();

        Assert.Equal(handInput, planInput);
        Assert.Equal(handSim, planSim);
        Assert.Equal(handPostSim, planPostSim);
    }

    /// <summary>
    /// The Brain must come first in the plan. Both packs carry <c>UnitHierarchySystem</c> and
    /// <c>EqsResultUpdateSystem</c>; de-duplication keeps the first, so reversing the two capabilities
    /// would silently swap which pack's instance runs — a change no count-based assertion would see.
    /// </summary>
    [Fact]
    public void BrainResolvesBeforeMuscleGround()
    {
        using var world = new EntityRepository();
        var (cgf, muscle, perception) = BuildPacks(world);

        IReadOnlyList<INodeCapability> resolved =
            EditorCapabilities.BuildDefault(cgf, muscle, perception)
                              .Resolve(EditorCapabilities.DefaultRole);

        int brain  = resolved.ToList().FindIndex(c => c.Key == CapabilityKeys.Brain);
        int muscleI = resolved.ToList().FindIndex(c => c.Key == CapabilityKeys.MuscleGround);

        Assert.True(brain >= 0, "the Brain capability must resolve");
        Assert.True(muscleI >= 0, "the MuscleGround capability must resolve");
        Assert.True(brain < muscleI, "Brain must resolve before MuscleGround — DistinctByType keeps the first");
    }

    /// <summary>The declared role must resolve to something. An empty resolution is the silent failure.</summary>
    [Fact]
    public void DefaultRole_ResolvesToANonEmptySet()
    {
        using var world = new EntityRepository();
        var (cgf, muscle, perception) = BuildPacks(world);

        Assert.NotEmpty(EditorCapabilities.BuildDefault(cgf, muscle, perception)
                                          .Resolve(EditorCapabilities.DefaultRole));
    }

    /// <summary>
    /// The injected arm must carry the Brain and the area queries but contribute NO muscle systems —
    /// the supplying host owns that tier. A capability that quietly contributed SimHost's systems here
    /// would double the muscle on a Stride node.
    /// </summary>
    [Fact]
    public void InjectedArm_ContributesNoMuscleSystemsOfItsOwn()
    {
        using var world = new EntityRepository();
        var (cgf, _, _) = BuildPacks(world);

        IReadOnlyList<INodeCapability> resolved =
            EditorCapabilities.BuildWithInjectedMuscle(cgf, Array.Empty<IEcsModule>())
                              .Resolve(EditorCapabilities.DefaultRole);

        var input = new List<IEcsModuleSystem>();
        var sim = new List<IEcsModuleSystem>();
        var postSim = new List<IEcsModuleSystem>();
        foreach (INodeCapability c in resolved)
            c.PopulateSystems(context: null!, input, sim, postSim);

        // Everything contributed as SYSTEMS on this arm comes from the Brain pack.
        Assert.Equal(cgf.InputSystems.Select(s => s.GetType()), input.Select(s => s.GetType()));
        Assert.Equal(cgf.SimulationSystems.Select(s => s.GetType()), sim.Select(s => s.GetType()));
        // CgfLogicPack contributes no post-simulation systems, and on this arm nothing else does
        // either — the supplying host owns that tier.
        Assert.Empty(postSim);
    }

    /// <summary>
    /// 🔒 The user's role ruling, asserted so a later edit cannot quietly narrow it: CGF ∪ SimHost,
    /// and NOT ImageGenerator (the editor's 2-D map is not the IG presentation tier).
    /// </summary>
    [Fact]
    public void DeclaredRoleIsCgfUnionSimHost_AndNotImageGenerator()
    {
        Assert.True(EditorCapabilities.DefaultRole.HasFlag(NodeRole.Brain));
        Assert.True(EditorCapabilities.DefaultRole.HasFlag(NodeRole.MuscleGround));
        Assert.True(EditorCapabilities.DefaultRole.HasFlag(NodeRole.Perception));
        Assert.True(EditorCapabilities.DefaultRole.HasFlag(NodeRole.NavigationSolver));
        Assert.False(EditorCapabilities.DefaultRole.HasFlag(NodeRole.ImageGenerator));
    }
}
