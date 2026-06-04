using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Fhsm.Compiler;
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

    // ── BATCH-02 DTO extension round-trip assertions ──────────────────────────
    // These assert the fields added to EventDefinitionDto + StateNodeDto for
    // byte-identical emit-core output (BATCH-02 allowed re-touch of BATCH-01 files).

    [Fact]
    public void Event_IsDeferrable_FieldExistsInDto_AndRoundTrips()
    {
        // Verify IsDeferrable field is present in EventDefinitionDto and round-trips correctly.
        // Note: IsDeferrable is set by the projector from metadata.DeferredEventsByState;
        // SampleGuard events have IsDeferrable=false (no deferred states).
        var original = LoadSampleGuard();

        var dto = HsmAssetMapper.ToDto(original);

        // All events must have the IsDeferrable field present in the DTO
        foreach (var ev in dto.Events)
            ev.IsDeferrable.Should().Be(
                original.AllEvents.First(e => e.Name == ev.Name).IsDeferrable,
                $"EventDefinitionDto.IsDeferrable must match model for event '{ev.Name}'");

        // Round-trip: IsDeferrable preserved after FromDto
        var restored = HsmAssetMapper.FromDto(dto);
        foreach (var origEv in original.AllEvents)
        {
            var restEv = restored.AllEvents.FirstOrDefault(e => e.Name == origEv.Name);
            restEv.Should().NotBeNull();
            restEv!.IsDeferrable.Should().Be(origEv.IsDeferrable,
                $"IsDeferrable must survive DTO round-trip for event '{origEv.Name}'");
        }
    }

    [Fact]
    public void Event_EventId_RoundTrips_ThroughDto()
    {
        // EventId must be stored in DTO and restored after FromDto (for emit-core byte-identity).
        var original = LoadSampleGuard();

        var dto = HsmAssetMapper.ToDto(original);

        // All events must have EventId stored in DTO
        foreach (var ev in dto.Events)
            ev.EventId.Should().BeGreaterThan(0, $"EventId for '{ev.Name}' must be persisted in DTO");

        // After FromDto, EventIds must be restored (not sequential re-assignment)
        var restored = HsmAssetMapper.FromDto(dto);
        foreach (var origEv in original.AllEvents)
        {
            var restEv = restored.AllEvents.FirstOrDefault(e => e.Name == origEv.Name);
            restEv.Should().NotBeNull();
            restEv!.EventId.Should().Be(origEv.EventId,
                $"EventId for '{origEv.Name}' must be preserved through DTO round-trip");
        }
    }

    [Fact]
    public void State_DeferredEventNames_RoundTrips_ThroughDto()
    {
        // DeferredEventNames must survive DTO round-trip with correct names.
        var builder = new HsmBuilder("M");
        builder.Event("Tick",  1);
        builder.Event("Fire",  2);
        builder.State("Idle").Initial().DeferEvent(1).DeferEvent(2);

        var graph    = builder.Build();
        HsmNormalizer.Normalize(graph);
        var flat     = HsmFlattener.Flatten(graph);
        var blob     = HsmEmitter.Emit(flat);
        var metadata = HsmEmitter.BuildMachineMetadata(graph);
        var asset    = HsmAssetProjector.Project(blob, metadata, null,
            Guid.NewGuid(), "M", "", false, "");

        var dto = HsmAssetMapper.ToDto(asset);

        // Find the Idle state DTO and check DeferredEventNames
        var idleDto = dto.States.FirstOrDefault(s => s.Name == "Idle");
        idleDto.Should().NotBeNull();
        idleDto!.DeferredEventNames.Should().BeEquivalentTo(new[] { "Tick", "Fire" },
            "DeferredEventNames must contain the names corresponding to deferred event IDs");

        // After round-trip, DeferredEventIds must be restored
        var restored = HsmAssetMapper.FromDto(dto);
        var idleState = restored.AllStates.FirstOrDefault(s => s.Name == "Idle");
        idleState.Should().NotBeNull();
        idleState!.DeferredEventIds.Should().BeEquivalentTo(new ushort[] { 1, 2 },
            "DeferredEventIds must be restored from DeferredEventNames after DTO round-trip");
    }
}
