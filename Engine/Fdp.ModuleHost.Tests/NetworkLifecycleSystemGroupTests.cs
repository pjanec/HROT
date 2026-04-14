using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Xunit;

namespace Fdp.ModuleHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="NetworkLifecycleSystemGroup"/> (CGF1-S0304).
    /// </summary>
    public class NetworkLifecycleSystemGroupTests
    {
        // ── CGF1-S0304 success condition 5 ───────────────────────────────────────

        /// <summary>
        /// When <see cref="NetworkLifecycleSystemGroup.Enabled"/> is <c>false</c>,
        /// calling <see cref="NetworkLifecycleSystemGroup.ExecuteGroup"/> must not
        /// invoke <c>Execute</c> on any of the inner systems — the group is a
        /// complete no-op.
        /// </summary>
        [Fact]
        public void Enabled_False_SkipsAllInnerSystems()
        {
            // Arrange: three counting inner systems.
            var sys1 = new CountingSystem("Sys1");
            var sys2 = new CountingSystem("Sys2");
            var sys3 = new CountingSystem("Sys3");

            var group = new NetworkLifecycleSystemGroup(sys1, sys2, sys3);
            group.Enabled = false;

            using var world = new EntityRepository();
            ISimulationView view = world;

            // Act: run the group five times while disabled.
            for (int i = 0; i < 5; i++)
                group.ExecuteGroup(view, 0.016f);

            // Assert: none of the inner systems were called.
            Assert.Equal(0, sys1.ExecuteCount);
            Assert.Equal(0, sys2.ExecuteCount);
            Assert.Equal(0, sys3.ExecuteCount);
        }

        [Fact]
        public void Enabled_True_ExecutesAllInnerSystems()
        {
            // Verify the positive path too: when enabled all three systems run.
            var sys1 = new CountingSystem("Sys1");
            var sys2 = new CountingSystem("Sys2");
            var sys3 = new CountingSystem("Sys3");

            var group = new NetworkLifecycleSystemGroup(sys1, sys2, sys3);
            // Enabled = true is the default.

            using var world = new EntityRepository();
            ISimulationView view = world;

            group.ExecuteGroup(view, 0.016f);

            Assert.Equal(1, sys1.ExecuteCount);
            Assert.Equal(1, sys2.ExecuteCount);
            Assert.Equal(1, sys3.ExecuteCount);
        }

        [Fact]
        public void Enabled_CanBeToggledAtRuntime()
        {
            var sys = new CountingSystem("Sys");
            var group = new NetworkLifecycleSystemGroup(sys);

            using var world = new EntityRepository();
            ISimulationView view = world;

            // Enabled → executes.
            group.ExecuteGroup(view, 0.016f);
            Assert.Equal(1, sys.ExecuteCount);

            // Disable → skips.
            group.Enabled = false;
            group.ExecuteGroup(view, 0.016f);
            Assert.Equal(1, sys.ExecuteCount); // still 1

            // Re-enable → executes again.
            group.Enabled = true;
            group.ExecuteGroup(view, 0.016f);
            Assert.Equal(2, sys.ExecuteCount);
        }

        // ── Helper ────────────────────────────────────────────────────────────────

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
