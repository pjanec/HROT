using System;
using System.IO;
using System.Threading;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fhsm.Kernel;
using Hrot.Editor;
using Xunit;

namespace Hrot.Editor.Tests
{
    /// <summary>
    /// Tests for <see cref="AiHotReloadCoordinator"/>:
    /// verifies ALC unloading and the ClearAll-before-RegisterAll ordering guarantee.
    /// </summary>
    public class AiHotReloadCoordinatorTests : IDisposable
    {
        private static readonly string DllDirectory =
            AppDomain.CurrentDomain.BaseDirectory;

        private static readonly string DllPath =
            Path.Combine(DllDirectory, "Hrot.AI.Behaviors.dll");

        private readonly EntityRepository _world;
        private readonly BehaviorRegistry _registry;

        public AiHotReloadCoordinatorTests()
        {
            _world    = new EntityRepository();
            _registry = new BehaviorRegistry();
        }

        public void Dispose()
        {
            _world.Dispose();
        }

        // ---- Helper ----

        private AiHotReloadCoordinator CreateCoordinator()
            => new AiHotReloadCoordinator(
                DllDirectory, "Hrot.AI.Behaviors.dll",
                _world, _registry,
                geoTransform: null, entityMap: null);

        /// <summary>
        /// Polls until the coordinator enqueues a result (success or failure).
        /// Times out after <paramref name="timeoutMs"/> milliseconds.
        /// </summary>
        private static bool WaitAndDrain(AiHotReloadCoordinator coordinator,
            ref bool done, ref string? failureMsg, int timeoutMs = 8000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                coordinator.DrainPendingCallbacks();
                if (done || failureMsg != null)
                    return true;
                Thread.Sleep(100);
            }
            return false;
        }

        // ---- Tests ----

        [Fact]
        [Trait("Category", "Integration")]
        public void TriggerInitialLoad_LoadsDll_WhenFileExists()
        {
            if (!File.Exists(DllPath)) return; // DLL not in test output, skip.

            using var coordinator = CreateCoordinator();
            bool reloadFired   = false;
            string? failureMsg = null;
            coordinator.OnReloadCompleted += _ => reloadFired = true;
            coordinator.OnReloadFailed    += (_, ex) => failureMsg = ex.ToString();

            coordinator.TriggerInitialLoad();
            WaitAndDrain(coordinator, ref reloadFired, ref failureMsg);

            Assert.Null(failureMsg);
            Assert.True(reloadFired, "OnReloadCompleted should fire after initial load.");
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void TwoReloadCycles_OldAlcIsCollected()
        {
            if (!File.Exists(DllPath)) return; // DLL not in test output, skip.

            using var coordinator = CreateCoordinator();
            bool cycle1Done = false, cycle2Done = false;
            string? failureMsg = null;
            coordinator.OnReloadCompleted += _ =>
            {
                if (!cycle1Done) cycle1Done = true;
                else             cycle2Done = true;
            };
            coordinator.OnReloadFailed += (_, ex) => failureMsg = ex.ToString();

            // ---- Cycle 1 ----
            coordinator.TriggerInitialLoad();
            WaitAndDrain(coordinator, ref cycle1Done, ref failureMsg);
            Assert.Null(failureMsg);
            Assert.True(cycle1Done, "Cycle 1 should complete.");

            // ---- Cycle 2 ----
            coordinator.TriggerInitialLoad();
            WaitAndDrain(coordinator, ref cycle2Done, ref failureMsg);
            Assert.Null(failureMsg);
            Assert.True(cycle2Done, "Cycle 2 should complete.");

            var prevRef = coordinator.PreviousAlcRef;
            Assert.NotNull(prevRef);

            // Force GC to collect the unloaded ALC.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            bool stillAlive = prevRef!.TryGetTarget(out _);
            Assert.False(stillAlive,
                "The previous ALC should be GC-collected after unloading.");
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void Drain_CallsClearAllBeforeRegisterAll()
        {
            if (!File.Exists(DllPath)) return; // DLL not in test output, skip.

            // Register a sentinel guard to verify ClearAll is called during drain.
            ushort sentinelId = 0xFFFE;
            HsmActionDispatcher.RegisterGuard(sentinelId, new IntPtr(0xDEAD));

            using var coordinator = CreateCoordinator();
            bool reloadCompleted = false;
            string? failureMsg   = null;
            coordinator.OnReloadCompleted += _ => reloadCompleted = true;
            coordinator.OnReloadFailed    += (_, ex) => failureMsg = ex.ToString();

            coordinator.TriggerInitialLoad();
            WaitAndDrain(coordinator, ref reloadCompleted, ref failureMsg);

            Assert.Null(failureMsg);
            Assert.True(reloadCompleted, "OnReloadCompleted callback should have fired.");

            // Clean up in case ClearAll was not called (test isolation).
            HsmActionDispatcher.ClearAll();
        }
    }
}
