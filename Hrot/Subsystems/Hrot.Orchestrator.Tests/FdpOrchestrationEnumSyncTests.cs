using System;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// CMC-S001: Verifies that the FDP domain enums in <c>FDP.Toolkit.Orchestration</c>
/// have identical integer values to their <c>Hrot.NED.Descriptors.Orchestration</c> counterparts.
/// </summary>
public sealed class FdpOrchestrationEnumSyncTests
{
    [Fact]
    public void ClusterStateValuesMatchHrot()
    {
        foreach (Fdp.Toolkit.Orchestration.ClusterState fdpVal
                 in Enum.GetValues<Fdp.Toolkit.Orchestration.ClusterState>())
        {
            var nedVal = (Hrot.NED.Descriptors.Orchestration.ClusterState)(int)fdpVal;
            Assert.Equal(fdpVal.ToString(), nedVal.ToString());
        }
    }

    [Fact]
    public void ClusterOpTypeValuesMatchHrot()
    {
        foreach (Fdp.Toolkit.Orchestration.ClusterOpType fdpVal
                 in Enum.GetValues<Fdp.Toolkit.Orchestration.ClusterOpType>())
        {
            var nedVal = (Hrot.NED.Descriptors.Orchestration.ClusterOpType)(int)fdpVal;
            Assert.Equal(fdpVal.ToString(), nedVal.ToString());
        }
    }

    [Fact]
    public void NodeOpTypeValuesMatchHrot()
    {
        foreach (Fdp.Toolkit.Orchestration.NodeOpType fdpVal
                 in Enum.GetValues<Fdp.Toolkit.Orchestration.NodeOpType>())
        {
            var nedVal = (Hrot.NED.Descriptors.Orchestration.NodeOpType)(int)fdpVal;
            Assert.Equal(fdpVal.ToString(), nedVal.ToString());
        }
    }

    /// <summary>⭐ <c>CE-4093</c> — the check round's node op: ⛔ PERMANENT WIRE VALUE 35 (R-42) in BOTH enums, the next free value after
    /// <c>FinalizeReview = 34</c>. 🔴 Mutation: renumbering either side ⇒ this fails.</summary>
    [Fact]
    public void CheckUnsavedIsTheWireValue35InBothEnums()
    {
        Assert.Equal(35, (int)Fdp.Toolkit.Orchestration.NodeOpType.CheckUnsaved);
        Assert.Equal(35, (int)Hrot.NED.Descriptors.Orchestration.NodeOpType.CheckUnsaved);
    }

    // ⭐ CE-4071 (R-134: an internal enum + its wire twin is the INTENDED pattern) — the twins are pinned, not unified.
    // Both directions, by NAME and by NUMERIC VALUE. The only legitimate asymmetry is a wire-only member, listed here with its reason.
    private static readonly System.Collections.Generic.Dictionary<string, int> WireOnlyClusterOps = new()
    {
        // The live distributed JSON scenario save (CE-277(c0)/CE-278): routed at ClusterMaster, never an engine-toolkit op.
        ["SaveScenario"] = 17,
    };

    private static void AssertTwinsEqual<TEngine, TWire>(System.Collections.Generic.IReadOnlyDictionary<string, int> wireOnly)
        where TEngine : struct, Enum where TWire : struct, Enum
    {
        foreach (var name in Enum.GetNames<TEngine>())
        {
            Assert.True(Enum.IsDefined(typeof(TWire), name), $"{typeof(TWire).Name} has no member '{name}'");
            Assert.Equal(Convert.ToInt32(Enum.Parse<TEngine>(name)), Convert.ToInt32(Enum.Parse<TWire>(name)));
        }
        foreach (var name in Enum.GetNames<TWire>())
        {
            if (Enum.IsDefined(typeof(TEngine), name)) continue;
            Assert.True(wireOnly.TryGetValue(name, out var v), $"{typeof(TWire).Name}.{name} has no engine twin and is not a listed wire-only member");
            Assert.Equal(v, Convert.ToInt32(Enum.Parse<TWire>(name)));
        }
    }

    [Fact]
    public void ClusterOpTypeTwinsAgreeInBothDirectionsByNameAndValue()
        => AssertTwinsEqual<Fdp.Toolkit.Orchestration.ClusterOpType, Hrot.NED.Descriptors.Orchestration.ClusterOpType>(WireOnlyClusterOps);

    [Fact]
    public void NodeOpTypeTwinsAgreeInBothDirectionsByNameAndValue()
        => AssertTwinsEqual<Fdp.Toolkit.Orchestration.NodeOpType, Hrot.NED.Descriptors.Orchestration.NodeOpType>(
            new System.Collections.Generic.Dictionary<string, int>());
}
