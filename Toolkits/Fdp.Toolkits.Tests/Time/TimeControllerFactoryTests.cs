using System;
using System.Collections.Generic;
using Fdp.ModuleHost.Core.Time;
using Fdp.Kernel;
using Xunit;

using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Time.Messages;

namespace FDP.Toolkit.Time.Tests
{
    public class TimeControllerFactoryTests
    {
        [Fact]
        public void Create_Standalone_ReturnsMasterController_LocalClock()
        {
            var config = new TimeControllerConfig
            {
                Role = TimeRole.Standalone,
                Mode = TimeMode.Continuous
            };

            var controller = TimeControllerFactory.Create(new FdpEventBus(), config);

            Assert.IsType<MasterSyncController>(controller);
            Assert.Equal(TimeMode.Continuous, controller.GetMode());
        }

        [Fact]
        public void Create_ContinuousMaster_ReturnsMasterController()
        {
            var config = new TimeControllerConfig
            {
                Role = TimeRole.Master,
                Mode = TimeMode.Continuous
            };

            var controller = TimeControllerFactory.Create(new FdpEventBus(), config);

            Assert.IsType<MasterSyncController>(controller);
        }

        [Fact]
        public void Create_ContinuousSlave_ReturnsSlaveController()
        {
            var config = new TimeControllerConfig
            {
                Role = TimeRole.Slave,
                Mode = TimeMode.Continuous
            };

            var controller = TimeControllerFactory.Create(new FdpEventBus(), config);

            Assert.IsType<SlaveSyncController>(controller);
        }

        [Fact]
        public void Create_DeterministicMaster_ReturnsMasterSyncController()
        {
            var config = new TimeControllerConfig
            {
                Role = TimeRole.Master,
                Mode = TimeMode.Deterministic,
                AllNodeIds = new HashSet<int> { 1, 2 }
            };

            var controller = TimeControllerFactory.Create(new FdpEventBus(), config);

            Assert.IsType<MasterSyncController>(controller);
        }

        [Fact]
        public void Create_DeterministicSlave_ReturnsSteppedSlave()
        {
            var config = new TimeControllerConfig
            {
                Role = TimeRole.Slave,
                Mode = TimeMode.Deterministic,
                LocalNodeId = 1
            };

            var controller = TimeControllerFactory.Create(new FdpEventBus(), config);

            Assert.IsType<SlaveSyncController>(controller);
        }

        // ── TCU-T004: Factory tests for updated role/mode routes ─────────────

        [Fact]
        public void TimeControllerFactory_Master_Continuous_ReturnsMasterSyncController()
        {
            var config = new TimeControllerConfig
            {
                Role = TimeRole.Master,
                Mode = TimeMode.Continuous,
            };

            var controller = TimeControllerFactory.Create(new FdpEventBus(), config);

            Assert.IsType<MasterSyncController>(controller);
        }

        [Fact]
        public void TimeControllerFactory_Slave_Continuous_ReturnsSlaveSyncController()
        {
            var config = new TimeControllerConfig
            {
                Role = TimeRole.Slave,
                Mode = TimeMode.Continuous,
                LocalNodeId = 5,
            };

            var controller = TimeControllerFactory.Create(new FdpEventBus(), config);

            Assert.IsType<SlaveSyncController>(controller);
        }

        [Fact]
        public void TimeControllerFactory_Slave_Deterministic_ReturnsSlaveSyncController()
        {
            var config = new TimeControllerConfig
            {
                Role = TimeRole.Slave,
                Mode = TimeMode.Deterministic,
                LocalNodeId = 5,
            };

            var controller = TimeControllerFactory.Create(new FdpEventBus(), config);

            Assert.IsType<SlaveSyncController>(controller);
        }

        [Fact]
        public void TimeControllerFactory_Standalone_ReturnsUnchangedType()
        {
            var config = new TimeControllerConfig
            {
                Role = TimeRole.Standalone,
                Mode = TimeMode.Continuous,
            };

            var controller = TimeControllerFactory.Create(new FdpEventBus(), config);

            Assert.IsType<MasterSyncController>(controller);
        }
    }
}
