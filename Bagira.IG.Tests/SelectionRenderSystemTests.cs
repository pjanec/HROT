using System.Numerics;
using Bagira.IG.Components;
using Bagira.IG.Systems;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D.Abstractions;
using FDP.Toolkit.Vis2D.Components;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace Bagira.IG.Tests;

/// <summary>
/// Unit tests for <see cref="SelectionRenderSystem"/> layer-visibility enforcement (BUG2-V001).
/// Uses <see cref="SelectionRenderSystem.TestHook_SkipRaylibCalls"/> so Raylib is never invoked.
/// </summary>
public class SelectionRenderSystemTests
{
    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SelectionState>();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<MapDisplayComponent>();
        return repo;
    }

    [Fact]
    public void Draw_HiddenLayerEntity_DoesNotRenderRing()
    {
        var repo  = CreateRepo();
        var query = repo.Query().With<SelectionState>().With<SimTransform>().Build();
        var sys   = new SelectionRenderSystem(repo, query) { TestHook_SkipRaylibCalls = true };

        // Entity on layer 0 (mask=0x1), selected, but layer 0 is hidden.
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SelectionState { IsSelected = true, IsPrimarySelection = true });
        repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
        repo.SetComponent(entity, new MapDisplayComponent { LayerMask = 0x1u });

        // VisibleLayersMask = 0x2 → layer 0 hidden.
        sys.Draw(new RenderContext { VisibleLayersMask = 0x2u });

        Assert.Equal(0, sys.TestHook_RingDrawCount);
    }

    [Fact]
    public void Draw_VisibleLayerEntity_RendersRing()
    {
        var repo  = CreateRepo();
        var query = repo.Query().With<SelectionState>().With<SimTransform>().Build();
        var sys   = new SelectionRenderSystem(repo, query) { TestHook_SkipRaylibCalls = true };

        // Entity on layer 0 (mask=0x1), selected; layer 0 is visible.
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SelectionState { IsSelected = true, IsPrimarySelection = true });
        repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
        repo.SetComponent(entity, new MapDisplayComponent { LayerMask = 0x1u });

        // VisibleLayersMask = 0x1 → layer 0 visible.
        sys.Draw(new RenderContext { VisibleLayersMask = 0x1u });

        Assert.Equal(1, sys.TestHook_RingDrawCount);
    }
}
