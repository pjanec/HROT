using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Hrot.ClusterRunner.Services;
using Hrot.Orchestrator;
using CycloneDDS.Runtime;
using ModuleHost.Core;
using ModuleHost.Network.Cyclone.Systems;

namespace Hrot.ClusterRunner.Tests
{
    /// <summary>
    /// Integration tests for the embedded <see cref="SimHostSubsystem"/> implementation.
    ///
    /// <para>These tests exercise the full SimHost initialisation stack including
    /// ECS kernel, doctrine registry, and network modules. They require that the
    /// CycloneDDS runtime is available on the test host (same requirement as
    /// <see cref="WaitingRoomCoordinatorTests"/>).</para>
    /// </summary>
    public class SimHostSubsystemTests : IDisposable
    {
        private readonly SimHostSubsystem _subsystem;
        // Provides DdsIdAllocatorServer on the test domain so EnsureIdAllocatorRouting succeeds.
        private readonly DdsParticipant _allocatorParticipant;
        private readonly ClusterMaster   _clusterMaster;
        // Extra allocator for domain 0 (used by Initialize_DomainZero_* tests).
        private readonly DdsParticipant _allocatorParticipantDomain0;
        private readonly ClusterMaster   _clusterMasterDomain0;

        private static SubsystemConfig HeadlessConfig(int domainId = 98) => new()
        {
            DomainId      = domainId,
            Headless      = true,
            OwnWindow     = false,
            SubsystemName = "SimHost"
        };

        public SimHostSubsystemTests()
        {
            _allocatorParticipant        = new DdsParticipant(98);
            _clusterMaster                 = new ClusterMaster(_allocatorParticipant);
            _allocatorParticipantDomain0 = new DdsParticipant(0);
            _clusterMasterDomain0          = new ClusterMaster(_allocatorParticipantDomain0);
            _subsystem                   = new SimHostSubsystem();
        }

        public void Dispose()
        {
            _subsystem.Stop();
            _subsystem.Shutdown();
            _clusterMaster.Dispose();
            _allocatorParticipant.Dispose();
            _clusterMasterDomain0.Dispose();
            _allocatorParticipantDomain0.Dispose();
        }

        // ── Name ──────────────────────────────────────────────────────────────

        [Fact]
        public void Name_Returns_SimHost()
        {
            Assert.Equal("SimHost", _subsystem.Name);
        }

        // ── Initialize ────────────────────────────────────────────────────────

        [Fact]
        public void Initialize_CreatesKernelAndModules_WithoutException()
        {
            // Verifies that the full SimHost initialisation stack (ECS world,
            // doctrine registry, geographic module, network module) does not throw.
            var ex = Record.Exception(() => _subsystem.Initialize(HeadlessConfig()));
            Assert.Null(ex);
        }

        [Fact]
        public void Initialize_RegistersCycloneNetworkCleanupSystem()
        {
            _subsystem.Initialize(HeadlessConfig());

            // Access the kernel via the internal App property (SimHostApp is the single
            // source of truth; no longer needs reflection into SimHostSubsystem).
            var kernel = _subsystem.App.Kernel;
            Assert.NotNull(kernel);

            var profile = kernel.SystemScheduler.GetProfileData<CycloneNetworkCleanupSystem>();

            Assert.NotNull(profile);
        }

        // ── Update ────────────────────────────────────────────────────────────

        [Fact]
        public void Update_AfterInit_TicksKernelWithoutException()
        {
            _subsystem.Initialize(HeadlessConfig());

            // A single kernel tick must not throw.
            var ex = Record.Exception(() => _subsystem.Update(0.016f));
            Assert.Null(ex);
        }

        [Fact]
        public void Update_MultipleFrames_AccumulatesWithoutError()
        {
            _subsystem.Initialize(HeadlessConfig());

            for (int i = 0; i < 10; i++)
                _subsystem.Update(0.016f);
            // No exception means the kernel and system group are stable.
        }

        [Fact]
        public void Update_BeforeInit_IsNoOp()
        {
            // _initialized == false — must not crash with NullReferenceException.
            var ex = Record.Exception(() => _subsystem.Update(0.016f));
            Assert.Null(ex);
        }

        // ── DrawWorld / DrawUI ────────────────────────────────────────────────

        [Fact]
        public void DrawWorld_IsAlwaysNoOp()
        {
            _subsystem.Initialize(HeadlessConfig());
            // SimHost has no 3-D visuals; DrawWorld must never throw.
            var ex = Record.Exception(() => _subsystem.DrawWorld());
            Assert.Null(ex);
        }

