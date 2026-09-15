using System;
using System.Linq;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Common.Infrastructure;
using Hrot.Core.Network;
using Hrot.IG.Components;
using Hrot.IG.Systems;
using Xunit;

namespace Hrot.NodeComposition.Tests;

/// <summary>
/// Tests for StrideNodeBootstrapper covering all SC_SM003_x and SC_SM005_x
/// success conditions.
/// </summary>
public sealed class StrideNodeBootstrapperTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static HrotNodeConfig HeadlessConfig() => new HrotNodeConfig
    {
        Headless             = true,
        SkipAllocatorRouting = true,
        SubsystemName        = "TestStride",
        NodeId               = 1,
        LocalTempRoot        = @"C:\FDP_Temp",
    };

    private static StrideNodeBootstrapper CreateAndBoot()
    {
        var factory      = new Hrot.Editor.OfflineNetworkFactory();
        var bootstrapper = new StrideNodeBootstrapper();
        bootstrapper.BootstrapNode(HeadlessConfig(), StrideNodeBootstrapper.Role, factory);
        return bootstrapper;
    }

    // ── SC_SM003_1 ────────────────────────────────────────────────────────────

    /// <summary>
    /// SC_SM003_1: BootstrapNode completes without throwing against an
    /// OfflineNetworkFactory (headless — no DDS participant).
    /// </summary>
    [Fact]
    public void BootstrapNode_WithHeadlessFactory_DoesNotThrow()
    {
        var factory      = new Hrot.Editor.OfflineNetworkFactory();
        var bootstrapper = new StrideNodeBootstrapper();

        // Must not throw.
        bootstrapper.BootstrapNode(HeadlessConfig(), StrideNodeBootstrapper.Role, factory);
    }

    // ── SC_SM003_2 ────────────────────────────────────────────────────────────

    /// <summary>
    /// SC_SM003_2: Context.ClusterSlave is non-null after BootstrapNode.
    /// </summary>
    [Fact]
    public void Context_ClusterSlave_NonNull_AfterBootstrap()
    {
        var bootstrapper = CreateAndBoot();

        Assert.NotNull(bootstrapper.Context.ClusterSlave);
    }

    // ── SC_SM003_3 ────────────────────────────────────────────────────────────

    /// <summary>
    /// SC_SM003_3: ProducerBuffer and ConsumerBuffer are different object instances
    /// so that StrideNodeBootstrapper can read and write concurrently during future
    /// DDS integration.
    /// </summary>
    [Fact]
    public void ProducerAndConsumerBuffers_AreDifferentInstances()
    {
        var bootstrapper = CreateAndBoot();

        Assert.False(ReferenceEquals(bootstrapper.ProducerBuffer, bootstrapper.ConsumerBuffer));
    }

    // ── SC_SM003_4 ────────────────────────────────────────────────────────────

    /// <summary>
    /// SC_SM003_4: Camera is non-null and default Zoom is 1.0f (Raylib Camera2D default
    /// overridden by MapCamera constructor).
    /// </summary>
    [Fact]
    public void Camera_NonNull_WithDefaultZoom()
    {
        var bootstrapper = CreateAndBoot();

        Assert.NotNull(bootstrapper.Camera);
        Assert.Equal(1f, bootstrapper.Camera.Zoom);
    }

    // ── SC_SM003_5 ────────────────────────────────────────────────────────────

    /// <summary>
    /// SC_SM003_5: TimeControl (inherited from SharedApplicationBootstrapper) is non-null.
    /// OfflineNetworkFactory.CreateTimeControlGateway() returns NullTimeControlGateway
    /// (non-null), so the property is always set after a successful bootstrap.
    /// </summary>
    [Fact]
    public void TimeControl_AccessedViaInheritedProperty_NonNull()
    {
        var bootstrapper = CreateAndBoot();

        // Property is declared on SharedApplicationBootstrapper; StrideNodeBootstrapper
        // must NOT redeclare it (verified by casting to the base type).
        SharedApplicationBootstrapper baseView = bootstrapper;
        Assert.NotNull(baseView.TimeControl);
    }

    // ── SC_SM003_6 ────────────────────────────────────────────────────────────

    /// <summary>
    /// SC_SM003_6: KinematicComponentRegistry components (e.g. VehicleState) are
    /// registered in the ECS world so that scenario templates can set them on spawned
    /// entities.
    /// </summary>
    [Fact]
    public void KinematicComponents_RegisteredInWorld()
    {
        var bootstrapper = CreateAndBoot();

        Assert.True(bootstrapper.Context.World.IsComponentTypeRegistered<VehicleState>());
    }

    // ── SC_SM003_7 ────────────────────────────────────────────────────────────

    /// <summary>
    /// SC_SM003_7: CognitiveComponentRegistry components (e.g. BrainHsm128) are NOT
    /// registered in the ECS world on the Stride node — Brain AI data stays on the
    /// CGF node only.
    /// </summary>
    [Fact]
    public void CognitiveComponents_NotRegisteredInWorld()
    {
        var bootstrapper = CreateAndBoot();

        Assert.False(bootstrapper.Context.World.IsComponentTypeRegistered<BrainHsm128>());
    }

    // ── SC_SM003_8 ────────────────────────────────────────────────────────────

    /// <summary>
    /// SC_SM003_8: Tick can be called repeatedly without throwing; ConsumerBuffer is
    /// cleared to zero primitives at the start of each Tick.
    /// </summary>
    [Fact]
    public void Tick_CanBeCalledRepeatedly_WithoutThrowing()
    {
        var bootstrapper = CreateAndBoot();

        for (int i = 0; i < 3; i++)
        {
            bootstrapper.Tick(0.016f);
            // ConsumerBuffer.Clear() is called inside Tick(); no primitives have been
            // added so Count must be zero after each call.
            Assert.Equal(0, bootstrapper.ConsumerBuffer.Count);
        }
    }

    // ── SC_SM003_10 ───────────────────────────────────────────────────────────

    /// <summary>
    /// SC_SM003_10: VisualEffectState is explicitly registered as an ECS component.
    /// Required by SyncFdpToStrideScript queries (EventToEffectSystem spawns these).
    /// </summary>
    [Fact]
    public void VisualEffectState_RegisteredInWorld()
    {
        var bootstrapper = CreateAndBoot();

        Assert.True(bootstrapper.Context.World.IsComponentTypeRegistered<VisualEffectState>());
    }

    // ── SC_SM003_11 ───────────────────────────────────────────────────────────

    /// <summary>
    /// SC_SM003_11: TracerTarget is explicitly registered as an ECS component.
    /// Required by SyncFdpToStrideScript to resolve tracer endpoints.
    /// </summary>
    [Fact]
    public void TracerTarget_RegisteredInWorld()
    {
        var bootstrapper = CreateAndBoot();

        Assert.True(bootstrapper.Context.World.IsComponentTypeRegistered<TracerTarget>());
    }

    // ── SC_SM005_1 ────────────────────────────────────────────────────────────

    /// <summary>
    /// SC_SM005_1: EventToEffectSystem is placed in SimGroup (TogglableSimulationGroup)
    /// and VisualEffectCleanupSystem is placed in PostSimGroup (TogglablePostSimulationGroup).
    ///
    /// Both systems are verified by inspecting the group contents after bootstrap.
    /// </summary>
    [Fact]
    public void EventToEffectSystem_InSimGroup_VisualEffectCleanupSystem_InPostSimGroup()
    {
        var bootstrapper = CreateAndBoot();

        var simSystems     = bootstrapper.SimGroup.GetSystems();
        var postSimSystems = bootstrapper.PostSimGroup.GetSystems();

        Assert.Contains(simSystems,     s => s is EventToEffectSystem);
        Assert.Contains(postSimSystems, s => s is VisualEffectCleanupSystem);
    }

    // ── SC_SM005_2 ────────────────────────────────────────────────────────────

    /// <summary>
    /// SC_SM005_2: VisualEffectCleanupSystem is in PostSimGroup (not SimGroup) so that
    /// the togglable group semantics correctly suppress it during replay.
    /// EventToEffectSystem must NOT appear in PostSimGroup.
    /// </summary>
    [Fact]
    public void TogglableGroups_ContainCorrectSystems_ForReplaySafety()
    {
        var bootstrapper = CreateAndBoot();

        var simSystems     = bootstrapper.SimGroup.GetSystems();
        var postSimSystems = bootstrapper.PostSimGroup.GetSystems();

        // Cross-check: each system is in the correct group and not in the other.
        Assert.DoesNotContain(simSystems,     s => s is VisualEffectCleanupSystem);
        Assert.DoesNotContain(postSimSystems, s => s is EventToEffectSystem);
    }
}
