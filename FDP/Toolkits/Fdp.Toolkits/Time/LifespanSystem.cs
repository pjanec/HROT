using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Time
{
    /// <summary>
    /// ⭐ W1 (docs/DESIGN_Visual_Effects.md §7 VE-Q, R-269 (ui)) — <b>the one collector for timed entities:</b> destroys every entity whose
    /// <see cref="Lifespan"/> is over by SIMULATION time (<see cref="SimClock"/>), or that a jump back put before its own birth.
    /// <para>⛔ OFF during a replay (<see cref="IsReplayActive"/>, VE-R): the recording already holds when each timed entity went, and
    /// the replay restores it — removing it here as well would fight the playback.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class LifespanSystem : IEcsModuleSystem
    {
        private EntityRepository? _queryWorld;
        private EntityQuery? _query;

        /// <summary>True while this node's world is driven by a replay; null ⇒ never (a host that does not replay its own world).</summary>
        public Func<bool>? IsReplayActive { get; set; }

        /// <summary>Timed entities still alive after the last tick — the rail's counter.</summary>
        public int Alive { get; private set; }

        /// <summary>Removed in the last tick — the rail's counter.</summary>
        public int Removed { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            Alive = 0;
            Removed = 0;
            if (view is not EntityRepository world || !world.IsComponentTypeRegistered<Lifespan>()) return;
            if (IsReplayActive?.Invoke() == true) return;
            if (!ReferenceEquals(world, _queryWorld))
            {
                _queryWorld = world;
                _query = world.Query().With<Lifespan>().Build();
            }
            double now = SimClock.Of(world).TotalTime;
            var cmd = view.GetCommandBuffer();
            foreach (var e in _query!)
            {
                if (world.GetComponentRO<Lifespan>(e).IsOver(now))
                {
                    cmd.DestroyEntity(e);
                    Removed++;
                }
                else Alive++;
            }
        }
    }
}