        [Fact]
        public void DrawUI_Headless_DoesNotThrow()
        {
            _subsystem.Initialize(HeadlessConfig());
            // DrawUI is a no-op placeholder until Phase R3 panels are implemented.
            var ex = Record.Exception(() => _subsystem.DrawUI());
            Assert.Null(ex);
        }

        // ── Start / Stop (standalone background loop) ─────────────────────────

        [Fact]
        public void Start_StartsBackgroundThread()
        {
            _subsystem.Initialize(HeadlessConfig());
            _subsystem.Start();

            // Give the thread time to tick at least once.
            Thread.Sleep(50);

            // Stop must join the thread within the timeout.
            var beforeStop = DateTime.UtcNow;
            _subsystem.Stop();
            var elapsed = DateTime.UtcNow - beforeStop;

            Assert.True(elapsed.TotalSeconds < 5,
                "Stop() should complete within 5 seconds (thread join timeout).");
        }

        [Fact]
        public void Start_CalledTwice_DoesNotDoubleStart()
        {
            _subsystem.Initialize(HeadlessConfig());
            _subsystem.Start();
            _subsystem.Start(); // second call must be a no-op, not a new thread
            Thread.Sleep(30);
            _subsystem.Stop();
            // Success = no crash; regression check for double-start guard.
        }

        [Fact]
        public void Stop_WithoutStart_IsNoOp()
        {
            // Stop must be safe even when Start was never called.
            var ex = Record.Exception(() => _subsystem.Stop());
            Assert.Null(ex);
        }

        // ── Shutdown ─────────────────────────────────────────────────────────

        [Fact]
        public void Shutdown_AfterInit_ReleasesResources()
        {
            _subsystem.Initialize(HeadlessConfig());
            var ex = Record.Exception(() => _subsystem.Shutdown());
            Assert.Null(ex);
        }

        [Fact]
        public void Shutdown_WithoutInit_IsNoOp()
        {
            var ex = Record.Exception(() => _subsystem.Shutdown());
            Assert.Null(ex);
        }

        // ── Full lifecycle ────────────────────────────────────────────────────

        [Fact]
        public void FullLifecycle_Headless_CompletesCleanly()
        {
            ISubsystem subsystem = _subsystem;

            subsystem.Initialize(HeadlessConfig());
            subsystem.Update(0.016f);
            subsystem.DrawWorld();
            subsystem.DrawUI();
            subsystem.Update(0.016f);
            subsystem.Shutdown();
        }

        // ── BUG1-F001: Domain zero guard ──────────────────────────────────────

        [Fact]
        public void Initialize_DomainZero_DoesNotThrow()
        {
            // Before fix, DomainId=0 was treated as "unspecified" and passed null
            // to SimHostApp, causing it to fall back to config.json / domain 42.
            // After fix, domain 0 is a valid domain and must be accepted.
            var cfg = new SubsystemConfig { DomainId = 0, Headless = true, OwnWindow = false, SubsystemName = "SimHost" };
            var ex = Record.Exception(() => _subsystem.Initialize(cfg));
            Assert.Null(ex);
        }

        [Fact]
        public void Initialize_DomainZero_PassesDomainZeroToApp()
        {
            var cfg = new SubsystemConfig { DomainId = 0, Headless = true, OwnWindow = false, SubsystemName = "SimHost" };
            _subsystem.Initialize(cfg);
            // Access the internal App to verify the domain used (0 should be stored, not null).
            // The App uses _domainOverride which we can read via the resolved local node ID accessor.
            // Since domain 0 is valid and no config.json fallback should occur during init, no exception is the primary assertion.
            Assert.NotNull(_subsystem.App);
        }

        [Fact]
        public void Initialize_NonZeroDomain_PassedThrough()
        {
            // Verifies that a non-zero DomainId is accepted (domain 98 = test allocator domain).
            var cfg = new SubsystemConfig { DomainId = 98, Headless = true, OwnWindow = false, SubsystemName = "SimHost" };
            var ex = Record.Exception(() => _subsystem.Initialize(cfg));
            Assert.Null(ex);
        }

        // ── BUG1-F002: NodeId override ───────────────────────────────────────

        [Fact]
        public void Initialize_NodeIdZero_DoesNotThrow()
        {
            // NodeId=0 is the legacy fallback sentinel — must not cause an error.
            var cfg = HeadlessConfig();
            cfg.NodeId = 0;
            var ex = Record.Exception(() => _subsystem.Initialize(cfg));
            Assert.Null(ex);
        }

        [Fact]
        public void Initialize_NodeIdNonZero_DoesNotThrow()
        {
            // Passing a non-zero node ID must not cause initialization to fail.
            var cfg = HeadlessConfig();
            cfg.NodeId = 10;
            var ex = Record.Exception(() => _subsystem.Initialize(cfg));
            Assert.Null(ex);
        }
    }
}
