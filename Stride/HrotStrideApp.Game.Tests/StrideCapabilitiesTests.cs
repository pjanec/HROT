#nullable enable
using System.Collections.Generic;
using System.Linq;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Stride.Core;
using HrotStrideApp;
using Xunit;
using Fdp.Core;

namespace HrotStrideApp.Game.Tests;

/// <summary>
/// Rails for <see cref="StrideCapabilities"/> — host (e) of the capability seam (<c>CE-205</c>,
/// <c>CE-206</c>).
///
/// <para>
/// These assert the <b>resolved</b> set rather than the declaration, because the failure this seam
/// exists to prevent is a capability that is declared and resolves to nothing. The programme has hit
/// that shape repeatedly: Stride's <c>Perception</c> flag was declared with an empty constructor slot
/// at every call site, and BDC's dead reckoning was registered against components nothing wrote. A
/// test that only checked the declaration would have passed in both cases.
/// </para>
/// </summary>
public class StrideCapabilitiesTests
{
    private static StrideMuscleModuleSet BuildMuscleSet()
        => StrideMuscleModules.Build(new DotRecastDtCrowdProvider(maxAgentRadius: 0.4f));

    /// <summary>
    /// The declared role must resolve to a NON-EMPTY set. An empty resolution is the exact silent
    /// failure the seam is for: the node claims a role and composes nothing.
    /// </summary>
    [Fact]
    public void DefaultRole_ResolvesToANonEmptySet()
    {
        IReadOnlyList<INodeCapability> resolved =
            StrideCapabilities.Build(BuildMuscleSet()).Resolve(StrideCapabilities.DefaultRole);

        Assert.NotEmpty(resolved);
    }

    /// <summary>
    /// <c>CE-206</c> — perception must actually be filled. Stride declared <c>Perception</c> while its
    /// perception module slot was null at every call site, and the gap was masked by CGF's brain
    /// sharing the world. In mode 2 that cover is gone.
    /// </summary>
    [Fact]
    public void PerceptionIsFilled_NotJustDeclared()
    {
        IReadOnlyList<INodeCapability> resolved =
            StrideCapabilities.Build(BuildMuscleSet()).Resolve(StrideCapabilities.DefaultRole);

        Assert.Contains(resolved, c => c.Key == CapabilityKeys.Perception);
        Assert.Contains(resolved, c => c.Key == CapabilityKeys.Perception + ":spatial");
    }

    /// <summary>The muscle tier resolves too — the half that already worked, kept honest.</summary>
    [Fact]
    public void MuscleGroundResolves()
    {
        IReadOnlyList<INodeCapability> resolved =
            StrideCapabilities.Build(BuildMuscleSet()).Resolve(StrideCapabilities.DefaultRole);

        Assert.Contains(resolved, c => c.Key == CapabilityKeys.MuscleGround);
    }

    /// <summary>
    /// Every declared <c>Needs</c> key must be one the node can actually provide. A need nothing
    /// provides fails resolution at boot, which is a worse way to find out than a test.
    /// </summary>
    [Fact]
    public void NoCapabilityDeclaresANeedNothingProvides()
    {
        IReadOnlyList<INodeCapability> resolved =
            StrideCapabilities.Build(BuildMuscleSet()).Resolve(StrideCapabilities.DefaultRole);

        Assert.All(resolved, c => Assert.Empty(c.Needs));
    }

    /// <summary>
    /// <c>ImageGenerator</c> must NOT resolve to anything on Stride. Dropping the flag is only safe
    /// because <c>CE-211</c> made dead reckoning unconditional; if a capability were ever keyed to it
    /// here, that would quietly reintroduce the coupling the design removed.
    /// </summary>
    [Fact]
    public void ImageGeneratorContributesNothing()
    {
        IReadOnlyList<INodeCapability> resolved =
            StrideCapabilities.Build(BuildMuscleSet()).Resolve(NodeRole.ImageGenerator);

        Assert.Empty(resolved);
    }
}
