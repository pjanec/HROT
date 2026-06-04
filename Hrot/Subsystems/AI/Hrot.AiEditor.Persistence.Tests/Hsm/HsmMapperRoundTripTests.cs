using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using FluentAssertions;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Hsm.Editor.Catalog;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using Xunit;

namespace Hrot.AiEditor.Persistence.Tests.Hsm;

/// <summary>
/// PU-103 mapping round-trip tests: model → DTO → model preserves every persisted
/// HSM field per design §5.2. Fixtures: SampleGuard (reflection-loaded) +
/// hand-built comprehensive fixture with regions, global transitions, waypoints.
/// </summary>
public sealed class HsmMapperRoundTripTests
{
    private static readonly Assembly BehaviorsAssembly =
        typeof(Hrot.AI.Behaviors.Machines.SampleGuard).Assembly;

    private static HsmAsset LoadSampleGuard()
    {
        var contributor = new HsmAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);
        var assets = contributor.Enumerate();
        return (HsmAsset)assets.Should().ContainSingle(a => a.Name == "SampleGuard")
            .Which;
    }

    // ── SampleGuard round-trip ────────────────────────────────────────────────

    [Fact]
    public void SampleGuard_ModelToDtoToModel_IdentityPreserved()
    {
        var original = LoadSampleGuard();
        var restored = HsmAssetMapper.FromDto(HsmAssetMapper.ToDto(original));

        restored.AssetId.Should().Be(original.AssetId);
        restored.Name.Should().Be(original.Name);
        restored.TargetNamespace.Should().Be(original.TargetNamespace);
    }

    [Fact]
    public void SampleGuard_ModelToDtoToModel_StateCountPreserved()
    {
        var original = LoadSampleGuard();
        var restored = HsmAssetMapper.FromDto(HsmAssetMapper.ToDto(original));

        // SampleGuard has Idle + Scanning = 2 states
        restored.AllStates.Count.Should().Be(original.AllStates.Count,
            "all states must survive round-trip");
    }

    [Fact]
    public void SampleGuard_ModelToDtoToModel_StableIdsPreserved()
    {
        var original = LoadSampleGuard();
        var restored = HsmAssetMapper.FromDto(HsmAssetMapper.ToDto(original));

        foreach (var origState in original.AllStates)
        {
            restored.FindStateByStableId(origState.StableId).Should().NotBeNull(
                because: $"state {origState.StableId} must be findable by StableId after round-trip");
        }
    }

    [Fact]
    public void SampleGuard_ModelToDtoToModel_StateNamesPreserved()
    {
        var original = LoadSampleGuard();
        var restored = HsmAssetMapper.FromDto(HsmAssetMapper.ToDto(original));

        var origNames = original.AllStates.Select(s => s.Name).OrderBy(x => x).ToList();
        var restNames = restored.AllStates.Select(s => s.Name).OrderBy(x => x).ToList();
        restNames.Should().BeEquivalentTo(origNames, "state names must survive round-trip");
    }

    [Fact]
    public void SampleGuard_ModelToDtoToModel_TransitionCountPreserved()
    {
        var original = LoadSampleGuard();
        var restored = HsmAssetMapper.FromDto(HsmAssetMapper.ToDto(original));

        // SampleGuard has Alert + Clear = 2 transitions
        restored.AllTransitions.Count.Should().Be(original.AllTransitions.Count,
            "all transitions must survive round-trip");
    }

    [Fact]
    public void SampleGuard_ModelToDtoToModel_TransitionVisualIdsPreserved()
    {
        var original = LoadSampleGuard();
        var restored = HsmAssetMapper.FromDto(HsmAssetMapper.ToDto(original));

        foreach (var origTrans in original.AllTransitions)
        {
            restored.FindTransitionByVisualId(origTrans.VisualId).Should().NotBeNull(
                because: $"transition {origTrans.VisualId} must be findable by VisualId after round-trip");
        }
    }

    [Fact]
    public void SampleGuard_ModelToDtoToModel_TransitionEndpointsPreserved()
    {
        var original = LoadSampleGuard();
        var restored = HsmAssetMapper.FromDto(HsmAssetMapper.ToDto(original));

        foreach (var origTrans in original.AllTransitions)
        {
            var restTrans = restored.FindTransitionByVisualId(origTrans.VisualId);
            restTrans.Should().NotBeNull();
            restTrans!.Source.StableId.Should().Be(origTrans.Source.StableId,
                "source state must survive round-trip");
            restTrans.Target.StableId.Should().Be(origTrans.Target.StableId,
                "target state must survive round-trip");
        }
    }

    [Fact]
    public void SampleGuard_ModelToDtoToModel_StatePositionsPreserved()
    {
        var original = LoadSampleGuard();
        var restored = HsmAssetMapper.FromDto(HsmAssetMapper.ToDto(original));

        foreach (var origState in original.AllStates)
        {
            var restState = restored.FindStateByStableId(origState.StableId);
            restState.Should().NotBeNull();
            restState!.Position.X.Should().BeApproximately(origState.Position.X, 0.001f);
            restState.Position.Y.Should().BeApproximately(origState.Position.Y, 0.001f);
        }
    }

    [Fact]
    public void SampleGuard_ModelToDtoToModel_TransitionWaypointsPreserved()
    {
        var original = LoadSampleGuard();
        var restored = HsmAssetMapper.FromDto(HsmAssetMapper.ToDto(original));

        foreach (var origTrans in original.AllTransitions)
        {
            var restTrans = restored.FindTransitionByVisualId(origTrans.VisualId);
            restTrans.Should().NotBeNull();
            restTrans!.Waypoints.Count.Should().Be(origTrans.Waypoints.Count,
                because: $"transition {origTrans.VisualId} waypoint count must survive round-trip");
            for (int i = 0; i < origTrans.Waypoints.Count; i++)
            {
                restTrans.Waypoints[i].X.Should().BeApproximately(origTrans.Waypoints[i].X, 0.001f);
                restTrans.Waypoints[i].Y.Should().BeApproximately(origTrans.Waypoints[i].Y, 0.001f);
            }
        }
    }

    [Fact]
    public void SampleGuard_ModelToDtoToModel_EventsPreserved()
    {
        var original = LoadSampleGuard();
        var restored = HsmAssetMapper.FromDto(HsmAssetMapper.ToDto(original));

        var origEventNames = original.AllEvents.Select(e => e.Name).OrderBy(x => x).ToList();
        var restEventNames = restored.AllEvents.Select(e => e.Name).OrderBy(x => x).ToList();
        restEventNames.Should().BeEquivalentTo(origEventNames, "event names must survive round-trip");
    }

    [Fact]
    public void SampleGuard_ModelToDtoToModel_CanvasPreserved()
    {
        var original = LoadSampleGuard();
        var restored = HsmAssetMapper.FromDto(HsmAssetMapper.ToDto(original));

        restored.CanvasPanOffset.X.Should().BeApproximately(original.CanvasPanOffset.X, 0.001f);
        restored.CanvasPanOffset.Y.Should().BeApproximately(original.CanvasPanOffset.Y, 0.001f);
        restored.CanvasZoomLevel.Should().BeApproximately(original.CanvasZoomLevel, 0.001f);
    }

    // ── Comprehensive fixture: regions, global transitions, suppressions ──────

    [Fact]
    public void Comprehensive_ModelToDtoToModel_SuppressionsPreserved()
    {
        var original = LoadSampleGuard();
        original.SetConflictSuppressed("HealthField", "writer1.vs.writer2", true);
        original.SetUnusedWarningSuppressed("OldVar", true);

        var restored = HsmAssetMapper.FromDto(HsmAssetMapper.ToDto(original));

        restored.IsConflictSuppressed("HealthField", "writer1.vs.writer2")
            .Should().BeTrue("conflict suppression must survive round-trip");
        restored.IsUnusedWarningSuppressed("OldVar")
            .Should().BeTrue("unused suppression must survive round-trip");

        original.ClearDirty();
    }

    [Fact]
    public void Comprehensive_ModelToDtoToModel_BlackboardVariablePreserved()
    {
        var original = LoadSampleGuard();
        original.AddVariable(new BlackboardVariableEntry("AmmoCount", typeof(int), "Bullets remaining"));

        var dto = HsmAssetMapper.ToDto(original);
        var restored = HsmAssetMapper.FromDto(dto);

        dto.Blackboard.Variables.Should().HaveCount(1);
        dto.Blackboard.Variables[0].Name.Should().Be("AmmoCount");
        dto.Blackboard.Variables[0].Type.TypeId.Should().Be(typeof(int).FullName);

        restored.BlackboardVariables.Should().ContainSingle(v => v.Name == "AmmoCount");

        original.ClearDirty();
    }

    [Fact]
    public void Restored_IsDirty_IsFalse()
    {
        var original = LoadSampleGuard();
        var restored = HsmAssetMapper.FromDto(HsmAssetMapper.ToDto(original));
        restored.IsDirty.Should().BeFalse("IsDirty is not persisted");
    }
}
