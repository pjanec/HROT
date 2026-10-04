using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Events;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// ⭐ <c>CE-2076</c> — records every <see cref="SensorChangedEvent"/> into the unit's <see cref="RecentSenses"/>
    /// (added on first use), stamped with <see cref="GlobalTime.TotalTime"/>. Runs before the brain tick so a condition sees
    /// last frame's changes. The event's producers are not touched.
    /// </summary>
    public sealed class RecentSensesSystem : IEcsModuleSystem
    {
        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.IsComponentTypeRegistered<RecentSenses>()) return;
            double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : 0d;

            foreach (var evt in repo.Bus.Read<SensorChangedEvent>())
            {
                if (!repo.IsAlive(evt.Unit)) continue;
                if (!repo.HasComponent<RecentSenses>(evt.Unit)) repo.AddComponent(evt.Unit, new RecentSenses());
                repo.GetComponentRW<RecentSenses>(evt.Unit).Record(evt.What, now);
            }
        }
    }
}
