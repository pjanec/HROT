using System.Numerics;
using Fdp.Examples.NetworkDemo.Systems;
using Fdp.Core;
using Fdp.Modules.Geographic.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// Tests for the ground-clamping Z-offset logic added to
/// <see cref="TransformSyncSystem"/> (MOD1-P7T5).
/// </summary>
public sealed class TransformSyncSystemGroundClampingTests
{
    // ── World factory ─────────────────────────────────────────────────────────

    private static EntityRepository CreateWorld()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<NetworkTransform>();
        repo.RegisterComponent<NetworkAuthority>();
        repo.RegisterComponent<GroundClampingState>();
        return repo;
    }

    private static void PlaybackCommands(EntityRepository repo)
    {
        var view = (ISimulationView)repo;
        if (view.GetCommandBuffer() is EntityCommandBuffer ecb)
            ecb.Playback(repo);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// SC1 (MOD1-P7T5): When <see cref="GroundClampingState"/> is present the system
    /// must lerp <see cref="GroundClampingState.CurrentZOffset"/> toward
    /// <see cref="GroundClampingState.TargetZOffset"/> and apply it to the output Z.
    ///
    /// <c>deltaTime = 1/60 ≈ 0.0167 s</c>; lerp factor = <c>0.0167 × 5 ≈ 0.0833</c>.
    /// So <c>CurrentZOffset</c> moves from 0 toward 2 by ~0.167 — strictly between 0 and 2.
    /// Output Z = <c>netTf.LastPosition.Z + newCurrentOffset</c>.
    /// </summary>
    [Fact]
    public void SyncRemoteEntities_AppliesZOffset_WhenClampingStatePresent()
    {
        using var repo = CreateWorld();
        const float networkZ   = 10f;
        const float targetOffset = 2f;
        const float dt = 1f / 60f;

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform { Position = new Vector3(0f, 0f, networkZ) });
        repo.AddComponent(entity, new NetworkTransform
        {
            LastPosition = new Vector3(0f, 0f, networkZ),
        });
        // Remote entity (PrimaryOwnerId != LocalNodeId)
        repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
        repo.AddComponent(entity, new GroundClampingState
        {
            TargetZOffset       = targetOffset,
            CurrentZOffset      = 0f,
            LastValidIgAltitude = networkZ,
        });

        var system = new TransformSyncSystem();
        system.Execute((ISimulationView)repo, dt);
        PlaybackCommands(repo);

        var resultTf    = repo.GetComponent<SimTransform>(entity);
        var resultClamp = repo.GetComponent<GroundClampingState>(entity);

        // CurrentZOffset must have moved toward TargetZOffset (strictly between 0 and targetOffset)
        Assert.InRange(resultClamp.CurrentZOffset, 0f + float.Epsilon, targetOffset);

        // Output Z = networkZ + CurrentZOffset
        float expectedZ = networkZ + resultClamp.CurrentZOffset;
        Assert.Equal(expectedZ, resultTf.Position.Z, precision: 4);
    }

    /// <summary>
    /// SC2 (MOD1-P7T5): When <see cref="GroundClampingState"/> is <em>absent</em> the
    /// output Z must equal the dead-reckoned (lerped) network Z exactly.
    /// </summary>
    [Fact]
    public void SyncRemoteEntities_DoesNotModifyZ_WithoutClampingState()
    {
        using var repo = CreateWorld();
        const float simZ     = 5f;
        const float networkZ = 20f;
        const float dt       = 0.05f;

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform { Position = new Vector3(0f, 0f, simZ) });
        repo.AddComponent(entity, new NetworkTransform
        {
            LastPosition = new Vector3(0f, 0f, networkZ),
        });
        repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
        // No GroundClampingState added

        var system = new TransformSyncSystem();
        system.Execute((ISimulationView)repo, dt);
        PlaybackCommands(repo);

        var resultTf = repo.GetComponent<SimTransform>(entity);

        // Z should be lerped between simZ and networkZ — NOT equal to networkZ + any offset
        const float smoothingRate = 10f;
        float expectedZ = simZ + (networkZ - simZ) * (dt * smoothingRate);
        Assert.Equal(expectedZ, resultTf.Position.Z, precision: 4);
    }
}
