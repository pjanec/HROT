using System;
using System.Numerics;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Examples.NetworkDemo.Systems;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;

namespace Bagira.IG.Tests;

/// <summary>
/// Tests for TASK-IF005: Register TransformSyncSystem.
///
/// Validates that <see cref="TransformSyncSystem"/> constructed with
/// <c>driveFromNetwork: true</c> behaves correctly for ghost (read-only) IG nodes:
/// all entities have their <see cref="SimTransform"/> updated from
/// <see cref="NetworkPosition"/> regardless of local authority.
///
/// SC1 (structural): The system can be constructed with <c>driveFromNetwork = true</c>
/// and registered via <c>_kernel.RegisterGlobalSystem</c>.
/// SC2 (behavioral): After one tick, <see cref="SimTransform"/> reflects the interpolated
/// network position.
/// </summary>
public class TransformSyncSystemRegistrationTests
{
    // ── World factory ─────────────────────────────────────────────────────────

    private static EntityRepository CreateWorld()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<NetworkTransform>();
        repo.RegisterComponent<NetworkAuthority>();
        return repo;
    }

    private static void PlaybackCommands(EntityRepository repo)
    {
        var view = (ISimulationView)repo;
        if (view.GetCommandBuffer() is EntityCommandBuffer ecb)
            ecb.Playback(repo);
    }

    // ── SC1: System construction ───────────────────────────────────────────────

    /// <summary>
    /// SC1 (TASK-IF005): <see cref="TransformSyncSystem"/> must be constructable with
    /// <c>driveFromNetwork: true</c> and must implement <see cref="IModuleSystem"/>
    /// so it can be passed to <c>ModuleHostKernel.RegisterGlobalSystem</c>.
    /// </summary>
    [Fact]
    public void TransformSyncSystem_DriveFromNetwork_ImplementsIModuleSystem()
    {
        var system = new TransformSyncSystem(driveFromNetwork: true);

        Assert.IsAssignableFrom<IModuleSystem>(system);
    }

    // ── SC2: Behavioral interpolation for remote entities ─────────────────────

    /// <summary>
    /// SC2 (TASK-IF005): With <c>driveFromNetwork: true</c>, all entities \u2014 even
    /// those that would normally have local authority \u2014 must have their
    /// <see cref="SimTransform"/> lerped towards <see cref="NetworkPosition"/>.
    ///
    /// This verifies dead-reckoning applies to ghost-node entities.
    /// </summary>
    [Fact]
    public void TransformSyncSystem_DriveFromNetwork_InterpolatesAllEntities()
    {
        // ── Arrange ─────────────────────────────────────────────────────────
        using var repo = CreateWorld();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
        repo.AddComponent(entity, new NetworkTransform { LastPosition = new Vector3(100f, 0f, 0f) });
        // Simulate a "locally-owned" entity (PrimaryOwnerId == LocalNodeId) — with
        // driveFromNetwork: true the system must still update it.
        repo.AddComponent(entity, new NetworkAuthority(IgNetworkConstants.LocalNodeId, IgNetworkConstants.LocalNodeId));

        var system = new TransformSyncSystem(driveFromNetwork: true);

        // ── Act ──────────────────────────────────────────────────────────────
        // Use dt=1.0 / smoothingRate=10 → t=10 → Lerp(0, 100, 10) is clamped to 100 by Vector3.Lerp (t>1 clamps)
        system.Execute(repo, 1.0f);
        PlaybackCommands(repo);

        // ── Assert ───────────────────────────────────────────────────────────
        var tf = repo.GetComponent<SimTransform>(entity);
        // SimTransform.Position.X must have moved from 0 toward 100 (lerp).
        Assert.True(tf.Position.X > 0f,
            $"SimTransform.X must be > 0 after dead-reckoning. Got {tf.Position.X}");
    }

    /// <summary>
    /// With <c>driveFromNetwork: false</c> (default), an entity that is locally
    /// owned must NOT have its <see cref="SimTransform"/> updated by the remote path
    /// (it only copies SimTransform → NetworkPosition instead).
    /// This confirms the driveFromNetwork flag functions as intended.
    /// </summary>
    [Fact]
    public void TransformSyncSystem_DriveFromLocal_OwnedEntityNotUpdatedFromNetwork()
    {
        // ── Arrange ─────────────────────────────────────────────────────────
        using var repo = CreateWorld();

        var entity = repo.CreateEntity();
        var initialPosition = new Vector3(5f, 5f, 5f);
        repo.AddComponent(entity, new SimTransform { Position = initialPosition });
        repo.AddComponent(entity, new NetworkTransform { LastPosition = new Vector3(100f, 100f, 100f) });
        // Locally owned
        repo.AddComponent(entity, new NetworkAuthority(IgNetworkConstants.LocalNodeId, IgNetworkConstants.LocalNodeId));

        var system = new TransformSyncSystem(driveFromNetwork: false);

        // ── Act ──────────────────────────────────────────────────────────────
        system.Execute(repo, 0.1f);
        PlaybackCommands(repo);

        // ── Assert ───────────────────────────────────────────────────────────
        var tf = repo.GetComponent<SimTransform>(entity);
        // The owned path only copies SimTransform → NetworkPosition, so SimTransform stays.
        Assert.Equal(initialPosition, tf.Position);
    }
}
