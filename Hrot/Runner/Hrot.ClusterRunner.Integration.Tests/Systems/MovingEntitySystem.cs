using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.ClusterRunner.Testing;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Test-only ECS system that advances the X position of every entity tagged with
/// <see cref="MovingTestTag"/> by <c>VelocityX * DeltaTime</c> each tick.
///
/// <para>Registered exclusively by E2E test fixtures (<c>ClusterOpE2eScriptTests</c>) after
/// <c>SubsystemOrchestrator.Initialize()</c> via
/// <c>SimHostSubsystem.TestHook_AddSystem</c>. Never wired into production boots.</para>
///
/// <para>The caller must also call
/// <c>world.RegisterComponent&lt;MovingTestTag&gt;()</c> before the first simulation
/// tick so the query builder finds the component table.</para>
/// </summary>
internal sealed class MovingEntitySystem : IEcsModuleSystem
{
    private EntityQuery? _query;

    public void Execute(ISimulationView view, float deltaTime)
    {
        var repo = (EntityRepository)view;

        // Lazily build the query on the first execution so that MovingTestTag is
        // guaranteed to be registered (the test fixture registers it right after
        // SubsystemOrchestrator.Initialize() and before the first tick).
        _query ??= repo.Query().With<MovingTestTag>().With<SimTransform>().Build();

        foreach (var entity in _query)
        {
            float velocityX = repo.GetComponent<MovingTestTag>(entity).VelocityX;
            ref var tf = ref repo.GetComponentRW<SimTransform>(entity);
            tf.Position.X += velocityX * deltaTime;
        }
    }
}
