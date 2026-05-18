using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Xunit;

namespace Fdp.ModuleHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="TogglableSimulationGroup"/>, <see cref="TogglableInputGroup"/>,
    /// and <see cref="TogglablePostSimulationGroup"/>.
    /// </summary>
    public class TogglableGroupTests
    {
        // ── TogglableSimulationGroup ──────────────────────────────────────────

        [Fact]
        public void TogglableSimulationGroup_WhenEnabled_ExecutesAllInnerSystems()
        {
            var sys1 = new CountingSystem("Sys1");
            var sys2 = new CountingSystem("Sys2");
            var group = new TogglableSimulationGroup("sim", sys1, sys2);

            using var world = new EntityRepository();
            ISimulationView view = world;

            group.Execute(view, 0.016f);

            Assert.Equal(1, sys1.ExecuteCount);
            Assert.Equal(1, sys2.ExecuteCount);
        }

        [Fact]
        public void TogglableSimulationGroup_WhenDisabled_SkipsAllInnerSystems()
        {
            var sys1 = new CountingSystem("Sys1");
            var sys2 = new CountingSystem("Sys2");
            var group = new TogglableSimulationGroup("sim", sys1, sys2);
            group.Enabled = false;

            using var world = new EntityRepository();
            ISimulationView view = world;

            for (int i = 0; i < 5; i++)
                group.Execute(view, 0.016f);

            Assert.Equal(0, sys1.ExecuteCount);
            Assert.Equal(0, sys2.ExecuteCount);
        }

        [Fact]
        public void TogglableSimulationGroup_GetSystems_ReturnsAllInnerSystems()
        {
            var sys1 = new CountingSystem("Sys1");
            var sys2 = new CountingSystem("Sys2");
            var sys3 = new CountingSystem("Sys3");
            var group = new TogglableSimulationGroup("sim", sys1, sys2, sys3);

            IReadOnlyList<IEcsModuleSystem> systems = group.GetSystems();

            Assert.Equal(3, systems.Count);
            Assert.Same(sys1, systems[0]);
            Assert.Same(sys2, systems[1]);
            Assert.Same(sys3, systems[2]);
        }

        // ── TogglableInputGroup ───────────────────────────────────────────────

        [Fact]
        public void TogglableInputGroup_WhenEnabled_ExecutesAllInnerSystems()
        {
            var sys1 = new CountingSystem("Sys1");
            var sys2 = new CountingSystem("Sys2");
            var group = new TogglableInputGroup("input", sys1, sys2);

            using var world = new EntityRepository();
            ISimulationView view = world;

            group.Execute(view, 0.016f);

            Assert.Equal(1, sys1.ExecuteCount);
            Assert.Equal(1, sys2.ExecuteCount);
        }

        [Fact]
        public void TogglableInputGroup_WhenDisabled_SkipsAllInnerSystems()
        {
            var sys1 = new CountingSystem("Sys1");
            var sys2 = new CountingSystem("Sys2");
            var group = new TogglableInputGroup("input", sys1, sys2);
            group.Enabled = false;

            using var world = new EntityRepository();
            ISimulationView view = world;

            for (int i = 0; i < 5; i++)
                group.Execute(view, 0.016f);

            Assert.Equal(0, sys1.ExecuteCount);
            Assert.Equal(0, sys2.ExecuteCount);
        }

        [Fact]
        public void TogglableInputGroup_GetSystems_ReturnsAllInnerSystems()
        {
            var sys1 = new CountingSystem("Sys1");
            var sys2 = new CountingSystem("Sys2");
            var sys3 = new CountingSystem("Sys3");
            var group = new TogglableInputGroup("input", sys1, sys2, sys3);

            IReadOnlyList<IEcsModuleSystem> systems = group.GetSystems();

            Assert.Equal(3, systems.Count);
            Assert.Same(sys1, systems[0]);
            Assert.Same(sys2, systems[1]);
            Assert.Same(sys3, systems[2]);
        }

        // ── TogglablePostSimulationGroup ──────────────────────────────────────

        [Fact]
        public void TogglablePostSimulationGroup_WhenEnabled_ExecutesAllInnerSystems()
        {
            var sys1 = new CountingSystem("Sys1");
            var sys2 = new CountingSystem("Sys2");
            var group = new TogglablePostSimulationGroup("postSim", sys1, sys2);

            using var world = new EntityRepository();
            ISimulationView view = world;

            group.Execute(view, 0.016f);

            Assert.Equal(1, sys1.ExecuteCount);
            Assert.Equal(1, sys2.ExecuteCount);
        }

        [Fact]
        public void TogglablePostSimulationGroup_WhenDisabled_SkipsAllInnerSystems()
        {
            var sys1 = new CountingSystem("Sys1");
            var sys2 = new CountingSystem("Sys2");
            var group = new TogglablePostSimulationGroup("postSim", sys1, sys2);
            group.Enabled = false;

            using var world = new EntityRepository();
            ISimulationView view = world;

            for (int i = 0; i < 5; i++)
                group.Execute(view, 0.016f);

            Assert.Equal(0, sys1.ExecuteCount);
            Assert.Equal(0, sys2.ExecuteCount);
        }

        [Fact]
        public void TogglablePostSimulationGroup_GetSystems_ReturnsAllInnerSystems()
        {
            var sys1 = new CountingSystem("Sys1");
            var sys2 = new CountingSystem("Sys2");
            var sys3 = new CountingSystem("Sys3");
            var group = new TogglablePostSimulationGroup("postSim", sys1, sys2, sys3);

            IReadOnlyList<IEcsModuleSystem> systems = group.GetSystems();

            Assert.Equal(3, systems.Count);
            Assert.Same(sys1, systems[0]);
            Assert.Same(sys2, systems[1]);
            Assert.Same(sys3, systems[2]);
        }

        // ── Helper ────────────────────────────────────────────────────────────

        private sealed class CountingSystem : IEcsModuleSystem
        {
            public string Name { get; }
            public int ExecuteCount { get; private set; }

            public CountingSystem(string name) => Name = name;

            public void Execute(ISimulationView view, float deltaTime)
                => ExecuteCount++;
        }
    }
}
