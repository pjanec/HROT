using Hrot.ClusterRunner.Testing;
using Fdp.Core;

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
internal sealed class MovingEntitySystem : ComponentSystem
{
    private EntityQuery? _query;

    protected override void OnUpdate()
    {
        // Lazily build the query on the first execution so that MovingTestTag is
        // guaranteed to be registered (the test fixture registers it right after
        // SubsystemOrchestrator.Initialize() and before the first tick).
        _query ??= World.Query().With<MovingTestTag>().With<SimTransform>().Build();

        float dt = DeltaTime;
        foreach (var entity in _query)
        {
            float velocityX = World.GetComponent<MovingTestTag>(entity).VelocityX;
            ref var tf = ref World.GetComponentRW<SimTransform>(entity);
            tf.Position.X += velocityX * dt;
        }
    }
}
