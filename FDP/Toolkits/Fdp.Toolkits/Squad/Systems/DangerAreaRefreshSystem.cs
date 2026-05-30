using System;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Squad.DangerArea;

namespace Fdp.Toolkit.Squad.Systems
{
    /// <summary>
    /// Refreshes the <see cref="DangerAreaCognitiveBuffer"/> on a sensor child entity when
    /// the configured refresh interval has elapsed.
    /// </summary>
    public sealed class DangerAreaRefreshSystem
    {
        private readonly IDangerAreaProvider _provider;

        public DangerAreaRefreshSystem(IDangerAreaProvider provider)
        {
            _provider = provider;
        }

        /// <summary>
        /// Refreshes the danger-area buffer on a sensor child entity if the refresh
        /// interval has elapsed.
        /// </summary>
        /// <param name="repo">Entity repository.</param>
        /// <param name="sensorChild">Entity carrying DangerAreaSensor + DangerAreaCognitiveBuffer
        ///   + PartMetadata.</param>
        /// <param name="currentSimTime">Current simulation time in seconds.</param>
        public void Run(EntityRepository repo, Entity sensorChild, float currentSimTime)
        {
            if (!repo.HasComponent<DangerAreaSensor>(sensorChild)) return;
            if (!repo.HasComponent<DangerAreaCognitiveBuffer>(sensorChild)) return;
            if (!repo.HasComponent<PartMetadata>(sensorChild)) return;

            ref var sensor = ref repo.GetComponentRW<DangerAreaSensor>(sensorChild);

            // Check interval: always refresh if interval is zero.
            if (sensor.RefreshIntervalSeconds > 0f &&
                currentSimTime - sensor.LastRefreshSimTime < sensor.RefreshIntervalSeconds)
            {
                return;
            }

            ref readonly var meta = ref repo.GetComponentRO<PartMetadata>(sensorChild);
            var commander = meta.ParentEntity;

            // Refresh into a stack buffer (max 8 descriptors = cap of DangerAreaCognitiveBuffer).
            Span<DangerAreaDescriptor> stackBuf = stackalloc DangerAreaDescriptor[8];
            _provider.Refresh(repo, commander, stackBuf, out int count);

            ref var buffer = ref repo.GetComponentRW<DangerAreaCognitiveBuffer>(sensorChild);
            var dst = buffer.GetSpanRW();
            stackBuf.Slice(0, count).CopyTo(dst);
            buffer.Count = count;

            sensor.Epoch++;
            sensor.LastRefreshSimTime = currentSimTime;
        }
    }
}
