using System.Numerics;
using Fdp.Examples.Common.Systems;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// Tests that <see cref="TransformSyncSystem"/> smooths the full authoritative position —
/// including Z — toward the network position, with no separate visual Z correction (P3D-103,
/// 3D Cognitive Spatial Awareness promotion). The former <c>GroundClampingState</c> visual-offset
/// path has been removed.
/// </summary>
public sealed class TransformSyncSystemAltitudeTests
{
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

    /// <summary>
    /// A remote entity whose <see cref="NetworkTransform.LastPosition"/> Z = h smooths its
    /// authoritative <c>SimTransform.Position.Z</c> toward h by the standard lerp — no offset added.
    /// </summary>
    [Fact]
    public void SyncRemoteEntities_SmoothsAuthoritativeZ_TowardNetwork_NoOffset()
    {
        using var repo = CreateWorld();
        const float simZ     = 5f;
        const float networkZ = 20f;
        const float dt       = 0.05f;

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform { Position = new Vector3(0f, 0f, simZ) });
        repo.AddComponent(entity, new NetworkTransform { LastPosition = new Vector3(0f, 0f, networkZ) });
        repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1)); // remote

        var system = new TransformSyncSystem();
        system.Execute((ISimulationView)repo, dt);
        PlaybackCommands(repo);

        var resultTf = repo.GetComponent<SimTransform>(entity);

        // Output Z is the plain lerp between simZ and networkZ; no terrain offset is applied.
        const float smoothingRate = 10f;
        float expectedZ = simZ + (networkZ - simZ) * (dt * smoothingRate);
        Assert.Equal(expectedZ, resultTf.Position.Z, precision: 4);

        // Z is strictly moving toward the network altitude (not forced to 0, not offset past it).
        Assert.InRange(resultTf.Position.Z, simZ, networkZ);
    }

    /// <summary>
    /// Repeated smoothing converges the authoritative Z to the network altitude.
    /// </summary>
    [Fact]
    public void SyncRemoteEntities_ConvergesZ_OverManyFrames()
    {
        using var repo = CreateWorld();
        const float networkZ = 12.5f;
        const float dt       = 1f / 60f;

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform { Position = new Vector3(0f, 0f, 0f) });
        repo.AddComponent(entity, new NetworkTransform { LastPosition = new Vector3(0f, 0f, networkZ) });
        repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));

        var system = new TransformSyncSystem();
        for (int i = 0; i < 240; i++)
        {
            system.Execute((ISimulationView)repo, dt);
            PlaybackCommands(repo);
        }

        var resultTf = repo.GetComponent<SimTransform>(entity);
        Assert.Equal(networkZ, resultTf.Position.Z, precision: 2);
    }
}
