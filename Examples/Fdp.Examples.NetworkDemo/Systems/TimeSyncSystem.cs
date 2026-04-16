using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Time.Messages;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Toolkit.Replication;
using Fdp.ModuleHost.Time; // For casts

namespace Fdp.Examples.NetworkDemo.Systems
{
    [UpdateInPhase(SystemPhase.Input)]
    public class TimeSyncSystem : IEcsModuleSystem
    {
        private readonly FdpEventBus _bus;
        private readonly bool _isMaster;
        
        public TimeSyncSystem(FdpEventBus bus, bool isMaster)
        {
            _bus = bus;
            _isMaster = isMaster;
            _bus.Register<SwitchTimeModeEvent>();
        }

        public void Execute(ISimulationView view, float dt)
        {
            if (_isMaster)
            {
                ExecuteMaster(view);
            }
            else
            {
                ExecuteSlave(view);
            }
        }

        private void ExecuteMaster(ISimulationView view)
        {
            var cmd = view.GetCommandBuffer();
            foreach (var evt in _bus.Read<SwitchTimeModeEvent>())
            {
                var query = view.Query().With<TimeModeComponent>().Build();
                foreach (var entity in query)
                {
                    cmd.SetComponent(entity, new TimeModeComponent
                    {
                        TargetMode = (int)evt.TargetMode,
                        BarrierWallTicks = evt.BarrierWallTicks,
                        FixedDelta = evt.FixedDelta
                    });
                     // Singleton, break after first
                     break; 
                }
            }
        }

        private long _lastProcessedBarrier = -1;

        private void ExecuteSlave(ISimulationView view)
        {
            var query = view.Query().With<TimeModeComponent>().Build();
            foreach (var entity in query)
            {
                ref readonly var comp = ref view.GetComponentRO<TimeModeComponent>(entity);
                
                if (comp.BarrierWallTicks > _lastProcessedBarrier && comp.BarrierWallTicks > 0)
                {
                     _lastProcessedBarrier = comp.BarrierWallTicks;
                     
                     var evt = new SwitchTimeModeEvent
                     {
                        TargetMode = (TimeMode)comp.TargetMode,
                        BarrierWallTicks = comp.BarrierWallTicks,
                        FixedDelta = comp.FixedDelta
                     };
                     
                     _bus.Publish(evt);
                }
            }
        }
    }
}
