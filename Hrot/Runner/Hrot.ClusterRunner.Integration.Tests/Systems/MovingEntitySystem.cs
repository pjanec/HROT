using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.ClusterRunner.Testing;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Test-only ECS system that advances the X position of every entity tagged with
/// <see cref="MovingTestTag"/> by <c>VelocityX * DeltaTime</c> each tick.
///
/// <para>⭐ <c>QA-018</c>: registered exclusively by E2E test fixtures
/// (<c>ClusterOpE2eScriptTests</c>) via <c>SimHostSubsystem.TestHook_QueueSystem</c>, which runs
/// <b>before</b> <c>SubsystemOrchestrator.Initialize()</c> — the kernel refuses global systems
/// afterwards. Never wired into production boots.</para>
///
/// <para>The caller must also call
/// <c>world.RegisterComponent&lt;MovingTestTag&gt;()</c> before the first simulation
/// tick so the query builder finds the component table.</para>
///
/// <para>⭐⭐ <c>QA-018</c> — <b><c>[UpdateInPhase]</c> is REQUIRED and was missing.</b>
/// <c>ModuleHostKernel.RegisterGlobalSystem</c> throws
/// <c>"System MovingEntitySystem must have [UpdateInPhase] attribute"</c> without it. ⚠ That
/// validation sits AFTER the <c>_initialized</c> guard, so while the old post-Initialize hook was in
/// use this second defect was invisible — fixing the first one is what surfaced it.
/// ⛔ <c>SystemPhase.Simulation</c> would be wrong: the kernel runs that phase for MODULE systems on
/// background threads only, and a global system marked with it would silently never execute.
/// ⭐ <c>PostSimulation</c> is the position-advancing slot — after the sim, before egress reads it.</para>
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
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
