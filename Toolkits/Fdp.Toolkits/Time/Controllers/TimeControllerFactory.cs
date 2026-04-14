using System;
using Fdp.Core;
using Fdp.ModuleHost.Time;

namespace Fdp.Toolkit.Time.Controllers
{
    /// <summary>
    /// Factory for creating time controllers based on configuration.
    /// Handles role/mode combinations: Standalone, Master/Slave x Continuous/Deterministic.
    /// </summary>
    public static class TimeControllerFactory
    {
        /// <summary>
        /// Create a time controller based on configuration.
        /// </summary>
        public static ITimeController Create(
            FdpEventBus eventBus,
            TimeControllerConfig config)
        {
            if (eventBus == null)
                throw new ArgumentNullException(nameof(eventBus));
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            
            return config.Role switch
            {
                TimeRole.Standalone => CreateStandalone(config),
                TimeRole.Master => CreateMaster(eventBus, config),
                TimeRole.Slave => CreateSlave(eventBus, config),
                _ => throw new ArgumentException($"Unknown TimeRole: {config.Role}")
            };
        }
        
        private static ITimeController CreateStandalone(TimeControllerConfig config)
        {
            // Standalone uses MasterSyncController with a private bus (no DDS publishing).
            var controller = new MasterSyncController(
                eventBus: new FdpEventBus(),
                config:   config.SyncConfig
            );
            controller.SetTimeScale(config.InitialTimeScale);
            return controller;
        }
        
        private static ITimeController CreateMaster(
            FdpEventBus eventBus, 
            TimeControllerConfig config)
        {
            return config.Mode switch
            {
                TimeMode.Continuous => CreateContinuousMaster(eventBus, config),
                TimeMode.Deterministic => CreateDeterministicMaster(eventBus, config),
                _ => throw new ArgumentException($"Unknown TimeMode: {config.Mode}")
            };
        }
        
        private static ITimeController CreateSlave(
            FdpEventBus eventBus,
            TimeControllerConfig config)
        {
            return config.Mode switch
            {
                TimeMode.Continuous => CreateContinuousSlave(eventBus, config),
                TimeMode.Deterministic => CreateDeterministicSlave(eventBus, config),
                _ => throw new ArgumentException($"Unknown TimeMode: {config.Mode}")
            };
        }
        
        // Continuous Mode
        
        private static ITimeController CreateContinuousMaster(
            FdpEventBus eventBus,
            TimeControllerConfig config)
        {
            var controller = new MasterSyncController(eventBus, config: config.SyncConfig);
            controller.SetTimeScale(config.InitialTimeScale);
            return controller;
        }
        
        private static ITimeController CreateContinuousSlave(
            FdpEventBus eventBus,
            TimeControllerConfig config)
        {
            return new SlaveSyncController(eventBus, config.LocalNodeId, config.SyncConfig);
        }
        
        // Deterministic Mode
        
        private static ITimeController CreateDeterministicMaster(
            FdpEventBus eventBus,
            TimeControllerConfig config)
        {
            // MasterSyncController unifies Continuous + Deterministic (SteppedMasterController removed).
            var controller = new MasterSyncController(
                eventBus,
                config.AllNodeIds,
                config.SyncConfig
            );
            controller.SetTimeScale(config.InitialTimeScale);
            return controller;
        }
        
        private static ITimeController CreateDeterministicSlave(
            FdpEventBus eventBus,
            TimeControllerConfig config)
        {
            return new SlaveSyncController(eventBus, config.LocalNodeId, config.SyncConfig);
        }
    }
}
